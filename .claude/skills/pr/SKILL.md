---
name: pr
description: Create a GitHub pull request for the current branch. When the diff touches the frontend, captures before/after screenshots of the changed pages and embeds them in the PR body. Use when the user says "/pr", "open a PR", "create a pull request", or "raise a PR".
allowed-tools: Bash(gh:*) Bash(git:*) Bash(pnpm:*) Bash(dotnet:*) Bash(aspire:*) Bash(playwright-cli:*)
---

# /pr — open a pull request, with before/after screenshots

Creates a GitHub PR for the current branch against `main`. If the change touches the frontend, it
captures **before** and **after** screenshots of what changed and embeds them in the PR description
(standard 20, see the `bcc-standards` skill). Both are taken against one running app, swapping the
frontend source underneath it with `git checkout` and letting Vite hot-reload, so the two shots
share a backend, a database and a seed, and can show a dialog or any other state a URL alone cannot reach.

Screenshots are uploaded with `gh ... --attach`, which puts them on GitHub's `user-attachments`
CDN, the same place the web UI uploads to. Requires **gh 2.101+**.

> **Do not commit screenshots to a branch and link them from the PR body.** GitHub renders body
> images through its Camo proxy, which fetches **anonymously**, so `raw.githubusercontent.com` URLs
> into a private repo return 404 and the image renders broken. `--attach` gives real inline images.

## Arguments

- `routes=/,/notes` — comma-separated routes to shoot, overriding auto-detection.
- `--no-screenshots` — skip capture even if the frontend changed (fast, PR only).
- Any other trailing text is used as extra context for the PR description.

## Workflow

Follow these steps in order. Run the shell steps yourself and read their output rather than assuming results.

### 1. Preflight

```bash
git rev-parse --abbrev-ref HEAD          # BRANCH; must not be "main"
git status --porcelain                    # warn if there are uncommitted changes
BASE=$(git merge-base main HEAD)          # base commit for the diff
git fetch origin main --quiet
```

- If `BRANCH` is `main`, stop and tell the user to switch to a feature branch.
- If the branch has no upstream, push it: `git push -u origin "$BRANCH"`. Otherwise `git push` any unpushed commits (the PR must reflect what is on origin).
- If there are uncommitted changes, tell the user and ask whether to proceed. They will not be in the PR, and the "after" screenshots reflect committed state only. If screenshots are in scope, **stop rather than proceed**: step 4 checks part of `frontend/` out at the base commit and back, which would discard uncommitted work there.

### 2. Decide whether to screenshot

```bash
git diff --name-only "$BASE"...HEAD
```

Capture screenshots only if **both**: the diff includes a path under `frontend/` **and** `--no-screenshots` was not passed. Otherwise skip to step 6.

### 3. Pick what to shoot

If `routes=` was given, use it verbatim. Otherwise auto-detect:

- Look at the changed files under `frontend/src/`. Map a feature or page folder to its route with the router file under `frontend/src/` (find it with `grep -rl createRouter frontend/src`). Do not guess a route; read the router.
- De-duplicate. If nothing maps cleanly (only shared components or styles changed), fall back to a small default set: the home page and the Notes page.

A route is the starting point, not the whole list. Because step 4 drives a real browser, shoot the **state the change is actually in**: if the diff is mostly a dialog, a menu or a validation message, a shot of the page behind it proves nothing. Work out what a reviewer needs to see, then get the page into that state before shooting.

### 4. Capture before/after against one running app

**Run the app once and swap the frontend source underneath it.** The dev server is Vite, so checking the changed files out at `$BASE` hot-reloads the old UI into the browser you already have open, with the same backend, database and seeded rows on both sides. Check the files back out at `HEAD` and the new UI reloads. Two separate boots would seed different rows, and the comparison would show data changes instead of UI changes.

Requires a clean tree; preflight already checks. Pick any scratch directory and call it `$OUT`.

**REQUIRED SUB-SKILL:** Use `screenshot` for everything that drives the browser: starting the app, logging in once with `login.js`, and running the capture script for each area the diff touches. If no script covers an area, write one to that skill's contract and commit it on its own before shooting, so the next PR can reuse it. The script must reach the states step 3 picked, dialogs included.

Run the same scripts twice, once per side, into two folders:

```bash
# AFTER — the working tree is already at HEAD
#   run each script with OUT="$OUT/after"   (see the screenshot skill)

# BEFORE — swap just the changed module back and let Vite reload it
git checkout "$BASE" -- frontend/src/<changed-module>/
#   run the same scripts with OUT="$OUT/before"

git checkout HEAD -- frontend/src/<changed-module>/   # restore, always
git status --porcelain                                # must be empty
```

Each script does its own `goto`, so the swapped files are picked up without a manual reload.

Check each PNG by reading it before you attach it. A change can look correct in the diff and wrong on screen (a list that gained dividers, a link that changed colour). Those are what these shots are for; finding them after the PR is open wastes a review.

**Scope the checkout to the module**, not all of `frontend/`. A narrower checkout reloads faster, cannot disturb an unrelated file, and keeps `git status` readable. Widen it only if the change spans shared components too.

**Commit before you swap.** `git checkout HEAD -- <dir>` restores the *committed* state, so any uncommitted edit in that directory is gone, including a fix you just made because the first screenshot showed something wrong. Commit the fix, then swap.

**Restore the tree even if something fails.** A `git checkout "$BASE" -- …` left in place is the base version sitting uncommitted on top of your branch.

If the app will not start or the capture fails, do not abort the PR. Tell the user, show the error, and continue to create the PR **without** screenshots (step 6, no table).

Stop the app when you are done: `aspire stop`.

### 5. Name the screenshots

The scripts name each PNG `<area>-<state>.png`, and both sides use the same names. `--attach` takes a flat list and the basenames become the alt-text fallback, so copy each into one folder with the side appended: `$OUT/before/notes-list.png` → `$OUT/up/notes-list-before.png`. Nothing is uploaded at this point; that happens in step 6 as part of creating the PR.

### 6. Create the PR

Generate the title and body from the branch's commits and diff. If `.github/PULL_REQUEST_TEMPLATE.md` exists, follow it; otherwise use descriptive, multi-paragraph prose without a rigid template:

```bash
git log --format='%s%n%n%b' "$BASE"..HEAD
git diff --stat "$BASE"...HEAD
```

Write the body to `$OUT/pr-body.md`. Structure:

- A short summary paragraph of what the PR does and why (fold in any extra context argument).
- A "## Changes" bullet list of the notable changes.
- If the change adds or changes how personal data is processed, a "## Privacy" section: what data, why, where stored, retention, who sees it (standard 18, rule 3).
- If screenshots were captured, a "## Screenshots" section with a before/after table per route. Write the image URLs as `PLACEHOLDER-<slug>-before` / `PLACEHOLDER-<slug>-after` for now; step 6b swaps in the real ones:

  ```markdown
  ## Screenshots

  ### /notes
  | Before | After |
  |--------|-------|
  | ![before](PLACEHOLDER-notes-before) | ![after](PLACEHOLDER-notes-after) |
  ```

- End with the attribution line your harness specifies for pull request descriptions. No `Co-Authored-By` trailer in the body; that belongs on the commit.

Then create it, attaching the PNGs in the same command:

```bash
gh pr create --base main --head "$BRANCH" --title "<title>" --body-file "$OUT/pr-body.md" \
  --attach "$OUT/up/<slug>-before.png#<route> before" \
  --attach "$OUT/up/<slug>-after.png#<route> after"
```

The text after `#` is the alt text; without it the filename is used. Up to 50 files per command.

### 6b. Point the table at the uploaded images

**`--attach` appends the images to the end of the body rather than rewriting references in it.** Read the body back, take the uploaded URLs in order, substitute them for the placeholders, delete the appended copies, and edit the PR again:

```bash
gh pr view <N> --json body --jq .body > body.md
# the uploads appear as ![alt](https://github.com/user-attachments/assets/<uuid>), in
# --attach order; swap each into its PLACEHOLDER-* slot and remove the appended lines
gh pr edit <N> --body-file body.md
```

Verify before moving on: `gh pr view <N> --json body --jq .body | grep '!\['` should show the images **inside the table** and nowhere else. A `user-attachments` URL is correct; a `raw.githubusercontent.com` one is the broken path.

### 7. Cleanup

```bash
rm -rf "$OUT"
playwright-cli -s=user close
aspire stop
git status --porcelain              # must be empty: the module was checked out and back
```

Report the PR URL to the user, and note whether screenshots were included. Remind the user to review the PR text, since an AI wrote the draft.

## Known limitations

- Both shots run the **head backend**. The frontend is swapped underneath it, the backend is not. For frontend-only or frontend-dominant changes that is right; if the PR also changes an API that feeds the page, the "before" shot uses the new backend, so say so.
- **A backend change in the diff breaks the premise.** Hot reload covers `frontend/src`, not C#. If the PR touches both, restart the app between the two states or take the shots as frontend-only and say which parts are not comparable.
- Capture needs the app running (a couple of minutes cold start), so it only runs when `frontend/` changed; use `--no-screenshots` to skip.
- Screenshots are taken as the `user` persona at 1280 wide, pages full-height and dialogs at viewport size (the `screenshot` skill's contract). Use `admin` only for admin pages.
- `--attach` needs **gh 2.101+** and push access to the repo. On an older `gh` the flag does not exist; say so and create the PR without screenshots.
