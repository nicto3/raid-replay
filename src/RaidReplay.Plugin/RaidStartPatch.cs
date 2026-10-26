using System;
using System.Reflection;
using EFT;
using EFT.Bots;
using HarmonyLib;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// "Quiet raid" for replays. Same hook SPT itself uses (TarkovApplication.LoadMapAndData receives the RaidSettings by ref):
    /// when a replay is queued the raid is started without bots, bosses and PMC waves, and without hunger/thirst, so the raid loads
    /// faster, nobody can kill the camera body and there is nothing to switch off by hand. Also measures how long the raid took to load.
    /// </summary>
    internal static class RaidStartPatch
    {
        static float _loadStartedAt;
        public static bool LastWasQuiet { get; private set; }
        public static float LastLoadSeconds { get; private set; }

        public static void TryApply(Harmony h)
        {
            try
            {
                var load = AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LoadMapAndData));
                h.Patch(load, prefix: new HarmonyMethod(typeof(RaidStartPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                var waves = AccessTools.Method(typeof(LocalGame), nameof(LocalGame.smethod_8));
                h.Patch(waves, postfix: new HarmonyMethod(typeof(RaidStartPatch).GetMethod(nameof(WavesPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("Quiet replay raid: hooks installed.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Quiet replay raid hooks failed (replays still work, bots are switched off after load instead): " + e.Message); }
        }

        static void Prefix(ref RaidSettings ____raidSettings)
        {
            try
            {
                _loadStartedAt = Time.realtimeSinceStartup; LastWasQuiet = false;
                if (!Cfg.QuietRaid.Value || string.IsNullOrEmpty(ReplaySession.Pending)) return;
                var rs = ____raidSettings; if (rs == null) return;
                rs.WavesSettings.BotAmount = EBotAmount.NoBots;   // both are structs stored in fields, so they are edited in place
                rs.WavesSettings.IsBosses = false;
                rs.BotSettings.IsScavWars = false;
                rs.MetabolismDisabled = true;
                LastWasQuiet = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Quiet raid settings could not be applied: " + e.Message); }
        }

        // PMC waves are boss-type spawns in SPT, so "no bosses" alone leaves them in: drop the whole list for a quiet raid
        static void WavesPostfix(ref BossLocationSpawn[] __result)
        {
            try { if (LastWasQuiet) __result = new BossLocationSpawn[0]; } catch { }
        }

        /// <summary>Called once the raid world exists: logs how long loading took.</summary>
        public static void MarkReady()
        {
            if (_loadStartedAt <= 0f) return;
            LastLoadSeconds = Time.realtimeSinceStartup - _loadStartedAt; _loadStartedAt = 0f;
            Plugin.Log.LogInfo($"Raid world ready {LastLoadSeconds:F0}s after load start ({(LastWasQuiet ? "quiet replay raid" : "normal raid")}).");
        }
    }
}
