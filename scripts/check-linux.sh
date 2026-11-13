#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
dotnet build PocketBridge.slnx -c Release
dotnet run --project tests/PocketBridge.Core.Checks/PocketBridge.Core.Checks.csproj -c Release --no-build
