using System.Collections.Generic;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// Stand-in body for a recorded entity: capsule + head + weapon bar. Pure Unity, no EFT internals, so it cannot
    /// break on a game update. A model-based puppet (real gear and animations) would replace this class only.
    /// </summary>
    internal sealed class Puppet
    {
        public readonly EntityInfo Info;
        readonly GameObject _root;
        readonly Transform _body, _head, _gun;
        readonly Renderer[] _all;
        readonly Renderer _headR;
        readonly Color _color;
        public Vector3 HeadPos => _head.position;
        public Quaternion LookRot { get; private set; } = Quaternion.identity;
        public bool Alive { get; private set; } = true;

        public Puppet(EntityInfo info, bool isPovEntity)
        {
            Info = info;
            _color = isPovEntity ? new Color(0.92f, 0.92f, 0.92f)
                   : info.Side == EntitySide.Usec ? new Color(0.25f, 0.5f, 0.95f)
                   : info.Side == EntitySide.Bear ? new Color(0.9f, 0.25f, 0.2f)
                   : new Color(0.95f, 0.65f, 0.2f);
            _root = new GameObject("RR_" + info.Nickname);
            _body = Prim(PrimitiveType.Capsule, "body");
            _head = Prim(PrimitiveType.Sphere, "head"); _head.localScale = Vector3.one * 0.24f;
            _gun = Prim(PrimitiveType.Cube, "gun"); _gun.localScale = new Vector3(0.05f, 0.07f, 0.75f);
            _gun.GetComponent<Renderer>().material.color = new Color(0.12f, 0.12f, 0.12f);
            _all = _root.GetComponentsInChildren<Renderer>();
            _headR = _head.GetComponent<Renderer>();
            Tint(_color);
        }

        Transform Prim(PrimitiveType t, string name)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);   // purely visual: must not touch game physics
            go.transform.SetParent(_root.transform, false);
            return go.transform;
        }

        void Tint(Color c) { foreach (var r in _all) if (r.transform != _gun) r.material.color = c; }

        public void Apply(Sample s, bool hideHead)
        {
            Alive = s.Alive;
            var feet = new Vector3(s.Px, s.Py, s.Pz);
            var eye = new Vector3(s.Ex, s.Ey, s.Ez);
            var body = Quaternion.Euler(0f, s.BodyYaw, 0f);
            LookRot = Quaternion.Euler(s.LookPitch, s.LookYaw, s.LookRoll);

            bool lying = !s.Alive || SampleFlags.Has(s.Flags, SampleFlags.Prone);
            if (lying)
            {
                _body.SetPositionAndRotation(feet + Vector3.up * 0.22f + body * Vector3.forward * 0.6f, body * Quaternion.Euler(90f, 0f, 0f));
                _body.localScale = new Vector3(0.4f, 0.85f, 0.4f);
            }
            else
            {
                float h = Mathf.Max(0.5f, eye.y - feet.y + 0.12f);
                _body.SetPositionAndRotation(feet + Vector3.up * (h * 0.5f), body);
                _body.localScale = new Vector3(0.45f, h * 0.5f, 0.45f);
            }
            _head.position = eye;
            _gun.SetPositionAndRotation(eye + LookRot * new Vector3(0.12f, -0.22f, 0.45f), LookRot);
            Tint(s.Alive ? _color : new Color(0.35f, 0.35f, 0.35f));
            _headR.enabled = !hideHead;
        }

        public bool HeadVisible => _headR.enabled;
        public void ForceHeadVisible(bool v) { _headR.enabled = v; }
        public void SetVisible(bool v) { foreach (var r in _all) r.enabled = v; }
        public void Destroy() { if (_root != null) Object.Destroy(_root); }
    }

    internal sealed class PuppetManager
    {
        readonly Dictionary<int, Puppet> _puppets = new Dictionary<int, Puppet>();
        readonly Replay _rep;
        readonly int _povIdx;
        readonly List<Tracer> _tracers = new List<Tracer>();
        sealed class Tracer { public Transform T; public float Until; }

        public PuppetManager(Replay rep, int povIdx) { _rep = rep; _povIdx = povIdx; }

        public Puppet Get(int idx) => _puppets.TryGetValue(idx, out var p) ? p : null;

        public void Apply(Sample[] cur, bool[] present, int hideHeadOf)
        {
            for (int i = 0; i < cur.Length; i++)
            {
                if (!present[i]) { if (_puppets.TryGetValue(i, out var gone)) gone.SetVisible(false); continue; }
                if (!_puppets.TryGetValue(i, out var p))
                {
                    var info = _rep.Entities[i]; if (info == null) continue;
                    _puppets[i] = p = new Puppet(info, i == _povIdx);
                }
                p.SetVisible(true);
                p.Apply(cur[i], i == hideHeadOf);
            }
            for (int i = _tracers.Count - 1; i >= 0; i--)
                if (Time.time > _tracers[i].Until) { Object.Destroy(_tracers[i].T.gameObject); _tracers.RemoveAt(i); }
        }

        public void SpawnTracer(Vector3 from, float yaw, float pitch)
        {
            if (_tracers.Count > 40) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            go.transform.SetPositionAndRotation(from + rot * Vector3.forward * 20f, rot);
            go.transform.localScale = new Vector3(0.025f, 0.025f, 40f);
            go.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.3f);
            _tracers.Add(new Tracer { T = go.transform, Until = Time.time + 0.12f });
        }

        public void ClearTracers() { foreach (var t in _tracers) if (t.T != null) Object.Destroy(t.T.gameObject); _tracers.Clear(); }

        public void DestroyAll()
        {
            foreach (var p in _puppets.Values) p.Destroy();
            _puppets.Clear(); ClearTracers();
        }
    }

    /// <summary>Small render-to-texture camera used for the operator's face portrait.</summary>
    internal sealed class FaceCam
    {
        readonly Camera _cam; public readonly RenderTexture Tex;
        float _next;

        public FaceCam()
        {
            Tex = new RenderTexture(128, 128, 16);
            var go = new GameObject("RR_FaceCam");
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false; _cam.targetTexture = Tex; _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.13f, 0.13f); _cam.fieldOfView = 28f; _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 15f;
            _cam.allowHDR = false; _cam.allowMSAA = false;
        }

        public void Render(Puppet p, float fps = 6f)
        {
            if (p == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / fps;
            var fwd = p.LookRot * Vector3.forward;
            _cam.transform.SetPositionAndRotation(p.HeadPos + fwd * 0.85f, Quaternion.LookRotation(-fwd, Vector3.up));
            bool was = p.HeadVisible;
            p.ForceHeadVisible(true);
            _cam.Render();
            p.ForceHeadVisible(was);
        }

        public void Destroy() { if (_cam != null) Object.Destroy(_cam.gameObject); if (Tex != null) { Tex.Release(); Object.Destroy(Tex); } }
    }

    /// <summary>Live top-down render of the level for the minimap's TopDownCamera mode.</summary>
    internal sealed class TopDownView
    {
        readonly Camera _cam; public RenderTexture Tex; float _next;

        public TopDownView(int size)
        {
            Tex = new RenderTexture(size, size, 16);
            var go = new GameObject("RR_TopDown");
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false; _cam.targetTexture = Tex; _cam.orthographic = true; _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.06f, 0.06f); _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 40f;
            _cam.allowHDR = false; _cam.allowMSAA = false; _cam.useOcclusionCulling = false;
        }

        public void Render(Vector3 center, float yaw, float range, int cullingMask, float fps)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / Mathf.Max(1f, fps);
            _cam.cullingMask = cullingMask; _cam.orthographicSize = range;
            // camera sits 2.2 m above the viewer: roofs and upper floors are behind the camera and get clipped away
            _cam.transform.SetPositionAndRotation(center + Vector3.up * 2.2f, Quaternion.Euler(90f, yaw, 0f));
            _cam.Render();
        }

        public void Destroy() { if (_cam != null) Object.Destroy(_cam.gameObject); if (Tex != null) { Tex.Release(); Object.Destroy(Tex); } }
    }
}
