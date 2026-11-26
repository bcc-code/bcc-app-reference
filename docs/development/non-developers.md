# Non-developers working through Claude

How to set up an app so that a person who is not a developer can change it through Claude Code in the cloud
(claude.ai/code), and what stays with the developers. The person describes what they want; Claude changes the code, runs the
checks, runs the app and shows screenshots, and opens a pull request. A developer reviews and merges every pull request, and
deploys stay as they are ([deploying](README.md#deploying)).

## Who does what

| Who | Does |
|---|---|
| Developers | The first version of the app, anything under `.github/`, `infra/` and `.claude/`, sign-in and access rules, new kinds of data, reviewing and merging every pull request, deploys |
| The non-developer, through Claude | Changes to what the app looks like and does, inside what the app already has |
| Claude in their session | Follows the [`non-developer` skill](../../.claude/skills/non-developer/SKILL.md): plain language, runs every command itself, shows screenshots, opens the pull request, hands over to a developer when stuck |

The skill is switched on by `BCC_NON_DEVELOPER=true` on the cloud environment, through the SessionStart hook
`tools/cloud-session/non-developer.mjs`. Developers who use another environment, or work locally, get the usual behaviour.

## Setting it up

Once per repository:

1. **Branch ruleset on `main`** as in [repository settings](repository-settings.md), plus "Require review from Code Owners".
2. **CODEOWNERS**: replace the placeholder team in `.github/CODEOWNERS` and add a line for the whole repository
   (`* @bcc-code/<team>`), so every pull request asks the developers for review.
3. **Claude GitHub App** installed on the repository, so cloud sessions can clone, push and open pull requests
   ([Claude Code on the web](https://code.claude.com/docs/en/claude-code-on-the-web)).

Once per organisation, by an owner, as an organisation-shared cloud environment
([cloud environments](https://code.claude.com/docs/en/cloud-environments)):

| Setting | Value |
|---|---|
| Network access | Limited, with "Allow package managers", plus the domains `builds.dotnet.microsoft.com` and `nodejs.org` |
| Setup script | The contents of [`scripts/cloud-setup.sh`](../../scripts/cloud-setup.sh): the .NET SDK, Node 24, pnpm, the Postgres image |
| Environment variables | `BCC_NON_DEVELOPER=true` |

Anyone who uses an environment can read its variables and setup script, so neither holds a secret. The app needs none in a
cloud session: it runs against the OIDC mock and a local database.

## What happens in a session

1. The setup script has installed the toolchains (cached between sessions).
2. The SessionStart hook `tools/cloud-session/prepare.mjs` puts Node 24 first on the PATH and restores the .NET tools, the NuGet
   packages and the frontend packages. It skips the restore when the lock files have not changed, and tells Claude when the
   setup script has not run.
3. The SessionStart hook `tools/cloud-session/non-developer.mjs` tells Claude to follow the `non-developer` skill.
4. Claude makes the change, runs the checks, starts the app with `dotnet aspire run --isolated` (Docker runs inside the
   session) and shows screenshots. The Aspire CLI is the repository's local tool there, not a global `aspire`: a global one on
   the PATH would need its own bundle, and the AppHost build otherwise gets it through `dnx`, as in CI.
5. Claude opens the pull request with the GitHub REST API (the cloud's GitHub proxy rejects `gh pr`, which uses GraphQL). CI runs
   as for any pull request.
6. When the person wants to show the change to someone else, Claude adds the `preview` label, which gives a public preview
   ([pr-previews.md](../pr-previews.md)). Previews stay opt-in (standard 07.1).
7. A developer reviews and merges.

The app running in a cloud session cannot be opened from outside it: the session has outbound network access only. A developer
who wants to try a change takes the session to their own machine with `claude --teleport <session-id>` (or "Open in → Terminal"
on claude.ai; the work must be committed and pushed first) and runs `aspire run`, or opens the preview.

## Not yet checked in a cloud session

Checked in a clean `ubuntu:24.04` container, the cloud's OS: the setup script, then the hook on an empty package cache (about
2¼ minutes, including the retry of `dotnet tool restore` that dotnet/sdk#53783 needs), the full build, the unit and
architecture tests, and the frontend type check and tests.

The documentation of the cloud environment says Docker is available, but not whether these work there. Try them in the first
session and update this page:

- `dotnet aspire run --isolated` (the AppHost starts Postgres, the OIDC mock and Vite as containers and processes).
- The integration tests (Testcontainers).
- The E2E tests (`E2E_BROWSER_CHANNEL=chrome` with the image's Chrome for Testing) and the `screenshot` skill (`playwright-cli`).
- Whether `builds.dotnet.microsoft.com` is needed, or `dot.net` alone is enough for the .NET install script.
- How long the setup script and the first restore take on the cloud VM. The setup cache only keeps a script that finishes in
  about five minutes; a hook is stopped after ten.
