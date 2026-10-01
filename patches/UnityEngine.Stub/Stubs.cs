// A compile-only facade for the UnityEngine API that DieGamepad uses. See UnityEngine.Stub.csproj for
// why this exists and what must stay true of it. Nothing here runs: the game's real UnityEngine.dll
// provides every one of these members at run time, so the bodies only need to satisfy the compiler.
//
// Member kinds (property vs field) and struct layouts must match what the runtime provides exactly — each
// one is confirmed by running the built payload, because getting it wrong is a run-time MissingMember
// rather than a build error.

using System;

namespace UnityEngine
{
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public static Vector2 zero { get { return new Vector2(0f, 0f); } }

        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float d) { return new Vector2(a.x * d, a.y * d); }

        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }

        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }

        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public class Object
    {
        public HideFlags hideFlags { get; set; }
        public string name { get; set; }

        public static Object FindObjectOfType(Type type) { return null; }
        public static void DontDestroyOnLoad(Object target) { }
        public static void Destroy(Object obj) { }

        // The overload that makes a destroyed object compare equal to null. The payload leans on this
        // deliberately (a stale FindObjectOfType result must read as null), so it has to be declared here
        // or the compiler would emit a plain reference comparison.
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }

        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    public class Component : Object
    {
        public Transform transform { get { return null; } }
        public GameObject gameObject { get { return null; } }

        public T GetComponent<T>() where T : Component { return null; }
        public Component[] GetComponents(Type type) { return null; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }

        public void SetAsFirstSibling() { }
        public void SetSiblingIndex(int index) { }
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }

        public Transform transform { get { return null; } }
        public bool activeInHierarchy { get { return false; } }
        // activeSelf, NOT activeInHierarchy, is what the skip-prompt read wants: the game sets the prompt's
        // own flag while it toggles the whole HUD root off around it, so the prompt is "showing" with an
        // inactive ancestor. Both are properties, not fields.
        public bool activeSelf { get { return false; } }

        public T AddComponent<T>() where T : Component { return null; }
        public void SetActive(bool value) { }
    }

    public class Camera : Behaviour
    {
        public static Camera main { get { return null; } }

        public Vector3 WorldToScreenPoint(Vector3 position) { return Vector3.zero; }
        public Vector3 ScreenToWorldPoint(Vector3 position) { return Vector3.zero; }
    }

    public static class Screen
    {
        public static int width { get { return 0; } }
        public static int height { get { return 0; } }
        public static bool showCursor { get; set; }
    }

    public static class Input
    {
        public static bool GetKey(KeyCode key) { return false; }
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetKeyUp(KeyCode key) { return false; }
        public static bool GetMouseButton(int button) { return false; }
        public static float GetAxis(string axisName) { return 0f; }
        public static float GetAxisRaw(string axisName) { return 0f; }
        public static string[] GetJoystickNames() { return null; }

        public static Vector3 mousePosition { get { return Vector3.zero; } }
        public static bool anyKey { get { return false; } }
    }

    public static class Time
    {
        public static float time { get { return 0f; } }
        public static float deltaTime { get { return 0f; } }
        public static float realtimeSinceStartup { get { return 0f; } }
    }

    public static class Application
    {
        public static string dataPath { get { return null; } }
    }

    public static class Resources
    {
        public static Object[] FindObjectsOfTypeAll(Type type) { return null; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public static class Mathf
    {
        public static float Min(float a, float b) { return 0f; }
        // Fields at run time, not properties.
        public const float PI = 3.14159274f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;

        public static float Abs(float f) { return 0f; }
        public static float Sqrt(float f) { return 0f; }
        public static float Pow(float f, float p) { return 0f; }
        public static float Clamp(float value, float min, float max) { return 0f; }
        public static float Clamp01(float value) { return 0f; }
        public static float Atan2(float y, float x) { return 0f; }
        public static float DeltaAngle(float current, float target) { return 0f; }
        public static int RoundToInt(float f) { return 0; }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
}
