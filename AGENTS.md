# For AI coding assistants

Read [`CONTRIBUTING.md`](CONTRIBUTING.md) first and follow it. It is short and complete.

Then read [`CLAUDE.md`](CLAUDE.md) for the hard rules. The rules are the same for every
assistant: Claude Code, Codex, Cursor, Copilot or anything else.

Three things that matter most:

- One change per pull request, with a regression test for behavior changes; docs-only changes need link and command checks.
- For code changes, build and run the tests documented in CONTRIBUTING.md; report missing game references and live tests explicitly.
- Do not bump the version, commit anything from `libs/`, create tags or swap the DLL bundled in
  the server. Releases are the maintainer's job.
