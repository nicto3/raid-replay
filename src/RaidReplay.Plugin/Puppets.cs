using System.Collections.Generic;
using RaidReplay.Core;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// Stylised humanoid built from primitives and animated procedurally from the recorded data (walk / run cycle by speed,
    /// crouch, prone, lean, weapon raise when aiming, muzzle flash, collapse on death). Pure Unity: cannot break on a game update.
    /// A model-based puppet (real gear and animations) would replace this class only.
    /// </summary>
    internal sealed class Puppet
    {
        public readonly EntityInfo Info;
        readonly GameObject _root;
        readonly Transform _hips, _torso, _headPivot, _armL, _armR, _legL, _legR, _kneeL, _kneeR, _gun, _flash;
        readonly List<Renderer> _body = new List<Renderer>(), _arms = new List<Renderer>(), _head = new List<Renderer>(), _torsoR = new List<Renderer>(), _legs = new List<Renderer>();
        readonly List<Renderer> _all = new List<Renderer>();
        readonly Color _color, _limb, _skin;
        float _phase, _flashUntil, _crouchBlend, _lieBlend;
        bool _visible = true;
        public Vector3 HeadPos => _headPivot.position;
        public Quaternion LookRot { get; private set; } = Quaternion.identity;
        public bool Alive { get; private set; } = true;

        public Puppet(EntityInfo info, bool isPovEntity)
        {
            Info = info;
            _color = isPovEntity ? new Color(0.88f, 0.88f, 0.84f)
                   : info.Side == EntitySide.Usec ? new Color(0.26f, 0.45f, 0.82f)
                   : info.Side == EntitySide.Bear ? new Color(0.78f, 0.27f, 0.22f)
                   : new Color(0.82f, 0.6f, 0.24f);
            _limb = Dark(_color, 0.72f);
            _skin = new Color(0.86f, 0.72f, 0.62f);

            _root = new GameObject("RR_" + info.Nickname);
            _hips = Pivot("hips", _root.transform, new Vector3(0f, 0.95f, 0f));
            _torso = Pivot("torso", _hips, Vector3.zero);
            Part(PrimitiveType.Cube, _torso, new Vector3(0f, 0.30f, 0f), new Vector3(0.42f, 0.56f, 0.24f), _color, _torsoR);
            _headPivot = Pivot("head", _root.transform, new Vector3(0f, 1.62f, 0f));
            Part(PrimitiveType.Sphere, _headPivot, new Vector3(0f, 0.02f, 0f), Vector3.one * 0.24f, _skin, _head);
            Part(PrimitiveType.Cube, _headPivot, new Vector3(0f, 0.07f, 0.0f), new Vector3(0.26f, 0.09f, 0.26f), Dark(_color, 0.5f), _torsoR); // helmet band (hidden together with the torso)

            _armL = Pivot("armL", _root.transform, Vector3.zero); Part(PrimitiveType.Cube, _armL, new Vector3(0f, -0.27f, 0f), new Vector3(0.10f, 0.56f, 0.10f), _limb, _arms);
            _armR = Pivot("armR", _root.transform, Vector3.zero); Part(PrimitiveType.Cube, _armR, new Vector3(0f, -0.27f, 0f), new Vector3(0.10f, 0.56f, 0.10f), _limb, _arms);

            _legL = Pivot("legL", _hips, new Vector3(-0.10f, 0f, 0f)); Part(PrimitiveType.Cube, _legL, new Vector3(0f, -0.225f, 0f), new Vector3(0.15f, 0.46f, 0.15f), _limb, _legs);
            _kneeL = Pivot("kneeL", _legL, new Vector3(0f, -0.45f, 0f)); Part(PrimitiveType.Cube, _kneeL, new Vector3(0f, -0.23f, 0.02f), new Vector3(0.13f, 0.46f, 0.15f), Dark(_limb, 0.8f), _legs);
            _legR = Pivot("legR", _hips, new Vector3(0.10f, 0f, 0f)); Part(PrimitiveType.Cube, _legR, new Vector3(0f, -0.225f, 0f), new Vector3(0.15f, 0.46f, 0.15f), _limb, _legs);
            _kneeR = Pivot("kneeR", _legR, new Vector3(0f, -0.45f, 0f)); Part(PrimitiveType.Cube, _kneeR, new Vector3(0f, -0.23f, 0.02f), new Vector3(0.13f, 0.46f, 0.15f), Dark(_limb, 0.8f), _legs);

            _gun = Pivot("gun", _root.transform, Vector3.zero);
            Part(PrimitiveType.Cube, _gun, new Vector3(0f, 0f, 0.25f), new Vector3(0.05f, 0.08f, 0.72f), new Color(0.1f, 0.1f, 0.1f), _body);
            Part(PrimitiveType.Cube, _gun, new Vector3(0f, -0.08f, 0.05f), new Vector3(0.04f, 0.14f, 0.07f), new Color(0.14f, 0.14f, 0.14f), _body);
            _flash = Part(PrimitiveType.Sphere, _gun, new Vector3(0f, 0f, 0.68f), Vector3.one * 0.16f, new Color(1f, 0.85f, 0.35f), _body).transform;
            _flash.gameObject.SetActive(false);

            _all.AddRange(_body); _all.AddRange(_arms); _all.AddRange(_head); _all.AddRange(_torsoR); _all.AddRange(_legs);
        }

        Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = localPos; return t;
        }

        GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, List<Renderer> bucket)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);   // purely visual: must not touch game physics
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.material.color = c; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bucket.Add(r);
            return go;
        }

        static Color Dark(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
        static void Tint(List<Renderer> rs, Color c) { foreach (var r in rs) r.material.color = c; }

        public void Flash() { _flashUntil = Time.time + 0.06f; }

        /// <param name="subject">true when the camera sits in this puppet's head: hide head/torso/arms, keep legs and weapon like a first-person view.</param>
        public void Apply(Sample s, bool subject)
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool alive = s.Alive, wasAlive = Alive; Alive = alive;
            bool prone = SampleFlags.Has(s.Flags, SampleFlags.Prone), crouch = SampleFlags.Has(s.Flags, SampleFlags.Crouch);
            bool sprint = SampleFlags.Has(s.Flags, SampleFlags.Sprint), aim = SampleFlags.Has(s.Flags, SampleFlags.Aiming);
            bool lying = prone || !alive;
            float v = s.SpeedMs;

            var feet = new Vector3(s.Px, s.Py, s.Pz);
            var bodyRot = Quaternion.Euler(0f, s.BodyYaw, 0f);
            LookRot = Quaternion.Euler(s.LookPitch, s.LookYaw, s.LookRoll);
            _crouchBlend = Mathf.MoveTowards(_crouchBlend, crouch && !lying ? 1f : 0f, dt * 5f);
            _lieBlend = Mathf.MoveTowards(_lieBlend, lying ? 1f : 0f, dt * (alive ? 3f : 4.5f));   // fall-down animation on death, crawl-down when going prone

            // walk / run cycle
            _phase += v * dt * (sprint ? 2.6f : 3.8f);
            float amp = Mathf.Clamp01(v / (sprint ? 4.5f : 2.2f)) * (sprint ? 58f : 34f) * (1f - _crouchBlend * 0.45f);
            float sw = Mathf.Sin(_phase) * amp;

            // root: upright, or tipped over onto belly / back
            float tip = _lieBlend * (alive ? 90f : (Info.Index % 2 == 0 ? 90f : -90f));
            _root.transform.SetPositionAndRotation(feet + Vector3.up * Mathf.Lerp(0f, 0.13f, _lieBlend), bodyRot * Quaternion.Euler(tip, 0f, 0f));

            float hipY = Mathf.Lerp(0.95f, 0.64f, _crouchBlend);
            _hips.localPosition = new Vector3(0f, hipY, 0f);
            bool leanL = SampleFlags.Has(s.Flags, SampleFlags.LeanLeft), leanR = SampleFlags.Has(s.Flags, SampleFlags.LeanRight);
            float fwd = sprint ? 16f : _crouchBlend * 24f + 3f;
            _torso.localRotation = Quaternion.Euler(fwd, 0f, leanL ? 15f : leanR ? -15f : 0f);

            float thighBase = -_crouchBlend * 62f, kneeBase = _crouchBlend * 105f;
            _legL.localRotation = Quaternion.Euler(thighBase + sw, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(thighBase - sw, 0f, 0f);
            _kneeL.localRotation = Quaternion.Euler(kneeBase + Mathf.Max(0f, -sw) * 1.3f, 0f, 0f);
            _kneeR.localRotation = Quaternion.Euler(kneeBase + Mathf.Max(0f, sw) * 1.3f, 0f, 0f);
            if (!alive) { _legL.localRotation = Quaternion.Euler(8f, 0f, -6f); _legR.localRotation = Quaternion.Euler(-4f, 0f, 7f); _kneeL.localRotation = Quaternion.Euler(10f, 0f, 0f); _kneeR.localRotation = Quaternion.Euler(22f, 0f, 0f); }

            // head follows the recorded view; neck sits on the torso
            Vector3 neck = _torso.TransformPoint(new Vector3(0f, 0.64f, 0f));
            _headPivot.SetPositionAndRotation(neck + _torso.up * 0.02f, alive ? LookRot : _torso.rotation);

            // arms + weapon: aim = shouldered along the view, otherwise low-ready, sprint = weapon dropped + arm swing
            Vector3 shoulderL = _torso.TransformPoint(new Vector3(-0.27f, 0.54f, 0f)), shoulderR = _torso.TransformPoint(new Vector3(0.27f, 0.54f, 0f));
            _armL.position = shoulderL; _armR.position = shoulderR;
            Quaternion gunRot; Vector3 gunPos;
            if (!alive)
            {
                _armL.rotation = _torso.rotation * Quaternion.Euler(10f, 0f, 22f); _armR.rotation = _torso.rotation * Quaternion.Euler(-12f, 0f, -26f);
                gunRot = _root.transform.rotation * Quaternion.Euler(0f, 40f, 90f); gunPos = _root.transform.position + _root.transform.right * 0.5f + Vector3.up * 0.1f;
            }
            else
            {
                float drop = aim ? 0f : sprint ? 62f : 34f;
                gunRot = LookRot * Quaternion.Euler(drop, 0f, 0f);
                gunPos = (shoulderL + shoulderR) * 0.5f + gunRot * new Vector3(0.04f, -0.12f, 0.30f);
                if (sprint && !aim)
                {   // arms hold the gun while running; slight bob
                    gunPos += Vector3.up * Mathf.Abs(Mathf.Sin(_phase)) * 0.03f;
                }
                _armR.rotation = Quaternion.LookRotation(gunPos + gunRot * new Vector3(0f, -0.03f, 0.10f) - shoulderR, _torso.up) * Quaternion.Euler(-90f, 0f, 0f);
                _armL.rotation = Quaternion.LookRotation(gunPos + gunRot * new Vector3(0f, -0.02f, 0.46f) - shoulderL, _torso.up) * Quaternion.Euler(-90f, 0f, 0f);
            }
            _gun.SetPositionAndRotation(gunPos, gunRot);

            bool flash = Time.time < _flashUntil;
            if (_flash.gameObject.activeSelf != flash) _flash.gameObject.SetActive(flash);

            // colours
            if (wasAlive != alive || !alive)
            {
                var c = alive ? _color : new Color(0.33f, 0.33f, 0.33f);
                Tint(_torsoR, c); Tint(_arms, alive ? _limb : Dark(c, 0.85f)); Tint(_legs, alive ? _limb : Dark(c, 0.75f)); Tint(_head, alive ? _skin : new Color(0.5f, 0.45f, 0.42f));
            }

            // subject (camera inside this body): first-person style
            SetGroup(_head, _visible && !subject); SetGroup(_torsoR, _visible && !subject); SetGroup(_arms, _visible && !subject);
            SetGroup(_legs, _visible); SetGroup(_body, _visible);
            if (!flash && _flash.gameObject.activeSelf) _flash.gameObject.SetActive(false);
        }

        static void SetGroup(List<Renderer> rs, bool on) { foreach (var r in rs) if (r != null && r.enabled != on) r.enabled = on; }

        public bool HeadVisible => _head.Count > 0 && _head[0].enabled;
        public void ForceHeadVisible(bool v) { SetGroup(_head, v); }
        public void SetVisible(bool v) { _visible = v; if (!v) foreach (var r in _all) if (r != null) r.enabled = false; }
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
        public void Flash(int idx) { if (_puppets.TryGetValue(idx, out var p)) p.Flash(); }

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
