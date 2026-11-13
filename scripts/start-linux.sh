#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
exec dotnet run --project src/PocketBridge.Desktop/PocketBridge.Desktop.csproj -c Release -- "$@"
