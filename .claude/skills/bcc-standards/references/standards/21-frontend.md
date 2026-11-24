# 21 Frontend

This standard covers the Vue SPA that our .NET backends serve as a BFF: language settings, server state, the API client, structure, styling, i18n, accessibility, linting and tests. The rules close the gaps that let defects through: no type check in the build, lint that auto-fixes instead of failing, missing tests and missing accessibility checks. The tools themselves are described in [Frontend (Vue)](../technology/frontend-vue.md).

## Rules

### Language and types

1. **Components MUST use `<script setup lang="ts">`.** Options API and untyped components are not allowed in new code.
2. **TypeScript MUST be strict (`@vue/tsconfig`) with `noUncheckedIndexedAccess`.** Indexing an array or record then returns `T | undefined`, which catches a common class of runtime errors.
3. **CI MUST type-check with `vue-tsc --build`.** `vite build` does not type-check, so a build can pass with type errors.

### Server state and API access

4. **Server state MUST go through TanStack Query, with `queryOptions` or key factories per module and `@tanstack/eslint-plugin-query` rules set to error.** One key per resource lets mutations invalidate the right queries; the lint rules catch missing dependencies in keys.
5. **Pinia MUST NOT be used unless there is client state that is shared across unrelated components and is not server state.**
6. **API calls MUST use the generated Hey API client only.** No hand-written `fetch` calls to our own API. See [08 Code Generation and Contracts](08-code-generation-and-contracts.md).
7. **All API traffic MUST pass through one fetch wrapper that sends the user to login on 401.** CSRF needs nothing in the frontend: the server refuses requests from other origins ([16](16-http-security-hardening.md) rule 9). Write it as a factory with injected dependencies (`fetch`, redirect, location) so it can be unit-tested. See [14 Authentication and Sessions](14-authentication-and-sessions.md) and [16 HTTP Security Hardening](16-http-security-hardening.md).
8. **Runtime configuration MUST come from the server (bootstrap data inlined in `index.html` or a config endpoint), never from secrets in `.env` files.** Anything in a Vite `.env` file ends up in the bundle. A `.env` may hold only a base URL or build mode.

### Structure and components

9. **Code MUST be organised by feature or area, mirroring the backend slices** (`admin/modules/<feature>/` with its routes, views and composables). Logic that is not rendering SHOULD live in plain `.ts` modules next to the component, where it can be tested.
10. **Create and edit SHOULD be separate components that share form parts.** One component with `isEdit` branches grows conditionals for loading, defaults and validation. See [Separate Create and Edit Components](../technology/frontend-vue.md#separate-create-and-edit-components).
11. **UI MUST use the BCC component library (PrimeVue with the BCC theme) and Tailwind 4 with the BCC design tokens.** No other component library.
12. **Every user-facing string MUST go through vue-i18n, and a test MUST fail on keys missing in any locale and on unused keys.** Where the backend also produces user-facing text, share the translation JSON with the backend source generator ([08 Code Generation and Contracts](08-code-generation-and-contracts.md)); the shared JSON and the unused-key check then cover both sides.
13. **Heavy optional SDKs (charting, editors, and Firebase in apps that use it) SHOULD be loaded with dynamic `import()`** so they stay out of the main bundle.
14. **Embeddable widgets MUST be custom elements built with a separate Vite config, and their styles MUST be scoped to the shadow root, not injected into global CSS.** Import CSS with `?inline` and attach it to the shadow root. Vue's custom element mode (`.ce.vue`) inlines SFC `<style>` blocks into the shadow root, which is also acceptable.
15. **In-app help (user manual) MAY be rendered from markdown in `src/content/`.**

### Routing

16. **Route-level auth meta MAY be omitted for routes any logged-in user may see, and MUST be set, with a navigation guard, for role-specific routes (admin pages).** The server requires login before it serves `index.html`, so the SPA never loads unauthenticated. This deviates from the general [Vue Router](../technology/frontend-vue.md#vue-router) guidance, which asks for `meta` and navigation guards on every route. A user without the role gets a clear page from the guard instead of a page full of 403 errors. The server remains the authority.

### Security and accessibility

17. **`v-html` MUST NOT be used on user-provided content without sanitization.** See [16 HTTP Security Hardening](16-http-security-hardening.md).
18. **Interactive elements MUST be semantic (`<button>`, `<a>`, form controls), never clickable `<div>`s, and MUST be keyboard operable.** Use roving focus for composite widgets (lists of options, toolbars).
19. **Accessibility MUST be checked with axe in E2E flows** as described in [19 Testing Strategy](19-testing-strategy.md).

### Tooling and tests

20. **Linting and formatting MUST use oxlint (correctness = error, `complexity` ≤ 10, `max-props`), ESLint for Vue/TS rules with `eslint-plugin-oxlint` to avoid duplicate rules, oxfmt and knip.** See [Oxlint](../technology/frontend-vue.md#oxlint) and [Oxfmt](../technology/frontend-vue.md#oxfmt).
21. **CI MUST run lint, format, knip and `vue-tsc` in check mode and fail on findings.** `--fix` in CI and auto-commit bots hide problems from the author and let unfixable ones pass. Auto-fix belongs in the editor or a pre-commit hook ([25 Code Quality Guards](25-code-quality-guards.md)).
22. **Frontend logic MUST have vitest tests next to the source** (`foo.ts` + `foo.test.ts`).
23. **pnpm MUST be pinned to an exact version with the supply-chain block** (`minimumReleaseAge`, `trustPolicy`, `blockExoticSubdeps`, `strictDepBuilds`). See [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).

## Examples

### Fetch wrapper with injected dependencies (rule 7)
Write the wrapper as a factory and unit-test it with fake dependencies.

```ts
export interface AppFetchDeps {
  fetch: typeof fetch
  redirect: (url: string) => void
  location: () => { pathname: string; search: string }
}

/** /api 401s go to login with the current location as the return URL. */
export function createAppFetch(deps: AppFetchDeps): typeof fetch {
```

Give it to the generated Hey API client as its `fetch` through the client plugin's `runtimeConfigPath`, so it is in place before the first call. Prefer the injectable factory over module state, so it can be tested.

### TanStack Query lint rules as errors (rules 4, 20)
```json
"tanstack-query/exhaustive-deps": "error",
"tanstack-query/no-rest-destructuring": "error",
"tanstack-query/stable-query-client": "error",
...
"eslint/complexity": ["error", { "max": 10 }],
```

### i18n completeness test (rule 12)
A test flattens every locale file and fails on keys missing from a locale and on keys used by neither frontend nor backend source.

### CI in check mode (rules 3, 21, 22)
```yaml
- run: pnpm --dir frontend lint
- run: pnpm --dir frontend type-check
- run: pnpm --dir frontend test
- run: pnpm --dir frontend knip
- run: pnpm --dir frontend build
```
Add an `oxfmt --check` step so formatting is also checked without writing.

### Feature folders and create/edit split (rules 9, 10)
`admin/modules/orders/` holds `CreateOrderDialog.vue`, `EditOrder.vue` and `ViewOrder.vue`. Where create and edit are pages, `OrderCreatePage.vue` and `OrderEditorPage.vue` share a `BasicsForm.vue`.

### Other patterns
- Dynamic import of a heavy SDK in `src/realtime/firebase.ts` (rule 13).
- Bootstrap data read from `index.html` in `src/bootstrap.ts` (rule 8).
- A separate Vite config such as `vite.config.widget.ts` for custom-element widgets, with `import css from './widget.css?inline'` attached to the shadow root (rule 14).
- Roving focus helper in a plain `.ts` module (rule 18).
- In-app manual as markdown under `src/content/manual` (rule 15).
- `queryOptions` factories in `src/queries/*.ts` (rule 4).

## Related
- [Frontend (Vue)](../technology/frontend-vue.md) — stack and tool guidance
- [08 Code Generation and Contracts](08-code-generation-and-contracts.md) — generated client and shared translations
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — BFF session
- [16 HTTP Security Hardening](16-http-security-hardening.md) — CSRF, sanitization
- [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md) — pnpm settings
- [19 Testing Strategy](19-testing-strategy.md) — vitest and axe
- [25 Code Quality Guards](25-code-quality-guards.md) — pre-commit hooks and lint
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — how the SPA is served
