## Summary

<!-- What does this change, and why? Link the issue or ClickUp task. -->

## Changes

<!-- The notable changes, by area. Screenshots for UI changes. -->

## Review notes

<!-- What a reviewer should look at first, decisions that could go the other way, and anything left out on purpose. -->

## Testing

<!-- Tests added or changed, and manual checks. -->

## Privacy

<!-- Required when the change adds or changes processing of personal data (standard 18 rule 3). Otherwise write "No change". -->

- Data:
- Purpose:
- Stored where:
- Retention and deletion:
- Who can see it:
- `docs/data-protection/data-inventory.md` updated: yes / no / not needed

## Checklist

- [ ] OpenAPI document and generated client are regenerated when the API changed
- [ ] New endpoints have an explicit authorization policy (default deny) and an ownership check where data belongs to a user
- [ ] No personal identifiers, message bodies or full URLs with query strings in logs
- [ ] Out-of-scope findings are recorded in `docs/tech-debt/`, not fixed in this PR
- [ ] A decision that others will ask about has an ADR in `docs/adrs/`
