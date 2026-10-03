using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PhValheimCompanion
{
    // The welcome dialog, and the small button that brings it back.
    //
    // Built on Valheim's own YesNoPopup rather than a hand-rolled uGUI panel. That buys the
    // game's panel art, fonts, gamepad focus handling and input blocking for free, none of
    // which is worth reimplementing and all of which is fiddly to get right without a real
    // client to look at. The two buttons are relabelled Connect and Close.
    //
    // Added to the FejdStartup GameObject by the SetupGui patch, so its lifetime is the main
    // menu's. Nothing here runs in-game.
    internal class ConnectDialog : MonoBehaviour
    {
        // THE PANEL IS A FIXED SIZE AND THE BODY TEXT DOES NOT CLIP -- it overflows in BOTH
        // directions. That is the single most important fact about laying this dialog out, and
        // the first version ignored it: with four mods on one line each, the body grew past the
        // panel and drew the world name ON TOP of the "PhValheim" header while the closing
        // sentence disappeared UNDER the Connect and Close buttons. Nothing errors, nothing
        // logs; it just renders wrong.
        //
        // So the layout budget is a hard constraint, not a preference. Keep the rendered body
        // at or under BodyLineBudget lines at BodyFontSize. Everything below -- the one-line
        // mod summary, the character cap, the short closing sentence -- exists to hold that
        // budget with the longest realistic content.
        //
        // Counting what the old layout cost: 1 name + 1 method + 1 blank + 1 count + 4 mods +
        // 1 blank + 2 wrapped sentence = 11 lines. The panel fits about 8.
        private const int BodyLineBudget = 8;

        // When the scrolling list is up it takes the BOTTOM 45% of the body's rect, so the
        // summary rows only have the top 55% to live in. Checking those layouts against the
        // full-panel budget of 8 would pass a body that then renders straight through the list
        // -- which is exactly what the screenshot showed.
        private const int BodyLineBudgetWithList = 5;

        // Mods are joined onto a shared line and capped by CHARACTER length, not by count.
        // A count cap cannot hold a height budget: twelve short names and twelve long ones are
        // the same count and a very different number of rendered lines, which is exactly how
        // the old MaxModsListed = 12 let a long list overflow.
        private const int ModLineBudgetChars = 86;

        private bool _shown;
        private bool _closedByPlayer;

        // Saved so they can be put back. yesText and noText live on the UnifiedPopup INSTANCE,
        // not on the popup being pushed, so overwriting them without restoring would relabel
        // every later Yes/No dialog in the session -- "Remove this character?" would offer
        // Connect and Close. Restored in both callbacks, which between them cover every way
        // this dialog can close.
        private string _savedYesText;
        private string _savedNoText;
        private bool _labelsOverridden;
        private TextAlignmentOptions _savedBodyAlignment;
        private float _savedBodyFontSize;
        private bool _savedBodyAutoSize;
        private Vector4 _savedBodyMargin;
        private bool _bodyStyleOverridden;

        // THE PANEL AND ITS CONTENTS ARE SIZED INDEPENDENTLY, AND THAT IS THE WHOLE FIX.
        //
        // PanelScale is a localScale on the popup's root, so it multiplies EVERYTHING below it:
        // the background art, the header, the body, the mod list and both buttons. Two rounds
        // were spent raising it -- 1.28, then 1.75 -- and the dialog stayed exactly as cramped
        // each time, because growing the box and its contents by the same factor buys ZERO
        // extra room for content. Measured from Brian's own screenshots: 572x442 -> 724x554 ->
        // 995x740, every pixel of growth going to a proportionally larger everything.
        //
        // So content sizes are expressed in SCREEN units -- what the player actually sees --
        // and divided by PanelScale on the way in. Raising PanelScale now makes the panel
        // bigger and leaves the text exactly where it was, which is the knob we actually
        // wanted. Lowering a *ScreenSize makes that one element smaller without touching
        // anything else. The two are finally orthogonal.
        //
        // localScale rather than resizing the RectTransform: a sizeDelta change depends on the
        // prefab's anchors, which cannot be inspected from the build host, and would stretch
        // the panel's nine-slice background in ways I could not predict. A scale is predictable
        // and reverses exactly.
        internal const float PanelScale = 2.0f;

        // The launch-help notice gets a SMALLER panel, because it has a fraction of the content.
        //
        // PanelScale 2.0 exists for the connect dialog: eight lines of labelled table plus a
        // scrolling mod list. The help notice is five lines and no list, and at 2.0 it was a
        // vast box around a short sentence -- Brian's words, and he is right.
        //
        // Everything scales with the panel, so the body budget has to scale with it too: the
        // inner height is proportional to the scale while the text stays a fixed SCREEN size.
        //
        // 1.5/2.0 of the connect dialog's 8 lines is 6, and 7 is that figure plus one line of
        // slack for the one case that needs it: a very long host:port wraps its labelled row
        // onto a second line, and withholding the address from a player who has to join by
        // hand would be the wrong thing to trim. The proportionality is an ESTIMATE -- it comes
        // from the panel's inner height scaling linearly, not from a measurement of this panel
        // at 1.5 -- so 7 is the number to revisit first if the notice ever looks tight.
        //
        // Change one of these three numbers without the others and the notice silently starts
        // overflowing again, which is the bug the render harness exists to catch.
        internal const float HelpPanelScale = 1.5f;
        private const int HelpBodyLineBudget = 7;

        // Which scale the styling methods are currently working in.
        //
        // Set before any of them runs and read instead of PanelScale throughout, so one dialog
        // can be laid out at 2.0 and the other at 1.5 without two copies of the arithmetic.
        // Font sizes are SCREEN sizes divided by this, so they come out the same physical size
        // in either panel -- that is the whole point of dividing rather than hardcoding.
        private float _activeScale = PanelScale;

        // Screen-space sizes. Divide by PanelScale, never use directly.
        //
        // Previous effective body size was 18 * 1.75 = 31.5 screen units. 22 is a ~30% cut, and
        // combined with the larger panel it is ~1.6x the content room at the same legibility
        // the rest of Valheim's UI uses. The header was never touched before and was the
        // largest thing on screen at 1.75x; the buttons were the second largest.
        private const float BodyScreenSize       = 22f;
        private const float HeaderScreenSize     = 34f;
        private const float ButtonTextScreenSize = 26f;

        // The buttons' own transforms, relative to the panel. Shrinking the label alone leaves
        // a small word in a very large frame, which is not what "make the buttons smaller"
        // means. Scaling the Button transform shrinks the frame with it; anchoredPosition is
        // untouched, so each button stays where the panel's layout puts it and only gets
        // smaller about its own pivot. Connect stays comfortably clickable at this size -- the
        // one thing that must not break.
        private const float ButtonScale = 0.62f;

        // Screen-space geometry nudges on UnifiedPopup's own rects.
        //
        // HeaderLift / BodyLift close the two bands of dead panel around the title: ~140px above
        // it and ~120px below it, measured off Brian's screenshot. The panel art is a fixed size
        // and cannot be trimmed, so the only way to spend that space is to move the things
        // inside it. The body is lifted further than the header, which closes the lower gap and
        // hands the difference to the content -- the mod list is anchored as a fraction of the
        // body rect, so it grows with it for free.
        //
        // ButtonPull brings Connect and Close toward each other. Scaling the buttons by
        // ButtonScale shrank each one about its own pivot, which WIDENED the gap between their
        // facing edges -- the spacing complaint is a direct consequence of the size fix, not a
        // separate problem.
        private const float HeaderLiftScreen = 60f;
        private const float BodyLiftScreen   = 110f;
        private const float ButtonPullScreen = 70f;

        // THESE THREE ARE POSITIONS, NOT SIZES, AND THEY SCALE DIFFERENTLY FROM FONTS.
        //
        // A font is divided by the ACTIVE scale, so the same screen size comes out of either
        // panel -- that is what makes the text legible in both. A nudge must not work that way.
        // Divided by the active scale it becomes a LARGER local offset in a SMALLER panel:
        // 60/1.5 = 40 local units against 60/2.0 = 30. That shoved the header up out of the top
        // of the 1.5 panel, which is exactly what Brian saw the moment the notice shrank.
        //
        // These are offsets inside the panel's own local space, and that space is identical at
        // every scale -- only the factor it is drawn at differs. So the local offset is a
        // constant, pinned to PanelScale, and the on-screen distance comes out proportional to
        // whatever panel it is in: 30 local is 60px at 2.0 and 45px at 1.5, the same fraction
        // of the panel either way.
        //
        // Exposed so dev_tools/renderDialog can assert the invariance rather than trust this
        // comment: a nudge that changes with the scale is the bug, and it is arithmetic, so it
        // can be checked without a game.
        internal static float HeaderLiftLocal(float activeScale) => HeaderLiftScreen / PanelScale;
        internal static float BodyLiftLocal(float activeScale)   => BodyLiftScreen / PanelScale;
        internal static float ButtonPullLocal(float activeScale) => ButtonPullScreen / PanelScale;

        // Readable at 1080p and up, and deliberately a constant rather than a percentage --
        // see ApplyPopupSkin for why percentages of Valheim's own body size were unreadable.
        // Runtime, not const: it depends on which panel is being laid out. As a const it was
        // silently the 2.0 value in both dialogs, which made the help notice's text half the
        // intended screen size in a 1.5 panel.
        private float BodyFontSize => BodyScreenSize / _activeScale;

        private Vector3 _savedPanelScale;
        private bool _panelScaled;

        // Header and buttons, saved for the same reason everything else here is: UnifiedPopup
        // is a shared singleton, so anything left behind lands on every later confirm dialog in
        // the session -- a tiny header on "Remove this character?" is our bug, not Valheim's.
        private float _savedHeaderFontSize;
        private bool _savedHeaderAutoSize;
        private bool _headerStyled;
        private float _savedLeftTextSize, _savedRightTextSize;
        private bool _savedLeftAutoSize, _savedRightAutoSize;
        private Vector3 _savedLeftScale, _savedRightScale;
        private Vector2 _savedLeftPos, _savedRightPos;
        private bool _buttonsStyled;

        // The launch-help notice has ONE action, so it is a WarningPopup -- Valheim's own
        // single-button popup, whose button the game centres itself.
        //
        // It used to offer a second button, "Get the app", opening settings.phvalheimClientURL
        // -- a single URL for a setting whose default has pointed at a Windows .exe since 2.31,
        // so on Linux or macOS it handed the player the wrong installer. Picking the right one
        // per platform is not something the server can do from one text field. Brian's call:
        // drop it.
        //
        // okText is restored like every other field touched here. This is the shared
        // UnifiedPopup singleton, so a label left behind lands on every later warning in the
        // session -- "Close" on a dialog that meant "OK".
        private string _savedOkText;
        private bool _okTextOverridden;

        // Which dialog is on screen. Read by ApplyChromeStyle to decide whether the live
        // button is buttonCenter (WarningPopup) or the left/right pair (YesNoPopup). Derived
        // from a flag rather than from _activeScale, so changing a scale cannot silently
        // change which buttons get styled.
        private bool _helpMode;

        // The centre button's own transform and label size, saved for the same reason.
        private Vector3 _savedCenterScale;
        private float _savedCenterTextSize;
        private bool _savedCenterAutoSize;
        private bool _centerStyled;

        // Rect geometry, saved separately from the text styling above: these move Valheim's own
        // layout, so a missed restore is the most visible leak of the lot.
        private Vector2 _savedHeaderPos;
        private bool _headerMoved;
        private Vector2 _savedBodyPos, _savedBodySize;
        private bool _bodyMoved;

        // Whether the scrolling mod list took. When it does, BuildBodyText leaves the names out
        // and the list shows them; when it does not, the names stay inline. The dialog has to
        // work either way, so this is read, never assumed.
        private bool _modListBuilt;

        private void Update()
        {
            // The watchdog has to run BEFORE the early return below, because the state it
            // exists to clear is exactly the state that return triggers on. Putting it after
            // meant it never ran once a join had started -- the one circumstance it is for.
            if (ConnectFlow.Connecting)
            {
                if (ConnectFlow.NoticeMainMenu(IsMainMenuActive()))
                {
                    // Offer the dialog again rather than only the reopen button. The player
                    // asked to join and it did not happen; a second Connect is the most likely
                    // next move, and a world that was mid-restart often works on the retry.
                    _shown = false;
                    _closedByPlayer = false;
                }
                return;
            }

            if (_shown || _closedByPlayer) return;

            // WHY THIS LOGS.
            //
            // The reopen button cleared the flags and the notice still did not come back, twice
            // in a row, and both of my explanations were wrong. Every gate from here on is
            // silent: Update simply returns and the next frame returns again, so "nothing
            // happened" is the only symptom and there is nothing in the log to separate the
            // five possible causes. One line per CHANGE of reason -- not per frame -- turns the
            // next report into a fact instead of a fourth theory.

            // BOTH modes, or this component is attached and then does nothing.
            //
            // This line was LaunchPayload.Present alone and it is the third gate in the chain:
            // FejdStartupPatch decides whether to attach, Show() decides which dialog to build,
            // and THIS decides whether Show() is ever called. Updating the first two and
            // missing this one is a Companion that loads, finds its manifest, builds nothing
            // and says nothing -- which is precisely the silence the feature exists to end.
            //
            // It shipped that way in the first :rc and every cheap check passed: the dll
            // contained all the new strings, the manifest was in the payload zip, eight verify
            // markers were green. None of them could see that the path was unreachable.
            // dev_tools/test-dialog-reachability.sh reads the IL of this method instead.
            if (!LaunchPayload.Present && !ClientManifest.Present)
            {
                NoteDecline("no payload and no manifest");
                return;
            }

            // UnifiedPopup is not wired up for the first few frames of the main menu, and
            // pushing into it early silently does nothing. Waiting for IsAvailable() is why
            // this is an Update loop and not a one-shot call from the patch.
            if (!IsReadyToShow())
            {
                NoteDecline(WhyNotReady());
                return;
            }

            NoteDecline(null);
            _shown = true;
            Show();
        }

        // The last reason Update gave for not showing, so the log carries one line per change
        // rather than one per frame at 60fps.
        private string _lastDecline;

        // Whether the reopen button has been reported as drawn. One line, once, because the
        // log already proves the dialog shows and closes but carries NO "button clicked" --
        // and with nothing logged at draw time there is no way to tell a button that is drawn
        // and ignoring clicks from a button that was never drawn at all. Those two need
        // completely different fixes, so the log has to separate them.
        private bool _buttonDrawReported;

        private void NoteDecline(string reason)
        {
            if (reason == _lastDecline) return;
            _lastDecline = reason;
            if (reason != null) Main.StaticLogger.LogMessage($"PhValheim dialog waiting: {reason}.");
            else Main.StaticLogger.LogMessage("PhValheim dialog: ready, showing it now.");
        }

        // Names which of IsReadyToShow's gates said no. Separate from IsReadyToShow so that
        // method keeps its single job and its one try/catch; this is only ever called on a
        // frame that already declined, so the double read costs nothing that matters.
        private string WhyNotReady()
        {
            try
            {
                if (FejdStartup.instance == null) return "the main menu object is gone";
                if (!UnifiedPopup.IsAvailable()) return "UnifiedPopup is not available yet";
                if (UnifiedPopup.IsVisible()) return "another popup is already on screen";
                if (!IsMainMenuActive()) return "the main menu is not the active screen";
                return "a gate in IsReadyToShow refused without saying which";
            }
            catch (Exception e)
            {
                return $"reading the menu state threw {e.GetType().Name}";
            }
        }

        // Three callers want this and each wants something different when the read itself
        // fails, so the failure is reported rather than folded into the answer. The watchdog
        // in particular must not read a thrown exception as "the join came back" -- that would
        // cancel a perfectly good join on any frame the reflection hiccuped.
        private static bool TryIsMainMenuActive(out bool active)
        {
            active = false;
            try
            {
                if (FejdStartup.instance == null) return true;   // read fine, menu just is not up
                var mainMenu = Utils.GetPrivateField<GameObject>(FejdStartup.instance, "m_mainMenu");
                active = mainMenu != null && mainMenu.activeInHierarchy;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMainMenuActive()
        {
            return TryIsMainMenuActive(out bool active) && active;
        }

        private bool IsReadyToShow()
        {
            try
            {
                if (FejdStartup.instance == null) return false;
                if (!UnifiedPopup.IsAvailable()) return false;
                if (UnifiedPopup.IsVisible()) return false;

                // Only on the main menu. If the player has wandered into the server list or a
                // character screen, a popup appearing unprompted would be an interruption --
                // and ProceedJoinRequest behaves differently with the server list open.
                if (!TryIsMainMenuActive(out bool active))
                {
                    Main.StaticLogger.LogWarning("Could not tell whether the main menu is ready; the connect dialog will not be shown automatically.");
                    _closedByPlayer = true;   // stops this retrying every frame forever
                    return false;
                }

                return active;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not tell whether the main menu is ready ({e.GetType().Name}); the connect dialog will not be shown automatically.");
                _closedByPlayer = true;   // stops this retrying every frame forever
                return false;
            }
        }

        internal void Show()
        {
            var payload = LaunchPayload.Current;

            // No payload, but a manifest: the launch-help notice instead of the connect
            // dialog. Two different questions, so two different dialogs -- but the same
            // UnifiedPopup plumbing, because getting that panel to lay out correctly took two
            // rounds of measuring screenshots and must not be reimplemented beside itself.
            if (payload == null)
            {
                ShowLaunchHelp();
                return;
            }

            try
            {
                ApplyPopupSkin(yesLabel: "Close", noLabel: "Connect");

                // Connect LEFT, Close RIGHT -- Brian's call, and it reads better: the action
                // you came for sits where you look first.
                //
                // Valheim decides the SIDES, not us. ShowYesNo wires yesText/yesCallback to
                // the RIGHT button and noText/noCallback to the LEFT. So putting Connect on
                // the left means Connect goes in the "no" slot. That reads backwards here and
                // is the only place it can be expressed -- hence this comment rather than a
                // tidier-looking call that would quietly put the buttons back.
                // The mod list is built FIRST, because whether it succeeds decides what the body
                // text says: with a scrolling list the names belong in the list, without one
                // they have to stay inline or the player cannot see them at all.
                List<string> mods = LaunchPayload.InstalledPlugins();
                _modListBuilt = TryBuildModList(mods);

                // Push FIRST, then style. See ApplyBodyStyle: styling an inactive popup is what
                // left the body vertically centred through two rounds of testing.
                UnifiedPopup.Push(new YesNoPopup(
                    "PhValheim",
                    BuildBodyText(payload, mods, _modListBuilt),
                    OnClose,     // yes slot -> right button -> "Close"
                    OnConnect,   // no  slot -> left  button -> "Connect"
                    // localizeText: false. The body carries a world name and mod names, which
                    // are operator data, not translation keys -- running them through
                    // Localization.Localize risks a name that starts with '$' being swallowed.
                    false,
                    true));

                // Chrome FIRST, then the body style. ApplyChromeStyle resizes the body rect, and
                // ApplyBodyStyle reserves the mod list's strip as a margin measured from that
                // rect's height -- run in the other order it measures the old height and the
                // reserved strip no longer matches where the list actually is.
                ApplyChromeStyle();
                ApplyBodyStyle();
            }
            catch (Exception e)
            {
                // The dialog is the convenience, not the product. If it cannot be shown the
                // player still has a working game and a server list.
                Main.StaticLogger.LogError($"Could not show the connect dialog: {e}");
                Main.StaticLogger.LogError($"Join \"{payload.World}\" from the server list at {payload.Host}:{payload.Port}.");
                ModListView.Destroy();
                RestorePopupSkin();
                _closedByPlayer = true;
            }
        }

        // No measurement. The list anchors to the bottom slice of the body's rect, so where it
        // lands does not depend on how tall the body text turned out -- which is what the first
        // version got wrong. It measured the body BEFORE UnifiedPopup.Push, when the panel is
        // not laid out, got a short answer, and drew the list over the closing sentence and
        // past the bottom of the panel.
        //
        // The contract is the other way round now: the list owns the bottom of the rect and the
        // body must fit in the top. BodyLineBudgetWithList is that promise, and the render
        // harness enforces it.
        private bool TryBuildModList(List<string> mods)
        {
            try
            {
                var popup = PopupInstance();
                if (popup == null) return false;
                if (!Utils.TryGetFieldValue(popup, "bodyText", out var bodyObj)) return false;
                if (!(bodyObj is TMP_Text body)) return false;

                return ModListView.Build(body, mods);
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not prepare the scrolling mod list ({e.GetType().Name}); the mods will be listed inline.");
                return false;
            }
        }

        // The notice for "a PhValheim world is installed here, but nothing asked me to join
        // it". No mod list and no Connect button, because neither would be honest: the mods
        // belong to a world the player has not asked to enter, and connecting needs a password
        // the manifest deliberately does not carry.
        private void ShowLaunchHelp()
        {
            var manifest = ClientManifest.Current;
            if (manifest == null) return;

            try
            {
                // WarningPopup, not YesNoPopup. Valheim's OWN single-button popup.
                //
                // UnifiedPopup.ShowWarning sets buttonCenterText from UnifiedPopup.okText,
                // activates buttonCenter and wires its onClick -- verified in its IL, not
                // assumed. So the one button is centred by the game's own layout.
                //
                // The first attempt pushed a YesNoPopup and hid the unused left button, which
                // left the right one sitting where the right of a PAIR goes: off centre, and
                // pulled further off by ButtonPull. Centring it by hand would have meant
                // another screen-space nudge to maintain, on the shared singleton, when the
                // game already has the layout we want.
                _helpMode = true;
                _activeScale = HelpPanelScale;
                ApplyOkLabel("Close");

                UnifiedPopup.Push(new WarningPopup(
                    "PhValheim",
                    BuildLaunchHelpBody(manifest),
                    OnClose,
                    false));

                ApplyChromeStyle();
                ApplyBodyStyle();
            }
            catch (Exception e)
            {
                // The STACK, not just the type and message. This catch is the one remaining
                // explanation for "the button does nothing": it logs, sets _closedByPlayer and
                // so puts the button straight back, which from outside is indistinguishable
                // from a click that never landed. Type+message alone does not say which of the
                // ~dozen reflected reads in the styling chain failed; the stack does.
                Main.StaticLogger.LogError($"Could not show the launch-help notice: {e}");
                Main.StaticLogger.LogError($"To join \"{manifest.World}\", start it from the PhValheim app.");
                RestorePopupSkin();
                _closedByPlayer = true;
            }
        }

        // The centre button's label, and the panel scale, both live on the shared singleton.
        //
        // okText is the field UnifiedPopup.ShowWarning localizes onto buttonCenterText, so it
        // is the only way to label that button -- and like yesText/noText it must be put back,
        // or every later warning in the session says "Close" where it meant "OK".
        private void ApplyOkLabel(string label)
        {
            var popup = PopupInstance();
            if (popup == null)
            {
                Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup.instance not reachable -- the button keeps Valheim's own label.");
            }
            else if (Utils.TryGetFieldValue(popup, "okText", out var okObj))
            {
                _savedOkText = okObj as string;
                Utils.SetPrivateField(popup, "okText", label);
                _okTextOverridden = true;
            }

            ScalePanel();
        }

        private void OnConnect()
        {
            Pop();
            ModListView.Destroy();
            RestorePopupSkin();
            ConnectFlow.Begin(LaunchPayload.Current);
        }

        private void OnClose()
        {
            Pop();
            ModListView.Destroy();
            RestorePopupSkin();

            // Brian's call: Close drops to the normal game menu rather than quitting or
            // blocking, with a way back. Nothing about the launch is lost -- the payload is
            // still on the command line, so Show() can be called again unchanged.
            _closedByPlayer = true;
            Main.StaticLogger.LogMessage("Connect dialog closed. Use the PhValheim button on the main menu to bring it back.");
        }

        // YesNoPopup's click handlers call the callback and nothing else -- they do not close
        // the popup. Verified in UnifiedPopup.ShowYesNo's IL; without this the panel stays up
        // behind whatever happens next.
        private void Pop()
        {
            try
            {
                UnifiedPopup.Pop();
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not close the connect dialog cleanly ({e.GetType().Name}).");
            }
        }

        // UnifiedPopup.instance, yesText and noText are all PRIVATE in the assembly the game
        // loads -- confirmed by dev_tools/test-publicizer-trap.sh against a real, un-publicized
        // assembly_valheim.dll. Writing `UnifiedPopup.instance.yesText = "Connect"` builds
        // with zero warnings against the publicized reference and throws MissingFieldException
        // the instant the dialog is shown. Reflection is not defensive style here, it is the
        // only version that runs.
        private static object PopupInstance()
        {
            return Utils.GetStaticFieldValue(typeof(UnifiedPopup), "instance");
        }

        // Everything we change on the shared UnifiedPopup singleton, applied and put back as
        // one unit.
        //
        // All of it is singleton state, so forgetting any part of the restore leaks our
        // styling into every later Yes/No dialog in the session -- "Remove this character?"
        // offering Connect and Close, left-aligned. Keeping apply and restore as a matched
        // pair over one list is why there is no separate alignment-restore to forget.
        private void ApplyPopupSkin(string yesLabel, string noLabel)
        {
            var popup = PopupInstance();
            if (popup == null) return;

            // The originals are only recorded if BOTH reads succeed. Restoring a half-read
            // pair would write null into one of Valheim's labels and leave a blank button on
            // every later confirm dialog -- worse than not relabelling at all.
            if (!Utils.TryGetFieldValue(popup, "yesText", out var oldYes)) return;
            if (!Utils.TryGetFieldValue(popup, "noText", out var oldNo)) return;

            _savedYesText = oldYes as string;
            _savedNoText = oldNo as string;

            // Localize() passes a string with no $ token through unchanged, so plain English
            // is safe to put here.
            if (!Utils.SetPrivateField(popup, "yesText", yesLabel)) return;
            if (!Utils.SetPrivateField(popup, "noText", noLabel))
            {
                Utils.SetPrivateField(popup, "yesText", _savedYesText);
                return;
            }

            _labelsOverridden = true;

            // The panel itself, 28% bigger. Scaling popupUIParent takes the background art, the
            // header, the body and both buttons together, so nothing inside needs moving.
            //
            // Saved and restored with everything else here for the same reason the labels are:
            // this is the shared UnifiedPopup singleton, so a scale left behind would make
            // every later confirm dialog in the session oversized.
            ScalePanel();

            // Valheim's own popups are one or two centred sentences. Ours is a labelled table,
            // which wants top-left. TMP_Text.alignment is TextMeshPro's own public API; only
            // the UnifiedPopup FIELD holding the component is private, hence the reflected read.
            //
        }

        // The panel's localScale, at whatever _activeScale currently is.
        //
        // Shared by both dialogs so there is one place that scales the panel and one place that
        // puts it back. The connect dialog runs at PanelScale, the help notice at the smaller
        // HelpPanelScale, and the only difference between them is the field read here.
        private void ScalePanel()
        {
            var popup = PopupInstance();
            if (popup != null
                && Utils.TryGetFieldValue(popup, "popupUIParent", out var parentObj)
                && parentObj is GameObject panel && panel.transform != null)
            {
                _savedPanelScale = panel.transform.localScale;
                panel.transform.localScale = _savedPanelScale * _activeScale;
                _panelScaled = true;
            }
            else
            {
                Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup.popupUIParent not found -- the panel stays its original size.");
            }
        }

        // THE BODY STYLE IS APPLIED AFTER UnifiedPopup.Push, AND THAT IS THE WHOLE FIX.
        //
        // It used to run inside ApplyPopupSkin, before the push -- at which point the popup's
        // GameObject is still INACTIVE. The alignment write did not survive the object being
        // enabled, so the body kept Valheim's vertical centring. Horizontal looked right only
        // because BuildBodyText carries an <align=left> tag, which is why this was so easy to
        // misread as "alignment works".
        //
        // The arithmetic from Brian's screenshot: the body rect spanned y=160..394 and the
        // text was centred at 277. Top-aligned it would have started at 160; it started at
        // 225, which is exactly centre-minus-half-the-text. The mod list was correctly in the
        // bottom 38% of that rect the whole time -- the TEXT was in the wrong place, hanging
        // down into it. Two cycles of moving the list were two cycles of moving the wrong
        // thing.
        //
        // Labels still have to be set BEFORE the push, because ShowYesNo reads yesText/noText
        // while building the buttons. Only the body style moves.
        private void ApplyBodyStyle()
        {
            var popup = PopupInstance();
            if (popup == null) return;

            if (!Utils.TryGetFieldValue(popup, "bodyText", out var bodyObj))
            {
                Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup.bodyText not found -- the body keeps Valheim's centred style.");
                return;
            }

            if (!(bodyObj is TMP_Text body))
            {
                Main.StaticLogger.LogWarning($"Dialog layout: UnifiedPopup.bodyText is {(bodyObj == null ? "null" : bodyObj.GetType().FullName)}, not the TMP_Text this mod compiled against -- body style skipped.");
                return;
            }

            _savedBodyAlignment = body.alignment;
            _savedBodyFontSize = body.fontSize;
            _savedBodyAutoSize = body.enableAutoSizing;
            _savedBodyMargin = body.margin;
            _bodyStyleOverridden = true;

            // Both axes, explicitly. `alignment` is the combined enum; verticalAlignment is the
            // single axis that actually went wrong, and setting it on its own leaves no doubt
            // about which property was meant.
            body.alignment = TextAlignmentOptions.TopLeft;
            body.verticalAlignment = VerticalAlignmentOptions.Top;

            // enableAutoSizing off: with it on, TMP shrinks text to fit the box, so a long body
            // silently scales itself back to unreadable and no size set here would stick.
            body.enableAutoSizing = false;
            body.fontSize = BodyFontSize;

            // Belt and braces for the overlap, independent of whether the alignment above took.
            // Reserving the bottom of the text box by MARGIN means the text cannot be laid out
            // in the strip the mod list occupies even if it is still being centred -- a margin
            // is geometry, not alignment, so the two cannot fail the same way.
            //
            // Measured here rather than guessed because this runs after the push, when the rect
            // is real. That timing is the only reason this number can be trusted.
            float h = body.rectTransform != null ? body.rectTransform.rect.height : 0f;
            if (h > 1f && ModListView.IsBuilt)
            {
                var m = body.margin;
                body.margin = new Vector4(m.x, m.y, m.z, h * ModListView.ListTopFraction);
            }

            // Read the values BACK rather than reporting what we asked for. A property that
            // silently refuses a write looks identical to a successful one in any log that only
            // echoes the input. This is the difference between "we set Top" and "it is Top".
            Main.StaticLogger.LogMessage($"Dialog layout: body is vAlign={body.verticalAlignment}, align={body.alignment}, fontSize={body.fontSize}, autoSize={body.enableAutoSizing}, rectH={h:0}, margin={body.margin} (wanted Top/TopLeft/{BodyFontSize}/False).");
        }

        // The header and the two buttons. AFTER the push, for exactly the reason ApplyBodyStyle
        // is: before the push the popup's GameObject is inactive, and the write that did not
        // survive activation last time was a TMP property on a child of this same object.
        //
        // enableAutoSizing has to go off on each one first. With it on TMP treats fontSize as a
        // starting hint and rescales to fill the box, so every size set here would be quietly
        // overwritten and the log would still show the value we asked for -- the same way the
        // body's did.
        private void ApplyChromeStyle()
        {
            var popup = PopupInstance();
            if (popup == null) return;

            if (Utils.TryGetFieldValue(popup, "headerText", out var headerObj) && headerObj is TMP_Text header)
            {
                _savedHeaderFontSize = header.fontSize;
                _savedHeaderAutoSize = header.enableAutoSizing;
                _headerStyled = true;
                header.enableAutoSizing = false;
                header.fontSize = HeaderScreenSize / _activeScale;

                // Lift the title toward the top edge. +y is up in Unity UI regardless of how the
                // rect is anchored, so this does not depend on knowing the prefab's anchors.
                if (header.rectTransform != null)
                {
                    _savedHeaderPos = header.rectTransform.anchoredPosition;
                    _headerMoved = true;
                    header.rectTransform.anchoredPosition =
                        _savedHeaderPos + new Vector2(0f, HeaderLiftLocal(_activeScale));
                }
            }
            else
            {
                Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup.headerText not found -- the title stays panel-sized.");
            }

            // The help notice is a WarningPopup, so the live button is buttonCenter and the
            // left/right pair is inactive. Styling the pair there would set sizes on two hidden
            // objects and leave the one the player can see at Valheim's own size -- the
            // mismatch would read as a rendering fault, which is exactly what the "both buttons
            // or neither" rule below exists to avoid.
            //
            // No position nudge: ShowWarning's single button is already centred by the game.
            // ButtonPull exists only to close the gap a PAIR gets after being scaled down.
            if (_helpMode)
            {
                if (Utils.TryGetFieldValue(popup, "buttonCenterText", out var cObj) && cObj is TMP_Text centerText
                    && Utils.TryGetFieldValue(popup, "buttonCenter", out var cBtn) && cBtn is Button buttonCenter
                    && buttonCenter.transform != null)
                {
                    _savedCenterTextSize = centerText.fontSize;
                    _savedCenterAutoSize = centerText.enableAutoSizing;
                    _savedCenterScale = buttonCenter.transform.localScale;
                    _centerStyled = true;

                    centerText.enableAutoSizing = false;
                    centerText.fontSize = ButtonTextScreenSize / _activeScale;
                    buttonCenter.transform.localScale = _savedCenterScale * ButtonScale;

                    Main.StaticLogger.LogMessage($"Dialog layout: centre button is size={centerText.fontSize} scale={buttonCenter.transform.localScale.x:0.00} (wanted {ButtonTextScreenSize / _activeScale}/{ButtonScale:0.00}).");
                }
                else
                {
                    Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup.buttonCenter not found -- Close stays panel-sized.");
                }
            }
            // Both buttons or neither. A half-applied pair would leave Connect and Close
            // visibly different sizes, which reads as a rendering fault rather than a style.
            else if (Utils.TryGetFieldValue(popup, "buttonLeftText", out var lObj) && lObj is TMP_Text leftText
                && Utils.TryGetFieldValue(popup, "buttonRightText", out var rObj) && rObj is TMP_Text rightText
                && Utils.TryGetFieldValue(popup, "buttonLeft", out var lBtn) && lBtn is Button buttonLeft
                && Utils.TryGetFieldValue(popup, "buttonRight", out var rBtn) && rBtn is Button buttonRight
                && buttonLeft.transform != null && buttonRight.transform != null)
            {
                _savedLeftTextSize = leftText.fontSize;
                _savedRightTextSize = rightText.fontSize;
                _savedLeftAutoSize = leftText.enableAutoSizing;
                _savedRightAutoSize = rightText.enableAutoSizing;
                _savedLeftScale = buttonLeft.transform.localScale;
                _savedRightScale = buttonRight.transform.localScale;
                _buttonsStyled = true;

                leftText.enableAutoSizing = false;
                rightText.enableAutoSizing = false;
                leftText.fontSize = ButtonTextScreenSize / _activeScale;
                rightText.fontSize = ButtonTextScreenSize / _activeScale;
                buttonLeft.transform.localScale = _savedLeftScale * ButtonScale;
                buttonRight.transform.localScale = _savedRightScale * ButtonScale;

                // Pull the pair together. buttonLeft is the on-screen LEFT button (Connect) and
                // buttonRight the right one (Close) -- ShowYesNo maps noText to buttonLeft and
                // yesText to buttonRight, and this dialog passes Connect as the "no" label. The
                // trap test pins that mapping, so if Valheim ever swaps the slots this moves the
                // two buttons apart instead of together and the test says so first.
                var lRect = buttonLeft.transform as RectTransform;
                var rRect = buttonRight.transform as RectTransform;
                if (lRect != null && rRect != null)
                {
                    _savedLeftPos = lRect.anchoredPosition;
                    _savedRightPos = rRect.anchoredPosition;
                    float pull = ButtonPullLocal(_activeScale);
                    lRect.anchoredPosition = _savedLeftPos + new Vector2(pull, 0f);
                    rRect.anchoredPosition = _savedRightPos + new Vector2(-pull, 0f);
                }

                // Read back, not echo. See the body's log line for why.
                Main.StaticLogger.LogMessage($"Dialog layout: buttons are size={rightText.fontSize} scale={buttonRight.transform.localScale.x:0.00} (wanted {ButtonTextScreenSize / _activeScale}/{ButtonScale:0.00}).");
            }
            else
            {
                Main.StaticLogger.LogWarning("Dialog layout: UnifiedPopup button fields not found -- Connect and Close stay panel-sized.");
            }

            // Raise the body rect's TOP edge into the space the header just vacated, leaving its
            // bottom where it is. Grow by the lift, then move up by half of it: the rect expands
            // evenly about its pivot, so the two together move the top by exactly the lift and
            // the bottom by nothing.
            //
            // The mod list is anchored as a FRACTION of this rect, so it gains its share of the
            // new height with no second adjustment -- the reason ModListView was built on
            // normalized anchors in the first place.
            if (Utils.TryGetFieldValue(popup, "bodyText", out var bObj) && bObj is TMP_Text bodyForRect
                && bodyForRect.rectTransform != null)
            {
                var br = bodyForRect.rectTransform;
                _savedBodyPos = br.anchoredPosition;
                _savedBodySize = br.sizeDelta;
                _bodyMoved = true;

                float lift = BodyLiftLocal(_activeScale);
                br.sizeDelta = _savedBodySize + new Vector2(0f, lift);
                br.anchoredPosition = _savedBodyPos + new Vector2(0f, lift * 0.5f);

                Main.StaticLogger.LogMessage(
                    $"Dialog layout: body rect {_savedBodySize.y:0}->{br.sizeDelta.y:0} high, "
                    + $"lifted {lift:0}; rect is now {br.rect.height:0} tall.");
            }
        }

        private void RestorePopupSkin()
        {
            var popup = PopupInstance();

            // The centre button's label, put back before anything else. Left overridden it
            // reads "Close" on every later warning dialog in the session, vanilla's included.
            if (_okTextOverridden)
            {
                _okTextOverridden = false;
                if (popup != null) Utils.SetPrivateField(popup, "okText", _savedOkText);
            }

            if (_centerStyled)
            {
                _centerStyled = false;
                if (popup != null)
                {
                    if (Utils.TryGetFieldValue(popup, "buttonCenter", out var cBtn)
                        && cBtn is Button buttonCenter && buttonCenter.transform != null)
                    {
                        buttonCenter.transform.localScale = _savedCenterScale;
                    }
                    if (Utils.TryGetFieldValue(popup, "buttonCenterText", out var ctObj)
                        && ctObj is TMP_Text centerText)
                    {
                        centerText.fontSize = _savedCenterTextSize;
                        centerText.enableAutoSizing = _savedCenterAutoSize;
                    }
                }
            }

            _helpMode = false;

            // Back to the connect dialog's scale for whatever is shown next. Left at the help
            // notice's 1.5, a later Connect dialog would be laid out in a panel a quarter
            // smaller than its body budget assumes -- and that budget is a hard constraint.
            _activeScale = PanelScale;

            if (_labelsOverridden)
            {
                _labelsOverridden = false;
                if (popup != null)
                {
                    Utils.SetPrivateField(popup, "yesText", _savedYesText);
                    Utils.SetPrivateField(popup, "noText", _savedNoText);
                }
            }

            if (_panelScaled)
            {
                _panelScaled = false;
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "popupUIParent", out var parentObj)
                    && parentObj is GameObject panel && panel.transform != null)
                {
                    panel.transform.localScale = _savedPanelScale;
                }
            }

            if (_bodyStyleOverridden)
            {
                _bodyStyleOverridden = false;
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "bodyText", out var bodyObj)
                    && bodyObj is TMP_Text body)
                {
                    body.alignment = _savedBodyAlignment;
                    body.fontSize = _savedBodyFontSize;
                    body.enableAutoSizing = _savedBodyAutoSize;
                    body.margin = _savedBodyMargin;
                }
            }

            if (_headerStyled)
            {
                _headerStyled = false;
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "headerText", out var headerObj)
                    && headerObj is TMP_Text header)
                {
                    header.fontSize = _savedHeaderFontSize;
                    header.enableAutoSizing = _savedHeaderAutoSize;
                }
            }

            if (_buttonsStyled)
            {
                _buttonsStyled = false;
                if (popup != null)
                {
                    if (Utils.TryGetFieldValue(popup, "buttonLeftText", out var lObj) && lObj is TMP_Text leftText)
                    {
                        leftText.fontSize = _savedLeftTextSize;
                        leftText.enableAutoSizing = _savedLeftAutoSize;
                    }
                    if (Utils.TryGetFieldValue(popup, "buttonRightText", out var rObj) && rObj is TMP_Text rightText)
                    {
                        rightText.fontSize = _savedRightTextSize;
                        rightText.enableAutoSizing = _savedRightAutoSize;
                    }
                    // The scales are restored independently of the text: a button left at 0.62
                    // on every later dialog is the most visible way this could leak.
                    if (Utils.TryGetFieldValue(popup, "buttonLeft", out var lBtn) && lBtn is Button buttonLeft
                        && buttonLeft.transform != null)
                    {
                        buttonLeft.transform.localScale = _savedLeftScale;
                        var lr = buttonLeft.transform as RectTransform;
                        if (lr != null) lr.anchoredPosition = _savedLeftPos;
                    }
                    if (Utils.TryGetFieldValue(popup, "buttonRight", out var rBtn) && rBtn is Button buttonRight
                        && buttonRight.transform != null)
                    {
                        buttonRight.transform.localScale = _savedRightScale;
                        var rr = buttonRight.transform as RectTransform;
                        if (rr != null) rr.anchoredPosition = _savedRightPos;
                    }
                }
            }

            if (_headerMoved)
            {
                _headerMoved = false;
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "headerText", out var hObj)
                    && hObj is TMP_Text header && header.rectTransform != null)
                {
                    header.rectTransform.anchoredPosition = _savedHeaderPos;
                }
            }

            if (_bodyMoved)
            {
                _bodyMoved = false;
                if (popup != null
                    && Utils.TryGetFieldValue(popup, "bodyText", out var bObj)
                    && bObj is TMP_Text body && body.rectTransform != null)
                {
                    body.rectTransform.sizeDelta = _savedBodySize;
                    body.rectTransform.anchoredPosition = _savedBodyPos;
                }
            }
        }

        // Colours picked to sit with Valheim's parchment-on-dark popup rather than fight it:
        // a warm highlight for the world name, muted grey-blue for secondary lines, amber for
        // the one case the player needs to act on.
        private const string ColHighlight = "#E8D9A0";   // world name, join code
        private const string ColMuted     = "#9AA3B8";   // secondary text
        private const string ColWarn      = "#FFC083";   // no mods found

        // Where the value column starts, as a percentage of the body width. TMP's <pos> is what
        // makes this a table rather than a paragraph: every value lands on the same x no matter
        // how long its label is, which is the alignment Brian asked for. Spaces cannot do this
        // -- Valheim's body font is proportional, so padded labels drift by a few pixels per
        // row and look worse than no alignment at all.
        private const string ValueColumn = "<pos=26%>";

        // The mod list is a PARAMETER so this function is pure and can be rendered outside the
        // game. Reading BepInEx.Paths in here instead made the layout untestable: the only way
        // to see what a four-mod world looks like was to ship it and photograph the screen,
        // which is how a body that overflowed the panel in both directions got released.
        // dev_tools/render-dialog.sh calls THIS method on the built DLL.
        // listIsSeparate: the scrolling list is showing the names, so the body gives only the
        // count. Passed in rather than read from a field because this method is also called by
        // dev_tools/render-dialog, outside any instance.
        // The launch-help body: "this world is installed, but nothing asked me to join it".
        //
        // EVERY SENTENCE HERE HAS TO BE TRUE OF BOTH CAUSES.
        // The Companion cannot tell an outdated PhValheim app from a plain Steam launch of an
        // install PhValheim set up -- see ClientManifest. So this does not open with "your app
        // is out of date": for the player who launched from Steam on a current app that is
        // simply false, and a dialog that tells a player something false about their own
        // machine is how a helpful notice becomes a bug report. It describes the situation,
        // names both causes, and gives the one action that fixes either.
        //
        // Static and manifest-only so dev_tools/render-dialog can draw it without a game.
        internal static string BuildLaunchHelpBody(ClientManifest manifest)
        {
            var sb = new StringBuilder();

            sb.Append("<align=left>");

            sb.Append("<size=115%><b><color=").Append(ColHighlight).Append('>')
              .Append(Escape(manifest.World)).Append("</color></b></size>\n");

            // EVERY LINE COSTS, AND THIS BODY'S BUDGET IS SIX, NOT EIGHT.
            //
            // The notice is laid out at HelpPanelScale, a smaller panel than the connect
            // dialog's, so it holds proportionally fewer lines at the same text size. A blank
            // line costs one of the six and a sentence past the wrap width costs two. The first
            // draft explained both causes in full prose and came to FIFTEEN;
            // dev_tools/renderDialog caught it, which is the only reason it is not a screenshot
            // of text running off the bottom of the panel.
            sb.Append("Nothing handed Valheim a world to join.\n");

            // States the requirement without asserting the player has failed it. The Companion
            // cannot know which of the two causes applies (see ClientManifest), so "version X
            // or newer" is the one phrasing that is true for both the player on an old app and
            // the player who launched from Steam on a current one.
            if (!string.IsNullOrEmpty(manifest.MinClientVersion))
            {
                sb.Append("Start it from the PhValheim app, version <b>")
                  .Append(Escape(manifest.MinClientVersion)).Append("</b> or newer.\n");
            }
            else
            {
                sb.Append("Start it from the PhValheim app.\n");
            }

            // The address, for a player who wants to join by hand from Valheim's own menu.
            // Withheld for a crossplay world, which has no address -- see
            // ClientManifest.HasAddress.
            if (manifest.HasAddress)
            {
                Row(sb, "Address", "<color=" + ColHighlight + ">" + Escape(manifest.Host)
                    + ':' + Escape(manifest.Port) + "</color>");
            }
            else if (manifest.IsCrossplay)
            {
                Row(sb, "Joining", "by crossplay code -- on the world's page");
            }

            // Where the password is, never what it is. The manifest carries no password by
            // design; see writeClientManifest() in the engine for why.
            Row(sb, "Password", "on the world's page");

            return sb.ToString();
        }

        internal static string BuildBodyText(LaunchPayload payload, List<string> mods, bool listIsSeparate = false)
        {
            var sb = new StringBuilder();

            // <align=left> rather than relying on bodyText.alignment.
            //
            // ApplyPopupSkin reflects the TMP component and sets TopLeft, and that SHOULD be
            // enough -- UnifiedPopup.ShowYesNo only assigns .text and never touches alignment.
            // It demonstrably was not enough in the build Brian photographed: every line came
            // out centre-aligned, bullets ragged. Rather than keep guessing at which link in
            // that reflection chain failed, the alignment is now carried IN THE TEXT, where it
            // cannot be lost. The component-level set stays as well, because it is what handles
            // the VERTICAL axis, which no rich-text tag can reach.
            //
            // A tag that the reflection also happens to fix is harmless. A layout that depends
            // on reflection working is not.
            sb.Append("<align=left>");

            // Why the last attempt failed, if there was one. First thing in the body, because
            // the player is looking at a dialog that just reappeared and the only question in
            // their head is "what happened?". Before this, nothing answered it: every failure
            // path wrote to the BepInEx log and the dialog came back as if nothing had gone
            // wrong, which is indistinguishable from a misclick.
            if (!string.IsNullOrEmpty(ConnectFlow.LastFailure))
            {
                sb.Append("<color=").Append(ColWarn).Append(">⚠ ")
                  .Append(Escape(ConnectFlow.LastFailure)).Append("</color>\n");
                if (!listIsSeparate) sb.Append('\n');
            }

            // The world name, full width. Deliberately NOT a table row: names run long
            // ("ModdedCrossplayPublished" is a real one), and a long value in a narrow column
            // wraps and silently costs a line of the height budget.
            sb.Append("<size=115%><b><color=").Append(ColHighlight).Append('>')
              .Append(Escape(payload.World)).Append("</color></b></size>\n");

            // ONE source of truth: LaunchPayload.Method. The dialog does not re-derive this,
            // which is what once let it announce an address join on a crossplay world.
            switch (payload.Method)
            {
                case LaunchPayload.JoinMethod.JoinCode:
                    Row(sb, "Joining", "by code <b><color=" + ColHighlight + ">"
                        + Escape(payload.JoinCode) + "</color></b>");
                    break;

                default:
                    if (payload.CrossplayCodeNotReady)
                    {
                        // Honest about the fallback rather than describing this as a plain
                        // address world. The code usually appears within ~30s of the world
                        // starting, so the second line is real advice, not filler.
                        Row(sb, "Joining", "<color=" + ColHighlight + ">" + Escape(payload.Host)
                            + ':' + Escape(payload.Port) + "</color>");
                        Row(sb, "", "<color=" + ColWarn + ">crossplay code not published yet</color>");
                    }
                    else
                    {
                        Row(sb, "Joining", "<color=" + ColHighlight + ">" + Escape(payload.Host)
                            + ':' + Escape(payload.Port) + "</color>");
                    }
                    break;
            }

            AppendModsRow(sb, payload, mods, listIsSeparate);

            // One line, and short enough not to wrap at this width. The old two-sentence
            // version wrapped to two lines and was the part that ended up under the buttons.
            // The closing sentence is dropped entirely when the list is up. The body only owns
            // the top ~62% of the rect in that mode, and this line is precisely what the
            // overlapping screenshot showed the mod list being drawn straight through. The
            // Connect button directly below says the same thing well enough.
            if (!listIsSeparate)
            {
                sb.Append('\n');
                sb.Append("<size=92%><color=").Append(ColMuted)
                  .Append(">Connect takes you to character selection.</color></size>");
            }

            // Trim the trailing newline. In list mode the body ends on the Mods row, whose
            // Row() call appends one, and that dangling blank line is a REAL rendered line in a
            // space that only has five -- it cost exactly the line that tipped the
            // failure-notice layout over. Harmless in inline mode, where the closing sentence
            // already ends the string.
            return sb.ToString().TrimEnd('\n');
        }

        // One table row: muted label, then the value on the column stop.
        private static void Row(StringBuilder sb, string label, string value)
        {
            sb.Append("<color=").Append(ColMuted).Append('>').Append(label).Append("</color>")
              .Append(ValueColumn).Append(value).Append('\n');
        }

        private static void AppendModsRow(StringBuilder sb, LaunchPayload payload, List<string> mods, bool listIsSeparate)
        {
            if (mods == null) mods = new List<string>();

            if (mods.Count == 0)
            {
                if (payload.IsVanilla)
                {
                    Row(sb, "Mods", "<color=" + ColMuted + ">none — vanilla world</color>");
                    return;
                }

                // Worth saying rather than leaving blank: on a modded world this means the
                // client's sync did not land, and the player is about to be rejected for a
                // version mismatch with no idea why.
                Row(sb, "Mods", "<color=" + ColWarn + ">none found — re-run the PhValheim client</color>");
                return;
            }

            // The label already says "Mods", so the value needs no singular/plural of its own.
            Row(sb, "Mods", "<b>" + mods.Count + "</b> installed");

            // The scrolling list is showing the names, so the body stops at the count and
            // leaves it the room. Without the list, the names go inline here instead -- the
            // player must be able to see what is installed either way.
            if (listIsSeparate) return;

            // ...unless a failure notice is up. The notice costs two lines at the top, and on a
            // busy modded world that pushed the inline fallback to 10 rendered lines against a
            // budget of 8 -- found by dev_tools/test-dialog-layout.sh, not by shipping it. When
            // the player is being told why their join failed, that message outranks the mod
            // names; the count still tells them their mods are installed.
            if (!string.IsNullOrEmpty(ConnectFlow.LastFailure)) return;

            // The names on one wrapped line under the count, capped by width. Joining them is
            // what turned a four-line list into one and bought back the height the panel did
            // not have.
            var names = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                string name = Escape(mods[i]);
                if (names.Length > 0 && names.Length + name.Length + 2 > ModLineBudgetChars) break;
                if (names.Length > 0) names.Append(", ");
                names.Append(name);
                shown++;
            }

            if (shown < mods.Count) names.Append(", +").Append(mods.Count - shown).Append(" more");

            sb.Append(ValueColumn).Append("<size=92%><color=").Append(ColMuted).Append('>')
              .Append(names).Append("</color></size>\n");
        }

        // Mod and world names are operator-supplied and land in a TextMeshPro rich-text
        // field, where a '<' is read as the start of a tag. A world called "a<b" would have
        // the rest of its line swallowed as a malformed tag, so the dialog would quietly lose
        // entries rather than show one odd name.
        //
        // Angle brackets are stripped rather than escaped. <noparse> would preserve them, but
        // it cannot be nested and a name containing the literal "</noparse>" would break out
        // of it -- trading a cosmetic problem for an injection into our own markup. No Valheim
        // mod or world name needs angle brackets, so dropping them is the honest trade, and it
        // is why Prettify() returns plain text and adds no tags of its own.
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('<', ' ').Replace('>', ' ');
        }

        // The way back in, after Close. Drawn with IMGUI rather than built as a uGUI button
        // because it needs no prefab, no canvas and no layout group -- and a button that fails
        // to appear is a player with no route back to the dialog.
        private void OnGUI()
        {
            // Either mode can be reopened. The gate used to be LaunchPayload.Present alone,
            // which was right when that was the only reason the dialog existed; with the
            // launch-help notice it would have left that player's only route back missing.
            if (!_closedByPlayer || ConnectFlow.Connecting) return;
            if (!LaunchPayload.Present && !ClientManifest.Present) return;

            try
            {
                if (FejdStartup.instance == null) return;
                if (UnifiedPopup.IsVisible()) return;
            }
            catch
            {
                return;
            }

            if (!IsMainMenuActive()) return;

            const float w = 260f;
            const float h = 34f;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height - h - 24f, w, h);

            if (!_buttonDrawReported)
            {
                _buttonDrawReported = true;
                Main.StaticLogger.LogMessage($"PhValheim reopen button drawn at {rect} (screen {Screen.width}x{Screen.height}).");
            }

            // "Connect to X" would be a promise the help notice cannot keep -- it has no
            // password and therefore no connect path. The label names what the button opens.
            var label = LaunchPayload.Present
                ? $"Connect to {LaunchPayload.Current.World}"
                : $"PhValheim: {ClientManifest.Current.World}";

            if (GUI.Button(rect, label))
            {
                // Hand it to Update() instead of calling Show() here.
                //
                // This used to set _shown = true and push immediately, which had no retry in
                // it: UnifiedPopup silently drops a push it is not ready to accept, and with
                // _shown already true Update() would never try again. The player clicked and
                // nothing happened, for good -- Brian's report on the older-client path, where
                // reopening is the only way back to the notice.
                //
                // Clearing _shown instead lets Update() show it on a frame when
                // IsReadyToShow() actually says yes, which is the path that puts the dialog up
                // in the first place and the only one proven to work.
                Main.StaticLogger.LogMessage("PhValheim button clicked; handing the dialog to Update().");
                _closedByPlayer = false;
                _shown = false;
                _lastDecline = "__clicked__";   // force the next decline to log, whatever it is
            }
        }
    }
}
