param(
  [ValidateSet("inspect","build","capture")]
  [string]$Mode = "build",
  [string]$Capture = "artifacts/port-lab.png"
)
$ErrorActionPreference = "Continue"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
$project = Join-Path $repo "unity/UBlade3"
$logDir = Join-Path $repo "artifacts/ublade3"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "unity.log"
Remove-Item (Join-Path $project "Temp/UnityLockfile") -Force -ErrorAction SilentlyContinue
if ($Mode -eq "inspect" -or $Mode -eq "build") {
  $method = if ($Mode -eq "build") { "UBlade3.EditorTools.UBlade3Build.BuildWindows" } else { "UBlade3.EditorTools.UBlade3Build.Inspect" }
  & $unity -batchmode -quit -nographics -projectPath $project -executeMethod $method -logFile $log 2>&1 | Select-Object -Last 8
  if (-not (Select-String -Path $log -Pattern ("UBLADE3_" + $Mode.ToUpperInvariant() + "_OK") -Quiet)) { Write-Error "Unity $Mode marker missing"; exit 1 }
  Write-Output "UBLADE3_GAME_$($Mode.ToUpperInvariant())_OK log=artifacts/ublade3/unity.log"
  exit 0
}
$exe = Join-Path $repo "unity/UBlade3/build/UBlade3.exe"
if (-not (Test-Path $exe)) { Write-Error "UBlade3.exe missing; run -Mode build first"; exit 1 }
$out = Join-Path $repo $Capture
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
$playerLog = Join-Path $logDir "player.log"
$tempShot = Join-Path $env:TEMP "ublade3-port-lab.png"
$tempLog = Join-Path $env:TEMP "ublade3-port-lab.log"
Remove-Item $tempShot,$tempLog -Force -ErrorAction SilentlyContinue
$gameData = "D:\Games\HyperBlade\HYB"
$env:HYPERBLADE_GAME_DATA = $gameData
$arguments = @("--port-lab-capture=$tempShot","-screen-fullscreen","0","-screen-width","1280","-screen-height","720","-logFile",$tempLog)
$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ArgumentList $arguments -PassThru -WindowStyle "Normal"
for ($i = 0; $i -lt 120; $i++) { Start-Sleep -Milliseconds 500; if ($process.HasExited) { break } }
if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; Write-Error "player did not exit"; exit 1 }
if (-not (Test-Path $tempShot)) { Write-Error "capture missing"; exit 1 }
if (-not (Test-Path $tempLog)) { Write-Error "player log missing"; exit 1 }
if (-not (Select-String -Path $tempLog -Pattern "UBLADE3_CAPTURE_OK" -Quiet)) { Write-Error "capture marker missing"; exit 1 }
Copy-Item $tempShot $out -Force
Copy-Item $tempLog $playerLog -Force
Copy-Item $tempLog (Join-Path $logDir "capture-player.log") -Force
Write-Output "UBLADE3_GAME_CAPTURE_OK frame=$Capture log=artifacts/ublade3/unity.log"
