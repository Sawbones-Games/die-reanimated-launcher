/*
 * "Which device am I playing with?" — the one setting the player actually needs, and the behaviour it
 * selects.
 *
 * TWO values, and Keyboard & Mouse is the default:
 *
 *   Keyboard & Mouse  the mod synthesises nothing at all. A plugged-in pad is ignored completely.
 *   Gamepad           the pad owns input outright: the mouse and keyboard are swallowed while our window
 *                     has focus (InputBlock), so the two devices cannot fight over the cursor.
 *
 * There used to be a third, "Automatic", where both devices stayed live and touching the mouse handed
 * control back. It is gone on purpose. Half of it never worked as advertised — with an ANCHORED aim the
 * cursor is rewritten every frame, so "both live" really meant the pad winning every frame and the mouse
 * fighting it — and the other half made the mod's behaviour hard to predict from the setting. Two values
 * mean the setting says exactly what happens.
 *
 * A pad is also HARDWARE, so the choice can be impossible: with nothing plugged in, Gamepad would
 * synthesise nothing and block nothing, which is Keyboard & Mouse wearing the wrong label. PadPresent
 * gates the behaviour (below) and the options row is disabled outright while it is false — but the stored
 * CHOICE is left alone, because a wireless pad that has gone to sleep must not silently rewrite the
 * player's preference.
 *
 * Persisted to Gamepad.cfg rather than PlayerPrefs, deliberately: the file sits next to the exe and is
 * shared by BOTH clients, so choosing the pad in the Crib means the match client comes up the same way.
 * PlayerPrefs is keyed per Unity product and the two clients are separate products, so it would not carry.
 */

using System;
using System.Collections.Generic;
using System.IO;

namespace DieGamepad
{
    // Values ARE the dropdown's item indices — the game's own options widgets save a SaveAsInt dropdown as
    // the selected index (GUI_OptionsDropdownList.SaveData / UI_DropdownMenu.Save), and OptionsUi reads the
    // setting back out of that store. So index 0 is both "the first item" and the default, which is why
    // Keyboard & Mouse is first: an option the game has never saved reads as 0.
    enum InputDevice { KeyboardMouse = 0, Gamepad = 1 }

    static class DeviceSetting
    {
        /// <summary>Identifier handed to the game's own options widgets. Ours, not one of theirs.</summary>
        public const string OptionId = "GamepadInputDevice";

        /// <summary>Shown in the dropdown, in enum order.</summary>
        public static readonly string[] Labels = { "Keyboard & Mouse", "Gamepad (in development)" };

        /// <summary>What the player has CHOSEN. Not the same thing as what is in effect — see
        /// <see cref="PadAllowed"/>.</summary>
        public static InputDevice Value = InputDevice.KeyboardMouse;

        /// <summary>Is there a controller to play with? Asked of XInput (or of the test seam, which fakes a
        /// connected pad), cached there so both this and the Driver can ask it freely.</summary>
        public static bool PadPresent { get { return Inject.Active ? Inject.State.Connected : XInput.Present; } }

        /// <summary>May the mod synthesise anything at all? The player has to have chosen the pad AND have
        /// one plugged in: without the hardware check, "Gamepad" with nothing connected would leave the mod
        /// running a loop that presses nothing and a setting that describes nothing.</summary>
        public static bool PadAllowed { get { return Value == InputDevice.Gamepad && PadPresent; } }

        public static string Label { get { return Labels[(int)Value]; } }

        public static InputDevice FromLabel(string s)
        {
            if (s != null)
                for (int i = 0; i < Labels.Length; i++)
                    if (string.Equals(Labels[i], s.Trim(), StringComparison.OrdinalIgnoreCase))
                        return (InputDevice)i;
            return InputDevice.KeyboardMouse;
        }

        /// <summary>Parse the cfg token. "auto" is still accepted because configs written by the version
        /// that had three values are out there; it now means the default, Keyboard & Mouse.</summary>
        public static InputDevice Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return InputDevice.KeyboardMouse;
            switch (s.Trim().ToLowerInvariant())
            {
                case "gamepad": case "pad": case "controller": return InputDevice.Gamepad;
                default: return InputDevice.KeyboardMouse;
            }
        }

        public static string ToToken(InputDevice v)
        {
            return v == InputDevice.Gamepad ? "gamepad" : "keyboard";
        }

        static string _cfgPath;
        public static void Bind(string cfgPath) { _cfgPath = cfgPath; }

        /// <summary>Change the value and write it back. Called from the options poll once the player has
        /// pressed Apply, so it has to be cheap and must never throw into the game's UI code.</summary>
        public static void Set(InputDevice v)
        {
            if (v == Value) return;
            Value = v;
            Log.Line("input device -> " + Label);
            Save();
        }

        /// <summary>Rewrite only the inputDevice line, leaving the player's layout and tuning untouched.
        /// A full rewrite from defaults would silently discard their edits every time they flipped this.</summary>
        static void Save()
        {
            if (_cfgPath == null) return;
            try
            {
                var lines = File.Exists(_cfgPath)
                    ? new List<string>(File.ReadAllLines(_cfgPath))
                    : new List<string>();

                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].TrimStart();
                    if (!t.StartsWith("inputDevice", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 0) continue;
                    lines[i] = "inputDevice=" + ToToken(Value);
                    replaced = true;
                    break;
                }
                if (!replaced) lines.Insert(0, "inputDevice=" + ToToken(Value));

                File.WriteAllLines(_cfgPath, lines.ToArray());
            }
            catch (Exception ex) { Log.Line("could not save input device: " + ex.Message); }
        }
    }
}
