# Isolated mode: worktrees and parallel agents

In a git worktree, and for every parallel agent or second checkout, always start the app isolated:

```bash
aspire start --isolated      # or: aspire run --isolated
aspire describe              # the actual URLs of this instance
```

Isolated mode uses random ports, a throwaway database and no persistent containers. Without it, two checkouts share the fixed
ports and the persistent Postgres container, and one instance silently reads and writes the other's data. The sole,
non-isolated instance uses the fixed ports listed in [AGENTS.md](../../AGENTS.md).

The E2E tests start the AppHost the same way (random ports, throwaway database), so they can run next to a running instance.

When more than one AppHost is in the checkout's reach (for example a worktree under the main checkout), tell the Aspire CLI which
one to use with `aspire select` or the `--apphost` option, so it does not attach to another instance.
