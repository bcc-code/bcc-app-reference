---
title: "The admin MFA step-up asks for a second factor with an mfa scope, not acr_values"
status: needs-decision
decision-by: "the platform team that owns BCC Signon"
effort: S
priority: P3
scope: api
area: correctness
---

# The admin MFA step-up asks for a second factor with an mfa scope, not acr_values

`api/BccCode.Template.Api/Auth/MfaStepUp.cs` sends the browser back to the identity provider with the `mfa` scope when an administrator's first token has no `mfa` in `amr` (standard 14.4). This follows bcc-ateam. The standard-OIDC way to ask for a stronger authentication is `acr_values`, and whether BCC Signon honours it has not been verified.

To close it:

- Ask the platform team which of the two BCC Signon supports (or both).
- If `acr_values` works, send it on the step-up leg in `MfaStepUp.AddScope` (rename it) and keep the `amr` check; update ADR 0019 and `LoginTests`.
- If only the scope works, delete this file and drop the sentence in ADR 0019.
