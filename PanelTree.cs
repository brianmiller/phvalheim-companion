using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PhValheimCompanion
{
    // A READ-ONLY report on the popup's Image tree. It changes nothing.
    //
    // WHY IT EXISTS, AND WHY IT IS NOT A RESKIN
    // Brian asked for the dialog's box -- background, border, buttons -- to be in PhValheim's
    // colours, not just its text. The attempt at that reskinned the panel by finding the
    // background art as "the largest Image under popupUIParent", hiding it, and inserting a
    // solid quad copied from its rect. He got a FULL SCREEN with a border and no text.
    //
    // Which means the largest Image under popupUIParent is a full-screen overlay, not the
    // panel's background -- so the quad was screen-sized, and inserting it just above that
    // overlay in sibling order put it over the panel's text rather than behind it.
    //
    // Three separate rules in that code were invented on a build host and presented as
    // measurements: that the biggest Image is the background, that everything within 80% of
    // its area is also background, and that sibling index + 1 is behind the content. All three
    // were wrong at once, which is why the result was not a slightly-off panel but an
    // unusable one.
    //
    // So this logs the tree and stops. Once the real names, sizes and sprites are known, a
    // reskin can be written against them -- targeting the panel's actual background object by
    // NAME, with the geometry it really has. Until then the dialog keeps Valheim's panel art,
    // which is readable, and PhValheim's palette on every piece of text in it, which Brian had
    // already accepted.
    internal static class PanelTree
    {
        private static bool _logged;

        // One report per session. The tree is the same every time the popup is built, so a
        // per-show log would be noise in the file this is meant to make readable.
        internal static void LogOnce(GameObject panel)
        {
            if (_logged || panel == null) return;
            _logged = true;

            try
            {
                var images = panel.GetComponentsInChildren<Image>(true);
                var sb = new StringBuilder();
                sb.Append($"PhValheim panel tree: popupUIParent=\"{panel.name}\"");

                var panelRect = panel.transform as RectTransform;
                if (panelRect != null) sb.Append($" rect={panelRect.rect.width:0}x{panelRect.rect.height:0}");
                sb.Append($" screen={Screen.width}x{Screen.height}");
                sb.Append($"; {(images == null ? 0 : images.Length)} Images:");

                if (images != null)
                {
                    foreach (var img in images)
                    {
                        if (img == null) continue;
                        var rt = img.rectTransform;

                        // The PATH, not just the name. Half the trouble was not knowing which
                        // object was a child of which -- sibling order decides what draws over
                        // what, and that cannot be read off a flat list of names.
                        sb.Append($" [{Path(img.transform, panel.transform)}");
                        if (rt != null)
                        {
                            sb.Append($" {rt.rect.width:0}x{rt.rect.height:0}");
                            sb.Append($" idx={rt.GetSiblingIndex()}");
                        }
                        sb.Append($" sprite={(img.sprite == null ? "none" : img.sprite.name)}");
                        sb.Append($" color={ColorUtility.ToHtmlStringRGBA(img.color)}");
                        if (img.GetComponent<Button>() != null) sb.Append(" BUTTON");
                        if (!img.raycastTarget) sb.Append(" noRaycast");
                        if (!img.gameObject.activeInHierarchy) sb.Append(" INACTIVE");
                        sb.Append(']');
                    }
                }

                Main.StaticLogger.LogMessage(sb.ToString());
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not report the panel tree ({e.GetType().Name}).");
            }
        }

        // "a/b/c" relative to the panel, so the report shows the hierarchy rather than a flat
        // list of names that could each be anywhere.
        private static string Path(Transform t, Transform root)
        {
            var parts = new StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null && cur != root)
            {
                parts.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return parts.ToString();
        }
    }
}
