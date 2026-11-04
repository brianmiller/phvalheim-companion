<!-- Read CONTRIBUTING.md if you have not. This checklist is the whole review bar. -->

## What changes for a player or a server

<!-- One or two plain sentences. This is copied into the release notes. -->

## Checklist

- [ ] One change. A fix and a refactor are two PRs.
- [ ] `dotnet build -c Release` succeeds.
- [ ] `bash dev_tools/run-tests.sh` passes locally, including `test-publicizer-trap.sh`.
- [ ] Behavior change: regression test fails without the fix. Docs-only: links and changed commands checked (mark other checks N/A with a reason).
- [ ] New `.cs` files are listed in `PhValheimCompanion.csproj`.
- [ ] No `<Version>` bump. Nothing from `libs/` in the diff.
- [ ] Every game API I call was confirmed with `dev_tools/apiProbe`.

## What I ran in a real game

<!-- Which side (server, client, both), what you did, what you saw. -->

## What I did NOT test

<!-- Say it plainly. None of the tests run the game. -->

## Related changes and evidence

<!-- Related PRs and tested commit IDs; attach sanitized test results. Distinguish automated, platform, and live-game checks. -->
