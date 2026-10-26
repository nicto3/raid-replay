using System;
using System.Collections.Generic;
using EFT;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// Plays a recorded raid inside a normal raid on the same map: takes the camera away from the local player,
    /// puppets every recorded entity and drives them from the file. Nothing here is networked, so it is safe with Fika
    /// as long as you are the only real player in the raid.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed partial class ReplaySession : MonoBehaviour
    {
        enum CamMode { Pov, Follow, Free }

        public static string Pending { get; private set; }
        public static bool Active { get; private set; }
        static ReplaySession _inst;
        static bool _exitRequested;
        static string _toast; static float _toastUntil;

        public static string CurrentPath { get; private set; }

        public static void Queue(string path)
        {
            if (Active && _inst != null) { _inst.SwitchTo(path); return; }   // already in a raid on some map: switch without reloading
            Pending = path;
            Toast(EftBridge.TryGetWorld(out _) ? "Replay queued. It starts when a solo raid on the same map is running." : "Replay queued. Deploy on the same map (solo raid) to start it.", 6f);
        }
        public static void RequestExit() { _exitRequested = true; }
        public static void Cancel() { Pending = null; Toast("Queued replay cancelled."); }
        public static void Toast(string text, float seconds = 4f) { _toast = text; _toastUntil = Time.realtimeSinceStartup + seconds; }

        Replay _rep; ReplayClock _clock; PuppetManager _pup; FaceCam _face; TopDownView _top; GameWorld _gw; Player _me; EftBridge.LocalState _local;
        Sample[][] _tracks; Sample[] _cur; bool[] _present;
        WorldTimeline _wtl; WorldApplier _wapp; int _wprev; bool _wjump; readonly List<WorldChange> _wch = new List<WorldChange>();
        CamMode _mode; int _povIdx, _follow;
        int _evCursor; float _nextAiSweep;
        bool _showHud = true, _showMap = true, _altCursor;
        Vector3 _freePos; float _freeYaw, _freePitch, _freeSpeed = 6f;
        string _warnedPath;
        readonly List<KeyValuePair<string, float>> _feed = new List<KeyValuePair<string, float>>();

        void Awake() { _inst = this; }
        void OnDestroy() { if (Active) End(); }

        // ------------------------------------------------------------ lifecycle

        void Update()
        {
            if (!Active) { TryBegin(); return; }
            if (_exitRequested) { ExitReplay(); return; }
            if (!EftBridge.TryGetWorld(out var gw) || gw != _gw || _me == null) { End(); return; }

            HandleInput();
            if (!Active) return;
            _clock.Tick(Time.unscaledDeltaTime);
            int now = _clock.NowMs;
            for (int i = 0; i < _cur.Length; i++)
                _present[i] = TrackSampler.TryEvaluate(_tracks[i], now, TrackSampler.DefaultMaxGapMs, out _cur[i]);
            DispatchEvents(now);
            SyncWorld(now);
            _pup.Apply(_cur, _present, _mode == CamMode.Pov ? _povIdx : _mode == CamMode.Follow ? _follow : -1);

            if (Time.time >= _nextAiSweep) { EftBridge.DeactivateAI(_gw); _nextAiSweep = Time.time + 1f; }
            if (_clock.Ended && !_clock.Paused) { _clock.Paused = true; Toast("End of replay. Space = restart, End = exit."); }
            UpdateOverlays();
        }

        void TryBegin()
        {
            if (string.IsNullOrEmpty(Pending)) return;
            if (!EftBridge.TryGetWorld(out var gw) || !EftBridge.RaidReady(gw)) return;
            string path = Pending;
            ReplayInfo info;
            try { info = ReplayFile.ReadInfo(path); }
            catch (Exception e) { Toast("Cannot open replay: " + e.Message, 6f); Pending = null; return; }

            if (!MapIds.Same(info.Header.Map, EftBridge.MapId(gw)))
            {
                if (_warnedPath != path) { _warnedPath = path; Toast($"This replay was recorded on '{info.Header.Map}'. Deploy on that map.", 8f); }
                return;
            }
            int humans = 0; foreach (var p in gw.AllAlivePlayersList) if (p != null && !p.IsAI) humans++;
            if (humans > 1) { Toast("Replays are solo-only: other real players are in this raid. Start a solo raid.", 8f); Pending = null; return; }

            try { _rep = Replay.Load(path); }
            catch (Exception e) { Toast("Replay is damaged: " + e.Message, 6f); Pending = null; return; }
            if (_rep.Tracks.Count == 0) { Toast("Replay contains no data.", 5f); Pending = null; return; }
            Start(gw);
        }

        void Start(GameWorld gw)
        {
            _gw = gw; _me = gw.MainPlayer;
            LoadState();
            _local = EftBridge.EnterReplayMode(_me);
            EftBridge.DeactivateAI(gw); _nextAiSweep = Time.time + 1f;
            Pending = null; Active = true; _exitRequested = false;
        }

        /// <summary>Everything that depends on the loaded replay; also used when switching to another replay of the same map.</summary>
        void LoadState()
        {
            int n = _rep.Entities.Count;
            _tracks = new Sample[n][]; _cur = new Sample[n]; _present = new bool[n];
            for (int i = 0; i < n; i++) _tracks[i] = _rep.Tracks.TryGetValue(i, out var t) ? t : null;
            var pov = _rep.FindPov();
            _povIdx = pov != null ? pov.Index : 0; _follow = _povIdx; _mode = CamMode.Pov;
            _clock = new ReplayClock(_rep.DurationMs);
            _pup = new PuppetManager(_rep, _povIdx);
            _evCursor = 0; _feed.Clear(); _showHud = true; _showMap = true;
            _wtl = new WorldTimeline(_rep.World); _wapp = new WorldApplier(); _wprev = 0; _wjump = true; _wch.Clear();
            CurrentPath = _rep.SourcePath;
            Toast($"Replay: {_rep.Header.PovNickname} on {MapIds.DisplayName(_rep.Header.Map)}" + (_rep.Truncated ? " (file was cut off)" : "") + (_wtl.ObjectCount > 0 ? $"  ·  {_wtl.ObjectCount} doors/lamps synced" : "  ·  no door data in this recording") + "   [Space] pause  [V] free cam  [C] watch others", 7f);
        }

        void SwitchTo(string path)
        {
            try
            {
                var info = ReplayFile.ReadInfo(path);
                if (!MapIds.Same(info.Header.Map, EftBridge.MapId(_gw)))
                { Pending = path; Toast($"That replay is from {MapIds.DisplayName(info.Header.Map)}. Leave this raid and deploy there — it is queued."); return; }
                var rep = Replay.Load(path);
                if (rep.Tracks.Count == 0) { Toast("Replay contains no data."); return; }
                try { _pup?.DestroyAll(); } catch { }
                _rep = rep; LoadState();
            }
            catch (Exception e) { Toast("Could not switch replay: " + e.Message, 6f); }
        }

        void ExitReplay()
        {
            var me = _me; bool leave = Cfg.LeaveAsExtracted.Value;
            End();
            if (leave) { if (!EftBridge.TryLeaveAsExtracted(me)) Toast("Replay closed. Leaving the raid automatically is not available here — use the normal exit."); }
        }

        void End()
        {
            bool was = Active;
            Active = false; _exitRequested = false; CurrentPath = null;
            try { _pup?.DestroyAll(); } catch { }
            try { _face?.Destroy(); _top?.Destroy(); } catch { }
            _face = null; _top = null; _pup = null;
            try { EftBridge.ExitReplayMode(_me, _local); } catch (Exception e) { Plugin.Log.LogWarning("Restoring player view failed: " + e.Message); }
            EftBridge.ReactivateAI();
            if (_altCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; _altCursor = false; }
            _local = null;
            if (was) Toast("Replay closed.");
        }

        // ------------------------------------------------------------ input / time

        void HandleInput()
        {
            if (ReplayCenter.Visible) return;   // the Replay Center owns the keyboard and mouse while open
            if (Cfg.KeyExit.Value.IsDown()) { ExitReplay(); return; }
            if (Cfg.KeyPause.Value.IsDown())
            {
                if (_clock.Ended) { SeekTo(0); _clock.Paused = false; }
                else _clock.Paused = !_clock.Paused;
            }
            float step = Input.GetKey(KeyCode.LeftShift) ? 60000f : 10000f;
            if (Cfg.KeySeekBack.Value.IsDown()) SeekTo(_clock.TimeMs - step);
            if (Cfg.KeySeekFwd.Value.IsDown()) SeekTo(_clock.TimeMs + step);
            if (Cfg.KeyFaster.Value.IsDown()) _clock.SpeedUp();
            if (Cfg.KeySlower.Value.IsDown()) _clock.SpeedDown();
            if (Cfg.KeyHud.Value.IsDown()) _showHud = !_showHud;
            if (Cfg.KeyMinimap.Value.IsDown()) _showMap = !_showMap;
            if (Cfg.KeyBackToPov.Value.IsDown()) _mode = CamMode.Pov;

            if (Cfg.KeyFreeCam.Value.IsDown())
            {
                if (!Cfg.AllowFreeCam.Value) Toast("Free camera is disabled in settings.");
                else if (_mode == CamMode.Free) _mode = CamMode.Pov;
                else
                {
                    var cam = EftBridge.MainCamera;
                    if (cam != null) { _freePos = cam.transform.position; var e = cam.transform.eulerAngles; _freeYaw = e.y; _freePitch = e.x > 180 ? e.x - 360 : e.x; }
                    _mode = CamMode.Free;
                }
            }
            if (Cfg.KeyNextTarget.Value.IsDown())
            {
                if (!Cfg.AllowSpectate.Value) Toast("Watching other players is disabled in settings.");
                else NextTarget();
            }

            // hold Left Alt to get a mouse cursor (click the timeline to seek)
            bool alt = Input.GetKey(KeyCode.LeftAlt);
            if (alt != _altCursor)
            {
                _altCursor = alt;
                Cursor.lockState = alt ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = alt;
            }
        }

        void SeekTo(double ms)
        {
            _clock.Seek(ms);
            int now = _clock.NowMs;
            var ev = _rep.Events; int lo = 0, hi = ev.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (ev[mid].TimeMs <= now) lo = mid + 1; else hi = mid; }
            _evCursor = lo; _pup.ClearTracers(); _feed.Clear(); _wjump = true;
        }

        void SyncWorld(int now)
        {
            _wch.Clear();
            _wtl.Advance(_wprev, now, _wjump, _wch); _wjump = false; _wprev = now;
            if (_wch.Count > 0) _wapp.Enqueue(_wch);
            _wapp.Tick();
        }

        void NextTarget()
        {
            int n = _cur.Length, start = _mode == CamMode.Follow ? _follow : _povIdx;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (i == _povIdx || !_present[i] || !_cur[i].Alive) continue;
                _follow = i; _mode = CamMode.Follow; return;
            }
            Toast("Nobody else is alive in the replay right now.");
        }

        void DispatchEvents(int now)
        {
            var ev = _rep.Events;
            while (_evCursor < ev.Count && ev[_evCursor].TimeMs <= now)
            {
                var e = ev[_evCursor++];
                if (_clock.Paused || now - e.TimeMs > 400) continue;
                if (e.Type == EventKind.Shot && Cfg.ShowTracers.Value) { _pup.SpawnTracer(new Vector3(e.X, e.Y, e.Z), e.Arg, e.Arg2); _pup.Flash(e.Actor); }
                else if (e.Type == EventKind.Kill)
                {
                    string killer = e.Actor >= 0 && e.Actor < _rep.Entities.Count && _rep.Entities[e.Actor] != null ? _rep.Entities[e.Actor].Nickname : "?";
                    string victim = e.Target >= 0 && e.Target < _rep.Entities.Count && _rep.Entities[e.Target] != null ? _rep.Entities[e.Target].Nickname : "?";
                    _feed.Add(new KeyValuePair<string, float>($"{killer}  >  {victim}", Time.realtimeSinceStartup + 7f));
                    if (_feed.Count > 6) _feed.RemoveAt(0);
                }
            }
        }

        // ------------------------------------------------------------ camera

        void LateUpdate()
        {
            if (!Active) return;
            var cam = EftBridge.MainCamera;
            if (cam == null) return;

            if (_mode == CamMode.Free)
            {
                float dt = Time.unscaledDeltaTime;
                bool ui = ReplayCenter.Visible;
                if (!ui) { _freeYaw += Input.GetAxis("Mouse X") * 2.2f; _freePitch = Mathf.Clamp(_freePitch - Input.GetAxis("Mouse Y") * 2.2f, -89f, 89f); }
                _freeSpeed = Mathf.Clamp(_freeSpeed * (1f + Input.mouseScrollDelta.y * 0.15f), 0.5f, 80f);
                var rot = Quaternion.Euler(_freePitch, _freeYaw, 0f);
                Vector3 mv = Vector3.zero;
                if (!ui) { if (Input.GetKey(KeyCode.W)) mv += rot * Vector3.forward;
                if (Input.GetKey(KeyCode.S)) mv -= rot * Vector3.forward;
                if (Input.GetKey(KeyCode.D)) mv += rot * Vector3.right;
                if (Input.GetKey(KeyCode.A)) mv -= rot * Vector3.right;
                if (Input.GetKey(KeyCode.E)) mv += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) mv -= Vector3.up; }
                _freePos += mv * (_freeSpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * dt);
                cam.transform.SetPositionAndRotation(_freePos, rot);
                cam.fieldOfView = EftBridge.DefaultFov();
                return;
            }

            int idx = _mode == CamMode.Follow ? _follow : _povIdx;
            if (idx < 0 || idx >= _cur.Length || !_present[idx]) return;
            var s = _cur[idx];
            cam.transform.SetPositionAndRotation(new Vector3(s.Ex, s.Ey, s.Ez), Quaternion.Euler(s.LookPitch, s.LookYaw, s.LookRoll));
            cam.fieldOfView = idx == _povIdx && s.Fov > 0 ? s.Fov : EftBridge.DefaultFov();
        }
    }
}
