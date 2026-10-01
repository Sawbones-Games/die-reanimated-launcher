/*
 * Binding the pad into the game's OWN keybinding system.
 *
 * The overlay approach (Glyphs) rewrites one widget's text. That never scales: every prompt the game can
 * draw — "press X to start capturing", tooltips, the Controls tab — would need its own patch. Binding is
 * the version that works with the grain of the system, and this build allows it:
 *
 *   - Unity reports the pad's ten real buttons as KeyCode.JoystickButton0..9. Verified live on an Xbox 360
 *     pad: all of 0-9 answered Input.GetKey, and nothing in 10..19 (see NativeProbe).
 *   - cInput drives a binding's state straight off the KeyCode every frame
 *     (`bool key = Input.GetKey(hotkeyData.KeyCode)`), so an action bound to a joystick button is read
 *     from the pad NATIVELY. No synthesis, no SendInput, nothing to keep in step.
 *   - The display name comes from `cInput.GetText(action)`, which resolves through a lazily-filled
 *     `_KeyCodeStringCache` keyed by KeyCode and defaulting to `keyCode.ToString()`. Seed that cache and
 *     every consumer shows our name — because PCSettings.GetKeybindText falls through to GetText for any
 *     KeyCode it does not special-case, and joystick buttons are not special-cased.
 *
 * So the game writes "(A)" into its own prompts, everywhere, by itself.
 *
 * What this canNOT cover, and why the synthesis path stays:
 *   - The TRIGGERS and the D-PAD are axes, not buttons. They hold no KeyCode, so they cannot be bound.
 *   - The modifier control is excluded on purpose: the game would react to the raw button, so holding it
 *     as a layer selector would also fire whatever it is bound to.
 *   - The sticks, obviously.
 *
 * Bindings are changed IN MEMORY only — cInput.SaveInputs() is never called — and the previous values are
 * restored when the pad stops driving, so the player's stored keyboard profile is left alone.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DieGamepad
{
    static class NativeBinds
    {
        /// <summary>Pad control -> Unity's joystick button index, for the ten buttons Unity exposes as
        /// KeyCodes. Xbox layout, confirmed against the live probe.</summary>
        static readonly Dictionary<ushort, int> ButtonIndex = new Dictionary<ushort, int>
        {
            { Btn.A, 0 }, { Btn.B, 1 }, { Btn.X, 2 }, { Btn.Y, 3 },
            { Btn.LB, 4 }, { Btn.RB, 5 }, { Btn.Back, 6 }, { Btn.Start, 7 },
            { Btn.LS, 8 }, { Btn.RS, 9 },
        };

        /// <summary>What the game will print for each of them. Bracketed so a pad prompt reads as a pad
        /// prompt next to the keyboard ones, and short enough for the ability bar's little box.</summary>
        static readonly string[] Names =
        {
            "(A)", "(B)", "(X)", "(Y)", "(LB)", "(RB)", "(BACK)", "(START)", "(LS)", "(RS)",
        };

        static readonly Dictionary<string, object> _saved = new Dictionary<string, object>();
        /// <summary>Actions currently held by the pad natively — the Driver must not also synthesise them,
        /// or every press would arrive twice.</summary>
        static readonly List<string> _owned = new List<string>();

        public static bool Active { get; private set; }

        public static bool Owns(string action)
        {
            return Active && action != null && _owned.Contains(action);
        }

        /// <summary>Is this action bound to a joystick button right now? Used by the glyph overlay to leave
        /// natively-bound actions alone — the game is already printing the right thing for them.</summary>
        public static bool IsJoystickKey(KeyCode k)
        {
            int i = (int)k - (int)KeyCode.JoystickButton0;
            return i >= 0 && i < 20;
        }

        public static void Apply(bool wanted)
        {
            if (wanted == Active) return;
            if (wanted) Install(); else Restore();
        }

        static void Install()
        {
            Type cInput = Ref.FindType("cInput");
            if (cInput == null) { Log.Line("native binds: no cInput on this build"); return; }

            var current = Ref.GetStatic(cInput, "_CurrentInputs") as IDictionary;
            if (current == null) { Log.Line("native binds: cInput._CurrentInputs unreachable"); return; }

            if (!SeedNames(cInput)) return;

            Type dataType = Ref.FindType("BoundHotkeyData");
            if (dataType == null) { Log.Line("native binds: no BoundHotkeyData"); return; }

            // The ctor is (HotkeyType, KeyCode); take the enum type off the ctor so nothing is hardcoded.
            System.Reflection.ConstructorInfo ctor = null;
            object keyboard = null;
            foreach (var c in dataType.GetConstructors())
            {
                var ps = c.GetParameters();
                if (ps.Length != 2 || !ps[0].ParameterType.IsEnum) continue;
                if (ps[1].ParameterType != typeof(KeyCode)) continue;
                ctor = c;
                try { keyboard = Enum.Parse(ps[0].ParameterType, "Keyboard"); } catch (Exception) { }
                break;
            }
            if (ctor == null || keyboard == null) { Log.Line("native binds: no (HotkeyType, KeyCode) ctor"); return; }

            _saved.Clear();
            _owned.Clear();

            var done = new List<string>();
            foreach (var kv in Cfg.Bind)
            {
                // The modifier is ours, not the game's: bound natively it would fire its own action every
                // time it was held as a layer selector.
                if (kv.Key == Cfg.ModifierBit) continue;

                int idx;
                if (!ButtonIndex.TryGetValue(kv.Key, out idx)) continue;   // trigger, d-pad or stick
                string action = kv.Value;
                if (string.IsNullOrEmpty(action) || done.Contains(action)) continue;

                var code = (KeyCode)((int)KeyCode.JoystickButton0 + idx);
                object data;
                try { data = ctor.Invoke(new object[] { keyboard, code }); }
                catch (Exception ex) { Log.Line("native binds: " + action + ": " + ex.Message); continue; }

                _saved[action] = current.Contains(action) ? current[action] : null;
                current[action] = data;
                _owned.Add(action);
                done.Add(action);
            }

            Active = true;
            Log.Line("native binds ON: " + string.Join(", ", _owned.ToArray()));
            ReportAsTheGameWouldPrintIt();
        }

        /// <summary>Ask the GAME's own formatter what it will now print for the actions we took over.
        /// This is the whole claim of the file in one line of log: if it says "(A)", then every prompt the
        /// game draws for ActionButton says "(A)", because they all come through here.</summary>
        static void ReportAsTheGameWouldPrintIt()
        {
            try
            {
                Type cInput = Ref.FindType("cInput");
                var parts = new List<string>();
                foreach (var action in _owned)
                {
                    object t = Ref.CallStatic(cInput, "GetText", action);
                    parts.Add(action + "=" + (t as string ?? "?"));
                }
                Log.Line("native binds, as the game will print them: " + string.Join("  ", parts.ToArray()));
            }
            catch (Exception) { }
        }

        static void Restore()
        {
            Type cInput = Ref.FindType("cInput");
            var current = cInput == null ? null : Ref.GetStatic(cInput, "_CurrentInputs") as IDictionary;
            if (current != null)
                foreach (var kv in _saved)
                {
                    if (kv.Value == null) current.Remove(kv.Key);   // it had no override before; fall back to the default
                    else current[kv.Key] = kv.Value;
                }

            _saved.Clear();
            _owned.Clear();
            Active = false;
            Log.Line("native binds OFF (keyboard bindings restored)");
        }

        /// <summary>Pre-fill cInput's KeyCode -> display-name cache for the joystick buttons. It is filled
        /// lazily from keyCode.ToString(), so getting in first is all it takes to have the game print
        /// "(A)" instead of "JoystickButton0" wherever it names that binding.</summary>
        static bool SeedNames(Type cInput)
        {
            var cache = Ref.GetStatic(cInput, "_KeyCodeStringCache") as IDictionary;
            if (cache == null) { Log.Line("native binds: no _KeyCodeStringCache to seed"); return false; }
            for (int i = 0; i < Names.Length; i++)
                cache[(int)KeyCode.JoystickButton0 + i] = Names[i];
            return true;
        }
    }
}
