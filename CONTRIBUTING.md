# Contributing to PhValheim Companion

Thanks for helping. Start here to choose the setup and checks for your change. It is written for
people and for AI coding assistants alike. If something here is wrong or missing, fixing this
page is itself a welcome contribution.

## The 30-second version

1. Fork the repo and branch from `main`.
2. For plugin code, prepare your own game references as described below, then build. Docs-only changes do not need game references.
3. Make one change. Run `dev_tools/test-*.sh`. Load the DLL in a real game and try it.
4. Open a pull request. Fill in the checklist the template gives you.
5. The maintainer reviews, merges, and bundles the new DLL into the next server release.

## Which repo?

PhValheim is three repositories. Pick the right one before you start.

| You want to change | Repo |
| --- | --- |
| The in-game mod shipped with every world (join flow, player roster, panels) | **phvalheim-companion** (this one) |
| The server container: admin UI, public UI, engine, mod catalogue, docs website | [phvalheim-server](https://github.com/brianmiller/phvalheim-server) |
| The launcher players install on Windows, Linux or macOS | [phvalheim-client](https://github.com/brianmiller/phvalheim-client) |

## What the Companion is

A BepInEx plugin, one DLL, that loads on **both** the dedicated server and the player's game.
Each patch decides at runtime which side it is on. It is not published to any mod store: the
server image bundles it at `container/games/valheim/custom_plugins/PhValheimCompanion/` and
installs it into every world. The README explains what it does and how side detection works.

## Set up

You need the .NET SDK (8 or newer), the .NET 8 runtime for the test harnesses, Git and Bash. Building the plugin also requires references from your own modded Valheim installation.

```bash
git clone git@github.com:<you>/phvalheim-companion.git
cd phvalheim-companion
PHVALHEIM_REFS_DIR=/path/to/prepared-references ./refresh-libs.sh
dotnet build -c Release           # -> bin/Release/net472/PhValheimCompanion.dll
```

`libs/` is gitignored and must stay that way. It holds Iron Gate's and Unity's assemblies,
which are not ours to redistribute. `refresh-libs.sh` lists every required file and fails with the missing filenames.
It copies prepared references; it does not extract them from Docker or publicize them.
Prepare `assembly_valheim_publicized.dll` from your own matching `assembly_valheim.dll`
with your chosen assembly publicizer. Keep the original assembly separately for
`PHVALHEIM_REALREFS`, which the publicizer-trap test uses. Do not obtain these files from
an unverified download or commit them.

The decision and dialog tests run outside the game, but load the built plugin and
its reference assemblies. They still require a successful plugin build; there is no
reference-free runtime test path. Docs-only contributors can check links and commands
without building the plugin.

For full plugin validation, set `PHVALHEIM_REALREFS` to a directory with the original
`assembly_valheim.dll` and the supporting reference DLLs, then build and run the full suite.
Missing references or a skipped reachability check mean **unverified**, not a passing plugin build.

## Where things live

| Path | What it is |
| --- | --- |
| `Main.cs` | Plugin entry point. The `BepInPlugin` attribute reads the version from the csproj |
| `Patches/` | Harmony patches. Each one checks which side it is on before doing anything |
| `ConnectFlow.cs`, `JoinSentry.cs`, `ConnectDialog.cs` | The join flow: launch payload in, connected world out |
| `PlayerTelemetry.cs` | The server reporting its own player roster to PhValheim |
| `PanelTree.cs`, `PanelSkin.cs`, `Theme.cs`, `MenuButton.cs` | The in-game UI, built on Valheim's own uGUI prefabs |
| `dev_tools/` | Tests, the dialog renderer and `apiProbe` |
| `PhValheimCompanion.csproj` | The **only** place the version lives. Its compile list is explicit |

## Make a change

- **One change per pull request.**
- **Do not bump `<Version>`** in the csproj. The maintainer does that when a server release
  picks up the new DLL.
- **Add every new `.cs` file to the csproj.** The compile set is explicit, not globbed. A file
  you forget is silently left out and the build stays green.
- **Check the side.** A patch that runs on the server when it was meant for the client, or the
  other way round, is the most common Companion bug. See "Side detection" in the README.
- **Write code against the binary, not a wiki.** `dev_tools/apiProbe` answers "what is this
  method's signature", "which type owns it" and "what does it call". The README shows the modes.
- **Beware the publicizer.** You compile against a publicized assembly, so calling a private
  member compiles clean and throws the first time a player clicks. `test-publicizer-trap.sh`
  catches it, but only if you run it.

## Test it

For docs-only changes, verify links and any changed commands. No new runtime test or game install is required. For code changes, run the checks below and record unavailable checks explicitly.

```bash
bash dev_tools/run-tests.sh
```

The tests cover the join-outcome decision, the dialog layout and reachability, and the
publicizer trap. **None of them run the game.** For anything that touches the join flow, the UI
or telemetry, drop the built DLL into `BepInEx/plugins/PhValheimCompanion/` of a real client and
a real server, try it, and say in the PR what you saw. If you could not, say that instead.

For behavior changes, add a test as `dev_tools/test-<what-it-checks>.sh`:

- **It must be able to fail.** Break the code on purpose, watch the test go red, then fix it.
- **Assert on the outcome**, not on a summary line.
- **Pass the clock and the inputs in.** Decisions that read Unity statics directly cannot be
  tested outside the game. `ConnectFlow` is the pattern to copy.

## Open a pull request

The PR template is the checklist. In short:

- [ ] One change, described in plain words: what changes for a player or a server.
- [ ] `dotnet build -c Release` succeeds and every `dev_tools/test-*.sh` passes.
- [ ] For behavior changes, a test that fails without the change. For docs-only changes, check links and commands; no new runtime test is required.
- [ ] New source files are in the csproj.
- [ ] No version bump. Nothing from `libs/` committed.
- [ ] What you ran in a real game, on which side. Or "nothing", plainly.

Report suspected vulnerabilities privately using [SECURITY.md](SECURITY.md), before opening a public issue or PR.

## Rules that protect everyone

**This repository is public.** Never commit anything from `libs/`, and before every commit:

```bash
git diff --cached | grep -E '^\+' | grep -niE \
  'password|secret|api[_-]?key|token|bearer|([0-9]{1,3}\.){3}[0-9]{1,3}'
```

Never paste a world log into an issue or PR. They carry player SteamIDs.

**Releases are the maintainer's job.** The DLL reaches players inside the server image. Do not
create tags or GitHub releases, and do not open a server PR that swaps the bundled DLL. Say in
your PR that the change needs a server release and the maintainer takes it from there.

## Using an AI coding assistant

Claude Code reads `CLAUDE.md` automatically; Codex, Cursor and similar tools read `AGENTS.md`.
Both point here, so the assistant is working from the same rules you are. A prompt that works:

> Read CONTRIBUTING.md and follow it. Make this change: <describe it>. Use dev_tools/apiProbe
> to confirm every game API you call, add a test under dev_tools that fails without the
> change, build with `dotnet build -c Release`, run every `dev_tools/test-*.sh`, and tell me
> exactly what you did not test.

Review what it produces against the checklist above before you open the PR. You are the author.

## Where to ask

- [Discord](https://discord.gg/8RMMrJVQgy) for questions and design discussion.
- [GitHub issues](https://github.com/brianmiller/phvalheim-companion/issues) for bugs and
  feature requests.

PhValheim is MIT licensed. By contributing you agree your contribution is too.
