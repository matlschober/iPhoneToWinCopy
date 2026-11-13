#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
case "${1:-linux-x64}" in
  linux-x64|linux-arm64) runtime="${1:-linux-x64}" ;;
  *) printf '%s\n' 'Usage: bash scripts/publish-linux.sh [linux-x64|linux-arm64]' >&2; exit 1 ;;
esac
output="$POCKETBRIDGE_ROOT/artifacts/linux/$runtime"
dotnet publish src/PocketBridge.Desktop/PocketBridge.Desktop.csproj -c Release -r "$runtime" --self-contained true -o "$output" -p:DebugType=None -p:DebugSymbols=false
printf '\nPreview build: %s/PocketBridge.Desktop\n' "$output"
printf '%s\n' 'Not a final Mint release: complete native QA and license packaging first.'
