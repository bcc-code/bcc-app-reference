# 0010. Frontend type checking and tests

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0011.

## Context

`vite build` does not type-check, so a build can pass with type errors. Logic hidden inside components is hard to test.

## Decision

- TypeScript is strict (`@vue/tsconfig`) with `noUncheckedIndexedAccess`. Components use `<script setup lang="ts">`.
- `vue-tsc --build` runs before `vite build` and in CI.
- Frontend logic is tested with **vitest**, with the test next to the source (`foo.ts` and `foo.test.ts`). Logic that is not rendering lives in plain `.ts` modules so it can be tested without mounting components.
- Accessibility is checked with axe in the E2E tests ([0017](0017-integration-and-e2e-tests.md)).

## Consequences

| | |
|---|---|
| Positive | Type errors fail the build; indexing returns `T \| undefined`, which catches a common class of runtime errors; tests are fast and local to the code. |
| Negative | Strict typing and `noUncheckedIndexedAccess` add some friction when writing code. |
| Follow-up | `vue-tsc` and vitest run in the `frontend` CI job. |
