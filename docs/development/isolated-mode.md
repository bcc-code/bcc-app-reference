# Isolated mode: worktrees and parallel agents

In a git worktree, and for every parallel agent or second checkout, always start the app isolated:

```bash
aspire start --isolated      # or: aspire run --isolated
aspire describe              # the actual URLs of this instance
```

Without it, two checkouts share the fixed ports and the persistent Postgres container, and one instance silently reads and writes
the other's data. The sole, non-isolated instance uses the fixed ports listed in [AGENTS.md](../../AGENTS.md).

## What an isolated run gets

`api/AppHost/RunMode.cs` names everything after the checkout folder (lower case, anything but a letter or digit becomes `-`, at most
40 characters):

| | Sole instance | Isolated run | E2E run |
|---|---|---|---|
| Ports | fixed (16000...) | random | random |
| Browser address | `https://template.dev.localhost:16000` | `https://template-<folder>.dev.localhost:<port>` | `https://localhost:<port>` |
| Postgres container | persistent | removed when the AppHost stops | removed when the AppHost stops |
| Postgres data | volume `bcc-template-postgres` | volume `bcc-template-<folder>-postgres` | none |
| OIDC mock cookie | `Template.OidcMock` | `Template.OidcMock.<folder>` | `Template.OidcMock` |

- **A host name per instance.** Browsers keep cookies per host name, not per port. If every instance were browsed on
  `template.dev.localhost`, parallel instances would share the API's `__Host-` session cookie and sign each other out. Browsers
  resolve `*.localhost` themselves, so no hosts-file entry is needed, and the dev certificate covers `*.dev.localhost`.
- **A cookie name per instance in the OIDC mock.** The mock is always reached on `localhost`, so instances are kept apart by cookie
  name instead (`Session:CookieSuffix`).
- **A database volume per worktree.** A restart of the same worktree finds its data: the schema step only applies migrations that
  are new, and the seeder finds its rows. The password is fixed for isolated runs (a parameter in `DatabaseResources.cs`),
  because Postgres keeps the password a data directory was created with.

## Disk use

Each worktree keeps one Postgres volume while the worktree exists: an empty cluster with the template's seed is a few tens of MB,
and a large seed makes it larger (Orginn's are about 275 MB each). The container is gone when the AppHost stops, so a worktree
that is not running uses no memory or CPU. On Docker Desktop for Windows the volumes live in the WSL2 virtual disk, which does not
always shrink by itself after volumes are removed; Docker Desktop's clean-up or compacting the virtual disk gives the space back.

`tools/worktree-cleanup/prune-volumes.mjs` removes the volumes of worktrees that no longer exist (`--dry-run` lists them). It runs
at the start of every Claude Code session from the `SessionStart` hook in `.claude/settings.json`, and prints nothing when there
is nothing to do or Docker is not running. It never touches `bcc-template-postgres` or a volume a container uses. Run it by hand
after removing worktrees outside Claude Code:

```bash
node tools/worktree-cleanup/prune-volumes.mjs
```

To start a worktree's database from scratch, stop the AppHost and remove its volume:
`docker volume rm bcc-template-<folder>-postgres`.

## Picking the AppHost

When more than one AppHost is in the checkout's reach (for example a worktree under the main checkout), tell the Aspire CLI which
one to use with `aspire select` or the `--apphost` option, so it does not attach to another instance.
