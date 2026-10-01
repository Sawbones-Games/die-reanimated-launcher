/*
 * Writing input, at the OS level.
 *
 * The mod never patches a game method and never writes a game field: it presses the keys the player
 * already has bound and it moves the real cursor. Everything downstream — cInput, UpdateGameplayInputs,
 * the MousePosition raycast that derives AimLength/TargetAimDirection, GUI_Cursor's crosshair, the
 * hover target the input packet carries — then runs exactly as it does for a mouse player. That is why
 * a pad player is byte-identical on the wire and the server needs to know nothing.
 *
 * Two rules this file exists to enforce:
 *   1. Every key we press, we release. A stuck W outlives the process otherwise. ReleaseAll() is wired
 *      to OnDisable and OnApplicationQuit.
 *   2. Nothing is synthesised unless our window is foreground. SetCursorPos on an unfocused window
 *      drags the desktop cursor around, and injected keys would land in whatever app the player is in.
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DieGamepad
{
    static class Synth
    {
        // ── win32 ────────────────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // ── window ───────────────────────────────────────────────────────────────────────────────

        static IntPtr _hwnd = IntPtr.Zero;
        static uint _pid;

        static IntPtr Hwnd() { return _hwnd; }

        /// <summary>True when a window of THIS process is the foreground window — and, as a side effect,
        /// the one place our HWND is learned.
        ///
        /// <para>Asked of the OS rather than of the client, because <c>UnityClient._GameHasFocus</c>
        /// DEFAULTS TRUE and is only cleared by OnApplicationFocus(false): a client that never held focus
        /// still believes it has it (the trap behind reference_game_aim_hold).</para>
        ///
        /// <para>Asked by PROCESS ID rather than via Process.MainWindowHandle, which is what the first
        /// build did and which never returned a usable handle here: that property is a .NET convenience
        /// that Mono 2.x does not implement usefully, so the mod sat idle believing it was in the
        /// background while the window was demonstrably focused. GetForegroundWindow + the owning pid is
        /// the same question asked in a way the runtime cannot get wrong.</para></summary>
        public static bool Foreground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (_pid == 0) _pid = GetCurrentProcessId();

            uint owner;
            GetWindowThreadProcessId(fg, out owner);
            if (owner != _pid) return false;

            _hwnd = fg;     // whatever window of ours has focus is the one to map coordinates against
            return true;
        }

        // ── cursor ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Last position we commanded, in screen pixels. Diagnostic only now — the status line
        /// reports it — but it is also the pointer's own memory of where it put the cursor.</summary>
        public static int CommandedX { get; private set; }
        public static int CommandedY { get; private set; }

        /// <summary>Adopt wherever the cursor is NOW as the baseline, without moving it. Called when pad
        /// mode is entered, so the first status line reports the truth rather than a position commanded
        /// who-knows-when.</summary>
        public static void SyncCommanded()
        {
            int x, y;
            GetCursor(out x, out y);
            CommandedX = x; CommandedY = y;
        }

        public static void GetCursor(out int x, out int y)
        {
            POINT p;
            if (GetCursorPos(out p)) { x = p.X; y = p.Y; }
            else { x = 0; y = 0; }
        }

        /// <summary>Move the cursor to a point given in UNITY screen coordinates (client area, origin at
        /// bottom-left). Converts to Windows screen coordinates (top-left, whole desktop) on the way.</summary>
        public static void CursorToUnityPoint(float ux, float uy)
        {
            var p = new POINT { X = Mathf.RoundToInt(ux), Y = Mathf.RoundToInt(Screen.height - uy) };
            IntPtr h = Hwnd();
            if (h != IntPtr.Zero) ClientToScreen(h, ref p);
            SetCursorPos(p.X, p.Y);
            CommandedX = p.X; CommandedY = p.Y;
        }

        // ── keys and mouse buttons ───────────────────────────────────────────────────────────────

        static readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();

        public static bool IsHeld(KeyCode k) { return _held.Contains(k); }
        public static bool AnyHeld { get { return _held.Count > 0; } }
        public static IEnumerable<KeyCode> Held { get { return _held; } }

        /// <summary>Idempotent: press on the rising edge, release on the falling one, nothing in between.
        /// Re-sending a keydown every frame would be harmless for movement but would retrigger anything
        /// the game treats as a fresh press.</summary>
        public static void SetHeld(KeyCode k, bool down)
        {
            if (k == KeyCode.None) return;
            bool was = _held.Contains(k);
            if (was == down) return;
            if (down) { if (Send(k, true)) _held.Add(k); }
            else { Send(k, false); _held.Remove(k); }
        }

        public static void ReleaseAll()
        {
            if (_held.Count == 0) return;
            var copy = new List<KeyCode>(_held);
            foreach (var k in copy) { Send(k, false); }
            _held.Clear();
            Log.Line("released " + copy.Count + " held input(s)");
        }

        static bool Send(KeyCode k, bool down)
        {
            uint mouseFlag = MouseFlag(k, down);
            if (mouseFlag != 0) return SendMouse(mouseFlag);

            ushort vk = VirtualKey(k);
            if (vk == 0) { Log.Line("no virtual-key mapping for " + k); return false; }

            var inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].u.ki.wVk = vk;
            inp[0].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            inp[0].u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
            return SendInput(1, inp, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        static bool SendMouse(uint flags)
        {
            var inp = new INPUT[1];
            inp[0].type = INPUT_MOUSE;
            inp[0].u.mi.dwFlags = flags;
            return SendInput(1, inp, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        /// <summary>The wheel is a binding in its own right here: ZoomIn/ZoomOut are stored as
        /// <c>HotkeyType.MouseWheel</c>, not as keys.</summary>
        public static void Wheel(int notches)
        {
            if (notches == 0) return;
            var inp = new INPUT[1];
            inp[0].type = INPUT_MOUSE;
            inp[0].u.mi.dwFlags = MOUSEEVENTF_WHEEL;
            inp[0].u.mi.mouseData = unchecked((uint)(notches * 120));
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        static uint MouseFlag(KeyCode k, bool down)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                case KeyCode.Mouse1: return down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                case KeyCode.Mouse2: return down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                default: return 0;
            }
        }

        /// <summary>Unity's KeyCode is SDL-ordered, not VK-ordered, so this is a real translation and not
        /// a cast. Only the ranges the game's bindings can actually produce are covered; anything else
        /// logs once and is skipped rather than pressing the wrong key.</summary>
        public static ushort VirtualKey(KeyCode k)
        {
            int c = (int)k;

            if (k >= KeyCode.A && k <= KeyCode.Z) return (ushort)(0x41 + (c - (int)KeyCode.A));
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return (ushort)(0x30 + (c - (int)KeyCode.Alpha0));
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return (ushort)(0x60 + (c - (int)KeyCode.Keypad0));
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return (ushort)(0x70 + (c - (int)KeyCode.F1));

            switch (k)
            {
                case KeyCode.Backspace: return 0x08;
                case KeyCode.Tab: return 0x09;
                case KeyCode.Return: return 0x0D;
                case KeyCode.KeypadEnter: return 0x0D;
                case KeyCode.Escape: return 0x1B;
                case KeyCode.Space: return 0x20;
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.End: return 0x23;
                case KeyCode.Home: return 0x24;
                case KeyCode.LeftArrow: return 0x25;
                case KeyCode.UpArrow: return 0x26;
                case KeyCode.RightArrow: return 0x27;
                case KeyCode.DownArrow: return 0x28;
                case KeyCode.Insert: return 0x2D;
                case KeyCode.Delete: return 0x2E;
                case KeyCode.LeftShift: return 0xA0;
                case KeyCode.RightShift: return 0xA1;
                case KeyCode.LeftControl: return 0xA2;
                case KeyCode.RightControl: return 0xA3;
                case KeyCode.LeftAlt: return 0xA4;
                case KeyCode.RightAlt: return 0xA5;
                case KeyCode.KeypadMultiply: return 0x6A;
                case KeyCode.KeypadPlus: return 0x6B;
                case KeyCode.KeypadMinus: return 0x6D;
                case KeyCode.KeypadPeriod: return 0x6E;
                case KeyCode.KeypadDivide: return 0x6F;
                case KeyCode.Semicolon: return 0xBA;
                case KeyCode.Equals: return 0xBB;
                case KeyCode.Comma: return 0xBC;
                case KeyCode.Minus: return 0xBD;
                case KeyCode.Period: return 0xBE;
                case KeyCode.Slash: return 0xBF;
                case KeyCode.BackQuote: return 0xC0;
                case KeyCode.LeftBracket: return 0xDB;
                case KeyCode.Backslash: return 0xDC;
                case KeyCode.RightBracket: return 0xDD;
                case KeyCode.Quote: return 0xDE;
                default: return 0;
            }
        }
    }
}
