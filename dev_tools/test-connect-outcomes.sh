#!/bin/bash
# Oracle test: the watchdog tells the THREE ways a join ends apart.
#
# ConnectFlow has exactly one signal -- "the main menu is up again while a join was in
# flight" -- and three completely different causes for it. Every bug in this file has been the
# same bug: a cause the state could not distinguish, reported as whichever one was coded.
#
#   joined, then back          a disconnect the player asked for.  Shipped as "could not
#                              connect": Connecting was never cleared on success, so the
#                              watchdog sat armed all session and fired on logout.
#   character select, Back     a CANCEL. ProceedJoinRequest only QUEUES the join and shows
#                              character select -- JoinServer() does not run until
#                              OnCharacterStart() -- so nothing had touched the network.
#                              Shipped as "Could not connect. The world may still be
#                              starting.": a diagnosis of a connection never attempted, and
#                              a claim about a server nobody had asked.
#   neither                    a real failure. ZNet gives up with no exception, no callback
#                              and no log line, so the menu coming back IS the only signal.
#
# WHAT WOULD MAKE THIS TEST WORTHLESS
# Asserting only that the cancel is quiet. A fix that simply stopped reporting failures passes
# that and destroys the one case the watchdog exists for -- the silent ZNet give-up that left
# Connecting true forever with no dialog and no way back. So the failure case is asserted just
# as hard as the cancel (3), a landed join is asserted to outrank the screen it passed through
# (4) -- every join goes through character select, so getting that precedence backwards would
# relabel every ordinary logout as "you cancelled" -- and the per-attempt reset is checked (6),
# because a flag left set makes the next attempt inherit the last one's verdict.
#
#   ./test-connect-outcomes.sh
set -u

cd "$(dirname "$0")/.." || exit 1
DLL=bin/Release/net472/PhValheimCompanion.dll
[ -f "$DLL" ] || { echo "SKIP: $DLL not built -- run dotnet build first"; exit 1; }

pass=0; fail=0
ok()  { echo "  PASS  $1"; pass=$((pass+1)); }
bad() { echo "  FAIL  $1"; echo "        $2"; fail=$((fail+1)); }

HARNESS=dev_tools/connectOutcomes
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Driven by reflection against the BUILT assembly, the way dev_tools/renderDialog already
# does: ConnectFlow's state is private and the point is to exercise the real decision rather
# than a paraphrase of it here.
#
# It calls Decide(mainMenuActive, now) and not NoticeMainMenu, because that one reads
# Time.realtimeSinceStartup -- an extern in the reference assembly, which throws "ECall
# methods must be packaged into a system module" the moment a harness touches it. Passing the
# clock in is what makes the decision table testable at all.
if ! dotnet build "$HARNESS" -c Release -v q --nologo > "$TMP/build.log" 2>&1; then
	echo "SKIP: could not build $HARNESS:"; sed 's/^/    /' "$TMP/build.log" | tail -15; exit 1
fi
if ! dotnet "$HARNESS/bin/Release/net8.0/connectOutcomes.dll" "$DLL" libs \
     > "$TMP/out" 2>"$TMP/err"; then
	echo "SKIP: the harness did not run:"; sed 's/^/    /' "$TMP/err" | tail -12; exit 1
fi

got() { grep "^$1 " "$TMP/out" | sed "s/^$1  *//"; }

# ---- 1. a disconnect is silent -------------------------------------------------------------
if [ "$(got disconnect)" = "gaveUp=True connecting=False failure=null notice=null" ]; then
	ok "joined then back = a disconnect: attempt ends, nothing reported"
else
	bad "disconnect is silent" "got '$(got disconnect)'"
fi

# ---- 2. a cancel is a NOTICE, never a failure ----------------------------------------------
if [ "$(got cancel)" = "gaveUp=True connecting=False failure=null notice=set" ]; then
	ok "character select then Back = a cancel: a notice, and NO failure"
else
	bad "a cancel is not a failure" "got '$(got cancel)'
        This is the reported bug: Back from character select said \"Could not connect. The
        world may still be starting\" about a connection that was never attempted."
fi

# ---- 3. CONTROL: a real failure is still reported ------------------------------------------
# The case the watchdog exists for. A fix that just stopped reporting failures passes every
# other assertion here and silently restores the hang this file was written to end.
if [ "$(got failure)" = "gaveUp=True connecting=False failure=set notice=null" ]; then
	ok "CONTROL: neither = a real failure, still reported"
else
	bad "CONTROL: a real failure is reported" "got '$(got failure)'
        ZNet gives up with no exception and no callback; the menu returning is the only
        signal there is. Losing this leaves Connecting true forever -- no dialog, no way back."
fi

# ---- 4. a landed join outranks the screen it came through ----------------------------------
if [ "$(got joined_via)" = "gaveUp=True connecting=False failure=null notice=null" ]; then
	ok "joined via character select = a disconnect, not a cancel"
else
	bad "a landed join outranks the screen" "got '$(got joined_via)'
        Every join goes through character select, so if the cancel branch were tested first
        every ordinary logout would be reported as \"you cancelled\"."
fi

# ---- 5. the notice names an action --------------------------------------------------------
notice=$(got notice_text)
if echo "$notice" | grep -qi "connect" && echo "$notice" | grep -qi "character"; then
	ok "the notice names what to do: \"$notice\""
else
	bad "the notice is actionable" "got '$notice' -- it should name Connect and the character step"
fi
# Length is capped by test-publicizer-trap.sh against ConnectDialog's own budget.

# ---- 6. per-ATTEMPT reset ------------------------------------------------------------------
# Begin() must clear both flags. Left set, the next attempt inherits the last one's verdict:
# a cancel followed by a genuine failure would be reported as "you cancelled", and the player
# would be told nothing was wrong with a world that is down.
if grep -qF '_reachedCharacterSelect = false;' ConnectFlow.cs \
   && grep -qF '_joined = false;' ConnectFlow.cs; then
	ok "Begin() resets both outcome flags per attempt"
else
	bad "both flags reset in Begin()" \
	    "a flag surviving an attempt makes the next one inherit its verdict"
fi

# ---- 7. the observer is wired, and on the right side --------------------------------------
# _joined needed JoinSentry because ConnectDialog is destroyed on the way into a world. The
# character select screen is the opposite: it belongs to FejdStartup, which is alive while it
# is up -- so this one must be observed from the dialog's own Update, BEFORE the watchdog runs.
if grep -qF 'ConnectFlow.NoticeCharacterSelect(IsCharacterSelectActive());' ConnectDialog.cs; then
	ok "ConnectDialog.Update observes character select"
else
	bad "the observer is called" \
	    "NoticeCharacterSelect exists but nothing calls it, so the flag is never set and the
        cancel branch is dead code -- every Back stays a reported failure"
fi
order=$(grep -n 'NoticeCharacterSelect\|NoticeMainMenu' ConnectDialog.cs | head -2 | cut -d: -f1 | tr '\n' ' ')
first=${order%% *}; second=$(echo "$order" | awk '{print $2}')
if [ -n "$first" ] && [ -n "$second" ] && [ "$first" -lt "$second" ]; then
	ok "it runs BEFORE the watchdog (lines $first then $second)"
else
	bad "observe before deciding" \
	    "NoticeMainMenu decides on the frame the main menu reappears, and by then character
        select is already gone -- so observing after it would never see the screen at all"
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
