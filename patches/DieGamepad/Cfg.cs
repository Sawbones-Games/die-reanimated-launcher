/*
 * Configuration and the button layout.
 *
 * The layout maps a pad control to an ACTION NAME, never to a key: the key is whatever the player has
 * bound in the game's own Controls tab, looked up at press time (GameRefs.BoundKey). Rebinding W to Z
 * therefore keeps working with the pad, and we never fight the player's profile.
 *
 * Forty actions exist against fourteen buttons, so there is a modifier layer — and it lives HERE rather
 * than in the bindings, because cInput can only store one KeyCode per action. "Back held + A" is our
 * concept; what reaches the game is a single ordinary key press.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DieGamepad
{
    static class Cfg
    {
        /// <summary>Bumped whenever the file's SHAPE changes — a key removed, a value renamed. A file
        /// without the current marker is REWRITTEN from whatever was just parsed out of it, which keeps
        /// every setting the mod still understands (including the player's own binds) and drops the ones
        /// it does not, together with their now-wrong comments. Without this, removing a setting leaves
        /// every existing install documenting a value that no longer exists: dropping "Automatic" left
        /// "auto | gamepad | keyboard" sitting above the one line a player is most likely to read.
        /// What does NOT survive a rewrite is anything the mod has no concept of — a comment of the
        /// player's own, or their ordering.</summary>
        public const int Format = 3;
        static int _loadedFormat;

        public static bool Enabled = true;
        /// <summary>Radius of the aim circle, as a fraction of screen height. ~0.30 puts full deflection
        /// a little inside the screen edge at 16:9, which is about 30 world units at the default zoom —
        /// hero ability ranges cluster at 20-30.</summary>
        public static float AimRadius = 0.30f;
        /// <summary>Exponent on stick magnitude for the aim. &gt;1 gives fine control near the character
        /// and a fast run to the rim.</summary>
        public static float AimCurve = 1.6f;
        /// <summary>Menu pointer speed, in fractions of screen height per second at full deflection.</summary>
        public static float PointerSpeed = 1.1f;
        public static float PointerCurve = 2.0f;
        /// <summary>Extra degrees a stick must cross before the movement octant changes. Without this a
        /// stick resting on a boundary alternates between two octants, and every change is a NEW key
        /// press — which matters because a newly-set direction bit fires InterruptCast().</summary>
        public static float OctantHysteresis = 8f;
        /// <summary>Keep the cursor this many pixels inside the window. The client pans the camera when the
        /// cursor is near an edge, a panning camera moves the anchor, and a moving anchor moves the cursor:
        /// a feedback loop that runs away.</summary>
        public static int EdgeInset = 48;
        /// <summary>Seconds between status lines in Gamepad.log. 0 disables.</summary>
        public static float LogEvery = 0f;
        /// <summary>Read pad state from Gamepad.inject instead of XInput. The verification seam: it lets
        /// the whole chain be driven and asserted without a human holding the pad. Off by default: it is a
        /// test seam, and a player's install has no reason to look for the file.</summary>
        public static bool AllowInject = false;
        /// <summary>Relabel the ability bar with pad controls while the pad is driving.</summary>
        public static bool ShowPadGlyphs = true;
        /// <summary>With the device set to Gamepad, swallow real keyboard/mouse input so the two
        /// devices cannot fight. Alt, Ctrl, Win, Esc and the F-keys always pass through.</summary>
        public static bool BlockKeyboardMouse = true;
        /// <summary>Bind the pad's real buttons into the game's own keybinding system while the pad
        /// drives, so the game reads them natively and names them in its own prompts.</summary>
        public static bool NativeBinds = true;
        /// <summary>Seconds per frame-time report in the log. 0 = off, and off means the component is never
        /// created at all. A DIAGNOSTIC: it is how a mod proves it costs the player nothing before it
        /// ships (see FrameStats.cs), not something a shipped config should carry.</summary>
        public static float FrameStats = 0f;

        public static readonly Dictionary<ushort, string> Bind = new Dictionary<ushort, string>();
        public static readonly Dictionary<ushort, string> ModBind = new Dictionary<ushort, string>();
        /// <summary>Menu layer. Values are KeyCode NAMES (or WheelUp/WheelDown), not action names: there is
        /// no "click" in the game's binding table, and a menu does not want A to fire ActionButton.</summary>
        public static readonly Dictionary<ushort, string> UiBind = new Dictionary<ushort, string>();
        /// <summary>The control that selects the modifier layer while held.</summary>
        public static ushort ModifierBit = Btn.Back;
        /// <summary>Action fired when the modifier is TAPPED alone (not used as a layer). Empty = none.
        /// The scoreboard by default: it is a toggle on the client, it is what a pad player reaches Back
        /// for, and behind a two-button combo nobody would open it mid-fight.</summary>
        public static string ModifierTapAction = "Scoreboard";

        static void Defaults()
        {
            Bind.Clear(); ModBind.Clear();

            Bind[Btn.RT] = "PrimaryAttack";
            Bind[Btn.LT] = "SecondaryAttack";
            Bind[Btn.RB] = "Mutation1";
            Bind[Btn.LB] = "Mutation2";
            Bind[Btn.X] = "Mutation3";
            Bind[Btn.Y] = "Mutation4";
            Bind[Btn.A] = "ActionButton";
            Bind[Btn.B] = "Roll";
            Bind[Btn.DPadUp] = "Bandage";
            Bind[Btn.DPadDown] = "SwapWeapon";
            Bind[Btn.DPadLeft] = "Consumable1";
            Bind[Btn.DPadRight] = "Consumable2";
            Bind[Btn.LS] = "MinimapPing";
            Bind[Btn.RS] = "SelfCast";

            ModBind[Btn.A] = "ThrowSupplies";
            ModBind[Btn.B] = "StopCast";
            ModBind[Btn.X] = "Consumable3";
            ModBind[Btn.Y] = "EmoteMenu";
            ModBind[Btn.DPadUp] = "OpenChat";
            ModBind[Btn.DPadDown] = "OpenStatisticsWindow";
            ModBind[Btn.RB] = "ZoomIn";
            ModBind[Btn.LB] = "ZoomOut";

            // Menus. A is the click, because every widget in all three UI stacks answers a real click —
            // which is the whole reason the pointer exists rather than per-widget navigation.
            UiBind[Btn.A] = "Mouse0";
            UiBind[Btn.RT] = "Mouse0";       // the trigger keeps working as "confirm", so the hand can stay put
            UiBind[Btn.B] = "Escape";        // close / back — what every window here answers to
            UiBind[Btn.X] = "Mouse1";        // right-click (context actions, e.g. equipping from a slot)
            UiBind[Btn.Y] = "Return";
            UiBind[Btn.Start] = "Escape";
            UiBind[Btn.Back] = "Tab";
            UiBind[Btn.DPadUp] = "WheelUp";
            UiBind[Btn.DPadDown] = "WheelDown";
            UiBind[Btn.RB] = "WheelDown";    // shoulder scrolling for long lists (shop, inventory)
            UiBind[Btn.LB] = "WheelUp";
        }

        /// <summary>A KeyCode by name, for the menu layer. Returns None (and the caller warns once) rather
        /// than throwing on a typo in someone's config.</summary>
        public static KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), name.Trim(), true); }
            catch (Exception) { return KeyCode.None; }
        }

        public static void Load(string path)
        {
            Defaults();
            _loadedFormat = 0;
            try
            {
                if (!File.Exists(path)) { Write(path); return; }
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    Apply(k, v);
                }

                // Format 2 and older wrote allowInject=1 as the default; the seam is opt-in from format 3.
                if (_loadedFormat < 3) AllowInject = false;

                if (_loadedFormat != Format)
                {
                    Log.Line("config was written by an older version (format " + _loadedFormat
                             + ") - rewriting it, keeping your settings");
                    Write(path);
                }
            }
            catch (Exception ex) { Log.Line("cfg read failed (" + ex.Message + ") — using defaults"); }
        }

        static void Apply(string k, string v)
        {
            string lk = k.ToLowerInvariant();
            if (lk.StartsWith("bind."))
            {
                ushort bit = Btn.Parse(k.Substring(5));
                if (bit != 0) { if (v.Length == 0 || v == "-") Bind.Remove(bit); else Bind[bit] = v; }
                return;
            }
            if (lk.StartsWith("mod."))
            {
                ushort bit = Btn.Parse(k.Substring(4));
                if (bit != 0) { if (v.Length == 0 || v == "-") ModBind.Remove(bit); else ModBind[bit] = v; }
                return;
            }
            if (lk.StartsWith("uibind."))
            {
                ushort bit = Btn.Parse(k.Substring(7));
                if (bit != 0) { if (v.Length == 0 || v == "-") UiBind.Remove(bit); else UiBind[bit] = v; }
                return;
            }

            switch (lk)
            {
                case "format": _loadedFormat = (int)Float(v, 0); break;
                case "enabled": Enabled = Bool(v); break;
                case "inputdevice": DeviceSetting.Value = DeviceSetting.Parse(v); break;
                case "aimradius": AimRadius = Float(v, AimRadius); break;
                case "aimcurve": AimCurve = Float(v, AimCurve); break;
                case "pointerspeed": PointerSpeed = Float(v, PointerSpeed); break;
                case "pointercurve": PointerCurve = Float(v, PointerCurve); break;
                case "octanthysteresis": OctantHysteresis = Float(v, OctantHysteresis); break;
                case "edgeinset": EdgeInset = (int)Float(v, EdgeInset); break;
                case "logevery": LogEvery = Float(v, LogEvery); break;
                case "allowinject": AllowInject = Bool(v); break;
                case "showpadglyphs": ShowPadGlyphs = Bool(v); break;
                case "blockkeyboardmouse": BlockKeyboardMouse = Bool(v); break;
                case "nativebinds": NativeBinds = Bool(v); break;
                case "framestats": FrameStats = Float(v, FrameStats); break;
                case "modifier": ModifierBit = Btn.Parse(v); break;
                case "modifiertap": ModifierTapAction = (v == "-" ? "" : v); break;
            }
        }

        static bool Bool(string v)
        {
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }

        static float Float(string v, float fallback)
        {
            float f;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }

        static void Write(string path)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Dead Island: Epidemic — gamepad support");
                sb.AppendLine("# Values on the right of each bind are ACTION names (the game's own, as the");
                sb.AppendLine("# Controls tab lists them); the key pressed is whatever you have bound to it.");
                sb.AppendLine("# Controls: A B X Y LB RB LT RT LS RS Back Start DPadUp DPadDown DPadLeft DPadRight");
                sb.AppendLine();
                sb.AppendLine("# Set in-game under Options > General > Input device. keyboard | gamepad");
                sb.AppendLine("# The row is greyed out while no controller is plugged in.");
                sb.AppendLine("inputDevice=" + DeviceSetting.ToToken(DeviceSetting.Value));
                sb.AppendLine("enabled=" + (Enabled ? "1" : "0"));
                sb.AppendLine("# Do not edit: says which version wrote this file.");
                sb.AppendLine("format=" + Format);
                sb.AppendLine("aimRadius=" + AimRadius.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("aimCurve=" + AimCurve.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pointerSpeed=" + PointerSpeed.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pointerCurve=" + PointerCurve.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("octantHysteresis=" + OctantHysteresis.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("edgeInset=" + EdgeInset);
                sb.AppendLine("logEvery=" + LogEvery.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("allowInject=" + (AllowInject ? "1" : "0"));
                sb.AppendLine("showPadGlyphs=" + (ShowPadGlyphs ? "1" : "0"));
                sb.AppendLine("blockKeyboardMouse=" + (BlockKeyboardMouse ? "1" : "0"));
                sb.AppendLine("nativeBinds=" + (NativeBinds ? "1" : "0"));
                sb.AppendLine("# Diagnostic: seconds between frame-time reports in the log. 0 = off.");
                sb.AppendLine("frameStats=" + FrameStats.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("modifier=" + Btn.Name(ModifierBit));
                sb.AppendLine("# Action fired when the modifier is TAPPED alone; - for none");
                sb.AppendLine("modifierTap=" + (ModifierTapAction.Length == 0 ? "-" : ModifierTapAction));
                sb.AppendLine();
                sb.AppendLine("# In-game layout: control = ACTION name");
                foreach (var kv in Bind) sb.AppendLine("bind." + Btn.Name(kv.Key) + "=" + kv.Value);
                sb.AppendLine();
                sb.AppendLine("# Held-modifier layer: control = ACTION name");
                foreach (var kv in ModBind) sb.AppendLine("mod." + Btn.Name(kv.Key) + "=" + kv.Value);
                sb.AppendLine();
                sb.AppendLine("# Menu layout: control = KeyCode NAME, or WheelUp / WheelDown.");
                sb.AppendLine("# Mouse0 = left click, Mouse1 = right click.");
                foreach (var kv in UiBind) sb.AppendLine("uibind." + Btn.Name(kv.Key) + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString());
                _loadedFormat = Format;
                Log.Line("wrote config to " + path);
            }
            catch (Exception ex) { Log.Line("could not write config: " + ex.Message); }
        }
    }
}
