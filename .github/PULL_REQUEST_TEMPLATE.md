<!-- Read CONTRIBUTING.md if you have not. Brian must approve the reviewed revision before merge. -->

## What changes for a player or a server

<!-- One or two plain sentences. This is copied into the release notes. -->

## Checklist

- [ ] I understand that Brian Miller (`@brianmiller`) must approve the reviewed revision before merge; this checkbox is not approval.

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

## Brian's merge approval

Pending Brian's review. Green checks and this template do not authorize merging.
Any changes after approval require Brian to approve the new revision.

<!-- Do not claim approval on Brian's behalf. Link his actual review when available.
For PRs opened under Brian's account, follow CONTRIBUTING.md's self-approval procedure. -->
