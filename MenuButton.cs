using System;
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
    // Four rounds went into that button on the assumption the problem was dialog STATE. It was
    // not. The lesson for next time is that "drawn" and "clickable" are separate facts and the
    // log has to carry both -- adding the draw line is what ended the guessing.
    //
    // WHY A CLONE
    // FejdStartup.m_menuButtons are the menu's own buttons, so instantiating one inherits
    // Valheim's art, font, hover sound and click wiring for free. Nothing here styles a button
    // from scratch, and nothing depends on a prefab path that could move between versions.
    internal static class MenuButton
    {
        private const string CloneName = "PhValheimReopenButton";

        private static GameObject _clone;
        private static Action _onClick;

        // Whether a native button currently exists. ConnectDialog reads this to decide whether
        // the IMGUI fallback is still needed, so a failure here is never silent.
        internal static bool Exists => _clone != null;

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
                if (fejd == null) return false;

                var buttons = fejd.GetPrivateField<Button[]>("m_menuButtons");
                if (buttons == null || buttons.Length == 0)
                {
                    Main.StaticLogger.LogWarning("Menu button: FejdStartup.m_menuButtons is empty -- falling back to the drawn button.");
                    return false;
                }

                Button template = null;
                foreach (var b in buttons)
                {
                    if (b != null && b.gameObject != null) { template = b; break; }
                }
                if (template == null)
                {
                    Main.StaticLogger.LogWarning("Menu button: no usable template in m_menuButtons -- falling back to the drawn button.");
                    return false;
                }

                // Parented to the CANVAS, not to the template's own parent. The menu's button
                // column is driven by a layout group: a clone dropped in there would be given a
                // row of its own and shove every vanilla button up. On the canvas it is
                // anchored where we put it and Valheim's own menu is untouched.
                var canvas = template.GetComponentInParent<Canvas>();
                Transform parent = canvas != null ? canvas.transform : template.transform.parent;

                _clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                _clone.name = CloneName;

                var rect = _clone.transform as RectTransform;
                if (rect != null)
                {
                    // Bottom centre, clear of the menu column. Anchors set together with the
                    // pivot, or the offset is measured from whatever the template happened to
                    // use and the button lands off screen.
                    rect.anchorMin = new Vector2(0.5f, 0f);
                    rect.anchorMax = new Vector2(0.5f, 0f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.anchoredPosition = new Vector2(0f, 60f);
                    rect.localScale = Vector3.one;
                }

                SetLabel(_clone, label);

                var button = _clone.GetComponent<Button>();
                if (button == null)
                {
                    Main.StaticLogger.LogWarning("Menu button: the clone has no Button component -- falling back to the drawn button.");
                    Remove();
                    return false;
                }

                // RemoveAllListeners FIRST. The template is a live menu button with Valheim's
                // own handler on it -- left in place, this button would also Start Game.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(Invoke);
                button.interactable = true;

                _clone.SetActive(true);

                Main.StaticLogger.LogMessage($"Menu button created as a clone of \"{template.gameObject.name}\" under \"{parent.name}\".");
                return true;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: could not create it ({e.GetType().Name}: {e.Message}) -- falling back to the drawn button.");
                Remove();
                return false;
            }
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

        private static void SetLabel(GameObject go, string label)
        {
            try
            {
                Color themed;
                bool themeOk = ColorUtility.TryParseHtmlString(Theme.Button, out themed);
                // Both text kinds. Valheim's menu buttons are TMP, but a clone that picked up a
                // legacy Text somewhere would otherwise keep the template's own wording -- a
                // button reading "Start Game" that opens our notice is worse than no button.
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.text = label;
                    if (themeOk) t.color = themed;
                }
                foreach (var t in go.GetComponentsInChildren<Text>(true))
                {
                    t.text = label;
                    if (themeOk) t.color = themed;
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Menu button: could not set its label ({e.GetType().Name}).");
            }
        }
    }
}
