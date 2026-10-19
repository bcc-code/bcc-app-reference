# ADR 0006: No Slopwatch in the template

- Status: Accepted
- Date: 2026-10-19

## Context

Standards 24 (rule 6) and 25 (rule 9) require Slopwatch in CI with `--fail-on warning` and an empty baseline. It reports
shortcuts such as disabled tests, suppressed warnings and empty catch blocks. The first version of the template ran it in the
`backend` job of `pr.yml` and pinned it in `.config/dotnet-tools.json`.

## Decision

The template does not use Slopwatch. It is removed from the tool manifest, the CI workflow, the agent settings and the docs,
and `.slopwatch/` is deleted. This deviates from standards 24.6 and 25.9 (and the Slopwatch mentions in 01.13 and 05).

The shortcuts it looked for are still covered in part by:

- `TreatWarningsAsErrors` for every project, with every exception commented (`Directory.Build.props`).
- The architecture tests and startup assertions (standard 25).
- The rule in `AGENTS.md` that a test is never deleted, skipped or weakened to make CI green, and review of every diff.

## Consequences

- Disabled tests, `#pragma warning disable` and empty catch blocks are no longer caught automatically; reviewers and the
  `standards-reviewer` agent have to look for them.
- An app created from the template can add Slopwatch back: pin `slopwatch.cmd` in `.config/dotnet-tools.json`, add an empty
  `.slopwatch/baseline.json`, and run `dotnet tool run slopwatch analyze -d . --fail-on warning` in the `backend` job.
