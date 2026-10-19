---
standard: 14 Authentication and Sessions
assessed: 2026-10-19
commit: e4b2b86
---
# 14 Authentication and Sessions — assessment

The BFF sign-in, server-side session store, back-channel logout and CSRF-protected logout are implemented in the shared
`BccCode.Template.Web` project and tested. Admin MFA step-up is missing, and the session cookie flags are set in code but no
test asserts them.

**Score: Strong** · ✅ 13 · ⚠️ 0 · ❌ 1 · N/A 4

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | BFF with OIDC code flow, PKCE and PAR | MUST | ✅ | `api/BccCode.Template.Web/Auth/AuthConfiguration.cs:114-116` — `ResponseType = Code`, `UsePkce = true`, `PushedAuthorizationBehavior.UseIfAvailable`. Browser only gets a cookie (ADR 0004) |
| 2 | `SaveTokens = false` unless calling APIs for the user | MUST | ✅ | `AuthConfiguration.cs:117` — `SaveTokens = false`, `GetClaimsFromUserInfoEndpoint = false` |
| 3 | No inbound claim mapping, minimal scopes, slim principal | MUST | ✅ | `AuthConfiguration.cs:119-122` (`MapInboundClaims = false`, scopes `openid profile`, no `offline_access`); `Auth/SessionClaims.cs:33-45` keeps `sub name role sid iss`. Tests: `SessionClaimsTests.Slimming_keeps_what_the_app_reads_and_adds_a_missing_issuer`, `SessionTicketStoreTests.Only_the_claims_the_app_reads_are_kept` |
| 4 | Admins stepped up to MFA before the session is written | SHOULD | ❌ | No step-up in `OnTokenValidated` (`AuthConfiguration.cs:128-132`); no `mfa`/`acr` handling anywhere in `api/` |
| 5 | Startup fails without OIDC authority/client/secret/audience outside Dev/Test | MUST | ✅ | `AuthConfiguration.cs:32,77-83` throws for `OIDC:Authority`, `OIDC:ClientId`, `OIDC:ClientSecret`. No bearer scheme exists, so the audience part does not apply. Tests: `OidcConfigurationTests.A_deployed_environment_without_oidc_settings_fails_at_startup` (Production, Sandbox, Preview), `Each_missing_setting_is_named` |
| 6 | Opaque `__Host-` cookie, HttpOnly, Secure=Always, SameSite=Lax | MUST | ✅ | `AuthConfiguration.cs:24,87-91` — `__Host-template`, `HttpOnly`, `SecurePolicy.Always`, `SameSite.Lax`, `Path=/`, no `Domain`. Code only: the integration tests replace the cookie handler (`AppFixture.cs:67-71`), so no test reads the session `Set-Cookie` |
| 7 | Sessions stored server side via `ITicketStore` | MUST | ✅ | `Auth/SessionTicketStore.cs:34-71` (random 32-byte key, ticket protected with Data Protection), wired in `SessionStorePostConfigure`; storage per variant in `MartenAuthSessions.cs` and `EfAuthSessions.cs`. Tests: `SessionTicketStoreTests` (8 tests), `PersistenceTests.The_ticket_store_works_end_to_end_on_the_real_storage` in both variants |
| 8 | Short sliding lifetime, value and reason documented | MUST | ✅ | `AuthConfiguration.cs:28-30` — 8 hours sliding, reason in comment; `SlidingExpiration = true` at `:95`. `SessionTicketStore.cs:36` repeats the 8 h as a fallback constant |
| 9 | Back-channel logout validates token, deletes by `iss`+`sid` | MUST | ✅ | `AuthEndpoints.cs:55-67`, `BackchannelLogoutValidator.cs:27-58` (signature, issuer, audience, lifetime, `iat`, `sid`, `events`, no `nonce`). Tests: `BackchannelLogoutValidatorTests` (8 tests), `CsrfTests.Back_channel_logout_refuses_a_bad_token_without_saying_why`, `PersistenceTests.A_session_survives_the_database_and_back_channel_logout_removes_it` (both variants) |
| 10 | Logout is a POST with antiforgery token | MUST | ✅ | `AuthEndpoints.cs:32-35` calls `AntiforgeryMiddleware.CheckAsync`. Tests: `CsrfTests.Logout_is_a_post_that_needs_the_token_and_a_get_does_not_log_out`, `Logout_needs_a_session`; client in `frontend/src/lib/logout.ts` (tested by `logout.test.ts`) |
| 11 | "Who am I" returns only what the SPA needs | MUST | ✅ | `Features/CurrentUser/GetMe.cs` in both variants returns `DisplayName` and `Roles`. Test: `NotesApiTests.Me_returns_the_display_name_and_roles_and_nothing_else` |
| 12 | `/api` gets 401/403, never a redirect | MUST | ✅ | Cookie events `AuthConfiguration.cs:97-106`; OIDC `OnRedirectToIdentityProvider` `:133-142`. Test: `SecurityTests.Without_a_session_an_api_call_answers_401_and_not_a_redirect` |
| 13 | SPA sends 401 to login with return URL; server checks it is local | MUST | ✅ | `frontend/src/lib/templateFetch.ts:58-61` (test `sends an expired session to login and comes back to the same place`); `/user/login` uses `LocalRedirectGuard.SafeOrRoot` (`AuthEndpoints.cs:24-27`). Tests: `LocalRedirectGuardTests` |
| 14 | Data Protection keys shared in Postgres, KMS-encrypted | MUST | ✅ | `DataProtection/DataProtectionConfiguration.cs:19-38` (key store from the variant's database; KMS encryptor; throws outside Dev/Test without `Gcp:DataProtectionKmsKeyName`); `KmsXmlEncryptor.cs`, `KmsXmlDecryptor.cs`. Test: EF Core `PersistenceTests.The_key_ring_is_stored_in_the_database_and_a_second_instance_reads_it` (no Marten equivalent; KMS classes untested) |
| 15 | Own JWT bearer scheme per machine caller | MUST | N/A | No machine callers; no `JwtBearer` in `api/` or `Directory.Packages.props` |
| 16 | Internal calls use Google-signed OIDC tokens per caller | MUST | N/A | No Pub/Sub push, Cloud Tasks or Scheduler endpoints |
| 17 | Reject cookie + bearer for different persons | MUST | N/A | Only the cookie scheme is registered |
| 18 | Test-only auth off by default, never in production | MUST | N/A | The API has no test-only auth path. The header handler exists only in the test fixtures (`AppFixture.cs:75-90`); the OIDC mock is a separate local/preview service (`api/OidcMock/`), not an API switch |

## Gaps to close

- **4**: Add an admin MFA step-up in `OnTokenValidated` (`api/BccCode.Template.Web/Auth/AuthConfiguration.cs`), with the
  decision in a pure function and a unit test, following orginn's `ShouldStepUpAdmin`. Check first which `acr`/`amr` values
  BCC Signon returns.
- **6** (test coverage, not a rule failure): add a test that runs the real cookie handler and asserts the session
  `Set-Cookie` has `__Host-`, `path=/`, `secure`, `httponly`, `samesite=lax` and no `domain`. Today the flags are only visible
  in code because `AppFixture` replaces authentication.
- **14** (test coverage): add a Marten counterpart to the EF Core key-ring test, and a unit test for the KMS encryptor/decryptor
  round trip with a fake `KeyManagementServiceClient`.

## Not verifiable in the repo

- Whether BCC Signon issues `sid` in the ID token and is configured to call `/user/backchannel-logout`; check the client
  registration in the identity provider.
- Whether the deployed environments set `Gcp:DataProtectionKmsKeyName` and the OIDC secret; `infra/cloudrun_api.tf` and
  `infra/kms.tf` define them, the running service must be checked in GCP.
