using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>Records the raid in the background: poses at a fixed rate + shots + deaths. Works in plain SPT and in Fika raids
    /// (other humans are ObservedPlayer objects, so they appear in AllAlivePlayersList like everyone else).</summary>
    internal sealed class RaidRecorder : MonoBehaviour
    {
        public static RaidRecorder Instance;

        ReplayWriter _w;
        GameWorld _gw;
        Player _me;
        float _t0, _nextPov, _nextOthers;
        readonly Dictionary<string, int> _idx = new Dictionary<string, int>();
        readonly HashSet<string> _hooked = new HashSet<string>();
        readonly Dictionary<Player, Delegate> _deathHandlers = new Dictionary<Player, Delegate>();
        readonly Dictionary<string, Player> _players = new Dictionary<string, Player>();

        void Awake() { Instance = this; }

        int NowMs => Mathf.Max(0, Mathf.RoundToInt((Time.time - _t0) * 1000f));

        void Update()
        {
            bool ready = EftBridge.TryGetWorld(out var gw) && EftBridge.RaidReady(gw);
            if (_w == null)
            {
                if (ready && Cfg.RecordEnabled.Value && !ReplaySession.Active) Begin(gw);
                return;
            }
            if (!ready || gw != _gw || ReplaySession.Active) { End(); return; }
            Tick(gw);
        }

        void OnDestroy() { End(); }

        void Begin(GameWorld gw)
        {
            try
            {
                _gw = gw; _me = gw.MainPlayer; _t0 = Time.time; _nextPov = _nextOthers = 0f;
                _idx.Clear(); _hooked.Clear(); _players.Clear();
                var header = new ReplayHeader
                {
                    Map = EftBridge.MapId(gw), StartedUtcTicks = DateTime.UtcNow.Ticks, PovProfileId = _me.ProfileId,
                    PovNickname = _me.Profile.Info.Nickname ?? "", GameInfo = "RaidReplay " + Plugin.Version + (Plugin.FikaPresent ? " +Fika" : ""),
                    OthersHz = Cfg.OthersHz.Value, PovHz = Cfg.PovHz.Value
                };
                _w = new ReplayWriter(Plugin.Library.CreatePath(header.Map, DateTime.Now), header);
                Plugin.Log.LogInfo("Recording raid on " + header.Map + " -> " + _w.Path);
            }
            catch (Exception e) { Plugin.Log.LogError("Could not start recording: " + e); _w = null; }
        }

        void Tick(GameWorld gw)
        {
            float now = Time.time - _t0;
            int t = NowMs;
            if (now >= _nextPov)
            {
                _nextPov = now + 1f / Mathf.Max(1, Cfg.PovHz.Value);
                if (EftBridge.IsAlive(_me)) WriteSample(_me, true, t);
            }
            if (now >= _nextOthers)
            {
                _nextOthers = now + 1f / Mathf.Max(1, Cfg.OthersHz.Value);
                var list = gw.AllAlivePlayersList;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null || p == _me || !EftBridge.IsAlive(p)) continue;
                    WriteSample(p, false, t);
                }
            }
        }

        int Ensure(Player p, bool isPov)
        {
            if (_idx.TryGetValue(p.ProfileId, out int i)) return i;
            i = _w.DefineEntity(EftBridge.Describe(p, isPov));
            _idx[p.ProfileId] = i; _players[p.ProfileId] = p;
            if (_hooked.Add(p.ProfileId)) HookDeathEvent(p);
            return i;
        }

        void WriteSample(Player p, bool isPov, int t)
        {
            if (!EftBridge.ReadSample(p, isPov, t, out var s)) return;
            _w.AddSample(Ensure(p, isPov), s);
        }

        void HookDeathEvent(Player player)
        {
            try
            {
                var deathEvent = player.GetType().GetEvent("OnPlayerDead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var handlerType = deathEvent?.EventHandlerType;
                var invoke = handlerType?.GetMethod("Invoke");
                if (deathEvent == null || invoke == null || invoke.ReturnType != typeof(void)) return;
                var signature = invoke.GetParameters();
                if (signature.Length < 2) return;
                var args = new ParameterExpression[signature.Length];
                for (int i = 0; i < args.Length; i++) args[i] = Expression.Parameter(signature[i].ParameterType, "arg" + i);
                var callback = GetType().GetMethod(nameof(OnPlayerDeadDynamic), BindingFlags.Instance | BindingFlags.NonPublic);
                var body = Expression.Call(Expression.Constant(this), callback,
                    Expression.Convert(args[0], typeof(Player)), Expression.Convert(args[1], typeof(IPlayer)));
                var handler = Expression.Lambda(handlerType, body, args).Compile();
                deathEvent.AddEventHandler(player, handler);
                _deathHandlers[player] = handler;
            }
            catch (Exception e) { Plugin.Log.LogDebug("Could not hook player death event: " + e.Message); }
        }

        void OnPlayerDeadDynamic(Player victim, IPlayer killer)
        {
            if (victim != null) OnDead(victim.ProfileId, killer);
        }

        void OnDead(string victimId, IPlayer killer)
        {
            if (_w == null || !_idx.TryGetValue(victimId, out int v)) return;
            int t = NowMs;
            var victim = _players[victimId];
            if (EftBridge.ReadSample(victim, victimId == _me.ProfileId, t, out var s)) { s.Flags = (byte)(s.Flags & ~SampleFlags.Alive); _w.AddSample(v, s); }
            int k = killer != null && killer.ProfileId != null && _idx.TryGetValue(killer.ProfileId, out int ki) ? ki : -1;
            _w.AddEvent(new ReplayEvent { TimeMs = t, Type = EventKind.Kill, Actor = k, Target = v, X = victim.Position.x, Y = victim.Position.y, Z = victim.Position.z });
        }

        public void OnShot(Player shooter, Vector3 pos, Vector3 dir)
        {
            if (_w == null || shooter == null || !_idx.TryGetValue(shooter.ProfileId, out int a)) return;
            var e = Quaternion.LookRotation(dir).eulerAngles;
            _w.AddEvent(new ReplayEvent { TimeMs = NowMs, Type = EventKind.Shot, Actor = a, Target = -1, X = pos.x, Y = pos.y, Z = pos.z, Arg = e.y, Arg2 = e.x });
        }

        void End()
        {
            if (_w == null) return;
            var w = _w; _w = null;
            foreach (var pair in _deathHandlers)
            {
                try { pair.Key.GetType().GetEvent("OnPlayerDead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.RemoveEventHandler(pair.Key, pair.Value); }
                catch { }
            }
            _deathHandlers.Clear();
            _hooked.Clear();
            _players.Clear();
            try
            {
                int dur = w.LastTimeMs; string path = w.Path;
                w.Dispose();
                if (!string.IsNullOrEmpty(w.Error)) Plugin.Log.LogWarning("Writer reported: " + w.Error);
                if (dur < Cfg.MinRaidSeconds.Value * 1000) { File.Delete(path); Plugin.Log.LogInfo("Raid too short, replay discarded."); }
                else
                {
                    int removed = Plugin.Library.ApplyRetention(Cfg.KeepLast.Value, path);
                    Plugin.Log.LogInfo($"Saved replay {Path.GetFileName(path)} ({w.BytesWritten / 1048576f:F1} MB, {dur / 1000}s). Removed {removed} old.");
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Finalising replay failed: " + e); }
        }
    }

    /// <summary>Hooks Player.FirearmController.InitiateShot (base class; Fika's observed/client controllers call into it too).</summary>
    internal static class ShotPatch
    {
        public static void TryApply()
        {
            try
            {
                var fc = typeof(Player).GetNestedType("FirearmController", BindingFlags.Public | BindingFlags.NonPublic);
                var m = fc == null ? null : AccessTools.Method(fc, "InitiateShot");
                if (m == null) { Plugin.Log.LogWarning("InitiateShot not found - shots will not be recorded."); return; }
                new Harmony(Plugin.Guid).Patch(m, postfix: new HarmonyMethod(typeof(ShotPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Shot hook failed (replays still work, without tracers): " + e.Message); }
        }

        // args: 0 weapon, 1 ammo, 2 shotPosition, 3 shotDirection, 4 fireportPosition, 5 chamberIndex, 6 overheat
        static void Postfix(object __instance, object[] __args)
        {
            try
            {
                var rec = RaidRecorder.Instance;
                if (rec == null || !EftBridge.TryGetWorld(out var gw)) return;
                rec.OnShot(EftBridge.FindPlayerByHands(gw, __instance), (Vector3)__args[2], (Vector3)__args[3]);
            }
            catch { /* never break the game's shooting code */ }
        }
    }
}
