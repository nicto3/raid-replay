using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using RaidReplay.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidReplay
{
    /// <summary>
    /// The one place for everything replay-related: list of recordings, recording / playback / minimap / badge settings, hotkeys, status.
    /// Opens from the native Settings screen (REPLAYS button), from F12, or with the hotkey (default F10).
    /// </summary>
    internal sealed class ReplayCenter : MonoBehaviour
    {
        public static bool Visible { get; private set; }
        static ReplayCenter _inst;
        static readonly string[] Tabs = { "REPLAYS", "RECORDING", "PLAYBACK", "MINIMAP", "OPERATOR", "HOTKEYS", "ABOUT" };

        int _tab; float _scroll, _fade, _refreshAt, _confirmUntil, _ry;
        List<ReplayInfo> _list = new List<ReplayInfo>();
        string _confirm;
        ConfigEntry<KeyboardShortcut> _rebind;
        Rect _c;
        CursorLockMode _prevLock; bool _prevVisible, _eventSystemWas, _eventSystemTouched;

        void Awake() { _inst = this; }

        public static void Open() { if (_inst != null) _inst.Show(true); }
        public static void Close() { if (_inst != null) _inst.Show(false); }

        void Show(bool on)
        {
            if (on == Visible) return;
            Visible = on;
            if (on)
            {
                _list = Plugin.Library != null ? Plugin.Library.List() : _list; _refreshAt = Time.realtimeSinceStartup + 4f;
                _tab = 0; _scroll = 0; _rebind = null; _confirm = null;
                _prevLock = Cursor.lockState; _prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                var es = EventSystem.current;
                if (es != null) { _eventSystemWas = es.enabled; es.enabled = false; _eventSystemTouched = true; }   // IMGUI does not stop clicks reaching the menu below, so pause the uGUI input while we are open
            }
            else
            {
                Cursor.lockState = _prevLock; Cursor.visible = _prevVisible;
                var es = EventSystem.current;
                if (es != null && _eventSystemTouched) es.enabled = _eventSystemWas;
                _eventSystemTouched = false; _rebind = null;
            }
        }

        void Update()
        {
            if (_rebind == null && Cfg.KeyOpenCenter != null && Cfg.KeyOpenCenter.Value.IsDown()) Show(!Visible);
            _fade = Mathf.MoveTowards(_fade, Visible ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            if (Visible && Time.realtimeSinceStartup > _refreshAt && _tab == 0) { _list = Plugin.Library.List(); _refreshAt = Time.realtimeSinceStartup + 3f; }
        }

        void OnDestroy() { if (Visible) Show(false); }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!Visible && _fade <= 0.01f) return;
            GUI.depth = -100;
            var e = Event.current;
            if (_rebind != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode != KeyCode.Escape && e.keyCode != KeyCode.None) _rebind.Value = new KeyboardShortcut(e.keyCode);
                _rebind = null; e.Use(); return;
            }
            if (Visible && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Show(false); e.Use(); return; }

            Ui.Begin();
            Ui.Alpha = _fade;
            try { Draw(); }
            finally { Ui.Alpha = 1f; Ui.End(); }
            if (Visible && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel)) e.Use();
        }

        void Draw()
        {
            var vs = Ui.Virtual;
            Ui.Fill(new Rect(0, 0, vs.x, vs.y), Ui.Dim);
            float W = Mathf.Min(1280f, vs.x - 80f), H = 880f;
            var win = new Rect((vs.x - W) * 0.5f, (1080f - H) * 0.5f + (1f - _fade) * 20f, W, H);
            Ui.Panel(win, Ui.Window);

            // header
            var hd = new Rect(win.x, win.y, W, 76f);
            Ui.Fill(hd, Ui.Header); Ui.Fill(new Rect(hd.x, hd.yMax - 2f, W, 2f), Ui.Accent);
            Ui.Label(new Rect(hd.x + 32f, hd.y + 8f, 520f, 40f), "RAID REPLAY", 32, Ui.Text, TextAnchor.MiddleLeft, true);
            Ui.Label(new Rect(hd.x + 34f, hd.y + 46f, 760f, 22f), $"v{Plugin.Version}   ·   {(Plugin.FikaPresent ? "Fika detected" : "SPT")}   ·   {(Plugin.Library != null ? Plugin.Library.Directory : "")}", 13, Ui.Muted);
            DrawStatus(new Rect(hd.xMax - 460f, hd.y + 20f, 380f, 36f));
            if (Ui.Button(new Rect(hd.xMax - 64f, hd.y + 16f, 44f, 44f), "X", Ui.Kind.Normal, true, 20)) Show(false);

            // tabs
            float tx = win.x + 32f, ty = hd.yMax + 14f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float tw = 44f + Tabs[i].Length * 12f;
                var r = new Rect(tx, ty, tw, 44f);
                bool act = i == _tab, hov = Ui.Hover(r);
                Ui.Label(r, Tabs[i], 18, act ? Ui.Accent : hov ? Ui.Text : Ui.Muted, TextAnchor.MiddleCenter, true);
                if (act) Ui.Fill(new Rect(r.x + 8f, r.yMax - 3f, r.width - 16f, 3f), Ui.Accent);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _tab = i; _scroll = 0; _rebind = null; if (i == 0) _refreshAt = 0; }
                tx += tw + 8f;
            }
            Ui.Fill(new Rect(win.x + 32f, ty + 46f, W - 64f, 1f), Ui.Border);

            _c = new Rect(win.x + 32f, ty + 62f, W - 64f, win.yMax - (ty + 62f) - 76f);
            _ry = _c.y;
            switch (_tab)
            {
                case 0: DrawReplays(_c); break;
                case 1: DrawRecording(); break;
                case 2: DrawPlayback(); break;
                case 3: DrawMinimap(); break;
                case 4: DrawOperator(); break;
                case 5: DrawHotkeys(); break;
                default: DrawAbout(); break;
            }

            // footer
            Ui.Fill(new Rect(win.x, win.yMax - 64f, W, 1f), Ui.Border);
            Ui.Label(new Rect(win.x + 32f, win.yMax - 56f, W - 260f, 48f), Hint(), 14, Ui.Muted);
            if (Ui.Button(new Rect(win.xMax - 172f, win.yMax - 54f, 140f, 40f), "CLOSE", Ui.Kind.Primary)) Show(false);
        }

        string Hint()
        {
            switch (_tab)
            {
                case 0: return ReplaySession.Active ? "SWITCH changes to another replay of this map instantly — no reload. Other maps need a new raid." : "PLAY queues the replay: start a solo raid on the same map and it takes over. Pinned replays are never deleted.";
                case 1: return "Recording runs in the background. Changes apply from the next raid.";
                case 5: return "Click a key, then press the new one (Esc cancels).";
                default: return "Changes are saved immediately.";
            }
        }

        void DrawStatus(Rect r)
        {
            string rec = RaidRecorder.Instance != null ? RaidRecorder.Instance.StatusLine : "";
            if (ReplaySession.Active) { Ui.Chip(r, "WATCHING A REPLAY", Ui.Accent, 15); return; }
            if (!string.IsNullOrEmpty(rec))
            {
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
                Ui.Fill(new Rect(r.x, r.center.y - 6f, 12f, 12f), new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, pulse));
                Ui.Label(new Rect(r.x + 22f, r.y, r.width - 22f, r.height), rec, 15, Ui.Text, TextAnchor.MiddleLeft, true);
            }
            else Ui.Label(r, Cfg.RecordEnabled.Value ? "READY TO RECORD" : "RECORDING OFF", 15, Cfg.RecordEnabled.Value ? Ui.Good : Ui.Muted, TextAnchor.MiddleRight, true);
        }

        // ------------------------------------------------------------------ replays tab

        static string Fmt(double ms) => TimeSpan.FromMilliseconds(ms).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");

        void DrawReplays(Rect c)
        {
            Ui.Label(new Rect(c.x, c.y, 700f, 34f), $"{_list.Count} replay{(_list.Count == 1 ? "" : "s")}   ·   keeping the last {Cfg.KeepLast.Value} unpinned", 16, Ui.Muted);
            if (Ui.Button(new Rect(c.xMax - 262f, c.y, 124f, 34f), "REFRESH", Ui.Kind.Normal, true, 15)) _refreshAt = 0f;
            if (Ui.Button(new Rect(c.xMax - 130f, c.y, 130f, 34f), "OPEN FOLDER", Ui.Kind.Normal, true, 15)) OpenFolder();
            float y = c.y + 46f;

            if (!string.IsNullOrEmpty(ReplaySession.Pending))
            {
                var b = new Rect(c.x, y, c.width, 52f);
                Ui.Fill(b, Ui.AccentDark); Ui.Frame(b, Ui.Accent);
                string name = System.IO.Path.GetFileNameWithoutExtension(ReplaySession.Pending);
                Ui.Label(new Rect(b.x + 18f, b.y, b.width - 200f, b.height), "QUEUED   " + name + "   —   deploy on the same map in a solo raid", 16, Ui.Text, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(b.xMax - 150f, b.y + 8f, 134f, 36f), "CANCEL", Ui.Kind.Normal, true, 15)) ReplaySession.Cancel();
                y += 62f;
            }

            var view = new Rect(c.x, y, c.width, c.yMax - y);
            const float rowH = 96f, gap = 10f;
            float total = _list.Count * (rowH + gap);
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
                _scroll += Event.current.delta.y * 36f;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, total - view.height));

            if (_list.Count == 0)
            {
                Ui.Label(view, "No replays yet.\nPlay a raid — recording starts automatically and the file appears here when you leave.", 20, Ui.Muted, TextAnchor.MiddleCenter);
                return;
            }

            GUI.BeginGroup(view);
            for (int i = 0; i < _list.Count; i++)
            {
                var r = new Rect(0f, i * (rowH + gap) - _scroll, view.width - 16f, rowH);
                if (r.yMax < 0f || r.y > view.height) continue;
                DrawReplayRow(r, _list[i]);
            }
            GUI.EndGroup();

            if (total > view.height)
            {
                float th = Mathf.Max(40f, view.height * view.height / total);
                float ty = view.y + (view.height - th) * (_scroll / (total - view.height));
                Ui.Fill(new Rect(view.xMax - 6f, view.y, 4f, view.height), Ui.Track);
                Ui.Fill(new Rect(view.xMax - 6f, ty, 4f, th), Ui.Accent);
            }
        }

        void DrawReplayRow(Rect r, ReplayInfo info)
        {
            bool queued = ReplaySession.Pending == info.Path;
            bool playing = ReplaySession.Active && ReplaySession.CurrentPath == info.Path;
            Ui.Fill(r, Ui.Hover(r) ? Ui.RowHover : Ui.Row); Ui.Frame(r, queued ? Ui.Accent : new Color(0.22f, 0.23f, 0.22f));
            Ui.Fill(new Rect(r.x, r.y, 5f, r.height), info.Pinned ? Ui.Good : Ui.Accent);

            Ui.Label(new Rect(r.x + 26f, r.y + 10f, 520f, 36f), MapIds.DisplayName(info.Header.Map).ToUpperInvariant(), 27, Ui.Text, TextAnchor.MiddleLeft, true);
            var when = info.Header.StartedUtc.ToLocalTime();
            Ui.Label(new Rect(r.x + 26f, r.y + 50f, 640f, 22f), $"{when:dd MMM yyyy  ·  HH:mm}     {Fmt(info.DurationMs)}     {info.SizeBytes / 1048576f:F1} MB     operator: {info.Header.PovNickname}", 15, Ui.Muted);
            Ui.Label(new Rect(r.x + 26f, r.y + 70f, 640f, 20f), System.IO.Path.GetFileName(info.Path), 11, new Color(0.4f, 0.42f, 0.4f));

            var del = new Rect(r.xMax - 126f, r.y + 25f, 108f, 46f);
            var pin = new Rect(del.x - 100f, del.y, 90f, 46f);
            var play = new Rect(pin.x - 146f, del.y, 136f, 46f);
            float cx = play.x - 12f;
            if (info.Pinned) { cx -= 84f; Ui.Chip(new Rect(cx, r.y + 36f, 78f, 24f), "PINNED", Ui.Good); }
            if (queued) { cx -= 84f; Ui.Chip(new Rect(cx, r.y + 36f, 78f, 24f), "QUEUED", Ui.Accent); }
            if (playing) { cx -= 92f; Ui.Chip(new Rect(cx, r.y + 36f, 86f, 24f), "PLAYING", Ui.Good); }
            if (info.Header.GameInfo != null && info.Header.GameInfo.Contains("Fika")) { cx -= 66f; Ui.Chip(new Rect(cx, r.y + 36f, 60f, 24f), "FIKA", Ui.Muted); }

            string playText = playing ? "PLAYING" : queued ? "QUEUED" : ReplaySession.Active ? "▶  SWITCH" : "▶  PLAY";
            if (Ui.Button(play, playText, Ui.Kind.Primary, !queued && !playing, 18)) ReplaySession.Queue(info.Path);
            if (Ui.Button(pin, info.Pinned ? "UNPIN" : "PIN", Ui.Kind.Normal, true, 16)) { Plugin.Library.SetPinned(info.Path, !info.Pinned); _refreshAt = 0f; }
            bool confirming = _confirm == info.Path && Time.realtimeSinceStartup < _confirmUntil;
            if (Ui.Button(del, confirming ? "SURE?" : "DELETE", Ui.Kind.Danger, true, 16))
            {
                if (confirming) { Plugin.Library.Delete(info.Path); _confirm = null; _refreshAt = 0f; }
                else { _confirm = info.Path; _confirmUntil = Time.realtimeSinceStartup + 3f; }
            }
        }

        static void OpenFolder()
        {
            try { System.Diagnostics.Process.Start(Plugin.Library.Directory); }
            catch (Exception e) { ReplaySession.Toast("Could not open the folder: " + e.Message); }
        }

        // ------------------------------------------------------------------ settings tabs

        Rect BeginRow(string title, string desc, float h = 64f)
        {
            var r = new Rect(_c.x, _ry, _c.width, h);
            Ui.Fill(r, Ui.Row); Ui.Frame(r, new Color(0.2f, 0.21f, 0.2f));
            Ui.Label(new Rect(r.x + 22f, r.y + 8f, r.width * 0.5f, 26f), title, 19, Ui.Text, TextAnchor.MiddleLeft, true);
            if (!string.IsNullOrEmpty(desc)) Ui.Label(new Rect(r.x + 22f, r.y + 34f, r.width * 0.56f, 22f), desc, 13, Ui.Muted);
            _ry += h + 8f;
            return r;
        }

        void ToggleRow(ConfigEntry<bool> e, string title, string desc)
        {
            var r = BeginRow(title, desc);
            if (Ui.Toggle(new Rect(r.xMax - 92f, r.y + 16f, 68f, 32f), e.Value)) e.Value = !e.Value;
            Ui.Label(new Rect(r.xMax - 170f, r.y, 70f, r.height), e.Value ? "ON" : "OFF", 15, e.Value ? Ui.Accent : Ui.Muted, TextAnchor.MiddleRight, true);
        }

        void IntRow(ConfigEntry<int> e, string title, string desc, string unit = "")
        {
            var r = BeginRow(title, desc);
            var rng = e.Description.AcceptableValues as AcceptableValueRange<int>;
            int min = rng != null ? rng.MinValue : 0, max = rng != null ? rng.MaxValue : 100;
            float nv = Ui.Slider(new Rect(r.xMax - 470f, r.y + 16f, 320f, 32f), e.Value, min, max);
            int iv = Mathf.RoundToInt(nv); if (iv != e.Value) e.Value = iv;
            Ui.Label(new Rect(r.xMax - 138f, r.y, 116f, r.height), e.Value + unit, 21, Ui.Accent, TextAnchor.MiddleRight, true);
        }

        void FloatRow(ConfigEntry<float> e, string title, string desc, string unit, bool percent = false)
        {
            var r = BeginRow(title, desc);
            var rng = e.Description.AcceptableValues as AcceptableValueRange<float>;
            float min = rng != null ? rng.MinValue : 0f, max = rng != null ? rng.MaxValue : 1f;
            float nv = Ui.Slider(new Rect(r.xMax - 470f, r.y + 16f, 320f, 32f), e.Value, min, max);
            nv = percent ? Mathf.Round(nv * 100f) / 100f : Mathf.Round(nv * 10f) / 10f;
            if (!Mathf.Approximately(nv, e.Value)) e.Value = nv;
            Ui.Label(new Rect(r.xMax - 138f, r.y, 116f, r.height), percent ? Mathf.RoundToInt(e.Value * 100f) + unit : e.Value.ToString("0.#") + unit, 21, Ui.Accent, TextAnchor.MiddleRight, true);
        }

        void EnumRow<T>(ConfigEntry<T> e, string title, string desc) where T : struct, Enum
        {
            var r = BeginRow(title, desc);
            var names = Enum.GetNames(typeof(T)); var vals = (T[])Enum.GetValues(typeof(T));
            float sw = names.Length > 3 ? 112f : 150f; float x = r.xMax - 22f - sw * names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = EqualityComparerEq(vals[i], e.Value);
                if (Ui.Button(new Rect(x + i * sw, r.y + 14f, sw - 6f, 36f), Pretty(names[i]), sel ? Ui.Kind.Primary : Ui.Kind.Normal, true, 14)) e.Value = vals[i];
            }
        }

        static bool EqualityComparerEq<T>(T a, T b) => System.Collections.Generic.EqualityComparer<T>.Default.Equals(a, b);
        static string Pretty(string s) => Regex.Replace(s, "([a-z])([A-Z])", "$1 $2").ToUpperInvariant();

        void DrawRecording()
        {
            ToggleRow(Cfg.RecordEnabled, "Record raids", "Every raid is written in the background while you play.");
            IntRow(Cfg.KeepLast, "Replays to keep", "Older unpinned replays are deleted automatically. Pinned ones never are.");
            IntRow(Cfg.OthersHz, "Players & bots sample rate", "Updates per second for everyone except you.", " Hz");
            IntRow(Cfg.PovHz, "Your view sample rate", "Your own camera — keep high for smooth first person.", " Hz");
            IntRow(Cfg.MinRaidSeconds, "Ignore raids shorter than", "Skips aborted raids so they do not push real ones out.", " s");
            float kbMin = 5f * (24f * Cfg.OthersHz.Value + Cfg.PovHz.Value) * 60f / 1024f;
            var r = BeginRow("Estimated size", $"About {kbMin * 25f / 1024f:F1} MB for a 25-minute raid with 24 bots/players; all {Cfg.KeepLast.Value} replays ≈ {kbMin * 25f / 1024f * Cfg.KeepLast.Value:F0} MB.");
            Ui.Label(new Rect(r.xMax - 260f, r.y, 238f, r.height), $"{kbMin:F0} KB / min", 21, Ui.Good, TextAnchor.MiddleRight, true);
        }

        void DrawPlayback()
        {
            ToggleRow(Cfg.QuietRaid, "Quiet raid for replays", "The raid you deploy into for a replay starts with no bots, bosses or PMC waves, and no hunger/thirst. Loads faster, nothing can kill you.");
            ToggleRow(Cfg.LeaveAsExtracted, "Exit key also extracts", "Ends the raid as Survived so gear is kept. Counts in your stats/quests. Not available in Fika raids.");
            ToggleRow(Cfg.AllowSpectate, "Watch other players", "Switch the camera to any recorded player or bot (first person of that operator).");
            ToggleRow(Cfg.AllowFreeCam, "Free camera", "Detach the camera and fly around the raid.");
            ToggleRow(Cfg.ShowNameplates, "Nameplates", "Nickname and distance above everyone.");
            FloatRow(Cfg.NameplateRange, "Nameplate range", "How far nameplates are drawn.", " m");
            ToggleRow(Cfg.ShowTracers, "Shot tracers & muzzle flash", "A short line and a flash for every recorded shot.");
            ToggleRow(Cfg.ShowTimeline, "Timeline bar", "Bottom bar with time, speed and kill markers. Hold Alt to click it.");
        }

        void DrawMinimap()
        {
            ToggleRow(Cfg.MinimapEnabled, "Minimap", "Built in — needs no map images and no other mod.");
            EnumRow(Cfg.MinimapModeE, "Mode", "Radar = dots on a dark panel. Top-down camera = live view of the level (experimental, costs FPS).");
            IntRow(Cfg.MinimapSize, "Size", "", " px");
            FloatRow(Cfg.MinimapRange, "Range", "Radius around the camera.", " m");
            ToggleRow(Cfg.MinimapRotate, "Rotate with view", "Up is where the camera looks. Off = north is up.");
            FloatRow(Cfg.MinimapOpacity, "Opacity", "", "%", true);
            EnumRow(Cfg.MinimapCorner, "Corner", "");
            IntRow(Cfg.MinimapFps, "Top-down refresh", "Only used by the top-down camera mode.", " fps");
        }

        void DrawOperator()
        {
            ToggleRow(Cfg.BadgeEnabled, "Operator badge", "Tarkov-style plate with the operator's portrait, nickname, level and side.");
            ToggleRow(Cfg.BadgeShowFace, "Portrait", "Live picture of the operator's head next to the nickname.");
            EnumRow(Cfg.BadgeCorner, "Corner", "");
            FloatRow(Cfg.BadgeScale, "Scale", "", "x");
        }

        void DrawHotkeys()
        {
            var keys = new[]
            {
                ("Open Replay Center", Cfg.KeyOpenCenter), ("Pause / resume", Cfg.KeyPause), ("Seek back (Shift = 60 s)", Cfg.KeySeekBack),
                ("Seek forward (Shift = 60 s)", Cfg.KeySeekFwd), ("Slower", Cfg.KeySlower), ("Faster", Cfg.KeyFaster),
                ("Watch next player / bot", Cfg.KeyNextTarget), ("Back to operator view", Cfg.KeyBackToPov), ("Toggle free camera", Cfg.KeyFreeCam),
                ("Toggle minimap", Cfg.KeyMinimap), ("Hide / show replay HUD", Cfg.KeyHud), ("Exit replay", Cfg.KeyExit)
            };
            float colW = (_c.width - 16f) * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                var r = new Rect(_c.x + (i % 2) * (colW + 16f), _c.y + (i / 2) * 64f, colW, 56f);
                Ui.Fill(r, Ui.Row); Ui.Frame(r, new Color(0.2f, 0.21f, 0.2f));
                Ui.Label(new Rect(r.x + 20f, r.y, r.width - 200f, r.height), keys[i].Item1, 17, Ui.Text, TextAnchor.MiddleLeft, true);
                bool waiting = _rebind == keys[i].Item2;
                var btn = new Rect(r.xMax - 176f, r.y + 10f, 156f, 36f);
                if (Ui.Button(btn, waiting ? "PRESS A KEY…" : keys[i].Item2.Value.MainKey.ToString().ToUpperInvariant(), waiting ? Ui.Kind.Primary : Ui.Kind.Normal, true, 15)) _rebind = waiting ? null : keys[i].Item2;
            }
            Ui.Label(new Rect(_c.x, _c.y + 6f * 64f + 6f, _c.width, 60f),
                "Free camera: WASD move · Q/E down/up · Shift fast · mouse wheel changes speed.     Hold Alt: get a mouse cursor to click the timeline.", 14, Ui.Muted);
        }

        void DrawAbout()
        {
            string[] on = { "Players & bots: position, view direction, stance, sprint, aim, lean, alive/dead", "Your own camera exactly as you saw it (bob, ADS, FOV, roll)", "Shots (tracer + muzzle flash) and kills", "Doors and containers: open / close / lock / unlock", "Lamps and lights (power on/off)" };
            string[] off = { "Loot on the ground and in containers", "Grenades, smoke, windows breaking", "Real character models, gear and hand animations (stylised bodies are used)", "Weather and time of day" };
            float y = _c.y;
            Ui.Label(new Rect(_c.x, y, 600f, 30f), "SYNCED IN THE REPLAY", 17, Ui.Good, TextAnchor.MiddleLeft, true); y += 34f;
            foreach (var s in on) { Ui.Label(new Rect(_c.x + 14f, y, _c.width, 26f), "✔  " + s, 16, Ui.Text); y += 28f; }
            y += 14f;
            Ui.Label(new Rect(_c.x, y, 600f, 30f), "NOT YET", 17, Ui.Bad, TextAnchor.MiddleLeft, true); y += 34f;
            foreach (var s in off) { Ui.Label(new Rect(_c.x + 14f, y, _c.width, 26f), "✘  " + s, 16, Ui.Muted); y += 28f; }
            y += 20f;
            Ui.Label(new Rect(_c.x, y, _c.width, 26f), $"Plugin {Plugin.Version}   ·   {(Plugin.FikaPresent ? "Fika present: recording works in co-op, playback is solo only" : "Fika not detected")}", 15, Ui.Muted); y += 26f;
            Ui.Label(new Rect(_c.x, y, _c.width, 26f), "Replay folder: " + (Plugin.Library != null ? Plugin.Library.Directory : ""), 15, Ui.Muted); y += 26f;
            if (RaidStartPatch.LastLoadSeconds > 0f)
                Ui.Label(new Rect(_c.x, y, _c.width, 26f), $"Last raid world loaded in {RaidStartPatch.LastLoadSeconds:F0} s ({(RaidStartPatch.LastWasQuiet ? "quiet replay raid" : "normal raid")})", 15, Ui.Accent);
        }
    }
}
