#!/bin/bash
#
# The connect dialog's layout, as a test.
#
# The panel YesNoPopup gives us is a FIXED size and its body text does NOT clip -- it overflows
# in both directions at once. So a body that is a few lines too tall draws the world name on top
# of the "PhValheim" header AND hides the closing sentence behind the Connect and Close buttons.
# Nothing throws, nothing logs, and the build is green. The only previous way to see it was to
# ship it and look at a photograph of someone's screen, which is exactly what happened.
#
# This renders the REAL ConnectDialog.BuildBodyText out of the built DLL for a set of worlds
# that stress the height budget, and fails when any of them exceeds it. It reflects the shipped
# method rather than re-implementing the formatting: a re-implementation would agree with itself
# and prove nothing.
#
# Exit 0 = every layout fits. Exit 1 = at least one overflows. Exit 2 = could not run.

set -u

cd "$(dirname "$0")/.." || exit 2

COMPANION_DLL="bin/Release/net472/PhValheimCompanion.dll"
HARNESS="dev_tools/renderDialog"

if [ ! -f "$COMPANION_DLL" ]; then
	echo "  SKIP  no $COMPANION_DLL -- run: dotnet build -c Release"
	echo "        Exiting 2 rather than 0: an absent artifact is not a pass."
	exit 2
fi

# The harness targets net8.0 while the Companion is net472. That is deliberate: there is no mono
# on the build host, and .NET can load the net472 assembly for reflection perfectly well because
# BuildBodyText touches nothing but System.Text and System.Collections.
if ! dotnet build "$HARNESS" -c Release -v q --nologo >/dev/null 2>&1; then
	echo "  FAIL  the render harness does not build"
	exit 2
fi

echo "== connect dialog layout =="
dotnet "$HARNESS/bin/Release/net8.0/renderDialog.dll" "$COMPANION_DLL" libs
status=$?

# Distinguish "a layout is too tall" from "the harness could not run". Reporting a crash as an
# overflow sends you editing the layout to fix a signature change, which is a wrong-severity
# message -- it blames the wrong thing and the real fault stays put.
if [ "$status" -eq 1 ]; then
	echo ""
	echo "  A layout above is taller than the panel. It will render over the header and under"
	echo "  the buttons. Shorten the body -- do NOT raise BodyLineBudget to make this pass,"
	echo "  because the budget is a property of Valheim's panel, not of this test."
elif [ "$status" -ne 0 ]; then
	echo ""
	echo "  The harness itself failed (exit $status) -- this is NOT a layout verdict."
	echo "  Most likely BuildBodyText changed shape and renderDialog has not followed it."
fi

exit $status
