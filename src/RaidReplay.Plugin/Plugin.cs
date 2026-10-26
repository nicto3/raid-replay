using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RaidReplay.Core;

namespace RaidReplay
{
    [BepInPlugin(Guid, "Raid Replay", Version)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.raidreplay.mod";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;
        internal static ReplayLibrary Library;
        internal static bool FikaPresent => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.fika.core");

        void Awake()
        {
            Log = Logger;
            Cfg.Bind(Config);
            string dir = string.IsNullOrWhiteSpace(Cfg.ReplayFolder.Value)
                ? Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(BaseUnityPlugin).Assembly.Location), "..")), "RaidReplay", "replays") : Cfg.ReplayFolder.Value;
            Library = new ReplayLibrary(dir);
            ShotPatch.TryApply();
            RaidStartPatch.TryApply(new Harmony(Guid + ".raid"));
            NativeSettingsHook.TryApply(new Harmony(Guid + ".ui"));
            gameObject.AddComponent<RaidRecorder>();
            gameObject.AddComponent<ReplaySession>();
            gameObject.AddComponent<ReplayCenter>();
            Logger.LogInfo($"Raid Replay {Version} ready. Replays: {dir}");
        }
    }
}
