# CLAUDE.md

**Merge approval is mandatory:** Brian Miller (`@brianmiller`) must explicitly approve
any change before it enters `main` or `master`. Prepare and test a PR, then wait.
Implementation requests, green CI, another reviewer's approval, and access to Brian's
credentials are not merge authorization. New changes require renewed approval.
Do not merge, enable auto-merge, push directly, or bypass protections without his
explicit authorization. Never submit an approval as Brian or impersonate his review.
See [the approval procedure](CONTRIBUTING.md#brian-must-approve-every-merge).

**Dynamic-page review is mandatory for every contribution**, including code-only and
docs-only work. Review the landing page, portal guidance, download pages/fallbacks,
backlog/status files, and related docs for needed changes. Update affected content;
record reviewed paths and explain when no update is needed. Never assume automatic
refresh keeps descriptions or status correct. Follow the
[documentation/status review](CONTRIBUTING.md#always-review-dynamic-pages-and-status-content).

Guidance for Claude Code when working in this repository.

**Contributing a change? Read [`CONTRIBUTING.md`](CONTRIBUTING.md) first and follow it.** It is
the short, complete procedure: set up, change, test, open a PR.

## What this is

The PhValheim Companion: one BepInEx plugin DLL that loads on both the dedicated server and the
player's game. Each Harmony patch decides at runtime which side it is on. It is not on any mod
store; the phvalheim-server image bundles it and installs it into every world. `README.md`
covers what it does and how side detection works.

## Hard rules

- `./refresh-libs.sh` fills `libs/`. `libs/` is gitignored and nothing in it is ever committed.
- `<Version>` in `PhValheimCompanion.csproj` is the only version. `Main.cs` reads it.
- The csproj compile list is explicit. A new `.cs` file must be added to it or it is silently
  left out of the build.
- You compile against a publicized assembly. A call to a member that is really private
  compiles clean and throws in the game. Run `dev_tools/test-publicizer-trap.sh`.
- Confirm every game API with `dev_tools/apiProbe` before you call it. Do not trust a wiki or a
  design note for a signature.
- Back from character select is a cancel, not a failed connection. `ProceedJoinRequest` only
  queues the join. Decisions that read Unity statics directly cannot be tested; pass the clock
  and the inputs in, as `ConnectFlow` does.
- The launch payload never carries the password on disk. The world manifest the server writes
  to `BepInEx/plugins/PhValheimCompanion/phvalheim-world.cfg` names the world and nothing more.

## Tests

```bash
bash dev_tools/run-tests.sh
```

None of them run the game. Say so when you hand over anything touching the join flow, the UI
or telemetry.
