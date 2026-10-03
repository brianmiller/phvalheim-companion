using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PhValheimCompanion
{
    // The way back to the dialog, as a REAL Valheim menu button.
    //
    // WHY NOT IMGUI
    // The first version drew the button with GUI.Button in OnGUI. It rendered -- Brian's log
    // has "reopen button drawn at (x:830, y:1142, width:260, height:34) (screen 1920x1200)",
    // the right size in the right place -- and clicking it never once produced the "button
    // clicked" line that sits inside the same if. So the rect was painted and the mouse event
    // never arrived. Valheim ships Unity.InputSystem.dll alongside
    // UnityEngine.InputLegacyModule.dll, which is consistent with legacy IMGUI input being
    // dead, but the cause does not actually matter: a uGUI Button receives clicks through
    // whatever path the game itself uses, by construction.
    //
    // HOW WE KNOW THE CLONE WAS NOT BEING MADE
    // The first native version still left Brian with a GRAY button that did nothing. Gray is
    // Unity's DEFAULT IMGUI SKIN -- a cloned Valheim menu button is dark with a light border
    // and, since the theming pass, a violet label. He was reporting cyan body text from the
    // same build, so the dll was current. A gray button on a current dll can only be OnGUI's
    // fallback, which only draws when _nativeButtonOk is false. So Ensure() was returning
    // false, and the ONE return that did so without logging anything was `if (fejd == null)`.
    // That is why every exit below now logs, and why Describe() reports what was actually
    // found instead of what was expected.
    //
    // WHY IT IS PARENTED TO THE MENU LIST NOW
    // The previous version parented the clone to the CANVAS ROOT and positioned it by hand at
    // bottom-centre, to avoid disturbing the menu's layout group. That put our button on a
    // different branch of the hierarchy from every button Valheim itself knows works, so it
    // had its own answer to all the questions that decide whether a click lands: raycast
    // order against UnifiedPopup's fullScreenBackgroundCover, which CanvasGroup it inherits,
    // and whether its hand-computed anchoredPosition was even on screen.
    //
    // Cloning into the template's OWN PARENT makes all of those questions have the same
    // answer as the vanilla buttons, because it is the same branch. It costs one extra row in
    // the menu column, which is a cosmetic change and the better UX anyway -- the player looks
    // for a menu entry in the menu, not for a floating button at the bottom of the screen.
    internal static class MenuButton
    {
        private const string CloneName = "PhValheimReopenButton";

        private static GameObject _clone;
        private static Action _onClick;

        // Whether a native button currently exists. ConnectDialog reads this to decide whether
        // the IMGUI fallback is still needed, so a failure here is never silent.
        internal static bool Exists => _clone != null;

        // One structural report per creation, not per frame. Everything in it is READ BACK off
        // the live object rather than echoed from what we asked for -- the gray-button round
        // was lost precisely because the log said what the code intended.
        private static bool _described;

        // Creates the button if it is not already there. Returns false if it could not be
        // made, which is the signal to fall back rather than leave the player with no route.
        internal static bool Ensure(string label, Action onClick)
        {
            _onClick = onClick;

            if (_clone != null)
            {
                SetLabel(_clone, label);
                return true;
            }

            try
            {
                var fejd = FejdStartup.instance;
                if (fejd == null)
                {
                    // THIS RETURN USED TO BE SILENT and it is the one that cost a release
                    // cycle. It is not even unlikely: Update() calls ManageReopenButton on
                    // every frame, including frames where the menu object has gone.
                    Warn("FejdStartup.instance is null -- no menu to hang the button on");
                    return false;
                }

                Button template = FindTemplate(fejd);
                if (template == null) return false;   // FindTemplate logs which routes failed

                // The template's OWN parent, so the clone shares every ancestor that decides
                // whether a click lands. See the class comment.
                Transform parent = template.transform.parent;
                if (parent == null)
                {
                    Warn($"the template \"{template.gameObject.name}\" has no parent transform");
                    return false;
                }

                _clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                _clone.name = CloneName;
                _clone.transform.localScale = Vector3.one;

                // A layout group places its children itself, and writing anchoredPosition
                // under one is overwritten on the next layout pass -- so the offset is applied
                // ONLY when there is no layout group to do the placing. Without this branch
                // the clone either fought the layout group or, with no group, landed exactly
                // on top of the template it was copied from.
                var group = parent.GetComponent<UnityEngine.UI.LayoutGroup>();
                var rect = _clone.transform as RectTransform;
                var templateRect = template.transform as RectTransform;
                if (group == null && rect != null && templateRect != null)
                {
                    float drop = templateRect.rect.height > 1f ? templateRect.rect.height : 44f;
                    rect.anchoredPosition = templateRect.anchoredPosition - new Vector2(0f, drop);
                }

                var button = _clone.GetComponent<Button>();
                if (button == null)
                {
                    Warn($"the clone of \"{template.gameObject.name}\" has no Button component");
                    Remove();
                    return false;
                }

                // RemoveAllListeners IS NOT ENOUGH, and this cost a round.
                //
                // Brian: "Clicking it actually does something now, but it takes you to the
                // character selection screen." The clone was still running Valheim's own
                // handler alongside ours -- exactly the hazard the old comment here claimed
                // RemoveAllListeners prevented.
                //
                // UnityEvent.RemoveAllListeners() removes only the listeners added at RUNTIME
                // through AddListener. Valheim's menu buttons have their handlers wired in the
                // Inspector, which makes them PERSISTENT listeners serialized into the prefab,
                // and those are untouched by it. So the call did nothing to the one listener
                // that mattered.
                //
                // The IL test asserted that RemoveAllListeners was CALLED, and it was. Present
                // is not effective: the assertion was true and the button still started the
                // game. Persistent listeners have to be switched off one by one.
                DisableInheritedHandlers(button);

                button.onClick.AddListener(Invoke);
                button.interactable = true;

                SetLabel(_clone, label);
                Skin(_clone);

                _clone.SetActive(true);

                if (!_described)
                {
                    _described = true;
                    Describe(template, parent, button, group);
                }
                return true;
            }
            catch (Exception e)
            {
                // The STACK. "could not create it (NullReferenceException)" does not say which
                // of the reflected reads failed, and there are several.
                Main.StaticLogger.LogWarning($"Menu button: could not create it -- falling back to the drawn button. {e}");
                Remove();
                return false;
            }
        }

        // Three independent routes to a template, because the first one is the only one that
        // can be wrong in a way that compiles.
        //
        // m_menuButtons is read by reflection against a field whose name and type were
        // confirmed in the real assembly (Button[] m_menuButtons) -- but a reflected read is
        // still the one step here that a game update can break silently, and an array that
        // exists but holds nulls would get past a null check on the array itself. The menu
        // demonstrably HAS working buttons whenever it is on screen, so routes 2 and 3 cannot
        // both come up empty on a menu the player is looking at.
        private static Button FindTemplate(FejdStartup fejd)
        {
            var tried = new StringBuilder();

            Button[] buttons = null;
            try
            {
                buttons = fejd.GetPrivateField<Button[]>("m_menuButtons");
            }
            catch (Exception e)
            {
                tried.Append($"m_menuButtons threw {e.GetType().Name}; ");
            }

            var fromArray = FirstUsable(buttons);
            if (fromArray != null) return fromArray;
            tried.Append(buttons == null ? "m_menuButtons null; " : $"m_menuButtons had {buttons.Length} entries, none usable; ");

            foreach (var fieldName in new[] { "m_menuList", "m_mainMenu" })
            {
                GameObject root = null;
                try
                {
                    root = fejd.GetPrivateField<GameObject>(fieldName);
                }
                catch (Exception e)
                {
                    tried.Append($"{fieldName} threw {e.GetType().Name}; ");
                    continue;
                }

                if (root == null) { tried.Append($"{fieldName} null; "); continue; }

                var found = FirstUsable(root.GetComponentsInChildren<Button>(true));
                if (found != null)
                {
                    Main.StaticLogger.LogMessage($"Menu button: m_menuButtons gave no template, using \"{found.gameObject.name}\" found under {fieldName} instead.");
                    return found;
                }
                tried.Append($"{fieldName} had no usable Button; ");
            }

            Warn($"no usable template anywhere -- {tried}");
            return null;
        }

        // "Usable" means it has a live GameObject AND a parent to clone into. A Button whose
        // gameObject is fine but which sits at the root of its canvas would pass a null check
        // and then fail at the parent read, one step further on.
        private static Button FirstUsable(Button[] candidates)
        {
            if (candidates == null) return null;
            foreach (var b in candidates)
            {
                if (b == null) continue;
                if (b.gameObject == null) continue;
                if (b.transform == null || b.transform.parent == null) continue;
                return b;
            }
            return null;
        }

        // Read back off the live objects. Every field here answers a question that came up as
        // a theory during the gray-button rounds and could not be settled from the log:
        //
        //   canvas            -- is it on a canvas with a raycaster at all
        //   cover active      -- UnifiedPopup's fullScreenBackgroundCover is a full-screen
        //                        input blocker; left active it eats the click and the button
        //                        looks dead while looking perfectly fine
        //   corners           -- where it actually IS in screen space, so "off screen" stops
        //                        being a theory
        //   interactable      -- a non-interactable Button renders GRAYED and takes no clicks,
        //                        which is one cause for both of Brian's symptoms at once
        //   raycastTarget     -- an Image with this off is invisible to the pointer
        private static void Describe(Button template, Transform parent, Button button, LayoutGroup group)
        {
            try
            {
                var canvas = button.GetComponentInParent<Canvas>();
                var rect = button.transform as RectTransform;

                var corners = new Vector3[4];
                if (rect != null) rect.GetWorldCorners(corners);

                var img = button.GetComponent<Image>();
                var cg = button.GetComponentInParent<CanvasGroup>();

                bool coverActive = false;
                string coverState = "unknown";
                var popup = Utils.GetStaticFieldValue(typeof(UnifiedPopup), "instance");
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "fullScreenBackgroundCover", out var coverObj)
                    && coverObj is GameObject cover)
                {
                    coverActive = cover.activeInHierarchy;
                    coverState = coverActive ? "ACTIVE (it blocks clicks)" : "inactive";
                }

                Main.StaticLogger.LogMessage(
                    $"Menu button: cloned \"{template.gameObject.name}\" into \"{parent.name}\""
                    + $" (layoutGroup={(group == null ? "none" : group.GetType().Name)});"
                    + $" canvas={(canvas == null ? "NONE" : canvas.name)};"
                    + $" active={button.gameObject.activeInHierarchy};"
                    + $" interactable={button.interactable};"
                    + $" raycastTarget={(img == null ? "no Image" : img.raycastTarget.ToString())};"
                    + $" canvasGroup={(cg == null ? "none" : $"interactable={cg.interactable} blocksRaycasts={cg.blocksRaycasts}")};"
                    + $" screenRect={corners[0]}..{corners[2]};"
                    + $" fullScreenBackgroundCover={coverState}.");
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: created, but could not describe it ({e.GetType().Name}).");
            }
        }

        private static void Warn(string why)
        {
            Main.StaticLogger.LogWarning($"Menu button: {why} -- falling back to the drawn button.");
        }

        // Strip EVERY handler the clone inherited from the live menu button it was copied from.
        //
        // Two kinds, and they need two different calls:
        //
        //   runtime listeners    -- added via AddListener; cleared by RemoveAllListeners()
        //   persistent listeners -- wired in the Unity Inspector and serialized in the prefab;
        //                           NOT cleared by RemoveAllListeners(), which is why the
        //                           clone of "Start Game" still started the game
        //
        // Persistent ones are disabled by index rather than removed, because there is no
        // runtime API to remove them -- SetPersistentListenerState(i, Off) is the supported
        // way, and it only affects this clone's own copy of the event.
        //
        // Logged with a count, because "we disabled the handlers" and "there were none to
        // disable" look identical otherwise, and the difference decides whether a future
        // click-does-two-things report is this code or something else.
        private static void DisableInheritedHandlers(Button button)
        {
            button.onClick.RemoveAllListeners();

            int persistent = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < persistent; i++)
            {
                button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            }

            Main.StaticLogger.LogMessage(
                $"Menu button: cleared runtime listeners and switched off {persistent} inherited persistent listener(s) "
                + "(these are the ones RemoveAllListeners does not touch -- the reason the clone used to start the game).");
        }

        // Separate named method rather than a lambda so the IL carries a call this mod's own
        // reachability test can see.
        private static void Invoke()
        {
            Main.StaticLogger.LogMessage("PhValheim menu button clicked; handing the dialog to Update().");
            var handler = _onClick;
            if (handler != null) handler();
        }

        internal static void Remove()
        {
            if (_clone == null) return;
            try
            {
                UnityEngine.Object.Destroy(_clone);
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: could not destroy it ({e.GetType().Name}).");
            }
            _clone = null;
        }

        // PhValheim's colours on the button's own art.
        //
        // Only the clone is touched, and the clone is ours, so there is nothing to save or put
        // back here -- unlike everything in ConnectDialog, which works on the shared
        // UnifiedPopup singleton. Destroying the clone is the whole restore.
        private static void Skin(GameObject go)
        {
            try
            {
                Color bg;
                if (!ColorUtility.TryParseHtmlString(Theme.ButtonFill, out bg)) return;

                // The button's own frame, and only that -- GetComponent, not
                // GetComponentsInChildren. A menu button can carry child Images for its
                // selection highlight and gamepad cursor; recolouring those would leave a
                // permanently-highlighted-looking button.
                var img = go.GetComponent<Image>();
                if (img != null) img.color = bg;

                // Valheim tints the button's graphic through its own ColorBlock on hover and
                // press, and those multiply against the sprite, NOT against img.color -- so
                // the colour above would be thrown away the first time the pointer touched it.
                // Rewriting the ColorBlock keeps the hover and press feedback the player
                // expects while keeping it in PhValheim's palette.
                var button = go.GetComponent<Button>();
                if (button != null)
                {
                    Color hover, press;
                    var colors = button.colors;
                    colors.normalColor = Color.white;
                    if (ColorUtility.TryParseHtmlString(Theme.AccentHover, out hover)) colors.highlightedColor = hover;
                    if (ColorUtility.TryParseHtmlString(Theme.Accent, out press)) colors.pressedColor = press;
                    button.colors = colors;
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: could not skin it ({e.GetType().Name}).");
            }
        }

        // The label is RICH TEXT -- two colours in one string, magenta prefix and cyan world
        // name, built by ConnectDialog.ReopenButtonLabel.
        //
        // THE BASE COLOUR MUST BE WHITE. TMP multiplies a <color> tag against the component's
        // own `color` property, so leaving the violet theme colour on the component would
        // darken and skew both tag colours -- the magenta would not be magenta. White is the
        // identity for that multiply, which is why the per-component tint that used to be here
        // is gone rather than merely changed.
        private static void SetLabel(GameObject go, string label)
        {
            try
            {
                // Both text kinds. Valheim's menu buttons are TMP, but a clone that picked up a
                // legacy Text somewhere would otherwise keep the template's own wording -- a
                // button reading "Start Game" that opens our notice is worse than no button.
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.richText = true;             // default, but the label is meaningless without it
                    t.color = Color.white;
                    t.text = label;
                }
                foreach (var t in go.GetComponentsInChildren<Text>(true))
                {
                    t.supportRichText = true;
                    t.color = Color.white;
                    t.text = label;
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: could not set its label ({e.GetType().Name}).");
            }
        }
    }
}
