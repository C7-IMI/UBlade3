param(
  [string]$Version = "2026.10.31.002"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$build = Join-Path $repo "unity/UBlade3/build-native"
$exe = Join-Path $build "UBlade3Native.exe"
if (-not (Test-Path $exe)) { throw "UBlade3Native.exe missing; build the native Unity scene first" }
$dist = Join-Path $repo "dist"
$stage = Join-Path $dist ("UBlade3-" + $Version + "-win64")
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $build "*") $stage -Recurse -Force
$docs = Join-Path $stage "Documentation"
New-Item -ItemType Directory -Force -Path $docs | Out-Null
foreach ($file in @("README.md","LICENSE","THIRD-PARTY-NOTICES.md")) {
  if (Test-Path (Join-Path $repo $file)) { Copy-Item (Join-Path $repo $file) $docs -Force }
}
foreach ($file in @("PORT-PLAN.md","EXACTNESS.md","CORE-NOTES.md","DATA-FORMATS.md","ASSET-FORMATS.md","SIM-SLICE.md","PORT-LEDGER.md","REA-GAME-ANALYSIS.md","NATIVE-ENGINE-PLAN.md","PERFORMANCE-RESOLUTION-PLAN.md")) {
  $source = Join-Path $repo ("docs/" + $file)
  if (Test-Path $source) { Copy-Item $source $docs -Force }
}
$license = Join-Path $repo "third_party/hyperblade-20XX/LICENSE"
if (Test-Path $license) { Copy-Item $license (Join-Path $stage "20XX-LICENSE.txt") -Force }
$play = @"
ULTRABLADE 3 - NATIVE EXACT ENGINE
==================================

This build embeds the exact HyperBlade engine in the Unity player. There is no
hyperblade_20XX.exe, no second process and no external runtime. The original game
data is read from your HyperBlade installation; no original asset is distributed.

REQUIREMENTS
------------
- Windows 64-bit.
- Your own HyperBlade installation containing Data (for example
  D:\Games\HyperBlade\HYB). The game data is proprietary and is not in this package.

GAME DATA PATH
--------------
The native engine resolves the install in this order:
1. HYPERBLADE_GAME_DATA.
2. A line in UBlade3Native_Data\StreamingAssets\game-data-path.txt.
3. Play-UBlade3.bat detects D:\Games\HyperBlade\HYB.

The exact engine assets needed to run (20XX_data.pak) are included under
UBlade3Native_Data\StreamingAssets\20xx. Original models, textures, plans and
audio stay in your HyperBlade installation.

PLAY
----
Run UBlade3Native.exe (or Play-UBlade3.bat). Click PLAYERS ONLY, then TO ARENA,
then click to continue; the native exact match starts. Mouse and keyboard are
passed to the embedded engine. The match HUD, drome, skaters, rok and gameplay
are the original engine's own; the Unity player is the host.

This is a native build of the exact engine, not a second executable. The Unity
C# data/simulation and asset-reader tracks continue in the repository for the
fully native C# engine work; this build is the exact playable runtime.

VERSION
-------
$Version
"@
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $stage "PLAY-README.txt"), $play, $utf8)
$bat = @"
@echo off
setlocal
if exist "D:\Games\HyperBlade\HYB\Data" set HYPERBLADE_GAME_DATA=D:\Games\HyperBlade\HYB
start "" "%~dp0UBlade3Native.exe"
"@
$ascii = New-Object System.Text.ASCIIEncoding
[System.IO.File]::WriteAllText((Join-Path $stage "Play-UBlade3.bat"), $bat, $ascii)
Get-ChildItem $stage -Recurse -Filter 'hyperblade_20XX.exe' | ForEach-Object { throw ("external runtime must not be packaged: " + $_.FullName) }
$zip = Join-Path $dist ("UBlade3-" + $Version + "-win64.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($zip)))).Replace("-", "").ToLowerInvariant()
$exeHash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($exe)))).Replace("-", "").ToLowerInvariant()
$sha.Dispose()
$doc = [ordered]@{
  schema = "ublade3.release/v2"
  version = $Version
  generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  unityVersion = "6000.6.7f1"
  gitHead = (git rev-parse HEAD)
  nativeEngine = $true
  externalProcess = $false
  windowsExe = "unity/UBlade3/build-native/UBlade3Native.exe"
  windowsExeSha256 = $exeHash
  windowsZip = [ordered]@{ path = ("dist/UBlade3-" + $Version + "-win64.zip"); bytes = (Get-Item $zip).Length; sha256 = $hash }
  passed = $true
  note = "PC-only. Exact HyperBlade engine runs as the ublade3_native plugin inside the Unity player; no external runtime. User-supplied original data required; no original assets distributed."
}
$json = $doc | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText((Join-Path $repo "artifacts/ublade3-release.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("UBLADE3_NATIVE_RELEASE_OK version=" + $Version + " zip=" + $zip + " sha256=" + $hash + " exe_sha256=" + $exeHash + " external=false")
