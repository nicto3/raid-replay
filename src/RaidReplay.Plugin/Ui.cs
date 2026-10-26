using System;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// Tiny IMGUI toolkit for the Replay Center. Everything is laid out in a virtual 1080-pixel-high screen and scaled to the real
    /// resolution, so it looks the same at 1080p / 1440p / 4K. Colours follow Tarkov's dark-grey + khaki UI.
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.62f),
            Window = new Color(0.075f, 0.078f, 0.078f, 0.985f), Header = new Color(0.105f, 0.108f, 0.106f, 1f),
            Row = new Color(0.11f, 0.113f, 0.112f, 1f), RowHover = new Color(0.145f, 0.15f, 0.147f, 1f),
            Border = new Color(0.30f, 0.31f, 0.30f, 1f), Text = new Color(0.89f, 0.88f, 0.82f, 1f), Muted = new Color(0.58f, 0.60f, 0.58f, 1f),
            Accent = new Color(0.77f, 0.72f, 0.56f, 1f), AccentDark = new Color(0.34f, 0.32f, 0.23f, 1f),
            Good = new Color(0.55f, 0.72f, 0.4f, 1f), Bad = new Color(0.86f, 0.32f, 0.28f, 1f), Track = new Color(0.19f, 0.2f, 0.2f, 1f);

        public static float Alpha = 1f;
        static Texture2D _white; static Font _font; static bool _ready;
        static Matrix4x4 _saved;
        static GUIStyle _label;
        static int _slideId = -1;

        public static float Scale => Mathf.Max(0.5f, Screen.height / 1080f);
        public static Vector2 Virtual => new Vector2(Screen.width / Scale, 1080f);

        static void Ensure()
        {
            if (_ready) return;
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0) { _font = f; break; }
            _label = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, richText = false, padding = new RectOffset(0, 0, 0, 0) };
            if (_font != null) _label.font = _font;
            _ready = true;
        }

        public static void Begin() { Ensure(); _saved = GUI.matrix; GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f)); }
        public static void End() { GUI.matrix = _saved; }

        static Color A(Color c) { c.a *= Alpha; return c; }
        public static bool Hover(Rect r) => r.Contains(Event.current.mousePosition);

        public static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = A(c); GUI.DrawTexture(r, _white); GUI.color = o; }
        public static void Frame(Rect r, Color c, float t = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        public static void Panel(Rect r, Color bg) { Fill(r, bg); Frame(r, Border); }

        public static void Label(Rect r, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
        {
            _label.fontSize = size; _label.alignment = a; _label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            var o = GUI.color; GUI.color = Color.white; _label.normal.textColor = A(c);
            GUI.Label(r, s, _label); GUI.color = o;
        }

        public enum Kind { Normal, Primary, Danger }

        /// <summary>Flat Tarkov-style button. Returns true on click.</summary>
        public static bool Button(Rect r, string text, Kind kind = Kind.Normal, bool enabled = true, int size = 17)
        {
            bool hover = enabled && Hover(r);
            Color bg = kind == Kind.Primary ? (hover ? new Color(0.86f, 0.81f, 0.64f) : Accent)
                     : kind == Kind.Danger ? (hover ? new Color(0.45f, 0.17f, 0.15f) : new Color(0.27f, 0.12f, 0.11f))
                     : hover ? new Color(0.22f, 0.23f, 0.22f) : new Color(0.15f, 0.155f, 0.153f);
            Color fg = kind == Kind.Primary ? new Color(0.1f, 0.1f, 0.08f) : Text;
            if (!enabled) { bg = new Color(0.12f, 0.12f, 0.12f); fg = new Color(0.4f, 0.4f, 0.4f); }
            Fill(r, bg); Frame(r, kind == Kind.Primary ? Accent : Border);
            Label(r, text, size, fg, TextAnchor.MiddleCenter, true);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static bool Toggle(Rect r, bool on)
        {
            bool hover = Hover(r);
            Fill(r, on ? AccentDark : Track); Frame(r, hover ? Accent : Border);
            float k = r.height - 8f; float x = on ? r.xMax - k - 4f : r.x + 4f;
            Fill(new Rect(x, r.y + 4f, k, k), on ? Accent : Muted);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Horizontal slider; returns the new value (== v when untouched).</summary>
        public static float Slider(Rect r, float v, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            var track = new Rect(r.x, r.center.y - 3f, r.width, 6f);
            float t = Mathf.InverseLerp(min, max, v);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; _slideId = id; e.Use(); }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && GUIUtility.hotControl == id && _slideId == id)
            {
                v = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - r.x) / r.width)); t = Mathf.InverseLerp(min, max, v); e.Use();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; _slideId = -1; e.Use(); }
            Fill(track, Track); Fill(new Rect(track.x, track.y, track.width * t, track.height), Accent);
            var thumb = new Rect(r.x + r.width * t - 7f, r.center.y - 11f, 14f, 22f);
            Fill(thumb, GUIUtility.hotControl == id || Hover(r) ? Text : Accent); Frame(thumb, new Color(0.1f, 0.1f, 0.1f));
            return v;
        }

        public static void Chip(Rect r, string text, Color c, int size = 13)
        {
            Fill(r, new Color(c.r * 0.28f, c.g * 0.28f, c.b * 0.28f, 1f)); Frame(r, c * 0.8f);
            Label(r, text, size, c, TextAnchor.MiddleCenter, true);
        }
    }
}
