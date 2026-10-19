# 25 Code Quality Guards

This standard lists the automated checks that turn code rules into build or startup failures: compiler settings, analyzers, formatting, banned APIs, architecture tests, startup assertions and frontend checks. A rule that only lives in a document or a reviewer's head is broken as soon as someone, or an agent, does not know it. CI is the gate; local hooks only make feedback faster.

## Rules

### Compiler and analyzers

1. **`Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` MUST be set once in `Directory.Build.props`**, not per project. See [01 Repository Layout and Build](01-repository-layout-and-build.md).
2. **Roslyn analyzers MUST be enabled centrally with `AnalysisLevel` = `latest-recommended` and `EnforceCodeStyleInBuild` = `true`, and `.editorconfig` MUST give the rules we care about a severity of `warning` or `error`.** Turn rules on in batches and fix or suppress (with a reason) per batch.
3. **CI MUST run `dotnet format --verify-no-changes`.** Formatting differences then never reach review.

### Banned APIs

4. **APIs that break our conventions MUST be banned by a check that fails the build.** Use `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a `BannedSymbols.txt` (for example `DateTime.Now`, `DateTime.UtcNow`, `DateOnly.Today`, `ILoggerFactory` in domain code), and/or an IL-level scanner in the architecture tests for rules about specific method calls.
5. **A custom scanner MUST have a self-test** that runs it against a fixture containing a known violation. Without it, a broken scanner passes every rule.

### Architecture tests

6. **Each repo MUST have an architecture test project (NetArchTest plus custom reflection or IL checks)** covering the rules that apply to its style:
   - endpoint and handler classes are static (slices);
   - no `*Service` or `*Repository` types in the domain ([10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md), [12 Data Access and Migrations](12-data-access-and-migrations.md));
   - integration messages are sealed records;
   - no direct clock access ([11 Domain Modelling and Errors](11-domain-modelling-and-errors.md));
   - every `[LoggerMessage]` class is registered ([22 Observability and Operations](22-observability-and-operations.md));
   - cascaded messages are routed to an explicit endpoint;
   - serverless style only: no hosted services, scheduling, local queues or async daemon.
7. **Every endpoint MUST be covered by an authorization sweep test** ([15 Authorization and Permission Checks](15-authorization-and-permission-checks.md)).

### Startup assertions

8. **Misconfiguration that would be a security or correctness bug MUST fail at startup.** At least: an anonymous endpoint not in an explicit allowlist, listeners on the API host when a worker owns them, routes mapped in the wrong role, missing pre-generated Wolverine types in production (`AssertAllPreGeneratedTypesExist`), and missing OIDC authority or audience outside Development.

### Other guards

9. **Slopwatch MUST run in CI** as described in [24 AI Agent Tooling](24-ai-agent-tooling.md). *This template deviates: ADR 0006 removes Slopwatch.*
10. **Frontend checks (oxlint, ESLint, oxfmt, knip, `vue-tsc`) MUST run in CI in check mode** as described in [21 Frontend](21-frontend.md).
11. **Committed OpenAPI document and generated client MUST be checked for drift in CI** as described in [08 Code Generation and Contracts](08-code-generation-and-contracts.md).
12. **Pre-commit hooks MAY be added with lefthook for fast checks (format, lint on staged files).** CI stays the gate, because hooks can be skipped and are not installed on every machine.
13. **Complexity MUST be limited.** Frontend: oxlint `complexity` max 10. Backend: there is no limit today; enable a cyclomatic complexity rule at warning level (CA1502 via `CodeMetricsConfig.txt`, or a Sonar-style analyzer). Verify the chosen rule and threshold on one repo before rolling it out. See Keep Code Complexity Low.
14. **Coverage SHOULD be measured but MUST NOT gate merges** ([19 Testing Strategy](19-testing-strategy.md)).

## Example

`Directory.Build.props`, `.editorconfig`, `api/tests/BccCode.Template.ArchitectureTests/` (architecture tests, banned APIs), startup assertions in `api/BccCode.Template.Web/`. Run the `standards-reviewer` agent for a review against these rules.
