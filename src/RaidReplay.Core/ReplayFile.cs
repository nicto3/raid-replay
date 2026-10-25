using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RaidReplay.Core
{
    /// <summary>
    /// File layout:  "RRPL" | u16 version | header | chunk*
    /// chunk = u32 startMs | u32 endMs | u32 rawLen | u32 compLen | deflate(payload)
    /// Chunks are independent, so a crash loses at most the last few seconds.
    /// </summary>
    public static class ReplayFile
    {
        public const ushort CurrentVersion = 1;
        public const string Extension = ".rrpl";
        internal static readonly byte[] Magic = { (byte)'R', (byte)'R', (byte)'P', (byte)'L' };

        internal static void WriteHeader(BinaryWriter w, ReplayHeader h)
        {
            w.Write(Magic);
            w.Write(h.Version);
            w.Write(h.Map ?? ""); w.Write(h.StartedUtcTicks);
            w.Write(h.PovProfileId ?? ""); w.Write(h.PovNickname ?? ""); w.Write(h.GameInfo ?? "");
            w.Write(h.OthersHz); w.Write(h.PovHz);
        }

        internal static ReplayHeader ReadHeader(BinaryReader r)
        {
            var magic = r.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("not a RaidReplay file");
            var h = new ReplayHeader { Version = r.ReadUInt16() };
            if (h.Version > CurrentVersion) throw new InvalidDataException("replay is from a newer mod version");
            h.Map = r.ReadString(); h.StartedUtcTicks = r.ReadInt64();
            h.PovProfileId = r.ReadString(); h.PovNickname = r.ReadString(); h.GameInfo = r.ReadString();
            h.OthersHz = r.ReadInt32(); h.PovHz = r.ReadInt32();
            return h;
        }

        /// <summary>Cheap metadata read (header + chunk headers only, no decompression).</summary>
        public static ReplayInfo ReadInfo(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(fs, Encoding.UTF8);
            var info = new ReplayInfo { Path = path, SizeBytes = fs.Length, Header = ReadHeader(r) };
            try
            {
                int maxEnd = 0;
                while (fs.Position + 16 <= fs.Length)
                {
                    r.ReadUInt32(); uint end = r.ReadUInt32(); r.ReadUInt32(); uint comp = r.ReadUInt32();
                    if (fs.Position + comp > fs.Length) break;
                    fs.Seek(comp, SeekOrigin.Current);
                    if (end > maxEnd) maxEnd = (int)end;
                }
                info.DurationMs = maxEnd;
            }
            catch (EndOfStreamException) { }
            return info;
        }
    }

    public sealed class ReplayInfo
    {
        public string Path;
        public string FileName => System.IO.Path.GetFileName(Path);
        public long SizeBytes;
        public int DurationMs;
        public bool Pinned;
        public ReplayHeader Header;
    }

    public sealed class Replay
    {
        public ReplayHeader Header;
        public List<EntityInfo> Entities = new List<EntityInfo>();
        public Dictionary<int, Sample[]> Tracks = new Dictionary<int, Sample[]>();
        public List<ReplayEvent> Events = new List<ReplayEvent>();
        public int DurationMs;
        public bool Truncated;

        public EntityInfo FindPov()
        {
            foreach (var e in Entities) if (e.IsPov) return e;
            return null;
        }

        public static Replay Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(fs, Encoding.UTF8);
            var rep = new Replay { Header = ReplayFile.ReadHeader(r) };
            var lists = new Dictionary<int, List<Sample>>();
            try
            {
                while (fs.Position < fs.Length)
                {
                    if (fs.Length - fs.Position < 16) { rep.Truncated = true; break; }
                    uint start = r.ReadUInt32(), end = r.ReadUInt32(), rawLen = r.ReadUInt32(), compLen = r.ReadUInt32();
                    if (compLen > fs.Length - fs.Position || rawLen > 256 * 1024 * 1024) { rep.Truncated = true; break; }
                    var comp = r.ReadBytes((int)compLen);
                    var raw = new byte[rawLen];
                    using (var ds = new DeflateStream(new MemoryStream(comp), CompressionMode.Decompress))
                    {
                        int off = 0;
                        while (off < raw.Length) { int n = ds.Read(raw, off, raw.Length - off); if (n <= 0) throw new EndOfStreamException(); off += n; }
                    }
                    ParseChunk(raw, (int)start, rep, lists);
                    if ((int)end > rep.DurationMs) rep.DurationMs = (int)end;
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is InvalidDataException || e is IOException)
            {
                rep.Truncated = true;
            }
            foreach (var kv in lists) rep.Tracks[kv.Key] = kv.Value.ToArray();
            rep.Events.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return rep;
        }

        static void ParseChunk(byte[] raw, int chunkStart, Replay rep, Dictionary<int, List<Sample>> lists)
        {
            using var r = new BinaryReader(new MemoryStream(raw), Encoding.UTF8);
            uint defs = r.ReadVar();
            for (uint i = 0; i < defs; i++)
            {
                var e = new EntityInfo
                {
                    Index = (int)r.ReadVar(), ProfileId = r.ReadString(), Nickname = r.ReadString(),
                    Side = (EntitySide)r.ReadByte()
                };
                byte f = r.ReadByte(); e.IsAI = (f & 1) != 0; e.IsPov = (f & 2) != 0;
                e.Level = (int)r.ReadVar(); e.Role = r.ReadString();
                e.Blob = r.ReadBytes((int)r.ReadVar());
                while (rep.Entities.Count <= e.Index) rep.Entities.Add(null);
                rep.Entities[e.Index] = e;
            }
            uint evs = r.ReadVar();
            for (uint i = 0; i < evs; i++)
            {
                var ev = new ReplayEvent
                {
                    TimeMs = chunkStart + (int)r.ReadVar(), Type = (EventKind)r.ReadByte(),
                    Actor = (int)r.ReadVar() - 1, Target = (int)r.ReadVar() - 1,
                    X = Codec.QToPos(r.ReadZig()), Y = Codec.QToPos(r.ReadZig()), Z = Codec.QToPos(r.ReadZig()),
                    Arg = r.ReadSingle(), Arg2 = r.ReadSingle()
                };
                rep.Events.Add(ev);
            }
            uint tracks = r.ReadVar();
            for (uint t = 0; t < tracks; t++)
            {
                int idx = (int)r.ReadVar(); uint count = r.ReadVar();
                if (!lists.TryGetValue(idx, out var list)) lists[idx] = list = new List<Sample>();
                int time = chunkStart, px = 0, py = 0, pz = 0, ox = 0, oy = 0, oz = 0;
                ushort by = 0, ly = 0, lp = 0, lr = 0;
                for (uint i = 0; i < count; i++)
                {
                    time += (int)r.ReadVar();
                    px += r.ReadZig(); py += r.ReadZig(); pz += r.ReadZig();
                    ox += r.ReadZig(); oy += r.ReadZig(); oz += r.ReadZig();
                    by = (ushort)(by + r.ReadZig()); ly = (ushort)(ly + r.ReadZig());
                    lp = (ushort)(lp + r.ReadZig()); lr = (ushort)(lr + r.ReadZig());
                    list.Add(new Sample
                    {
                        TimeMs = time,
                        Px = Codec.QToPos(px), Py = Codec.QToPos(py), Pz = Codec.QToPos(pz),
                        Ex = Codec.QToPos(px + ox), Ey = Codec.QToPos(py + oy), Ez = Codec.QToPos(pz + oz),
                        BodyYaw = Codec.QToAngle(by), LookYaw = Codec.QToAngle(ly),
                        LookPitch = Codec.QToAngle(lp), LookRoll = Codec.QToAngle(lr),
                        Flags = r.ReadByte(), Speed = r.ReadByte(), Fov = r.ReadByte()
                    });
                }
            }
        }
    }
}
