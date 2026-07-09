; Inno Setup script for Claude Work.
;
; Expects the app to already be built (see build.ps1) at ..\dist\Claude Work.exe.
; Version is normally passed by CI: ISCC /DMyAppVersion=1.2.3 ClaudeWork.iss
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "Claude Work"
#define MyAppPublisher "Claude Multi-Account contributors"
#define MyAppURL "https://github.com/GiovanniTrevisan/claude-multi-account"
#define MyAppExeName "Claude Work.exe"

[Setup]
AppId={{10169E72-046F-4022-BA61-DBBF3E6F0636}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; Everything this app touches is per-user (HKCU registry, %LOCALAPPDATA%
; install/profile dirs) — no admin rights should be needed to install it.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=ClaudeMultiAccount-Setup
SetupIconFile=..\assets\claude-work.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\dist\Claude Work.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\claude-work.ico"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; Creates the Start Menu/Desktop shortcuts and the AppUserModelID registration
; ourselves (see src/ShortcutInstaller.cs) instead of Inno's built-in [Icons],
; since only our own code embeds the custom icon and relaunch properties into
; the shortcut correctly.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-shortcuts"; Flags: runhidden; StatusMsg: "Creating shortcuts..."
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Must run before Inno deletes the exe below, so it can still execute itself
; to remove the shortcuts and registry entry it created.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; Flags: runhidden
