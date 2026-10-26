using System;
using System.Collections.Generic;
using System.IO;
using RaidReplay.Core;

static class T
{
    static int _fail, _pass;
    public static void Check(bool ok, string name) { if (ok) _pass++; else { _fail++; Console.WriteLine("FAIL: " + name); } }
    public static bool Near(float a, float b, float eps) => Math.Abs(a - b) <= eps;
    public static float AngDiff(float a, float b) { float d = Math.Abs(a - b) % 360f; return d > 180f ? 360f - d : d; }

    static ReplayHeader Hdr(string map = "bigmap") => new ReplayHeader { Map = map, StartedUtcTicks = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc).Ticks, PovProfileId = "pov", PovNickname = "Me", OthersHz = 15, PovHz = 60 };

    static Sample Mk(int t, float x, float z, float yaw, byte flags = SampleFlags.Alive) => new Sample
    { TimeMs = t, Px = x, Py = 1.5f, Pz = z, Ex = x, Ey = 3.1f, Ez = z, BodyYaw = yaw, LookYaw = yaw, LookPitch = -10, LookRoll = 0, Flags = flags, Speed = 40, Fov = 75 };

    static void RoundTrip(string dir)
    {
        string p = Path.Combine(dir, "rt.rrpl");
        using (var w = new ReplayWriter(p, Hdr()))
        {
            int a = w.DefineEntity(new EntityInfo { ProfileId = "pov", Nickname = "Me", Side = EntitySide.Usec, IsPov = true, Level = 42, Role = "pmc", Blob = new byte[] { 1, 2, 3 } });
            int b = w.DefineEntity(new EntityInfo { ProfileId = "bot1", Nickname = "Reshala", Side = EntitySide.Savage, IsAI = true, Role = "bossBully" });
            for (int t = 0; t <= 12000; t += 100)
            {
                w.AddSample(a, Mk(t, 100 + t * 0.001f, -50 + t * 0.002f, (t * 0.03f) % 360));
                if (t % 200 == 0) w.AddSample(b, Mk(t, -20.123f - t * 0.0005f, 8.987f, 350 + t * 0.002f));
            }
            w.AddEvent(new ReplayEvent { TimeMs = 4900, Type = EventKind.Shot, Actor = a, Target = -1, X = 1.234f, Y = 2.5f, Z = -3.75f, Arg = 123.4f, Arg2 = -5f });
            w.AddEvent(new ReplayEvent { TimeMs = 11000, Type = EventKind.Kill, Actor = a, Target = b, X = 9, Y = 1, Z = 2 });
        }
        var r = Replay.Load(p);
        Check(!r.Truncated, "roundtrip not truncated");
        Check(r.Header.Map == "bigmap" && r.Header.PovNickname == "Me" && r.Header.PovHz == 60, "header fields");
        Check(r.Entities.Count == 2 && r.Entities[0].IsPov && r.Entities[1].IsAI && r.Entities[1].Nickname == "Reshala" && r.Entities[0].Level == 42, "entity info");
        Check(r.Entities[0].Blob.Length == 3 && r.Entities[0].Blob[2] == 3, "entity blob");
        Check(r.FindPov().ProfileId == "pov", "FindPov");
        Check(r.Tracks[0].Length == 121 && r.Tracks[1].Length == 61, "sample counts " + r.Tracks[0].Length + "/" + r.Tracks[1].Length);
        Check(r.DurationMs >= 12000, "duration " + r.DurationMs);
        bool ok = true;
        var s = r.Tracks[1];
        for (int i = 0; i < s.Length; i++)
        {
            int t = i * 200;
            ok &= s[i].TimeMs == t && Near(s[i].Px, -20.123f - t * 0.0005f, 0.006f) && Near(s[i].Pz, 8.987f, 0.006f)
                  && AngDiff(s[i].BodyYaw, (350 + t * 0.002f) % 360) < 0.01f && s[i].Flags == SampleFlags.Alive && s[i].Fov == 75 && Near(s[i].Ey, 3.1f, 0.006f);
        }
        Check(ok, "sample fidelity (cm / 0.0055 deg)");
        Check(r.Events.Count == 2 && r.Events[0].Type == EventKind.Shot && Near(r.Events[0].Arg, 123.4f, 0.001f) && r.Events[1].Target == 1 && r.Events[1].Actor == 0, "events");
        var info = ReplayFile.ReadInfo(p);
        Check(info.DurationMs == r.DurationMs && info.Header.Map == "bigmap", "ReadInfo matches Load");

        // truncated file (crash / killed process): must still load earlier chunks
        var bytes = File.ReadAllBytes(p);
        string cut = Path.Combine(dir, "cut.rrpl");
        File.WriteAllBytes(cut, bytes[..(bytes.Length - 25)]);
        var rc = Replay.Load(cut);
        Check(rc.Truncated && rc.Tracks[0].Length > 40 && rc.Tracks[0].Length < 121, "truncated file still loads: " + rc.Tracks[0].Length);
        var ic = ReplayFile.ReadInfo(cut);
        Check(ic.DurationMs > 0 && ic.DurationMs <= r.DurationMs, "ReadInfo on truncated file");
        File.WriteAllText(Path.Combine(dir, "junk.rrpl"), "not a replay");
        bool threw = false; try { Replay.Load(Path.Combine(dir, "junk.rrpl")); } catch (InvalidDataException) { threw = true; } catch (EndOfStreamException) { threw = true; }
        Check(threw, "junk file rejected");
    }

    static void Sampler()
    {
        var s = new[] { Mk(0, 0, 0, 350), Mk(1000, 10, 0, 10), Mk(2000, 10, 10, 10) };
        Check(!TrackSampler.TryEvaluate(s, -1, 2000, out _), "before first sample = absent");
        Check(TrackSampler.TryEvaluate(s, 500, 2000, out var m) && Near(m.Px, 5, 0.001f), "lerp position");
        Check(AngDiff(m.BodyYaw, 0) < 0.01f, "angle lerp wraps 350->10 through 0, got " + m.BodyYaw);
        var dead = new[] { Mk(0, 0, 0, 0), Mk(1000, 1, 1, 0, 0) };
        Check(TrackSampler.TryEvaluate(dead, 60000, 2000, out var c) && !c.Alive, "corpse stays after last sample");
        Check(!TrackSampler.TryEvaluate(s, 9000, 2000, out _), "living entity that stopped reporting disappears");
        Check(TrackSampler.TryEvaluate(s, 2500, 2000, out _), "short hold after last sample");
        var gap = new[] { Mk(0, 0, 0, 0), Mk(10000, 50, 0, 0) };
        Check(TrackSampler.TryEvaluate(gap, 1000, 2000, out var g) && Near(g.Px, 0, 0.001f), "gap: hold, no interpolation");
        Check(!TrackSampler.TryEvaluate(gap, 5000, 2000, out _), "gap: hidden after maxGap");
    }

    static void Clock()
    {
        var c = new ReplayClock(10000);
        c.Tick(1); Check(Near((float)c.TimeMs, 1000, 0.1f), "tick 1x");
        c.SpeedUp(); c.SpeedUp(); Check(c.Speed == 4f, "speed steps"); c.Tick(1); Check(Near((float)c.TimeMs, 5000, 0.1f), "tick 4x");
        c.SpeedDown(); Check(c.Speed == 2f, "speed down");
        c.Paused = true; c.Tick(5); Check(Near((float)c.TimeMs, 5000, 0.1f), "paused");
        c.Seek(-50); Check(c.TimeMs == 0, "seek clamps low"); c.SeekBy(99999); Check(c.TimeMs == 10000 && c.Ended, "seek clamps high / ended");
    }

    static void Library(string dir)
    {
        var lib = new ReplayLibrary(Path.Combine(dir, "lib"));
        var paths = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            string p = lib.CreatePath("woods", new DateTime(2026, 10, 20, 10, i, 0));
            using (var w = new ReplayWriter(p, Hdr("woods"))) { int e = w.DefineEntity(new EntityInfo { IsPov = true, ProfileId = "x" }); w.AddSample(e, Mk(0, 0, 0, 0)); }
            paths.Add(p);
        }
        lib.SetPinned(paths[1], true);
        int deleted = lib.ApplyRetention(5, paths[7]);
        var left = lib.List();
        Check(deleted == 2, "retention deleted 2, got " + deleted);
        Check(left.Count == 6, "5 newest + 1 pinned remain, got " + left.Count);
        Check(File.Exists(paths[1]) && lib.IsPinned(paths[1]), "pinned survives");
        Check(!File.Exists(paths[0]) && !File.Exists(paths[2]) && File.Exists(paths[3]), "oldest unpinned deleted, newest 5 kept");
        Check(left[0].FileName.CompareTo(left[left.Count - 1].FileName) > 0, "list newest first");
        lib.ApplyRetention(1, paths[3]); Check(lib.List().Count == 3 && File.Exists(paths[3]) && File.Exists(paths[7]) && File.Exists(paths[1]), "keep=1: newest + protected + pinned");
        lib.SetPinned(paths[1], false); Check(!lib.IsPinned(paths[1]), "unpin");
    }

    static void World(string dir)
    {
        // timeline logic
        var ev = new List<WorldEvent>
        {
            new WorldEvent { TimeMs = 0, Kind = WorldKind.Door, NetId = 7, State = 2 },      // Shut
            new WorldEvent { TimeMs = 0, Kind = WorldKind.Door, NetId = 9, State = 1 },      // Locked
            new WorldEvent { TimeMs = 0, Kind = WorldKind.Lamp, NetId = 7, State = 1 },      // same netId, different kind
            new WorldEvent { TimeMs = 5000, Kind = WorldKind.Door, NetId = 7, State = 3 },   // Open
            new WorldEvent { TimeMs = 9000, Kind = WorldKind.Door, NetId = 7, State = 2 },   // Shut again
            new WorldEvent { TimeMs = 9500, Kind = WorldKind.Lamp, NetId = 7, State = 0 },
        };
        var tl = new WorldTimeline(ev); var o = new List<WorldChange>();
        Check(tl.ObjectCount == 3, "door 7, door 9, lamp 7 are three separate objects");
        tl.Advance(0, 0, true, o);
        Check(o.Count == 3 && !o[0].Animated, "baseline emits every object once, not animated");
        o.Clear(); tl.Advance(0, 4000, false, o); Check(o.Count == 0, "no change before first event");
        o.Clear(); tl.Advance(4000, 5100, false, o); Check(o.Count == 1 && o[0].NetId == 7 && o[0].To == 3 && o[0].Animated && o[0].Kind == WorldKind.Door, "door opens, animated");
        o.Clear(); tl.Advance(5100, 5200, false, o); Check(o.Count == 0, "no repeat emission");
        o.Clear(); tl.Advance(5200, 12000, false, o); Check(o.Count == 2 && o.Exists(c => c.Kind == WorldKind.Door && c.To == 2) && o.Exists(c => c.Kind == WorldKind.Lamp && c.To == 0), "door closes + lamp off in one step");
        o.Clear(); tl.Advance(12000, 6000, true, o); Check(o.Count == 2 && o.TrueForAll(c => !c.Animated) && o.Exists(c => c.Kind == WorldKind.Door && c.To == 3) && o.Exists(c => c.Kind == WorldKind.Lamp && c.To == 1), "seek back snaps door open + lamp on");
        o.Clear(); tl.Advance(6000, 6100, false, o); Check(o.Count == 0, "playing on after seek is quiet");
        o.Clear(); tl.Advance(6100, 100, false, o); Check(o.Count == 1 && o[0].To == 2 && !o[0].Animated, "backwards time is treated as jump (only the door differs)");
        // file roundtrip with world section
        string p = Path.Combine(dir, "world.rrpl");
        using (var w = new ReplayWriter(p, Hdr()))
        {
            int a = w.DefineEntity(new EntityInfo { IsPov = true, ProfileId = "x" });
            w.AddWorld(new WorldEvent { TimeMs = 0, Kind = WorldKind.Door, NetId = 1234, State = 2 });
            for (int t = 0; t < 13000; t += 500)
            {
                w.AddSample(a, Mk(t, t * 0.001f, 0, 0));
                if (t == 6000) w.AddWorld(new WorldEvent { TimeMs = 6100, Kind = WorldKind.Door, NetId = 1234, State = 3, Broken = true });
            }
            w.AddWorld(new WorldEvent { TimeMs = 12900, Kind = WorldKind.Lamp, NetId = 55, State = 1 });
        }
        var r = Replay.Load(p);
        Check(r.Header.Version == 2 && r.World.Count == 3 && r.World[1].TimeMs == 6100 && r.World[1].Broken && r.World[1].NetId == 1234 && r.World[2].Kind == WorldKind.Lamp, "world events roundtrip across chunks");
        Check(MapIds.DisplayName("factory4_night") == "Factory" && MapIds.DisplayName("bigmap") == "Customs" && MapIds.DisplayName("weird") == "weird", "map display names");
    }

    static void MiniMap()
    {
        Check(MapIds.Same("factory4_day","Factory4_Night") && MapIds.Same("Sandbox_high","sandbox") && !MapIds.Same("bigmap","Woods"), "map id normalisation");
        Check(MiniMapMath.WorldToMap(10, 0, 0, 0, 90, true, 50, 100, out var x, out var y) && Near(x, 0, 0.01f) && Near(y, -20, 0.01f), "entity ahead is up: " + x + "," + y);
        Check(MiniMapMath.WorldToMap(10, 0, 0, 0, 0, false, 50, 100, out x, out y) && Near(x, 20, 0.01f) && Near(y, 0, 0.01f), "north-up: east is right");
        Check(!MiniMapMath.WorldToMap(100, 0, 0, 0, 0, false, 50, 100, out _, out _), "out of range hidden");
    }

    static void SizeBenchmark(string dir)
    {
        // 25 minute raid, 25 entities @15 Hz + POV @60 Hz, smooth movement with turns and stops
        var rnd = new Random(7);
        string p = Path.Combine(dir, "bench.rrpl");
        const int ents = 25; int dur = 25 * 60 * 1000;
        var st = new float[ents, 5]; // x,z,heading,speed,turnrate
        for (int i = 0; i < ents; i++) { st[i, 0] = rnd.Next(-300, 300); st[i, 1] = rnd.Next(-300, 300); st[i, 2] = rnd.Next(360); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long samples = 0;
        using (var w = new ReplayWriter(p, Hdr()))
        {
            for (int i = 0; i < ents; i++) w.DefineEntity(new EntityInfo { ProfileId = "e" + i, Nickname = "N" + i, IsPov = i == 0, IsAI = i != 0 });
            int nextOthers = 0;
            for (int t = 0; t < dur; t += 1000 / 60 + (t % 3 == 0 ? 1 : 0))
            {
                float dt = 1f / 60f;
                for (int i = 0; i < ents; i++)
                {
                    if (rnd.NextDouble() < 0.004) { st[i, 3] = (float)(rnd.NextDouble() < 0.3 ? 0 : 1.5 + rnd.NextDouble() * 4); st[i, 4] = (float)(rnd.NextDouble() * 120 - 60); }
                    st[i, 2] += st[i, 4] * dt * (st[i, 3] > 0 ? 0.3f : 2f) + (float)(rnd.NextDouble() - 0.5) * 0.3f;
                    st[i, 0] += (float)Math.Sin(st[i, 2] * Math.PI / 180) * st[i, 3] * dt;
                    st[i, 1] += (float)Math.Cos(st[i, 2] * Math.PI / 180) * st[i, 3] * dt;
                }
                var pov = Mk(t, st[0, 0], st[0, 1], st[0, 2]); pov.Ey = 1.5f + (float)Math.Sin(t / 300.0) * 0.02f; pov.Speed = Codec2.Sp(st[0, 3]);
                w.AddSample(0, pov); samples++;
                if (t >= nextOthers)
                {
                    nextOthers = t + 66;
                    for (int i = 1; i < ents; i++) { var s = Mk(t, st[i, 0], st[i, 1], st[i, 2]); s.Fov = 0; s.Speed = Codec2.Sp(st[i, 3]); w.AddSample(i, s); samples++; }
                }
            }
        }
        long size = new FileInfo(p).Length;
        Console.WriteLine($"[bench] 25 min, {ents} entities, {samples:N0} samples -> {size / 1048576.0:F2} MB on disk ({size / (dur / 60000.0) / 1024:F0} KB/min), write {sw.ElapsedMilliseconds} ms");
        sw.Restart(); var r = Replay.Load(p); Console.WriteLine($"[bench] load {sw.ElapsedMilliseconds} ms, duration {r.DurationMs / 1000}s");
        Check(size < 25L * 1024 * 1024, "bench size sanity");
    }

    static class Codec2 { public static byte Sp(float ms) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ms * 20))); }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rr_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { RoundTrip(dir); Sampler(); Clock(); Library(dir); World(dir); MiniMap(); SizeBenchmark(dir); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}
