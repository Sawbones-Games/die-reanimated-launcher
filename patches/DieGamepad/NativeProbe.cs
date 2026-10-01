/*
 * Does UNITY see the pad's buttons?
 *
 * The whole native-binding idea rests on one fact we cannot check from a script: that
 * Input.GetKey(KeyCode.JoystickButton0..19) reports this controller. Buttons should need no
 * InputManager configuration (unlike named axes), but "should" is not evidence, and everything a test
 * can synthesise is keyboard/mouse — there is no way to fake a joystick button.
 *
 * If Unity does see them, actions can be bound to those KeyCodes through cInput and the game renders the
 * prompts itself, everywhere, with no per-widget patching. If it does not, the binding route is dead and
 * the overlay stays.
 *
 * Logs the joystick names once, then a line whenever the set of pressed buttons changes. Harmless to
 * leave in: it allocates nothing while nothing is pressed.
 */

using System;
using System.Text;
using UnityEngine;

namespace DieGamepad
{
    class NativeProbe : MonoBehaviour
    {
        const int First = (int)KeyCode.JoystickButton0;
        const int Count = 20;

        int _lastMask = -1;
        bool _saidNames;

        void Update()
        {
            if (!_saidNames)
            {
                _saidNames = true;
                string[] names;
                try { names = Input.GetJoystickNames(); } catch (Exception) { names = null; }
                Log.Line("probe: KeyCode.JoystickButton0 = " + First
                         + ", Input.GetJoystickNames() = ["
                         + (names == null ? "?" : string.Join(", ", names)) + "]");
            }

            int mask = 0;
            for (int i = 0; i < Count; i++)
            {
                bool down;
                try { down = Input.GetKey((KeyCode)(First + i)); }
                catch (Exception) { return; }
                if (down) mask |= 1 << i;
            }

            if (mask == _lastMask) return;
            _lastMask = mask;

            if (mask == 0) { Log.Line("probe: (released)"); return; }

            var sb = new StringBuilder("probe: UNITY SEES JoystickButton");
            for (int i = 0; i < Count; i++)
                if ((mask & (1 << i)) != 0) sb.Append(' ').Append(i);
            Log.Line(sb.ToString());
        }
    }
}
