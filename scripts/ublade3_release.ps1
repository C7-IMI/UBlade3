param(
  [string]$Version = "2026.10.31.001"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$build = Join-Path $repo "unity/UBlade3/build"
$exe = Join-Path $build "UBlade3.exe"
if (-not (Test-Path $exe)) { throw "UBlade3.exe missing; run scripts/ublade3_unity.ps1 -Mode build first" }
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
foreach ($file in @("PORT-PLAN.md","EXACTNESS.md","CORE-NOTES.md","DATA-FORMATS.md","SIM-SLICE.md","PORT-LEDGER.md","REA-GAME-ANALYSIS.md")) {
  $source = Join-Path $repo ("docs/" + $file)
  if (Test-Path $source) { Copy-Item $source $docs -Force }
}
$license = Join-Path $repo "third_party/hyperblade-20XX/LICENSE"
if (Test-Path $license) { Copy-Item $license (Join-Path $stage "20XX-LICENSE.txt") -Force }
$play = @"
ULTRABLADE 3 - PLAYABLE BUILD
=============================

This build is the Unity 6000.6.7f1 UBlade3 host plus the bundled exact HyperBlade
20XX runtime. The Unity Port Lab shows the exact original-data importer and the
C# simulation-core self-check; press PLAY EXACT HYPERBLADE to launch the fully
playable exact HyperBlade engine.

REQUIREMENTS
------------
- Windows 64-bit.
- Your own HyperBlade installation with a data folder (for example D:\Games\HyperBlade\HYB).
  The game data is proprietary and is not included in this package.

GAME DATA PATH
--------------
The host resolves the install in this order:
1. The HYPERBLADE_GAME_DATA environment variable.
2. A line in UBlade3_Data\StreamingAssets\game-data-path.txt (inside this build).
3. The Play-UBlade3.bat helper detects D:\Games\HyperBlade\HYB if present.

PLAY
----
Run UBlade3.exe (or Play-UBlade3.bat). In the Port Lab window press
"PLAY EXACT HYPERBLADE". The exact game opens in its own window; quit the game
to return to the UBlade3 host. The bundled 20XX runtime is LGPL-3.0 and is
staged beside your original data; no original asset is copied into this package
or the repository.

The C# port remains the editable path: see Documentation/PORT-PLAN.md,
Core-Notes, Data-Formats, Sim-Slice and Port-Ledger. Current verified data:
14/14 teams, 14/14 dromes, 209/209 simulation checks, port ledger 145 mapped
of 3,054 game functions (4.75%). The native exact runtime is the playable
engine while that renderer/plan-VM port continues.

VERSION
-------
$Version
"@
Set-Content -Path (Join-Path $stage "PLAY-README.txt") -Value $play -Encoding UTF8
$bat = @"
@echo off
setlocal
if exist "D:\Games\HyperBlade\HYB\data" set HYPERBLADE_GAME_DATA=D:\Games\HyperBlade\HYB
start "" "%~dp0UBlade3.exe"
"@
Set-Content -Path (Join-Path $stage "Play-UBlade3.bat") -Value $bat -Encoding ASCII
$zip = Join-Path $dist ("UBlade3-" + $Version + "-win64.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($zip)))).Replace("-", "").ToLowerInvariant()
$exeHash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($exe)))).Replace("-", "").ToLowerInvariant()
$sha.Dispose()
$doc = [ordered]@{
  schema = "ublade3.release/v1"
  version = $Version
  generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  unityVersion = "6000.6.7f1"
  gitHead = (git rev-parse HEAD)
  windowsExe = "unity/UBlade3/build/UBlade3.exe"
  windowsExeSha256 = $exeHash
  windowsZip = [ordered]@{ path = ("dist/UBlade3-" + $Version + "-win64.zip"); bytes = (Get-Item $zip).Length; sha256 = $hash }
  passed = $true
  note = "PC-only; exact playable path uses the bundled LGPL-3.0 20XX runtime and a user-supplied original HyperBlade data install. No original game assets are distributed."
}
$json = $doc | ConvertTo-Json -Depth 6
Set-Content -Path (Join-Path $repo "artifacts/ublade3-release.json") -Value $json -Encoding UTF8
Write-Output ("UBLADE3_RELEASE_OK version=" + $Version + " zip=" + $zip + " sha256=" + $hash + " exe_sha256=" + $exeHash)
