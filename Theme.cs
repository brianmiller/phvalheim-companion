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
        // Brian's note on the first theming pass was exact -- "the styling added to the dialog
        // is really just the blue text". The panel was still Valheim's brown parchment art,
        // which is the largest thing on screen and so decides what the dialog reads as. These
        // four are what PanelSkin paints it with.
        internal const string PanelFill  = "#0f172a";   // --bg-primary     slate 900, the box
        internal const string PanelEdge  = "#22d3ee";   // --accent-primary cyan 400,  the border
        internal const string ButtonFill = "#334155";   // --bg-tertiary    slate 700, button faces
        internal const string TextBody   = "#f1f5f9";   // --text-primary   slate 100, body text
    }
}
