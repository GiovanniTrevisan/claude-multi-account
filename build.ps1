<#
  Compiles Claude Work.exe (plain .NET Framework 4, no external SDK required)
  with the icon embedded, and copies the .ico next to the compiled binary.

  Usage:
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # also creates shortcuts
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$ico = Join-Path $root "assets\claude-work.ico"
$outDir = Join-Path $root "dist"
$outExe = Join-Path $outDir "Claude Work.exe"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw ".NET Framework 4 compiler not found at $csc" }
if (-not (Test-Path $ico)) { throw "Icon not found at $ico" }

$sources = Get-ChildItem -Path (Join-Path $root "src") -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "No .cs files found under src\" }

New-Item -ItemType Directory -Force $outDir | Out-Null

$refs = @(
  "/reference:System.Management.dll"
)

Write-Host "Compiling $outExe ..."
& $csc `
  /nologo `
  /target:winexe `
  /platform:x64 `
  /optimize+ `
  "/out:$outExe" `
  "/win32icon:$ico" `
  @refs `
  $sources

if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit $LASTEXITCODE)" }

Copy-Item $ico (Join-Path $outDir "claude-work.ico") -Force
Write-Host "OK: $outExe"

if ($Install) {
  Write-Host "Creating shortcuts (Start Menu + Desktop)..."
  & $outExe --install-shortcuts
  Write-Host "Shortcuts created."
}
