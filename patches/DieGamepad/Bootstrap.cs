/*
 * Dead Island: Epidemic — gamepad support.
 *
 * The gamepad half of the client patch. The launcher installs this DLL into BOTH clients' Managed folders
 * and inserts a call to DieGamepad.Bootstrap.Init() as the first instruction of CribStart.Awake (Crib) and
 * UnityClient.Awake (match) — see Launcher.Patching/GamepadPatcher.cs and
 * docs/reference/gamepad-patch.md.
 *
 * That hook shape is why several payloads can share one method: each inserts its own call and none
 * replaces the body, so independent payloads coexist on the same start-up method.
 *
 * Init() must never throw: a dead pad is recoverable, but an exception inside Awake means the client does
 * not start at all. Gamepad.cfg and Gamepad-crib.log / Gamepad-match.log appear next to the exe on first
 * run.
 *
 * Iterating on it: rebuild this project and copy the DLL over the one the launcher installed — the hooks
 * are already in place, so the next start picks it up.
 */

using System;
using System.IO;
using UnityEngine;

namespace DieGamepad
{
    public static class Bootstrap
    {
        static bool _started;

        public static void Init()
        {
            if (_started) return;
            _started = true;

            try
            {
                string dir, tag;
                try
                {
                    dir = Path.GetDirectoryName(Application.dataPath) ?? ".";
                    // Both clients live in the SAME install folder, so an unqualified Gamepad.log has the
                    // second process to start TRUNCATE the first one's log. The Crib's data folder is
                    // "... - Crib_Data"; the match client's is "..._Data".
                    tag = (Path.GetFileName(Application.dataPath) ?? "").IndexOf("Crib", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "crib" : "match";
                }
                catch (Exception) { dir = "."; tag = "client"; }

                string cfgPath = Path.Combine(dir, "Gamepad.cfg");
                Log.Open(Path.Combine(dir, "Gamepad-" + tag + ".log"));
                DeviceSetting.Bind(cfgPath);
                Cfg.Load(cfgPath);
                Inject.Bind(Path.Combine(dir, "Gamepad.inject"));

                var go = new GameObject("GamepadMod");
                UnityEngine.Object.DontDestroyOnLoad(go);

                // BEFORE the enabled check, on purpose: with enabled=0 this is the CONTROL for the mod's
                // own frame-budget measurement — the same client, the same session, the same scene, with
                // the mod doing nothing. Comparing against a separately launched unpatched client would
                // compare the weather too. Absent entirely unless frameStats names a window.
                if (Cfg.FrameStats > 0f) go.AddComponent<FrameStats>();

                if (!Cfg.Enabled) { Log.Line("disabled by config"); return; }

                go.AddComponent<Driver>();
                // Runs even when the device is "Keyboard & Mouse" — that is the row the player needs in
                // order to turn the pad back ON.
                go.AddComponent<OptionsUi>();
                go.AddComponent<Glyphs>();
                go.AddComponent<NativeProbe>();

                Log.Line("ready (input device: " + DeviceSetting.Label + ")");
            }
            catch (Exception ex)
            {
                // Never take the game down. A dead pad is recoverable; a throw inside Awake() means the
                // client does not start at all.
                Debug.LogError("[Gamepad] failed to start: " + ex);
            }
        }
    }
}
