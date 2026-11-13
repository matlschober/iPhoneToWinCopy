#!/usr/bin/env bash
# Source this file; paths are resolved from the script rather than the caller's cwd.
POCKETBRIDGE_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ -x "$POCKETBRIDGE_ROOT/.tools/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$POCKETBRIDGE_ROOT/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
elif ! command -v dotnet >/dev/null 2>&1; then
  printf '%s\n' 'No .NET SDK found. Run: bash scripts/setup-linux.sh' >&2
  return 1
fi
cd -- "$POCKETBRIDGE_ROOT"
if [[ "$(dotnet --version)" != 10.* ]]; then
  printf '%s\n' '.NET 10 SDK required. Run: bash scripts/setup-linux.sh' >&2
  return 1
fi
