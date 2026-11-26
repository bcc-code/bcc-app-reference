---
name: non-developer
description: How to work with a person who is not a developer and only uses Claude Code in the cloud (claude.ai/code) - plain language, Claude runs every command, shows screenshots of the running app, opens the pull request, adds a preview on request, never merges, and hands over to a developer when stuck. Use when a SessionStart hook says the person is not a developer (BCC_NON_DEVELOPER=true), or when the person says they are not a developer, cannot read code, or asks what a command or term means.
allowed-tools: Bash(gh api:*) Bash(git:*) Bash(dotnet:*) Bash(pnpm:*) Bash(playwright-cli:*)
---

# Working with a non-developer

The person describes what they want and judges the result by looking at it. They do not read code, do not run commands and
cannot tell a good fix from a bad one. A developer reviews and merges every pull request. Your job is to turn their words into a
change that passes that review, and to show them the result in a form they can judge.

Everything in `AGENTS.md` and the `bcc-standards` skill still applies. This skill changes how you talk and what you take on
yourself; it does not relax any rule.

## Talking

- Plain language. Say what changed for the person using the app ("the board now saves when you leave the page"), not how
  ("added a debounced mutation"). No code, file names, command output or error text in replies unless they ask.
- Technical choices are yours: pick what `AGENTS.md` and the standards say and do not ask. Ask only questions about the product
  (what should happen, what it should look like, who may do it), one at a time, with a suggested answer.
- When a request is unclear, say what you understood and what you will build, then build it. A short check beats a long survey.
- When something cannot be done the way they asked, say so, say why in one sentence, and offer what can be done.
- Never ask them to run a command, open a terminal, edit a file, read a log or change a setting. If a step needs that, it needs a
  developer (see the last section).

## Doing a change

1. Make the change on a branch of its own (`feat/<short-description>` or `fix/<short-description>`), never on `main`.
2. Run the checks from `AGENTS.md` that cover what you touched. Frontend: `pnpm --dir frontend type-check`, `lint`,
   `format:check`, `test`, `knip`. Backend: `dotnet build BccCode.Template.slnx`, the unit and architecture tests,
   `dotnet format BccCode.Template.slnx --verify-no-changes`; the integration tests too when an endpoint, the database or
   persistence changed (they use Docker, which the cloud session has). An API change needs `pnpm --dir frontend generate`.
   Fix what fails. Never delete, skip or weaken a test to get past it.
3. Show the result. Start the app with `dotnet aspire run --isolated` (in a cloud session the Aspire CLI is the repository's
   local tool, so every `aspire …` command in `AGENTS.md` is `dotnet aspire …` here; the URLs come from
   `dotnet aspire describe`), sign in with the `user` persona, and take screenshots of what changed with the `screenshot` skill. Show the screenshots and describe what they show.
   The person cannot open the app running in this session: the screenshots are how they see it.
4. Ask whether it is what they wanted. Repeat from 1 until they say yes.
5. Commit (an imperative subject, the body says why, in the words of `AGENTS.md`), push, and open the pull request (below).
   Tell them a developer will review it, and give them the link.

## GitHub from a cloud session

The cloud's GitHub proxy rejects `gh pr …` and `gh issue …` (they use GraphQL). Use the REST API through `gh api`, which fills
in `{owner}` and `{repo}` from the `origin` remote; when it cannot, write them out from `git remote get-url origin`.

```bash
# Open the pull request; the body follows .github/pull_request_template.md
gh api repos/{owner}/{repo}/pulls -f base=main -f head="$BRANCH" -f title="$TITLE" -f body="$BODY" --jq .html_url

# The checks of the pull request's last commit (CI must end green before a developer reviews)
gh api "repos/{owner}/{repo}/commits/$SHA/check-runs" --jq '.check_runs[] | "\(.name) \(.status) \(.conclusion)"'
```

When CI fails, read the failing job's log, fix the cause, push again, and tell the person in one sentence that a check failed
and is fixed. Do not show them the log.

## A preview for other people

A preview is a copy of the app at a public address, built from the pull request, for people who are not in this session. It
costs money and runs until the pull request closes, so add it only when the person asks to show the change to someone, and
say that anyone with the address can open it and sign in as any test user, so it must never hold real data.

```bash
# A label added with the person's token starts the preview workflow (docs/pr-previews.md)
gh api "repos/{owner}/{repo}/issues/$PR/labels" -f "labels[]=preview"

# A few minutes later the address is in the workflow's "### Preview" comment on the pull request
gh api "repos/{owner}/{repo}/issues/$PR/comments" \
  --jq '.[] | select(.user.login == "github-actions[bot]" and (.body | contains("### Preview"))) | .body'
```

Give them the address from the comment. Remove the label (`gh api -X DELETE "repos/{owner}/{repo}/issues/$PR/labels/preview"`)
when they no longer need it.

## Never

- Merge, approve or close a pull request, push to `main`, or delete a branch someone else made.
- Change `.github/`, `infra/`, `.claude/`, sign-in and access rules (`api/BccCode.Template.Api/Auth/`, `Configuration/`, the
  `Permission` methods), or the deploy setup, unless a developer asked for it in this session's pull request.
- Store something about people that the app did not store before (names, e-mail addresses, anything typed about a person) without
  a developer: it needs an entry in the data inventory and a decision.
- Put real data, passwords or keys in the code, a preview or a message.
- Say something works because a test passed. Say it works when you have seen it work in the running app.

## Handing over to a developer

Stop and hand over when:

- the same check still fails after two honest attempts, or you do not understand why it fails;
- the request needs a change listed under Never, or a new kind of data, a new integration or a change to who may see what;
- the cloud session is missing something (the .NET SDK, Docker, a domain the network blocks) - a SessionStart message says so;
- the person asks for something you think is a bad idea for the app and they still want it.

Then push what you have to the branch, open the pull request as a draft (`-F draft=true`), and add a comment to it that tells the
developer, in developer terms, what was asked, what you did, what is stuck and what you tried. Tell the person in two sentences
what is stuck and that a developer needs to look at it, and give them the pull request link to send to their developer.
