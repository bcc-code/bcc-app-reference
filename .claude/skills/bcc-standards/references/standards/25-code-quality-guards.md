# 25 Code Quality Guards

This standard lists the automated checks that turn code rules into build or startup failures: compiler settings, analyzers, formatting, startup assertions and frontend checks. A rule that only lives in a document or a reviewer's head is broken as soon as someone, or an agent, does not know it. CI is the gate; local hooks only make feedback faster.

Analyzer rules left at `suggestion` severity in `.editorconfig` enforce nothing, however long the file is. A rule counts as enforced when breaking it fails the build.

## Rules

### Compiler and analyzers

1. **`Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` MUST be set once in `Directory.Build.props`**, not per project. See [01 Repository Layout and Build](01-repository-layout-and-build.md).
2. **Roslyn analyzers MUST be enabled centrally with `AnalysisLevel` = `latest-recommended` and `EnforceCodeStyleInBuild` = `true`, and `.editorconfig` MUST give the rules we care about a severity of `warning` or `error`.** Rules set to `suggestion` enforce nothing. Turn rules on in batches and fix or suppress (with a reason) per batch.
3. **CI MUST run `dotnet format --verify-no-changes`.** Formatting differences then never reach review.

### Authorization sweep

4. **Every endpoint MUST be covered by an authorization sweep test** ([15 Authorization and Permission Checks](15-authorization-and-permission-checks.md)).

### Startup assertions

5. **Misconfiguration that would be a security or correctness bug MUST fail at startup.** At least: an anonymous endpoint not in an explicit allowlist, and missing OIDC authority or audience outside Development. Where applicable to the app: listeners on the API host when a worker owns them, routes mapped in the wrong role, and missing pre-generated Wolverine types in production (`AssertAllPreGeneratedTypesExist`).

### Other guards

6. **Frontend checks (oxlint, ESLint, oxfmt, knip, `vue-tsc`) MUST run in CI in check mode** as described in [21 Frontend](21-frontend.md).
7. **Committed OpenAPI document and generated client MUST be checked for drift in CI** as described in [08 Code Generation and Contracts](08-code-generation-and-contracts.md).
8. **Pre-commit hooks MAY be added with lefthook for fast checks (format, lint on staged files).** CI stays the gate, because hooks can be skipped and are not installed on every machine.
9. **Complexity SHOULD be limited, and MUST be limited once the rule and threshold are chosen.** Frontend: oxlint `complexity` max 10. Backend: enable a cyclomatic complexity rule at warning level (CA1502 via `CodeMetricsConfig.txt`, or a Sonar-style analyzer). Verify the chosen rule and threshold on one repo before rolling it out. See [Keep Code Complexity Low](../principles/code-design.md#keep-code-complexity-low).
10. **Coverage SHOULD be measured but MUST NOT gate merges** ([19 Testing Strategy](19-testing-strategy.md)).

## Examples

### Startup assertions (rule 5)
- Anonymous allowlist: the endpoint configuration throws at startup when an endpoint is anonymous and its route is not in an explicit allowlist. The startup failure exists so nobody can make an endpoint anonymous just by copying an attribute.
- No listeners on the API host: an assertion that fails startup when message listeners are registered in the API role while a worker owns them.
- Role/route separation:

  ```csharp
  var forbidden = role == AppRole.Api ? "/internal" : "/api";
  ...
  if (violations.Count > 0)
      throw new InvalidOperationException($"The {role} role must not map these routes: {string.Join(", ", violations)}");
  ```
- Pre-generated types: `critterStack.Production.AssertAllPreGeneratedTypesExist = true;` in `Program.cs`.
- OIDC audience: startup throws when the audience is missing, covered by a test such as `Startup_Throws_WhenOidcAudienceConfigurationIsMissing`.

### `Directory.Build.props` analyzer block (rules 1, 2)

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

## Related
- [01 Repository Layout and Build](01-repository-layout-and-build.md) — central build properties
- [05 CI Pipeline](05-ci-pipeline.md) — where the checks run
- [08 Code Generation and Contracts](08-code-generation-and-contracts.md) — drift checks
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md), [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md) — slice and domain rules
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — authorization sweep
- [19 Testing Strategy](19-testing-strategy.md) — coverage
- [21 Frontend](21-frontend.md) — frontend checks
- [Code design principles](../principles/code-design.md) — complexity, no magic
- [Security principles](../principles/security.md) — shift-left practices
