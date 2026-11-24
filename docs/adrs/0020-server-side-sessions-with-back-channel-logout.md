# 0020. Server-side sessions in EF Core with back-channel logout

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0022, with the session store in EF Core tables.

## Context

The BFF session lives in a cookie ([0019](0019-bff-sign-in-with-bcc-signon.md)). A stateless cookie carries the whole session, so it cannot be revoked: logout only removes it from one browser, and with sliding expiry a copied cookie that keeps being used never expires. BCC Signon supports OIDC back-channel logout, which tells the app when a person signs out centrally or is blocked. Standard 14.7 requires a server-side store, and 14.9 requires a back-channel logout endpoint when the provider supports it.

## Decision

We will keep sessions in Postgres through an EF Core `ITicketStore`.

- The cookie `__Host-<app>` (HttpOnly, Secure, SameSite=Lax) carries only the session key.
- The authentication ticket is serialized and protected with Data Protection ([0021](0021-data-protection-keys-in-postgres-with-kms.md)) before it is stored. The row also keeps `iss`, `sid` and `sub` for back-channel logout and the expiry time. The session table has no history twin: it is not domain data.
- Sessions slide over 8 hours. That covers a working day without new sign-ins; the store makes a long window acceptable because a session can be revoked.
- `POST /auth/backchannel-logout` validates the signed logout token and deletes the sessions by `iss` + `sid`, or `iss` + `sub` when the token has no `sid`.
- Expired sessions are deleted when new ones are stored, because the app runs no background jobs.

## Consequences

| | |
|---|---|
| Positive | A session can be revoked one at a time: on logout, on back-channel logout from BCC Signon, or by deleting its row. The cookie stays small whatever the claims. |
| Negative | One extra database read per authenticated request. Sessions are personal data at rest (personUid and name in a protected ticket, deleted when the session ends or expires); they are in the [data inventory](../data-protection/data-inventory.md). A database outage signs everyone out until it is back. |
| Follow-up | Cache tickets in memory for a short time if the extra read shows up in latency. |

## Alternatives considered

- **Stateless cookie** — no database read and no table, but no revocation and no way to act on back-channel logout. An absolute lifetime cap would limit, not remove, the risk of a stolen cookie.
- **A deny-list of revoked `sid`s next to a stateless cookie** — also needs a database read per request, so it saves little and adds a second mechanism.
