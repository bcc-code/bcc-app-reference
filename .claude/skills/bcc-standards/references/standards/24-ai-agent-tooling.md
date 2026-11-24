# 24 AI Agent Tooling

Much of the code in our repos is written with coding agents (Claude Code, Copilot). This standard sets the files and checks that let an agent work safely in a repo: one guide that agents and developers share, isolation between parallel runs, rules that fail the build instead of relying on review, and project skills for recurring tasks. The same files make the repo easier for new developers.

## Rules

### The guide

1. **`AGENTS.md` MUST be the single guide for agents and developers, and `CLAUDE.md` MUST contain only `@AGENTS.md`.** Tools differ in which file they read; one source avoids two guides drifting apart.
2. **`AGENTS.md` MUST contain:** start-here pointers to the composition roots, exact commands (build, test, run, codegen), ports and test personas, architecture rules, a Gotchas list of lessons learned, the deployment model, and team process (branch naming, ClickUp).
3. **Architecture rules SHOULD be written as "do X, because Y fails otherwise, enforced by Z".** The reason lets an agent handle a case the rule did not foresee; naming the check (an analyzer rule, a test or a startup assertion) shows the rule is checked.
4. **`AGENTS.md` MUST state these rules:**
   - In a git worktree, and for parallel agents, start the app with `aspire run --isolated` (or `aspire start --isolated`); see [02 Local Development](02-local-development.md) rule 7. Without it, a run can write into another instance's database when instances share a persistent container.
   - Out-of-scope findings go to `docs/tech-debt/` as one file each ([23 Documentation and Decisions](23-documentation-and-decisions.md)) instead of widening the change. Subagents return findings to the coordinator, which dedupes and writes the files; parallel agents otherwise create near-duplicates.
   - Never delete, skip or weaken a test to make CI green. Fix the cause or stop and report. Removing a test is right when the behaviour it checks is gone, or another test already checks it on a more real path (an integration test, the CI job that runs the image); name that test in the commit message. Do not add production code only so a unit test can reach it. A suite of tests that check nothing of value costs run time and review time and hides the tests that matter.
5. **`AGENTS.md` SHOULD stay lean: link specs and ADRs instead of copying them.** A 25 KB guide costs context on every task and duplicates text that then drifts.

### Guards

6. **Rules that matter MUST be enforced by analyzers, tests or startup assertions** ([25 Code Quality Guards](25-code-quality-guards.md)). An agent follows a failing test reliably; it follows a paragraph in a guide only sometimes.

### Configuration

7. **`.mcp.json` MUST register the Aspire MCP server** (`aspire agent mcp`); see [02 Local Development](02-local-development.md) rule 16.
8. **Recurring tasks SHOULD have repo-specific skills in `.claude/skills/`**: PR with screenshots, screenshot scripts ([20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md)), permission tests, user docs, ClickUp. Restrict `allowed-tools` where the skill only needs a few commands.
9. **Skills MUST live in one location.** Do not keep copies in both `.claude/skills/` and `.agents/skills/`; they drift. Use `.claude/skills/` and point other tools at it if needed.
10. **Commit `.claude/settings.json` with a small allowlist of safe commands (build, test). `.claude/settings.local.json` MUST NOT be committed** and MUST be in `.gitignore`; it holds personal permissions and machine paths.
11. **`.gitignore` MUST cover agent artefacts:** `.claude/worktrees/`, `.claude/settings.local.json`, `.playwright-mcp/`, `.playwright-cli/`, `.superpowers/` (agent plan and skill output).
12. **`.claude/launch.json` (the Claude Code configuration that tells the preview pane how to start and open the app) SHOULD define the app** (`aspire run` with the app URL), without machine-specific paths.
13. **A `copilot-setup-steps.yml` workflow MAY prepare the environment for Copilot coding agent** (restore, build, Playwright browsers).

## Examples

### One guide, rules in the recommended form (rules 1, 2, 3)
`CLAUDE.md` is the single line `@AGENTS.md`. `AGENTS.md` has the sections: Start here, Architecture that matters, Slices, Frontend + API contract flow, Day-to-day commands, Quality gates, Gotchas, Deployment. A rule in the recommended form:

```markdown
- No CPU outside requests: Wolverine `DurabilityMode.Serverless`, Marten inline projections only, no hosted services,
  no Wolverine scheduling or local queues. A startup assertion enforces this.
- Correctness times come from `IDbClock` (`clock_timestamp()`), never `DateTime.UtcNow`.
```

A gotcha entry: "An HTTP endpoint's `After` postprocessor runs before `SaveChangesAsync`, not after the commit."

### Isolation rule (rule 4)
A section titled "Always use `--isolated` in a git worktree". It can also explain that each worktree gets its own hostname, for example `myapp-<worktree>.dev.localhost`.

### Out-of-scope rule (rule 4)
```markdown
When dispatching subagents, relay this rule but have each agent **return** its findings rather than
write the files itself; the coordinator dedupes and writes them. Parallel agents otherwise produce
near-duplicate files for the same issue.
```

### Test rule (rule 4)
In the agent instructions: "Do not delete or disable tests to get CI green."

### Committed allowlist (rule 10)
```json
{ "permissions": { "allow": [
  "WebFetch(domain:martendb.io)", "Bash(dotnet test *)", "Bash(dotnet build *)" ] } }
```

### Skills, agents and MCP (rules 7, 8)
Skills under `.claude/skills/`: `pr`, `screenshot`, `permission-tests`, `writing-user-docs`, `clickup-tasks`, `clickup-work`. A skill that needs only a few commands lists them in `allowed-tools` in its `SKILL.md`. A repo agent under `.claude/agents/` can carry guardrails on which fields or files it may touch. `.mcp.json` registers the Aspire MCP server.

## Related
- [02 Local Development](02-local-development.md) — Aspire AppHost and isolated runs
- [19 Testing Strategy](19-testing-strategy.md) — tests that agents must not weaken
- [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md) — screenshot skills
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — tech-debt files and ADRs
- [25 Code Quality Guards](25-code-quality-guards.md) — analyzers and startup assertions
- [Code design principles](../principles/code-design.md) — no magic, readable code over clever code
- [Security principles](../principles/security.md) — shift-left practices
