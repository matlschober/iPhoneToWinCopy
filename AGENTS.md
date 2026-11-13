# PocketBridge project guidance

Read `docs/LINUX_MINT_HANDOFF.md` before Linux work. Final Linux acceptance takes place on the user's dedicated Linux Mint laptop, not in WSL on the original Windows PC.

## Architecture and commands

- `src/PocketBridge.Core`: shared .NET 10 receiver, mobile web UI, data persistence, quota, settings and speech wording. No WPF/Avalonia dependency.
- `src/PocketBridge`: established Windows WPF app and Windows SAPI/clipboard/image integration. Preserve it.
- `src/PocketBridge.Desktop`: Avalonia 11.3.23 Linux desktop preview. Finish native Mint integration here.
- `PocketBridge.slnx` contains only cross-platform projects. On Mint use `bash scripts/check-linux.sh` and `bash scripts/start-linux.sh`; do not try to run the WPF tests.
- Windows regression: `dotnet run --project tests/PocketBridge.Checks -c Release` using SDK 10.
- Do not run dependent builds/tests concurrently: they share project outputs and can lock files on Windows.

## Product requirements to preserve

- Receive photos, text and links locally via the authenticated iPhone page on TCP 47831.
- QR secret stays in the URL fragment and an authenticated request header; no cloud services or remote clipboard/inbox read API.
- One configurable shared storage quota, default 512 MiB. No separate count/photo/text limits. UTF-8 bytes count toward quota.
- Persistent inbox; cleanup choice on start and exit if nonempty, with used/total MB or GB and one decimal for used storage. Keep/cancel must preserve data.
- Search count is filtered/total during a search, total otherwise.
- Copy and original-byte save; configurable cache folder with migration, free-space checks and rollback. Do not overwrite nonempty destination folders.
- One local spoken aggregate per transfer; German/English follow desktop language. Speech is optional and cannot make an upload fail.

## Data safety and validation

- Use `--data-dir /tmp/pocketbridge-qa --port 47835` for desktop QA. Never delete the user's real inbox or answer a real cleanup dialog with Delete on their behalf.
- Do not install system packages or alter firewall rules automatically. Setup scripts deliberately use a project-local SDK; document requested OS dependencies.
- On Linux honor XDG data paths and case-sensitive paths. Native clipboard and file pickers must be tested in the actual Cinnamon session.
- Clearly distinguish cross-compilation/Windows tests from tests actually executed on Mint. Do not call the Linux preview release-ready until the handoff checklist passes.
- Do not replace the previously delivered Windows ZIP. New Linux releases need their own packaging and dependency license notices.
