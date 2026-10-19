# 23 Documentation and Decisions

This standard sets where documentation lives in a repo, which kinds we write, and how design proposals and decisions are recorded. A fixed layout lets developers and agents find the same kind of document in the same place in every repo, and a maintained decision record explains why the code looks the way it does after the people who decided have moved on.

## Rules

### Entry points

1. **`README.md` MUST be short and correct: purpose, how to run it, ports, and links into `docs/`.** It is the first file a new developer reads; a wrong command there costs everyone. Check it when tooling versions change.
2. **`AGENTS.md` MUST be the developer and agent guide** (commands, architecture rules, gotchas). See [24 AI Agent Tooling](24-ai-agent-tooling.md).

### Layout

3. **`docs/` MUST use this layout; create a folder when the first document of that kind is written:**

| Folder          | Content                                                                      | Audience                 |
| --------------- | ---------------------------------------------------------------------------- | ------------------------ |
| `adrs/`         | Architecture decision records                                                | Developers               |
| `architecture/` | C4 diagrams as editable source + exported SVG, "as built" overview           | Developers               |
| `runbooks/`     | Operational how-tos linked from alerts ([22 Observability and Operations](22-observability-and-operations.md)) | On-call                  |
| `specs/`        | Design specs/RFCs written before implementation                              | Developers, product      |
| `tech-debt/`    | One file per known issue                                                     | Developers, agents       |
| `manual/`       | End-user and admin handbook, Diátaxis                                        | End users                |
| `public/`       | Integrator docs, auto-published to the developer portal                      | External developers      |
| `security/`     | Pen-test reports with links to fix commits, threat models                    | Developers, security     |
| `privacy/`      | Data inventory and retention ([18 Privacy and GDPR](18-privacy-and-gdpr.md))                       | Developers, privacy team |

4. **C4 diagrams MUST be kept as editable source next to an exported SVG**: Structurizr DSL (optionally served from the AppHost) or Excalidraw source + SVG. A PNG without source cannot be updated.
5. **Engineering docs MUST be kept separate from customer notes, meeting notes and evaluations.** Put those under a dated subfolder (e.g. `docs/notes/`) or outside the repo, so the root of `docs/` stays navigable.

### Proposals and decisions

6. **Significant changes MUST start as a spec/RFC in `docs/specs/`** with context, constraints, options, a decision log and open questions with owners, before implementation. The flow is RFC → decision meeting → ADR, as described in Making Decisions - RFC and ADR.
7. **ADRs MUST use the Nygard format (Status, Context, Decision, Consequences) and SHOULD include a consequences table and alternatives considered.**
8. **ADRs MUST be written when the decision is made, not after implementation, and their status MUST be maintained:** Proposed → Accepted or Rejected, later Superseded by a linked ADR. An ADR that stays Proposed while the code follows it, or has an empty Decision section, tells the reader nothing reliable.
9. **A spec MUST either be kept current or be marked as superseded by the ADR(s) that record what was built.** Specs that describe a design the code no longer follows mislead agents as much as people.
10. **Agent-generated plans and specs (`docs/superpowers/`) MAY be kept, but they MUST NOT replace ADRs.** They have no status and are written for an agent executing a plan, not for a reader asking why.

### User and developer docs

11. **User docs and developer how-tos MUST follow Diátaxis** (tutorials, how-to, reference, explanation). See Documentation (Diátaxis).
12. **A docs checker SHOULD run in CI** to enforce the structure (no instructions in reference/explanation, UI labels match the app, AI disclaimers, anchors).
13. **Integrator documentation in `docs/public/` SHOULD be published automatically to the BCC developer portal on push to `main`.**

### Tech debt

14. **Known issues MUST be recorded as one file per issue in `docs/tech-debt/` with frontmatter `status`, `effort`, `priority`, `scope`, `area`.** There MUST be no index file (every branch would append to it and conflict), and the file MUST be deleted in the commit that fixes the issue; git history keeps the record. The folder has a `README.md` that defines the convention for that repo (orginn and bcc-privacy have near-identical ones). The convention:
    - **When to create one.** You notice something worth fixing that is outside the task you are on. Do not fix it inline and do not widen the PR; add the file and stay on the task. Agents follow the same rule ([24 AI Agent Tooling](24-ai-agent-tooling.md)): subagents return findings, and the coordinating agent dedupes and writes the files.
    - **File name.** A kebab-case slug describing the problem, not the fix (`activity-lists-show-empty-state-while-loading.md`). It does not change once committed, so links and searches keep working.
    - **Frontmatter.** `title` (one sentence naming the problem), `status` (`open` · `needs-decision` · `blocked`), `blocked-on` when blocked, `decision-by` when someone specific must decide, `effort` (`XS` < 1 h · `S` half a day · `M` a day or two · `L` multi-day or needs design), `priority` (`P0` blocks a release or deploy · `P1` wrong behaviour a user will hit · `P2` real defect, not urgent · `P3` hygiene), `scope` (where the fix lands; the values are defined per repo, e.g. `consents | admin | frontend | infra | general`), `area` (topic such as `correctness`, `permissions`, `performance`, `testing`; add a value if none fits). The fields are a triage pass, open to argument.
    - **Body.** A `# Title` heading, then what is wrong, where (`path/to/file.ext:line`), what you ruled out, and a direction if you have one. Enough that someone picking it up cold does not have to rediscover it.
    - **Spanning scopes.** Pick the scope where most of the fix lands and mention the others; split into cross-referencing files only when the parts can be fixed separately.
    - **No personal data.** Refer to test personas or placeholders, never real people (bcc-privacy's README states this; all repos should).
    - **Line citations.** When picking up an issue, check that the cited line still shows what it is cited for, not only that the number exists.
    - **Querying.** Use search instead of an index: `rg -l '^status: open' docs/tech-debt`, `rg -l '^priority: P[01]' docs/tech-debt`, `rg '^title:' docs/tech-debt/*.md`. Closed issues: `git log --diff-filter=D -- docs/tech-debt/`.
    - **Relation to ClickUp.** A tech-debt file records the diagnosis next to the code. When the work is planned, create a ClickUp task that links the file; delete the file when the fix merges.

### AI-drafted documents

15. **Documents drafted with AI MUST say so at the top and be marked for human verification** until someone has checked them against code and reality. This is required for privacy, security and legal content.

## Example

`docs/` in this repo: `adrs/`, `architecture/`, `runbooks/`, `tech-debt/`, `privacy/`, `specs/`, and `docs/user-manual/` for end-user docs. `AGENTS.md` is the agent guide.
