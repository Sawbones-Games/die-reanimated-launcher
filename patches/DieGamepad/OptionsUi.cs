/*
 * Putting the input-device setting into the game's OWN options screen, in both clients.
 *
 * The two clients do not share an options UI — different UI toolkits, in different assemblies, with
 * different widget types and different save machinery. What they have in common is the only thing that
 * matters here: each builds its rows at RUN TIME through a builder method rather than from fixed prefab
 * fields, which is the only reason a row can be added from outside at all. So there is one adapter each and
 * a shared rule about when to run them.
 *
 * Everything is reflection: this assembly references UnityEngine and nothing else, so it builds without
 * the game and survives a rebuild of it.
 *
 * Re-injection, not injection-once: an options screen is rebuilt when it is reopened, and the row goes
 * with it. So the check is "is my widget still alive?" (a destroyed UnityEngine.Object compares equal to
 * null through its overloaded operator) rather than a bool we set the first time.
 *
 * APPLY, not "as you pick". The setting is read out of the game's OWN settings store, never off the
 * widget, and that is the whole mechanism behind the Apply button working for our row the way it does for
 * the game's own. Both stacks register a dropdown with a save TYPE and only push the selection into
 * PCSettings when the player presses Apply (GUI_OptionsDropdownList.SaveData, UI_DropdownMenu.Save, both
 * reached from the window's apply handler); Cancel instead calls LoadData, which pulls the stored value
 * back into the widget. So polling PCSettings gives Apply, Cancel and "changed my mind twice" for free,
 * whereas polling the widget's selection — which is what the first version did — applied every flick of
 * the dropdown instantly.
 *
 * ⚠️ WHAT THIS FILE COSTS, because it is a POLL and a poll is charged to every frame forever.
 * Finding the screen means a scene search, and the Unity 4 searches that see an INACTIVE object —
 * Resources.FindObjectsOfTypeAll — walk every loaded object including assets and prefabs. The first
 * version ran one of those on every poll, i.e. twice a second, and the frame-budget check measured
 * exactly that: p99 8 ms → 26 ms and TWO long frames a second, with the heap flat and no collections. That
 * is the whole argument for measuring rather than reasoning about it.
 *
 * So the rule here is: a scene search happens when it can ACHIEVE something, never on a timer.
 *   - What is found is cached (the tab, the screen), not re-found. The Crib's tab exists from the moment
 *     the hub is built and simply sits inactive, so that is ONE search for the whole session.
 *   - The match client builds its options screen lazily, so there is nothing to find until the player
 *     opens it — and the mod already knows when that is, for free, from the HUD state it caches anyway.
 *   - A search that finds nothing backs off, so a client that never opens the screen is not paying a
 *     scene walk every half second for the rest of the match.
 *
 * The one thing that has to happen for that to be true is SEEDING: our identifier is not one of the
 * game's, so PCSettings has no entry for it and reads 0 for a key it has never seen. Reading that as the
 * setting would overwrite Gamepad.cfg with the default on the first frame, so the store is written from
 * the cfg when the row is built and only polled from then on (_seeded).
 */

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace DieGamepad
{
    class OptionsUi : MonoBehaviour
    {
        const float PollInterval = 0.5f;   // idle cost is one FindObjectOfType; only matters while a menu is open

        /// <summary>Back-off for the scene searches, which are the expensive part (see the header). A
        /// search that finds nothing doubles the wait up to <see cref="MaxSearchWait"/>; one that finds
        /// something never happens again, because the result is cached.</summary>
        const float MinSearchWait = 0.5f, MaxSearchWait = 8f;

        float _next;
        float _nextSearch;
        float _searchWait = MinSearchWait;
        UnityEngine.Object _cribTab;       // GUI_NewOptionsGeneralTab — found ONCE, then reused
        UnityEngine.Object _matchScreen;   // UI_Options_Settings — ditto
        UnityEngine.Object _cribRow;       // GUI_OptionsDropdownList we added
        UnityEngine.Object _matchRow;      // UI_DropdownMenu we added
        /// <summary>Has the game's settings store been told our value yet? Until it has, what it holds is
        /// 0 for a key it has never seen, which is not the setting — see the file header.</summary>
        bool _seeded;
        /// <summary>Last pad-presence state pushed into each row's Disabled, so the sprite recolouring in
        /// those setters runs on a change and not twice a second. -1 = never pushed, which is also what a
        /// freshly rebuilt row is: the options screen is rebuilt every time it opens, so the NEW widget has
        /// been told nothing however long the answer has been the same.</summary>
        int _cribGate = -1, _matchGate = -1;
        /// <summary>...and what we last said about it in the log, which a rebuild must NOT repeat.</summary>
        int _padSaid = -1;
        bool _warned;
        readonly System.Collections.Generic.List<string> _said = new System.Collections.Generic.List<string>();

        /// <summary>Say it once. A silent failure here cost a test cycle: the row simply never appeared and
        /// nothing in the log said why.</summary>
        void WarnOnce(string msg)
        {
            if (_said.Contains(msg)) return;
            _said.Add(msg);
            Log.Line(msg);
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + PollInterval;

            try
            {
                if (_cribRow == null) TryCrib(); else Gate(_cribRow, "Label", ref _cribGate);
                if (_matchRow == null) TryMatch(); else Gate(_matchRow, "InfoText", ref _matchGate);
                ReadApplied();
            }
            catch (Exception ex)
            {
                if (!_warned) { _warned = true; Log.Line("options injection failed: " + ex.Message); }
            }
        }

        /// <summary>May we pay for a scene search right now? Rate-limited and self-widening, so a client
        /// that never opens its options screen settles at one search every eight seconds instead of two a
        /// second — and a successful one resets it for whenever the screen is rebuilt.</summary>
        bool MaySearch()
        {
            if (Time.realtimeSinceStartup < _nextSearch) return false;
            _searchWait = Mathf.Min(_searchWait * 2f, MaxSearchWait);
            _nextSearch = Time.realtimeSinceStartup + _searchWait;
            return true;
        }

        void SearchFound()
        {
            _searchWait = MinSearchWait;
            _nextSearch = 0f;
        }

        /// <summary>A cached UnityEngine.Object, or null if it was never found or has been destroyed.
        /// A destroyed one compares equal to null through the overloaded operator, which a plain field
        /// read would miss.</summary>
        static object Alive(UnityEngine.Object o) { return o == null ? null : (object)o; }

        // ── the Crib: NGUI ───────────────────────────────────────────────────────────────────────

        static readonly string[] CribTabTypes = { "GUI_NewOptionsGeneralTab", "GUI_OptionsGeneralTab" };

        void TryCrib()
        {
            // BOTH tab classes exist in the assembly — this build ships the old NGUI options window and the
            // newer one side by side — so resolving the TYPE tells us nothing. Only a live instance does.
            // (First version used `FindType(a) ?? FindType(b)`, which always picked the newer type, found no
            // instance of it, and gave up without ever trying the one actually on screen.)
            object tab = Alive(_cribTab);
            if (tab == null)
            {
                if (!MaySearch()) return;
                foreach (var name in CribTabTypes)
                {
                    var t = Ref.FindType(name);
                    if (t == null) continue;
                    var found = FindAny(t);
                    if (found != null) { tab = found; break; }
                }
                if (tab == null) return;                  // window not built yet; normal, say nothing
                // The Crib builds its windows up front and leaves them inactive, so this is the only
                // scene walk this client will do.
                _cribTab = (UnityEngine.Object)tab;
                SearchFound();
            }

            // Two shapes for the same job:
            //   new: collection.AddDropdownListOption(id, nameKey, saveType, tooltip)
            //   old: tab.AddDropdownListOption(table, id, nameKey, saveType, tooltip)   (on the base class)
            // Find whichever exists and fill its parameters by position.
            object target = null, container = null;
            MethodInfo add = null;

            // OVERALL before GAME: it is the client-wide block (region, network card) where an input-device
            // preference belongs, and — practically — appending to GAME put the row past the end of the
            // scroll extent, where it was clipped by the panel and its popup had nowhere to draw.
            foreach (var holderName in new[] { "OverallCollection", "GameCollection" })
            {
                object holder = Ref.Get(tab, holderName);
                if (holder == null) continue;
                var m = holder.GetType().GetMethod("AddDropdownListOption", Ref.AnyInstance);
                if (m == null || m.GetParameters().Length != 4) continue;
                target = holder; add = m; break;
            }

            if (add == null)
            {
                var m = tab.GetType().GetMethod("AddDropdownListOption", Ref.AnyInstance);
                if (m != null && m.GetParameters().Length == 5)
                {
                    container = Ref.Get(tab, "GameTable") ?? Ref.Get(tab, "OverallTable");
                    if (container != null) { target = tab; add = m; }
                }
            }

            if (add == null || target == null)
            {
                WarnOnce("crib options: no usable AddDropdownListOption on " + tab.GetType().Name);
                return;
            }

            // Wait until the rows have actually been built, and read that from whichever object OWNS them:
            // the collection in the new shape, the tab in the old one. Reading it off the tab regardless
            // was wrong — the new tab keeps no list of its own, so the gate never opened and the row
            // silently never appeared.
            var own = Ref.Get(target, "_OptionItems") as IList;
            if (own == null || own.Count == 0) return;

            var ps = add.GetParameters();
            // The save-type enum is nested in GUI_OptionsDropdownList; take it off the parameter itself so
            // no nested-type name is hardcoded, and find it by TYPE so either shape works.
            int saveIdx = -1;
            for (int i = 0; i < ps.Length; i++) if (ps[i].ParameterType.IsEnum) { saveIdx = i; break; }
            if (saveIdx < 0) { WarnOnce("crib options: no save-type parameter"); return; }

            var args = new object[ps.Length];
            int a = 0;
            if (container != null) args[a++] = container;     // old shape leads with the table
            args[a++] = DeviceSetting.OptionId;               // optionIdentifier
            args[a++] = "";                                   // nameIdentifier (a loc key; label set below)
            args[saveIdx] = Enum.Parse(ps[saveIdx].ParameterType, "SaveAsInt");
            for (int i = 0; i < args.Length; i++)
                if (args[i] == null && ps[i].ParameterType == typeof(string)) args[i] = "";

            object row = add.Invoke(target, args);
            var rowObj = row as UnityEngine.Object;
            if (rowObj == null) { WarnOnce("crib options: AddDropdownListOption returned nothing"); return; }

            // The label comes from LocalizedString.Get(key) and our key is not in the string table, so set
            // the UILabel directly rather than shipping a localisation entry for one row.
            SetLabelText(Ref.Get(row, "Label"), "Input device");

            var addItem = row.GetType().GetMethod("AddItem", new[] { typeof(string) });
            if (addItem != null)
                foreach (var label in DeviceSetting.Labels) addItem.Invoke(row, new object[] { label });

            // Style it from a SIBLING, not from the tab's own atlas field: that field reads null on this
            // build, so the builder's SetAtlasAndFont(this._Atlas, …) styles the new row with nothing — it
            // drew no dropdown box and could not be opened. An existing dropdown in the same table is
            // demonstrably styled correctly, so copy the atlas and font off it.
            StyleFromSibling(own, row);

            Show(row);
            // No callback subscription: the widget's OnValueModified is a public delegate FIELD, not a C#
            // event (and the match client's is a UnityEvent), so GetEvent found nothing and the handler
            // silently never ran — the UI showed the new value while the setting stayed put. And nothing
            // needs one: the value is read out of the game's settings store, which only the Apply button
            // writes (see the file header), so there is no per-build delegate signature to get right.

            // The table/grid laid itself out before our row existed, so ask it to lay out again.
            if (container != null) Ref.Call(container, "Reposition");
            else Ref.Call(Ref.Get(target, "OptionsGrid"), "Reposition");

            _cribRow = rowObj;
            _cribGate = -1;                    // a new widget, whatever the old one had been told
            Gate(rowObj, "Label", ref _cribGate);
            Log.Line("added 'Input device' to the Crib options (value: " + DeviceSetting.Label
                     + ", controller " + (DeviceSetting.PadPresent ? "present" : "absent") + ")");
        }

        /// <summary>Copy atlas + font from an already-built dropdown in the same table onto ours.</summary>
        void StyleFromSibling(IList siblings, object row)
        {
            object donorList = null;
            foreach (var s in siblings)
            {
                if (s == null || ReferenceEquals(s, row)) continue;
                object dl = Ref.Get(s, "DropdownList");
                if (dl != null && Ref.Get(dl, "atlas") != null) { donorList = dl; break; }
            }
            if (donorList == null) { WarnOnce("crib options: no styled sibling to copy from"); return; }

            object atlas = Ref.Get(donorList, "atlas");
            object font = Ref.Get(donorList, "font");

            var m = row.GetType().GetMethod("SetAtlasAndFont", Ref.AnyInstance);
            if (m != null) { try { m.Invoke(row, new[] { atlas, font }); return; } catch (Exception) { } }

            // No such method on this build: set the pieces the sprite work actually needs.
            object mine = Ref.Get(row, "DropdownList");
            if (mine != null) { TrySet(mine, "atlas", atlas); TrySet(mine, "font", font); }
        }

        // ── the match client: the other UI stack ─────────────────────────────────────────────────

        void TryMatch()
        {
            var t = Ref.FindType("UI_Options_Settings");
            if (t == null) return;                       // not the match client

            object screen = Alive(_matchScreen);
            if (screen == null)
            {
                // Nothing to find until the player opens the menu, and the mod already knows when that is
                // from the HUD state it reads anyway — so in a match the scene walk simply never happens.
                if (!GameRefs.OptionsOpen() || !MaySearch()) return;
                screen = FindAny(t);
                if (screen == null) { WarnOnce("match options: screen not built yet"); return; }
                _matchScreen = (UnityEngine.Object)screen;
                SearchFound();
            }

            var parent = Ref.Get(screen, "GeneralContentParent") as GameObject;
            object items = Ref.Get(screen, "_GeneralItems");
            if (parent == null || items == null) { WarnOnce("match options: no General content parent/list"); return; }

            var add = t.GetMethod("AddDropdown", Ref.AnyInstance);
            if (add == null) return;
            var ps = add.GetParameters();
            if (ps.Length < 5) return;
            object saveAsInt = Enum.Parse(ps[4].ParameterType, "SaveAsInt");

            // This one takes the label as a plain string (it assigns InfoText.text directly), so no
            // localisation dance is needed on this side.
            object row = add.Invoke(screen, new object[] { parent, items, DeviceSetting.OptionId, "Input device", saveAsInt });
            var rowObj = row as UnityEngine.Object;
            if (rowObj == null) return;

            var addItem = row.GetType().GetMethod("AddItem");
            if (addItem != null)
            {
                var ips = addItem.GetParameters();
                foreach (var label in DeviceSetting.Labels)
                    addItem.Invoke(row, ips.Length >= 2 ? new object[] { label, "" } : new object[] { label });
            }

            Show(row);

            // Move it to the TOP of the General list. Appended, it landed past the end of the scroll
            // extent: the log said "added" and the row genuinely existed, but scrolling to the bottom
            // never reached it — the content's height had been computed before it was there. First place
            // needs no layout recalculation to be reachable.
            var rc = rowObj as Component;
            if (rc != null && rc.transform != null) rc.transform.SetAsFirstSibling();

            _matchRow = rowObj;
            _matchGate = -1;
            Gate(rowObj, "InfoText", ref _matchGate);
            Log.Line("added 'Input device' to the match options (value: " + DeviceSetting.Label
                     + ", controller " + (DeviceSetting.PadPresent ? "present" : "absent") + ")");
        }

        // ── the game's settings store: where Apply lands ───────────────────────────────────

        static Type _pcSettings;

        static Type PcSettings()
        {
            if (_pcSettings == null) _pcSettings = Ref.FindType("PCSettings");
            return _pcSettings;
        }

        /// <summary>Put OUR value into the game's store and then let the widget load itself from it.
        ///
        /// <para>Two jobs in one call, and they have to happen in this order. The seed is what makes the
        /// store's value mean something (it has never heard of our identifier, so it reads 0 for it), and
        /// it is also what makes Cancel work: Cancel calls the widget's LoadData, which pulls from exactly
        /// this store. Letting the widget load itself rather than forcing its selection directly is the
        /// other half — the match client's dropdown tracks a SELECTED ITEM object that Save() compares
        /// against its item list, and poking the visible text leaves that null, so Apply would find
        /// nothing to save.</para></summary>
        void Show(object row)
        {
            Type t = PcSettings();
            if (t != null)
            {
                Ref.CallStatic(t, "SetInteger", DeviceSetting.OptionId, (int)DeviceSetting.Value);
                _seeded = true;
                if (HasMethod(row, "LoadData")) { Ref.Call(row, "LoadData"); return; }
            }
            else WarnOnce("options: no PCSettings on this build - the row will not survive Apply");

            // No store, or no LoadData: at least show the right value, so the row is never a lie. AddItem
            // leaves the LAST item selected (it assigns each one as it goes), which is what this undoes.
            // The Crib's SelectedItem is read-only — the writable one is the list's own `selection`.
            object list = Ref.Get(row, "DropdownList");
            if (list != null) { TrySet(list, "selection", DeviceSetting.Label); return; }
            SetLabelText(Ref.Get(row, "SelectedItemText"), DeviceSetting.Label);
        }

        static bool HasMethod(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
                if (t.GetMethod(name, Ref.AnyInstance, null, Type.EmptyTypes, null) != null) return true;
            return false;
        }

        /// <summary>Read the value the player has APPLIED, out of the game's own store.</summary>
        void ReadApplied()
        {
            if (!_seeded) return;                      // the store does not hold our value yet
            Type t = PcSettings();
            if (t == null) return;
            object v = Ref.CallStatic(t, "_GetInteger", DeviceSetting.OptionId);
            if (!(v is int)) return;
            int i = (int)v;
            if (i < 0 || i >= DeviceSetting.Labels.Length) return;
            DeviceSetting.Set((InputDevice)i);
        }

        // ── no controller, no choice ────────────────────────────────────────────────────────────────────────

        /// <summary>Grey the row out while there is no controller plugged in, and say why in the label.
        ///
        /// <para>Both widgets carry a <c>Disabled</c> bool that greys them and turns their collider and
        /// button off, so the dropdown genuinely cannot be opened — the same treatment the game gives an
        /// option that does not apply. It also closes the one way the setting could be changed while
        /// disabled: the store is only written by a widget that has been CLICKED, so a row nobody can click
        /// cannot move the value either.</para></summary>
        void Gate(object row, string labelMember, ref int last)
        {
            int want = DeviceSetting.PadPresent ? 1 : 0;
            if (want == last) return;
            last = want;
            TrySet(row, "Disabled", want == 0);
            SetLabelText(Ref.Get(row, labelMember), want == 1 ? "Input device" : "Input device (no controller)");
            if (want == _padSaid) return;
            _padSaid = want;
            Log.Line("controller " + (want == 1 ? "found" : "not found")
                     + " - input device row " + (want == 1 ? "enabled" : "disabled"));
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Find an instance INCLUDING inactive ones.
        ///
        /// <para>FindObjectOfType only returns objects on ACTIVE GameObjects, and an options tab that has
        /// not been selected yet is inactive — so the first version simply never saw it and returned
        /// silently. Resources.FindObjectsOfTypeAll sees everything, at the cost of also returning assets
        /// and prefabs, so prefer a scene instance and fall back to whatever there is.</para></summary>
        static UnityEngine.Object FindAny(Type t)
        {
            var live = UnityEngine.Object.FindObjectOfType(t);
            if (live != null) return live;

            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll(t); }
            catch (Exception) { return null; }
            if (all == null) return null;

            UnityEngine.Object fallback = null;
            foreach (var o in all)
            {
                if (o == null) continue;
                var c = o as Component;
                // hideFlags marks editor/asset objects; a real scene instance has none.
                if (c != null && c.gameObject != null && c.gameObject.hideFlags == HideFlags.None) return o;
                if (fallback == null) fallback = o;
            }
            return fallback;
        }

        /// <summary>Set `.text` on an NGUI UILabel or a Unity UI Text without referencing either.</summary>
        static void SetLabelText(object label, string text)
        {
            if (label == null) return;
            TrySet(label, "text", text);
        }

        static void TrySet(object target, string member, object value)
        {
            if (target == null) return;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(member, Ref.AnyInstance);
                if (p != null && p.CanWrite) { try { p.SetValue(target, value, null); return; } catch (Exception) { } }
                var f = t.GetField(member, Ref.AnyInstance);
                if (f != null) { try { f.SetValue(target, value); return; } catch (Exception) { } }
            }
        }


    }
}
