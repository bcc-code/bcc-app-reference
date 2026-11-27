---
title: "Two administrators granting the admin role to the same new person at once can answer 500"
status: open
effort: S
priority: P3
scope: api
area: correctness
---

# Two administrators granting the admin role to the same new person at once can answer 500

`GrantAdminEndpoint` (`api/BccCode.Template/Admin/GrantAdmin.cs`) inserts a `role_grants` row when the person has none. If two administrators grant the same person for the first time within the same moment, both load "no grant", and the second insert hits the primary key (`person_id`, `role`). The conflict policy answers a concurrency conflict with 409, but a unique violation on insert is not one, so the loser gets 500 instead of the 204 the repeat of a grant should give. Nothing is stored twice and the winner's grant is correct; the loser only sees an error and can repeat the call.

To close it:

- Answer a unique violation on that insert like a conflict (409) in `ConflictPolicy`, or make the insert tolerate an existing row (`INSERT ... ON CONFLICT DO NOTHING`), whichever the team prefers for set-state operations.
- Add an integration test that grants one new person from two requests at once and expects no 500.
