# Frontend (Vue)

This page describes the approved frontend stack and the guidance for each tool: TypeScript in strict mode, Vue with the Composition API, Vue Router, Hey API, TanStack Query, Pinia (with caution), vue-i18n, PrimeVue, Tailwind CSS, Oxlint, Oxfmt, validation, real-time updates and separate create and edit components. The enforceable rules are in [21 Frontend](../standards/21-frontend.md).

## TypeScript (Strict Mode)
Always enable TypeScript strict mode. It turns on the strongest compile-time checks and catches a whole class of bugs before they reach runtime.

- Add `"strict": true` to `tsconfig.json`. Do not weaken it for convenience.
- Avoid `any`. If you do not know the type at a boundary, use `unknown` and parse it explicitly.
- Strict mode is more work upfront and saves significant debugging time downstream.

Example: a form model uses strict typing. Missing required fields are caught at compile time instead of producing `undefined` at runtime.

Related: [21 Frontend](../standards/21-frontend.md) rules 2 and 3, [Oxlint](#oxlint), [Oxfmt](#oxfmt).

## Vue.js (Composition API)
Always use Vue's Composition API. The Options API is not used in new code.

- Use `<script setup>` for components. It is the cleanest way to use the Composition API.
- Extract shared logic into composables (`useXxx()`) rather than duplicating it across components.
- Keep composables small and focused. A composable that does everything is just a service.
- The Options API may be used in legacy components with explicit approval, but not in new code.

Example: a `useUserPreferences()` composable handles preference loading and updates and is reused across multiple views.

Related: [21 Frontend](../standards/21-frontend.md) rule 1, [TanStack Query](#tanstack-query), [Pinia](#pinia-use-with-caution), [Vue Router](#vue-router).

## Vue Router
Vue Router is the official routing library for Vue. Use it for all client-side navigation.

- Define routes with explicit path definitions, in a `routes.ts` per feature folder (see [21 Frontend](../standards/21-frontend.md) rule 9), combined in one router module.
- Centralize auth guards. Do not scatter them across individual route definitions.
- Use route meta fields to declare auth requirements per route and check them in a navigation guard.
- Do not build custom routing logic ad hoc.

Example: routes are defined centrally with meta tags like `{ requiresAuth: true }`. A global navigation guard checks these before each navigation.

In apps where the server requires login before it serves `index.html`, the meta is optional for routes any logged-in user may see, but still required for role-specific routes. See [21 Frontend](../standards/21-frontend.md) rule 16 and [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## Hey.api
Hey API generates type-safe TypeScript clients directly from OpenAPI specs. When the backend API changes, you regenerate the client and TypeScript tells you immediately what broke.

- Regenerate clients whenever the OpenAPI spec changes. Make this part of the workflow, not an afterthought.
- Do not maintain API client code by hand. It will drift.
- Use Hey API as the only generator (see [21 Frontend](../standards/21-frontend.md) rule 6). If it does not meet a need, raise it in an ADR instead of adding a second generator.

Related: [08 Code Generation and Contracts](../standards/08-code-generation-and-contracts.md), [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md) (task-oriented APIs and DTOs rather than schema exposure).

## TanStack Query
TanStack Query (formerly React Query) handles server state in the frontend: fetching, caching, background refreshing and synchronization.

- Use TanStack Query for all server state. Do not put server data in Pinia or local component state and manage staleness by hand.
- Use consistent query keys. Sloppy key management leads to cache collisions or missed invalidations.
- Use the Vue integration for proper reactivity.

Why it matters: managing server state manually (loading flags, error states, cache invalidation, background refreshing) is tedious and error-prone. TanStack Query handles all of this with sensible defaults.

Example: a `useQuery` call fetches the order list and automatically re-fetches when the window regains focus.

Related: [21 Frontend](../standards/21-frontend.md) rule 4, [Vue.js (Composition API)](#vuejs-composition-api), [Pinia](#pinia-use-with-caution).

## Pinia (Use with Caution)
Pinia is Vue's global state management library. Use it when you need shared state across unrelated components, and reach for it only after ruling out simpler options.

- Default to local component state and TanStack Query for server data. Most "state management" problems are server state problems.
- Use Pinia when you have state that:
  - needs to be shared across multiple unrelated views
  - persists across navigation
  - cannot reasonably be owned by a single component
- Keep stores small and domain-focused. A `useCheckoutStore` that tracks wizard steps is reasonable. A global `useAppStore` that holds everything is not.

Example: a multi-step form wizard uses a small Pinia store to maintain step state across navigation. Single-view data uses local state instead.

Related: [21 Frontend](../standards/21-frontend.md) rule 5.

## Vue-I18n
vue-i18n is the standard internationalization library for our Vue applications.

- Use vue-i18n for all user-facing strings and locale-specific formatting (dates, numbers, currency).
- Choose stable, descriptive translation keys: `order.status.active` is better than `statusText1`.
- Do not hard-code strings in components.

```vue
<template>
  <p>{{ t('order.status.active') }}</p>
</template>
```

Related: [21 Frontend](../standards/21-frontend.md) rule 12.

## PrimeVue
PrimeVue is our standard UI component library for Vue. Use it with the BCC default theme.

- Use the BCC default theme. Do not use the default PrimeVue theme or build a custom one from scratch.
- Prefer PrimeVue's built-in components. They are accessible by default and tested.
- Do not mix multiple UI libraries in the same app. Pick one and stick with it.
- Only build custom components when PrimeVue does not cover the use case.
- Configure PrimeVue 4 with the per-response CSP nonce (`csp: { nonce }` in its config) so its styles work without `'unsafe-inline'`. Allow `'unsafe-inline'` for styles only with a code comment that says why. See [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

Related: [21 Frontend](../standards/21-frontend.md) rules 11 and 18, [Tailwind CSS](#tailwind-css).

## Tailwind CSS
Tailwind is our utility-first CSS framework. Instead of writing custom CSS classes, you compose styles directly in markup using utility classes.

- Use Tailwind for consistent, rapid styling.
- Extract repeated patterns into components rather than duplicating utility class lists.
- Keep class lists readable. A long string of Tailwind classes is fine for one element, but extract it if it is used in multiple places.

Related: [PrimeVue](#primevue), [21 Frontend](../standards/21-frontend.md) rule 11.

## Oxlint
Oxlint is our JavaScript/TypeScript linter. It is a Rust-based linter from the Oxc project, much faster than the older JS-based linters, and fast enough to run on every save and every commit.

- Use Oxlint as the linter for all frontend repos. Run it in CI and on every commit. ESLint stays in place for Vue and TypeScript rules Oxlint does not cover, with `eslint-plugin-oxlint` to switch off duplicates, and the complexity limit is an error ([21 Frontend](../standards/21-frontend.md) rule 20).
- Use a shared configuration across repos. Per-project customization creates drift.
- Auto-fix what can be auto-fixed; review what cannot. In CI, run in check mode only.
- Set up a pre-commit hook so issues are caught locally, before CI.

Why it matters: when linting is effectively free, there is no incentive to skip it or defer it to a nightly job. Fast feedback keeps the rule set enforced rather than quietly ignored.

Related: [Oxfmt](#oxfmt), [21 Frontend](../standards/21-frontend.md) rules 20 and 21, [25 Code Quality Guards](../standards/25-code-quality-guards.md).

## Oxfmt
Oxfmt is our code formatter. Like any formatter, it ends formatting debates by making every decision consistently. Being Rust-based (from the Oxc project), it is fast enough to run on save without lag.

- Run Oxfmt on save and via a pre-commit hook.
- Do not hand-tune formatting. The point is to stop making formatting decisions.
- Use a single shared Oxfmt config across repos so formatting is identical everywhere.
- Enforce formatting in CI so unformatted code cannot merge.

Why it matters: a single automatic formatter removes an entire category of pointless review comments and diff noise. Every editor produces the same output, so diffs show only real changes.

Related: [Oxlint](#oxlint).

## Validation Strategy
Server-side validation is authoritative. Client-side validation is a UX improvement, not a security control.

- Always validate on the server. Client-side validation can be bypassed trivially.
- Use client-side validation to give users immediate inline feedback, but do not rely on it for correctness.
- The API should return clear, structured error messages that the UI can display.
- Where possible, share validation schemas between client and server to keep rules consistent.

Example: a form shows inline errors as you type. On submit, the API also validates and returns error messages. The UI displays them alongside the relevant fields.

Related: [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md) (parse, do not validate).

## Real-Time Updates
For real-time UI updates, use Server-Sent Events (SSE). It is simple, works over HTTP and integrates well with an event sourcing model.

- Default to SSE for real-time updates from server to client.
- Use Google Pub/Sub for backend fan-out when multiple services need to be notified.
- SSE is the default. Use Firestore (or another client-readable realtime store) only with an ADR that explains why SSE does not fit. A store the browser reads directly creates a second auth and security model, which needs its own rules and tests ([15 Authorization and Permission Checks](../standards/15-authorization-and-permission-checks.md), [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md)).

SSE is one-directional (server to client) and works over standard HTTP. It is much simpler to set up, works well with load balancers and proxies and needs no special handling. For most real-time use cases (live feeds, status updates, notifications) it is enough.

Example: the UI opens an SSE connection to receive live updates for a list. Background jobs publish events to Pub/Sub, which the API reads and streams to connected clients.

Related: [13 Messaging and Integration](../standards/13-messaging-and-integration.md).

## Separate Create and Edit Components
Do not reuse a single component for both create and edit. Build separate components and share only the common parts.

- Create a `CreateX` and an `EditX` component. Each owns its own flow.
- Extract shared UI (the form fields) into a presentational child, and shared logic into a composable or service. Share those, not the whole screen.
- Let each component handle its own data loading, submission and success/error handling.

Why it matters: create and edit look similar but differ in ways that accumulate. Create starts empty, edit loads and pre-populates; they call different endpoints; validation, permissions and "what happens on success" often diverge. Forcing both through one component breeds `v-if="isEdit"` branches until the component is a tangle of conditionals that is hard to read and easy to break, and a bug in one mode risks the other. Two focused components with shared pieces stay simple and independently testable.

Related: [21 Frontend](../standards/21-frontend.md) rule 10, [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [Code design](../principles/code-design.md).
