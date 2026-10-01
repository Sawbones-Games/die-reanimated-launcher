/*
 * Minimal reflection, and a log.
 *
 * Deliberately small and self-contained: everything here returns null rather than throwing, so a member
 * this build does not have degrades to a disabled feature instead of an exception every frame.
 *
 * The type cache matters more than it looks: FindType's fallback calls Assembly.GetTypes() on every
 * loaded assembly, and this is on a per-frame path. Uncached, that has been measured at roughly half a
 * megabyte of garbage per rendered frame — a visible stutter on its own.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DieGamepad
{
    static class Ref
    {
        public const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();

        public static Type FindType(string name)
        {
            Type cached;
            if (_types.TryGetValue(name, out cached)) return cached;   // negatives cached too
            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    found = asm.GetType(name, false);
                    if (found != null) break;
                    foreach (var t in asm.GetTypes())
                        if (t.Name == name) { found = t; break; }
                    if (found != null) break;
                }
                catch (Exception) { }
            }
            _types[name] = found;
            return found;
        }

        /// <summary>Property or field, public or not, anywhere up the base chain.</summary>
        public static object Get(object target, string name)
        {
            if (target == null) return null;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, AnyInstance);
                if (p != null && p.CanRead) { try { return p.GetValue(target, null); } catch (Exception) { return null; } }
                var f = t.GetField(name, AnyInstance);
                if (f != null) { try { return f.GetValue(target); } catch (Exception) { return null; } }
            }
            return null;
        }

        public static object GetStatic(Type t, string name)
        {
            if (t == null) return null;
            var p = t.GetProperty(name, AnyStatic);
            if (p != null && p.CanRead) { try { return p.GetValue(null, null); } catch (Exception) { return null; } }
            var f = t.GetField(name, AnyStatic);
            if (f != null) { try { return f.GetValue(null); } catch (Exception) { return null; } }
            return null;
        }

        public static object CallStatic(Type t, string name, params object[] args)
        {
            if (t == null) return null;
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i] == null ? typeof(object) : args[i].GetType();
            var m = t.GetMethod(name, AnyStatic, null, types, null);
            if (m == null) return null;
            try { return m.Invoke(null, args); } catch (Exception) { return null; }
        }

        public static object Call(object target, string name, params object[] args)
        {
            if (target == null) return null;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name, AnyInstance, null, Type.EmptyTypes, null);
                if (m != null && args.Length == 0) { try { return m.Invoke(target, null); } catch (Exception) { return null; } }
            }
            return null;
        }

        public static bool IsTrue(object o) { return o is bool && (bool)o; }
    }

    static class Log
    {
        static string _path;
        static readonly object _lock = new object();

        public static void Open(string path)
        {
            _path = path;
            try { File.WriteAllText(_path, "# gamepad payload " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine); }
            catch (Exception) { _path = null; }
        }

        public static void Line(string s)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s;
            UnityEngine.Debug.Log("[Gamepad] " + s);
            if (_path == null) return;
            lock (_lock)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine); } catch (Exception) { }
            }
        }
    }
}
