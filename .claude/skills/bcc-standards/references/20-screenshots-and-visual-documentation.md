# 20 Screenshots and Visual Documentation

Screenshots have two uses in our repos: before/after images on pull requests so reviewers can see a UI change, and images in user and developer documentation. Both should come from committed scripts or tests rather than manual capture, so they can be repeated and stay consistent. This standard covers how they are taken, named, stored and kept current.

## Rules

### PR screenshots

1. **Every pull request that changes the UI MUST include before and after screenshots of the affected states.** Reviewers otherwise approve visual changes they have not seen.
2. **Before and after MUST be taken against one running app.** Check out the base version of only the changed frontend module (`git checkout $BASE -- <module>`), let Vite hot-reload, shoot, then restore with `git checkout HEAD -- <module>` and confirm `git status --porcelain` is empty. Both shots then share backend, database and seed data; two separate boots seed different rows and the comparison shows data changes instead of UI changes.
3. **Captures MUST go through a committed library of `playwright-cli` scripts, one per area.** Reuse a script when one covers the area; otherwise write one and commit it, so the next capture is one command.
4. **Capture scripts MUST follow a written contract:** output folder through an `__OUT__` placeholder substituted into a scratch copy, a header listing the states, persona and data assumed, no login inside area scripts, and never changing data the script did not create. A script that edits an existing record breaks every later before/after pair.
5. **The browser locale MUST be fixed in `.playwright/cli.config.json`** (`--lang=nb`, locale `nb-NO`). Without it Chrome follows the machine language and date fields render differently between developers.
6. **Images MUST be attached with `gh pr create --attach` (or `gh pr edit --attach`).** Images committed to a branch and linked from the PR body break in private repos: GitHub's Camo proxy fetches anonymously and gets 404 (orginn tried a `pr-assets` branch and dropped it).
7. **Every PNG MUST be looked at before it is attached.** A script can finish without error and still have captured a spinner, an error page or a half-open dialog.

### Documentation screenshots

8. **Documentation images MUST be produced by a dedicated test project or script set that regenerates them deterministically**, per language, viewport and persona. Determinism needs: injected CSS that zeroes animations and transitions, a fixed settle delay with a comment explaining why a network-idle wait is not used, and an assertion that dev overlays (Vue devtools) are absent.
9. **Documentation images MUST be named `<view>/<scenario>.<viewport>.<lang>.png` and stored with Git LFS** (`*.png filter=lfs` in `.gitattributes`). The name tells a reader and a script exactly which variant a file is.
10. **Documentation images MUST be taken as the role the page is written for, never as the system administrator.** End-user manuals read with admin names, admin menus or admin signatories mislead readers about what they will see.
11. **Documentation screenshots SHOULD be regenerated in CI**, on a schedule or when docs or frontend change, and the diff opened as a PR. Manual runs fall behind the UI.
12. **Visual regression diffing MAY be added later.** None of the repos has it; the deterministic capture in rule 8 is the prerequisite.

### Hygiene

13. **Agent page snapshots MUST NOT be committed.** Add `.playwright-mcp/` and `.playwright-cli/` to `.gitignore`.

## Example

Skills `pr` and `screenshot` in `.claude/skills/`, capture scripts under `.claude/skills/screenshot/scripts/`, and `docs/user-manual/` for documentation images.
