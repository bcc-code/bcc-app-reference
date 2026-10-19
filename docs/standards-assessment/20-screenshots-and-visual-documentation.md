---
standard: 20 Screenshots and Visual Documentation
assessed: 2026-10-19
commit: e4b2b86
---
# 20 Screenshots and Visual Documentation — assessment

PR screenshots are well covered: the `pr` and `screenshot` skills implement the one-app before/after swap, the script
contract, the fixed locale and `--attach` uploads. Documentation screenshots are only described in prose; nothing in the repo
generates them deterministically, and nothing regenerates them in CI.

**Score: Adequate** · ✅ 10 · ⚠️ 0 · ❌ 2 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | UI-changing PRs include before/after screenshots | MUST | ✅ | `.claude/skills/pr/SKILL.md:45-51` captures when `frontend/` changed; `.github/pull_request_template.md` asks for "screenshots for UI changes". Whether PRs actually carry them is not checkable here. |
| 2 | Before and after taken against one running app | MUST | ✅ | `.claude/skills/pr/SKILL.md:62-92` — `git checkout "$BASE" -- frontend/src/<module>/`, Vite reload, restore with `git checkout HEAD`, then `git status --porcelain` must be empty. |
| 3 | Captures use committed `playwright-cli` scripts, one per area | MUST | ✅ | `.claude/skills/screenshot/scripts/notes.js`, `login.js`; `SKILL.md:9-15` says to add one per area and commit it. Only the Notes area has a script (no admin stats script yet). |
| 4 | Scripts follow a written contract | MUST | ✅ | `.claude/skills/screenshot/SKILL.md:86-109` — `__OUT__` placeholder, header with states/persona/data, no login, never change data it did not create. `notes.js:1-6` follows it. |
| 5 | Browser locale fixed in `.playwright/cli.config.json` | MUST | ✅ | `.playwright/cli.config.json` — `--lang=nb`, `locale: nb-NO`, `timezoneId: Europe/Oslo`. |
| 6 | Images attached with `gh pr create/edit --attach` | MUST | ✅ | `.claude/skills/pr/SKILL.md:129-150`; `:18-20` warns against committing images to a branch. |
| 7 | Every PNG looked at before attaching | MUST | ✅ | `.claude/skills/pr/SKILL.md:86`, `.claude/skills/screenshot/SKILL.md:58-59`. |
| 8 | Docs images regenerated deterministically by a test project/script set | MUST | ❌ | No generator exists. `screenshot/SKILL.md:72-82` lists the requirements (animation-zeroing CSS, fixed settle delay, devtools assertion) but `notes.js:44-46` waits on `networkidle`, injects no CSS and asserts nothing about overlays; `login.js:16-22` only hides the devtools button. |
| 9 | Docs images named `<view>/<scenario>.<viewport>.<lang>.png`, in Git LFS | MUST | ✅ | `.gitattributes:2` — `*.png filter=lfs`; naming documented in `screenshot/SKILL.md:77`. No documentation images exist yet. |
| 10 | Docs images taken as the page's intended role, not admin | MUST | ✅ | `screenshot/SKILL.md:80-81`. No images exist yet, so this is guidance only. |
| 11 | Docs screenshots regenerated in CI | SHOULD | ❌ | No workflow in `.github/workflows/` captures screenshots. |
| 12 | Visual regression diffing may be added later | MAY | N/A | Optional; not adopted. |
| 13 | Agent page snapshots not committed | MUST | ✅ | `.gitignore` lists `.playwright-mcp/` and `.playwright-cli/`; `git ls-files` shows none. |

## Gaps to close

- **8**: Add a deterministic documentation capture set, e.g. `.claude/skills/screenshot/scripts/docs/` or a Playwright project
  under `frontend/e2e/docs/`, that injects CSS zeroing animations and transitions, uses a fixed settle delay with a comment on
  why `networkidle` is not used, asserts `#__vue-devtools-container__` is absent, and writes
  `<view>/<scenario>.<viewport>.<lang>.png` per language and persona into `docs/user-manual/`.
- **11**: Add a scheduled or path-triggered workflow that runs the docs capture set against the E2E topology and opens a PR with
  the changed images.
- **3** (minor): add a capture script for the admin area (`/admin/stats`), so a UI change there does not need a hand-written
  capture.

## Not verifiable in the repo

- **1, 7**: whether real PRs carry before/after screenshots and whether they were reviewed; check recent UI PRs on GitHub.
- **6**: needs `gh` 2.101+ on developer machines.
