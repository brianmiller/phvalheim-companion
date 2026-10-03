using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PhValheimCompanion
{
    // The BOX, as opposed to the words in it.
    //
    // Brian, on the first theming pass: "The styling added to the dialog is really just the
    // blue text. The entire thing, including the box background, buttons and border should be
    // in PhValheim's style." He is right, and the reason is worth writing down because it is
    // not obvious: the first pass recoloured every TMP string in the dialog and left Valheim's
    // parchment panel art untouched, and the panel is by far the largest thing on screen. It
    // decides what the dialog reads as. Cyan text on brown parchment reads as a Valheim dialog
    // with odd text.
    //
    // WHY THE BACKGROUND IS DRAWN AND NOT TINTED
    // The obvious move is Image.color on the panel art. It does not work, and the arithmetic
    // says so before any screenshot does: Unity multiplies the tint against the SPRITE, so the
    // result is spriteColour * tint and never the tint itself. Valheim's panel sprite is a
    // mid-brown, so tinting it with slate 900 (#0f172a, already almost black) multiplies down
    // to effectively black, and tinting it with anything light enough to stay visible keeps
    // the brown's hue. There is no tint that turns a brown sprite slate-blue.
    //
    // So Valheim's background art is made transparent -- its ALPHA only, which is reversible
    // and leaves the GameObject and its layout alone -- and a solid PhValheim-coloured quad is
    // inserted in its place, geometry copied from the art it replaces. An Image with no sprite
    // draws a plain rect, which is exactly what the web UI's panels are.
    //
    // The border is four solid edge strips rather than an Outline effect: Outline operates on
    // the sprite's own geometry, which is the thing being hidden here.
    //
    // WHY THE GEOMETRY IS COPIED FROM THE ART AND NOT FROM popupUIParent
    // popupUIParent is a GameObject whose rect could be the panel OR a full-screen container
    // holding it -- it cannot be inspected from a build host, and getting it wrong means a
    // PhValheim-coloured quad over the WHOLE SCREEN. Copying the anchors and offsets of the
    // largest background Image sidesteps the question: whatever rect Valheim's own panel art
    // occupies is by definition the right rect for the panel's background.
    //
    // EVERYTHING HERE IS ON THE SHARED UnifiedPopup SINGLETON, so the same rule as the rest of
    // ConnectDialog applies: every colour touched is saved, every object created is destroyed,
    // or vanilla's "Remove this character?" inherits our skin for the rest of the session.
    internal static class PanelSkin
    {
        private const string BackdropName = "PhValheimPanelBackdrop";
        private const string BorderName   = "PhValheimPanelBorder";

        // How thick the border is, in the panel's LOCAL units. A constant, not divided by the
        // active scale -- for the same reason the header and body nudges are constants: local
        // space is identical at every scale, so a constant comes out as the same FRACTION of
        // whichever panel it is drawn in. Divided by the scale it would be a chunky frame on
        // the small notice and a hairline on the big connect dialog.
        private const float BorderThickness = 2f;

        // An Image counts as background art if its area is at least this fraction of the
        // largest Image found under the panel. The panel art is the biggest Image in a popup by
        // a wide margin -- the others are button faces and the odd divider -- so this separates
        // them without needing to know any of their names.
        private const float BackgroundAreaFraction = 0.80f;

        private static readonly List<KeyValuePair<Graphic, Color>> _savedColors =
            new List<KeyValuePair<Graphic, Color>>();

        private static readonly List<GameObject> _created = new List<GameObject>();

        private static bool _applied;
        private static bool _described;

        internal static bool Applied => _applied;

        // Paints the panel. Never throws: the skin is cosmetic, and a dialog in Valheim's own
        // colours is enormously better than no dialog.
        internal static void Apply(GameObject panel)
        {
            if (panel == null || _applied) return;

            try
            {
                var images = panel.GetComponentsInChildren<Image>(true);
                if (images == null || images.Length == 0)
                {
                    Main.StaticLogger.LogWarning("Panel skin: no Image components under the popup panel -- it keeps Valheim's art.");
                    return;
                }

                // The largest Image, by rect area, is the panel's background art.
                Image biggest = null;
                float biggestArea = 0f;
                foreach (var img in images)
                {
                    if (img == null || img.rectTransform == null) continue;
                    var r = img.rectTransform.rect;
                    float area = Mathf.Abs(r.width * r.height);
                    if (area > biggestArea) { biggestArea = area; biggest = img; }
                }

                if (biggest == null || biggestArea <= 1f)
                {
                    Main.StaticLogger.LogWarning($"Panel skin: the largest Image under the panel has area {biggestArea:0} -- the panel is not laid out yet, so it keeps Valheim's art.");
                    return;
                }

                if (!_described) { _described = true; Describe(images, biggest, biggestArea); }

                // Valheim's art, alpha only. The Image, its sprite, its material and its rect
                // are all left exactly as they were, so the restore is one colour write and
                // the layout cannot have moved under us.
                foreach (var img in images)
                {
                    if (img == null || img.rectTransform == null) continue;
                    var r = img.rectTransform.rect;
                    float area = Mathf.Abs(r.width * r.height);

                    if (area >= biggestArea * BackgroundAreaFraction)
                    {
                        Save(img);
                        var c = img.color;
                        img.color = new Color(c.r, c.g, c.b, 0f);
                    }
                }

                // Our own background, in the rect Valheim's art just vacated.
                var backdrop = AddQuad(biggest.rectTransform.parent, BackdropName, Theme.PanelFill);
                if (backdrop == null)
                {
                    // Put the art back rather than leave a popup with no background at all --
                    // a transparent panel over the main menu is unreadable, which is strictly
                    // worse than the wrong colour.
                    Main.StaticLogger.LogWarning("Panel skin: could not create the backdrop -- restoring Valheim's art.");
                    Restore();
                    return;
                }

                CopyRect(biggest.rectTransform, backdrop.rectTransform);

                // Immediately AFTER the art it replaces, so it sits behind every sibling that
                // was already drawing on top of that art -- the header, the body, the buttons.
                // Last-child would put it over the text.
                backdrop.transform.SetSiblingIndex(biggest.rectTransform.GetSiblingIndex() + 1);

                AddBorder(backdrop.rectTransform);
                SkinButtons(images, biggestArea);

                _applied = true;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Panel skin: could not apply it, the panel keeps Valheim's art. {e}");
                Restore();
            }
        }

        // The button faces. These ARE tinted rather than replaced: unlike the background, a
        // button has to keep its hover and press feedback, which Valheim drives through the
        // Button's own ColorBlock multiplying against this sprite. Replacing the face with a
        // quad would mean reimplementing that feedback.
        //
        // A tint cannot reach an exact colour (see the class comment), so the aim here is
        // narrower and achievable: pull the brown toward PhValheim's slate and let the violet
        // label carry the identity.
        private static void SkinButtons(Image[] images, float biggestArea)
        {
            Color fill;
            if (!ColorUtility.TryParseHtmlString(Theme.ButtonFill, out fill)) return;

            // Lifted to a mid-tone before being used as a tint. ButtonFill is slate 700, which
            // is a BACKGROUND colour in the web UI -- used directly as a multiplier against an
            // already-dark sprite it produces a button indistinguishable from the panel behind
            // it. Doubling it keeps the hue and restores the contrast the web UI gets by
            // putting slate 700 against slate 900 rather than by multiplying the two.
            var tint = new Color(
                Mathf.Clamp01(fill.r * 2f),
                Mathf.Clamp01(fill.g * 2f),
                Mathf.Clamp01(fill.b * 2f),
                1f);

            foreach (var img in images)
            {
                if (img == null || img.rectTransform == null) continue;

                // Anything already hidden as background art, and anything we created.
                var r = img.rectTransform.rect;
                if (Mathf.Abs(r.width * r.height) >= biggestArea * BackgroundAreaFraction) continue;
                if (img.gameObject.name == BackdropName || img.gameObject.name == BorderName) continue;

                // Only Images that belong to a Button. A popup's non-button Images are
                // dividers and gamepad cursors, and recolouring a selection cursor leaves a
                // button that looks permanently highlighted.
                if (img.GetComponent<Button>() == null) continue;

                Save(img);
                img.color = tint;
            }
        }

        private static void AddBorder(RectTransform backdrop)
        {
            var frame = AddQuad(backdrop, BorderName, null);
            if (frame == null) return;

            Stretch(frame.rectTransform);
            frame.color = new Color(0f, 0f, 0f, 0f);   // the container itself draws nothing

            // Four strips. Each is stretched along its own edge and fixed-thickness across it,
            // so the frame follows the panel at any size without arithmetic.
            Edge(frame.rectTransform, "Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -BorderThickness), new Vector2(0f, 0f));
            Edge(frame.rectTransform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, BorderThickness));
            Edge(frame.rectTransform, "Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(BorderThickness, 0f));
            Edge(frame.rectTransform, "Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-BorderThickness, 0f), new Vector2(0f, 0f));
        }

        private static void Edge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var strip = AddQuad(parent, BorderName + name, Theme.PanelEdge);
            if (strip == null) return;

            var rt = strip.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        // A solid rect. sprite stays null, which is what makes an Image draw a plain quad --
        // no art to find, nothing that can go missing between Valheim versions.
        private static Image AddQuad(Transform parent, string name, string htmlColor)
        {
            if (parent == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            _created.Add(go);

            var img = go.GetComponent<Image>();

            // Our own decoration must never intercept a click meant for the button behind it.
            // The backdrop covers the entire panel, so with this left on it would swallow every
            // click in the dialog -- the exact failure that cost four rounds on the reopen
            // button, reintroduced one layer up.
            img.raycastTarget = false;

            Color c;
            if (htmlColor != null && ColorUtility.TryParseHtmlString(htmlColor, out c)) img.color = c;

            return img;
        }

        private static void CopyRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.offsetMin = from.offsetMin;
            to.offsetMax = from.offsetMax;
            to.anchoredPosition = from.anchoredPosition;
            to.sizeDelta = from.sizeDelta;
            to.localScale = Vector3.one;
            to.localRotation = Quaternion.identity;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static void Save(Graphic g)
        {
            if (g == null) return;
            _savedColors.Add(new KeyValuePair<Graphic, Color>(g, g.color));
        }

        // One structural report, once. The popup's Image tree cannot be inspected from a build
        // host, so without this the next report -- "the box is black now" -- would start
        // another round of guessing at which Image is which. Names, areas and colours make the
        // next change a targeted one.
        private static void Describe(Image[] images, Image biggest, float biggestArea)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append($"Panel skin: {images.Length} Images under the panel; background art is \"{biggest.gameObject.name}\" at {biggestArea:0} sq. Tree:");
                foreach (var img in images)
                {
                    if (img == null || img.rectTransform == null) continue;
                    var r = img.rectTransform.rect;
                    sb.Append($" [{img.gameObject.name} {r.width:0}x{r.height:0}"
                              + $" sprite={(img.sprite == null ? "none" : img.sprite.name)}"
                              + $" color={ColorUtility.ToHtmlStringRGBA(img.color)}"
                              + $"{(img.GetComponent<Button>() != null ? " BUTTON" : "")}]");
                }
                Main.StaticLogger.LogMessage(sb.ToString());
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Panel skin: applied, but could not describe the panel ({e.GetType().Name}).");
            }
        }

        // Called from ConnectDialog.RestorePopupSkin, on every path that closes the dialog.
        //
        // Colours first, then the objects: a destroyed GameObject's Image is null, and
        // restoring a colour onto it would throw partway through the list and leave the rest of
        // the panel skinned forever.
        internal static void Restore()
        {
            for (int i = 0; i < _savedColors.Count; i++)
            {
                var entry = _savedColors[i];
                try
                {
                    if (entry.Key != null) entry.Key.color = entry.Value;
                }
                catch (Exception e)
                {
                    Main.StaticLogger.LogWarning($"Panel skin: could not restore a colour ({e.GetType().Name}).");
                }
            }
            _savedColors.Clear();

            for (int i = 0; i < _created.Count; i++)
            {
                try
                {
                    if (_created[i] != null) UnityEngine.Object.Destroy(_created[i]);
                }
                catch (Exception e)
                {
                    Main.StaticLogger.LogWarning($"Panel skin: could not destroy \"{_created[i]?.name}\" ({e.GetType().Name}).");
                }
            }
            _created.Clear();

            _applied = false;
        }
    }
}
