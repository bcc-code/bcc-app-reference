---
title: "The Entra amr claim is only checked against the OIDC mock"
status: needs-decision
decision-by: "whoever registers the first Entra application and can sign in to that tenant"
effort: S
priority: P2
scope: api
area: correctness
---

# The Entra amr claim is only checked against the OIDC mock

[ADR 0031](../adrs/0031-entra-id-sign-in-next-to-bcc-signon.md) keeps an Entra administrator without a second factor out by reading `amr` in the ID token: `ProviderSignIn.EntraTokenValidatedAsync` gives no session when the person has the admin role and `amr` has no `mfa`. The `template-entra` client of the OIDC mock sends `amr` the way Entra is expected to. Nobody has checked that a real Entra v2 ID token for a Web registration carries `amr` with `mfa` after a Conditional Access multifactor prompt (Entra can leave `amr` out of a v2 token, or report `mfa` only in some flows).

If it does not, the app fails closed: no Entra administrator can sign in. Members are not affected.

To close it:

- Sign in with an administrator account in a sandbox tenant whose Conditional Access policy requires MFA for the app, and read the ID token's `amr` (a trace or a temporary local log of the claim names, never the values of personal claims).
- If `mfa` is there, delete this file and the sentence about it in ADR 0031.
- If not, decide between an optional claim on the registration, reading another claim (`acrs`, `xms_cc`), or trusting Conditional Access without the check, and update ADR 0031 and `ProviderSignInTests`.
