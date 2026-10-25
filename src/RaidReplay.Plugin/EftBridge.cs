using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// EVERY call into Tarkov's own classes lives here. If a game update renames something,
    /// this is the only file that needs fixing. Member names were checked against the EFT 0.16.9.x
    /// assembly that SPT 4.1 uses (same build Fika 2.4 targets).
    /// </summary>
    internal static class EftBridge
    {
        // MovementContext.Rotation.y sign is auto-calibrated against the real camera while recording
        static float _pitchSign = 1f;

        public static bool TryGetWorld(out GameWorld gw)
        {
            gw = null;
            if (!Singleton<GameWorld>.Instantiated) return false;
            gw = Singleton<GameWorld>.Instance;
            return gw != null;
        }

        public static bool RaidReady(GameWorld gw) =>
            gw != null && gw.MainPlayer != null && gw.AllAlivePlayersList != null && gw.AllAlivePlayersList.Count > 0;

        public static string MapId(GameWorld gw) => gw.LocationId?.ToString() ?? "unknown";

        // Keep these lookups reflective: the public compile references intentionally omit
        // some game-private camera/settings types, while they are present in the live client.
        static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var type = instance as Type ?? instance.GetType();
            var target = instance is Type ? null : instance;
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            var field = type.GetField(name, flags);
            return field != null ? field.GetValue(target) : null;
        }

        static Type GameType(string name) => typeof(Player).Assembly.GetType(name);

        static void UpdatePlayerCameraController(Player player)
        {
            var type = GameType("EFT.CameraControl.PlayerCameraController");
            if (player == null || type == null) return;
            var controller = player.GetComponent(type);
            var method = type.GetMethod("UpdatePointOfView", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (controller != null && method != null) method.Invoke(controller, null);
        }

        public static Camera MainCamera
        {
            get
            {
                try
                {
                    var managerType = GameType("EFT.CameraControl.CameraManager");
                    if (managerType == null || !Convert.ToBoolean(ReadMember(managerType, "Exist"))) return null;
                    var manager = ReadMember(managerType, "Instance");
                    return ReadMember(manager, "Camera") as Camera;
                }
                catch { return null; }
            }
        }

        public static bool IsAlive(Player p) => p != null && p.HealthController != null && p.HealthController.IsAlive;

        public static float DefaultFov()
        {
            try
            {
                var settingsType = GameType("EFT.Settings.SettingsManager");
                if (settingsType == null) return 75f;
                var singletonType = typeof(Singleton<>).MakeGenericType(settingsType);
                var singleton = ReadMember(singletonType, "Instance");
                var game = ReadMember(singleton, "Game");
                var settings = ReadMember(game, "Settings");
                var fov = ReadMember(settings, "FieldOfView");
                return fov != null ? Convert.ToSingle(fov) : 75f;
            }
            catch { return 75f; }
        }

        // ---------------------------------------------------------------- recording

        public static EntityInfo Describe(Player p, bool isPov)
        {
            var info = p.Profile.Info;
            EntitySide side = EntitySide.Unknown;
            switch (info.Side)
            {
                case EPlayerSide.Usec: side = EntitySide.Usec; break;
                case EPlayerSide.Bear: side = EntitySide.Bear; break;
                case EPlayerSide.Savage: side = EntitySide.Savage; break;
            }
            return new EntityInfo
            {
                ProfileId = p.ProfileId, Nickname = info.Nickname ?? "", Side = side, IsAI = p.IsAI, IsPov = isPov,
                Level = info.Level, Role = info.Settings != null ? info.Settings.Role.ToString() : ""
            };
        }

        static float Norm180(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }

        public static bool ReadSample(Player p, bool isPov, int timeMs, out Sample s)
        {
            s = default;
            if (p == null) return false;
            try
            {
                Vector3 feet = p.Position;
                float bodyYaw = p.Transform != null ? p.Transform.rotation.eulerAngles.y : 0f;
                var mc = p.MovementContext;
                bool prone = mc != null && mc.IsInPronePose;

                Vector3 eye = feet + Vector3.up * (prone ? 0.4f : 1.6f);
                if (p.PlayerBones != null && p.PlayerBones.Neck != null) eye = p.PlayerBones.Neck.position + Vector3.up * 0.08f;

                float yaw = bodyYaw, pitch = 0f, roll = 0f, fov = 0f;
                if (mc != null) { yaw = mc.Rotation.x; pitch = mc.Rotation.y * _pitchSign; }

                var cam = MainCamera;
                if (isPov && cam != null && p.PointOfView == EPointOfView.FirstPerson)
                {
                    // the real camera = exactly what the operator saw (head bob, lean, ADS offset, roll)
                    var e = cam.transform.eulerAngles;
                    if (mc != null)
                    {
                        float camPitch = Norm180(e.x), mcPitch = Norm180(mc.Rotation.y);
                        if (Mathf.Abs(camPitch) > 15f && Mathf.Abs(mcPitch) > 15f) _pitchSign = Mathf.Sign(camPitch) * Mathf.Sign(mcPitch);
                    }
                    eye = cam.transform.position; yaw = e.y; pitch = e.x; roll = e.z; fov = cam.fieldOfView;
                }

                byte flags = 0;
                if (IsAlive(p)) flags |= SampleFlags.Alive;
                if (prone) flags |= SampleFlags.Prone;
                else if (mc != null && mc.PoseLevel < 0.75f) flags |= SampleFlags.Crouch;
                if (mc != null && mc.IsSprintEnabled) flags |= SampleFlags.Sprint;
                if (p.HandsController != null && p.HandsController.IsAiming) flags |= SampleFlags.Aiming;
                if (mc != null && mc.SmoothedTilt < -1f) flags |= SampleFlags.LeanLeft;
                if (mc != null && mc.SmoothedTilt > 1f) flags |= SampleFlags.LeanRight;

                s = new Sample
                {
                    TimeMs = timeMs, Px = feet.x, Py = feet.y, Pz = feet.z, Ex = eye.x, Ey = eye.y, Ez = eye.z,
                    BodyYaw = bodyYaw, LookYaw = yaw, LookPitch = pitch, LookRoll = roll,
                    Flags = flags, Speed = mc != null ? (byte)Mathf.Clamp(Mathf.RoundToInt(mc.Velocity.magnitude * 20f), 0, 255) : (byte)0,
                    Fov = (byte)Mathf.Clamp(Mathf.RoundToInt(fov), 0, 255)
                };
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("ReadSample failed for " + p.ProfileId + ": " + e.Message);
                return false;
            }
        }

        public static Player FindPlayerByHands(GameWorld gw, object handsController)
        {
            var list = gw.AllAlivePlayersList;
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i].HandsController, handsController)) return list[i];
            return null;
        }

        // ---------------------------------------------------------------- playback

        public sealed class LocalState
        {
            public GamePlayerOwner Owner; public bool OwnerWasEnabled;
            public readonly List<Renderer> Hidden = new List<Renderer>();
            public GameObject BattleUi; public bool BattleUiWasActive;
        }

        /// <summary>Detaches the camera from the local player (same recipe Fika's free camera uses) and hides the local body + battle UI.</summary>
        public static LocalState EnterReplayMode(Player me)
        {
            var st = new LocalState();
            me.PointOfView = EPointOfView.ThirdPerson;
            if (me.PlayerBody != null)
            {
                me.PlayerBody.PointOfView.Value = EPointOfView.FreeCamera;
                UpdatePlayerCameraController(me);
            }
            st.Owner = me.GetComponentInChildren<GamePlayerOwner>();
            if (st.Owner != null) { st.OwnerWasEnabled = st.Owner.enabled; st.Owner.enabled = false; }
            foreach (var r in me.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; st.Hidden.Add(r); }
            var ui = UnityEngine.Object.FindObjectOfType<EftBattleUIScreen>();
            if (ui != null) { st.BattleUi = ui.gameObject; st.BattleUiWasActive = ui.gameObject.activeSelf; ui.gameObject.SetActive(false); }
            return st;
        }

        public static void ExitReplayMode(Player me, LocalState st)
        {
            if (st == null) return;
            foreach (var r in st.Hidden) if (r != null) r.enabled = true;
            if (st.BattleUi != null) st.BattleUi.SetActive(st.BattleUiWasActive);
            if (me != null)
            {
                me.PointOfView = EPointOfView.FirstPerson;
                if (me.PlayerBody != null)
                {
                    me.PlayerBody.PointOfView.Value = EPointOfView.FirstPerson;
                    UpdatePlayerCameraController(me);
                }
            }
            if (st.Owner != null) st.Owner.enabled = st.OwnerWasEnabled;
        }

        static readonly List<GameObject> _sleptAi = new List<GameObject>();

        /// <summary>Best effort: switches every bot off so the replay map is quiet. Run again periodically (waves keep spawning).</summary>
        public static void DeactivateAI(GameWorld gw)
        {
            var list = gw.AllAlivePlayersList;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                if (p == null || !p.IsAI || p.IsYourPlayer || !p.gameObject.activeSelf) continue;
                p.gameObject.SetActive(false); _sleptAi.Add(p.gameObject);
            }
        }

        public static void ReactivateAI()
        {
            foreach (var go in _sleptAi) if (go != null) go.SetActive(true);
            _sleptAi.Clear();
        }
    }
}
