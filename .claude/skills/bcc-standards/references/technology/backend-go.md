# Backend (Go)

Go is reserved for cases where the .NET runtime is too heavy or ultra-low overhead is required. The default backend stack is [Backend (.NET)](backend-dotnet.md).

## When to Use Go

Go is not the default. Use .NET unless there is a specific reason not to. Go is reserved for cases where the .NET runtime overhead is a genuine problem: high-throughput, low-latency services where every millisecond and megabyte counts.

Guidance:
- Document the specific performance or footprint requirement before choosing Go. "Might be faster" is not enough.
- Go increases stack diversity and maintenance burden. That is a real cost that needs justification.
- For most services, .NET on Cloud Run is fast enough.

Example: a high-throughput telemetry ingestion service processing millions of events per minute is implemented in Go due to strict latency requirements that .NET could not meet.

## Standards for Go services

A Go service follows the standards that do not depend on the .NET stack: [03 Infrastructure as Code](../standards/03-infrastructure-as-code.md), [04 GCP Runtime](../standards/04-gcp-runtime.md), [05 CI Pipeline](../standards/05-ci-pipeline.md), [06 Deployment and Release](../standards/06-deployment-and-release.md), [13 Messaging and Integration](../standards/13-messaging-and-integration.md), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md), [15 Authorization and Permission Checks](../standards/15-authorization-and-permission-checks.md), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md), [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md), [22 Observability and Operations](../standards/22-observability-and-operations.md) and [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md). The rules that name Wolverine, Marten, EF Core or .NET tooling ([08](../standards/08-code-generation-and-contracts.md), [10](../standards/10-vertical-slices-and-cqrs.md), [11](../standards/11-domain-modelling-and-errors.md), [12](../standards/12-data-access-and-migrations.md) and [25](../standards/25-code-quality-guards.md)) do not apply literally; apply their intent: pure decision functions, explicit errors, a private database, versioned migrations run as a separate step, and checks in CI that enforce the conventions.

Related: [Cloud and tools](cloud-and-tools.md) (Cloud Run), [04 GCP Runtime](../standards/04-gcp-runtime.md), [Code design](../principles/code-design.md) (reduce accidental complexity, YAGNI).
