/*
 * The handful of things the mod reads out of the running client.
 *
 * It writes nothing here — these are reads only, and every one of them exists to answer a question the
 * pad cannot answer by itself:
 *
 *   Anchor()        where the player's BODY is on screen, so an anchored aim can be centred on it.
 *                   The render transform, not Character.Position: the player aims at what they can see,
 *                   and the two differ by the client's one-frame render lag.
 *   PointerWanted() whether a wheel is open or the pointer is over UI. The same signals the client's own
 *                   input pass consults before it aims, so the mod switches pointer behaviour when the
 *                   client does rather than on a guess.
 *   BoundKey()      which key the PLAYER has bound to an action, so the layout maps pad -> ACTION and
 *                   never pad -> hardcoded key.
 *   SkipPrompt()    whether the client is asking for the one key that is NOT a binding — the tutorial
 *                   intro's hardcoded Space.
 */

using System;
using UnityEngine;

namespace DieGamepad
{
    static class GameRefs
    {
        /// <summary>The y the client drops the camera by before it aims. Unity units; the world is at 0.1
        /// scale, so this is 5 world units.</summary>
        public const float AimCameraDrop = 0.5f;


        // ── the local player ─────────────────────────────────────────────────────────────────────

        /// <summary>The live GameManager, or null when not in a match.</summary>
        public static object GameManager()
        {
            var t = Ref.FindType("GameManager");
            return t == null ? null : Ref.GetStatic(t, "ThreadStaticInstance");
        }

        public static object LocalPlayer()
        {
            var gm = GameManager();
            return gm == null ? null : Ref.Get(gm, "LocalPlayer");
        }

        /// <summary>The player's visible model transform. entity.CharacterModel is a wrapper component, and the model may be one level further
        /// in (_ActiveModel) when the hero swaps meshes.</summary>
        public static Transform ModelOf(object entity)
        {
            object wrapper = Ref.Get(entity, "CharacterModel");
            if (wrapper == null) return null;
            var c0 = wrapper as Component;
            if (c0 != null) return c0.transform;
            object model = Ref.Get(wrapper, "CharacterModel") ?? Ref.Get(wrapper, "_ActiveModel");
            var c = model as Component;
            return c == null ? null : c.transform;
        }

        /// <summary>The character's position in UNITY screen pixels (origin bottom-left), or false when
        /// there is no body to anchor on — dead, spectating, still loading, or a cinematic camera that has
        /// left the player behind.</summary>
        public static bool Anchor(out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            var player = LocalPlayer();
            if (player == null) return false;
            var model = ModelOf(player);
            if (model == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;

            // Project against the camera the GAMEPLAY AIM actually uses, not the one on screen: the client
            // lowers the camera before it raycasts the cursor to decide where you are aiming, and restores it
            // afterwards. Anchoring against the un-lowered camera offsets the whole aim circle vertically —
            // measured as a 5.0-7.7 deg heading error on the screen-horizontal axis, which is
            // atan(5 u / 36 u), because 0.5 Unity units IS 5 world units at the 0.1 scale.
            //
            // Translating the camera down by b is equivalent to translating the world up by b (the
            // rotation is untouched), so this needs no camera mutation and cannot race the client's own.
            Vector3 sp = cam.WorldToScreenPoint(model.position + new Vector3(0f, AimCameraDrop, 0f));
            if (sp.z <= 0f) return false;                       // behind the camera
            screenPoint = new Vector2(sp.x, sp.y);
            return true;
        }

        // ── the client's own UI / input gates ────────────────────────────────────────────────────

        static object _unityClient;

        public static object UnityClient()
        {
            if (_unityClient != null)
            {
                // FindObjectOfType results go stale across scene loads; a destroyed UnityEngine.Object
                // compares equal to null through its overloaded operator, which a plain != null misses.
                var asObj = _unityClient as UnityEngine.Object;
                if (asObj == null) _unityClient = null;
            }
            if (_unityClient == null)
            {
                var t = Ref.FindType("UnityClient");
                if (t != null) _unityClient = UnityEngine.Object.FindObjectOfType(t);
            }
            return _unityClient;
        }

        /// <summary>True when the pointer belongs to the UI rather than to aiming: a wheel is open, or the
        /// cursor is over a widget that blocks game input. Asks what the client's own input pass asks.</summary>
        public static bool PointerWanted()
        {
            var uc = UnityClient();
            if (uc == null) return true;            // no client yet: menus, loading — pointer mode is right

            if (Ref.IsTrue(Ref.Get(uc, "GUIHover"))) return true;

            object vm = Ref.Get(uc, "ViewManager");
            if (vm == null) return false;

            if (IsVisible(Ref.Get(vm, "EmoteMenu"))) return true;
            if (IsVisible(Ref.Get(vm, "PingWheel"))) return true;

            // Any full-screen HUD panel — SCOREBOARD, the in-match options menu, the victory screen, the
            // in-game shop. ViewManager asks exactly this (IsScoreboardVisible is
            // `HudBinding.CurrentState == HUD.SCOREBOARD`, IsOptionsMenuVisible the same for HUD.OPTION),
            // so one state read covers every panel instead of a list that rots. Without it the scoreboard
            // opens while the mod is still in ANCHORED AIM, pinning the cursor to the player's body and
            // making the panel unusable.
            //
            // ⚠️ TWO of these states are gameplay, not one. A live read mid-match returns MAIN, so the
            // obvious rule — "anything that is not NONE means a panel is up" — put the match client in
            // pointer mode PERMANENTLY and anchored aim never ran at all. Compared by NAME, so no numeric
            // enum value is baked in: those belong to one build and the names are the stable part.
            object hud = Ref.Get(vm, "HudBinding");
            object state = hud == null ? null : Ref.Get(hud, "CurrentState");
            if (state != null)
            {
                string s = state.ToString();
                if (!string.Equals(s, "NONE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(s, "MAIN", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Is the in-match options screen open? Cheap — the same cached HUD-state read
        /// <see cref="PointerWanted"/> already does — and it exists so the options injector never has to
        /// go LOOKING for that screen during gameplay. A Unity 4 search that can see an inactive object
        /// walks every loaded object including assets, and doing one twice a second was measured at two
        /// long frames a second (OptionsUi's header has the numbers).</summary>
        public static bool OptionsOpen()
        {
            var uc = UnityClient();
            object vm = uc == null ? null : Ref.Get(uc, "ViewManager");
            object hud = vm == null ? null : Ref.Get(vm, "HudBinding");
            object state = hud == null ? null : Ref.Get(hud, "CurrentState");
            return state != null && string.Equals(state.ToString(), "OPTION", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVisible(object widget)
        {
            if (widget == null) return false;
            var asObj = widget as UnityEngine.Object;
            if (asObj == null) return false;
            return Ref.IsTrue(Ref.Call(widget, "IsVisible"));
        }

        /// <summary>The client's own "press Space to skip intro" prompt, while it is on screen — or null.
        ///
        /// <para>This exists because the intro's skip is the one input no binding can reach: the client
        /// tests for that key literally instead of going through its binding system. So there is nothing to
        /// bind a pad button TO — neither the player's own Controls tab nor our native binds can help — and
        /// binding the pad natively is in fact what BROKE it: the dodge's default key happens to be the same
        /// one, so before native binds the B button sent it by accident. With B bound to a joystick button
        /// the client reads the pad directly and that key is never pressed.</para>
        ///
        /// <para>The signal is the game's own prompt label (<c>ViewManager.TutorialSkipText</c>), which it
        /// activates five seconds into the intro clip and deactivates when the intro ends or is skipped.
        /// "While the game is asking for that key" is a narrower and more honest gate than any guess at
        /// the game mode or the camera state, and it costs nothing when there is no tutorial.</para></summary>
        public static object SkipPrompt()
        {
            var uc = UnityClient();
            object vm = uc == null ? null : Ref.Get(uc, "ViewManager");
            object label = vm == null ? null : Ref.Get(vm, "TutorialSkipText");
            var c = label as Component;
            if (c == null) return null;
            GameObject go = c.gameObject;
            // activeSelf, not activeInHierarchy: the intro toggles the HUD root off around this label
            // every frame while it plays, so the prompt is visible with an inactive ancestor.
            return go != null && go.activeSelf ? label : null;
        }

        /// <summary>True when the client is accepting gameplay input at all. When it is not, the client
        /// discards movement and clears every pressed ability, so anything we synthesise would be thrown
        /// away — and the body may not even be on screen to anchor on.</summary>
        public static bool GameplayActive()
        {
            return LocalPlayer() != null;
        }

        // ── bindings ─────────────────────────────────────────────────────────────────────────────

        static Type _cInput;

        /// <summary>The KeyCode the player has bound to an action ("MoveUp", "PrimaryAttack", ...).
        /// Read from cInput so a rebind in the game's own Controls tab is respected; PCKeybinds' action
        /// names are plain strings equal to their field names, so no reflection into it is needed.</summary>
        public static KeyCode BoundKey(string action)
        {
            if (_cInput == null) _cInput = Ref.FindType("cInput");
            if (_cInput == null) return KeyCode.None;
            object k = Ref.CallStatic(_cInput, "GetKeyCode", action);
            if (k == null) return KeyCode.None;
            try { return (KeyCode)(int)Convert.ToInt32(k); } catch (Exception) { return KeyCode.None; }
        }
    }
}
