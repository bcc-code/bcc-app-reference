---
standard: 23 Documentation and Decisions
assessed: 2026-10-19
commit: e4b2b86
---
# 23 Documentation and Decisions — assessment

The `docs/` structure, ADRs in Nygard format with maintained status, and the tech-debt convention are in place. Most gaps are
small but real: the README has no ports, the manual folder has a different name, the C4 diagrams lack the exported SVG, known
issues sit in TODO comments instead of tech-debt files, and the claim that AI-drafted documents are marked is true for only two
documents. No MUST rule is failed outright; six are partly met, which is more than "a couple", but each fix is a small edit.

**Score: Adequate** · ✅ 5 · ⚠️ 6 · ❌ 1 · N/A 3

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | README short and correct: purpose, run, ports, links to `docs/` | MUST | ⚠️ | `README.md` has purpose, run commands (`:55-59`) and links. Ports are only in `AGENTS.md:40-41`. |
| 2 | `AGENTS.md` is the developer and agent guide | MUST | ✅ | `AGENTS.md` (commands, rules, gotchas, deployment, process). |
| 3 | `docs/` uses the fixed layout | MUST | ⚠️ | `adrs/`, `architecture/`, `runbooks/`, `specs/`, `tech-debt/`, `privacy/` exist. The end-user manual is `docs/user-manual/`, not `docs/manual/`. |
| 4 | C4 diagrams as editable source next to an exported SVG | MUST | ⚠️ | `docs/architecture/README.md` holds Mermaid C4 source (not Structurizr or Excalidraw as named in the rule) and no SVG; `:5-6` is a TODO to export. |
| 5 | Engineering docs separate from notes and evaluations | MUST | ✅ | `docs/` contains no meeting or customer notes. |
| 6 | Significant changes start as a spec in `docs/specs/` | MUST | ✅ | `docs/specs/README.md` defines content, flow and status. The template ships no specs; practice is not checkable. |
| 7 | ADRs in Nygard format; consequences table and alternatives | MUST / SHOULD | ✅ | ADRs 0001–0006 have Status, Context, Decision, Consequences; 0001–0005 have a consequences table and "Alternatives considered". ADR 0006 has neither (SHOULD part). |
| 8 | ADRs written at decision time; status maintained | MUST | ✅ | All six `Status: Accepted` with dates; `docs/adrs/README.md:7` states the lifecycle. Timing is not checkable from one commit. |
| 9 | Specs kept current or marked superseded | MUST | N/A | No specs exist. |
| 10 | Agent plans in `docs/superpowers/` do not replace ADRs | MAY / MUST NOT | N/A | No `docs/superpowers/`; `.superpowers/` is gitignored. |
| 11 | User docs and developer how-tos follow Diátaxis | MUST | ⚠️ | `docs/user-manual/README.md` sets up the four quadrants but has no content. `docs/development.md` mixes how-to sections with reference ("Conventions worth knowing", "Repository settings") on one page. |
| 12 | Docs checker in CI | SHOULD | ❌ | No docs job in `.github/workflows/pr.yml` or elsewhere. |
| 13 | `docs/public/` published to the developer portal | SHOULD | N/A | No integrator docs. |
| 14 | One file per issue in `docs/tech-debt/` with frontmatter; README; no index | MUST | ⚠️ | `docs/tech-debt/README.md` defines the convention, no index. It says "There are none yet", but known issues are recorded as TODO comments instead: `infra/preview/main.tf:34` (preview publicly reachable), `.github/workflows/preview.yml:171` (preview assets not uploaded), `infra/database.tf:25` (public IP), `.github/workflows/sbom.yml:36` (slnx not verified). |
| 15 | AI-drafted documents say so at the top and are marked for verification | MUST | ⚠️ | `docs/privacy/data-inventory.md:3-5` and `README.md:61-64` do. `docs/README.md:16` claims all AI-drafted documents say so, but the ADRs (including security ADR 0004), runbooks, `docs/development.md` and `infra/README.md` carry no marker, although the root README says much of the content was drafted with AI. |

## Gaps to close

- **15**: Add an "AI-drafted, verify before relying on it" line at the top of `docs/adrs/0004-*.md`, the runbooks,
  `docs/development.md` and `infra/README.md`, or narrow the claim in `docs/README.md:16`.
- **14**: Move the known issues in TODO comments (`infra/preview/main.tf:34`, `.github/workflows/preview.yml:171`,
  `infra/database.tf:25`, `.github/workflows/sbom.yml:36`, `.github/workflows/deploy.yml:114`) into one file each under
  `docs/tech-debt/`, or state in `docs/tech-debt/README.md` that template TODOs are placeholders for the adopting team.
- **4**: Export the two Mermaid diagrams to SVG next to `docs/architecture/README.md` (or switch to Structurizr DSL), and add a
  CI step that checks the SVG is current.
- **1**: Add the port table (API 18000, OIDC mock 18002, Postgres 18010, dashboard 18090) to `README.md`.
- **3**: Rename `docs/user-manual/` to `docs/manual/` and update the links in `docs/README.md`, `AGENTS.md` and the
  `writing-user-docs` and `screenshot` skills — or record the deviation in an ADR.
- **11**: Split `docs/development.md` into how-to pages and a reference page.
- **7** (SHOULD): add a consequences table and alternatives to ADR 0006.
- **12** (SHOULD): add a docs check job (structure, links, AI markers) to `pr.yml`.

## Not verifiable in the repo

- **6, 8**: whether specs preceded significant changes and whether ADRs were written when decided; check PR history once the
  repository has one.
