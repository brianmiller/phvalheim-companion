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

# NEGATIVE: the download button is gone. settings.phvalheimClientURL is a single url whose
# default has been a Windows .exe since 2.31, so on Linux or macOS it handed the player the
# wrong installer. Brian's call to drop it; this stops it being quietly reintroduced.
notcheck ConnectDialog.ShowLaunchHelp 'OpenURL' \
	"a single client url cannot be right for every platform -- the button was removed"

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
