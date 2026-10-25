using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>The replay list rendered inside the F12 settings window.</summary>
    internal static class ReplayListDrawer
    {
        static List<ReplayInfo> _cache = new List<ReplayInfo>();
        static float _nextRefresh;
        static string _confirmDelete; static float _confirmUntil;

        public static void Draw(ConfigEntryBase _)
        {
            if (Plugin.Library == null) { GUILayout.Label("Not initialised."); return; }
            if (Time.realtimeSinceStartup > _nextRefresh) { _cache = Plugin.Library.List(); _nextRefresh = Time.realtimeSinceStartup + 3f; }

            GUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(ReplaySession.Pending))
                GUILayout.Label("Queued: " + System.IO.Path.GetFileName(ReplaySession.Pending) + "  - deploy on that map to start (solo raid).");
            if (ReplaySession.Active) { if (GUILayout.Button("Exit current replay")) ReplaySession.RequestExit(); }

            GUILayout.Label($"{_cache.Count} replay(s) in {Plugin.Library.Directory}");
            foreach (var r in _cache)
            {
                GUILayout.BeginHorizontal();
                string when = r.Header.StartedUtc.ToLocalTime().ToString("dd.MM HH:mm");
                string dur = TimeSpan.FromMilliseconds(r.DurationMs).ToString(r.DurationMs >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
                GUILayout.Label($"{(r.Pinned ? "[pin] " : "")}{when}  {r.Header.Map}  {dur}  {r.SizeBytes / 1048576f:F1} MB  ({r.Header.PovNickname})", GUILayout.MinWidth(320));
                if (GUILayout.Button("Play", GUILayout.Width(60))) { ReplaySession.Queue(r.Path); }
                if (GUILayout.Button(r.Pinned ? "Unpin" : "Pin", GUILayout.Width(60))) { Plugin.Library.SetPinned(r.Path, !r.Pinned); _nextRefresh = 0; }
                bool confirming = _confirmDelete == r.Path && Time.realtimeSinceStartup < _confirmUntil;
                if (GUILayout.Button(confirming ? "Sure?" : "Delete", GUILayout.Width(60)))
                {
                    if (confirming) { Plugin.Library.Delete(r.Path); _confirmDelete = null; _nextRefresh = 0; }
                    else { _confirmDelete = r.Path; _confirmUntil = Time.realtimeSinceStartup + 3f; }
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Refresh")) _nextRefresh = 0;
            GUILayout.EndVertical();
        }
    }
}
