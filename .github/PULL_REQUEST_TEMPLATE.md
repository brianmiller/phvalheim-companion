<!-- Read CONTRIBUTING.md if you have not. Brian must approve the reviewed revision before merge. -->

## What changes for a player or a server

<!-- One or two plain sentences. This is copied into the release notes. -->

## Checklist

- [ ] Dynamic pages and status content reviewed for needed changes, even for code-only/docs-only work; paths, updates or reasons for no changes, and validation are recorded below.

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

## Documentation and status review (required for every PR)

<!-- Review landing-page/website content, portal/admin guidance, download/install pages
and fallbacks, README/version references, contributor guides, GitHub Issues for work
status, including affected content in other repositories. Review is always required;
edit only what needs changing. Do not leave this blank or write unexplained N/A. -->

- Affected GitHub issues reviewed (links, acceptance criteria, status/evidence or reason none apply):
- Pages/files reviewed:
- Updates made, or specific reasons no changes are needed:
- Validation and any unverified checks:
- Related documentation PRs or follow-ups (owner and release/deployment dependency):

<!-- GitHub Issues is the source of truth for work tracking; do not maintain Markdown task lists.
Keep implemented, merged, released, and deployed status distinct. Do not advertise
unreleased behavior as available or claim an issue is complete without evidence. -->

## Brian's merge approval

Pending Brian's review. Green checks and this template do not authorize merging.
Any changes after approval require Brian to approve the new revision.

<!-- Do not claim approval on Brian's behalf. Link his actual review when available.
For PRs opened under Brian's account, follow CONTRIBUTING.md's self-approval procedure. -->
