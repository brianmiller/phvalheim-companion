using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PhValheimCompanion
{
    // PhValheim's colours on the popup's own art. SECOND ATTEMPT, against a measured tree.
    //
    // WHAT THE FIRST ATTEMPT DID AND WHY IT FAILED
    // It identified the panel's background as "the largest Image under popupUIParent", hid it,
    // and inserted a solid quad copied from its rect. Brian got a full screen with a border
    // and no text. His log now says exactly why:
    //
    //   popupUIParent="PopupBlockingBackground" rect=1920x1200 screen=1920x1200
    //     [PopupBlockingBackground 1920x1200 sprite=none  color=0000005D]
    //     [FullscreenBlocker       1920x1200 sprite=load_bkg        INACTIVE]
    //     [Popup/bkg                 420x320 sprite=woodpanel_512x512]
    //     [Popup/ButtonOk            179x45  sprite=button  BUTTON]
    //
    // popupUIParent is itself a full-screen click blocker, and the two biggest Images under it
    // are screen-sized. The panel is "bkg", 420x320, and it is only the THIRD entry. Every
    // size-based rule I invented picked a blocker.
    //
    // WHAT THIS DOES INSTEAD
    // Targets the object by NAME -- "bkg" under a parent named "Popup" -- and refuses anything
    // that measures more than half the screen. That guard is not decoration: it is the check
    // that would have rejected PopupBlockingBackground outright, and it is asserted by a test.
    //
    // AND IT CREATES NOTHING. Only Image.color is written, and every original is saved. The
    // failure that made the dialog unusable was an inserted object landing over the text; with
    // no insertions that failure mode does not exist. The cost is that a tint MULTIPLIES
    // against the sprite, so this cannot produce an exact hex -- woodpanel is brown, so the
    // result is a slate-shifted panel, not #0f172a. Replacing the sprite outright is the only
    // way to get the web UI's flat slate, and that is not something to attempt without
    // seeing it.
    internal static class PanelSkin
    {
        // The panel must not be more than this fraction of the screen. PopupBlockingBackground
        // is 1.0 of it; the real panel is 420x320 on 1920x1200, which is 0.058.
        private const float MaxPanelScreenFraction = 0.5f;

        private static readonly List<KeyValuePair<Graphic, Color>> _saved =
            new List<KeyValuePair<Graphic, Color>>();

        private static bool _applied;
        private static bool _reported;

        // A background colour that keeps the palette's hue but is light enough to survive being
        // used as a MULTIPLIER. Theme.ButtonFill is slate 700 -- a background value in the web
        // UI, where it works because it sits ON slate 900 rather than being multiplied into a
        // sprite. Used raw it drags the panel to near-black. Doubling keeps the hue and the
        // contrast.
        private static bool TryTint(string html, out Color tint)
        {
            tint = Color.white;
            Color c;
            if (!ColorUtility.TryParseHtmlString(html, out c)) return false;
            tint = new Color(Mathf.Clamp01(c.r * 2f), Mathf.Clamp01(c.g * 2f), Mathf.Clamp01(c.b * 2f), 1f);
            return true;
        }

        internal static void Apply(GameObject panelRoot)
        {
            if (panelRoot == null || _applied) return;

            try
            {
                Color tint;
                if (!TryTint(Theme.ButtonFill, out tint)) return;

                var images = panelRoot.GetComponentsInChildren<Image>(true);
                if (images == null) return;

                float screenArea = Mathf.Max(1f, (float)Screen.width * Screen.height);
                int painted = 0;

                foreach (var img in images)
                {
                    if (img == null || img.rectTransform == null) continue;
                    if (!IsPanelBackground(img, screenArea) && img.GetComponent<Button>() == null) continue;

                    // A button that is inactive in this popup is one of the three Valheim is
                    // not using (ButtonYes/ButtonNo/ButtonConfirm in the tree above). Painting
                    // them costs nothing visually but leaves more state to restore.
                    if (img.GetComponent<Button>() != null && !img.gameObject.activeInHierarchy) continue;

                    _saved.Add(new KeyValuePair<Graphic, Color>(img, img.color));
                    img.color = tint;
                    painted++;
                }

                _applied = painted > 0;

                if (!_reported)
                {
                    _reported = true;
                    if (_applied)
                        Main.StaticLogger.LogMessage($"Panel skin: tinted {painted} object(s) with {Theme.ButtonFill} doubled; no objects created.");
                    else
                        Main.StaticLogger.LogWarning("Panel skin: found no \"bkg\" under a \"Popup\" within half the screen -- the panel keeps Valheim's colours. Compare the panel tree line above.");
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Panel skin: could not apply it ({e.GetType().Name}); restoring. {e.Message}");
                Restore();
            }
        }

        // BY NAME, with a size SANITY CHECK -- not by size alone, which is what shipped broken.
        //
        // Both conditions are required. The name alone would be fragile across Valheim
        // versions; the size alone is precisely the rule that picked a 1920x1200 blocker. The
        // size half of it can only ever REJECT, so it cannot select the wrong object on its own.
        private static bool IsPanelBackground(Image img, float screenArea)
        {
            if (img == null || img.rectTransform == null) return false;
            var parent = img.transform.parent;
            var r = img.rectTransform.rect;
            return IsPanelBackground(img.gameObject.name, parent == null ? null : parent.name,
                                     r.width, r.height, screenArea);
        }

        // The selector, as a pure function of the four things the decision actually turns on.
        //
        // Split out from the Image overload for one reason: a Unity Image cannot be built
        // outside the game, so as a method taking an Image this rule was untestable -- and an
        // untestable selector is precisely what shipped a full-screen box. As plain values it
        // can be driven with the exact numbers out of Brian's log, including the two blockers
        // it has to REJECT. dev_tools/renderDialog does that.
        internal static bool IsPanelBackground(string name, string parentName,
                                               float width, float height, float screenArea)
        {
            if (name != "bkg") return false;
            if (parentName != "Popup") return false;

            float area = Mathf.Abs(width * height);
            if (area <= 1f) return false;                                 // not laid out yet
            if (screenArea <= 1f) return false;                           // no screen to compare against

            // The size half can only REJECT. Selection is by name; this exists solely to stop
            // a screen-sized object ever being treated as the panel again.
            return area <= screenArea * MaxPanelScreenFraction;
        }

        internal static void Restore()
        {
            for (int i = 0; i < _saved.Count; i++)
            {
                try { if (_saved[i].Key != null) _saved[i].Key.color = _saved[i].Value; }
                catch (Exception e) { Main.StaticLogger.LogWarning($"Panel skin: could not restore a colour ({e.GetType().Name})."); }
            }
            _saved.Clear();
            _applied = false;
        }
    }
}
