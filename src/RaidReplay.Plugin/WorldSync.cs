using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using RaidReplay.Core;

namespace RaidReplay
{
    /// <summary>Finds the doors / containers / lamps of the current map. API taken from what Fika itself uses to sync late joiners.</summary>
    internal static class WorldObjects
    {
        public static List<WorldInteractiveObject> Interactives()
        {
            var list = new List<WorldInteractiveObject>();
            try
            {
                foreach (var o in LocationScene.GetAllObjects<WorldInteractiveObject>(false))
                    if (o != null && o.NetId != 0 && !(o is Switch)) list.Add(o);   // Switch objects drive power/exfil logic: their visible effect (lamps) is synced separately
            }
            catch (Exception e) { Plugin.Log.LogWarning("Interactive object lookup failed: " + e.Message); }

            if (list.Count == 0 && EftBridge.TryGetWorld(out var gw))
            {
                try   // fallback: the list Fika walks (field is not public in the retail assembly)
                {
                    var f = AccessTools.Field(gw.World.GetType(), "_interactableObjectsForNetSync");
                    if (f != null && f.GetValue(gw.World) is System.Collections.IEnumerable en)
                        foreach (var o in en) if (o is WorldInteractiveObject w && w.NetId != 0 && !(w is Switch)) list.Add(w);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Fallback interactive lookup failed: " + e.Message); }
            }
            return list;
        }

        public static List<LampController> Lamps()
        {
            var list = new List<LampController>();
            try { foreach (var l in LocationScene.GetAllObjects<LampController>(false)) if (l != null && l.NetId != 0) list.Add(l); }
            catch (Exception e) { Plugin.Log.LogWarning("Lamp lookup failed: " + e.Message); }
            return list;
        }
    }

    /// <summary>Writes door/container/lamp state changes while recording. Baseline at t=0, then only changes.</summary>
    internal sealed class WorldRecorder
    {
        readonly ReplayWriter _w;
        readonly List<WorldInteractiveObject> _doors;
        readonly List<LampController> _lamps;
        readonly Dictionary<int, int> _lastDoor = new Dictionary<int, int>();
        readonly Dictionary<int, byte> _lastLamp = new Dictionary<int, byte>();
        float _nextDoor, _nextLamp;

        public int DoorCount => _doors.Count;
        public int LampCount => _lamps.Count;

        public WorldRecorder(ReplayWriter w)
        {
            _w = w; _doors = WorldObjects.Interactives(); _lamps = WorldObjects.Lamps();
            PollDoors(0); PollLamps(0);
        }

        public void Tick(float now, int timeMs)
        {
            if (now >= _nextDoor) { _nextDoor = now + 0.2f; PollDoors(timeMs); }
            if (now >= _nextLamp) { _nextLamp = now + 0.5f; PollLamps(timeMs); }
        }

        void PollDoors(int t)
        {
            for (int i = 0; i < _doors.Count; i++)
            {
                var o = _doors[i];
                if (o == null) continue;
                var st = o.DoorState;
                if (st == EDoorState.Interacting || st == EDoorState.Breaching) continue; // transient: logged once it settles
                bool broken = o is Door d && d.IsBroken;
                int packed = (int)st | (broken ? 0x100 : 0);
                if (_lastDoor.TryGetValue(o.NetId, out int prev) && prev == packed) continue;
                _lastDoor[o.NetId] = packed;
                _w.AddWorld(new WorldEvent { TimeMs = t, Kind = WorldKind.Door, NetId = o.NetId, State = (byte)st, Broken = broken });
            }
        }

        void PollLamps(int t)
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                var l = _lamps[i];
                if (l == null) continue;
                byte st = (byte)l.LampState;
                if (_lastLamp.TryGetValue(l.NetId, out byte prev) && prev == st) continue;
                _lastLamp[l.NetId] = st;
                _w.AddWorld(new WorldEvent { TimeMs = t, Kind = WorldKind.Lamp, NetId = l.NetId, State = st });
            }
        }
    }

    /// <summary>Executes WorldTimeline changes in the replay raid using the same interaction calls Fika uses for remote players.</summary>
    internal sealed class WorldApplier
    {
        readonly Dictionary<int, WorldInteractiveObject> _doors = new Dictionary<int, WorldInteractiveObject>();
        readonly Dictionary<int, LampController> _lamps = new Dictionary<int, LampController>();
        readonly Dictionary<long, WorldChange> _pending = new Dictionary<long, WorldChange>();
        readonly Dictionary<long, int> _tries = new Dictionary<long, int>();
        bool _warned;

        public int DoorCount => _doors.Count;
        public int LampCount => _lamps.Count;
        public int Pending => _pending.Count;

        public WorldApplier()
        {
            foreach (var o in WorldObjects.Interactives()) _doors[o.NetId] = o;
            foreach (var l in WorldObjects.Lamps()) _lamps[l.NetId] = l;
        }

        static long Key(WorldChange c) => ((long)c.Kind << 32) | (uint)c.NetId;

        public void Enqueue(List<WorldChange> changes)
        {
            foreach (var c in changes) { long k = Key(c); _pending[k] = c; _tries.Remove(k); }
        }

        /// <summary>Call every frame: finishes changes that had to wait for a running door animation.</summary>
        public void Tick()
        {
            if (_pending.Count == 0) return;
            var keys = new List<long>(_pending.Keys);
            int budget = 80;
            foreach (long k in keys)
            {
                if (budget-- <= 0) break;
                var c = _pending[k];
                if (TryApply(c)) { _pending.Remove(k); _tries.Remove(k); continue; }
                _tries.TryGetValue(k, out int n);
                if (++n > 400) { _pending.Remove(k); _tries.Remove(k); } else _tries[k] = n;
            }
        }

        bool TryApply(WorldChange c)
        {
            try
            {
                if (c.Kind == WorldKind.Lamp)
                {
                    if (!_lamps.TryGetValue(c.NetId, out var lamp) || lamp == null) return true;
                    var target = (Turnable.EState)c.To;
                    if (lamp.LampState != target) lamp.Switch(target);
                    return true;
                }

                if (!_doors.TryGetValue(c.NetId, out var d) || d == null || !d.isActiveAndEnabled) return true;
                var cur = d.DoorState; var tgt = (EDoorState)c.To;
                if (cur == tgt) return true;
                if (cur == EDoorState.Interacting || cur == EDoorState.Breaching) return false;   // wait for the running animation

                EInteractionType? act = null;
                switch (tgt)
                {
                    case EDoorState.Open: act = cur == EDoorState.Locked ? EInteractionType.Unlock : EInteractionType.Open; break;
                    case EDoorState.Shut: act = cur == EDoorState.Open ? EInteractionType.Close : cur == EDoorState.Locked ? EInteractionType.Unlock : (EInteractionType?)null; break;
                    case EDoorState.Locked: act = cur == EDoorState.Open ? EInteractionType.Close : EInteractionType.Lock; break;
                }
                if (act == null) return true;
                d.Interact(new InteractionResult(act.Value));
                return false;   // re-check next frame; Locked -> Open needs two steps
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; Plugin.Log.LogWarning("World sync step failed (further errors muted): " + e.Message); }
                return true;
            }
        }
    }
}
