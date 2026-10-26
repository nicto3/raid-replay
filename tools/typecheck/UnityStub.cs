// Minimal stand-in for UnityEngine.CoreModule: only enough surface for the compiler to type-check the EFT-facing plugin files.
using System;
using System.Collections.Generic;
namespace UnityEngine.Events { public delegate void UnityAction(); public class UnityEvent { public void AddListener(UnityAction a) { } public void RemoveAllListeners() { } public void Invoke() { } } }
namespace UnityEngine
{
    public class Object { public string name; public static void Destroy(Object o) { } public static T FindObjectOfType<T>() where T : Object => null;
        public static T Instantiate<T>(T original, Transform parent) where T : Object => null; public static Object Instantiate(Object original) => null;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b); public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null); public override bool Equals(object o) => base.Equals(o); public override int GetHashCode() => 0; }
    public class Component : Object { public GameObject gameObject; public Transform transform;
        public T GetComponent<T>() => default; public T GetComponentInChildren<T>() => default; public T[] GetComponentsInChildren<T>(bool includeInactive) => null; public T[] GetComponentsInChildren<T>() => null; }
    public class Behaviour : Component { public bool enabled; public bool isActiveAndEnabled; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object { public GameObject(string n) { } public bool activeSelf; public Transform transform; public int layer;
        public void SetActive(bool b) { } public T AddComponent<T>() where T : Component => null; public T GetComponent<T>() => default; public T GetComponentInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => null; }
    public class Transform : Component { public Vector3 position; public Quaternion rotation; public Vector3 eulerAngles; public Transform parent; public Transform Find(string n) => null; }
    public class Renderer : Component { public bool enabled; }
    public class Camera : Behaviour { public float fieldOfView; public int cullingMask; }
    public struct Vector2 { public float x, y; public float magnitude => 0; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => default; public static Vector3 zero => default; public float magnitude => 0;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a; public static Vector3 operator -(Vector3 a, Vector3 b) => a; public static Vector3 operator *(Vector3 a, float f) => a; }
    public struct Quaternion { public Vector3 eulerAngles; public static Quaternion LookRotation(Vector3 d) => default; public static Quaternion Euler(float x, float y, float z) => default; }
    public static class Mathf { public static int RoundToInt(float f) => 0; public static float Abs(float f) => 0; public static float Sign(float f) => 0; public static int Clamp(int v, int a, int b) => v; public static float Clamp(float v, float a, float b) => v;
        public static float Max(float a, float b) => a; public static int Max(int a, int b) => a; }
    public static class Time { public static float time; public static float realtimeSinceStartup; }
    public enum KeyCode { None = 0 }
}
