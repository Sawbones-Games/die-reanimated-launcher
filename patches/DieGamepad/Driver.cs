/*
 * The loop.
 *
 * Every frame: read the pad, decide which of the two pointer behaviours applies, hold the movement keys
 * the left stick's octant implies, hold the action keys the layout implies, and put the cursor where the
 * right stick says. Nothing here writes a game field; the client's own input pass reads real OS input
 * and does the rest.
 *
 * Controller mode is a MODE, not an arbitration between pad and mouse. With an anchored aim the cursor is
 * a pure function of stick state, rewritten every frame, so a physical mouse could never win a frame even
 * if we wanted it to — which is why there is no negotiation left here at all: the mode is on whenever the
 * player has chosen the pad, one is plugged in, and our window has focus. (An earlier "Automatic" device
 * setting kept both devices live and watched for the physical mouse to move in order to hand control back.
 * That whole layer went with the setting: it was a coin-toss every frame dressed up as arbitration.)
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DieGamepad
{
    class Driver : MonoBehaviour
    {
        // Screen-space octants, counter-clockwise from +X. Index = round(angle / 45) % 8.
        static readonly string[][] OctantActions =
        {
            new[] { "MoveRight" },                 // 0  E
            new[] { "MoveRight", "MoveUp" },       // 1  NE
            new[] { "MoveUp" },                    // 2  N
            new[] { "MoveLeft", "MoveUp" },        // 3  NW
            new[] { "MoveLeft" },                  // 4  W
            new[] { "MoveLeft", "MoveDown" },      // 5  SW
            new[] { "MoveDown" },                  // 6  S
            new[] { "MoveRight", "MoveDown" },     // 7  SE
        };

        static readonly string[] MoveActions = { "MoveUp", "MoveDown", "MoveLeft", "MoveRight" };

        /// <summary>Tokens that mean a wheel notch rather than a key. `ZoomIn`/`ZoomOut` are game actions
        /// the client stores as HotkeyType.MouseWheel — there IS no KeyCode to press for them — and
        /// `WheelUp`/`WheelDown` are the menu-layer spelling of the same thing.</summary>
        static readonly Dictionary<string, int> WheelTokens = new Dictionary<string, int>
        {
            { "ZoomIn", 1 }, { "ZoomOut", -1 }, { "WheelUp", 1 }, { "WheelDown", -1 },
        };

        // OFF until the loop has established that the pad is the device AND our window is in front.
        bool _padMode;
        /// <summary>Whether the pad is currently driving — read by the HUD glyph layer, which must relabel
        /// the ability bar only while the pad is actually the device in hand.</summary>
        public static bool PadModeActive { get; private set; }
        int _octant = -1;
        Vector2 _aimStick = Vector2.zero;       // last non-zero aim, so a released stick holds the aim
        Vector2 _pointer;
        bool _pointerInit;
        float _nextLog;
        bool _wasForeground;
        bool _saidBackground;
        bool _modWasDown, _modUsed;
        KeyCode _tapRelease = KeyCode.None;
        /// <summary>Whether WE are the one holding Space down for the tutorial skip. Tracked separately
        /// from the per-control bookkeeping below because the skip is not a control: it is an override that
        /// only exists while the game is showing its own prompt (see GameRefs.SkipPrompt).</summary>
        bool _skipHeld;

        /// <summary>The KEY each control is currently holding down — resolved, not the action name, so a
        /// control whose meaning changes mid-hold (the modifier layer, or the layout flipping when a
        /// window opens) releases what it actually pressed.</summary>
        readonly Dictionary<ushort, KeyCode> _activeKey = new Dictionary<ushort, KeyCode>();
        readonly Dictionary<ushort, float> _wheelNext = new Dictionary<ushort, float>();
        readonly List<ushort> _scratch = new List<ushort>();
        readonly List<string> _warned = new List<string>();

        void Awake()
        {
            Log.Line("driver up - aimRadius=" + Cfg.AimRadius + " curve=" + Cfg.AimCurve
                     + " hysteresis=" + Cfg.OctantHysteresis + "deg");

            // Probe the pad once here rather than lazily on the first foreground frame, so the log says
            // what the hardware situation is even if the window never gains focus. A pad that reads but
            // never CHANGES is the normal picture for an untouched controller and must not look like a
            // failure: XInput's packet number only moves when the state does.
            PadState p = XInput.Read();
            Log.Line("xinput: dll=" + (XInput.Which ?? "unprobed")
                     + " connected=" + p.Connected + " packet=" + XInput.Packet + " state=" + p);
        }

        void OnDisable() { SetPadMode(false); ReleaseEverything(); InputBlock.Remove(); }
        void OnApplicationQuit() { SetPadMode(false); ReleaseEverything(); InputBlock.Remove(); }

        /// <summary>Single place pad mode changes, so the static mirror, the HUD glyphs and the
        /// keyboard/mouse blocking can never drift out of step with it.</summary>
        void SetPadMode(bool on)
        {
            _padMode = on;
            PadModeActive = on;
            // Pad mode only ever comes on for the explicit Gamepad choice now, so this is just the
            // player's cfg opt-out.
            InputBlock.Apply(on && Cfg.BlockKeyboardMouse);
            // Hand the pad's real buttons to the game's own binding system while the pad drives, so it
            // reads them natively and prints their names in every prompt itself.
            NativeBinds.Apply(on && Cfg.NativeBinds);
        }

        void ReleaseEverything()
        {
            _activeKey.Clear();
            _wheelNext.Clear();
            _octant = -1;
            _skipHeld = false;
            Synth.ReleaseAll();
        }

        void Update()
        {
            if (!Cfg.Enabled) return;

            // BEFORE the device gate, not after: the seam can report a pad as DISCONNECTED, which closes
            // that gate — so polling behind it would be a one-way door, with no way back to the real pad
            // short of restarting the client.
            if (Cfg.AllowInject) Inject.Poll();

            // "Keyboard & Mouse", or no controller at all, means exactly that: synthesise nothing.
            if (!DeviceSetting.PadAllowed)
            {
                if (_padMode || Synth.AnyHeld) { SetPadMode(false); ReleaseEverything(); }
                return;
            }

            // Never synthesise into someone else's window: SendInput goes to whatever is focused, and
            // SetCursorPos would drag the desktop cursor. Ask the OS, not UnityClient._GameHasFocus,
            // which defaults TRUE and is only cleared by OnApplicationFocus(false).
            bool fg = Synth.Foreground();
            if (!fg)
            {
                if (_wasForeground)
                {
                    // Never leave the desktop's keyboard and mouse swallowed by a window that is not even
                    // in front.
                    Log.Line("window lost focus - releasing");
                    SetPadMode(false);
                    ReleaseEverything();
                }
                else if (!_saidBackground) { _saidBackground = true; Log.Line("window is not foreground - idle until it is"); }
                _wasForeground = false;
                return;
            }
            if (!_wasForeground) { Log.Line("window is foreground"); _saidBackground = false; }
            _wasForeground = true;

            PadState pad = Inject.Active ? Inject.State : XInput.Read();
            if (!pad.Connected) { SetPadMode(false); ReleaseEverything(); return; }

            if (!_padMode)
            {
                SetPadMode(true);
                _pointerInit = false;
                Synth.SyncCommanded();       // adopt wherever the cursor is as the baseline
                Log.Line("pad mode ON (" + (Inject.Active ? "inject" : "xinput") + ") " + pad);
            }

            // A tap fired last frame: let it go, so the client sees a clean down/up edge.
            if (_tapRelease != KeyCode.None) { Synth.SetHeld(_tapRelease, false); _tapRelease = KeyCode.None; }

            bool inGame = GameRefs.GameplayActive();
            bool pointerMode = GameRefs.PointerWanted() || !inGame;

            // Movement stays live even in pointer mode — the game itself keeps walking you while a wheel is
            // open or the cursor is over the HUD — but there is nothing to walk in the Crib, so do not
            // hammer WASD at a menu.
            if (inGame) Movement(pad); else ReleaseMovement();
            Actions(pad, pointerMode, inGame);
            SkipIntro(pad);
            if (pointerMode) Pointer(pad); else Aim(pad);

            Status(pad, pointerMode);
        }

        // ── movement ─────────────────────────────────────────────────────────────────────────────

        void Movement(PadState pad)
        {
            int oct = Octant(pad.LX, pad.LY);
            if (oct != _octant)
            {
                _octant = oct;
                // Release first, then press: a diagonal that loses one of its two keys must not have the
                // survivor re-pressed, and a bit that is newly SET fires InterruptCast() on the client.
                var wanted = oct < 0 ? null : OctantActions[oct];
                foreach (var action in MoveActions)
                {
                    bool want = false;
                    if (wanted != null)
                        foreach (var w in wanted) if (w == action) { want = true; break; }
                    Synth.SetHeld(GameRefs.BoundKey(action), want);
                }
            }
        }

        void ReleaseMovement()
        {
            if (_octant < 0) return;
            _octant = -1;
            foreach (var action in MoveActions) Synth.SetHeld(GameRefs.BoundKey(action), false);
        }

        /// <summary>Quantise to one of eight directions, with hysteresis so a stick resting on a boundary
        /// does not chatter between two octants.</summary>
        int Octant(float x, float y)
        {
            float mag = Mag(x, y);
            if (mag < 0.01f) return -1;                       // deadzone already applied by the source

            float ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            if (ang < 0f) ang += 360f;

            int candidate = Mathf.RoundToInt(ang / 45f) % 8;
            if (_octant < 0 || candidate == _octant) return candidate;

            float centre = _octant * 45f;
            float diff = Mathf.Abs(Mathf.DeltaAngle(ang, centre));
            return diff > 22.5f + Cfg.OctantHysteresis ? candidate : _octant;
        }

        // ── actions ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Hold the right key for each pressed control.
        ///
        /// <para>There are TWO layouts, because a menu and a fight want different things from the same
        /// buttons. In gameplay a control names an ACTION and the key is whatever the player bound to it
        /// (cInput). In a menu a control names a KEY OR A CLICK directly — there is no "click" action in
        /// the game's binding table to look up, and a menu does not want A to fire ActionButton. Without
        /// this split, A in the Crib resolved to the ActionButton key (X) and menus were unreachable.</para>
        ///
        /// <para>What is held is tracked as a resolved KeyCode rather than an action name, so a control
        /// whose MEANING changes while it is held — the modifier layer, or the layout flipping as a window
        /// opens — releases the key it actually pressed instead of whatever the new map would name.</para></summary>
        void Actions(PadState pad, bool pointerMode, bool inGame)
        {
            bool modHeld = !pointerMode && Cfg.ModifierBit != 0 && pad.Down(Cfg.ModifierBit);

            // The modifier TAPPED on its own is an action in its own right — the scoreboard, by default.
            // A scoreboard buried under "hold Back + D-pad up" is one nobody presses mid-fight, and Back is
            // where a pad player's thumb already goes for it. Held WITH another button it stays the layer
            // selector, so both meanings fit on one button.
            //
            // Gated on IN A MATCH rather than on the pointer mode, and the menu layer gives the modifier up
            // while in one (below), so Back has exactly ONE meaning at a time. Gating on !pointerMode split
            // it across the two layers: opening the scoreboard flipped us to pointer mode, so CLOSING it
            // went through the menu layer's Back→Tab while the tap ALSO fired on release — two toggles for
            // one press. It only stayed closed because Tab was already held, so the second press produced
            // no key-down edge for the client to see. That is luck, not a design.
            if (modHeld && OtherActionDown(pad)) _modUsed = true;
            if (Cfg.ModifierBit != 0 && _modWasDown && !pad.Down(Cfg.ModifierBit))
            {
                if (!_modUsed && inGame) Tap(Cfg.ModifierTapAction);
                _modUsed = false;
            }
            _modWasDown = Cfg.ModifierBit != 0 && pad.Down(Cfg.ModifierBit);

            _scratch.Clear();
            if (pointerMode)
            {
                foreach (var bit in Cfg.UiBind.Keys) if (!_scratch.Contains(bit)) _scratch.Add(bit);
            }
            else
            {
                foreach (var bit in Cfg.Bind.Keys) if (!_scratch.Contains(bit)) _scratch.Add(bit);
                foreach (var bit in Cfg.ModBind.Keys) if (!_scratch.Contains(bit)) _scratch.Add(bit);
            }

            foreach (var bit in _scratch)
            {
                // The modifier belongs to the tap/layer logic whenever we are in a match — in a menu OUTSIDE
                // a match (the Crib) it is free to be an ordinary menu key.
                if ((!pointerMode || inGame) && bit == Cfg.ModifierBit) continue;

                KeyCode want = KeyCode.None;
                int wheel = 0;
                if (pad.Down(bit))
                {
                    string token = null;
                    if (pointerMode) Cfg.UiBind.TryGetValue(bit, out token);
                    else if (modHeld) Cfg.ModBind.TryGetValue(bit, out token);
                    else Cfg.Bind.TryGetValue(bit, out token);

                    if (token != null)
                    {
                        // Bound natively? The game already reads that button off the pad; synthesising a
                        // key for it too would deliver every press twice.
                        if (!pointerMode && NativeBinds.Owns(token)) { token = null; }
                    }

                    if (token != null)
                    {
                        if (WheelTokens.TryGetValue(token, out wheel)) { }
                        else if (pointerMode) want = Cfg.ParseKey(token);       // a KeyCode by name
                        else want = GameRefs.BoundKey(token);                   // an action, via cInput
                        if (want == KeyCode.None && wheel == 0)
                            WarnOnce("no key for '" + token + "'" + (pointerMode ? " (ui)" : " (action)"));
                    }
                }

                // A wheel notch cannot be held, so it repeats on its own clock while the control is down.
                if (wheel != 0) { Repeat(bit, wheel); } else { _wheelNext.Remove(bit); }

                KeyCode have;
                if (!_activeKey.TryGetValue(bit, out have)) have = KeyCode.None;
                if (have == want) continue;

                if (have != KeyCode.None) { Synth.SetHeld(have, false); _activeKey.Remove(bit); }
                if (want != KeyCode.None) { Synth.SetHeld(want, true); _activeKey[bit] = want; }
            }
        }

        /// <summary>Is any control OTHER than the modifier currently down? Decides whether a modifier
        /// release was a tap or the end of a layer press.</summary>
        static bool OtherActionDown(PadState pad)
        {
            return (pad.Buttons & ~Cfg.ModifierBit) != 0;
        }

        /// <summary>Press and release an action in one go, for things the client reads as a key-DOWN edge
        /// (the scoreboard is `GetKeyDown` + ToggleScoreboard, so a tap opens it and the next tap closes).
        /// Released on the next frame by the normal held-key bookkeeping.</summary>
        void Tap(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            KeyCode k = GameRefs.BoundKey(action);
            if (k == KeyCode.None) { WarnOnce("no binding for tap action '" + action + "'"); return; }
            Synth.SetHeld(k, true);
            _tapRelease = k;
            Log.Line("tap: " + action + " (" + k + ")");
        }

        /// <summary>Send the literal Space the tutorial intro's skip is hardcoded to, while — and only
        /// while — the game is showing its own prompt asking for it.
        ///
        /// <para>Not a layout entry, because it cannot be one: there is no action to bind (see
        /// GameRefs.SkipPrompt) and the prompt is on screen for twenty seconds once per account. A and
        /// Start both do it, being the two buttons a hand reaches for when a screen says "press to
        /// continue".</para></summary>
        void SkipIntro(PadState pad)
        {
            bool want = GameRefs.SkipPrompt() != null && (pad.Down(Btn.A) || pad.Down(Btn.Start));
            if (want == _skipHeld) return;
            _skipHeld = want;
            Synth.SetHeld(KeyCode.Space, want);
            if (want) Log.Line("skipping the tutorial intro");
        }

        void Repeat(ushort bit, int notches)
        {
            float now = Time.realtimeSinceStartup;
            float due;
            if (_wheelNext.TryGetValue(bit, out due) && now < due) return;
            Synth.Wheel(notches);
            _wheelNext[bit] = now + (due == 0f ? WheelFirstDelay : WheelRepeat);
        }

        const float WheelFirstDelay = 0.25f, WheelRepeat = 0.10f;

        void WarnOnce(string msg)
        {
            if (_warned.Contains(msg)) return;
            _warned.Add(msg);
            Log.Line(msg);
        }

        // ── aim ──────────────────────────────────────────────────────────────────────────────────

        void Aim(PadState pad)
        {
            Vector2 anchor;
            if (!GameRefs.Anchor(out anchor)) { Pointer(pad); return; }
            _pointerInit = false;

            float m = Mag(pad.RX, pad.RY);
            if (m > 0.01f)
            {
                float shaped = Mathf.Pow(Mathf.Clamp01(m), Cfg.AimCurve);
                _aimStick = new Vector2(pad.RX / m * shaped, pad.RY / m * shaped);
            }
            // else: hold the last aim. Collapsing onto the character when the thumb lifts would swing the
            // player's facing in the middle of a fight.

            float r = Cfg.AimRadius * Screen.height;
            float px = anchor.x + _aimStick.x * r;
            float py = anchor.y + _aimStick.y * r;
            Commit(px, py);
        }

        void Pointer(PadState pad)
        {
            if (!_pointerInit)
            {
                // Start from wherever the cursor already is, in Unity's own client coordinates.
                Vector3 mp = Input.mousePosition;
                _pointer = new Vector2(mp.x, mp.y);
                _pointerInit = true;
            }

            // EITHER stick drives a menu pointer. In a menu the left stick is what most hands reach for,
            // and there is no aim to conflict with, so whichever is pushed further wins.
            float sx = pad.RX, sy = pad.RY;
            if (Mag(pad.LX, pad.LY) > Mag(sx, sy)) { sx = pad.LX; sy = pad.LY; }

            float m = Mag(sx, sy);
            if (m <= 0.01f) return;   // a resting stick must not PIN the cursor; relative means relative

            float shaped = Mathf.Pow(Mathf.Clamp01(m), Cfg.PointerCurve);
            float speed = Cfg.PointerSpeed * Screen.height * Time.deltaTime;
            _pointer.x += sx / m * shaped * speed;
            _pointer.y += sy / m * shaped * speed;
            Commit(_pointer.x, _pointer.y);
        }

        /// <summary>Clamp inside the window and warp. The inset is not cosmetic: the client pans the
        /// camera from cursor proximity to the edge, a moving camera moves the anchor, and a moving anchor
        /// moves the cursor — a feedback loop that runs away.</summary>
        void Commit(float x, float y)
        {
            float inset = Cfg.EdgeInset;
            x = Mathf.Clamp(x, inset, Screen.width - inset);
            y = Mathf.Clamp(y, inset, Screen.height - inset);
            _pointer = new Vector2(x, y);
            Synth.CursorToUnityPoint(x, y);
        }

        // ── diagnostics ──────────────────────────────────────────────────────────────────────────

        void Status(PadState pad, bool pointerMode)
        {
            if (Cfg.LogEvery <= 0f || Time.realtimeSinceStartup < _nextLog) return;
            _nextLog = Time.realtimeSinceStartup + Cfg.LogEvery;

            var keys = new List<string>();
            foreach (var k in Synth.Held) keys.Add(k.ToString());

            Vector2 anchor; bool haveAnchor = GameRefs.Anchor(out anchor);
            Log.Line(string.Format("{0} | {1} | oct={2} | anchor={3} | cursor=({4},{5}) | held=[{6}]",
                Inject.Active ? "INJECT" : "XINPUT", pointerMode ? "pointer" : "aim", _octant,
                haveAnchor ? anchor.ToString() : "none",
                Synth.CommandedX, Synth.CommandedY, string.Join(",", keys.ToArray())));
        }

        static float Mag(float x, float y) { return Mathf.Sqrt(x * x + y * y); }
    }
}
