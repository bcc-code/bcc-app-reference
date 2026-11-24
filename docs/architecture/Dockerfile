# The Structurizr images, pinned by digest (standard 17). Nothing builds from this file except the AppHost (the
# `structurizr` stage); it is the one place the image references live, so Dependabot can update them. Keep both on the
# same release. A new `export` image changes the hash in diagrams/source.sha256: run export.ps1 or export.sh and commit.

# Structurizr local (viewer and DSL editor) and `validate`. No browser.
FROM structurizr/structurizr:2026.10.26@sha256:8aebde645a7cc8d3b9e1e774cd6d75a84ce45d74ae70eb783876182fe7db31a2 AS structurizr

# PNG and SVG export needs a headless browser, which only the -playwright variant has (about 2 GB).
FROM structurizr/structurizr:2026.10.26-playwright@sha256:77dc0c529d9bdf46a19da044d6a4f430369b8d85bf9142c1cfc3ed536de262c1 AS export
