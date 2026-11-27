# Personas

The OIDC mock (`api/OidcMock`) lets you pick a persona at login. The app's sign-in page (`/auth/sign-in`) first asks for the provider: BCC Signon (the mock's `template` client) or Microsoft Entra (its `template-entra` client). Nobody uses real credentials locally. The personas are synthetic:
their personUids and names are made up and match no real person ([ADR 0019](../adrs/0019-bff-sign-in-with-bcc-signon.md)). The list
is in `api/OidcMock/Personas.cs`.

| Persona | Use |
|---|---|
| `user` | A plain member. Owns the seeded notes |
| `user2` | A second member with no notes. Use it to check that one user cannot read or change another user's notes (403) |
| `admin` | Has the admin role and signs in with a second factor (`amr` has `mfa`). Can call `GET /api/admin/stats` |
| `admin-no-mfa` | Has the admin role but no second factor. Sign-in ends on the second-factor page, and every `/api` call answers 401 with the step-up problem |

## Entra personas

Through the `template-entra` client the same four personas sign in as Microsoft Entra people ([ADR 0031](../adrs/0031-entra-id-sign-in-next-to-bcc-signon.md)). The mock then issues what Entra does: a `sub` that is not a Guid (a different one per persona, written in `Personas.cs`), an `oid`, a `tid`, `name`, `sid` and `amr`, and no personUid and no role. The app gives each Entra person a person id of its own, so `user` through Entra is not the owner of the seeded notes (no account linking) and sees an empty list. `admin` and `admin-no-mfa` are administrators through `Auth:BootstrapAdmins`, which the AppHost sets to their `oid` as `entra:<oid>,entra:<oid>`; `admin-no-mfa` ends on the second-factor page as with Signon. Other personas can be made administrators on the admin page once they have signed in. Previews stay on BCC Signon only.

The seeder (`api/BccCode.Template.TestData`) compiles `api/OidcMock/Personas.cs` in as a linked file and gives the seeded notes to
`Persona.User`, so the personUid of the notes' owner and the one the mock issues cannot differ. The integration tests and the AppHost use the
same file, and the E2E tests reach it through the AppHost. In previews the same personas sign in through the OIDC mock deployed with the preview, and anyone with the URL can pick any
of them ([pr-previews](../pr-previews.md)).
