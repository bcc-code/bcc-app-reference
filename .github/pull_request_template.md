## What and why

<!-- What does this change, and why? Link the issue or ClickUp task. -->

## How it was checked

<!-- Tests added or changed, manual checks, screenshots for UI changes. -->

## Privacy

<!-- Required when the change adds or changes processing of personal data (standard 18 rule 3). Otherwise write "No change". -->

- Data:
- Purpose:
- Stored where:
- Retention and deletion:
- Who can see it:
- `docs/privacy/data-inventory.md` updated: yes / no / not needed

## Checklist

- [ ] OpenAPI document and generated client are regenerated when the API changed
- [ ] New endpoints have an explicit authorization policy (default deny) and an ownership check where data belongs to a user
- [ ] No personal identifiers, message bodies or full URLs with query strings in logs
- [ ] Out-of-scope findings are recorded in `docs/tech-debt/`, not fixed in this PR
- [ ] A decision that others will ask about has an ADR in `docs/adrs/`
