#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != Linux ]]; then
  printf '%s\n' 'Run this script on the dedicated Linux Mint laptop.' >&2
  exit 1
fi
command -v curl >/dev/null || { printf '%s\n' 'Install curl first: sudo apt install curl ca-certificates'; exit 1; }
mkdir -p -- "$repo_root/.tools"
curl --fail --location --retry 2 https://dot.net/v1/dotnet-install.sh --output "$repo_root/.tools/dotnet-install.sh"
bash "$repo_root/.tools/dotnet-install.sh" --channel 10.0 --install-dir "$repo_root/.tools/dotnet" --no-path
source "$repo_root/scripts/env.sh"
dotnet restore PocketBridge.slnx
printf '\n%s\n' 'SDK installed locally. No system packages, firewall rules or user app data were changed.'
bash "$repo_root/scripts/doctor-linux.sh"
