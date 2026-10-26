using System;
using BepInEx.Configuration;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>Read by BepInEx ConfigurationManager (F12) through reflection - field names must stay as they are.</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? HideDefaultButton, HideSettingName, IsAdvanced, Browsable;
        public Action<ConfigEntryBase> CustomDrawer;
        public string DispName;
    }

    internal enum MinimapMode { Radar, TopDownCamera }
    internal enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>All user settings. They live in the F12 menu under the "Raid Replay ..." categories.</summary>
    internal static class Cfg
    {
        const string CList = "0. Raid Replay - Replays", CRec = "1. Raid Replay - Recording", CPlay = "2. Raid Replay - Playback",
                     CMap = "3. Raid Replay - Minimap", CBadge = "4. Raid Replay - Operator badge", CKeys = "5. Raid Replay - Hotkeys";

        public static ConfigEntry<string> ListEntry;
        public static ConfigEntry<bool> RecordEnabled;
        public static ConfigEntry<int> KeepLast, OthersHz, PovHz, MinRaidSeconds;
        public static ConfigEntry<string> ReplayFolder;

        public static ConfigEntry<bool> AllowSpectate, AllowFreeCam, ShowNameplates, ShowTracers, ShowTimeline, QuietRaid, LeaveAsExtracted;
        public static ConfigEntry<float> NameplateRange;

        public static ConfigEntry<bool> MinimapEnabled, MinimapRotate;
        public static ConfigEntry<MinimapMode> MinimapModeE;
        public static ConfigEntry<int> MinimapSize, MinimapFps;
        public static ConfigEntry<float> MinimapRange, MinimapOpacity;
        public static ConfigEntry<Corner> MinimapCorner;

        public static ConfigEntry<bool> BadgeEnabled, BadgeShowFace;
        public static ConfigEntry<Corner> BadgeCorner;
        public static ConfigEntry<float> BadgeScale;

        public static ConfigEntry<KeyboardShortcut> KeyPause, KeySeekBack, KeySeekFwd, KeySlower, KeyFaster,
            KeyFreeCam, KeyNextTarget, KeyBackToPov, KeyHud, KeyMinimap, KeyExit, KeyOpenCenter;

        public static void Bind(ConfigFile c)
        {
            ListEntry = c.Bind(CList, "Replay Center", "", new ConfigDescription("Replay list, recording, playback, minimap and hotkeys all live in the Replay Center.", null,
                new ConfigurationManagerAttributes { CustomDrawer = ReplayListDrawer.Draw, HideDefaultButton = true, HideSettingName = true, Order = 1000 }));

            RecordEnabled = c.Bind(CRec, "Record raids", true, Desc("Record every raid in the background.", 100));
            KeepLast = c.Bind(CRec, "Keep last N replays", 5, new ConfigDescription("Older unpinned replays are deleted automatically. Pinned replays are never deleted and do not count.",
                new AcceptableValueRange<int>(1, 500), new ConfigurationManagerAttributes { Order = 90 }));
            OthersHz = c.Bind(CRec, "Others sample rate (Hz)", 15, new ConfigDescription("Bots / other players. Higher = smoother, bigger files (about 130 KB per minute at 15 Hz).",
                new AcceptableValueRange<int>(5, 30), new ConfigurationManagerAttributes { Order = 80 }));
            PovHz = c.Bind(CRec, "Operator (your view) sample rate (Hz)", 60, new ConfigDescription("Your own camera. 60 keeps first-person smooth.",
                new AcceptableValueRange<int>(20, 120), new ConfigurationManagerAttributes { Order = 70 }));
            MinRaidSeconds = c.Bind(CRec, "Discard raids shorter than (s)", 30, new ConfigDescription("Skips accidental / instantly aborted raids.",
                new AcceptableValueRange<int>(0, 600), new ConfigurationManagerAttributes { Order = 60 }));
            ReplayFolder = c.Bind(CRec, "Replay folder (restart needed)", "", Desc("Empty = BepInEx/RaidReplay/replays", 50, advanced: true));

            QuietRaid = c.Bind(CPlay, "Quiet raid for replays", true, Desc("When a replay is queued, the next raid starts without bots, bosses, PMC waves and hunger/thirst.", 120));
            LeaveAsExtracted = c.Bind(CPlay, "Exit key also leaves the raid as extracted", false, Desc("Ends the raid as Survived so your gear is kept. It counts as a normal survived raid in your stats and quests.", 110));
            AllowSpectate = c.Bind(CPlay, "Allow watching other players", true, Desc("Switch the camera to any other recorded entity (first-person of that operator/bot).", 100));
            AllowFreeCam = c.Bind(CPlay, "Allow free camera", true, Desc("Detach the camera and fly around.", 90));
            ShowNameplates = c.Bind(CPlay, "Show nameplates", true, Desc("Nickname + distance above other entities.", 80));
            NameplateRange = c.Bind(CPlay, "Nameplate range (m)", 120f, new ConfigDescription("", new AcceptableValueRange<float>(10f, 500f), new ConfigurationManagerAttributes { Order = 70 }));
            ShowTracers = c.Bind(CPlay, "Show shot tracers", true, Desc("Short line for each recorded shot.", 60));
            ShowTimeline = c.Bind(CPlay, "Show timeline bar", true, Desc("Bottom bar with time / speed. Click it to seek.", 50));

            MinimapEnabled = c.Bind(CMap, "Enabled", true, Desc("Built-in minimap. Does not need Raid Review or any map images.", 100));
            MinimapModeE = c.Bind(CMap, "Mode", MinimapMode.Radar, Desc("Radar = dots on a dark panel (always works). TopDownCamera = live top-down render of the level (experimental, costs FPS).", 90));
            MinimapSize = c.Bind(CMap, "Size (px)", 220, new ConfigDescription("", new AcceptableValueRange<int>(120, 480), new ConfigurationManagerAttributes { Order = 80 }));
            MinimapRange = c.Bind(CMap, "Range (m)", 70f, new ConfigDescription("Radius shown around the camera.", new AcceptableValueRange<float>(20f, 400f), new ConfigurationManagerAttributes { Order = 70 }));
            MinimapRotate = c.Bind(CMap, "Rotate with view", true, Desc("Up = where the camera looks. Off = north up.", 60));
            MinimapOpacity = c.Bind(CMap, "Opacity", 0.85f, new ConfigDescription("", new AcceptableValueRange<float>(0.2f, 1f), new ConfigurationManagerAttributes { Order = 50 }));
            MinimapCorner = c.Bind(CMap, "Corner", Corner.TopRight, Desc("", 40));
            MinimapFps = c.Bind(CMap, "TopDownCamera refresh (fps)", 12, new ConfigDescription("", new AcceptableValueRange<int>(2, 30), new ConfigurationManagerAttributes { Order = 30, IsAdvanced = true }));

            BadgeEnabled = c.Bind(CBadge, "Show operator badge", true, Desc("Face + nickname plate of the operator you are watching, Tarkov style.", 100));
            BadgeShowFace = c.Bind(CBadge, "Show face picture", true, Desc("Renders a small portrait of the operator's head.", 90));
            BadgeCorner = c.Bind(CBadge, "Corner", Corner.BottomLeft, Desc("", 80));
            BadgeScale = c.Bind(CBadge, "Scale", 1f, new ConfigDescription("", new AcceptableValueRange<float>(0.6f, 2f), new ConfigurationManagerAttributes { Order = 70 }));

            KeyPause = Key(c, "Pause / resume", KeyCode.Space, 100);
            KeySeekBack = Key(c, "Seek back 10 s (Shift = 60 s)", KeyCode.LeftArrow, 90);
            KeySeekFwd = Key(c, "Seek forward 10 s (Shift = 60 s)", KeyCode.RightArrow, 80);
            KeySlower = Key(c, "Slower", KeyCode.Comma, 70);
            KeyFaster = Key(c, "Faster", KeyCode.Period, 60);
            KeyNextTarget = Key(c, "Watch next player / bot", KeyCode.C, 50);
            KeyBackToPov = Key(c, "Back to operator view", KeyCode.B, 45);
            KeyFreeCam = Key(c, "Toggle free camera", KeyCode.V, 40);
            KeyMinimap = Key(c, "Toggle minimap", KeyCode.M, 30);
            KeyHud = Key(c, "Toggle all replay HUD", KeyCode.Home, 20);
            KeyExit = Key(c, "Exit replay", KeyCode.End, 10);
            KeyOpenCenter = Key(c, "Open Replay Center", KeyCode.F10, 200);

            // Everything is edited in the Replay Center; F12 only shows the "open" button.
            foreach (var kv in c)
            {
                if (kv.Value == ListEntry) continue;
                foreach (var tag in kv.Value.Description.Tags)
                    if (tag is ConfigurationManagerAttributes a) a.Browsable = false;
            }
        }

        static ConfigEntry<KeyboardShortcut> Key(ConfigFile c, string name, KeyCode k, int order) =>
            c.Bind(CKeys, name, new KeyboardShortcut(k), Desc("", order));

        static ConfigDescription Desc(string text, int order, bool advanced = false) =>
            new ConfigDescription(text, null, new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced });
    }
}
