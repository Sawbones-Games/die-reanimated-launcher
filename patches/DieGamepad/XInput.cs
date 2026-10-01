/*
 * Reading the pad.
 *
 * XInput rather than Unity's own joystick input, for a reason that is a fact about this build and not a
 * preference: its binding system can hold a keyboard key or a mouse-wheel notch and nothing else, so a
 * STICK AXIS cannot be expressed as a binding at all. Going through Unity's joystick axes would also mean
 * relying on the shipped input configuration declaring the axes we need, which we cannot check from here.
 * XInput sidesteps both: it needs no Unity configuration and no focus.
 *
 * Which DLL: xinput1_4 ships with Windows 8+, xinput1_3 comes with the DirectX redist, xinput9_1_0 is
 * present on everything since Vista but drops the guide button. Probe in that order once and remember.
 */

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DieGamepad
{
    /// <summary>One frame of pad state, already deadzoned and normalised. Sticks are -1..1 with Y up;
    /// triggers 0..1; <see cref="Buttons"/> is the raw XInput bitmask (see <see cref="Btn"/>).</summary>
    struct PadState
    {
        public bool Connected;
        public float LX, LY, RX, RY;
        public float LT, RT;
        public ushort Buttons;

        public bool Down(ushort mask) { return (Buttons & mask) != 0; }

        public override string ToString()
        {
            return string.Format("L({0:0.00},{1:0.00}) R({2:0.00},{3:0.00}) LT{4:0.00} RT{5:0.00} b{6:X4}",
                LX, LY, RX, RY, LT, RT, Buttons);
        }
    }

    /// <summary>XInput button bits, plus the two triggers promoted to bits of their own so the whole
    /// layout table can be written in one vocabulary.</summary>
    static class Btn
    {
        public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
        public const ushort Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080;
        public const ushort LB = 0x0100, RB = 0x0200;
        public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        // Synthetic: XInput reports the triggers as analog bytes, not buttons. Bits 0x0400/0x0800 are
        // unused by XInput, so they are free for us to fold the triggers into.
        public const ushort LT = 0x0400, RT = 0x0800;

        public static string Name(ushort bit)
        {
            switch (bit)
            {
                case DPadUp: return "DPadUp";
                case DPadDown: return "DPadDown";
                case DPadLeft: return "DPadLeft";
                case DPadRight: return "DPadRight";
                case Start: return "Start";
                case Back: return "Back";
                case LS: return "LS";
                case RS: return "RS";
                case LB: return "LB";
                case RB: return "RB";
                case LT: return "LT";
                case RT: return "RT";
                case A: return "A";
                case B: return "B";
                case X: return "X";
                case Y: return "Y";
                default: return "0x" + bit.ToString("X4");
            }
        }

        public static ushort Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            switch (s.Trim().ToUpperInvariant())
            {
                case "DPADUP": return DPadUp;
                case "DPADDOWN": return DPadDown;
                case "DPADLEFT": return DPadLeft;
                case "DPADRIGHT": return DPadRight;
                case "START": return Start;
                case "BACK": return Back;
                case "LS": return LS;
                case "RS": return RS;
                case "LB": return LB;
                case "RB": return RB;
                case "LT": return LT;
                case "RT": return RT;
                case "A": return A;
                case "B": return B;
                case "X": return X;
                case "Y": return Y;
                default: return 0;
            }
        }
    }

    static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState14(uint i, out XINPUT_STATE s);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState13(uint i, out XINPUT_STATE s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState910(uint i, out XINPUT_STATE s);

        delegate uint GetStateFn(uint i, out XINPUT_STATE s);

        // Microsoft's own recommended deadzones (XINPUT_GAMEPAD_*_THUMB_DEADZONE). Applied radially,
        // then rescaled, so the usable range still reaches 1.0 and a diagonal is not clipped short.
        const float LeftDead = 7849f, RightDead = 8689f, TriggerDead = 30f;

        static GetStateFn _fn;
        static bool _probed;
        static uint _pad;
        static uint _lastPacket;

        public static string Which { get; private set; }

        /// <summary>Is a controller plugged in? Cached, because two callers need it on different clocks:
        /// the Driver reads the pad every frame anyway, but the OPTIONS row has to know whether the
        /// Gamepad choice is even offerable — and it has to know that while the device is Keyboard &amp;
        /// Mouse, i.e. while the Driver never touches XInput at all. Answering that from the options poll
        /// with a fresh 4-slot scan would mean scanning four slots twice a second forever, so a Read() is
        /// reused for half a second and only a stale answer costs a call.</summary>
        public static bool Present
        {
            get
            {
                if (Time.realtimeSinceStartup - _presenceAt >= PresenceTtl) Read();
                return _present;
            }
        }

        const float PresenceTtl = 0.5f;
        static bool _present;
        static float _presenceAt = float.NegativeInfinity;
        /// <summary>Increments whenever the pad reports a changed state. A frozen value across many
        /// frames means nothing is being touched — which is exactly what an untouched pad looks like,
        /// and is worth distinguishing from "not connected" when diagnosing.</summary>
        public static uint Packet { get { return _lastPacket; } }

        static bool Probe()
        {
            if (_probed) return _fn != null;
            _probed = true;

            GetStateFn[] fns = { GetState14, GetState13, GetState910 };
            string[] names = { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };
            for (int i = 0; i < fns.Length; i++)
            {
                try
                {
                    XINPUT_STATE s;
                    fns[i](0u, out s);          // any return code means the DLL and entry point exist
                    _fn = fns[i];
                    Which = names[i];
                    break;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (Exception ex) { Log.Line("xinput probe " + names[i] + ": " + ex.Message); }
            }

            if (_fn == null) { Which = "none"; Log.Line("no xinput DLL on this machine"); return false; }
            Log.Line("xinput via " + Which);
            return true;
        }

        public static PadState Read()
        {
            var st = new PadState();
            if (!Probe()) { Presence(false); return st; }

            XINPUT_STATE x;
            // Stay on whichever index answered last; rescan only when it stops answering, so a pad
            // that is unplugged and replugged is picked up without scanning four slots every frame.
            if (_fn(_pad, out x) != 0)
            {
                bool found = false;
                for (uint i = 0; i < 4; i++)
                {
                    if (_fn(i, out x) != 0) continue;
                    _pad = i; found = true; break;
                }
                if (!found) { Presence(false); return st; }
            }
            Presence(true);

            _lastPacket = x.dwPacketNumber;
            var g = x.Gamepad;

            st.Connected = true;
            st.Buttons = g.wButtons;
            Stick(g.sThumbLX, g.sThumbLY, LeftDead, out st.LX, out st.LY);
            Stick(g.sThumbRX, g.sThumbRY, RightDead, out st.RX, out st.RY);
            st.LT = Trigger(g.bLeftTrigger);
            st.RT = Trigger(g.bRightTrigger);
            if (st.LT > 0f) st.Buttons |= Btn.LT;
            if (st.RT > 0f) st.Buttons |= Btn.RT;
            return st;
        }

        static void Presence(bool connected)
        {
            _present = connected;
            _presenceAt = Time.realtimeSinceStartup;
        }

        /// <summary>Radial deadzone: measure the magnitude first, then rescale what is left of it. Per-axis
        /// deadzones square off the circle and make a slow diagonal impossible, which on an ANCHORED aim
        /// would show up as the cursor refusing to sit near the character.</summary>
        static void Stick(short rawX, short rawY, float dead, out float x, out float y)
        {
            float fx = rawX, fy = rawY;
            float mag = (float)Math.Sqrt(fx * fx + fy * fy);
            if (mag <= dead) { x = 0f; y = 0f; return; }

            float max = 32767f;
            if (mag > max) mag = max;
            float scaled = (mag - dead) / (max - dead);   // 0..1 across the live part of the throw
            x = fx / mag * scaled;
            y = fy / mag * scaled;
        }

        static float Trigger(byte raw)
        {
            if (raw <= TriggerDead) return 0f;
            return (raw - TriggerDead) / (255f - TriggerDead);
        }
    }
}
