---
name: screenshot
description: Use when capturing screenshots of the running app - for a PR's before/after table, a bug report, a design review, documentation images, or checking how a page or dialog renders - and whenever a playwright-cli capture of an area is about to be written by hand.
allowed-tools: Bash(playwright-cli:*) Bash(aspire:*) Bash(sed:*)
---

# Screenshot

Captures go through a library of `playwright-cli` scripts in `.claude/skills/screenshot/scripts/`,
one per area of the app. A script puts the app into each state worth seeing and saves one PNG per
state to a folder you name. Reuse a script when one covers the area; when none does, write it
following the contract below and commit it, so the next capture is one command. This is
standard 20 (see the `bcc-standards` skill, reference 20).

The library starts with one example, `notes.js` (the Notes page). Add a script per area as the app grows.

## Running the app and logging in

```bash
aspire start --isolated     # in a worktree, always --isolated; plain `aspire run` otherwise
aspire describe             # APP = the api resource's https://template.dev.localhost:<port>
```

Aspire's DCP needs local sockets, so in an agent sandbox run `aspire` and `playwright-cli` unsandboxed.
Wait for the **API** resource to be Healthy; Postgres and the mocks go Healthy much earlier.

```bash
sed "s|__PERSON__|user|g" .claude/skills/screenshot/scripts/login.js > "$SCRATCH/login-user.run.js"
playwright-cli -s=user open --browser=chrome "$APP"
playwright-cli -s=user run-code --filename="$SCRATCH/login-user.run.js"
```

`__PERSON__` is `user` (plain member), `user2` (a second member, for ownership checks) or `admin` (has the admin role and
signs in with a second factor). `admin-no-mfa` has the role without a second factor and ends on the second-factor page, so it is
not offered for captures. Use one playwright-cli session per persona (`-s=user`, `-s=admin`), so they stay logged in side by
side. The personas come from the OIDC mock (`api/OidcMock/Personas.cs`); keep the table in
`login.js` in step with it.

Run `open` from the repository root, so playwright-cli picks up `.playwright/cli.config.json`, which
fixes the browser language and locale. Without it Chrome follows the machine's language and date
fields render differently between developers. If the file is missing, create it before shooting
(`--lang=<app default language>`, matching `locale` and `timezoneId`, `ignoreHTTPSErrors: true`).

`login.js` is the only script that logs in. Opening the app redirects to the OIDC mock; the script
signs in as the persona, waits until the browser is back on the app, and sets the viewport to
1280x720. Log in once per session; every other script assumes it.

## Running a capture script

`run-code` takes no arguments, so a script names its output folder with the placeholder `__OUT__`,
which you substitute into a scratch copy:

```bash
OUT="$SCRATCH/screenshots/notes"        # absolute, forward slashes, no trailing slash
sed "s|__OUT__|$OUT|g" .claude/skills/screenshot/scripts/notes.js > "$SCRATCH/notes.run.js"
playwright-cli -s=user run-code --filename="$SCRATCH/notes.run.js"
```

The PNGs land in `$OUT`, named `<area>-<state>.png`. **Read every PNG before using it.** A script
that ran without an error can still have shot a spinner, an error page or a half-open dialog.

For before/after, run the same script twice into two folders, swapping the frontend source in
between; the `pr` skill describes the swap (one running app, so both shots share data).

Close the sessions when done: `playwright-cli -s=user close`.

## Available scripts

`ls .claude/skills/screenshot/scripts/`. Each script's header comment lists the states it captures,
the persona it expects and the data it relies on. If this listing and the folder disagree, the
folder is right.

## Documentation images

Images for `docs/manual/` are taken with the same scripts or a dedicated set, with these
differences (standard 20, rules 8 to 11):

- Name them `<view>/<scenario>.<viewport>.<lang>.png` and store them with Git LFS.
- Inject CSS that zeroes animations and transitions, use a fixed settle delay, and check that dev
  overlays (Vue devtools) are absent. `login.js` already hides the devtools button.
- Take them as the role the page is written for. A manual for members is shot as `user`, never as
  `admin`; show `admin` only on pages that describe admin-only actions.
- Read every PNG for names, menus or data that do not belong to the intended reader before committing it.

## Writing a new script

Name it for the area (`notes.js`, `admin-stats.js`) and follow this contract:

1. The file is exactly `async page => { ... }` with every constant inside the arrow function.
   A top-level `const` fails with "Unexpected token 'const'".
2. A header comment: the states it captures, one line each, the persona, and the data it assumes.
3. `const OUT = '__OUT__'` and `const APP = page.url().match(/^https:\/\/[^/]+/)?.[0]` as the
   first lines. APP comes from the page because the port changes on every isolated run. The
   `run-code` sandbox has no `URL`, so take the origin with a regex.
4. It does not log in. If the page is blank or on the OIDC mock, it throws `'run login.js first'`.
5. It goes **straight to a URL** for stable routes and **navigates through the UI** for anything
   with a generated id (ids are `Guid.CreateVersion7()`, never stable).
6. After each navigation it waits for `networkidle` plus a short settle; after opening a dialog,
   drawer or menu it waits for the open animation.
7. Pages are shot `fullPage: true`; dialogs, drawers, menus and dropdowns at viewport size.
8. **It never changes data it did not create.** It opens dialogs and closes them with Escape; it
   never clicks Save, Archive or any other button that saves or deletes on an existing record. A
   changed record breaks every later before/after pair. A script that needs a record in a given
   state creates its own, says so in its header, and is run against an `--isolated` app, whose
   database is thrown away.
9. One PNG per state, named `<area>-<state>.png`.

Read the label text from the frontend i18n files; the browser runs in the app's default language.
Run the script and read every PNG before committing it. Commit the script on its own, not buried in
an unrelated change, so it reaches `main` for the next person.

## Data: what can be addressed directly

| Data | Stable? | How to reach it |
|---|---|---|
| Personas `user`, `user2`, `admin` | Yes, fixed in the OIDC mock | `__PERSON__` |
| Notes list page | Yes | the route in `frontend/` (see `notes.js`) |
| A note | No, ids are generated and the database starts empty | Create one in the script (isolated run), or click through the list by title |

A generated id can also be looked up from the page with `page.evaluate(() => fetch('/api/...'))`
when clicking to it is awkward. Never hard-code one.

## Common mistakes

| Mistake | Fix |
|---|---|
| Logging in inside a capture script | Only `login.js` logs in; run it once per session |
| Several personas in one session | One playwright-cli session per persona |
| Hard-coding an id | Navigate through the UI or look it up |
| Editing a record you didn't create to reach a state | Create your own in an `--isolated` run, or leave that state out and say so |
| Shooting straight after `goto` | Wait for `networkidle` plus a settle, or the shot shows a loader |
| Using the built-in browser pane | It cannot write files; use `playwright-cli` |
| A Windows path with backslashes in `OUT` | Use an absolute path with forward slashes; inside a JS string a backslash is an escape |
