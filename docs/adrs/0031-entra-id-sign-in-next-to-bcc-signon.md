# 0031. Microsoft Entra ID sign-in next to BCC Signon, with app-issued person ids and stored admin grants

- Status: Proposed
- Date: 2026-11-27
- Drafted with AI assistance; a person must review it before it is accepted.
- Modelled on bcc-service-portal (decisions D-07, D-169 to D-171, D-285 there). Differs on the user id, account linking,
  the second factor and the app registration, see below.
- No spec in `docs/specs/` came first (standard 23.6): the open design questions (how a provider is chosen, the Entra user id,
  linking, admin grants, the second factor, the registration) were put to the product owner one by one and answered before any
  code was written. The answers are the Decision section below; what is still open is under Follow-up.

## Context

Some BCC apps are used by staff who sign in with the BCC Microsoft tenant (Entra ID) and not with BCC Signon, and some by
both groups. The template only knows BCC Signon ([0019](0019-bff-sign-in-with-bcc-signon.md)): the fallback policy requires
the `https://login.bcc.no/claims/personUid` claim, all data is keyed by that Guid, administrators come from the Signon `role`
claim, and the second factor is asked for with Signon's `mfa` scope.

bcc-service-portal signs in with both providers at once. It registers each OIDC scheme only when its settings are complete,
lets the person pick a provider on a sign-in page, uses `sub` as the Entra user id, links accounts across providers by email,
keeps admin grants in its own database, leaves the second factor to Entra Conditional Access, and creates the Entra app
registration with the Terraform `azuread` provider.

Entra differs from BCC Signon in ways that matter here: it has no `personUid`; its `sub` is pairwise (a different value per
app registration) and not a Guid; it does not offer PAR or OIDC back-channel logout; it does not understand a custom `mfa`
scope; and app roles live on the registration, outside the app.

## Decision

An app can turn on BCC Signon, Entra ID, or both. Signon apps that do not configure Entra keep working as before.

- **Providers by configuration.** Signon is on when `Oidc:Authority` and `Oidc:ClientId` are set (as today); Entra is on when
  `Entra:TenantId` and `Entra:ClientId` are set. Outside Development and Test a provider that is half configured, a missing
  client secret, or no provider at all stops the host (14.5). The Entra authority is
  `https://login.microsoftonline.com/<tenant>/v2.0`, single tenant; `Entra:Authority` may override it only in Development and
  Test (the local mock). The ID token's `tid` must equal `Entra:TenantId`.
- **Signing in.** Each provider has its own OIDC scheme and callback (`/signin-oidc` for Signon as before, `/signin-entra`).
  With one provider, a navigation without a session goes straight to it, as today. With both, it goes to `/auth/sign-in`, a
  plain server-rendered page with one link per provider (`/auth/login/bcc-signon`, `/auth/login/entra`). `/auth/login?returnUrl=`
  keeps working for the SPA's 401 handling. All of them are on the anonymous allowlist and accept only local return URLs.
  Both schemes use the same settings: code flow, PKCE, PAR `UseIfAvailable` (Entra has no PAR endpoint, so it falls back),
  `SaveTokens = false`, scopes `openid profile`, no userinfo call.
- **The person id.** The app's own claim `template:person` (a Guid) keys all data, and the fallback policy requires it instead
  of the Signon claim. For Signon it is the `personUid`, so existing data keeps its owner. For Entra it is a Guid the app issues
  the first time a `sub` signs in, kept in `person_identities` (provider, issuer, subject, person id, display name, first and
  last sign-in). Signon sign-ins are recorded in the same table, so the admin page can list everyone who has signed in. **No
  linking**: a Signon person and an Entra person are always two app persons, even with the same email. Email is not requested
  or stored. `template:provider` says which scheme a session came from, for logout. Both claims are set only by the app; claims
  of those names from a token are dropped.
- **Admin grants in the database.** `role_grants` (person id, role, granted and revoked time and by whom, `version`) gives a
  person the role named in `Auth:AdminRole`. At sign-in an active grant adds that role to the session, for both providers. The
  Signon `role` claim still grants it too, so Signon apps set up today need no change; Entra's own `roles` claim is not read.
  Administrators list the people who have signed in and grant or revoke the role under `/api/admin/people`. A revoke ends that
  person's sessions at once; a grant takes effect at their next sign-in. An administrator cannot revoke their own grant.
- **The first administrator.** `Auth:BootstrapAdmins` is one setting of `<provider>:<id>` entries: the Signon `personUid`, or
  the Entra object id (`oid`, which the Entra admin centre shows; `sub` is only known after a sign-in). These ids belong to real
  people, so they are not committed: in GCP the setting comes from an optional Secret Manager secret
  (`<app>-bootstrap-admins`), which can be removed once the first administrator has signed in. At sign-in a listed person who has never
  had a grant row gets one, recorded as granted by the configuration. After that the row is the truth, so a revoke sticks while
  the person stays in the list.
- **Second factor.** Unchanged for Signon (step-up with the `mfa` scope). For Entra there is no step-up: an administrator whose
  ID token has no `mfa` in `amr` gets no session and ends on `/auth/mfa-required`. The tenant must have a Conditional Access
  policy that requires MFA for the app; the runbook says so. Whether Entra's v2 ID token always carries `amr` is verified
  against the mock only ([tech debt](../tech-debt/entra-amr-claim-not-verified-in-a-real-tenant.md)); if it does not, admins
  cannot sign in with Entra, which fails closed.
- **Logout.** `POST /auth/logout` signs out of the scheme the session came from. Entra has no OIDC back-channel logout, so an
  Entra session ends at logout, on a revoke, or after the 8-hour sliding lifetime ([0020](0020-server-side-sessions-with-back-channel-logout.md),
  14.9). `/auth/backchannel-logout` stays for Signon.
- **App registration by hand.** One single-tenant registration per environment, created by hand like the Signon client (the
  platform's Terraform workflow has no Azure credentials). Terraform holds the tenant and client id per environment and reads
  the secret `<app>-entra-client-secret` from Secret Manager, only where Entra is on. Steps are in the
  [first-deploy runbook](../runbooks/first-deploy.md).
- **Locally and in tests.** The OIDC mock has a second client, `template-entra`, which issues Entra-shaped tokens for the same
  personas: a pairwise-looking `sub`, `oid`, `tid`, `name`, `amr`, and no `personUid` or role. Development and E2E run with both
  providers on, so the sign-in page and both flows are tested end to end.

## Consequences

| | |
|---|---|
| Positive | An app picks its providers without code changes. Signon apps keep their data and roles. Staff sign in with their work account. Admins can be managed in the app without a deploy, and a revoke ends the person's sessions. |
| Negative | Two schemes and a sign-in page to keep working. `person_identities` and `role_grants` are new personal data at rest (a pseudonymous subject, display name and sign-in times); they are in the [data inventory](../data-protection/data-inventory.md) and wait on the same erasure decisions. A person who uses both providers has two sets of data. Sessions written before this change lack `template:person`; the cookie handler rejects and removes such a session, so everyone signs in once more after the deploy. Entra sessions cannot be ended from Entra. |
| Follow-up | Verify `amr` in a real tenant. Revisit account linking if an app needs one person across providers. |

## Deviations from the standards

- **15.13** says admin access should come from the identity provider, not from the app. The grants are stored in the app
  because Entra app roles would need a tenant administrator for every change and Signon roles are not available to staff-only
  apps; the admin page makes them visible to the people who manage access, and only the first administrator comes from
  configuration.
- **14.4** asks for a step-up with `acr_values`. Entra gets no step-up at all; the `amr` check keeps admin sessions without a
  second factor out, and Conditional Access makes the second factor happen.

## Alternatives considered

- **One provider per app** — simpler, but an app used by both members and staff would need a second deployment.
- **`oid` as the Entra person id** — a stable Guid across registrations, but the team chose `sub`, as service-portal does. With
  `sub` the registration must not be replaced, or every Entra person gets a new id; the runbook says so.
- **Link accounts by email (service-portal)** — one person across providers, but it stores email and trusts both providers'
  email claims equally, so an unverified address at one provider could take over an account at the other.
- **Entra app roles for admin** — no new tables, but every grant needs a tenant administrator.
- **Trust Conditional Access without checking `amr` (service-portal)** — the app could not tell when the policy is missing.
- **App registration with the Terraform `azuread` provider (service-portal)** — needs an Azure service principal in the deploy
  workflow, which the platform does not provide.
