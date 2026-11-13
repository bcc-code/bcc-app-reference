# 23 Documentation and Decisions

This standard sets where documentation lives in a repo, which kinds we write, and how design proposals and decisions are recorded. A fixed layout lets developers and agents find the same kind of document in the same place in every repo, and a maintained decision record explains why the code looks the way it does after the people who decided have moved on.

Documentation that is wrong costs more than documentation that is missing. Decision records need a status that is kept up to date, and they are written when the decision is made. Specs describe a design before it is built and must be updated or marked superseded once the code diverges. Recording a decision after the implementation is finished loses the alternatives that were considered at the time.

## Rules

### Entry points

1. **`README.md` MUST be short and correct: purpose, how to run it, ports, and links into `docs/`.** It is the first file a new developer reads; a wrong command there costs everyone. Check it when tooling versions change (for example, a package manager version named in the README that differs from the one pinned in `package.json`).
2. **`AGENTS.md` MUST be the developer and agent guide** (commands, architecture rules, gotchas). See [24 AI Agent Tooling](24-ai-agent-tooling.md).

### Layout

3. **`docs/` MUST use this layout; create a folder when the first document of that kind is written:**

| Folder           | Content                                                                                                         | Audience                         |
| ---------------- | --------------------------------------------------------------------------------------------------------------- | -------------------------------- |
| `adrs/`          | Architecture decision records                                                                                   | Developers                       |
| `architecture/`  | C4 diagrams as editable source + exported SVG, "as built" overview                                              | Developers                       |
| `runbooks/`      | Operational how-tos linked from alerts ([22 Observability and Operations](22-observability-and-operations.md))  | On-call                          |
| `specs/`         | Design specs/RFCs written before implementation                                                                 | Developers, product              |
| `tech-debt/`     | One file per known issue                                                                                        | Developers, agents               |
| `development/`   | Developer tutorials, how-tos, reference and explanation, Diátaxis                                               | Developers                       |
| `manual/`        | End-user and admin handbook, Diátaxis                                                                           | End users                        |
| `notes/`         | Customer notes, meeting notes and evaluations, in dated subfolders                                              | Developers, product              |
| `public/`        | Integrator docs, auto-published to the developer portal                                                         | External developers              |
| `security/`      | Pen-test reports with links to fix commits, threat models                                                       | Developers, security             |
| `data-protection/` | Data inventory and retention ([18 Personal Data and GDPR](18-personal-data-and-gdpr.md))                      | Developers, data protection team |

4. **C4 diagrams MUST be kept as editable source next to an exported SVG**: Structurizr DSL (optionally served from the AppHost) or Excalidraw source + SVG. A PNG without source cannot be updated.
5. **Engineering docs MUST be kept separate from customer notes, meeting notes and evaluations.** Put those under a dated subfolder (e.g. `docs/notes/`) or outside the repo, so the root of `docs/` stays navigable.

### Proposals and decisions

6. **Significant changes MUST start as a spec/RFC in `docs/specs/`** with context, constraints, options, a decision log and open questions with owners, before implementation. A change is significant when it is hard to reverse or affects more than one team or service: a new service or data store, a change to an API or event contract, an authentication or data-protection change, or a new cross-cutting dependency. The flow is RFC, then decision, then ADR. The decision MAY be taken in a meeting or asynchronously in the RFC review, as long as it is recorded in the decision log (see [Understand the Problem Before Choosing Architecture](../principles/code-design.md#understand-the-problem-before-choosing-architecture) and [Ways of working](../principles/ways-of-working.md)).
7. **ADRs MUST use the Nygard format (Status, Context, Decision, Consequences) and SHOULD include a consequences table and alternatives considered.**
8. **ADRs MUST be written when the decision is made, not after implementation, and their status MUST be maintained:** Proposed, then Accepted or Rejected, later Superseded by a linked ADR. An ADR that stays Proposed while the code follows it, or has an empty Decision section, tells the reader nothing reliable.
9. **A spec MUST either be kept current or be marked as superseded by the ADR(s) that record what was built.** Specs that describe a design the code no longer follows mislead agents as much as people.
10. **Agent-generated plans and specs (for example under `docs/superpowers/`, where the Superpowers agent skills write them) MAY be kept, but they MUST NOT replace ADRs.** They have no status and are written for an agent executing a plan, not for a reader asking why.

### User and developer docs

11. **User docs and developer how-tos MUST follow Diátaxis** (tutorials, how-to, reference, explanation).
12. **A docs checker SHOULD run in CI** to enforce the structure (no instructions in reference/explanation, UI labels match the app, AI disclaimers, anchors).
13. **Integrator documentation in `docs/public/` SHOULD be published automatically to the BCC developer portal on push to `main`.**

### Tech debt

14. **Known issues MUST be recorded as one file per issue in `docs/tech-debt/` with frontmatter `status`, `effort`, `priority`, `scope`, `area`.** There MUST be no index file (every branch would append to it and conflict), and the file MUST be deleted in the commit that fixes the issue; git history keeps the record. The folder MUST have a `README.md` that defines the convention for that repo, and the files MUST NOT contain personal data (refer to test personas or placeholders). Agents follow the same rule ([24 AI Agent Tooling](24-ai-agent-tooling.md)). A suggested convention is in the examples.

### AI-drafted documents

15. **Documents drafted with AI MUST say so at the top and be marked for human verification** until someone has checked them against code and reality. This is required for data protection, security and legal content.

## Examples

### README (rules 1, 2)
A good README covers purpose, flows, stack, prerequisites, run instructions for the CLI, VS Code, Rider and devcontainer, what starts and on which ports, C4 container and component SVGs, deployment, and an index of `docs/`.

### ADR with consequences (rules 7, 8)
Write the ADR when the decision is made. An ADR that carries a line such as "Recorded after implementation" is the gap rule 8 addresses; the format below is otherwise right.

```markdown
# ADR 0013: Retention and purge of personal data

- Status: accepted
- Date: 2026-10-14
- Context: The decision was made in the spec decision log ...
```

### Specs before code (rules 6, 9)
`docs/specs/` holds numbered specs (01, 02, ...) and a `README.md` with a decision log and a "verify before implementing" table with an owner per item. When a spec stops matching the code, mark it as superseded by the ADR that records what was built.

### C4 as source (rule 4)
Either a Structurizr workspace (`docs/architecture/workspace.dsl`, served from the AppHost on a fixed port) or Excalidraw files (`docs/architecture/{system-landscape,container,component}.excalidraw`) with an exported `.svg` next to each.

### Diátaxis trees with a checker (rules 11, 12)
A small script lists each documentation tree, its quadrants, where instructions are not allowed, and whether UI labels are checked against the app. Run it in CI.

```js
const TREES = [
  { dir: 'docs/manual',
    quadrants: ['tutorials', 'how-to', 'reference', 'explanation'],
    noInstructions: ['reference', 'explanation'], checkLabels: true, ... },
  { dir: 'docs/development',
    quadrants: ['tutorials', 'how-to', 'reference', 'explanation'], ... },
]
```

### Public docs auto-published (rule 13)
```yaml
on:
  push:
    branches: ["main"]
    paths:
      - docs/public/**
...
      - uses: bcc-code/bcc-developer-docs-deploy@<sha> # v1
        with:
          docs-dir: docs/public
```

Pin the action to a commit SHA, as in [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).

### Tech-debt convention (rule 14)
The `docs/tech-debt/README.md` states: "There is deliberately **no index file**. A committed list of every issue is the one thing every branch would append to, so a shared list conflicts on merges." Start a new repo from an existing README and replace the `scope` values. A suggested convention:

- **When to create one.** You notice something worth fixing that is outside the task you are on. Do not fix it inline and do not widen the PR; add the file and stay on the task. Subagents return findings, and the coordinating agent dedupes and writes the files.
- **File name.** A kebab-case slug describing the problem, not the fix (`order-lists-show-empty-state-while-loading.md`). It does not change once committed, so links and searches keep working.
- **Frontmatter.** `title` (one sentence naming the problem), `status` (`open` · `needs-decision` · `blocked`), `blocked-on` when blocked, `decision-by` when someone specific must decide, `effort` (`XS` < 1 h · `S` half a day · `M` a day or two · `L` multi-day or needs design), `priority` (`P0` blocks a release or deploy · `P1` wrong behaviour a user will hit · `P2` real defect, not urgent · `P3` hygiene), `scope` (where the fix lands; the values are defined per repo, e.g. `orders | admin | frontend | infra | general`), `area` (topic such as `correctness`, `permissions`, `performance`, `testing`; add a value if none fits). The fields are a triage pass, open to argument.
- **Body.** A `# Title` heading, then what is wrong, where (`path/to/file.ext:line`), what you ruled out, and a direction if you have one. Enough that someone picking it up cold does not have to rediscover it.
- **Spanning scopes.** Pick the scope where most of the fix lands and mention the others; split into cross-referencing files only when the parts can be fixed separately.
- **No personal data.** Refer to test personas or placeholders, never real people.
- **Line citations.** When picking up an issue, check that the cited line still shows what it is cited for, not only that the number exists.
- **Querying.** Use search instead of an index: `rg -l '^status: open' docs/tech-debt`, `rg -l '^priority: P[01]' docs/tech-debt`, `rg '^title:' docs/tech-debt/*.md`. Closed issues: `git log --diff-filter=D -- docs/tech-debt/`.
- **Relation to ClickUp.** A tech-debt file records the diagnosis next to the code. When the work is planned, create a ClickUp task that links the file; delete the file when the fix merges.

An issue file (`docs/tech-debt/order-lists-show-empty-state-while-loading.md`):

```markdown
---
title: "The order lists show their empty state while the first request is in flight"
status: open
effort: S
priority: P3
scope: general
area: correctness
---

# The order lists show their empty state while the first request is in flight

`OrderList` has no loading state, and its callers render it as soon as the query has no error, ...

- `Frontend/src/components/orders/List.vue:30`
```

### Pen-test report (rule 3)
`docs/security/penetration-test-2026-06.md` lists each finding with the commit that fixed it.

### AI-drafted marker (rule 15)
Put a status block at the top of the document: "Status: draft for review. This inventory was drafted with AI assistance ... It must be verified by a human against the code and the deployed environment before ..."

## Related
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — data inventory and retention documents
- [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md) — images in user docs
- [22 Observability and Operations](22-observability-and-operations.md) — runbooks linked from alerts
- [24 AI Agent Tooling](24-ai-agent-tooling.md) — `AGENTS.md` and agent handling of tech debt
- [Code design principles](../principles/code-design.md) — comment the why, understand the problem before choosing architecture
- [Ways of working](../principles/ways-of-working.md) — RFC and ADR flow, Diátaxis
