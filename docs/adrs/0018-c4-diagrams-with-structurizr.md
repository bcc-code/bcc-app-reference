# 0018. C4 diagrams as Structurizr DSL, exported to SVG by a script, checked by hash in CI

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0020.

## Context

Standard 23.4 asks for C4 context and container diagrams kept as editable source next to an exported SVG. The template used Mermaid text with no exported SVG, which broke that rule. Structurizr's file-based export reads layout only from a `workspace.json` next to the DSL, and rendering SVG needs a headless browser image of about 2 GB. Diagrams that silently fall behind the model are worse than none (standard 23).

## Decision

We will:

- Keep the model in `docs/architecture/workspace.dsl` and the layout in `docs/architecture/workspace.json`. The deployment views use `autolayout`; the other views may be laid out by hand in Structurizr local, which saves positions to `workspace.json`.
- Export SVGs with `docs/architecture/export.ps1` or `export.sh`. They run `structurizr/structurizr:<release>-playwright`, apply the layout to the model with `merge`, and write `docs/architecture/diagrams/<view>.svg` and `diagrams/source.sha256`, a hash of the DSL, the layout and the export image reference. The diagram footer hides the generation date (`structurizr.metadata`), so an export only changes when the model or layout does.
- Check in CI (`architecture` job) that the DSL validates and that `export.sh --check` finds `source.sha256` matching; CI does not render.
- Pin both images by digest in `docs/architecture/Dockerfile`, which only exists so the references live in one file that Dependabot's docker ecosystem updates.
- Serve the workspace locally from the AppHost (`architecture` resource, Structurizr `local`), not in E2E runs (standard 02.13).

## Consequences

| | |
|---|---|
| Positive | SVGs render on GitHub; a stale export fails the PR without a browser in CI. The export is byte-stable, so a re-export without a change produces no diff. |
| Negative | Two files to commit together. Positions are matched by element name, so a rename drops a position until it is placed again. After a DSL change Structurizr rewrites `workspace.json` once when the diagrams are opened, so exporting before opening them gives a stale hash. Contributors need Docker and a one-off 2 GB pull to export. The hash proves the SVGs were exported from these files, not that nobody edited an SVG by hand afterwards. A Dependabot bump of the export image fails `architecture` until someone runs the script on that PR. |
| Follow-up | An application re-exports when it adds containers, integrations or slices. |

## Alternatives considered

- **Mermaid C4 blocks** — GitHub renders them, but the C4 support is experimental, the layout is not controllable, and there is no SVG file as standard 23.4 asks.
- **Render in GitHub Actions and commit the SVGs back** — a commit pushed with `GITHUB_TOKEN` does not start the CI workflow again, and every run pulls the 2 GB image.
- **Re-render in CI and diff the SVGs** — stronger than the hash, but costs the 2 GB pull on every PR that touches the folder.
