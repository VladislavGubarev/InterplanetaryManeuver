param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $root 'installer\build.ps1'
$publishDir = Join-Path $root 'dist\publish'
$brandingDir = Join-Path $root 'assets\branding'
$wxs = Join-Path $PSScriptRoot 'InterplanetaryManeuver.wxs'
$outDir = Join-Path $root 'dist\installer'
$outMsi = Join-Path $outDir 'InterplanetaryManeuver_0.1.0_x64.msi'
$outPdb = Join-Path $outDir 'InterplanetaryManeuver_0.1.0_x64.wixpdb'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue $outPdb

# Publish + generate branding assets (does not require Inno Setup).
& powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -SkipInno

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
  Write-Host "WiX Toolset (wix) not found. Installing dotnet global tool..."
  dotnet tool install --global wix
  $wix = Get-Command wix -ErrorAction Stop
}

$publishDirFull = (Resolve-Path $publishDir).Path
$brandingDirFull = (Resolve-Path $brandingDir).Path

Write-Host "Building MSI..."
Write-Host "PublishDir: $publishDirFull"
Write-Host "BrandingDir: $brandingDirFull"
Write-Host "Output: $outMsi"

wix build `
  -arch x64 `
  -d "PublishDir=$publishDirFull" `
  -d "BrandingDir=$brandingDirFull" `
  -pdbtype none `
  -out "$outMsi" `
  "$wxs" | Out-Host

if ($LASTEXITCODE -ne 0) {
  throw "wix build failed with exit code $LASTEXITCODE"
}

Write-Host "MSI ready: $outMsi"
