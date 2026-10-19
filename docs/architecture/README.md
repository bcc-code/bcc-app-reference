# Architecture

The system as built, as C4 diagrams. The source is the Mermaid text in this file (rendered by GitHub and most editors).

TODO: standard 23, rule 4 asks for an exported SVG next to the source. Export each diagram with `mmdc` (Mermaid CLI) or the
Mermaid live editor into this folder when the diagrams have settled, and keep both in step.

## Context

Who uses the application and which systems it talks to.

```mermaid
C4Context
    title Template: system context
    Person(user, "Signed-in user", "Keeps personal notes")
    Person(admin, "Administrator", "Reads note statistics")
    System(app, "Template", "Web application for personal notes")
    System_Ext(oidc, "OIDC provider", "BCC sign-on: login and back-channel logout")
    System_Ext(gateway, "BCC platform gateway", "TLS, host name, routing")

    Rel(user, gateway, "Uses", "HTTPS")
    Rel(admin, gateway, "Uses", "HTTPS")
    Rel(gateway, app, "Routes requests to")
    Rel(app, oidc, "Authenticates users with", "OIDC code flow")
    Rel(oidc, app, "Ends sessions at", "Back-channel logout")
```

## Containers

What runs and how requests flow. The API serves `index.html` and every `/api` route. Only the content-hashed bundles under
`/assets` come from the bucket (standard 26).

```mermaid
C4Container
    title Template: containers
    Person(user, "User", "Browser")
    System_Ext(oidc, "OIDC provider", "Login and back-channel logout")

    System_Boundary(gcp, "GCP project (one per environment)") {
        Container(gw, "Platform gateway", "Cloud Load Balancing", "default backend: API; /assets/*: bucket")
        Container(api, "API", "ASP.NET Core, Wolverine HTTP, Cloud Run", "BFF: login and session, Notes endpoints, serves index.html")
        Container(bucket, "Assets bucket", "Cloud Storage, private", "Content-hashed JS and CSS bundles")
        ContainerDb(db, "Database", "Cloud SQL for PostgreSQL", "Notes, sessions, Data Protection keys")
        Container(migrate, "Migration job", "Cloud Run job", "Applies the schema before each rollout")
        Container(kms, "Key", "Cloud KMS", "Encrypts the Data Protection keys")
        Container(secrets, "Secrets", "Secret Manager", "OIDC client secret")
    }

    Rel(user, gw, "Uses", "HTTPS")
    Rel(gw, api, "Everything except /assets", "HTTPS")
    Rel(gw, bucket, "/assets/*", "HTTPS")
    Rel(api, oidc, "Authenticates users with", "OIDC code flow")
    Rel(oidc, api, "Ends sessions at", "Back-channel logout")
    Rel(api, db, "Reads and writes", "Npgsql through Cloud SQL Auth Proxy, IAM authentication")
    Rel(migrate, db, "Applies schema to", "Same, as its own IAM user")
    Rel(api, kms, "Encrypts keys with")
    Rel(api, secrets, "Reads client secret from")
```

The browser holds a session cookie only. Tokens stay on the server ([ADR 0004](../adrs/0004-bff-authentication-with-server-side-sessions.md)).

## Variants

The template has two APIs with the same behaviour and the same HTTP contract: `BccCode.Template.Api.Marten` (event-sourced
notes) and `BccCode.Template.Api.EfCore` (relational notes). Both use `BccCode.Template.Web` for the shared HTTP plumbing.
After `scripts/init-app.ps1` only one remains ([ADR 0003](../adrs/0003-marten-or-ef-core.md)).

## Local development

The Aspire AppHost starts the API, Postgres, the OIDC mock and the frontend dev server. See [../development.md](../development.md).
