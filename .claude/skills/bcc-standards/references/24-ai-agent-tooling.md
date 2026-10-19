# 24 AI Agent Tooling

Much of the code in our repos is written with coding agents (Claude Code, Copilot). This standard sets the files and checks that let an agent work safely in a repo: one guide that agents and developers share, isolation between parallel runs, rules that fail the build instead of relying on review, and project skills for recurring tasks. The same files make the repo easier for new developers.

## Rules

### The guide

1. **`AGENTS.md` MUST be the single guide for agents and developers, and `CLAUDE.md` MUST contain only `@AGENTS.md`.** Tools differ in which file they read; one source avoids two guides drifting apart.
2. **`AGENTS.md` MUST contain:** start-here pointers to the composition roots, exact commands (build, test, run, codegen), ports and test personas, architecture rules, a Gotchas list of lessons learned, the deployment model, and team process (branch naming, ClickUp).
3. **Architecture rules SHOULD be written as "do X, because Y fails otherwise, enforced by test Z".** The reason lets an agent handle a case the rule did not foresee; the test name shows the rule is checked.
4. **`AGENTS.md` MUST state these rules:**
   - In a git worktree, start the app with `aspire run --isolated` (or `aspire start --isolated`). A non-isolated run attaches to the shared persistent Postgres container and writes into another instance's database without any error.
   - Out-of-scope findings go to `docs/tech-debt/` as one file each ([23 Documentation and Decisions](23-documentation-and-decisions.md)) instead of widening the change. Subagents return findings to the coordinator, which dedupes and writes the files; parallel agents otherwise create near-duplicates.
   - Never delete, skip or weaken a test to make CI green. Fix the cause or stop and report.
5. **`AGENTS.md` SHOULD stay lean: link specs and ADRs instead of copying them.** A 25 KB guide costs context on every task and duplicates text that then drifts.

### Guards

6. **Slopwatch MUST run in CI with `--fail-on warning`, an empty baseline, and a written reason for every suppression.** It catches disabled tests, suppressed warnings and empty catch blocks, which are the shortcuts agents take to get green. *This template deviates: ADR 0006 removes Slopwatch.*
7. **Rules that matter MUST be enforced by architecture tests or startup assertions** ([25 Code Quality Guards](25-code-quality-guards.md)). An agent follows a failing test reliably; it follows a paragraph in a guide only sometimes.

### Configuration

8. **`.mcp.json` MUST register the Aspire MCP server** (`aspire agent mcp`) so agents can list resources, read logs and run dashboard commands.
9. **Recurring tasks SHOULD have repo-specific skills in `.claude/skills/`**: PR with screenshots, screenshot scripts ([20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md)), permission tests, user docs, ClickUp. Restrict `allowed-tools` where the skill only needs a few commands.
10. **Skills MUST live in one location.** Do not keep copies in both `.claude/skills/` and `.agents/skills/`; they drift. Use `.claude/skills/` and point other tools at it if needed.
11. **Commit `.claude/settings.json` with a small allowlist of safe commands (build, test). `.claude/settings.local.json` MUST NOT be committed** and MUST be in `.gitignore`; it holds personal permissions and machine paths.
12. **`.gitignore` MUST cover agent artefacts:** `.claude/worktrees/`, `.claude/settings.local.json`, `.playwright-mcp/`, `.playwright-cli/`, `.superpowers/`.
13. **`.claude/launch.json` SHOULD define the app for the preview pane** (`aspire run` with the app URL), without machine-specific paths.
14. **A `copilot-setup-steps.yml` workflow MAY prepare the environment for Copilot coding agent** (restore, build, Playwright browsers).

## Example

`AGENTS.md` and `CLAUDE.md` (contains `@AGENTS.md`) at the root, `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/launch.json`, `.mcp.json`.
