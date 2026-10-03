namespace PhValheimCompanion
{
    // PhValheim's palette, as used inside Valheim.
    //
    // ONE copy, because there were four. ConnectDialog had three constants, MenuButton had its
    // own duplicate of a fourth, ModListView had a hex inline in a format string, and
    // FejdStartupPatch had another in the version label -- so "re-theme the dialog" meant
    // finding all of them, and the mod-list bullets were still on the old colour after the
    // first pass. The palette check in dev_tools/test-client-manifest.sh found that, which is
    // the whole reason it carries a control for the retired values.
    //
    // Every value is copied from :root in container/nginx/www/css/phvalheimStyles.css and is
    // named for the variable it came from. That test re-reads the stylesheet and fails if these
    // drift, so re-theming the web UI cannot silently leave the in-game UI behind.
    //
    // Hex strings rather than Color structs: TextMeshPro's rich text takes <color=#rrggbb>
    // directly, and the two places that need a real Color parse it with
    // ColorUtility.TryParseHtmlString. Keeping the string form means the stylesheet value
    // appears verbatim in the assembly, which is what makes it checkable from outside.
    internal static class Theme
    {
        internal const string Accent      = "#22d3ee";   // --accent-primary          cyan 400
        internal const string AccentHover = "#67e8f9";   // --accent-hover            cyan 300
        internal const string Muted       = "#94a3b8";   // --text-secondary          slate 400
        internal const string Warn        = "#fbbf24";   // --warning                 amber 400
        internal const string Button      = "#a78bfa";   // --accent-secondary        violet 400

        // The chrome: the box itself, rather than the words in it.
        //
        // THE PANEL'S BACKGROUND AND BORDER ARE NOT HERE, DELIBERATELY. They were -- as
        // PanelFill and PanelEdge, painted by a PanelSkin class that identified the panel's
        // background art by rect area. That shipped as a full-screen box with a border and no
        // text, because the largest Image under the popup is a full-screen overlay and not the
        // panel. See PanelTree for the full account. Re-adding them needs the real Image tree
        // first and a rule written against object NAMES, not sizes.
        internal const string ButtonFill = "#334155";   // --bg-tertiary    slate 700, button faces
        internal const string TextBody   = "#f1f5f9";   // --text-primary   slate 100, body text

        // NOT FROM THE STYLESHEET, AND THAT IS DELIBERATE.
        //
        // Brian asked for the menu entry's "PhValheim" to be magenta with the world name in
        // cyan. There is no magenta in :root, so this is his explicit choice rather than a
        // palette value -- which is worth writing down, because the palette drift test exists
        // to prove every OTHER colour here came from phvalheimStyles.css. A future pass that
        // reads "all colours come from the stylesheet" as a rule would delete this one.
        internal const string Magenta = "#ff00ff";
    }
}
