# Tech debt and out-of-scope follow-ups

One file per known issue. There are none yet. When an issue is resolved, delete its file in the same commit as the fix. Git
history keeps the record: `git log --diff-filter=D -- docs/tech-debt/` lists what was closed.

There is deliberately **no index file**. A committed list of every issue is the one file every branch would append to, so it
would conflict on almost every merge. Query the folder instead:

```bash
rg -l '^status: open' docs/tech-debt           # everything ready to work
rg -l '^priority: P[01]' docs/tech-debt         # what matters most
rg -l '^scope: infra' docs/tech-debt            # one scope
rg '^title:' docs/tech-debt/*.md                # a listing, generated on demand
```

## Adding an issue

You noticed something worth fixing that is outside the task you are on. Do not fix it inline and do not widen the PR: add a
file here and stay on the task. Subagents return findings to the coordinating agent, which removes duplicates and writes the
files.

The file name is a kebab-case slug that describes the problem, not the fix (`note-list-shows-empty-state-while-loading.md`). It
does not change once committed, so links and searches keep working.

Frontmatter:

```yaml
---
title: "One sentence naming the problem"
status: open              # open | needs-decision | blocked
blocked-on: "..."         # only when status is blocked
decision-by: "..."        # only when status is needs-decision and someone specific must decide
effort: S                 # XS < 1 h | S half a day | M a day or two | L multi-day or needs design
priority: P2              # P0 blocks a release or deploy | P1 wrong behaviour a user will hit | P2 real defect, not urgent | P3 hygiene
scope: general            # where the fix lands: api | frontend | infra | docs | general (add values as the project needs them)
area: correctness         # topic: correctness, permissions, performance, testing, privacy, deployment (add if none fits)
---
```

Then a `# Title` heading and the body: what is wrong, where (`path/to/file.ext:line`), what you ruled out, and a direction if
you have one. Write enough that someone picking it up cold does not have to rediscover it.

- **Spanning scopes.** Pick the scope where most of the fix lands and mention the others. Split into cross-referencing files
  only when the parts can be fixed separately.
- **No personal data.** Refer to test personas or placeholders, never real people.
- **Line citations rot.** When picking an issue up, check that the cited line still shows what it is cited for, not only that
  the line number exists.
- **ClickUp.** The file records the diagnosis next to the code. When the work is planned, create a ClickUp task that links the
  file, and delete the file when the fix merges.
