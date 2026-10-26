using BepInEx.Configuration;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>What F12 shows for this mod: one button. Everything else is in the Replay Center.</summary>
    internal static class ReplayListDrawer
    {
        public static void Draw(ConfigEntryBase _)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("All replay features - list, recording, playback, minimap, operator badge, hotkeys - are in the Replay Center.");
            if (GUILayout.Button("OPEN REPLAY CENTER", GUILayout.Height(34))) ReplayCenter.Open();
            GUILayout.Label("Shortcut: " + (Cfg.KeyOpenCenter != null ? Cfg.KeyOpenCenter.Value.ToString() : "F10") + "   |   also in the game's Settings screen (REPLAYS button)");
            GUILayout.EndVertical();
        }
    }
}
