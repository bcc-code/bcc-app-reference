---
name: clickup-tasks
description: Create work items as ClickUp tasks instead of GitHub issues, and move GitHub issues labelled `clickup` over to ClickUp. Use whenever the task is to file, create, open, register or write up an issue, bug, feature request, task, epic or follow-up for this app, including "file an issue", "create a task" and "gh issue create", and whenever the user asks to migrate, sync or move GitHub issues to ClickUp. Does not cover docs/tech-debt findings, which stay in the repository.
---

# Work items live in ClickUp

New work for this app is created as a ClickUp task, not a GitHub issue. Do not run
`gh issue create`. GitHub issues that already exist are moved over when someone labels them
`clickup` (see the end of this file).

Out-of-scope findings still go to `docs/tech-debt/` as `AGENTS.md` describes (standard 23, rule 14).
This skill is for work someone has asked to have filed.

## Config (the app team fills this in)

This section holds every workspace-specific value. **Nothing outside it hard-codes an id.** Read the
values from here. If a value below still says `TODO`, stop and ask the user for it; do not guess ids
and do not search the workspace for a likely match. Look the ids up with the ClickUp tools
(`clickup_get_custom_fields`, `clickup_get_list`, `clickup_get_folder`) and write them in here.

| Setting | Value |
|---|---|
| GitHub repository (`owner/repo`) | `TODO` |
| ClickUp space id | `TODO` |
| ClickUp folder id (the app's folder) | `TODO` |
| Backlog list id (every new task goes here) | `TODO` |
| Projects list id (the list that holds project tasks) | `TODO` or `none` |
| Team custom field id and value (labels field) | `TODO` or `none` |
| Project custom field id (relationship field) | `TODO` or `none` |
| Default project task id | `TODO` or `none` |
| Status names, in order | `TODO` (for example `new`, `in progress`, `review`, `done`) |
| Task language rule | `TODO` (for example: business-facing work in the stakeholders' language, technical work in English) |

Projects (only if the workspace uses a Project relationship field):

| Project | Task id | Use for |
|---|---|---|
| `TODO` | `TODO` | `TODO` |

Epics (only if the app uses epics for milestones):

| Epic | Task id | GitHub milestone |
|---|---|---|
| `TODO` | `TODO` | `TODO` |

## Where the task goes

In the Backlog list from the config. Do not create tasks in sprint lists. A task is *added* to a
sprint later, never moved there.

## How the task is set up

| Field | Value |
|---|---|
| Name | A short, specific title, in the language chosen below. |
| Description | Markdown. Use the structure of a good issue: background, scope, acceptance criteria, links to specs under `docs/` on GitHub. |
| Status | The first status in the config (`new`), unless the user says the work has started. |
| Team | The team custom field from the config, when it has one. Always set it then. |
| Project | The project custom field from the config, when it has one. Always set it then. A task without it can drop out of every project view. |
| Assignees | Only when the user names someone. Look the person up with `clickup_find_member_by_name` and pass the member id as a string. |
| Tags | None, unless the config says otherwise. |

Do not put real personal data in a task: refer to test personas or placeholders, never to real people
or members (standard 18).

### Choosing the language

Follow the language rule in the config. Pick the language by who reads the task, not by the language
of the conversation. Write the title and the description in the same language. Code, file names and
identifiers stay as they are. Moving an existing GitHub issue does not change its language; the title
and body are copied as they are.

### Choosing the project

Use the default project from the config unless the work is clearly for another project in the table.
If you cannot tell, ask. If a Project write fails with *"All tasks must belong to the same linked
list"* or an unknown-field error, re-read the list with `clickup_get_custom_fields` instead of
guessing ids, then update the config section.

### Milestones and larger pieces of work are epics

If the app uses epics: a milestone, module or other larger piece of work is a task of type **Epic**
in the same Backlog. It is never a list, a folder or a project. The work under it is its subtasks.

- When a new task belongs to an epic, pass the epic's task id as `parent` to `clickup_create_task`.
- To create an epic, pass `task_type: "Epic"`.
- ClickUp does not copy custom fields from an epic to its subtasks. Set Team and Project on every subtask as well.
- The ClickUp MCP tools cannot move an existing task under a parent. For an existing task, ask the
  user to do it in the ClickUp UI. Do not delete and recreate the task, because that breaks the GitHub links.

When an issue from a GitHub milestone is moved, it goes under that milestone's epic. If the milestone
has no epic yet, create one first, named after the milestone, with the milestone URL as the first
line of the description, and add it to the config.

### Example call

Ids come from the config; the values below are placeholders.

```json
clickup_create_task {
  "list_id": "<Backlog list id>",
  "name": "<short specific title>",
  "markdown_description": "## Background\n...",
  "status": "<first status>",
  "custom_fields": [
    {"id": "<team field id>", "value": "[\"<team value id>\"]"},
    {"id": "<project field id>", "value": "{\"add\":[\"<project task id>\"]}"}
  ]
}
```

Leave out `custom_fields` that the config says `none`. Afterwards, give the user the task as a
markdown link with the task name as the link text.

## Moving a GitHub issue labelled `clickup`

Two labels in the repository from the config track this:

- `clickup` means "move this issue to ClickUp". People set it by hand.
- `clickup-migrated` means "moved". The ClickUp link is in the issue description.

Find candidates with
`gh issue list -R <owner/repo> --label clickup --state open --limit 500 --json number,title,body,assignees`.
Without `--limit`, `gh` returns only the first 30.

For each issue:

1. **Check for an existing link first.** If the description already has a
   `<!-- clickup-sync:start -->` block with an `app.clickup.com/t/<id>` link, the issue is already in
   ClickUp. Do not create a second task. Verify the task with `clickup_get_task` and go to step 5.
   Never use `clickup_search` to decide whether an issue was moved. It matches loosely.
2. **Dry run.** List the issues you will create tasks for, their project, and the GitHub edits, then
   wait for a yes. Editing issue descriptions changes other people's work.
3. **Create the task** as above, with these differences:
   - Name: the issue title, unchanged.
   - Description: first line `GitHub: https://github.com/<owner/repo>/issues/<n>`, a blank line, then
     the whole issue body, copied as it is. Do not shorten it. ClickUp is where the work is tracked
     from now on, so the task has to stand on its own. ClickUp rewrites some GitHub markdown when it
     stores it (callouts become plain quotes, nested checkbox lists and formatting around inline code
     can change slightly). The text is all kept. Mention it in the report instead of working around it.
   - Status: the first status for an open issue. Skip closed issues unless the user asks.
   - Assignees: the issue's assignees as ClickUp member ids. Get each login's real name with
     `gh api users/<login> --jq .name`, look that name up with `clickup_find_member_by_name`, and pass
     the member id. If there is no match, or more than one, leave that person off and say so in the report.
4. **Link back right away.** Before doing anything else with the task, append this footer to the issue
   description with `gh issue edit <n> --body-file <file>`, replacing an existing block rather than
   adding a second. The footer is what step 1 checks, so until it is written a re-run would create a
   second task. If the edit fails, stop and report the task id instead of going on. Editing the
   description notifies nobody, while a comment would notify every watcher:

   ```
   <!-- clickup-sync:start -->

   ---
   ClickUp: https://app.clickup.com/t/<task id>
   <!-- clickup-sync:end -->
   ```

5. **Copy the comments.** Copy each issue comment, oldest first, as a ClickUp comment on the task with
   `clickup_create_task_comment`. Start each one with
   `**GitHub comment from <login>, <YYYY-MM-DD>** ([original](<comment url>))`, then a blank line and
   the comment body unchanged. The comments are posted as the person running the move, so the first
   line is the only record of who wrote them. Do not notify anyone. Before posting, check the task's
   comments so a re-run does not post the same comment twice. ClickUp comments do not support Markdown
   tables: a table is dropped and shows as `undefined`. Rewrite a table as a bullet list and add a line
   saying so. Task descriptions do support tables.
6. **Swap labels.** `gh issue edit <n> --add-label clickup-migrated --remove-label clickup`.

Never close the GitHub issue or delete a ClickUp task as part of a move. Report what was created, what
was skipped and why.
