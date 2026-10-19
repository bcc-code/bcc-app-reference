---
name: clickup-work
description: Use when the user gives a bare ClickUp task id (an 11-character lowercase id such as `abc12def345`), an `app.clickup.com/t/<id>` link, or asks to work on, solve, pick up or implement a ClickUp task or issue. Also use when a pull request opened for such a task is created or merged. Does not cover filing new tasks; that is the `clickup-tasks` skill.
---

# Working on a ClickUp task

A ClickUp task id in the user's message means "do this task". The task is the spec: read it, set up
the session and the branch for it, mark it as yours, do the work, and keep the task in step with the
pull request until it is merged.

The user's request to work on the task covers every ClickUp update below (status, assignee, comment,
attachments). Do not ask again for each one.

## Config

Status names, the GitHub repository and the language rule are in the **Config** section of the
`clickup-tasks` skill. Read them there; do not hard-code them here. The default flow is
`new`/`ready` → `in progress` → `review` → `done`; `clickup_get_task` with `expand_statuses: true`
lists the real names if a write is refused. If the config in `clickup-tasks` still says `TODO`, ask
the user for the status names before changing any status.

## 1. Read the task

- `clickup_get_task` with `include: ["description", "attachments", "subtasks", "linked_tasks"]` and
  `clickup_get_task_comments`. Comments often hold answers that the description lacks.
- Read linked tasks and the parent epic when the description points to them. Download image
  attachments with `clickup_download_task_attachment` when they show what to build.
- **Stop and ask** if the task's status is `done` or `archived`, or someone other than the user is
  assigned and working on it. Confirm before taking it over.
- If the description has open questions ("to be decided", "TBD", unanswered comments) that change
  what you build, put them to the user before writing code. Suggest an answer for each.
- If the task or its comments contain personal data of real people, do not copy it into code, tests,
  docs, commit messages or the PR. Use test personas.

## 2. Set up the session

Do these before any code changes, in this order:

1. **Rename the session** with `set_session_title` (`session_id: "self"`): a short title from the
   task name, in the task's language.
2. **Workspace.**
   - In the main clone (`git rev-parse --git-dir` equals `--git-common-dir`): create a worktree with
     `EnterWorktree` (`name` = the branch slug below) and work there. The tool picks its own branch
     name, so step 3 still applies.
   - Already in a worktree: stay in it. Do not move or rename its folder; the session runs from that
     folder, and moving it breaks the session. Check that it is clean and has no commits beyond
     `origin/main`. If it does, ask before reusing it. If it is behind, bring it up to date with
     `git merge --ff-only origin/main`.
   - Either way, start the app with `aspire start --isolated` (standard 02, rule 7).
3. **Branch.** Name it `<type>/<english-kebab-slug>`, with `type` one of `feat`, `fix`, `docs`,
   `refactor`, `chore`, `perf`, for example `feat/note-search`. Rename a generated `claude/...` branch
   with `git branch -m`. If the branch is already pushed, follow the branch rules in `AGENTS.md`.
4. **Claim the task.** Resolve the user's member id with `clickup_resolve_assignees` (`["me"]`). Then
   call `clickup_update_task` with the in-progress status and `assignees` set to the current assignees
   **plus** the user. Always pass the full list: if the tool replaces rather than adds, a list with only
   the user removes everyone else.

## 3. Do the work

Work as for any other task: the usual process skills, `AGENTS.md`, the `bcc-standards` skill, tests,
and tech-debt findings to `docs/tech-debt/`. The acceptance criteria in the task are the definition of
done. Check each one before opening the PR, and say which were left out and why.

## 4. When the PR is created

Use the `pr` skill. Add these to it:

- The PR body's first line is `ClickUp: [<task name>](https://app.clickup.com/t/<id>)`. The id must be
  in the PR, so a later session can find the task from the PR.
- **Before the `pr` skill's cleanup (its step 7) deletes `$OUT`**, copy every PNG in it to the
  scratchpad. The ClickUp upload happens after the PR exists.

After `gh pr create`, update the task:

1. `clickup_update_task` with the review status.
2. **Screenshots** (only if `frontend/` changed and screenshots were captured): attach the "after"
   shots, plus the "before" shot for each where the difference matters. For each file, call
   `clickup_request_attachment_upload` (`task_id`, `file_name`) and upload the local PNG with `curl` as
   the response describes. Give each file a name that says what it shows, for example
   `note-dialog-after.png`. If no screenshots exist but the change is visible, take them with the
   `screenshot` skill. If the upload fails, say so and carry on; the PR still has them.
3. **Comment** with `clickup_create_comment` (`entity_id` = the task id, `notify_all: false`), in the
   task's language:

   ```
   PR: [#<n> <PR title>](<PR url>)

   <One short paragraph: what was built or changed, from the user's point of view first, and any
   acceptance criterion left out or decision made along the way. Mention attached screenshots.>
   ```

   The paragraph is for stakeholders who read ClickUp, not GitHub. Leave out file names and class
   names unless the task is technical.

Read the task back with `clickup_get_task` to confirm the status and attachments took. Then tell the
user the task moves to the done status only if this session learns of the merge, so they should say so
here when it is merged.

## 5. When the PR is merged

Set the task to the done status as soon as the session learns the PR merged: the user saying so, a CI
monitor event, or any time you check the PR. Confirm first with
`gh pr view <n> --json state,mergedAt` (state `MERGED`). A PR that closed without merging does not
count. Then `clickup_update_task` with the done status. Add no comment, since the merge is already in
the PR.

If the PR was closed without merging, leave the status alone and ask the user whether the task should
go back to in progress or ready, or stay in review for a replacement PR.

If a later session is asked about a merged PR, find the task id in the `ClickUp:` line of the PR body.

## Quick reference

| Moment | Task status | Other updates |
|---|---|---|
| Start | in progress | User added to assignees, session renamed, branch named |
| PR opened | review | Comment with PR link and summary, screenshots attached |
| PR merged | done | none |

## Common mistakes

- Passing only the user as `assignees`, which removes the other assignees.
- Starting to code with the task's open questions unanswered.
- Leaving the ClickUp line out of the PR body. Later sessions cannot find the task without it.
- Writing the comment in a different language from the task, or as a file-by-file changelog.
- Setting done when the PR was closed without merging.
