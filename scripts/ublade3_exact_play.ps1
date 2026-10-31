param(
  [string]$Artifact = "artifacts/ublade3/exact-play.png",
  [int]$TimeoutSeconds = 60
)
$ErrorActionPreference = "Continue"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$exe = Join-Path $repo "unity/UBlade3/build/UBlade3.exe"
if (-not (Test-Path $exe)) { Write-Error "UBlade3.exe missing; run -Mode build first"; exit 1 }
$gameRoot = "D:\Games\HyperBlade\HYB"
if (-not (Test-Path (Join-Path $gameRoot "data"))) { Write-Error "HyperBlade game data missing at $gameRoot"; exit 1 }
$logDir = Join-Path $repo "artifacts/ublade3"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$tempLog = Join-Path $env:TEMP "ublade3-exact-player.log"
Remove-Item $tempLog -Force -ErrorAction SilentlyContinue
$env:HYPERBLADE_GAME_DATA = $gameRoot
$unity = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ArgumentList @("--play-exact","-logFile",$tempLog) -PassThru -WindowStyle Normal
$game = $null
for ($i = 0; $i -lt ($TimeoutSeconds * 2); $i++) {
  Start-Sleep -Milliseconds 500
  $game = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "*hyperblade*" } | Select-Object -First 1
  if ($game) { break }
}
if (-not $game) {
  Copy-Item $tempLog (Join-Path $logDir "exact-play.log") -Force -ErrorAction SilentlyContinue
  if (-not $unity.HasExited) { Stop-Process -Id $unity.Id -Force }
  Write-Error "exact game did not launch; see artifacts/ublade3/exact-play.log"
  exit 1
}
$game.Refresh()
for ($i = 0; $i -lt 20 -and $game.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $game.Refresh() }
Start-Sleep -Seconds 4
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Drawing;
public class U3ExactCap {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public struct RECT { public int Left,Top,Right,Bottom; }
}
'@ -ReferencedAssemblies System.Drawing
[U3ExactCap]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 700
$rect = New-Object U3ExactCap+RECT
[U3ExactCap]::GetWindowRect($game.MainWindowHandle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 100 -or $height -lt 100) {
  Copy-Item $tempLog (Join-Path $logDir "exact-play.log") -Force -ErrorAction SilentlyContinue
  if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force }
  if (-not $unity.HasExited) { Stop-Process -Id $unity.Id -Force }
  Write-Error "exact game window has no usable size"
  exit 1
}
$out = Join-Path $repo $Artifact
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
$bmp = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $graphics.GetHdc()
[U3ExactCap]::PrintWindow($game.MainWindowHandle, $dc, 2) | Out-Null
$graphics.ReleaseHdc($dc)
$graphics.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force }
if (-not $unity.HasExited) { Stop-Process -Id $unity.Id -Force }
Copy-Item $tempLog (Join-Path $logDir "exact-play.log") -Force -ErrorAction SilentlyContinue
Write-Output ("UBLADE3_EXACT_PLAY_OK frame=" + $Artifact + " size=" + $width + "x" + $height)
