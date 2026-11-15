<#
  Builds a Slate Companion release for Windows: self-contained publish + Velopack installer and update packages.

    .\build-release.ps1                 # uses <Version> from src/SlateCompanion/SlateCompanion.csproj
    .\build-release.ps1 -Version 0.2.0  # override

  Output (releases\):
    SlateCompanion-win-Setup.exe   one-click per-user installer (no admin needed): the file people download
    *.nupkg, releases.win.json, RELEASES   read by the built-in updater; upload them to the GitHub release too
  The Linux AppImage is built by .github/workflows/linux.yml.
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$proj = 'src/SlateCompanion/SlateCompanion.csproj'
if (-not $Version) { $Version = (([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1) }
$Version = "$Version".Trim()
$publishDir = "dist\publish-$Version"
Write-Host "== Slate Companion $Version" -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $publishDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

dotnet tool restore | Out-Null
if (Test-Path releases) { Remove-Item -Recurse -Force releases }
dotnet vpk pack `
  --packId SlateCompanion `
  --packVersion $Version `
  --packDir $publishDir `
  --mainExe SlateCompanion.exe `
  --packTitle "Slate Companion" `
  --packAuthors "devall96" `
  --icon src/SlateCompanion/Assets/icon.ico `
  --outputDir releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
# The portable zip isn't offered (one installer), and assets.*.json is vpk's own upload checklist.
Remove-Item releases\*-Portable.zip, releases\assets.*.json -ErrorAction SilentlyContinue
Write-Host "== Done. Installer: releases\SlateCompanion-win-Setup.exe" -ForegroundColor Green
Get-ChildItem releases | Format-Table Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
