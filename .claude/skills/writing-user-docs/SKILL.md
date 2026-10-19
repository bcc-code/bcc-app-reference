---
name: writing-user-docs
description: Use when writing, restructuring or reviewing end-user documentation under docs/user-manual/, or any page that follows Diataxis (tutorials, how-to guides, reference, explanation), or when the task mentions user manual, user documentation, handbook, tutorial, how-to, or adding screenshots to docs.
---

# Writing User Docs

## Overview

End-user documentation lives in `docs/user-manual/` (standard 23, see the `bcc-standards` skill) and
follows Diataxis (standard 23, rule 11). The tree is **module first, documentation axis second**:
the top folder level says which part of the app a page is about, and an optional level below it
says what kind of page it is. A small module holds its pages directly.

**Core principle:** the folder structure is the navigation. There is no separate index to maintain,
so a page is discoverable exactly when it sits in the right folder with the right frontmatter.

Write in the app's default user language (check the frontend i18n files). Write for the reader the
page is about, usually a member, not a developer.

## When to Use

- Writing a new user-facing page, or a whole module's documentation
- Restructuring or moving pages between modules or axes
- Reviewing documentation for structure and honesty
- Adding screenshots to a page

**Not for:** developer documentation (`docs/architecture/`, `docs/runbooks/`, ADRs), integrator docs
(`docs/public/`), release notes, or UI strings (the frontend i18n files).

## Where Files Live

```
docs/user-manual/
  getting-started/                module without axes: holds pages directly
    index.md                      module label + order
    welcome.md
    images/
      notes-list.png
  notes/                          module using the axes
    index.md
    how-to/
      index.md                    axis label + order
      write-a-note.md
    explanation/
      index.md
      why-notes-are-private.md
```

If the app renders the manual in-app (standard 21, rule 15), the same tree is bundled from
`frontend/src/content/`; check how this repo wires it before adding files there.

## Frontmatter

Every file, pages and `index.md` alike:

```markdown
---
title: Write a note
order: 1
---
```

| Field | Applies to | Meaning |
|---|---|---|
| `title` | every file | Sidebar label. On `index.md`, the folder's label |
| `order` | every file | Ascending sort within the folder |
| `audience` | pages, optional | `admin` marks a page only for administrators |

`index.md` is never itself a chapter; it labels and orders its folder.

If the manual contains pages only administrators need, mark them with `audience: admin`. Hiding text
never grants or removes access; the backend enforces access.

## Quick Reference

| Rule | Why |
|---|---|
| Body starts at `##`, never `#` | The page renders `title` as the heading; a `#` duplicates it |
| Images in the module's `images/` folder, referenced relatively | Paths stay valid when the tree is bundled |
| Every image has alt text describing what it shows | Screen readers, and the alt survives a broken path |
| No raw HTML | Keeps rendering predictable |
| Quote UI labels exactly as they appear in the frontend i18n file | The label is never paraphrased or translated on the fly |
| Fixed axis order: tutorials 1, how-to 2, reference 3, explanation 4 | The axes read the same way in every module |

## Choosing Where a Page Goes

Pick the module first (the part of the app the reader is working in), then the axis if the module uses them:

| Axis folder | The reader wants | Never contains |
|---|---|---|
| `tutorials` | To be taken through one path start to finish, learning as they go | Choices, alternatives, digressions |
| `how-to` | To finish one specific task they already know they need | Explanation of why |
| `reference` | To look something up: statuses, fields, formats, permissions | Instructions |
| `explanation` | To understand why it works this way | Steps |

Skip the axes when a module has only a handful of pages that do not split cleanly. Splitting three
pages across four axes reads worse than not splitting.

**Structural rules (Diataxis):**

1. A tutorial is one path with no choices.
2. A how-to solves exactly one task and does not explain why.
3. Reference never instructs.
4. Explanation never gives steps.
5. Cross-links point out of an axis, never sideways into another how-to's steps.
6. No page assumes knowledge of a module's previous version.

Rules 3 and 4 can be checked mechanically: no "click", "press" or "select" (in any form, in the
page's language) inside `reference/` or `explanation/`. A docs checker in CI (standard 23, rule 12) is
the place to enforce them; if the repo has one, run it.

## Adding a Module

1. Create `docs/user-manual/<module>/` with a lowercase, hyphenated slug.
2. Add `index.md` with the module's `title` and its `order` among the other modules.
3. Add pages directly, or axis folders each with their own `index.md`.
4. Add `images/` inside the module when it needs images.

Do not create empty placeholder modules. Add the folder when the first page is ready.

## Images

Put them in the module's `images/` folder and reference them relative to the page:

```markdown
![The notes list with two notes and the "New note" button.](images/notes-list.png)
```

Name documentation images `<view>/<scenario>.<viewport>.<lang>.png` and store them with Git LFS
(standard 20, rule 9). Produce them with the `screenshot` skill, as the role the page is written
for, never as the administrator unless the page describes an admin-only action.

**Content rules for screenshots:** test personas and fictional data only. No real personal data, no
special-category data. Crop the shot rather than publish a name that is not a test persona. Read
every PNG before committing it.

## Honesty Requirements

- **Trace every claim.** Read the code or walk the running app. Where the app and the code disagree,
  the app wins; record the discrepancy as a file in `docs/tech-debt/` rather than fixing it inline.
- **Mark AI drafts for review.** Per standard 23, rule 15, a document drafted with AI says so at the
  top and is marked for human verification until someone has checked it against the app. Do this in
  the way the repo's docs already do (a status line under the title), and tell your human partner in
  the summary or PR body. Do not put disclaimers inside individual claims; if a claim cannot be
  verified, leave it out.
- **Label a placeholder as a placeholder.** A wireframe or mock standing in for a screenshot says so
  in the page, so no reader mistakes it for the product.

## Verifying

Open the page in the running app or in a markdown preview, and check:

- the page appears under the right module and axis, in the intended order
- links and images resolve
- tables are readable at a narrow width
- every UI label in quotes exists in the frontend i18n file

Run the repo's docs checker and frontend checks if they exist (`pnpm lint`, `pnpm test` in `frontend/`).

## Common Mistakes

| Mistake | What happens | Fix |
|---|---|---|
| No frontmatter block | Page skipped or unlabelled | Add `title` and `order` |
| `# Title` as the first body line | Title shown twice | Start the body at `##` |
| Folder without `index.md` | Raw folder slug used as the label | Add `index.md` with `title` and `order` |
| Image path relative to the manual root | Image does not load | Make it relative to the page |
| Paraphrased UI label in quotes | Documentation drifts from the interface | Copy the string from the i18n file |
| Empty module folder as a placeholder | Looks broken to whoever finds it | Create the folder with its first real page |
| Steps inside a reference or explanation page | Breaks the Diataxis rule | Move the steps to a how-to and link to it |
