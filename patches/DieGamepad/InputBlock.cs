/*
 * Making "Gamepad" actually MEAN gamepad.
 *
 * Picking Gamepad used to leave the keyboard and mouse half-alive rather than off: the keys still reached
 * the game, and the mouse fought the pad for the cursor every frame (we rewrite it, the player moves it, we
 * rewrite it) — which is worse than either device working on its own. The setting promised exclusivity and
 * did not deliver it.
 *
 * The fix is a pair of low-level hooks that swallow input coming from the REAL devices while the mode is
 * on. Our own synthesised events carry the INJECTED flag and pass straight through, so the pad keeps
 * working through exactly the same path as before.
 *
 * Safety, because a hook that eats the keyboard can lock somebody out of their machine:
 *   - it is installed only while the mode is Gamepad AND our window is foreground, and is torn down on
 *     focus loss, on disable and on quit;
 *   - Alt, Ctrl, Win, Escape and the F-keys are NEVER swallowed, so Alt+Tab, Alt+F4 and Ctrl+Alt+Del
 *     always work — there is always a way out even if everything else here is wrong;
 *   - the callback does no allocation and no game work, because it runs on the input path for the whole
 *     desktop. Windows silently drops a hook that is too slow (LowLevelHooksTimeout), which is a backstop
 *     rather than something to rely on.
 */

using System;
using System.Runtime.InteropServices;

namespace DieGamepad
{
    static class InputBlock
    {
        const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, HC_ACTION = 0;
        const uint LLMHF_INJECTED = 0x01, LLKHF_INJECTED = 0x10;

        // Virtual keys we refuse to swallow, so the player can always escape the mode.
        const int VK_ESCAPE = 0x1B, VK_TAB = 0x09;
        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        const int VK_F1 = 0x70, VK_F24 = 0x87;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public int x, y;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);

        static IntPtr _kbHook = IntPtr.Zero, _mouseHook = IntPtr.Zero;
        // Held in statics so the GC cannot collect the delegates while Windows still holds the pointers.
        static HookProc _kbProc, _mouseProc;
        static bool _swallow;

        public static bool Installed { get { return _kbHook != IntPtr.Zero || _mouseHook != IntPtr.Zero; } }

        /// <summary>Turn blocking on or off. Cheap to call every frame — it only acts on a change.</summary>
        public static void Apply(bool wanted)
        {
            _swallow = wanted;
            if (wanted && !Installed) Install();
            else if (!wanted && Installed) Remove();
        }

        static void Install()
        {
            try
            {
                _kbProc = KeyboardProc;
                _mouseProc = MouseProc;
                IntPtr mod = GetModuleHandle(null);
                _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, mod, 0);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
                Log.Line("keyboard/mouse blocked (device = Gamepad); Alt, Ctrl, Win, Esc and F-keys still pass");
                if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
                    Log.Line("warning: a hook failed to install (kb=" + _kbHook + " mouse=" + _mouseHook + ")");
            }
            catch (Exception ex) { Log.Line("could not install input hooks: " + ex.Message); }
        }

        public static void Remove()
        {
            try
            {
                if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
                if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                _kbProc = null; _mouseProc = null;
                _swallow = false;
                Log.Line("keyboard/mouse unblocked");
            }
            catch (Exception) { }
        }

        static IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HC_ACTION && _swallow)
            {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if ((k.flags & LLKHF_INJECTED) == 0 && !IsEscapeHatch((int)k.vkCode))
                    return (IntPtr)1;      // a real key press, and not one of the ways out: drop it
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HC_ACTION && _swallow)
            {
                var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if ((m.flags & LLMHF_INJECTED) == 0)
                    return (IntPtr)1;      // real mouse movement/buttons: drop, so nothing fights the pad
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>Keys that must always reach Windows, whatever the mode says.</summary>
        static bool IsEscapeHatch(int vk)
        {
            if (vk == VK_ESCAPE || vk == VK_TAB) return true;
            if (vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU) return true;
            if (vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_LMENU || vk == VK_RMENU) return true;
            if (vk == VK_LWIN || vk == VK_RWIN) return true;
            if (vk >= VK_F1 && vk <= VK_F24) return true;
            return false;
        }
    }
}
