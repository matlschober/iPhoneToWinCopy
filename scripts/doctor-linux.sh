#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' 'PocketBridge Linux diagnostics'
[[ -r /etc/os-release ]] && cat /etc/os-release
printf 'Architecture: %s\nSession: %s\nDesktop: %s\nDISPLAY: %s\nLANG: %s\n' "$(uname -m)" "${XDG_SESSION_TYPE:-unset}" "${XDG_CURRENT_DESKTOP:-unset}" "${DISPLAY:-unset}" "${LANG:-unset}"
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -x "$repo_root/.tools/dotnet/dotnet" ]]; then
  "$repo_root/.tools/dotnet/dotnet" --info
elif command -v dotnet >/dev/null 2>&1; then
  dotnet --info
else
  printf '%s\n' 'SDK missing: bash scripts/setup-linux.sh'
fi
ldconfig_command="$(command -v ldconfig || true)"
[[ -z "$ldconfig_command" && -x /sbin/ldconfig ]] && ldconfig_command=/sbin/ldconfig
libraries="$(${ldconfig_command:-false} -p 2>/dev/null || true)"
for library in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
  if grep -Fq "$library" <<< "$libraries"; then printf 'OK: %s\n' "$library"; else printf 'MISSING: %s\n' "$library"; fi
done
for command_name in espeak-ng spd-say; do
  if command -v "$command_name" >/dev/null 2>&1; then command -v "$command_name"; else printf 'Optional speech backend missing: %s\n' "$command_name"; fi
done
printf '%s\n' 'For GUI tests, run inside the logged-in Mint desktop session. See docs/LINUX_MINT_HANDOFF.md.'
