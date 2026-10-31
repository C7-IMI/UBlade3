param(
  [ValidateSet("build","capture","release")]
  [string]$Mode = "build",
  [string]$GameData = $env:HYPERBLADE_GAME_DATA
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
$build = Join-Path $repo "unity/UBlade3/build-native"
$exe = Join-Path $build "UBlade3Native.exe"
$log = Join-Path $env:TEMP "ublade3-native-unity.log"
if ($Mode -eq "build") {
  if (-not (Test-Path $unity)) { throw "Unity 6000.6.7f1 not found" }
  & $unity -batchmode -quit -nographics -projectPath "unity/UBlade3" -executeMethod UBlade3.EditorTools.UBlade3Build.BuildNativeWindows -logFile $log 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Unity native build failed; see $log" }
  if (-not (Test-Path $exe)) { throw "native build did not produce UBlade3Native.exe" }
  Write-Output "UBLADE3_NATIVE_BUILD_OK exe=$exe"
  exit 0
}
if ($Mode -eq "capture") {
  if (-not (Test-Path $exe)) { throw "native build missing; run -Mode build" }
  if ([string]::IsNullOrWhiteSpace($GameData)) { $GameData = "D:\Games\HyperBlade\HYB" }
  $env:HYPERBLADE_GAME_DATA = $GameData
  $shot = Join-Path $repo "artifacts/native-release-match.png"
  $playerLog = Join-Path $env:TEMP "ublade3-native-player.log"
  Remove-Item $shot,$playerLog -Force -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath $exe -WorkingDirectory $build -ArgumentList @("--native-capture=$shot","--native-autostart","-screen-width","1280","-screen-height","720","-screen-fullscreen","0","-logFile",$playerLog) -PassThru -WindowStyle Normal
  for ($i = 0; $i -lt 60 -and -not (Test-Path $shot) -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 500 }
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  if (-not (Test-Path $shot)) { throw "native match capture missing" }
  Write-Output "UBLADE3_NATIVE_CAPTURE_OK frame=artifacts/native-release-match.png"
  exit 0
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts/ublade3_release.ps1")
exit $LASTEXITCODE
