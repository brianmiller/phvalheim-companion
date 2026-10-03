#!/bin/bash
# Oracle test: can the dialog's code actually be REACHED?
#
# WHY IT EXISTS
# 2.53's launch-help notice shipped in :rc unreachable. Three gates decide whether it appears:
#
#   1. FejdStartupPatch.SetupGui  -- attach the component at all?
#   2. ConnectDialog.Update       -- call Show() this frame?
#   3. ConnectDialog.Show         -- which dialog to build?
#
# Gates 1 and 3 were updated to consider the manifest. Gate 2 was not, so the component was
# attached, found its manifest, and returned early forever. Brian updated a world, launched it
# with a 2.0.13 client, and got exactly the silence the feature exists to end.
#
# EVERY CHEAP CHECK PASSED.
# The dll contained every new string. The manifest was written, and was inside the payload zip.
# Eight build-verify markers were green. All of them asked "is the code present?" -- none could
# ask "is it on a path that runs?". A source grep cannot answer it either: the missed line sat
# four lines below a comment block explaining the very feature it blocked.
#
# So this reads the IL of the built assembly. A method that does not CALL
# ClientManifest.get_Present cannot be making a decision about the manifest, whatever the
# source looks like.
#
# Usage:  bash dev_tools/test-dialog-reachability.sh [path/to/PhValheimCompanion.dll]
#         defaults to bin/Release/net472/PhValheimCompanion.dll

set -u

ROOT=$(cd "$(dirname "$0")/.." && pwd)
DLL=${1:-$ROOT/bin/Release/net472/PhValheimCompanion.dll}
PROBE=$ROOT/dev_tools/apiProbe
LIBS=$ROOT/libs

PASS=0; FAIL=0
pass(){ echo "  PASS: $1"; PASS=$((PASS+1)); }
fail(){ echo "  FAIL: $1"; [ -n "${2:-}" ] && echo "        $2"; FAIL=$((FAIL+1)); }

[ -f "$DLL" ] || { echo "no dll at $DLL -- build it first (dotnet build -c Release)"; exit 2; }

# The probe is the instrument; if it cannot run, say so instead of reporting a pass count that
# means nothing.
if ! (cd "$PROBE" && dotnet build -c Release -v q --nologo >/dev/null 2>&1); then
	echo "FAIL: apiProbe will not build -- this test is blind, fix the probe"
	exit 2
fi

il() {
	APIPROBE_ASSEMBLY="$DLL" sh -c "cd '$PROBE' && dotnet run -c Release --no-build -- '$LIBS' '!$1' 2>/dev/null"
}

# Every assertion runs against a REAL il dump, and a dump that came back empty or MISSING is a
# failure rather than a silent zero -- the method may have been renamed, which would make every
# "does it call X" check below vacuously true.
check() {
	local method="$1" want="$2" why="$3"
	local out; out=$(il "$method")

	case "$out" in
		*"MISSING"*|"")
			fail "$method: no IL (renamed or absent)" \
			     "every reachability claim about this method is now untested" ; return ;;
	esac

	if echo "$out" | grep -q "$want"; then
		pass "$method calls $want"
	else
		fail "$method does NOT call $want" "$why"
	fi
}

notcheck() {
	local method="$1" unwanted="$2" why="$3"
	local out; out=$(il "$method")
	case "$out" in *"MISSING"*|"") fail "$method: no IL (renamed or absent)"; return ;; esac

	if echo "$out" | grep -q "$unwanted"; then
		fail "$method unexpectedly calls $unwanted" "$why"
	else
		pass "control: $method does not call $unwanted"
	fi
}

echo
echo "== gate 2: Update() must consider BOTH, or Show() is never called =="

check ConnectDialog.Update 'LaunchPayload.get_Present' \
	"the launch path would stop working"
check ConnectDialog.Update 'ClientManifest.get_Present' \
	"THIS IS THE BUG THAT SHIPPED: component attached, manifest found, Show() never called"
check ConnectDialog.Update 'ConnectDialog.Show' \
	"Update is the only caller of Show on the automatic path"

echo
echo "== gate 1: the patch must attach the component for a manifest too =="

check SetupGui.Postfix 'ClientManifest.get_Present' \
	"with no payload the component would never be attached, so nothing could show"
check SetupGui.Postfix 'Main' \
	"the ShowLaunchHelp opt-out is read here; without it the notice cannot be silenced"

echo
echo "== gate 3: Show() must be able to build the help dialog =="

check ConnectDialog.Show 'ConnectDialog.ShowLaunchHelp' \
	"Show would fall through to the connect dialog with a null payload"
check ConnectDialog.ShowLaunchHelp 'ClientManifest.get_Current' \
	"the help dialog cannot name the world without reading the manifest"
check ConnectDialog.ShowLaunchHelp 'ConnectDialog.BuildLaunchHelpBody' \
	"the panel would be pushed with the wrong body"

echo
echo "== the way back is reachable in both modes =="

check ConnectDialog.OnGUI 'ClientManifest.get_Present' \
	"after Close, a help-mode player would have no button to reopen the notice"

# NEGATIVE, and this one shipped broken. OnGUI used to call Show() directly and set _shown,
# which has no retry in it: UnifiedPopup silently drops a push it is not ready for, and with
# _shown already true Update() never tried again. The player clicked and nothing happened, for
# good. Reopening must go through Update() -> IsReadyToShow(), the path that works.
notcheck ConnectDialog.OnGUI 'ConnectDialog.Show' \
	"pushing from OnGUI has no retry: a dropped push leaves the player with no way back"

echo
echo "== the way back is a REAL menu button =="

# The IMGUI button drew correctly -- the log has its exact rect -- and never received a click.
# A cloned uGUI Button gets clicks through whatever input path the game itself uses.
check ConnectDialog.Update 'ConnectDialog.ManageReopenButton' \
	"nothing would create or remove the button"
check ConnectDialog.ManageReopenButton 'MenuButton.Ensure' \
	"the native button would never be created, leaving only the unclickable drawn one"
check MenuButton.Ensure 'Instantiate' \
	"nothing is cloned, so there is no button"

# THE DANGEROUS ONE, AND THE CHECK THAT WAS TRUE WHILE THE BUG WAS LIVE.
#
# The template is a LIVE Valheim menu button with its own handler attached. This used to assert
# MenuButton.Ensure calls RemoveAllListeners, which it did -- and Brian still reported "clicking
# it takes you to the character selection screen", because the clone was running Valheim's
# handler as well as ours.
#
# UnityEvent.RemoveAllListeners() clears only RUNTIME listeners (AddListener). Valheim's menu
# buttons are wired in the Inspector, so theirs are PERSISTENT listeners serialized in the
# prefab, and RemoveAllListeners does not touch them. The assertion was satisfied and the
# button still started the game: present is not effective.
#
# So the check is now anchored on the call that actually disables them. SetPersistentListenerState
# is the only runtime way to switch a persistent listener off.
check MenuButton.Ensure 'MenuButton.DisableInheritedHandlers' \
	"nothing would strip the handler the clone inherited from the live menu button"

check MenuButton.DisableInheritedHandlers 'SetPersistentListenerState' \
	"INHERITED HANDLER LIVE: RemoveAllListeners alone leaves the prefab's own listener, so the clone also runs Start Game"

check MenuButton.DisableInheritedHandlers 'GetPersistentEventCount' \
	"without the count the loop cannot visit every inherited listener"

check MenuButton.DisableInheritedHandlers 'RemoveAllListeners' \
	"runtime listeners would survive a re-Ensure and our own handler would fire twice"
check MenuButton.Ensure 'AddListener' \
	"the clone would be inert"

echo
echo "== NOTHING ON THE PER-FRAME PATH MAY LOG UNCONDITIONALLY =="

# ManageReopenButton calls Ensure on EVERY Update. A log on a failure exit here is ~60 lines a
# second, each formatting a string and each flushed to disk by BepInEx.
#
# This shipped. Every failure exit in Ensure used to be SILENT, which is what made the
# gray-button rounds unfalsifiable -- so a log was added to each one, and what went out was a
# per-frame LogWarning for as long as the main menu was open. Brian's client ran out of memory
# on that build after a hundred launches that were fine. A real diagnostic became a leak.
#
# Warn must dedupe on the reason, so a stuck failure costs ONE line.
check MenuButton.Warn 'MenuButton._lastWarn' \
	"PER-FRAME LOG: a stuck failure would write ~60 lines a second for as long as the menu is open"

# And a failing Ensure must STOP. A reflected lookup that failed on frame 1 will not succeed on
# frame 3600, and the drawn fallback is already on screen in the meantime.
check MenuButton.Ensure 'MenuButton.ShouldGiveUp' \
	"NO GIVE-UP: Ensure would retry a hopeless lookup at frame rate forever"

# The label is only reapplied when it CHANGED. TMP rebuilds its mesh on assignment, so setting
# the same string every frame is continuous garbage on the one path that runs every frame while
# the button is up.
check MenuButton.Ensure 'MenuButton.LabelNeedsApplying' \
	"PER-FRAME TMP WRITE: the label would be re-set and re-meshed every frame"

# The clone lives on the CANVAS, not on the menu object, so it outlives this component unless
# it is explicitly destroyed -- a stale button into the next main menu with a dead callback.
check ConnectDialog.OnDestroy 'MenuButton.Remove' \
	"the clone would survive into the next main menu"

echo
echo "== one button, centred by the game, and every override put back =="

# WarningPopup, not YesNoPopup. UnifiedPopup.ShowWarning activates buttonCenter and centres
# it itself, which is why the notice needs no position nudge. The first attempt pushed a
# YesNoPopup and hid the unused left button, leaving the right one where the right of a PAIR
# goes -- off centre, and pulled further off by ButtonPull.
check ConnectDialog.ShowLaunchHelp 'WarningPopup' \
	"a YesNoPopup would put its single button in a two-button position: off centre"
notcheck ConnectDialog.ShowLaunchHelp 'YesNoPopup' \
	"the two-button popup is the connect dialog's, not the notice's"

# The label lives on UnifiedPopup.okText -- the field ShowWarning localizes onto
# buttonCenterText. Verified in its IL, not assumed.
check ConnectDialog.ShowLaunchHelp 'ConnectDialog.ApplyOkLabel' \
	"the button would keep Valheim's own OK label"

# THE DANGEROUS HALF. UnifiedPopup is a shared singleton, so okText left overridden reads
# "Close" on every later warning dialog in the session, vanilla's included.
# Anchored on _savedOkText, not on SetPrivateField. RestorePopupSkin calls SetPrivateField for
# yesText and noText as well, so the looser check passed a mutant with the okText restore
# deleted -- it could not tell WHICH field was being put back. This field is read by nothing
# else, so loading it is the restore.
check ConnectDialog.RestorePopupSkin 'ConnectDialog._savedOkText' \
	"MISSING RESTORE: okText would stay overridden, so every later warning dialog in the session reads Close"

# The smaller panel. Both dialogs scale through one method so there is one place that sets it
# and one that puts it back.
check ConnectDialog.ApplyOkLabel 'ConnectDialog.ScalePanel' \
	"the notice would be drawn in the connect dialog's much larger panel"

# The title is tinted to PhValheim's accent, and UnifiedPopup is shared -- left behind, every
# later popup in the session has a cyan title, vanilla's "Remove this character?" included.
# Anchored on the saved field, which nothing else reads, rather than on a set_color call that
# the tint itself would also satisfy.
check ConnectDialog.RestorePopupSkin 'ConnectDialog._savedHeaderColor' \
	"MISSING RESTORE: the themed title colour would leak onto every later popup in the session"

# NEGATIVE: the download button is gone. settings.phvalheimClientURL is a single url whose
# default has been a Windows .exe since 2.31, so on Linux or macOS it handed the player the
# wrong installer. Brian's call to drop it; this stops it being quietly reintroduced.
notcheck ConnectDialog.ShowLaunchHelp 'OpenURL' \
	"a single client url cannot be right for every platform -- the button was removed"

echo
echo "== the panel tree is REPORTED, not guessed at =="

# The reskin that identified the panel background by rect area shipped as a full-screen box
# with a border and no text. What replaced it only READS. These two checks pin that: the tree
# must be logged, and nothing may paint the panel again until the tree has been read.
check ConnectDialog.ApplyChromeStyle 'PanelTree.LogOnce' \
	"the popup's Image tree would still be unknown, and the next reskin another guess"

check ConnectDialog.ApplyChromeStyle 'PanelSkin.Apply' \
	"the panel would keep Valheim's brown parchment and only the text would be themed"

check ConnectDialog.RestorePopupSkin 'PanelSkin.Restore' \
	"MISSING RESTORE: the tint would land on vanilla's own popups for the rest of the session"

# THE GUARANTEE THAT MAKES THE SECOND ATTEMPT SAFE. The first reskin INSERTED a quad, and the
# quad landed over the dialog's text -- a full screen with a border and nothing readable. This
# version only writes Image.color. If PanelSkin ever gains an Instantiate it has reacquired
# the exact failure mode that made the dialog unusable.
notcheck PanelSkin.Apply 'Instantiate' \
	"an inserted object can cover the text; the tint-only version cannot, and that is the point"

# NEGATIVE, and this is the one with teeth: a read-only reporter must not instantiate or
# recolour anything. If PanelTree ever grows an Instantiate it has stopped being a diagnostic.
notcheck PanelTree.LogOnce 'Instantiate' \
	"a diagnostic that creates objects is a reskin again, and the last one made the dialog unusable"

echo
echo "== the way back does not depend on a button at all =="

# The reopen button has failed in three forms on Brian's client. This route uses only
# m_mainMenu's active state -- the same read that already decides whether the dialog may show,
# which is proven to work because the dialog shows.
check ConnectDialog.Update 'ConnectDialog._menuWasActive' \
	"leaving the main menu and returning would not re-offer the notice, so a dead button would again be the only route"

echo
echo "== the reopen button has more than one way to find a template =="

# The gray-button round: Ensure() returned false and the ONE return that did it logged
# nothing, so there was no way to tell from the log which gate refused.
check MenuButton.Ensure 'MenuButton.Warn' \
	"a refusal would be silent again, which is what made the last round unfalsifiable"

check MenuButton.Ensure 'MenuButton.FindTemplate' \
	"the reflected m_menuButtons read would be the only route to a template"

# The fallback routes. m_menuButtons is the one step here a game update can break silently;
# the menu demonstrably HAS buttons whenever it is on screen, so a hierarchy search cannot
# come up empty on a menu the player is looking at.
check MenuButton.FindTemplate 'GetComponentsInChildren' \
	"with m_menuButtons empty or renamed there would be no second route and no native button"

check MenuButton.Ensure 'MenuButton.Skin' \
	"the button would keep Valheim's own colours -- Brian's 'the Connect button is still gray'"

echo
echo "== controls: the manifest must NOT have leaked everywhere =="

# If every method consulted the manifest, the checks above would pass for a build in which the
# distinction between the two modes had been dissolved. OnConnect is launch-path only: it
# connects using the payload, which the help mode by definition does not have.
notcheck ConnectDialog.OnConnect 'ClientManifest' \
	"connecting is a launch-payload act; the help mode has no password to connect with"

# And the help dialog must never read the payload -- in that mode there is none, so a call here
# would be a NullReferenceException on the player's screen.
notcheck ConnectDialog.ShowLaunchHelp 'LaunchPayload' \
	"there is no payload in help mode; reading it would throw"

notcheck ConnectDialog.BuildLaunchHelpBody 'LaunchPayload' \
	"same -- the body is built from the manifest alone"

echo
echo "$PASS passed, $FAIL failed"
[ "$FAIL" = "0" ] || exit 1
