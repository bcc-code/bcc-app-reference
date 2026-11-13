# 0029. TimeProvider as the clock

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.

## Context

bcc-ateam chose NodaTime for dates and times, with `IClock` as its clock (its ADR 0028, Proposed there). Its domain has calendar dates, Oslo-local rules ("once per Oslo week") and several kinds of stored time, where `DateTimeOffset` cannot say whether a value is a date or an instant. It pays for that with a deviation from standard 11.7, a `TimeProvider` adapter over `IClock` so framework code and domain code share one clock, two JSON setups and the NodaTime support packages for Marten and OpenAPI.

The template has no such domain. Its data holds instants (a note was created, edited, archived) and the time of a change. Standard 11.7 says domain code gets the current time from `TimeProvider`; ASP.NET Core, the cookie authentication and token validation take it too, and `FakeTimeProvider` freezes all of them in a test.

## Decision

We will use `TimeProvider` as the one clock and `DateTimeOffset` for stored instants. We will not use NodaTime in the template.

- Code gets the time from an injected `TimeProvider`, never from `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.UtcNow`. `BannedSymbols.txt` and the banned-API analyzer make them a build error (RS0030), and the feature project also bans `TimeProvider.System`, which only the host's composition root may use.
- Tests use `FakeTimeProvider`. The seeder passes its own clock, so seeded history can carry earlier times.
- A change records its time from `TimeProvider` ([0028](0028-relational-domain-data-with-typed-history.md)).
- An application whose domain needs calendar dates or time zone rules decides for itself, with an ADR that supersedes this one. bcc-ateam's ADR 0028 and its dates-and-times guide are the starting point.

## Consequences

| | |
|---|---|
| Positive | Follows standard 11.7 with no deviation. No extra packages, no second JSON setup, no clock adapter. A new contributor already knows the types. |
| Negative | A date and an instant are both `DateTimeOffset` or `DateOnly`, so the type does not stop a mix-up, and Oslo-local rules use `TimeZoneInfo`. |
| Follow-up | Revisit when an application in the template's family needs calendar dates in several places. |

## Alternatives considered

- **NodaTime with `IClock` (bcc-ateam, ADR 0028)** — a better fit for a domain with calendar dates and zone rules, but it deviates from standard 11.7 and the template's sample has no use for it.
- **NodaTime types with `TimeProvider` as the clock** — keeps 11.7, but every call site converts, and two sets of testing types are in use.
