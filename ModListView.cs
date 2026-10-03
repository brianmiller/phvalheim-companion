using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PhValheimCompanion
{
    // A scrolling list of the player's installed mods, built on top of Valheim's popup.
    //
    // WHY THIS EXISTS AS ITS OWN OBJECT TREE
    //
    // The obvious implementation is to wrap UnifiedPopup's own bodyText in a ScrollRect. That
    // is rejected on purpose: it would RESTRUCTURE Valheim's popup prefab at runtime, and the
    // popup is a shared singleton reused by every confirm dialog in the game. If the teardown
    // missed anything -- a re-parent, a added Mask, a changed anchor -- every later popup in
    // the session would be subtly wrong, with no way for a player to recover short of
    // restarting. The blast radius of a mistake is the whole game's UI.
    //
    // So this builds its OWN GameObject under the popup's transform, reads Valheim's font off
    // bodyText so it matches, and destroys itself on close. Valheim's own objects are never
    // re-parented, resized or reconfigured. The worst case for a bug in here is that our list
    // looks wrong; it cannot damage anything else.
    //
    // GEOMETRY IS THE PART I CANNOT SEE
    //
    // Everything here is positioned RELATIVE to bodyText's RectTransform, read at runtime,
    // rather than against hardcoded screen coordinates -- there is no way to inspect a live
    // Unity hierarchy from the build host, so absolute numbers would be guesses. Build() is
    // wrapped in a try/catch by its caller and returns false on any failure, in which case the
    // dialog falls back to listing the mods inline. A broken scroll view must not take the
    // Connect button with it.
    internal static class ModListView
    {
        private const string ObjectName = "PhValheimModList";

        // The list occupies the BOTTOM this-much of the body's rect; the summary rows get the
        // rest, above it.
        //
        // This is a FRACTION OF THE PARENT, applied through normalized anchors, and that is the
        // whole point. The first version positioned the list by measuring the rendered height of
        // the body text and offsetting downward by it. That measurement was taken before
        // UnifiedPopup.Push, when the panel is not laid out yet, so it came back short: the list
        // landed on top of the closing sentence AND ran past the bottom of the panel, behind the
        // Connect and Close buttons.
        //
        // Anchors cannot do that. anchorMin.y = 0 / anchorMax.y = this stretches the list across
        // the bottom slice of the parent whatever size the parent turns out to be, so the list
        // is inside the body rect by construction -- no measurement, no layout timing, nothing
        // to get wrong at a different resolution or UI scale.
        //
        // 0.60, raised from 0.38 then 0.5. This is NOT a free dial: the body above the list has
        // to hold a two-line failure notice plus the three summary rows without overlapping it.
        // Measured off Brian's screenshot, the body rect is ~390 screen px, a line at
        // BodyScreenSize is ~30, and BodyLineBudgetWithList is 5 -- so the text needs ~150 and
        // the list can have at most 1 - 150/390 = 0.61 of the rect. 0.60 sits just under that
        // ceiling. Raising it further silently puts the failure notice behind the list, and the
        // one case that needs the notice is the one where the player is already stuck.
        // The split and ConnectDialog.BodyLineBudgetWithList are two halves of one decision;
        // move one and the render harness will tell you about the other.
        internal const float ListTopFraction = 0.60f;

        // How far BELOW the body rect the list is allowed to run, as a fraction of that rect.
        //
        // The body rect ends well above the Connect/Close buttons -- about 105 screen px of
        // empty panel sat between the two, which is most of what "too much padding" was. That
        // strip belongs to the panel, not to the body, so no fraction of the body rect can
        // reach it; only a NEGATIVE anchor can, and negative anchors are legal.
        //
        // Deliberately conservative. At the measured proportions this reclaims ~58 px and still
        // leaves ~47 px of clear panel above the buttons. Overshooting far enough to reach them
        // would put a scroll view on top of Connect, and a dialog whose Connect button cannot
        // be clicked is worse than one with a short list -- the same reason Build() is wrapped
        // in a try/catch. If the list ever appears to touch the buttons, this is the number.
        private const float ListBottomOvershoot = 0.15f;

        // Divided by the panel scale for the same reason everything in ConnectDialog is: this
        // object is built UNDER popupUIParent, so the panel's localScale multiplies it too. A
        // bare 16 here would grow with the panel and undo the shrink one file over.
        private const float ScrollbarScreenWidth = 14f;
        private const float ScrollbarWidth = ScrollbarScreenWidth / ConnectDialog.PanelScale;
        private const float RowScreenSize = 20f;
        private const float RowFontSize = RowScreenSize / ConnectDialog.PanelScale;

        // Keeps the bullets off the panel edge and off the scrollbar.
        private const float RowInsetScreen = 10f;
        private const float RowInset = RowInsetScreen / ConnectDialog.PanelScale;

        private static GameObject _root;

        internal static bool IsBuilt => _root != null;

        // Returns true if the list was built and the caller should therefore NOT list the mods
        // inline. Any failure returns false and leaves nothing behind.
        internal static bool Build(TMP_Text bodyText, List<string> mods)
        {
            Destroy();

            if (bodyText == null || mods == null || mods.Count == 0) return false;

            var bodyRect = bodyText.rectTransform;
            if (bodyRect == null) return false;

            try
            {
                _root = NewUIObject(ObjectName, bodyRect);
                var rootRect = (RectTransform)_root.transform;
                StretchBottom(rootRect, ListTopFraction);

                // A faint panel so the list reads as a distinct, scrollable region rather than
                // as body text that happens to be clipped.
                var bg = _root.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.25f);

                // RectMask2D rather than Mask: no stencil material, no extra draw call, and it
                // does not care what sprite the background uses.
                _root.AddComponent<RectMask2D>();

                var scroll = _root.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 24f;

                // Viewport
                var viewport = NewUIObject("Viewport", rootRect);
                var viewportRect = (RectTransform)viewport.transform;
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = new Vector2(-ScrollbarWidth, 0f);
                viewport.AddComponent<RectMask2D>();
                scroll.viewport = viewportRect;

                // Content: a single TMP text, height driven by its own preferred height. One
                // text object scrolls just as well as N row objects and costs far less.
                var content = NewUIObject("Content", viewportRect);
                var contentRect = (RectTransform)content.transform;
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0f, 1f);
                contentRect.anchoredPosition = Vector2.zero;

                var text = content.AddComponent<TextMeshProUGUI>();
                text.font = bodyText.font;
                text.fontSharedMaterial = bodyText.fontSharedMaterial;
                text.enableAutoSizing = false;
                text.fontSize = RowFontSize;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.richText = true;
                text.margin = new Vector4(RowInset, RowInset * 0.5f, RowInset, RowInset * 0.5f);

                // One mod per line, never wrapped. This is what makes the height below EXACT
                // rather than estimated: with wrapping on, a long mod name silently becomes two
                // lines and every row after it is off by one line's worth.
                // enableWordWrapping, not textWrappingMode. The reference assembly here is a
                // NEWER TextMeshPro than the one Valheim ships and marks this obsolete, but
                // obsolete is not removed, and the replacement property does not exist in older
                // TMP at all. Compiling against the newer name would bind to a member the game
                // may not have -- the same class of failure as the publicizer trap, just via a
                // reference that is too new rather than too permissive.
#pragma warning disable CS0618
                text.enableWordWrapping = false;
#pragma warning restore CS0618
                text.overflowMode = TextOverflowModes.Overflow;
                text.text = BuildRows(mods);

                text.ForceMeshUpdate();

                // THE CONTENT HEIGHT IS COMPUTED, NOT MEASURED, AND THAT IS THE WHOLE FIX.
                //
                // This was `text.preferredHeight`, read immediately after ForceMeshUpdate. That
                // runs before the layout pass has given this rect its real width, so TMP
                // answered for a rect it had not been laid out in yet and returned roughly one
                // viewport's worth. ScrollRect clamps travel to (content - viewport), so a
                // content that size means there is nothing to scroll: the bar appeared, its
                // handle filled ~97% of the track, and 34 mods sat in a list that would not
                // move. The scrollbar being visible made it look like a scrolling problem when
                // it was a measurement problem.
                //
                // Line count is known exactly -- one line per mod, wrapping disabled above -- so
                // the height is arithmetic and depends on nothing that has to have happened
                // first. Font metrics give the real line height where they are available;
                // 1.25em is the fallback, which is close for Valheim's font and never zero.
                // Taken from the laid-out text's OWN first line rather than from the font's
                // FaceInfo: FaceInfo lives in UnityEngine.TextCoreFontEngineModule, which this
                // mod does not reference and should not start referencing for one number.
                // textInfo is produced by the ForceMeshUpdate above and is part of TextMeshPro
                // itself, so it costs no new dependency. 1.25em is the fallback for the case
                // where there are no lines to read -- never zero, which would mean no scrolling.
                float lineHeight = RowFontSize * 1.25f;
                if (text.textInfo != null && text.textInfo.lineCount > 0
                    && text.textInfo.lineInfo != null && text.textInfo.lineInfo.Length > 0
                    && text.textInfo.lineInfo[0].lineHeight > 0f)
                {
                    lineHeight = text.textInfo.lineInfo[0].lineHeight;
                }
                float contentHeight = (mods.Count * lineHeight) + RowInset;
                contentRect.sizeDelta = new Vector2(0f, contentHeight);
                scroll.content = contentRect;

                BuildScrollbar(scroll, rootRect);

                // Read the geometry BACK rather than echoing what was asked for -- the same
                // reason ConnectDialog logs the body style it reads. These numbers are the only
                // view of this layout available from outside the game, and the scrollbar
                // question in particular is "is the content taller than the viewport", which
                // only the measured values can answer.
                Main.StaticLogger.LogMessage(
                    $"Mod list: {mods.Count} mods, listH={rootRect.rect.height:0}, contentH={contentHeight:0} "
                    + $"(lineH={lineHeight:0.0}, tmpPreferred={text.preferredHeight:0}), "
                    + $"scrollable={(contentHeight > rootRect.rect.height ? "yes" : "no")}, "
                    + $"rowSize={text.fontSize}, anchors={rootRect.anchorMin.y:0.00}..{rootRect.anchorMax.y:0.00}.");

                return true;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not build the scrolling mod list ({e.GetType().Name}: {e.Message}); listing the mods inline instead.");
                Destroy();
                return false;
            }
        }

        private static void BuildScrollbar(ScrollRect scroll, RectTransform parent)
        {
            var barObj = NewUIObject("Scrollbar", parent);
            var barRect = (RectTransform)barObj.transform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.sizeDelta = new Vector2(ScrollbarWidth, 0f);
            barRect.anchoredPosition = Vector2.zero;

            var barBg = barObj.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.35f);

            var bar = barObj.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var handleArea = NewUIObject("SlidingArea", barRect);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            var handle = NewUIObject("Handle", handleAreaRect);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            // Valheim's parchment-on-dark palette, so the bar does not read as a browser widget
            // dropped into the game.
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.91f, 0.85f, 0.63f, 0.55f);

            bar.targetGraphic = handleImage;
            bar.handleRect = handleRect;

            scroll.verticalScrollbar = bar;

            // Permanent, NOT AutoHide. The list shipped with no visible scrollbar at all on a
            // world with 34 mods, where it plainly should have been showing one: AutoHide makes
            // Unity disable the scrollbar's GameObject whenever it decides the content fits, and
            // that decision is made from the content size at the moment it is evaluated -- which
            // is before the first layout pass has given this rect its real height.
            //
            // Permanent sidesteps the ordering question entirely. The viewport already reserves
            // ScrollbarWidth whether or not the bar is drawn, so showing it always costs no
            // layout room, and a list the player can see is scrollable is worth more than a few
            // pixels saved on the rare world with three mods.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static string BuildRows(List<string> mods)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < mods.Count; i++)
            {
                // Angle brackets stripped for the same reason as everywhere else: mod names are
                // operator data landing in a rich-text field.
                string name = mods[i] == null ? "" : mods[i].Replace('<', ' ').Replace('>', ' ');
                sb.Append("<color=").Append(Theme.Muted).Append(">  • ").Append(name).Append("</color>");
                if (i < mods.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        private static GameObject NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        // Stretch across the BOTTOM `fraction` of the parent, full width. Everything is
        // normalized, so the list scales with the parent and can never be positioned outside
        // it -- see the note on ListTopFraction for what the measured version got wrong.
        private static void StretchBottom(RectTransform rect, float fraction)
        {
            // anchorMin.y is NEGATIVE on purpose -- see ListBottomOvershoot. It extends the list
            // down into the panel's own padding below the body rect, which is space nothing else
            // uses and which no positive fraction can reach.
            rect.anchorMin = new Vector2(0f, -ListBottomOvershoot);
            rect.anchorMax = new Vector2(1f, fraction);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Called from every path that closes the dialog. Idempotent on purpose: the dialog can
        // be closed by Connect, by Close, or by being rebuilt, and a leaked list would stack a
        // second copy on top of the first.
        internal static void Destroy()
        {
            if (_root == null) return;
            try
            {
                UnityEngine.Object.Destroy(_root);
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not remove the mod list cleanly ({e.GetType().Name}).");
            }
            _root = null;
        }
    }
}
