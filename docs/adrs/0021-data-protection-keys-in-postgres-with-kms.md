# 0021. Data Protection keys in Postgres, wrapped with Cloud KMS by in-house code

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0023, with the key ring in an EF Core table.

## Context

Session tickets and the session cookie are protected with ASP.NET Core Data Protection. Cloud Run runs several instances and replaces them on every deploy, so the key ring must be shared and durable. Standards 04.11 and 14.14 require the keys in Postgres or a GCS bucket, encrypted with Cloud KMS, and startup to fail outside Development and Test without the KMS key. Google's `Google.Cloud.AspNetCore.DataProtection.Kms` package never left alpha and its repository is archived.

## Decision

We will store the Data Protection key ring in a table in our own Postgres database and wrap each key with a Cloud KMS key.

- The code is in-house and says so (04.11). It is small, has no app-specific dependencies and is unit-tested with a fake KMS client, so it can move into a shared package later.
- Terraform creates the key ring and a crypto key with 90-day rotation and `prevent_destroy`. Only the API's runtime account may encrypt and decrypt with it.
- `Gcp:DataProtectionKmsKeyName` is required outside Development and Test. Locally and in tests the keys are stored unencrypted in the throwaway database.
- The application name is the app's slug, so another app on the shared instance cannot read the keys even with access to the table.
- Keys are not personal data, but they decrypt session tickets, so the table is in the data inventory.

## Consequences

| | |
|---|---|
| Positive | No new store: the database and its IAM access already exist, and the schema step creates the table. A database dump does not give up the keys. |
| Negative | We maintain about 60 lines of encryption glue that a vendor package would normally provide. Losing the KMS key makes every session invalid, which is why it has `prevent_destroy`. |
| Follow-up | A shared BCC package for the KMS encryptor would replace the copies in each app. |

## Alternatives considered

- **`Google.Cloud.AspNetCore.DataProtection.Kms`** — the API the standard names, but alpha and archived: no fixes or security updates will come.
- **GCS bucket or Secret Manager without app-side encryption** — encryption at rest only. Anyone who can read the objects gets the keys in plain text, which 04.11 is meant to prevent.
- **Keys in a GCS bucket with our KMS encryptor** — works equally well, but adds a bucket and its IAM for no gain over the database we already have.
