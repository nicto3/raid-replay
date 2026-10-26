// Stand-ins for the plugin classes that the EFT-facing files call, so those files can be compiled in isolation.
using BepInEx.Configuration;
namespace RaidReplay
{
    internal static class Plugin { public const string Guid = "x", Version = "1"; public static BepInEx.Logging.ManualLogSource Log; public static RaidReplay.Core.ReplayLibrary Library; public static bool FikaPresent; }
    internal static class Cfg { public static ConfigEntry<bool> RecordEnabled, QuietRaid; public static ConfigEntry<int> KeepLast, OthersHz, PovHz, MinRaidSeconds; }
    internal static class ReplaySession { public static bool Active; public static string Pending; }
    internal static class ReplayCenter { public static void Open() { } }
}
