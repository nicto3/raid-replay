using System;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>On-screen elements: timeline, operator badge, minimap, nameplates, kill feed. Plain IMGUI, Tarkov-ish colours.</summary>
    internal sealed partial class ReplaySession
    {
        static Texture2D _white; static Font _bender; static bool _stylesReady;
        static GUIStyle _sBig, _sMid, _sSmall, _sCenter;
        static readonly Color Bg = new Color(0.08f, 0.085f, 0.085f, 0.92f), Border = new Color(0.31f, 0.32f, 0.31f, 1f),
                              TextC = new Color(0.88f, 0.87f, 0.80f, 1f), Dim = new Color(0.58f, 0.60f, 0.58f, 1f);

        static void EnsureStyles()
        {
            if (_stylesReady) return;
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0) { _bender = f; break; } // Tarkov's UI font, when loaded
            GUIStyle Mk(int size, FontStyle fs, TextAnchor a, Color c)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = false, clipping = TextClipping.Overflow, richText = false };
                s.normal.textColor = c; if (_bender != null) s.font = _bender; return s;
            }
            _sBig = Mk(20, FontStyle.Bold, TextAnchor.MiddleLeft, TextC);
            _sMid = Mk(13, FontStyle.Normal, TextAnchor.MiddleLeft, TextC);
            _sSmall = Mk(11, FontStyle.Normal, TextAnchor.MiddleLeft, Dim);
            _sCenter = Mk(13, FontStyle.Bold, TextAnchor.MiddleCenter, TextC);
            _stylesReady = true;
        }

        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        static void Frame(Rect r, Color c, float t = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        static void Panel(Rect r) { Fill(r, Bg); Frame(r, Border); }
        static void Text(Rect r, string s, GUIStyle st, Color c) { var o = st.normal.textColor; st.normal.textColor = c; GUI.Label(r, s, st); st.normal.textColor = o; }

        static Rect Place(Corner c, float w, float h, float m = 16f)
        {
            float x = (c == Corner.TopLeft || c == Corner.BottomLeft) ? m : Screen.width - w - m;
            float y = (c == Corner.TopLeft || c == Corner.TopRight) ? m : Screen.height - h - m;
            return new Rect(x, y, w, h);
        }

        static Color SideColor(EntityInfo i) =>
            i == null ? Dim : i.Side == EntitySide.Usec ? new Color(0.36f, 0.58f, 0.95f) : i.Side == EntitySide.Bear ? new Color(0.9f, 0.32f, 0.27f) : new Color(0.95f, 0.72f, 0.3f);

        int SubjectIdx() => _mode == CamMode.Follow ? _follow : _povIdx;

        void GetViewPoint(out Vector3 c, out float yaw)
        {
            if (_mode == CamMode.Free) { c = _freePos; yaw = _freeYaw; return; }
            int i = SubjectIdx();
            if (i >= 0 && i < _cur.Length && _present[i]) { var s = _cur[i]; c = new Vector3(s.Ex, s.Ey, s.Ez); yaw = s.LookYaw; }
            else { c = Vector3.zero; yaw = 0f; }
        }

        // renders the two helper cameras (face portrait, top-down minimap); called from Update
        void UpdateOverlays()
        {
            if (!_showHud) return;
            int subj = SubjectIdx();
            if (Cfg.BadgeEnabled.Value && Cfg.BadgeShowFace.Value)
            {
                if (_face == null) _face = new FaceCam();
                var pp = _pup.Get(subj);
                if (pp != null && _present[subj]) _face.Render(pp);
            }
            else if (_face != null) { _face.Destroy(); _face = null; }

            if (Cfg.MinimapEnabled.Value && _showMap && Cfg.MinimapModeE.Value == MinimapMode.TopDownCamera)
            {
                if (_top == null || _top.Tex.width != Cfg.MinimapSize.Value) { _top?.Destroy(); _top = new TopDownView(Cfg.MinimapSize.Value); }
                GetViewPoint(out var c, out float yaw);
                var cam = EftBridge.MainCamera;
                _top.Render(c, Cfg.MinimapRotate.Value ? yaw : 0f, Cfg.MinimapRange.Value, cam != null ? cam.cullingMask : -1, Cfg.MinimapFps.Value);
            }
            else if (_top != null) { _top.Destroy(); _top = null; }
        }

        void OnGUI()
        {
            if (Time.realtimeSinceStartup < _toastUntil && !string.IsNullOrEmpty(_toast) && Event.current.type == UnityEngine.EventType.Repaint)
            {
                EnsureStyles();
                var r = new Rect(Screen.width * 0.5f - 330f, Screen.height * 0.14f, 660f, 38f);
                Panel(r); Text(r, _toast, _sCenter, TextC);
            }
            if (!Active || !_showHud) return;
            EnsureStyles();
            if (Event.current.type == UnityEngine.EventType.Repaint)
            {
                DrawKillFeed();
                if (Cfg.ShowNameplates.Value) DrawNameplates();
                if (Cfg.MinimapEnabled.Value && _showMap) DrawMinimap();
                if (Cfg.BadgeEnabled.Value) DrawBadge();
            }
            if (Cfg.ShowTimeline.Value) DrawTimeline();
        }

        void DrawKillFeed()
        {
            float y = 16f;
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup > _feed[i].Value) { _feed.RemoveAt(i); continue; }
            }
            foreach (var f in _feed) { var r = new Rect(16, y, 320, 22); Fill(r, new Color(0.08f, 0.085f, 0.085f, 0.75f)); Text(new Rect(r.x + 8, r.y, r.width - 8, r.height), f.Key, _sMid, TextC); y += 24f; }
        }

        void DrawNameplates()
        {
            var cam = EftBridge.MainCamera; if (cam == null) return;
            int subj = _mode == CamMode.Free ? -1 : SubjectIdx();
            Vector3 cp = cam.transform.position;
            for (int i = 0; i < _cur.Length; i++)
            {
                if (!_present[i] || i == subj || i == _povIdx && _mode == CamMode.Pov) continue;
                var info = _rep.Entities[i]; if (info == null) continue;
                var s = _cur[i];
                var wp = new Vector3(s.Ex, s.Ey + 0.35f, s.Ez);
                float d = Vector3.Distance(cp, wp);
                if (d > Cfg.NameplateRange.Value) continue;
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0f) continue;
                var c = s.Alive ? SideColor(info) : Dim; c.a = Mathf.Clamp01(1.4f - d / Cfg.NameplateRange.Value);
                Text(new Rect(sp.x - 100f, Screen.height - sp.y - 12f, 200f, 24f), (s.Alive ? "" : "+ ") + info.Nickname + "  " + Mathf.RoundToInt(d) + "m", _sCenter, c);
            }
        }

        void DrawMinimap()
        {
            float size = Cfg.MinimapSize.Value, half = size * 0.5f, op = Cfg.MinimapOpacity.Value;
            var r = Place(Cfg.MinimapCorner.Value, size, size);
            Fill(r, new Color(Bg.r, Bg.g, Bg.b, op));
            if (Cfg.MinimapModeE.Value == MinimapMode.TopDownCamera && _top != null)
            { var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, op); GUI.DrawTexture(r, _top.Tex, ScaleMode.StretchToFill, false); GUI.color = o; }
            var ctr = new Vector2(r.center.x, r.center.y);
            var ring = new Color(Border.r, Border.g, Border.b, 0.5f);
            Fill(new Rect(r.x, ctr.y, size, 1f), ring); Fill(new Rect(ctr.x, r.y, 1f, size), ring);

            GetViewPoint(out var c, out float viewYaw);
            bool rot = Cfg.MinimapRotate.Value; float range = Cfg.MinimapRange.Value;
            int subj = SubjectIdx();
            for (int i = 0; i < _cur.Length; i++)
            {
                if (!_present[i]) continue;
                var s = _cur[i];
                if (!MiniMapMath.WorldToMap(s.Px, s.Pz, c.x, c.z, viewYaw, rot, range, half - 2f, out float mx, out float my)) continue;
                var info = _rep.Entities[i];
                bool me = i == subj;
                Color col = me ? Color.white : s.Alive ? SideColor(info) : Dim;
                float sz = me ? 8f : s.Alive ? 6f : 4f;
                var p = new Vector2(ctr.x + mx, ctr.y + my);
                Fill(new Rect(p.x - sz / 2f, p.y - sz / 2f, sz, sz), col);
                if (s.Alive)
                {
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(s.LookYaw - (rot ? viewYaw : 0f), p);
                    Fill(new Rect(p.x - 1f, p.y - sz / 2f - (me ? 9f : 6f), 2f, me ? 9f : 6f), col);
                    GUI.matrix = m;
                }
            }
            Frame(r, Border);
            Text(new Rect(r.x + 5, r.yMax - 17, 120, 14), Mathf.RoundToInt(range) + " m" + (rot ? "" : "  N^"), _sSmall, Dim);
        }

        void DrawBadge()
        {
            int i = SubjectIdx(); if (i < 0 || i >= _rep.Entities.Count) return;
            var info = _rep.Entities[i]; if (info == null) return;
            float k = Cfg.BadgeScale.Value, w = 330f * k, h = 94f * k;
            var r = Place(Cfg.BadgeCorner.Value, w, h);
            Panel(r);
            Color accent = SideColor(info);
            Fill(new Rect(r.x, r.y, 4f * k, h), accent);

            var face = new Rect(r.x + 14f * k, r.y + 9f * k, 76f * k, 76f * k);
            Fill(face, new Color(0.12f, 0.13f, 0.13f, 1f));
            if (Cfg.BadgeShowFace.Value && _face != null) GUI.DrawTexture(face, _face.Tex, ScaleMode.ScaleToFit, false);
            Frame(face, Border);

            float tx = face.xMax + 12f * k, tw = r.xMax - tx - 8f;
            string side = info.Side == EntitySide.Usec ? "USEC" : info.Side == EntitySide.Bear ? "BEAR" : info.IsAI ? (string.IsNullOrEmpty(info.Role) ? "AI" : info.Role.ToUpperInvariant()) : "SCAV";
            string lvl = info.Level > 0 ? "LVL " + info.Level + "   " : "";
            var big = new GUIStyle(_sBig) { fontSize = Mathf.RoundToInt(20f * k) };
            Text(new Rect(tx, r.y + 8f * k, tw, 28f * k), (info.Nickname ?? "").ToUpperInvariant(), big, TextC);
            Text(new Rect(tx, r.y + 38f * k, tw, 18f * k), lvl + side, _sMid, accent);
            string mode = _mode == CamMode.Free ? "FREE CAMERA" : _mode == CamMode.Follow ? "WATCHING" : "OPERATOR VIEW";
            bool dead = i < _present.Length && _present[i] && !_cur[i].Alive;
            Text(new Rect(tx, r.y + 60f * k, tw, 18f * k), dead ? "KILLED IN ACTION" : mode, _sSmall, dead ? new Color(0.9f, 0.3f, 0.27f) : Dim);
        }

        void DrawTimeline()
        {
            float w = Mathf.Min(900f, Screen.width - 40f);
            var bar = new Rect((Screen.width - w) * 0.5f, Screen.height - 52f, w, 16f);
            float dur = Mathf.Max(1, _clock.DurationMs), frac = (float)(_clock.TimeMs / dur);

            if (Event.current.type == UnityEngine.EventType.MouseDown && Event.current.button == 0 && bar.Contains(Event.current.mousePosition))
            {
                SeekTo(Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width) * dur);
                Event.current.Use();
                return;
            }
            if (Event.current.type != UnityEngine.EventType.Repaint) return;

            Fill(new Rect(bar.x - 4, bar.y - 6, bar.width + 8, 52f), new Color(Bg.r, Bg.g, Bg.b, 0.8f));
            Fill(bar, new Color(0.18f, 0.19f, 0.19f, 1f));
            Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height), new Color(0.55f, 0.62f, 0.45f, 1f));
            foreach (var e in _rep.Events)
                if (e.Type == EventKind.Kill) Fill(new Rect(bar.x + bar.width * (e.TimeMs / dur) - 1f, bar.y - 4f, 2f, bar.height + 8f), new Color(0.9f, 0.3f, 0.27f, 0.9f));
            Frame(bar, Border);

            string t = Fmt(_clock.TimeMs) + " / " + Fmt(dur);
            Text(new Rect(bar.x, bar.yMax + 2f, 200f, 18f), t, _sMid, TextC);
            string state = (_clock.Paused ? "PAUSED   " : "") + "x" + _clock.Speed.ToString("0.##");
            Text(new Rect(bar.xMax - 200f, bar.yMax + 2f, 200f, 18f), state, new GUIStyle(_sMid) { alignment = TextAnchor.MiddleRight }, TextC);
            Text(new Rect(bar.x + 110f, bar.yMax + 2f, bar.width - 320f, 18f), "SPACE pause  <- -> seek  , . speed  C watch  V free cam  M map  HOME hide  END exit  (hold ALT = cursor)", new GUIStyle(_sSmall) { alignment = TextAnchor.MiddleCenter }, Dim);
        }

        static string Fmt(double ms) => TimeSpan.FromMilliseconds(ms).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
    }
}
