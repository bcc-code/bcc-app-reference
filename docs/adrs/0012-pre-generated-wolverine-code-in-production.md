# 0012. Pre-generated Wolverine code in production

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0013.

## Context

Wolverine builds an adapter class for every endpoint and handler. By default it generates and compiles that code with Roslyn when the app starts. On Cloud Run that costs cold-start time on every new instance, and a handler that compiled locally can still fail to compile in production. Standard 08 rules 8 and 9 require pre-generated code in production and a CI check that generates and compiles it.

## Decision

We will generate the Wolverine code in the build with `dotnet run --project api/BccCode.Template.Api -c Release -- codegen write` and load it with `TypeLoadMode.Static` and `AssertAllPreGeneratedTypesExist = true` in Production. Development and tests keep runtime compilation, so nobody has to regenerate code while working locally. The generated files (`Internal/Generated/`) are build output and are not committed. The CI `codegen` job generates the code and builds the Release container, and the deploy workflow does the same before it publishes the image.

The command-line tools (`codegen`, `resources`, `db-apply`, `db-assert`, `openapi`) run without Pub/Sub or `Internal:*` settings; startup code that needs infrastructure checks `CodeGeneration.IsCommandLineTool()`.

## Consequences

| | |
|---|---|
| Positive | No Roslyn compilation at startup in Cloud Run. A handler whose dependencies do not resolve fails the PR, not the deploy. |
| Negative | Production loads different generated code than Development compiles at runtime; a difference between the two only shows in the `codegen` job or after deploy. |
| Follow-up | The EF Core storage code goes through the same `codegen write` step. |

## Alternatives considered

- **Commit the generated files** — reviewable, but every endpoint change produces generated diffs, and stale files are easy to commit. The CI job gives the same guarantee.
- **Runtime compilation in production** — rejected for the cold-start cost and the late failure.
