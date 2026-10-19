---
standard: 21 Frontend
assessed: 2026-10-19
commit: e4b2b86
---
# 21 Frontend — assessment

The Vue SPA follows the standard closely: strict TypeScript with `noUncheckedIndexedAccess`, TanStack Query with key
factories and lint rules at error, one tested fetch wrapper, feature folders, the BCC component library, and all checks in CI.
The gaps are small: lint warnings do not fail CI, the translation keys are cross-checked by a test rather than shared through a
generator, and two E2E flows skip the axe check.

**Score: Strong** · ✅ 17 · ⚠️ 3 · ❌ 0 · N/A 3

Commands run in `frontend/`: `pnpm run lint`, `format:check`, `type-check`, `knip`, `test` — all exit 0 (36 vitest tests pass).

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Components use `<script setup lang="ts">` | MUST | ✅ | grep over `src/**/*.vue` finds no `<script>` other than `setup lang="ts"`. |
| 2 | Strict TS via `@vue/tsconfig` with `noUncheckedIndexedAccess` | MUST | ✅ | `tsconfig.app.json` extends `@vue/tsconfig/tsconfig.dom.json`, sets `noUncheckedIndexedAccess: true`. |
| 3 | CI type-checks with `vue-tsc --build` | MUST | ✅ | `.github/workflows/pr.yml:121` (`type-check` = `vue-tsc --build`); `build` script also runs it first. |
| 4 | Server state via TanStack Query, `queryOptions`/key factories, query lint at error | MUST | ✅ | `src/features/notes/queries.ts:4-21` (key factory + `queryOptions`); `.oxlintrc.json` `tanstack-query/*` rules set to `error`. |
| 5 | No Pinia without shared client state | MUST | ✅ | Pinia is not a dependency (`package.json`). |
| 6 | API calls only through the generated Hey API client | MUST | ✅ | Features call `@/client` (`queries.ts:2`). Direct `fetch` only for the antiforgery token endpoint (`src/lib/logout.ts:20`, `src/components/UserMenu.vue:19`), which is not part of the API contract. |
| 7 | One fetch wrapper: CSRF on demand, retry on `X-Csrf-Refresh`, 401 → login, injectable | MUST | ✅ | `src/lib/templateFetch.ts:26-64`, factory with injected `fetch`/`cookies`/`redirect`/`location`; wired in `src/main.ts:13`; tested in `src/lib/templateFetch.test.ts`. |
| 8 | Runtime config from the server, no secrets in `.env` | MUST | ✅ | No `.env` file and no `import.meta.env` in `src/` or `vite.config.ts`; roles come from `/api/me` (`src/features/session/me.ts`). |
| 9 | Code organised by feature, mirroring backend slices | MUST | ✅ | `src/features/notes/`, `src/features/admin/`, `src/features/session/` match `Features/Notes`, `Admin`, `CurrentUser`. |
| 10 | Create and edit are separate components sharing form parts | SHOULD | ✅ | `NoteCreatePage.vue`, `NoteEditPage.vue`, shared `NoteForm.vue`. |
| 11 | BCC component library (PrimeVue) and Tailwind 4 with BCC tokens | MUST | ✅ | `@bcc-code/component-library-vue`, `tailwindcss ^4.3.3`; `src/style/main.css` imports `tailwindcss` and the BCC `theme.css`; components use `BccButton` and `--color-*` tokens. |
| 12 | All strings via vue-i18n; test fails on missing/unused keys; shared with backend generator | MUST | ⚠️ | `src/i18n/i18n.test.ts:71-102` checks missing keys per locale, unused keys and every `errors.*` key the API names. Sharing is by a test that scans `api/**/*.cs`, not by a backend source generator; no check catches hard-coded strings in templates. |
| 13 | Heavy optional SDKs loaded with dynamic `import()` | SHOULD | N/A | No heavy SDKs in `package.json`. Routes are lazy-loaded anyway (`src/router.ts:17-22`). |
| 14 | Embeddable widgets as custom elements, separate Vite config | MUST | N/A | No widgets in the template. |
| 15 | In-app help from markdown in `src/content/` | MAY | N/A | Not adopted. |
| 16 | Route meta guards for role-specific routes | MUST | ✅ | `src/router.ts:20` (`meta: { role: ADMIN_ROLE }`), guard at `:26-31`; `src/router.test.ts`; E2E `admin.spec.ts:13`. |
| 17 | No unsanitised `v-html` | MUST | ✅ | No `v-html` in `src/`. |
| 18 | Semantic, keyboard-operable interactive elements | MUST | ✅ | `UserMenu.vue:27-36` uses `<button>` with `aria-expanded`/`aria-controls`, Escape closes (`:27`); lists use `RouterLink` and `BccButton`; skip link in `App.vue`. No clickable `<div>`. |
| 19 | Accessibility checked with axe in E2E flows | MUST | ⚠️ | `e2e/support.ts:24-32` (WCAG 2.1 A/AA), called in 4 of 6 tests. `notes.spec.ts:69` (not-found page for another user) and `admin.spec.ts:21` (open user menu) never call `expectAccessible`, although `AGENTS.md:37` says "axe in every flow". |
| 20 | oxlint (correctness error, complexity ≤ 10, max-props), ESLint + `eslint-plugin-oxlint`, oxfmt, knip | MUST | ✅ | `.oxlintrc.json` (`correctness: error`, `eslint/complexity` max 10, `vue/max-props` 8); `eslint.config.ts:26` `buildFromOxlintConfigFile`; `.oxfmtrc.json`; `knip.config.ts`. |
| 21 | CI runs lint, format, knip, `vue-tsc` in check mode and fails on findings | MUST | ⚠️ | `pr.yml:119-123`, no `--fix`. But `.oxlintrc.json` sets `suspicious: warn` and several rules to `warn`, and neither `oxlint` nor `eslint` is run with `--deny-warnings`/`--max-warnings 0`, so warning-level findings pass CI. |
| 22 | Frontend logic has vitest tests next to the source | MUST | ✅ | `templateFetch.test.ts`, `logout.test.ts`, `problems.test.ts`, `me.test.ts`, `NoteForm.test.ts`, `router.test.ts`, `layerOrder.test.ts`, `i18n/*.test.ts`. |
| 23 | pnpm pinned exactly, with the supply-chain block | MUST | ✅ | `package.json` `devEngines.packageManager.version: 12.11.2`, `onFail: error`; `pnpm-workspace.yaml` sets `minimumReleaseAge`, `trustPolicy`, `blockExoticSubdeps`, `strictDepBuilds`. |

## Gaps to close

- **21**: Make warnings fail CI: run `oxlint --deny-warnings` and `eslint --max-warnings 0` in the `lint` script of
  `frontend/package.json`, or raise the `warn` rules in `.oxlintrc.json` to `error`.
- **19**: Call `expectAccessible` in `frontend/e2e/notes.spec.ts` ("another user does not see the note", after the not-found
  alert) and `frontend/e2e/admin.spec.ts` ("signing out", with the user menu open).
- **12**: Either share the translation JSON with the backend through a source generator (standard 08) or record in an ADR that
  the `i18n.test.ts` cross-check replaces it. Consider an ESLint rule (`@intlify/vue-i18n/no-raw-text`) to catch hard-coded
  template strings.

## Not verifiable in the repo

- **11**: whether the BCC design tokens used match the current design system version; check with the design system owners.
