#!/bin/bash
#
# The publicizer trap, as a test.
#
# The Companion compiles against libs/assembly_valheim_publicized.dll, in which every member
# is public. So a direct call to a member that is really PRIVATE in the assembly the game
# loads compiles with zero warnings and throws MissingMethodException or
# MissingFieldException the first time a player clicks the button. A green build cannot see
# this. Neither can any test that only runs the build.
#
# This test can see it. It asks a real, un-publicized assembly_valheim.dll what each member's
# visibility actually is, and fails if something the Companion calls DIRECTLY is not public.
#
# Two lists, and the split is the whole point:
#
#   DIRECT  -- called as ordinary C#. Must be public in the real assembly.
#   REFLECTED -- reached through Utils.InvokePrivate / GetPrivateField / GetProperty. May be
#               private; must still EXIST, because a renamed member fails just as hard, only
#               later and with a worse message.
#
# A member that moves from DIRECT to private in a Valheim update fails here instead of in
# front of Brian.

set -u

cd "$(dirname "$0")/.." || exit 1

PROBE="dev_tools/apiProbe"
PUBLICIZED="libs"

# Where to find a real assembly_valheim.dll. This is NOT the publicized one and NOT the
# dedicated-server one -- the server build has no FejdStartup or UnifiedPopup at all, so
# checking against it would report every menu member MISSING and look like a catastrophe.
REALREFS="${PHVALHEIM_REALREFS:-/tmp/realrefs}"

fail=0
checked=0

say()  { printf '%s\n' "$*"; }
pass() { checked=$((checked+1)); say "  ok    $1"; }
bad()  { checked=$((checked+1)); fail=$((fail+1)); say "  FAIL  $1 -- $2"; }

probe() {
	dotnet run --project "$PROBE" --no-build -- "$1" "$2" 2>/dev/null | grep -v '^skip '
}

# ---------------------------------------------------------------------------
# Preconditions. Each of these would otherwise make the whole suite pass for
# the wrong reason -- a missing oracle reads as "nothing to report".
# ---------------------------------------------------------------------------

say "== preconditions =="

if [ ! -f "$PUBLICIZED/assembly_valheim_publicized.dll" ]; then
	say "  FAIL  no $PUBLICIZED/assembly_valheim_publicized.dll -- run refresh-libs.sh"
	exit 1
fi

if [ ! -f "$REALREFS/assembly_valheim.dll" ]; then
	say "  SKIP  no un-publicized assembly_valheim.dll in $REALREFS"
	say ""
	say "  This test cannot run without one, and it must be the CLIENT assembly:"
	say "    find / -name assembly_valheim.dll | grep -v valheim_server_Data"
	say "  then copy it plus libs/*.dll into \$PHVALHEIM_REALREFS (default /tmp/realrefs)."
	say ""
	say "  Exiting 2 rather than 0: an absent oracle is not a pass."
	exit 2
fi

if ! dotnet build "$PROBE" -v q --nologo >/dev/null 2>&1; then
	say "  FAIL  the API probe does not build"
	exit 1
fi
say "  ok    probe builds, both assemblies present"

# The control. If the probe reports the same visibility for a member that is public in one
# assembly and private in the other, it is not reading the assemblies -- it is guessing, and
# every result below it is worthless. ProceedJoinRequest is the known-private case.
pub_answer=$(probe "$PUBLICIZED" '=FejdStartup.ProceedJoinRequest')
real_answer=$(probe "$REALREFS" '=FejdStartup.ProceedJoinRequest')

if [ "$pub_answer" = "PUBLIC  FejdStartup.ProceedJoinRequest" ] \
	&& [ "$real_answer" = "NON-PUBLIC  FejdStartup.ProceedJoinRequest" ]; then
	say "  ok    control: probe tells the two assemblies apart"
else
	say "  FAIL  control: probe cannot tell the assemblies apart"
	say "          publicized said: $pub_answer"
	say "          real said:       $real_answer"
	say "        Every result below this line would be meaningless. Stopping."
	exit 1
fi

# ---------------------------------------------------------------------------
# DIRECT calls. Must be public in the real assembly.
# ---------------------------------------------------------------------------

say ""
say "== members the Companion calls directly (must be PUBLIC) =="

DIRECT="
FejdStartup.get_instance
FejdStartup.m_versionLabel
UnifiedPopup.IsAvailable
UnifiedPopup.IsVisible
UnifiedPopup.Push
UnifiedPopup.Pop
YesNoPopup..ctor
ZPlayFabMatchmakingSuccessCallback.Invoke
ZPlayFabMatchmakingFailedCallback.Invoke
ServerJoinData..ctor
ServerJoinDataDedicated..ctor
ServerJoinDataPlayFabUser..ctor
ZPlayFabMatchmaking.ResolveJoinCode
PlayFabManager.get_IsLoggedIn
PlayFabMatchmakingServerData.remotePlayerId
PlayFabMatchmakingServerData.serverName
ZNet.get_instance
ZNet.IsServer
ZNet.IsDedicated
WorldGenerator.get_instance
World.m_name
ItemStand.m_supportedItems
MultiBackendMatchmaking.GetServerIPAsync
ResolveDomainCompletedHandler.Invoke
TMP_Text.set_verticalAlignment
TMP_Text.set_margin
TMP_Text.set_enableWordWrapping
TMP_Text.set_overflowMode
TMP_Text.get_textInfo
TMP_Text.get_rectTransform
Player.m_localPlayer
"

for member in $DIRECT; do
	out=$(probe "$REALREFS" "=$member")
	case "$out" in
		PUBLIC*)          pass "$member" ;;
		NON-PUBLIC*)      bad  "$member" "private in the real assembly; call it through Utils.InvokePrivate/GetPrivateField instead" ;;
		MISSING*)         bad  "$member" "not in the real assembly at all; Valheim renamed or removed it" ;;
		*)                bad  "$member" "probe said: $out" ;;
	esac
done

# ---------------------------------------------------------------------------
# REFLECTED members. May be private, but must exist.
# ---------------------------------------------------------------------------

say ""
say "== members the Companion reflects (may be private, must EXIST) =="

# UnifiedPopup.instance, yesText and noText are on this list because this test put them
# here: they were written as direct field access, built clean, and would have thrown
# MissingFieldException the first time the dialog appeared.
REFLECTED="
FejdStartup.ProceedJoinRequest
FejdStartup.m_mainMenu
FejdStartup.ServerPassword
WorldGenerator.m_world
UnifiedPopup.instance
UnifiedPopup.yesText
UnifiedPopup.noText
UnifiedPopup.bodyText
UnifiedPopup.popupUIParent
UnifiedPopup.headerText
UnifiedPopup.buttonLeft
UnifiedPopup.buttonRight
UnifiedPopup.buttonLeftText
UnifiedPopup.buttonRightText
"

for member in $REFLECTED; do
	out=$(probe "$REALREFS" "=$member")
	case "$out" in
		MISSING*)  bad  "$member" "not in the real assembly; reflection will fail at runtime" ;;
		PUBLIC*)   pass "$member (public -- reflection still correct, just not required)" ;;
		NON-PUBLIC*) pass "$member (private, as expected)" ;;
		*)         bad  "$member" "probe said: $out" ;;
	esac
done

# ---------------------------------------------------------------------------
# The flow itself. These are not visibility questions -- they are the facts the
# connect flow is BUILT on, and if Valheim changes any of them the flow breaks
# while every visibility check above still passes.
# ---------------------------------------------------------------------------

say ""
say "== the assumptions the connect flow rests on =="

# OnCharacterStart must still complete the join by itself. If Valheim stops reading
# m_queuedJoinServer there, setting it achieves nothing and the player lands in the world
# list after picking a character -- with no error anywhere.
if probe "$REALREFS" '!FejdStartup.OnCharacterStart' | grep -q 'ldflda   FejdStartup.m_queuedJoinServer'; then
	pass "OnCharacterStart still tests m_queuedJoinServer"
else
	bad "OnCharacterStart" "no longer reads m_queuedJoinServer -- ConnectFlow's whole premise is gone"
fi

if probe "$REALREFS" '!FejdStartup.OnCharacterStart' | grep -q 'call     FejdStartup.JoinServer'; then
	pass "OnCharacterStart still calls JoinServer itself"
else
	bad "OnCharacterStart" "no longer calls JoinServer -- the join would never happen"
fi

# ProceedJoinRequest must still be the thing that queues the join AND shows character
# selection. If it stops calling ShowCharacterSelection, Connect would queue a join and leave
# the player looking at the menu.
if probe "$REALREFS" '!FejdStartup.ProceedJoinRequest' | grep -q 'stfld    FejdStartup.m_queuedJoinServer'; then
	pass "ProceedJoinRequest still sets m_queuedJoinServer"
else
	bad "ProceedJoinRequest" "no longer sets m_queuedJoinServer"
fi

if probe "$REALREFS" '!FejdStartup.ProceedJoinRequest' | grep -q 'call     FejdStartup.ShowCharacterSelection'; then
	pass "ProceedJoinRequest still shows character selection"
else
	bad "ProceedJoinRequest" "no longer calls ShowCharacterSelection -- Connect would go nowhere"
fi

# The popup labels. ShowYesNo maps yesText to the RIGHT button and noText to the LEFT. If that
# mapping flips, Connect and Close swap places and a player clicks the wrong one -- a bug no
# build or exception would ever reveal.
yesno=$(probe "$REALREFS" '!UnifiedPopup.ShowYesNo')
if printf '%s' "$yesno" | grep -A2 'buttonRightText' | grep -q 'UnifiedPopup.yesText'; then
	pass "ShowYesNo still puts yesText on the right button (Connect)"
else
	bad "ShowYesNo" "yesText is no longer the right button -- Connect and Close may be swapped"
fi

if printf '%s' "$yesno" | grep -A2 'buttonLeftText' | grep -q 'UnifiedPopup.noText'; then
	pass "ShowYesNo still puts noText on the left button (Close)"
else
	bad "ShowYesNo" "noText is no longer the left button -- Connect and Close may be swapped"
fi

# ZNet.RPC_ClientHandshake is what consumes FejdStartup.ServerPassword. If it stops reading
# it, pre-filling the password silently does nothing and players get an unexpected prompt.
if probe "$REALREFS" '@get_ServerPassword' | grep -q 'ZNet.RPC_ClientHandshake'; then
	pass "ZNet.RPC_ClientHandshake still reads FejdStartup.ServerPassword"
else
	bad "ServerPassword" "nothing reads it during the handshake any more; pre-filling it is a no-op"
fi

# ---------------------------------------------------------------------------
# The DNS timing the IP:PORT join depends on.
#
# ConnectFlow pre-resolves gameDNS before calling ProceedJoinRequest. That is only necessary
# -- and only sufficient -- because of four facts about Valheim's own code. If any of them
# changes, the pre-resolve becomes either pointless or actively wrong, and NOTHING else here
# would notice: the build stays green, every visibility check above still passes, and the
# symptom is a player bounced back to the main menu with no error.
#
# These were read out of a real client's IL, not assumed. See the comment block on
# JoinByAddressRoutine.
# ---------------------------------------------------------------------------

say ""
say "== the DNS timing the IP:PORT join depends on =="

joinserver=$(probe "$REALREFS" '!FejdStartup.JoinServer')

# 1. JoinServer still resolves the address rather than connecting to the name.
if printf '%s' "$joinserver" | grep -q 'MultiBackendMatchmaking.GetServerIPAsync'; then
	pass "JoinServer still resolves dedicated addresses via GetServerIPAsync"
else
	bad "JoinServer" "no longer calls GetServerIPAsync -- the pre-resolve may now be pointless; re-read the dedicated branch before trusting it"
fi

# 2. JoinServer still does NOT wait for that callback -- it transitions regardless. This is
#    the actual bug being worked around. If Valheim ever starts waiting, the pre-resolve is
#    redundant (harmless, but the comment explaining it becomes a lie).
if printf '%s' "$joinserver" | grep -q 'FejdStartup.TransitionToMainScene'; then
	pass "JoinServer still transitions to the main scene without awaiting the resolve"
else
	bad "JoinServer" "no longer calls TransitionToMainScene -- the join sequence has changed shape"
fi

# 3. A cold cache really is asynchronous. GetServerIPAsync answers synchronously ONLY when the
#    address is already known; otherwise it hands off to the resolver. If it stopped doing
#    that, there would be no race to lose and no reason for any of this.
if probe "$REALREFS" '!MultiBackendMatchmaking.GetServerIPAsync' | grep -q 'DnsResolver.ResolveDomainNameAsync'; then
	pass "GetServerIPAsync still defers an unknown host to the async resolver"
else
	bad "GetServerIPAsync" "no longer defers to ResolveDomainNameAsync -- the cold-cache race this works around may be gone"
fi

# 4. Warming the cache actually helps: ResolveDomainNameAsync must still consult the cache, and
#    the resolver must still write to it. Both halves, because either one missing breaks the
#    mechanism while leaving the other looking fine.
resolve_async=$(probe "$REALREFS" '!DnsResolver.ResolveDomainNameAsync')
if printf '%s' "$resolve_async" | grep -q 'DnsResolver.m_dnsResolveCache'; then
	pass "ResolveDomainNameAsync still answers from the DNS cache when it is warm"
else
	bad "ResolveDomainNameAsync" "no longer reads m_dnsResolveCache -- pre-resolving would NOT make Valheim's own call synchronous, so the IP:PORT join is still broken"
fi

if probe "$REALREFS" '@m_dnsResolveCache' | grep -q 'DnsResolver.SetCacheEntry'; then
	pass "something still writes the DNS cache (SetCacheEntry)"
else
	bad "m_dnsResolveCache" "nothing writes the cache any more -- a pre-resolve would never warm it"
fi

# The negative control. These checks are all greps for a string in a dump, and a grep against
# an empty or failed dump answers "not found" for every one of them -- which here would read
# as a cascade of real failures, or, if the polarity were ever flipped, as a clean pass over
# nothing at all. Assert the dump has content AND that an absent member really does come back
# absent.
if [ -n "$joinserver" ] && ! printf '%s' "$joinserver" | grep -q 'MultiBackendMatchmaking.ThisMethodDoesNotExist'; then
	pass "control: the IL dump has content and does not match a fabricated member"
else
	bad "control" "the JoinServer IL dump is empty or matches anything -- every DNS check above is answering about nothing"
fi

# ---------------------------------------------------------------------------
# Button order. Brian asked for Connect on the LEFT and Close on the RIGHT.
#
# Valheim decides the sides: ShowYesNo puts the YES callback on the right button and the NO
# callback on the left. So Connect-on-the-left means Connect must be passed in the NO slot --
# which reads backwards, and is therefore exactly the thing a later tidy-up "corrects" back.
# Checked here because nothing else can see it: both orders compile, both run, and the only
# symptom is a player clicking the wrong button.
# ---------------------------------------------------------------------------

say ""
say "== button order (Connect left, Close right) =="

ctor_args=$(sed -n '/new YesNoPopup(/,/));/p' ConnectDialog.cs | grep -oE 'On(Connect|Close)' | tr '\n' ' ')

case "$ctor_args" in
	"OnClose OnConnect ")
		pass "Connect is in the no slot (left), Close in the yes slot (right)" ;;
	"OnConnect OnClose ")
		bad "button order" "OnClose then OnConnect; passing OnConnect first puts Connect on the RIGHT, which is the old order Brian asked to change" ;;
	*)
		bad "button order" "could not read the YesNoPopup callbacks from ConnectDialog.cs (got: '$ctor_args')" ;;
esac

# The labels have to follow the callbacks. Connect on the left means the NO label is "Connect".
if grep -q 'ApplyPopupSkin(yesLabel: "Close", noLabel: "Connect")' ConnectDialog.cs; then
	pass "labels match the slots (yes=Close on the right, no=Connect on the left)"
else
	bad "button labels" "ApplyPopupSkin's yes/no labels do not read yes=Close, no=Connect -- labels and callbacks must move together or a button says Connect and closes"
fi

# ---------------------------------------------------------------------------
# The watchdog has to be reachable.
#
# ConnectDialog.Update() returns early when the dialog is already shown or the player closed
# it. The Connecting watchdog must run BEFORE that return, because the state it exists to
# clear is the state that return triggers on. Move it below, or fold ConnectFlow.Connecting
# back into the early-return condition as a "tidy-up", and the watchdog becomes dead code in
# the one situation it is for -- a failed join with no dialog and no way back.
#
# Nothing else can see this: it compiles, it runs, and the only symptom needs a world that
# fails to join. It is the same shape as the bug that hung the mod picker -- a branch the
# common path never reaches.
# ---------------------------------------------------------------------------

say ""
say "== the Connecting watchdog is reachable =="

update_body=$(sed -n '/private void Update()/,/^        }/p' ConnectDialog.cs)
watchdog_line=$(printf '%s\n' "$update_body" | grep -n 'ConnectFlow.NoticeMainMenu' | head -1 | cut -d: -f1)
return_line=$(printf '%s\n' "$update_body" | grep -n 'if (_shown || _closedByPlayer)' | head -1 | cut -d: -f1)

if [ -z "$watchdog_line" ]; then
	bad "watchdog" "ConnectDialog.Update no longer calls ConnectFlow.NoticeMainMenu -- a failed join leaves Connecting true forever and the dialog never comes back"
elif [ -z "$return_line" ]; then
	bad "watchdog" "could not find Update's early return -- this check cannot tell whether the watchdog is reachable, so treat it as failing"
elif [ "$watchdog_line" -lt "$return_line" ]; then
	pass "the watchdog runs before Update's early return"
else
	bad "watchdog" "ConnectFlow.NoticeMainMenu is called AFTER the early return, so it never runs once a join has started -- exactly the case it exists for"
fi

# And Connecting must not be back in that early-return condition, which would return before
# the watchdog regardless of where the call sits.
if printf '%s\n' "$update_body" | grep -q 'if (_shown || _closedByPlayer || ConnectFlow.Connecting)'; then
	bad "watchdog" "ConnectFlow.Connecting is back in Update's early-return condition -- the watchdog is unreachable again"
else
	pass "Connecting is not folded back into the early-return condition"
fi

# ---------------------------------------------------------------------------
# A disconnect is not a failure.
#
# Nothing cleared Connecting on success, so after a good join it stayed true all session and the
# watchdog reported the player's own deliberate logout as "could not connect". The fix is one
# piece of state -- did this attempt ever actually land -- and these checks pin the two halves
# that make it work. Both are inside NoticeMainMenu, so they are read from that method's body
# rather than the whole file: a mention anywhere else would pass while the watchdog still lied.
# ---------------------------------------------------------------------------

say ""
say "== a disconnect is not reported as a failure =="

# Decide(), not NoticeMainMenu(): the decision was split out of it so a harness could run it
# without Unity's clock (Time.realtimeSinceStartup is an extern and throws out of process).
# NoticeMainMenu is now a one-line wrapper, so scanning it left this whole block blind -- which
# it reported as a FAILURE rather than a pass, which is the only reason the move was noticed.
watchdog_body=$(sed -n '/internal static bool Decide(bool mainMenuActive/,/^        }/p' ConnectFlow.cs)

# THE LIFETIME CHECK. This is the one that matters, and its absence is why the first fix
# shipped doing nothing.
#
# The join can only be observed while the player is IN the world. ConnectDialog lives on
# FejdStartup's GameObject, which Valheim destroys on the way into a world, so anything placed
# in ConnectDialog.Update is dead during exactly that window. The previous version of this test
# checked that the code was PRESENT and passed happily while it was unreachable -- present is
# not the same as able to run.
#
# So: the observer must be on an object that survives the scene load, and DontDestroyOnLoad is
# what makes that true. Without it this is the same bug with more files.
# Comment lines are stripped before matching. The first version of this check grepped the whole
# file for "DontDestroyOnLoad", which appears in the long comment above the call explaining why
# it is there -- so deleting the actual CALL still passed. Verified by deleting it: 68/0. A
# marker that matches its own documentation asserts nothing.
sentry_code=$(grep -v '^[[:space:]]*//' JoinSentry.cs 2>/dev/null)

if [ ! -f JoinSentry.cs ]; then
	bad "disconnect" "JoinSentry.cs is gone -- nothing observes the join landing from an object that outlives the main menu"
elif ! printf '%s\n' "$sentry_code" | grep -q 'DontDestroyOnLoad('; then
	bad "disconnect" "JoinSentry does not call DontDestroyOnLoad -- its object dies with the main menu, so it cannot see the player reach the world and every disconnect is reported as a failed connection"
elif ! printf '%s\n' "$sentry_code" | sed -n '/private void Update()/,/^        }/p' | grep -q 'Player.m_localPlayer'; then
	bad "disconnect" "JoinSentry.Update does not read Player.m_localPlayer -- it is installed but observes nothing"
elif ! printf '%s\n' "$sentry_code" | sed -n '/private void Update()/,/^        }/p' | grep -q 'ConnectFlow.NoticeJoined'; then
	bad "disconnect" "JoinSentry sees the player but never calls ConnectFlow.NoticeJoined -- the observation goes nowhere"
else
	pass "JoinSentry observes the join from a DontDestroyOnLoad object and reports it"
fi

# Installed from plugin load, NOT from a main-menu patch -- installing it from the menu would
# give it the same doomed lifetime as the thing it replaces.
if sed -n '/private void Awake()/,/^        }/p' Main.cs | grep -q 'JoinSentry.Install'; then
	pass "the sentry is installed at plugin load, so it exists before any join"
else
	bad "disconnect" "Main.Awake does not install JoinSentry -- nothing creates the observer"
fi

# NEGATIVE: the dead check must not come back. It reads as a belt-and-braces guard and is in
# fact unreachable at the moment it would matter.
if printf '%s\n' "$watchdog_body" | grep -q 'Player.m_localPlayer'; then
	bad "disconnect" "NoticeMainMenu reads Player.m_localPlayer again -- that method cannot run while the player is in the world, so this check can only ever be false and reads as protection that is not there"
else
	pass "Decide does not try to observe the join itself (it cannot run then)"
fi

# The success branch must come BEFORE the Fail call, or a successful join still falls through
# into the failure path and the bug is back with an extra unused variable.
joined_line=$(printf '%s\n' "$watchdog_body" | grep -n 'if (_joined)' | head -1 | cut -d: -f1)
fail_line=$(printf '%s\n' "$watchdog_body" | grep -n 'Fail("Could not connect' | head -1 | cut -d: -f1)

if [ -z "$joined_line" ] || [ -z "$fail_line" ]; then
	bad "disconnect" "could not find both the _joined branch and the Fail call in Decide -- this check cannot tell whether a disconnect still reports a failure, so treat it as failing"
elif [ "$joined_line" -lt "$fail_line" ]; then
	pass "the successful-join branch is checked before the failure path"
else
	bad "disconnect" "the _joined branch sits AFTER Fail, so a disconnect still falls into the failure path"
fi

# Per attempt, not per session: left set, a genuinely failed retry would be silently swallowed.
if sed -n '/public static void Begin(/,/^        }/p' ConnectFlow.cs | grep -q '_joined = false'; then
	pass "_joined is reset for each attempt in Begin"
else
	bad "disconnect" "Begin does not reset _joined -- after one successful join, a later join that really fails is reported as a disconnect and the player is told nothing"
fi

# ---------------------------------------------------------------------------
# Failure-notice length.
#
# renderDialog checks the dialog layout against an 80-character notice, which is a deliberate
# upper bound rather than a copy of any one message. That bound is only meaningful if the real
# messages stay under it: a longer one wraps to a third line, and in list mode the body only has
# five. Nothing else would notice -- the string compiles, the dialog renders, and the overflow
# only shows on a world that happens to fail to connect.
#
# 70 for the TEMPLATE, because {payload.Host} expands at runtime and a real hostname is longer
# than the token. 70 + a 20-character host lands inside 80.
# ---------------------------------------------------------------------------

say ""
say "== failure notices fit the dialog =="

longest=$(grep -oE 'Fail\(\$?"[^"]+"' ConnectFlow.cs | sed 's/Fail(\$\?"//; s/"$//' | awk '{ print length($0) }' | sort -rn | head -1)

if [ -z "$longest" ]; then
	bad "failure notices" "no Fail() messages found in ConnectFlow.cs -- either they were renamed or this check is now blind"
elif [ "$longest" -le 70 ]; then
	pass "longest failure notice template is $longest chars (cap 70)"
else
	bad "failure notices" "a template is $longest chars; over 70 it wraps to a third line and overflows the body in list mode. Shorten it -- do not raise the cap."
fi

# LastNotice goes through the SAME Clip(…, FailureLineBudgetChars) as a failure, but it is not
# a Fail() call, so the check above is blind to it. Mine was 93 characters when first written
# and would have rendered as 55 plus an ellipsis -- cut off mid-word, in the one line whose job
# is to tell the player what to do next.
#
# Capped at the budget itself (56), not at 70: a notice has no {token} to expand at runtime, so
# what is in the source is what gets clipped. The budget is read out of ConnectDialog.cs rather
# than written here twice -- a second copy is how a cap drifts away from the thing it caps.
budget=$(grep -oE 'FailureLineBudgetChars = [0-9]+' ../ConnectDialog.cs 2>/dev/null \
         || grep -oE 'FailureLineBudgetChars = [0-9]+' ConnectDialog.cs)
budget=${budget##* }
longestNotice=$(grep -oE 'LastNotice = "[^"]+"' ConnectFlow.cs | sed 's/LastNotice = "//; s/"$//' \
                | awk '{ print length($0) }' | sort -rn | head -1)

if [ -z "$budget" ]; then
	bad "cancel notices" "could not read FailureLineBudgetChars from ConnectDialog.cs -- this check is now blind"
elif [ -z "$longestNotice" ]; then
	pass "no LastNotice assignments to measure"
elif [ "$longestNotice" -le "$budget" ]; then
	pass "longest cancel notice is $longestNotice chars (budget $budget)"
else
	bad "cancel notices" "a notice is $longestNotice chars against a budget of $budget; Clip() will cut it mid-word with an ellipsis. Shorten the string."
fi

say ""
say "== $checked checks, $fail failed =="
[ "$fail" -eq 0 ] || exit 1
exit 0
