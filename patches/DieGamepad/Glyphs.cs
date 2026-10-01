/*
 * Making the HUD say what the PAD does.
 *
 * With a controller in hand the ability bar still read "Q W E R" — the keys, not the buttons you are
 * actually pressing. The bar builds its labels from the player's keyboard bindings
 * (UI_AbilityBarBinding.SetAbilityData -> PCSettings.GetShortKeybindText), so the fix is to overwrite the
 * label with the pad control that maps to the same ACTION.
 *
 * Which slot is which action is knowable, and not a guess: UI_AbilityBarBinding carries an explicit
 * bar-slot -> ability-slot table
 *     0->0, 1->13, 2->7, 3->2, 4->3, 5->4, 6->5, 7->21, 8->22, 9->23
 * which is primary, throw-supplies, dodge, Mutation1..4, then the three consumables — matching the game's
 * own default bindings for those ability slots. BarActions below is that table expressed as action names,
 * and the pad control is then looked up in the SAME layout maps that do the pressing, so a rebind in
 * Gamepad.cfg changes the HUD too and the two can never disagree.
 *
 * Written in LateUpdate: UI_AbilityBar assigns the text in its own Update, and rendering happens after
 * LateUpdate, so ours is what reaches the screen.
 *
 * The tutorial intro's "Press Space to skip intro" is relabelled here too, and it is the one prompt that
 * cannot be fixed by binding: the key is hardcoded in UnityClient, so the game has nothing to print a pad
 * name FOR (see GameRefs.SkipPrompt, and Driver.SkipIntro for the press itself).
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DieGamepad
{
    class Glyphs : MonoBehaviour
    {
        /// <summary>Ability-bar position -> the action that position casts. From the binding's own
        /// bar-slot -> ability-slot table (see the file header).</summary>
        static readonly string[] BarActions =
        {
            "PrimaryAttack",   // 0  -> ability slot 0
            "ThrowSupplies",   // 1  -> 13
            "Roll",            // 2  -> 7
            "Mutation1",       // 3  -> 2
            "Mutation2",       // 4  -> 3
            "Mutation3",       // 5  -> 4
            "Mutation4",       // 6  -> 5
            "Consumable1",     // 7  -> 21
            "Consumable2",     // 8  -> 22
            "Consumable3",     // 9  -> 23
        };

        /// <summary>Short names that fit the little keybind box on a slot.</summary>
        static readonly Dictionary<ushort, string> Short = new Dictionary<ushort, string>
        {
            { Btn.A, "A" }, { Btn.B, "B" }, { Btn.X, "X" }, { Btn.Y, "Y" },
            { Btn.LB, "LB" }, { Btn.RB, "RB" }, { Btn.LT, "LT" }, { Btn.RT, "RT" },
            { Btn.LS, "LS" }, { Btn.RS, "RS" },
            { Btn.Start, "STA" }, { Btn.Back, "BK" },
            { Btn.DPadUp, "DU" }, { Btn.DPadDown, "DD" },
            { Btn.DPadLeft, "DL" }, { Btn.DPadRight, "DR" },
        };

        object _bar;
        bool _warned;
        /// <summary>What the bar said before we touched it, per slot, so the keyboard labels can be put
        /// back the moment the player switches away from the pad.</summary>
        readonly Dictionary<int, string> _original = new Dictionary<int, string>();
        bool _applied;
        bool _said;
        readonly List<string> _saidMsgs = new List<string>();

        void WarnOnce(string m) { if (_saidMsgs.Contains(m)) return; _saidMsgs.Add(m); Log.Line(m); }

        void OnDisable() { Restore(); }

        void LateUpdate()
        {
            // NOT throttled. UI_AbilityBar.UpdateAbilityBar assigns Keybind.text EVERY frame from the
            // keyboard binding, so a periodic overwrite just alternates with it: at 4 Hz the label strobed
            // between "Space" and "B" fast enough to read as flicker rather than as a wrong value. Both
            // paths now write every frame and ours is last, because LateUpdate runs after every Update.
            // The expensive parts (finding the bar, formatting the key names) stay cached below; what runs
            // per frame is a handful of cached property reads.
            bool want = Cfg.ShowPadGlyphs && DeviceSetting.PadAllowed && Driver.PadModeActive;
            try
            {
                if (want) Apply();
                else if (_applied) Restore();
                if (want) SkipPrompt();
            }
            catch (Exception ex)
            {
                if (!_warned) { _warned = true; Log.Line("glyphs failed: " + ex.Message); }
            }
        }

        void Apply()
        {
            var slots = Slots();
            if (slots == null) { WarnOnce("glyphs: no ability bar yet"); return; }
            if (!_said)
            {
                _said = true;
                object l0 = slots.Count > 0 ? LabelOf(slots[0]) : null;
                var mm = KeyTextMap();
                var parts = new List<string>();
                if (mm != null) foreach (var kv in mm) parts.Add(kv.Key + "->" + kv.Value);
                Log.Line("glyphs: " + slots.Count + " slots, slot0=" + (l0 == null ? "null" : "'" + Ref.Get(l0, "text") + "'")
                         + ", map: " + string.Join(" ", parts.ToArray()));
            }

            var byKeyText = KeyTextMap();
            if (byKeyText == null || byKeyText.Count == 0) return;

            CacheLabels(slots);

            for (int i = 0; i < _labels.Length; i++)
            {
                object label = _labels[i];
                if (label == null || _textProp == null) continue;

                string current;
                try { current = _textProp.GetValue(label, null) as string; }
                catch (Exception) { continue; }
                if (string.IsNullOrEmpty(current)) continue;
                if (_glyphValues.Contains(current)) continue;   // already ours — the bar has not rewritten it

                string glyph;
                if (!byKeyText.TryGetValue(current, out glyph)) continue;   // not an action we drive

                if (!_original.ContainsKey(i)) _original[i] = current;
                try { _textProp.SetValue(label, glyph, null); } catch (Exception) { }
            }
            _applied = true;
        }

        /// <summary>Name the pad button in the tutorial intro's skip prompt.
        ///
        /// <para>The key name is wrapped in an NGUI colour tag in every language the game ships —
        /// "Press [efc73e]Space[-] to skip intro", "Appuyez sur [efc73e]Espace[-] pour passer l'intro" — so
        /// replacing what sits BETWEEN those markers needs no per-language word list and leaves the
        /// sentence around it alone. A string that does not carry them is left untouched and said once,
        /// which is also how a future build that reworded it would announce itself.</para>
        ///
        /// <para>No restore: the label belongs to the intro and is destroyed with it, and the prompt is
        /// gone by the time anything could switch back to the keyboard.</para></summary>
        void SkipPrompt()
        {
            object label = GameRefs.SkipPrompt();
            if (label == null) return;

            string text = Ref.Get(label, "text") as string;
            if (string.IsNullOrEmpty(text) || text.IndexOf(SkipGlyph, StringComparison.Ordinal) >= 0) return;

            int open = text.IndexOf(ColourOpen, StringComparison.Ordinal);
            int close = open < 0 ? -1 : text.IndexOf(ColourClose, open + ColourOpen.Length, StringComparison.Ordinal);
            if (close < 0) { WarnOnce("glyphs: skip prompt has no key to replace: '" + text + "'"); return; }

            int from = open + ColourOpen.Length;
            TrySetText(label, text.Substring(0, from) + SkipGlyph + text.Substring(close));
        }

        /// <summary>The colour tag the game wraps every in-sentence key name in, and what we put inside it.
        /// Bracketed like the names NativeBinds hands the game, so one pad prompt reads like another.</summary>
        const string ColourOpen = "[efc73e]", ColourClose = "[-]", SkipGlyph = "(A)";

        void Restore()
        {
            try
            {
                var slots = Slots();
                if (slots != null)
                    foreach (var kv in _original)
                    {
                        if (kv.Key >= slots.Count) continue;
                        object label = LabelOf(slots[kv.Key]);
                        if (label != null) TrySetText(label, kv.Value);
                    }
            }
            catch (Exception) { }
            _original.Clear();
            _applied = false;
        }

        object[] _labels = new object[0];
        System.Reflection.PropertyInfo _textProp;

        /// <summary>Resolve the slot label objects and the Text.text property ONCE per bar, so the
        /// per-frame pass is a cached property get plus an occasional set rather than a reflection walk
        /// over ten slots every frame.</summary>
        void CacheLabels(IList slots)
        {
            if (_labels.Length == slots.Count && (_labels.Length == 0 || _labels[0] != null)) return;

            _labels = new object[slots.Count];
            for (int i = 0; i < slots.Count; i++) _labels[i] = LabelOf(slots[i]);

            _textProp = null;
            foreach (var l in _labels)
            {
                if (l == null) continue;
                for (Type t = l.GetType(); t != null && _textProp == null; t = t.BaseType)
                {
                    var p = t.GetProperty("text", Ref.AnyInstance);
                    if (p != null && p.CanWrite && p.CanRead) _textProp = p;
                }
                if (_textProp != null) break;
            }
        }

        Dictionary<string, string> _keyText;
        readonly List<string> _glyphValues = new List<string>();
        float _keyTextBuilt;

        /// <summary>KEY LABEL the bar is showing -> the pad control to show instead.
        ///
        /// <para>Matching on the text rather than on the slot position, because the position is not what it
        /// looks like: the binding's bar-slot table says slot 0 is the primary attack, but GUIElements[0]
        /// came back labelled 'Q' (Mutation1) on a live bar — the array is not in that order. The label
        /// itself is unambiguous, and asking the game's OWN formatter for it
        /// (PCSettings.GetShortKeybindText, which is what UI_AbilityBarBinding uses) means our key string
        /// is character-identical to the one on screen, including its odd cases (LMB, Space, …).</para></summary>
        Dictionary<string, string> KeyTextMap()
        {
            if (_keyText != null && Time.realtimeSinceStartup - _keyTextBuilt < 5f) return _keyText;

            object settings = PcSettings();
            if (settings == null) { WarnOnce("glyphs: no PCSettings to format key names with"); return _keyText; }
            var m = settings.GetType().GetMethod("GetShortKeybindText", Ref.AnyInstance);
            if (m == null) { WarnOnce("glyphs: GetShortKeybindText missing on this build"); return _keyText; }

            var map = new Dictionary<string, string>();
            _glyphValues.Clear();
            foreach (var kv in Cfg.Bind) Add(map, m, settings, kv.Value, Name(kv.Key));
            foreach (var kv in Cfg.ModBind)
                Add(map, m, settings, kv.Value,
                    (Cfg.ModifierBit != 0 ? Name(Cfg.ModifierBit) + "+" : "") + Name(kv.Key));
            if (!string.IsNullOrEmpty(Cfg.ModifierTapAction))
                Add(map, m, settings, Cfg.ModifierTapAction, Name(Cfg.ModifierBit));

            _keyText = map;
            _keyTextBuilt = Time.realtimeSinceStartup;
            return _keyText;
        }

        void Add(Dictionary<string, string> map, System.Reflection.MethodInfo m, object settings,
                 string action, string glyph)
        {
            if (string.IsNullOrEmpty(action)) return;
            // Already bound natively: the game prints the pad name for it on its own, and re-mapping that
            // name here would just fight itself.
            if (NativeBinds.Owns(action)) return;
            object[] args = { action, false };
            string text;
            try { text = m.Invoke(settings, args) as string; }
            catch (Exception) { return; }
            if (string.IsNullOrEmpty(text)) return;

            // GetShortKeybindText hands back a LOCALISATION KEY for the named keys (LMB, Space, Return …)
            // and sets its out-param to say so; UI_AbilityBarBinding then runs it through LocalizedString.
            // Skipping that left the map holding "PCStrings.…ShortKeybinds.Space" while the bar displayed
            // "Space", so the dodge slot never matched and kept its keyboard label.
            if (args.Length > 1 && args[1] is bool && (bool)args[1]) text = Localize(text);
            if (string.IsNullOrEmpty(text) || map.ContainsKey(text)) return;
            map[text] = glyph;
            if (!_glyphValues.Contains(glyph)) _glyphValues.Add(glyph);
        }

        static Type _locType;

        /// <summary>LocalizedString.Get(key), reflectively. Returns the key unchanged if the type or the
        /// string is missing, which is exactly what the game shows in that case anyway.</summary>
        static string Localize(string key)
        {
            if (_locType == null) _locType = Ref.FindType("LocalizedString");
            if (_locType == null) return key;
            object s = Ref.CallStatic(_locType, "Get", key);
            string t = s as string;
            return string.IsNullOrEmpty(t) ? key : t;
        }

        object PcSettings()
        {
            object uc = GameRefs.UnityClient();
            object vm = uc == null ? null : Ref.Get(uc, "ViewManager");
            object hud = vm == null ? null : Ref.Get(vm, "HudBinding");
            object binding = hud == null ? null : Ref.Get(hud, "AbilityBarBinding");
            return binding == null ? null : Ref.Get(binding, "_PCSettings");
        }

        /// <summary>The pad control bound to an action, short-named — checking the held-modifier layer too,
        /// so "Back + A" shows as "BK+A" rather than silently falling back to the keyboard key.</summary>
        static string GlyphFor(string action)
        {
            foreach (var kv in Cfg.Bind)
                if (kv.Value == action) return Name(kv.Key);

            foreach (var kv in Cfg.ModBind)
                if (kv.Value == action)
                    return (Cfg.ModifierBit != 0 ? Name(Cfg.ModifierBit) + "+" : "") + Name(kv.Key);

            if (action == Cfg.ModifierTapAction) return Name(Cfg.ModifierBit);
            return null;
        }

        static string Name(ushort bit)
        {
            string s;
            return Short.TryGetValue(bit, out s) ? s : Btn.Name(bit);
        }

        // ── reaching the bar ─────────────────────────────────────────────────────────────────────

        /// <summary>Walk to the live bar rather than searching for it: FindObjectOfType came back empty
        /// here (the bar hangs off the HUD binding and is not reachable that way), and the binding gives an
        /// exact, cheap path — ViewManager.HudBinding.AbilityBarBinding._AbilityBar.</summary>
        IList Slots()
        {
            if (_bar != null && (_bar as UnityEngine.Object) == null) { _bar = null; _original.Clear(); _labels = new object[0]; }
            if (_bar == null)
            {
                object uc = GameRefs.UnityClient();
                object vm = uc == null ? null : Ref.Get(uc, "ViewManager");
                object hud = vm == null ? null : Ref.Get(vm, "HudBinding");
                object binding = hud == null ? null : Ref.Get(hud, "AbilityBarBinding");
                _bar = binding == null ? null : Ref.Get(binding, "_AbilityBar");
                if (_bar == null) return null;
            }
            return Ref.Get(_bar, "GUIElements") as IList;
        }

        /// <summary>The `Keybind` Text on a slot. GUIElements holds AbilitySlotGUIData structs whose
        /// AbilityGUI is the UI_AbilitySlot that owns the label.</summary>
        static object LabelOf(object guiData)
        {
            if (guiData == null) return null;
            object slot = Ref.Get(guiData, "AbilityGUI");
            return slot == null ? null : Ref.Get(slot, "Keybind");
        }

        static void TrySetText(object label, string text)
        {
            for (Type t = label.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty("text", Ref.AnyInstance);
                if (p != null && p.CanWrite) { try { p.SetValue(label, text, null); return; } catch (Exception) { } }
            }
        }
    }
}
