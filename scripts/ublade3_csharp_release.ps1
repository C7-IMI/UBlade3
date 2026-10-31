param(
  [string]$Version = "2026.10.31.005-csharp-preview",
  [switch]$ForceBuild
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$build = Join-Path $repo "unity/UBlade3/build-match"
$exe = Join-Path $build "UBlade3NativeMatchPlay.exe"
if ($ForceBuild -or -not (Test-Path $exe)) {
  $unity = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
  if (-not (Test-Path $unity)) { throw "Unity 6000.6.7f1 not found" }
  $log = Join-Path $env:TEMP "ublade3-csharp-release-build.log"
  & $unity -batchmode -quit -nographics -projectPath "unity/UBlade3" -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.BuildWindows -logFile $log 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "native C# player build failed; see $log" }
}
$dist = Join-Path $repo "dist"
$stage = Join-Path $dist ("UBlade3-CSharpPreview-" + $Version + "-win64")
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $build "*") $stage -Recurse -Force
$docs = Join-Path $stage "Documentation"
New-Item -ItemType Directory -Force -Path $docs | Out-Null
foreach ($file in @("README.md","LICENSE","THIRD-PARTY-NOTICES.md")) {
  if (Test-Path (Join-Path $repo $file)) { Copy-Item (Join-Path $repo $file) $docs -Force }
}
foreach ($file in @("NATIVE-MATCH.md","NATIVE-MATCH-N7.md","NATIVE-MATCH-N8.md","NATIVE-MATCH-N9.md","NATIVE-MATCH-N10.md","NATIVE-PLAY.md","NATIVE-GAMEPLAY.md","NATIVE-ENGINE-PLAN.md","ASSET-FORMATS.md","NATIVE-AUDIO.md","PLAN-VM.md","PLAN-AI.md")) {
  $source = Join-Path $repo ("docs/" + $file)
  if (Test-Path $source) { Copy-Item $source $docs -Force }
}
$play = @"
ULTRABLADE 3 - NATIVE C# ENGINE PREVIEW
=======================================

This is the native C# match engine: original HyperBlade drome, skaters and ROK
from your own game install, the C# 30 Hz simulation, PlanVM-driven AI on every
non-human slot, default rule scoring, HUD art and original-wave audio. There is
no hyperblade_20XX.exe and no external runtime.

STATUS: playable preview, not final parity
------------------------------------------
Working: human pickup/carry/charge/shoot/pass, real goals and score, face-off/
restart, head camera, PlanVM default AI (Defense/CarryRok/Goalie), plan passes
and plan shots, HUD art, audio playback.
Still incomplete: many plan handler bodies, exact steering/collision physics,
retail celebration/goalie plans, some HUD text/details, pixel-parity.

REQUIREMENTS
------------
Windows 64-bit and your own HyperBlade install (proprietary data not included).

GAME DATA PATH
--------------
Set HYPERBLADE_GAME_DATA to the install folder that contains Data\, for example
D:\Games\HyperBlade\HYB. Play-UBlade3-CSharp.bat sets that default if present.

PLAY
----
Run UBlade3NativeMatchPlay.exe --play-autostart, or Play-UBlade3-CSharp.bat.
Controls: Up/Down accelerate/brake, Left/Right steer, D/S strafe,
Space hold/release shoot, C pass, Escape pause.

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
start "" "%~dp0UBlade3NativeMatchPlay.exe" --play-autostart
"@
$ascii = New-Object System.Text.ASCIIEncoding
[System.IO.File]::WriteAllText((Join-Path $stage "Play-UBlade3-CSharp.bat"), $bat, $ascii)
Get-ChildItem $stage -Recurse -Filter 'hyperblade_20XX.exe' | ForEach-Object { throw "external runtime must not be packaged" }
$zip = Join-Path $dist ("UBlade3-CSharpPreview-" + $Version + "-win64.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($zip)))).Replace("-", "").ToLowerInvariant()
$exeHash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($exe)))).Replace("-", "").ToLowerInvariant()
$sha.Dispose()
$doc = [ordered]@{
  schema = "ublade3.release/v3"
  version = $Version
  generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  unityVersion = "6000.6.7f1"
  gitHead = (git rev-parse HEAD)
  nativeCSharp = $true
  externalProcess = $false
  preview = $true
  windowsExe = "unity/UBlade3/build-match/UBlade3NativeMatchPlay.exe"
  windowsExeSha256 = $exeHash
  windowsZip = [ordered]@{ path = ("dist/UBlade3-CSharpPreview-" + $Version + "-win64.zip"); bytes = (Get-Item $zip).Length; sha256 = $hash }
  passed = $true
  note = "Native C# playable preview: original assets from the user install, no external runtime. PlanVM AI default on non-human slots; parity gaps remain."
}
$json = $doc | ConvertTo-Json -Depth 6
$jsonUtf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $repo "artifacts/ublade3-csharp-release.json"), $json, $jsonUtf8)
Write-Output ("UBLADE3_CSHARP_RELEASE_OK version=" + $Version + " zip=" + $zip + " sha256=" + $hash + " exe_sha256=" + $exeHash + " external=false")
