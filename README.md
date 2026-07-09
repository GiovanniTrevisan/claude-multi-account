# Claude Multi-Account

[Leia em português](README.pt-BR.md)

Run a second, fully isolated [Claude Desktop](https://claude.ai/download) instance
(e.g. a work account alongside your personal one) with **its own login, cookies,
history and cache**, and make that instance show up as a **separate app in the
Windows taskbar** — its own icon, its own grouping, pin it independently — without
installing, copying, or modifying a single file of the official Claude
installation.

```
Claude (personal)     Claude Work (corporate)
     [icon A]                [icon B]        <- separate taskbar groups
```

## Why this isn't trivial

Claude Desktop on Windows ships as a Microsoft Store **MSIX** package. MSIX apps
run under a fixed package identity (`AppUserModelID`, `Claude_<hash>!Claude` in
this case) — that's what Windows uses to decide which windows to group in the
taskbar. There is only **one** `claude.exe` installed, so no matter how many
different user-data directories you launch it with, every window still belongs to
the same process/package identity and keeps grouping together.

The obvious fix would be to open the package, patch Electron's bootstrap to call
`app.setAppUserModelId(...)` with an ID of our own, and repackage — that's how
"Insiders"/"Canary"/"Dev" builds of other apps (VS Code, Chrome, Edge) solve this.
**That route is not viable here**: every file in the Store package (`claude.exe`,
`app.asar`, the `.pak` resources) is protected with `FILE_ATTRIBUTE_ENCRYPTED`
(visible via `cipher /c`, reported as "Protected App") — the Store's own
anti-copy protection. Copying or hard-linking those files fails by NTFS/EFS
design, so there is no way to extract, patch and repackage anything from the
official Claude build.

## The fix: per-window AppUserModelID

Windows exposes an official, lesser-known API for exactly this —
[`SHGetPropertyStoreForWindow`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-shgetpropertystoreforwindow) —
which lets you read/write Shell properties (including `PKEY_AppUserModel_ID`)
**directly on a specific window**, from outside the process that owns it, without
needing that process's cooperation. Stamping a different AUMID onto the "Work"
instance's window makes Windows treat it as a separate app in the taskbar —
immediately, no Explorer restart needed — and it never touches a single file of
the protected package.

What `Claude Work.exe` does:

1. Locates the official Claude installation via `Get-AppxPackage` (caches the
   resolved path; re-resolves automatically if Claude updates to a new version).
2. Starts `claude.exe --user-data-dir=<own profile> --disk-cache-dir=<own cache>`.
   This alone isolates login/cookies/history/cache (Electron's single-instance
   lock is per profile, so the personal and Work instances coexist).
3. Finds that instance's window(s) (by matching the profile directory in each
   `claude.exe` process's command line) and stamps them with:
   - `PKEY_AppUserModel_ID` → its own taskbar identity;
   - `PKEY_AppUserModel_RelaunchCommand` / `RelaunchDisplayNameResource` /
     `RelaunchIconResource` → so pinning grabs the right icon;
   - `WM_SETICON` → our own `.ico` (from `assets/`) instead of Claude's default icon.
4. Stays resident while the instance runs, re-stamping any new windows Electron
   opens, and exits on its own once the Work instance closes.
5. Registers the AUMID under `HKCU\...\AppUserModelId\<aumid>` (name + icon), so
   Windows notifications also show up with their own identity.

Verified manually: closing and reopening the Work instance keeps the taskbar
grouping separate — the stamp is reapplied to each new window automatically.

## Requirements

- Windows 10/11
- Claude Desktop installed from the Microsoft Store
- To build: the .NET Framework 4 `csc.exe` compiler, which ships with Windows
  (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) — no external SDK
  needed

## Install

Download the latest installer (`ClaudeMultiAccount-Setup.exe`) from the
[Releases page](https://github.com/GiovanniTrevisan/claude-multi-account/releases)
and run it. It installs per-user (no admin rights needed), creates the
shortcuts automatically, and registers a proper uninstaller under Windows
Settings → Apps.

## Build from source

```powershell
git clone https://github.com/GiovanniTrevisan/claude-multi-account.git
cd claude-multi-account
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

`-Install` also creates the shortcuts (Start Menu + Desktop) with the custom
icon. Without that flag, the build just produces `dist\Claude Work.exe`.

To build the installer itself (requires [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
.\build.ps1
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 installer\ClaudeWork.iss
```

Pushing a `v*` tag (e.g. `v1.0.0`) triggers
[`.github/workflows/release.yml`](.github/workflows/release.yml), which builds
the installer and attaches it to a new GitHub Release automatically.

## Usage

Click the "Claude Work" shortcut that was created (or run
`dist\"Claude Work.exe"` directly). It stays resident in the background taking
care of the window's identity; close Claude Work normally and the launcher
process exits on its own.

To uninstall: use Windows Settings → Apps if you used the installer, or run
`Claude Work.exe --uninstall` to remove the shortcuts and registry entry
manually (your isolated Claude profile — login, cookies, cache — is left
untouched either way).

### Configuration

Environment variables, all optional:

| Variable              | Default                    | Effect                                     |
|------------------------|-----------------------------|---------------------------------------------|
| `CLAUDE_WORK_PROFILE`  | `Claude-Work`              | Profile name (`%LOCALAPPDATA%\<name>`)      |
| `CLAUDE_WORK_AUMID`    | `ClaudeMultiAccount.Work`  | AppUserModelID stamped on the window        |
| `CLAUDE_WORK_NAME`     | `Claude Work`               | Display name (error messages, registration) |

Running `Claude Work.exe --install-shortcuts` again recreates the shortcuts —
useful after changing these variables.

For a third account, just run with a different `CLAUDE_WORK_PROFILE` /
`CLAUDE_WORK_AUMID` pair.

## Project layout

```
src/
  Program.cs                    entry point / orchestration
  AppConfig.cs                  configuration from environment variables
  ClaudeInstallationLocator.cs  finds claude.exe (via Get-AppxPackage, cached)
  ClaudeInstanceLauncher.cs     starts claude.exe with the isolated profile
  ClaudeProcessInspector.cs     WMI queries over claude.exe command lines
  ClaudeWindowFinder.cs         EnumWindows-based window discovery
  TaskbarIdentityStamper.cs     applies AUMID + relaunch props + icon to a window
  InstanceIdentityWatcher.cs    resident loop: stamp new windows, exit when done
  ShortcutInstaller.cs          creates .lnk shortcuts + registers the AUMID
  NativeMessageBox.cs           tiny MessageBox wrapper
  Interop/                      P/Invoke declarations, COM interfaces, PROPVARIANT
                                 handling — isolated from the rest of the code
installer/
  ClaudeWork.iss                 Inno Setup script (per-user install + uninstaller)
.github/workflows/release.yml    builds and publishes the installer on a `v*` tag
```

## Known limitations

- Depends on `Get-AppxPackage`/PowerShell to locate Claude — if Anthropic renames
  the MSIX package, detection needs updating.
- The icon stamp (`WM_SETICON`) is reapplied every second while the instance
  runs, in case Electron redraws its default icon on top of ours; this is a
  small polling cost, not an event hook.
- If Anthropic ever ships a Win32 installer (outside the Store) for Claude
  Desktop, a direct `app.asar` patch (without this content protection) would
  also become viable — but it isn't needed: the current approach doesn't depend
  on it.

## License

MIT — see [LICENSE](LICENSE).
