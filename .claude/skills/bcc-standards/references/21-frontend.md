# 21 Frontend

This standard covers the Vue SPA that our .NET backends serve as a BFF: language settings, server state, the API client, structure, styling, i18n, accessibility, linting and tests. The rules below close the gaps that let defects through (no type check in the build, lint that auto-fixes instead of failing, missing tests and accessibility checks).

## Rules

### Language and types

1. **Components MUST use `<script setup lang="ts">`.** Options API and untyped components are not allowed in new code.
2. **TypeScript MUST be strict (`@vue/tsconfig`) with `noUncheckedIndexedAccess`.** Indexing an array or record then returns `T | undefined`, which catches a common class of runtime errors.
3. **CI MUST type-check with `vue-tsc --build`.** `vite build` does not type-check, so a build can pass with type errors.

### Server state and API access

4. **Server state MUST go through TanStack Query, with `queryOptions` or key factories per module and `@tanstack/eslint-plugin-query` rules set to error.** One key per resource lets mutations invalidate the right queries; the lint rules catch missing dependencies in keys.
5. **Pinia MUST NOT be used unless there is client state that is shared across unrelated components and is not server state.**
6. **API calls MUST use the generated Hey API client only.** No hand-written `fetch` calls to our own API. See [08 Code Generation and Contracts](08-code-generation-and-contracts.md).
7. **All API traffic MUST pass through one fetch wrapper that handles the CSRF token (fetch on demand, retry once on `X-Csrf-Refresh`) and sends the user to login on 401.** Write it as a factory with injected dependencies (`fetch`, cookies, redirect, location) so it can be unit-tested. See [14 Authentication and Sessions](14-authentication-and-sessions.md) and [16 HTTP Security Hardening](16-http-security-hardening.md).
8. **Runtime configuration MUST come from the server (bootstrap data inlined in `index.html` or a config endpoint), never from secrets in `.env` files.** Anything in a Vite `.env` file ends up in the bundle. A `.env` may hold only a base URL or build mode.

### Structure and components

9. **Code MUST be organised by feature or area, mirroring the backend slices** (`admin/modules/<feature>/` with its routes, views and composables). Logic that is not rendering SHOULD live in plain `.ts` modules next to the component, where it can be tested.
10. **Create and edit SHOULD be separate components that share form parts.** One component with `isEdit` branches grows conditionals for loading, defaults and validation. See Separate Create and Edit Components.
11. **UI MUST use the BCC component library (PrimeVue with the BCC theme) and Tailwind 4 with the BCC design tokens.** No other component library.
12. **Every user-facing string MUST go through vue-i18n, and a test MUST fail on keys missing in any locale and on unused keys.** Share the translation JSON with the backend source generator ([08 Code Generation and Contracts](08-code-generation-and-contracts.md)).
13. **Heavy optional SDKs (Firebase, charting, editors) SHOULD be loaded with dynamic `import()`** so they stay out of the main bundle.
14. **Embeddable widgets MUST be custom elements built with a separate Vite config, with no `<style>` blocks in SFCs** (import CSS with `?inline`), so styles work inside a shadow root.
15. **In-app help (user manual, "what's new") MAY be rendered from markdown in `src/content/`.**

### Routing

16. **Route-level auth meta is optional for routes any logged-in user may see.** The server requires login before it serves `index.html`, so the SPA never loads unauthenticated. This deviates from Vue Router, which asks for `meta` + navigation guards. Meta guards are still REQUIRED for role-specific routes (admin pages), so a user without the role gets a clear page instead of a page full of 403 errors. The server remains the authority.

### Security and accessibility

17. **`v-html` MUST NOT be used on user-provided content without sanitization.** See [16 HTTP Security Hardening](16-http-security-hardening.md) and HTML Sanitization.
18. **Interactive elements MUST be semantic (`<button>`, `<a>`, form controls), never clickable `<div>`s, and MUST be keyboard operable.** Use roving focus for composite widgets (lists of options, toolbars).
19. **Accessibility MUST be checked with axe in E2E flows** as described in [19 Testing Strategy](19-testing-strategy.md).

### Tooling and tests

20. **Linting and formatting MUST use oxlint (correctness = error, `complexity` ≤ 10, `max-props`), ESLint for Vue/TS rules with `eslint-plugin-oxlint` to avoid duplicate rules, oxfmt and knip.** See Oxlint, Oxfmt.
21. **CI MUST run lint, format, knip and `vue-tsc` in check mode and fail on findings.** `--fix` in CI and auto-commit bots hide problems from the author and let unfixable ones pass. Auto-fix belongs in the editor or a pre-commit hook ([25 Code Quality Guards](25-code-quality-guards.md)).
22. **Frontend logic MUST have vitest tests next to the source** (`foo.ts` + `foo.test.ts`).
23. **pnpm MUST be pinned to an exact version with the supply-chain block** (`minimumReleaseAge`, `trustPolicy`, `blockExoticSubdeps`, `strictDepBuilds`). See [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).

## Example

`frontend/` (Vue 3, TypeScript strict, PrimeVue, Tailwind, TanStack Query, Hey API client, vue-i18n, pnpm). The Notes pages mirror `Features/Notes/` in the API.
