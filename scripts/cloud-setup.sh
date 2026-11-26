#!/usr/bin/env bash
# Setup script for a Claude Code cloud environment (claude.ai/code). Load this file into the environment's "Setup script"
# field; see docs/development/non-developers.md. It runs as root on Ubuntu 24.04 before Claude Code starts, and its result is
# cached, so it installs only toolchains and does not depend on the checkout. The repository's packages are restored by the
# SessionStart hook tools/cloud-session/prepare.mjs, which also puts Node 24 first on the PATH.
#
# The environment needs these domains on top of "Limited" with "Allow package managers":
#   builds.dotnet.microsoft.com   the .NET SDK download (dot.net, which serves the install script, is already allowed)
#   nodejs.org                    Node 24 (the image has Node 22; frontend/package.json requires 24)
#
# Keep the versions in step with global.json (the SDK feature band) and frontend/package.json (engines.node, devEngines pnpm).
set -euo pipefail

DOTNET_CHANNEL=11.0.1xx
NODE_MAJOR=24
PNPM_VERSION=12.17.0
POSTGRES_IMAGE=postgres:18 # api/AppHost/DatabaseResources.cs and the integration tests' PostgresFixture

DOTNET_DIR=/usr/share/dotnet
NODE_DIR=/opt/node$NODE_MAJOR

# .NET SDK. Not in the image.
if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%xx}"; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR" --no-path
fi
ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
# The AppHost build (Aspire 17) gets the Aspire CLI bundle through dnx when no `aspire` is on the PATH, as in CI. Do not put an
# `aspire` wrapper on the PATH: the build would pick it and fail with ASPIRE009. Run the CLI as `dotnet aspire` (a local tool).
ln -sf "$DOTNET_DIR/dnx" /usr/local/bin/dnx
# Built executables (the AppHost, the API) find the runtime through this file when DOTNET_ROOT is not set.
mkdir -p /etc/dotnet
echo "$DOTNET_DIR" > /etc/dotnet/install_location

# Node, checked against the release's SHASUMS256.txt.
if ! "$NODE_DIR/bin/node" --version 2>/dev/null | grep -q "^v$NODE_MAJOR\."; then
  case "$(uname -m)" in
    x86_64) arch=x64 ;;
    aarch64) arch=arm64 ;;
    *) echo "Unsupported architecture $(uname -m)" >&2; exit 1 ;;
  esac
  base="https://nodejs.org/dist/latest-v$NODE_MAJOR.x"
  sums=$(curl -fsSL "$base/SHASUMS256.txt")
  file=$(awk -v suffix="linux-$arch.tar.xz" '$2 ~ suffix"$" { print $2 }' <<<"$sums")
  curl -fsSL "$base/$file" -o "/tmp/$file"
  (cd /tmp && grep " $file\$" <<<"$sums" | sha256sum -c -)
  rm -rf "$NODE_DIR"
  mkdir -p "$NODE_DIR"
  tar -xJf "/tmp/$file" --strip-components=1 -C "$NODE_DIR"
fi

# npm and pnpm start with `#!/usr/bin/env node`; without this they would run on the image's Node 22.
export PATH="$NODE_DIR/bin:$PATH"

# pnpm, the exact version the lock file was written with (devEngines would otherwise download it on first use).
if [ "$(pnpm --version 2>/dev/null)" != "$PNPM_VERSION" ]; then
  npm install --global "pnpm@$PNPM_VERSION"
fi

# The database image, so the first `aspire run` or integration test run does not wait for the pull. Best effort: the cache
# keeps files, and Docker may not be running this early.
if docker info >/dev/null 2>&1; then
  docker pull --quiet "$POSTGRES_IMAGE" || echo "Could not pull $POSTGRES_IMAGE; it is pulled on first use instead."
else
  echo "Docker is not running yet; $POSTGRES_IMAGE is pulled on first use instead."
fi

echo ".NET SDK $("$DOTNET_DIR/dotnet" --version), Node $(node --version), pnpm $(pnpm --version)"
