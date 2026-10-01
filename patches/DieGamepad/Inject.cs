/*
 * The verification seam.
 *
 * The mod is driven by hardware that an automated check cannot press, so the pad state can also come
 * from a file instead of from XInput. That makes the whole chain — layout, octant quantisation, key
 * synthesis, anchor maths, cursor warp — assertable without a human holding the controller.
 *
 * Off unless `allowInject=1` is set in Gamepad.cfg: a player install never reads this file.
 *
 * Format: one line in `Gamepad.inject`, next to the exe.
 *     lx ly rx ry lt rt [buttons]
 *     0 1 0.5 0.5 0 0 A+RB          -> left stick north, right stick NE, A and RB down
 *     none                          -> report NO pad connected, whatever is really plugged in
 *     off                           -> hand control back to the real pad
 * Re-read only when the file's timestamp changes, so this costs one stat() per frame when idle.
 *
 * `none` is there because "what happens with no controller" is a real branch — the options row
 * disables itself and the mod synthesises nothing — and the only other way to reach it is to
 * physically unplug the hardware, which is not something a check can do to itself.
 */

using System;
using System.Globalization;
using System.IO;

namespace DieGamepad
{
    static class Inject
    {
        static string _path;
        static DateTime _stamp;
        static bool _active;
        static PadState _state;

        public static bool Active { get { return _active; } }
        public static PadState State { get { return _state; } }

        public static void Bind(string path) { _path = path; }

        /// <summary>Cheap poll: a timestamp check, then a parse only when it moved.</summary>
        public static void Poll()
        {
            if (_path == null) return;
            try
            {
                if (!File.Exists(_path))
                {
                    if (_active) { _active = false; Log.Line("inject: file gone, back to the real pad"); }
                    return;
                }
                DateTime t = File.GetLastWriteTimeUtc(_path);
                if (t == _stamp) return;

                // Read BEFORE recording the timestamp. The writer still has the file open often enough to
                // matter, and stamping first meant a sharing violation dropped that state for good — the
                // press never arrived and the click silently did nothing. Leaving _stamp alone makes the
                // next poll retry it.
                string text = File.ReadAllText(_path);
                _stamp = t;
                Parse(text);
            }
            catch (Exception ex) { Log.Line("inject: " + ex.Message); }
        }

        static void Parse(string text)
        {
            string line = null;
            foreach (var raw in text.Split('\n'))
            {
                string s = raw.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                line = s; break;
            }
            if (line == null) return;

            if (line.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _active = false;
                Log.Line("inject: off");
                return;
            }

            if (line.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                _state = new PadState();          // Connected = false
                _active = true;
                Log.Line("inject: no pad connected");
                return;
            }

            string[] p = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 6) { Log.Line("inject: need 6 numbers, got " + p.Length); return; }

            var st = new PadState();
            st.Connected = true;
            st.LX = F(p[0]); st.LY = F(p[1]);
            st.RX = F(p[2]); st.RY = F(p[3]);
            st.LT = F(p[4]); st.RT = F(p[5]);

            ushort buttons = 0;
            if (p.Length >= 7)
            {
                string b = p[6];
                if (b.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    ushort parsed;
                    if (ushort.TryParse(b.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                        buttons = parsed;
                }
                else if (b != "-" && b != "0")
                {
                    foreach (var name in b.Split('+'))
                    {
                        ushort bit = Btn.Parse(name);
                        if (bit != 0) buttons |= bit;
                        else Log.Line("inject: unknown control '" + name + "'");
                    }
                }
            }
            if (st.LT > 0f) buttons |= Btn.LT;
            if (st.RT > 0f) buttons |= Btn.RT;
            st.Buttons = buttons;

            _state = st;
            _active = true;
            Log.Line("inject: " + st);
        }

        static float F(string s)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f;
        }
    }
}
