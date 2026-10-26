using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace RaidReplay.Core
{
    /// <summary>
    /// Streaming recorder. Call from the game thread; compression and disk IO run on a worker thread.
    /// </summary>
    public sealed class ReplayWriter : IDisposable
    {
        sealed class Job { public int Start, End; public byte[] Raw; }

        readonly FileStream _fs;
        readonly int _chunkMs;
        readonly BlockingCollection<Job> _queue = new BlockingCollection<Job>();
        readonly Thread _worker;
        readonly CompressionLevel _level;

        readonly List<EntityInfo> _pendingDefs = new List<EntityInfo>();
        readonly List<ReplayEvent> _events = new List<ReplayEvent>();
        readonly List<WorldEvent> _world = new List<WorldEvent>();
        readonly Dictionary<int, List<Sample>> _tracks = new Dictionary<int, List<Sample>>();
        int _nextIndex, _chunkIndex = -1, _chunkStart, _maxTime;
        bool _disposed;
        long _bytes;

        public string Path { get; }
        public int EntityCount => _nextIndex;
        public int LastTimeMs { get; private set; }
        public long BytesWritten => Interlocked.Read(ref _bytes);
        public string Error { get; private set; }

        public ReplayWriter(string path, ReplayHeader header, int chunkMs = 5000, CompressionLevel level = CompressionLevel.Fastest)
        {
            Path = path; _chunkMs = Math.Max(1000, chunkMs); _level = level;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using (var w = new BinaryWriter(_fs, Encoding.UTF8, true)) ReplayFile.WriteHeader(w, header);
            _fs.Flush();
            _worker = new Thread(Work) { IsBackground = true, Name = "RaidReplay.Writer" };
            _worker.Start();
        }

        public int DefineEntity(EntityInfo info)
        {
            info.Index = _nextIndex++;
            _pendingDefs.Add(info);
            return info.Index;
        }

        public void AddSample(int entity, Sample s)
        {
            Advance(s.TimeMs);
            if (s.TimeMs < _chunkStart) s.TimeMs = _chunkStart;
            if (!_tracks.TryGetValue(entity, out var list)) _tracks[entity] = list = new List<Sample>();
            if (list.Count > 0 && s.TimeMs < list[list.Count - 1].TimeMs) s.TimeMs = list[list.Count - 1].TimeMs;
            list.Add(s);
            if (s.TimeMs > _maxTime) _maxTime = s.TimeMs;
            LastTimeMs = Math.Max(LastTimeMs, s.TimeMs);
        }

        public void AddEvent(ReplayEvent e)
        {
            Advance(e.TimeMs);
            if (e.TimeMs < _chunkStart) e.TimeMs = _chunkStart;
            _events.Add(e);
            if (e.TimeMs > _maxTime) _maxTime = e.TimeMs;
            LastTimeMs = Math.Max(LastTimeMs, e.TimeMs);
        }

        public void AddWorld(WorldEvent e)
        {
            Advance(e.TimeMs);
            if (e.TimeMs < _chunkStart) e.TimeMs = _chunkStart;
            _world.Add(e);
            if (e.TimeMs > _maxTime) _maxTime = e.TimeMs;
            LastTimeMs = Math.Max(LastTimeMs, e.TimeMs);
        }

        void Advance(int timeMs)
        {
            int idx = Math.Max(0, timeMs) / _chunkMs;
            if (idx > _chunkIndex) { FlushChunk(); _chunkIndex = idx; _chunkStart = idx * _chunkMs; _maxTime = _chunkStart; }
        }

        void FlushChunk()
        {
            if (_pendingDefs.Count == 0 && _events.Count == 0 && _tracks.Count == 0 && _world.Count == 0) return;
            var ms = new MemoryStream(64 * 1024);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.WriteVar((uint)_pendingDefs.Count);
                foreach (var e in _pendingDefs)
                {
                    w.WriteVar((uint)e.Index); w.Write(e.ProfileId ?? ""); w.Write(e.Nickname ?? ""); w.Write((byte)e.Side);
                    w.Write((byte)((e.IsAI ? 1 : 0) | (e.IsPov ? 2 : 0)));
                    w.WriteVar((uint)Math.Max(0, e.Level)); w.Write(e.Role ?? "");
                    var blob = e.Blob ?? Array.Empty<byte>(); w.WriteVar((uint)blob.Length); w.Write(blob);
                }
                w.WriteVar((uint)_events.Count);
                foreach (var ev in _events)
                {
                    w.WriteVar((uint)Math.Max(0, ev.TimeMs - _chunkStart)); w.Write((byte)ev.Type);
                    w.WriteVar((uint)(ev.Actor + 1)); w.WriteVar((uint)(ev.Target + 1));
                    w.WriteZig(Codec.PosToQ(ev.X)); w.WriteZig(Codec.PosToQ(ev.Y)); w.WriteZig(Codec.PosToQ(ev.Z));
                    w.Write(ev.Arg); w.Write(ev.Arg2);
                }
                w.WriteVar((uint)_tracks.Count);
                foreach (var kv in _tracks)
                {
                    w.WriteVar((uint)kv.Key); w.WriteVar((uint)kv.Value.Count);
                    int time = _chunkStart, px = 0, py = 0, pz = 0, ox = 0, oy = 0, oz = 0;
                    ushort by = 0, ly = 0, lp = 0, lr = 0;
                    foreach (var s in kv.Value)
                    {
                        w.WriteVar((uint)(s.TimeMs - time)); time = s.TimeMs;
                        int qx = Codec.PosToQ(s.Px), qy = Codec.PosToQ(s.Py), qz = Codec.PosToQ(s.Pz);
                        int qox = Codec.PosToQ(s.Ex) - qx, qoy = Codec.PosToQ(s.Ey) - qy, qoz = Codec.PosToQ(s.Ez) - qz;
                        w.WriteZig(qx - px); w.WriteZig(qy - py); w.WriteZig(qz - pz);
                        w.WriteZig(qox - ox); w.WriteZig(qoy - oy); w.WriteZig(qoz - oz);
                        px = qx; py = qy; pz = qz; ox = qox; oy = qoy; oz = qoz;
                        ushort nby = Codec.AngleToQ(s.BodyYaw), nly = Codec.AngleToQ(s.LookYaw),
                               nlp = Codec.AngleToQ(s.LookPitch), nlr = Codec.AngleToQ(s.LookRoll);
                        w.WriteZig(Codec.AngleDelta(nby, by)); w.WriteZig(Codec.AngleDelta(nly, ly));
                        w.WriteZig(Codec.AngleDelta(nlp, lp)); w.WriteZig(Codec.AngleDelta(nlr, lr));
                        by = nby; ly = nly; lp = nlp; lr = nlr;
                        w.Write(s.Flags); w.Write(s.Speed); w.Write(s.Fov);
                    }
                }
                w.WriteVar((uint)_world.Count);
                foreach (var we in _world)
                {
                    w.WriteVar((uint)Math.Max(0, we.TimeMs - _chunkStart)); w.Write((byte)we.Kind);
                    w.WriteVar((uint)we.NetId); w.Write(we.State); w.Write((byte)(we.Broken ? 1 : 0));
                }
            }
            _queue.Add(new Job { Start = _chunkStart, End = _maxTime, Raw = ms.ToArray() });
            _pendingDefs.Clear(); _events.Clear(); _tracks.Clear(); _world.Clear();
        }

        void Work()
        {
            try
            {
                foreach (var job in _queue.GetConsumingEnumerable())
                {
                    var cms = new MemoryStream(job.Raw.Length / 2 + 64);
                    using (var ds = new DeflateStream(cms, _level, true)) ds.Write(job.Raw, 0, job.Raw.Length);
                    var comp = cms.ToArray();
                    using (var w = new BinaryWriter(_fs, Encoding.UTF8, true))
                    {
                        w.Write((uint)job.Start); w.Write((uint)job.End); w.Write((uint)job.Raw.Length); w.Write((uint)comp.Length);
                        w.Write(comp);
                    }
                    _fs.Flush();
                    Interlocked.Add(ref _bytes, 16 + comp.Length);
                }
            }
            catch (Exception e) { Error = e.Message; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FlushChunk();
            _queue.CompleteAdding();
            _worker.Join();
            _fs.Dispose();
        }
    }
}
