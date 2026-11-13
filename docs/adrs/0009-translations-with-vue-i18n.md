# 0009. Translations with vue-i18n

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0010.

## Context

BCC apps are used in several languages. Hard-coded strings are expensive to find later, and translation files decay: keys go missing in one language, or stay after the code that used them is gone.

## Decision

- Every user-facing string goes through **vue-i18n** (Composition API, `legacy: false`). Dates and numbers are formatted through it too.
- Translations are JSON files in `frontend/src/i18n/`, one per locale. We start with English (`en`, also the fallback) and Norwegian (`no`) and add languages when users need them.
- Keys are stable and descriptive, grouped by feature: `notes.title`, not `text1`.
- A test fails on keys that are missing in any locale and on keys no code uses (standard 21.12).
- The backend answers validation failures with i18n keys (`errors.*`) in ProblemDetails, never with translated text. The same JSON files are the single source for both sides, and a test checks that every key the backend uses exists in every locale.

## Consequences

| | |
|---|---|
| Positive | Adding a language means adding a JSON file; missing and dead keys are caught by a test. |
| Negative | Every string needs a key, also in prototypes. |
| Follow-up | The completeness test is `frontend/src/i18n/i18n.test.ts`. An application decides its list of languages and the default language with its users. |
