using System.IO;
using BepInEx;
using BepInEx.Logging;
using RaidReplay.Core;

namespace RaidReplay
{
    [BepInPlugin(Guid, "Raid Replay", Version)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.raidreplay.mod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ReplayLibrary Library;
        internal static bool FikaPresent => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.fika.core");

        void Awake()
        {
            Log = Logger;
            Cfg.Bind(Config);
            string dir = string.IsNullOrWhiteSpace(Cfg.ReplayFolder.Value)
                ? Path.Combine(Paths.BepInExRootPath, "RaidReplay", "replays") : Cfg.ReplayFolder.Value;
            Library = new ReplayLibrary(dir);
            ShotPatch.TryApply();
            gameObject.AddComponent<RaidRecorder>();
            gameObject.AddComponent<ReplaySession>();
            Logger.LogInfo($"Raid Replay {Version} ready. Replays: {dir}");
        }
    }
}
