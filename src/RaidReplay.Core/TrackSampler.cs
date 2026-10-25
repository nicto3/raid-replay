using System;

namespace RaidReplay.Core
{
    public static class TrackSampler
    {
        public const int DefaultMaxGapMs = 2000;

        public static float LerpAngle(float a, float b, float t)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            float r = a + d * t;
            r %= 360f; if (r < 0) r += 360f;
            return r;
        }

        /// <summary>Evaluates an entity at time tMs. Returns false when the entity is not in the world at that moment.</summary>
        public static bool TryEvaluate(Sample[] s, int tMs, int maxGapMs, out Sample result)
        {
            result = default;
            if (s == null || s.Length == 0 || tMs < s[0].TimeMs) return false;

            int lo = 0, hi = s.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (s[mid].TimeMs <= tMs) lo = mid; else hi = mid - 1; }
            var a = s[lo];

            if (lo == s.Length - 1)
            {
                // after the last sample: corpses stay, living entities that stop reporting have left the raid
                if (!a.Alive || tMs - a.TimeMs <= maxGapMs) { result = a; result.TimeMs = tMs; return true; }
                return false;
            }

            var b = s[lo + 1];
            int gap = b.TimeMs - a.TimeMs;
            if (gap > maxGapMs)
            {
                if (tMs - a.TimeMs > maxGapMs && a.Alive) return false;
                result = a; result.TimeMs = tMs; return true;
            }
            float u = gap <= 0 ? 0f : (tMs - a.TimeMs) / (float)gap;
            result = new Sample
            {
                TimeMs = tMs,
                Px = a.Px + (b.Px - a.Px) * u, Py = a.Py + (b.Py - a.Py) * u, Pz = a.Pz + (b.Pz - a.Pz) * u,
                Ex = a.Ex + (b.Ex - a.Ex) * u, Ey = a.Ey + (b.Ey - a.Ey) * u, Ez = a.Ez + (b.Ez - a.Ez) * u,
                BodyYaw = LerpAngle(a.BodyYaw, b.BodyYaw, u),
                LookYaw = LerpAngle(a.LookYaw, b.LookYaw, u),
                LookPitch = LerpAngle(a.LookPitch, b.LookPitch, u),
                LookRoll = LerpAngle(a.LookRoll, b.LookRoll, u),
                Flags = u < 0.5f ? a.Flags : b.Flags,
                Speed = (byte)Math.Round(a.Speed + (b.Speed - a.Speed) * u),
                Fov = (byte)Math.Round(a.Fov + (b.Fov - a.Fov) * u)
            };
            return true;
        }
    }

    public sealed class ReplayClock
    {
        public static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        public int DurationMs { get; }
        public double TimeMs { get; private set; }
        public float Speed { get; private set; } = 1f;
        public bool Paused { get; set; }
        public bool Ended => TimeMs >= DurationMs;
        public int NowMs => (int)TimeMs;

        public ReplayClock(int durationMs) { DurationMs = Math.Max(0, durationMs); }

        public void Tick(double realDeltaSeconds)
        {
            if (Paused) return;
            TimeMs = Math.Min(DurationMs, TimeMs + realDeltaSeconds * 1000.0 * Speed);
        }
        public void Seek(double ms) { TimeMs = Math.Max(0, Math.Min(DurationMs, ms)); }
        public void SeekBy(double deltaMs) => Seek(TimeMs + deltaMs);
        public void SetSpeed(float s) { Speed = Math.Max(0.1f, Math.Min(16f, s)); }
        public void SpeedUp() { foreach (var s in SpeedSteps) if (s > Speed + 0.01f) { Speed = s; return; } }
        public void SpeedDown() { for (int i = SpeedSteps.Length - 1; i >= 0; i--) if (SpeedSteps[i] < Speed - 0.01f) { Speed = SpeedSteps[i]; return; } }
    }

    public static class MiniMapMath
    {
        /// <summary>Projects a world XZ point into minimap pixels relative to the map centre (screen Y grows downwards).</summary>
        public static bool WorldToMap(float wx, float wz, float cx, float cz, float viewYawDeg, bool rotateWithView,
                                      float rangeMeters, float radiusPx, out float mx, out float my)
        {
            float dx = wx - cx, dz = wz - cz;
            float yaw = rotateWithView ? viewYawDeg * (float)Math.PI / 180f : 0f;
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);   // forward
            float rx = fz, rz = -fx;                                       // right
            float right = dx * rx + dz * rz, fwd = dx * fx + dz * fz;
            float k = radiusPx / Math.Max(1f, rangeMeters);
            mx = right * k; my = -fwd * k;
            return mx * mx + my * my <= radiusPx * radiusPx;
        }
    }
}

namespace RaidReplay.Core
{
    public static class MapIds
    {
        /// <summary>Collapses day/night and "_high" variants so a replay recorded on factory4_day matches factory4_night.</summary>
        public static string Normalize(string id)
        {
            string s = (id ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("factory4")) return "factory4";
            if (s.StartsWith("sandbox")) return "sandbox";
            return s;
        }
        public static bool Same(string a, string b) => Normalize(a) == Normalize(b);
    }
}
