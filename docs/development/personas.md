# Personas

The OIDC mock (`api/OidcMock`) lets you pick a persona at login. Nobody uses real credentials locally. The personas are synthetic:
their personUids and names are made up and match no real person ([ADR 0019](../adrs/0019-bff-sign-in-with-bcc-signon.md)). The list
is in `api/OidcMock/Personas.cs`.

| Persona | Use |
|---|---|
| `user` | A plain member. Owns the seeded notes |
| `user2` | A second member with no notes. Use it to check that one user cannot read or change another user's notes (403) |
| `admin` | Has the admin role and signs in with a second factor (`amr` has `mfa`). Can call `GET /api/admin/stats` |
| `admin-no-mfa` | Has the admin role but no second factor. Sign-in ends on the second-factor page, and every `/api` call answers 401 with the step-up problem |

The seeder (`api/BccCode.Template.TestData`) compiles `api/OidcMock/Personas.cs` in as a linked file and gives the seeded notes to
`Persona.User`, so the personUid of the notes' owner and the one the mock issues cannot differ. The integration and E2E tests use the
same file. In previews the same personas sign in through the OIDC mock deployed with the preview, and anyone with the URL can pick any
of them ([pr-previews](../pr-previews.md)).
