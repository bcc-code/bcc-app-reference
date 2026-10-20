---
standard: 14 Authentication and Sessions
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 14 Authentication and Sessions — assessment

The BFF sign-in, server-side session store, back-channel logout and CSRF-protected logout are implemented in the shared
`BccCode.Template.Web` project and tested. The internal routes called by Pub/Sub push and Cloud Scheduler use a separate
`GoogleOidc` bearer scheme with one policy per caller, and the local switch that opens them is refused outside Development and
Test. Admin MFA step-up is missing, and the session cookie flags are set in code but no test asserts them.

**Score: Strong** · ✅ 15 · ⚠️ 0 · ❌ 1 · N/A 2

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | BFF with OIDC code flow, PKCE and PAR | MUST | ✅ | `api/BccCode.Template.Web/Auth/AuthConfiguration.cs:114-116` — `ResponseType = Code`, `UsePkce = true`, `PushedAuthorizationBehavior.UseIfAvailable`. Browser only gets a cookie (ADR 0004) |
| 2 | `SaveTokens = false` unless calling APIs for the user | MUST | ✅ | `AuthConfiguration.cs:117` — `SaveTokens = false`, `GetClaimsFromUserInfoEndpoint = false` |
| 3 | No inbound claim mapping, minimal scopes, slim principal | MUST | ✅ | `AuthConfiguration.cs:119-122` (`MapInboundClaims = false`, scopes `openid profile`, no `offline_access`); `Auth/SessionClaims.cs:33-45` keeps `sub name role sid iss`. Tests: `SessionClaimsTests.Slimming_keeps_what_the_app_reads_and_adds_a_missing_issuer`, `SessionTicketStoreTests.Only_the_claims_the_app_reads_are_kept` |
| 4 | Admins stepped up to MFA before the session is written | SHOULD | ❌ | No step-up in `OnTokenValidated` (`AuthConfiguration.cs:128-132`); no `mfa`/`acr` handling anywhere in `api/` |
| 5 | Startup fails without OIDC authority/client/secret/audience outside Dev/Test | MUST | ✅ | `AuthConfiguration.cs:32,77-83` throws for `OIDC:Authority`, `OIDC:ClientId`, `OIDC:ClientSecret`. The `GoogleOidc` bearer scheme requires both audiences and both service-account emails (`Internal/InternalAuthConfiguration.cs:77-82,101-104`) unless `Internal:AllowAll` is set, which only Development and Test accept. Tests: `OidcConfigurationTests.A_deployed_environment_without_oidc_settings_fails_at_startup` (Production, Sandbox, Preview), `Each_missing_setting_is_named`, `InternalAuthConfigurationTests.A_missing_setting_stops_the_host_and_names_the_key`, `InternalRoutesTests.A_host_without_the_internal_settings_and_without_AllowAll_does_not_start` (both variants) |
| 6 | Opaque `__Host-` cookie, HttpOnly, Secure=Always, SameSite=Lax | MUST | ✅ | `AuthConfiguration.cs:24,87-91` — `__Host-template`, `HttpOnly`, `SecurePolicy.Always`, `SameSite.Lax`, `Path=/`, no `Domain`. Code only: the integration tests replace the cookie handler (`AppFixture.cs:67-71`), so no test reads the session `Set-Cookie` |
| 7 | Sessions stored server side via `ITicketStore` | MUST | ✅ | `Auth/SessionTicketStore.cs:34-71` (random 32-byte key, ticket protected with Data Protection), wired in `SessionStorePostConfigure`; storage per variant in `MartenAuthSessions.cs` and `EfAuthSessions.cs`. Tests: `SessionTicketStoreTests` (8 tests), `PersistenceTests.The_ticket_store_works_end_to_end_on_the_real_storage` in both variants |
| 8 | Short sliding lifetime, value and reason documented | MUST | ✅ | `AuthConfiguration.cs:28-30` — 8 hours sliding, reason in comment; `SlidingExpiration = true` at `:95`. `SessionTicketStore.cs:36` repeats the 8 h as a fallback constant |
| 9 | Back-channel logout validates token, deletes by `iss`+`sid` | MUST | ✅ | `AuthEndpoints.cs:55-67`, `BackchannelLogoutValidator.cs:27-58` (signature, issuer, audience, lifetime, `iat`, `sid`, `events`, no `nonce`). Tests: `BackchannelLogoutValidatorTests` (8 tests), `CsrfTests.Back_channel_logout_refuses_a_bad_token_without_saying_why`, `PersistenceTests.A_session_survives_the_database_and_back_channel_logout_removes_it` (both variants) |
| 10 | Logout is a POST with antiforgery token | MUST | ✅ | `AuthEndpoints.cs:32-35` calls `AntiforgeryMiddleware.CheckAsync`. Tests: `CsrfTests.Logout_is_a_post_that_needs_the_token_and_a_get_does_not_log_out`, `Logout_needs_a_session`; client in `frontend/src/lib/logout.ts` (tested by `logout.test.ts`) |
| 11 | "Who am I" returns only what the SPA needs | MUST | ✅ | `Features/CurrentUser/GetMe.cs` in both variants returns `DisplayName` and `Roles`. Test: `NotesApiTests.Me_returns_the_display_name_and_roles_and_nothing_else` |
| 12 | `/api` gets 401/403, never a redirect | MUST | ✅ | Cookie events `AuthConfiguration.cs:97-106`; OIDC `OnRedirectToIdentityProvider` `:133-142`. Test: `SecurityTests.Without_a_session_an_api_call_answers_401_and_not_a_redirect` |
| 13 | SPA sends 401 to login with return URL; server checks it is local | MUST | ✅ | `frontend/src/lib/templateFetch.ts:58-61` (test `sends an expired session to login and comes back to the same place`); `/user/login` uses `LocalRedirectGuard.SafeOrRoot` (`AuthEndpoints.cs:24-27`). Tests: `LocalRedirectGuardTests` |
| 14 | Data Protection keys shared in Postgres, KMS-encrypted | MUST | ✅ | `DataProtection/DataProtectionConfiguration.cs:19-38` (key store from the variant's database; KMS encryptor; throws outside Dev/Test without `Gcp:DataProtectionKmsKeyName`); `KmsXmlEncryptor.cs`, `KmsXmlDecryptor.cs`. Test: EF Core `PersistenceTests.The_key_ring_is_stored_in_the_database_and_a_second_instance_reads_it` (no Marten equivalent; KMS classes untested) |
| 15 | Own JWT bearer scheme per machine caller | MUST | N/A | No external machine callers (M2M clients, widgets, webhooks with tokens). The only bearer scheme is for Google-signed internal calls, which rule 16 covers. It is not a default scheme and only the two internal policies use it (`Internal/InternalAuthConfiguration.cs:16-20,84-98`) |
| 16 | Internal calls use Google-signed OIDC tokens per caller | MUST | ✅ | `GoogleOidc` JWT bearer scheme with authority `https://accounts.google.com` and both audiences; policies `InternalPubSub` and `InternalScheduler` each require their own `aud`, `email` and `email_verified = true` and accept only that scheme (`Internal/InternalAuthConfiguration.cs:84-98`). Every route under `/internal` must carry the policy its path calls for, or startup fails (`Internal/InternalRouteGuard.cs`). Terraform gives each caller its own account and audience (`infra/pubsub.tf`, `infra/scheduler.tf`). Tests: `InternalAuthTests` (configured caller let in; no token, a signed-in user, a token from another key, a non-JWT and an unknown audience 401; wrong account, the other caller's audience and an unverified email 403), `InternalRouteGuardTests`, `InternalRoutesTests.Without_AllowAll_a_call_with_no_token_and_a_signed_in_user_are_both_401` (both variants) |
| 17 | Reject cookie + bearer for different persons | MUST | N/A | No policy accepts both schemes: user policies use the cookie, the internal policies use only `GoogleOidc` (`AddAuthenticationSchemes(GoogleOidcScheme)`), so two identities cannot merge. Test: `InternalAuthTests.No_token_is_401_even_with_a_signed_in_user_as_the_default` |
| 18 | Test-only auth off by default, never in production | MUST | ✅ | `Internal:AllowAll` opens both internal policies for the local Pub/Sub emulator, which sends no tokens. It is off unless set, the AppHost and the test fixtures set it, and the host refuses to start with it in any environment other than Development and Test (`Internal/InternalAuthConfiguration.cs:47-51`); `infra/cloudrun_api.tf` never sets it. Tests: `InternalAuthConfigurationTests.AllowAll_is_refused_outside_development_and_test`, `AllowAll_opens_both_policies_in_development_and_test`. No Terraform precondition mirrors the check (standard 03 rule 15 would allow one). The user-side header handler exists only in the test fixtures (`AppFixture.cs`); the OIDC mock is a separate local/preview service (`api/OidcMock/`) |

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
- Rule 16: that real Google-signed tokens from Pub/Sub push and Cloud Scheduler pass the scheme and policies (claim names and
  the `email_verified` value as Google sends them). The tests sign tokens with a local key in the same shape.
- Whether the deployed environments set `Gcp:DataProtectionKmsKeyName` and the OIDC secret; `infra/cloudrun_api.tf` and
  `infra/kms.tf` define them, the running service must be checked in GCP.
