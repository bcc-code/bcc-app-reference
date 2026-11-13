# 0007. Frontend linting with oxlint, ESLint and knip

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0008.

## Context

Without a linter, common bugs (unused variables, missing query-key dependencies, overly complex components) reach review or production. The BCC standards (21.20, 21.21) require a fixed lint setup that fails CI instead of quietly fixing code.

## Decision

- **oxlint** is the main linter for `.ts` and `.vue` files. The `correctness` category is an error, and `complexity` is capped at 10. The TanStack Query rules (`@tanstack/eslint-plugin-query`) run in oxlint as a JS plugin, with its rules as errors.
- **ESLint** runs only for the Vue and TypeScript rules oxlint does not cover (`eslint-plugin-vue`, `typescript-eslint`). `eslint-plugin-oxlint` turns off ESLint rules that oxlint already checks, so nothing is reported twice.
- **knip** reports unused files, exports and dependencies.
- The generated client (`src/client/`) is excluded from ESLint and knip, with a comment in the config saying why.
- CI runs all three in check mode and fails on any finding. `--fix` runs only in the editor or a local hook, never in CI.

## Consequences

| | |
|---|---|
| Positive | Fast linting (oxlint is Rust-based); the same rule set as the other BCC apps; dead code and unused packages are caught. |
| Negative | Three tools and their configs to keep current. A new rule can break CI after a version bump. |
| Follow-up | The checks run in the `frontend` CI job. A lefthook pre-commit hook is optional (standard 25.8); CI stays the gate. |

## Alternatives considered

- **ESLint only** — slower, and the standards name oxlint as the default.
- **Biome** — combines linting and formatting, but has weaker Vue SFC support, and the other BCC apps do not use it.
