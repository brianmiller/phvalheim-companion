# PhValheim Companion

The mod PhValheim ships with every world. It is **dual-role**: the same DLL loads on the
dedicated server and on the player's client, and each patch decides which side it is on at
runtime.

As of phvalheim-server **2.53** this mod is **bundled inside the phvalheim-server image** and
is **not published to Thunderstore or Hexium**. There is no catalogue entry, no `manifest.json`
and no icon, because nothing resolves it as a dependency any more — the server installs it
directly. That is why the version-skew machinery that used to compare a catalogue version
against a required minimum is gone.

## What it does

**Connecting to the world you clicked Launch! on** (client side, 2.53+)

The PhValheim client passes the Launch! link's payload to Valheim as
`--phvalheim-launch <base64>` on the command line. When that argument is present the Companion
shows a welcome dialog on the main menu naming the world and the mods installed on *this*
machine, with **Connect** and **Close**.

Connect hands the join to Valheim's own `ProceedJoinRequest`, which queues the server and
opens character selection; once a character is chosen Valheim connects on its own, by
`IP:PORT` or by resolving the crossplay join code. Close drops to the normal menu and leaves a
small button to bring the dialog back.

No `--phvalheim-launch` argument means the game was not started by the PhValheim client, so
nothing is shown at all. The payload's presence *is* the gate — there is no separate check to
drift out of step with it.

Two deliberate choices worth knowing:

- **The world details come from the link, not from a file.** A crossplay world reissues its
  join code every restart, so a cached copy on disk goes stale with no symptom except a failed
  join. The link is regenerated on every click.
- **The mod list is read from `BepInEx/plugins/`**, not from what the server believes it sent.
  If the client's sync half-finished, the directory is the version that matters.

**Hung boss heads** (client side)

Hanging a boss trophy on an item stand reports the event to the PhValheim backend, which is how
the admin UI knows a world's bosses have been placed. Hanging a trophy is a client-side act,
so this runs when `ZNet` says we are not the server.

## Side detection

There is **no `[BepInProcess]` attribute** on this plugin, and that is deliberate.
`[BepInProcess("valheim_server.x86_64")]` does not match the real process name, so it silently
prevents the plugin loading server-side at all. That is what broke `phvalheim-tickmonitor`.

Which side a patch is on is decided at runtime from `ZNet`, once `ZNet` exists — never from an
attribute and never in `Awake()`.

## Building

```bash
./refresh-libs.sh                      # populate ./libs from a Valheim install
dotnet build PhValheimCompanion.csproj  # -> bin/Release/net472/PhValheimCompanion.dll
```

`libs/` is gitignored and must stay that way: it holds Valheim and Unity assemblies, which are
not ours to redistribute.

The compile set in the `.csproj` is **explicit, not globbed** (`EnableDefaultCompileItems` is
false). A new source file has to be added to it or it is silently left out of the build.

## Testing: the publicizer trap

This project compiles against `libs/assembly_valheim_publicized.dll`, in which every member is
public. The assembly the game actually loads is not. So **a direct call to a Valheim private
builds with zero warnings and throws `MissingMethodException` or `MissingFieldException` the
first time a player clicks the button.** A green build proves nothing here.

```bash
PHVALHEIM_REALREFS=/path/to/real/refs ./dev_tools/test-publicizer-trap.sh
```

It needs a real, **un-publicized** `assembly_valheim.dll` from a **client** install — the
dedicated-server build contains no `FejdStartup` or `UnifiedPopup` at all, so checking against
it reports every menu member missing and looks like a disaster. Point
`PHVALHEIM_REALREFS` at a directory holding that DLL plus the contents of `libs/`. With no such
assembly the script exits **2**, not 0: an absent oracle is not a pass.

The script checks three things:

1. Every member called as ordinary C# is **public** in the real assembly.
2. Every member reached by reflection still **exists** — a rename fails just as hard.
3. The facts the connect flow is built on still hold: that `OnCharacterStart` reads
   `m_queuedJoinServer` and calls `JoinServer` itself, that `ProceedJoinRequest` both queues
   the join and opens character selection, and that `ShowYesNo` still maps `yesText` to the
   right-hand button. If any of those change, the flow breaks while every visibility check
   still passes.

It carries a control: it asserts the probe reports `FejdStartup.ProceedJoinRequest` as public
in the publicized assembly and private in the real one. If it cannot tell the two apart it is
not reading them, and it stops rather than reporting a pass.

This test is not theoretical. It was written after the connect dialog built clean, and it
immediately found three fields — `UnifiedPopup.instance`, `yesText` and `noText` — that would
have thrown the moment the dialog appeared.

## `dev_tools/apiProbe`

A small metadata and IL reader used to write code against what is in the binary rather than
what a wiki remembers.

```bash
dotnet run --project dev_tools/apiProbe -- libs FejdStartup.SelectCharacter   # signature
dotnet run --project dev_tools/apiProbe -- libs '?SetServerToJoin'            # which type owns it
dotnet run --project dev_tools/apiProbe -- libs '!FejdStartup.OnCharacterStart'  # what it calls
dotnet run --project dev_tools/apiProbe -- libs '@m_queuedJoinServer'         # who reads/writes it
dotnet run --project dev_tools/apiProbe -- /real/refs '=FejdStartup.ProceedJoinRequest'  # visibility
```

The `?` and `@` modes exist because the design notes recorded API calls by name without the
type they hang off, and the `!` mode exists because reading `OnCharacterStart`'s IL is what
revealed that `SetServerToJoin` was the wrong door: it writes `m_joinServer`, which
`OnCharacterStart` then overwrites from `m_queuedJoinServer`. The player would have selected a
character and landed in the world list, with no error anywhere.

## Credits

The project was originally forked from Aaron Scherer's
[valheim-discord-notifier](https://github.com/aequasi/valheim-discord-notifier) — thank you,
@aequasi. Essentially none of that code remains: the Discord webhook, the ignored-username
list, the `messages.json` templates, the LitJson and Newtonsoft dependencies, the chat, player
and ZNet patches and the `ValheimEventHandler` have all been removed. What is left is
PhValheim's own.
