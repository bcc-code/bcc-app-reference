# 20 Screenshots and Visual Documentation

Screenshots have two uses in our repos: before/after images on pull requests so reviewers can see a UI change, and images in user and developer documentation. Both should come from committed scripts or tests rather than manual capture, so they can be repeated and stay consistent. This standard covers how they are taken, named, stored and kept current.

## Rules

### PR screenshots

1. **Every pull request that changes the UI MUST include before and after screenshots of the affected states.** Reviewers otherwise approve visual changes they have not seen.
2. **For frontend-only changes, before and after SHOULD be taken against one running app.** Commit or stash your work first. Check out the base version of only the changed frontend module (`git checkout $BASE -- <module>`), let Vite hot-reload, shoot, then restore with `git checkout HEAD -- <module>` and confirm `git status --porcelain` is empty. Both shots then share backend, database and seed data; two separate boots seed different rows and the comparison shows data changes instead of UI changes. When the change also touches the backend, boot the base and the change from the same seed and say so in the PR.
3. **Captures MUST go through a committed library of `playwright-cli` scripts, one per area.** `playwright-cli` is a command-line tool that drives a Playwright browser session, so a script is a list of commands that can be rerun. Reuse a script when one covers the area; otherwise write one and commit it, so the next capture is one command.
4. **Capture scripts MUST follow a written contract:** a configurable output folder, a header listing the states, persona and data assumed, no login inside area scripts, and never changing data the script did not create. A script that edits an existing record breaks every later before/after pair.
5. **The browser locale MUST be fixed in `.playwright/cli.config.json`** to the app's primary locale (for example `--lang=nb`, locale `nb-NO`). Without it Chrome follows the machine language and date fields render differently between developers.
6. **Before and after images MUST appear in the pull request, uploaded through a documented mechanism.** `gh pr create` has no flag for attaching images. Two working options are adding the images by hand in the GitHub web UI (drag them into the description or a comment) and committing them to a dedicated assets branch linked by raw URL. In a private repo, check that the images render for another reviewer: raw links to a branch can fail there because GitHub's image proxy fetches anonymously. Write the chosen mechanism in the repo's contributing guide.
7. **Every PNG MUST be looked at before it is attached.** A script can finish without error and still have captured a spinner, an error page or a half-open dialog.

### Documentation screenshots

8. **Documentation images MUST be produced by a dedicated test project or script set that regenerates them deterministically**, per language, viewport and persona. Determinism needs: injected CSS that zeroes animations and transitions, a wait for an explicit ready condition (a locator or an element state) instead of a fixed sleep, and an assertion that dev overlays (Vue devtools) are absent. Use a fixed settle delay only when no ready condition exists (for example when a screenshot must show a spinner during a deliberately delayed request), with a comment saying why.
9. **Documentation images MUST be named `<view>/<scenario>.<viewport>.<lang>.png`, and SHOULD be stored with Git LFS limited to the screenshots folder** (for example `docs/images/screenshots/**/*.png filter=lfs` in `.gitattributes`), not every image in the repo. LFS has quota and setup costs, so keep other images in normal Git. The name tells a reader and a script exactly which variant a file is.
10. **Documentation images MUST be taken as the role the page is written for, never as the system administrator.** End-user manuals read with admin names or admin menus mislead readers about what they will see.
11. **Documentation screenshots SHOULD be regenerated in CI**, on a schedule or when docs or frontend change, and the diff opened as a PR. Manual runs fall behind the UI.
12. **Visual regression diffing MAY be added later.** The deterministic capture in rule 8 is the prerequisite.

### Hygiene

13. **Agent page snapshots MUST NOT be committed.** Add `.playwright-mcp/` and `.playwright-cli/` to `.gitignore`.

## Examples

### Before/after flow (rules 2, 3)
```bash
# Commit or stash first. AFTER: the working tree is already at HEAD.
#   run each script with OUT="$OUT/after"

# BEFORE: swap just the changed module back and let Vite reload it
git checkout "$BASE" -- frontend/src/admin/modules/<module>/
#   run the same scripts with OUT="$OUT/before"

git checkout HEAD -- frontend/src/admin/modules/<module>/   # restore, always
git status --porcelain                                      # must be empty
```

### Script library and contract (rules 3, 4)
Keep one script per area in a committed folder such as `docs/screenshots/scripts/` (for example `orders.js`, `bookings.js`, `login.js`). The scripts take the output folder from a placeholder that a small wrapper substitutes into a scratch copy before running it, so the committed script is never edited. Contract excerpt:

```text
1. `const OUT = '__OUT__'` and `const APP = page.url().match(/^https:\/\/[^/]+/)?.[0]` are the first lines.
2. It does not log in. If the page is blank or on the OIDC mock, it throws 'run login.js first'.
3. It never changes data it did not create. A changed order breaks every later before/after pair.
4. One PNG per state, named <area>-<state>.png.
```

### Uploading images (rule 6)
Drag the PNGs into the PR description in the GitHub web UI, or push them to an `assets/pr-<number>` branch and link them as raw URLs in a table with before and after columns. Verify the images render for a second reviewer.

### Fixed locale (rule 5)
```json
{ "browser": { "launchOptions": { "args": ["--lang=nb"] },
               "contextOptions": { "locale": "nb-NO" } } }
```

### Deterministic documentation screenshots (rules 8, 9)
A dedicated `MyApp.Screenshots` project writes to `docs/images/screenshots/` for each language and for mobile and desktop viewports:

```csharp
// A fixed delay is used here because the loading scenario screenshots the spinner during a
// deliberately delayed /api/orders/list request, so there is no ready condition to wait for.
private const int SettleMs = 600;
...
var path = Path.Combine(dir, $"{scenario}.{viewport}.{lang}.png");
await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content =
    "*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;" + ... });
await page.WaitForTimeoutAsync(SettleMs);
var devtoolsAnchors = await page.Locator("#vue-devtools-anchor, .vue-devtools-frame").CountAsync();
Assert.True(devtoolsAnchors == 0, ...);
```

### Role-correct documentation images (rule 10)
The capture contract states that documentation images never show the system administrator. Per-module `docs-<module>.js` scripts sign in as the persona the page is written for and write straight into that module's image folder under the user documentation.

## Related
- [19 Testing Strategy](19-testing-strategy.md) — shared personas and seeded data
- [21 Frontend](21-frontend.md) — i18n and the frontend being captured
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — where documentation images live
- [24 AI Agent Tooling](24-ai-agent-tooling.md) — skills and agent snapshots
