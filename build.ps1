<#
  Compila o Claude Work.exe (.NET Framework, sem dependencias externas) com o
  icone embutido, e copia o .ico para junto do exe.

  Uso:
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # + cria atalhos
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$src = Join-Path $root "src\ClaudeWork.cs"
$ico = Join-Path $root "assets\claude-work.ico"
$outDir = Join-Path $root "dist"
$outExe = Join-Path $outDir "Claude Work.exe"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe (.NET Framework 4) nao encontrado em $csc" }
if (-not (Test-Path $ico)) { throw "Icone nao encontrado em $ico" }

New-Item -ItemType Directory -Force $outDir | Out-Null

$refs = @(
  "/reference:System.Management.dll"
)

Write-Host "Compilando $outExe ..."
& $csc `
  /nologo `
  /target:winexe `
  /platform:x64 `
  /optimize+ `
  "/out:$outExe" `
  "/win32icon:$ico" `
  @refs `
  "$src"

if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao (csc exit $LASTEXITCODE)" }

Copy-Item $ico (Join-Path $outDir "claude-work.ico") -Force
Write-Host "OK: $outExe"

if ($Install) {
  Write-Host "Criando atalhos (Menu Iniciar + Area de Trabalho)..."
  & $outExe --install-shortcuts
  Write-Host "Atalhos criados."
}
