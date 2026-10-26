using System.Collections.Generic;

namespace RaidReplay.Core
{
    public struct WorldChange
    {
        public WorldKind Kind;
        public int NetId;
        public byte To;
        public bool Broken;
        /// <summary>true = normal forward playback (play the door animation); false = seek/jump (snap if possible).</summary>
        public bool Animated;
    }

    /// <summary>
    /// Turns recorded door/lamp states into "what must change now" lists while the replay clock moves.
    /// Pure logic: the game-side applier only has to execute the returned changes.
    /// </summary>
    public sealed class WorldTimeline
    {
        readonly List<WorldEvent> _all;
        readonly Dictionary<long, List<WorldEvent>> _by = new Dictionary<long, List<WorldEvent>>();
        readonly Dictionary<long, WorldEvent> _applied = new Dictionary<long, WorldEvent>();
        readonly Dictionary<long, WorldEvent> _scratch = new Dictionary<long, WorldEvent>();
        int _cursor;

        public int ObjectCount => _by.Count;

        static long Key(WorldKind k, int id) => ((long)k << 32) | (uint)id;

        public WorldTimeline(IEnumerable<WorldEvent> events)
        {
            _all = new List<WorldEvent>(events);
            _all.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            foreach (var e in _all)
            {
                long k = Key(e.Kind, e.NetId);
                if (!_by.TryGetValue(k, out var l)) _by[k] = l = new List<WorldEvent>();
                l.Add(e);
            }
        }

        public void Reset() { _applied.Clear(); _cursor = 0; }

        public void Advance(int prevMs, int nowMs, bool jump, List<WorldChange> output)
        {
            if (jump || nowMs < prevMs) { Jump(nowMs, output); return; }
            _scratch.Clear();
            while (_cursor < _all.Count && _all[_cursor].TimeMs <= nowMs)
            {
                var e = _all[_cursor++];
                _scratch[Key(e.Kind, e.NetId)] = e;
            }
            foreach (var e in _scratch.Values) Emit(e, true, output);
        }

        void Jump(int nowMs, List<WorldChange> output)
        {
            int lo = 0, hi = _all.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_all[mid].TimeMs <= nowMs) lo = mid + 1; else hi = mid; }
            _cursor = lo;
            foreach (var kv in _by)
            {
                var list = kv.Value; int a = 0, b = list.Count;
                while (a < b) { int mid = (a + b) >> 1; if (list[mid].TimeMs <= nowMs) a = mid + 1; else b = mid; }
                if (a > 0) Emit(list[a - 1], false, output);
            }
        }

        void Emit(WorldEvent e, bool animated, List<WorldChange> output)
        {
            long k = Key(e.Kind, e.NetId);
            if (_applied.TryGetValue(k, out var prev) && prev.State == e.State && prev.Broken == e.Broken) return;
            _applied[k] = e;
            output.Add(new WorldChange { Kind = e.Kind, NetId = e.NetId, To = e.State, Broken = e.Broken, Animated = animated });
        }
    }
}
