# 0011. BCC component library and Tailwind for UI

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0012.

## Context

BCC apps should look and behave alike, and accessible components are expensive to build well. Mixing UI libraries leads to inconsistent styling and larger bundles.

## Decision

- UI is built with the **BCC component library** (`@bcc-code/component-library-vue`, PrimeVue with the BCC theme). We build our own component only when the library has nothing that fits.
- Styling uses **Tailwind 4** with the BCC design tokens that come with the library's theme. There is no `tailwind.config`; Tailwind runs through `@tailwindcss/vite`.
- CSS layer order is fixed in `src/style/main.css` and re-applied at startup, because PrimeVue inserts its own layer order.
- No other component library. **Pinia** is used only for client state that is shared across unrelated components and is not server state (standard 21.5); server state goes through TanStack Query ([0005](0005-openapi-contract-and-generated-client.md)).

## Consequences

| | |
|---|---|
| Positive | Consistent BCC look; accessible components out of the box; small amount of custom CSS. |
| Negative | We depend on the library's release cadence. `@bcc-code/*` packages are excluded from pnpm's minimum release age so library fixes can be taken at once, which means less protection against a compromised release in that scope. |
| Follow-up | Configure PrimeVue with a CSP nonce when the library supports it (standard 16). |
