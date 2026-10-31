# NATIVE-DISPLAY - player display/resolution options (N11)

This is the N11 display/UI slice for the native C# player: a persisted display settings file, useful
resolution presets (720p, 1080p, QHD/2K, 4K, Native) in windowed/borderless/fullscreen mode, a
usable default window, validation/fallback for unsupported modes, HUD scaling and small runtime
display controls. It is a display slice, not a parity or release claim. Nothing was committed; the
Lead owns the commit.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3.

## 1. What was wrong

The C# preview player opens at its small compiled default window (about 653x396 client, the
PlayerSettings value inherited from this project). There was no persisted display choice, no
resolution list, no HUD scaling for larger displays and no in-player display control. ProjectSettings
are deliberately untouched by this slice, so the fix is a runtime display layer that applies before
the match scene builds.

## 2. Settings, presets and defaults

New files:

| File | Contents |
| --- | --- |
| `Native/MatchDisplaySettings.cs` | `MatchDisplaySettings` (JSON model, presets, parse/load/save), `MatchDisplayPreset`, `MatchDisplayPlan` (pure validation/fallback). |
| `Native/MatchDisplayController.cs` | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` install hook, startup application, validation fallback logging, HUD scaling, the display probe and F10/F11/F12 runtime controls. |
| `docs/NATIVE-DISPLAY.md` | This document. |

The Editor file `Assets/Editor/UBlade3MatchPlay.cs` gained `CheckDisplay` (pure
settings/validation check) and `BuildWindowsDisplay` (builds the probe player). One existing Native
file also needed a minimal hook: `MatchHud.cs` got a default-off `UiScale` field and applies it to
the layout canvas, sprites and interim-font glyphs in both OnGUI and the CPU composite capture path.
At `UiScale = 1` every N5/N6 capture is byte-identical; that is verified in section 6.4.

### 2.1 File format and location

Default path: `Application.persistentDataPath/ublade3-display.json` (on the verification host:
`C:/Users/<user>/AppData/LocalLow/C7-IMI/UBlade3/ublade3-display.json`). Format:

    {
        "Version": 1,
        "Width": 1920,
        "Height": 1080,
        "Mode": "windowed",
        "Preset": "1920x1080"
    }

`Mode` is `windowed`, `borderless` (Unity `FullScreenWindow`) or `fullscreen` (exclusive).
Missing/corrupt files fall back to the default and are logged as `UBLADE3_DISPLAY_SETTINGS ...
status=not-found` / `status=invalid(...)`; the file is rewritten in canonical form after the startup
apply.

### 2.2 Presets and default

| Preset id | Label | Size |
| --- | --- | --- |
| `1280x720` | 720p | 1280x720 (default) |
| `1920x1080` | 1080p | 1920x1080 |
| `2560x1440` | QHD/2K | 2560x1440 |
| `3840x2160` | 4K UHD | 3840x2160 |
| `native` | Native desktop | `Screen.currentResolution` at apply time |

The default is windowed 1280x720, a usable replacement for the old ~653x396 window. `native`
resolves the current desktop at apply time, so it follows the user's monitor.

### 2.3 Command-line overrides

    --ublade3-display=1920x1080|native
    --ublade3-display-mode=windowed|borderless|fullscreen
    --ublade3-display-preset=1280x720|1920x1080|2560x1440|3840x2160|native
    --ublade3-display-settings=<json path>
    --display-probe=<png path>  --display-probe-delay=<frames>

CLI values override the loaded file for this run and are persisted back as the new settings.

### 2.4 Startup order and validation/fallback

`MatchDisplayController.Install` is a `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` hook, so
`Screen.SetResolution` runs before the match scene is built. The log sequence is

    UBLADE3_DISPLAY_SETTINGS path=... status=loaded/not-found/invalid settings=...
    UBLADE3_DISPLAY_REQUESTED requested=WxH preset=... mode=... desktop=WxH supported=N
    UBLADE3_DISPLAY_APPLIED requested=WxH applied=WxH mode=... fallback=... reason=... preset=...
    UBLADE3_DISPLAY_SAVED path=... status=saved
    UBLADE3_DISPLAY_ACTUAL screen=WxH currentResolution=WxH fullScreenMode=... hudScale=...
    UBLADE3_DISPLAY_FALLBACK ... (only when a fallback happened)

Validation rules (`MatchDisplayPlan.Validate`, pure and unit-checked by `CheckDisplay`):

* Native preset: request becomes the current desktop size.
* Windowed: a request larger than the desktop is clamped to the desktop
  (`reason=windowed-larger-than-desktop`).
* Borderless/fullscreen: the requested size must be exactly one of `Screen.resolutions`. If it is
  not, the applied size is the largest supported resolution that fits both the request and the
  desktop (`reason=unsupported-mode-resolution`); if none, the desktop size.
* Minimum 640x360 and invalid/zero sizes fall back defensively.
* The requested settings are persisted even when the applied plan is a fallback, so a 1440p/4K
  setting remains readable/persisted on a smaller host. The applied size and reason are logged.

## 3. HUD/UI scaling

`MatchDisplayController` computes

    HudScale = clamp(Screen.height / 720, 1, 3)   // 1 @720p, 1.5 @1080p, 2 @1440p, 3 @4K

and assigns it to the scene's `MatchHud.UiScale`. The HUD layout is computed against a
`Screen.width/HudScale x Screen.height/HudScale` canvas; sprites and glyphs are drawn/scaled by
`HudScale` in both `OnGUI` and `Composite` (the capture path). This keeps the original HUD
proportions readable at 1080p and above instead of shrinking to 1/3 of the screen at 4K. The log
line `UBLADE3_DISPLAY_HUD hudScale=...` records the applied value. At scale 1 the N5/N6 HUD and
capture bytes are unchanged (section 6.4).

## 4. Runtime display controls

In the interactive player (non-probe runs):

| Key | Action |
| --- | --- |
| F10 | cycle the five presets (1280x720 -> 1920x1080 -> 2560x1440 -> 3840x2160 -> native) |
| F11 | toggle windowed/borderless |
| F12 | reset to the default 1280x720 windowed |

Each change applies the same validation/fallback, updates `HudScale`, saves the settings file and
logs `UBLADE3_DISPLAY_CHANGED source=hotkey-Fxx ...`. The existing Escape pause path is unchanged.
The F-keys are not used by the match controls. These controls compile in the player and are wired to
the same apply/save path; the automated verification sessions below are camera-probe runs and do
not inject keystrokes, so the hotkey path is not claimed as exercised evidence.

## 5. Probe verification mechanism

The player supports `--display-probe=<png>`. In probe mode the controller builds the match scene
once (the player scene contains MatchScene/MatchGameplay/MatchPlay, but MatchPlay.Start only
auto-builds for the N6/N7 capture commands), waits `--display-probe-delay` frames, then:

1. renders the live match camera into a `RenderTexture` at the actual `Screen.width x Screen.height`,
   reads it back, composites the HUD with the current `UiScale` (the same path as the N6/N7 capture
   suites) and writes the PNG;
2. logs `UBLADE3_DISPLAY_PROBE_FRAME path=... frame=WxH ... hudScale=...` and
   `UBLADE3_DISPLAY_PROBE_OK screenshot=... screen=WxH ...`;
3. exits 0.

`ScreenCapture.CaptureScreenshot` was tried first and produced a black frame from a windowed player
launched by a script on this host; the deterministic camera render was used instead so the captured
frame is the real rendered match at the real client resolution. The actual window/client dimensions
are confirmed externally with Win32 `GetClientRect`/`GetWindowRect` from a DPI-aware PowerShell
process (`SetProcessDPIAware` is required: the first un-DPI-aware query reported 960x540 for a
1920x1080 client because the desktop is at 200% scaling).

## 6. Verification evidence

Host: Windows 11, NVIDIA T600 Laptop GPU, desktop `Screen.currentResolution=3840x2160` at 200% DPI
scaling (27 supported modes). The 1080p-display case was therefore not the physical host; 1440p/4K
were directly validated on this 4K host, and the 1080p fallback rules are covered by the pure
`CheckDisplay` test (section 6.1). All runs had no 20XX/hyperblade process.

### 6.1 Pure settings/validation check (headless)

    Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CheckDisplay -logFile <temp>/n11-checkdisplay.log

Exit 0:

    UBLADE3_DISPLAY_CHECK_COUNTS presets=5 modes=windowed,borderless,fullscreen hd=1920x1080 qhdFallback=1920x1080 uhdFallback=1920x1080 native=1920x1080 failures=0
    UBLADE3_DISPLAY_CHECK_OK

The synthetic validation uses a 1080p desktop with supported modes {1280x720, 1920x1080}: 1440p
windowed falls back to 1920x1080 (`windowed-larger-than-desktop`), 4K borderless falls back to
1920x1080 (`unsupported-mode-resolution`), native resolves 1920x1080, and 1080p windowed applies
exactly. The round-trip save/load, corrupt-file fallback and default checks also pass.

### 6.2 Built player and probe matrix

Build:

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.BuildWindowsDisplay -logFile <temp>/n11-display-build.log

    UBLADE3_DISPLAY_BUILD_OK output=.../unity/UBlade3/build-match/UBlade3NativeDisplay.exe
    (player build report bytes=104170242; PlayerSettings untouched, runtime layer only)

Each probe launched the player windowed with an initial `-screen-width 1280 -screen-height 720` (the
old small window) plus a settings file and `--display-probe=<png> --display-probe-delay=120
-logFile ...`. Win32 dimensions were read from a DPI-aware process. The PNG dimensions are parsed
from the file's IHDR.

| Case | Requested | Applied (log) | Win32 client | Win32 window | Probe PNG | HUD scale | SHA-256 prefix |
| --- | --- | --- | --- | --- | --- | --- | --- |
| default (no file/args) | 1280x720 windowed | 1280x720 | 1280x720 | 1306x791 | 1280x720 | 1 | 346e09846a9663fe |
| 720p file | 1280x720 windowed | 1280x720 | 1280x720 | 1306x791 | 1280x720 | 1 | 42a4d3cb0752faae |
| 1080p file | 1920x1080 windowed | 1920x1080 | 1920x1080 | 1946x1151 | 1920x1080 | 1.5 | 2be8ddb66757f34c |
| 1440p file | 2560x1440 windowed | 2560x1440 | 2560x1440 | 2586x1511 | 2560x1440 | 2 | 7c63486a0b1fcacc |
| 4K file | 3840x2160 borderless | 3840x2160 | 3840x2160 | 3840x2160 | 3840x2160 | 3 | 304c395c73af3475 |
| 8K request (fallback) | 7680x4320 borderless | 3840x2160 fallback=true reason=unsupported-mode-resolution | 3840x2160 | 3840x2160 | 3840x2160 | 3 | 304c395c73af3475 |
| 1080p via CLI args | 1920x1080 windowed | 1920x1080 | 1920x1080 | 1946x1151 | 1920x1080 | 1.5 | 2be8ddb66757f34c |

The 1080p settings-file and CLI-args runs produce the identical frame hash, confirming both entry
paths. Example 1080p log:

    UBLADE3_DISPLAY_SETTINGS path=... status=loaded settings=preset=1920x1080 size=1920x1080 mode=windowed
    UBLADE3_DISPLAY_REQUESTED requested=1920x1080 preset=1920x1080 mode=windowed desktop=3840x2160 supported=27
    UBLADE3_DISPLAY_APPLIED requested=1920x1080 applied=1920x1080 mode=windowed fallback=False reason= preset=1920x1080
    UBLADE3_DISPLAY_PROBE_SCENE scene=0 hudBuilt=True playBuilt=True
    UBLADE3_DISPLAY_ACTUAL screen=1920x1080 currentResolution=3840x2160 fullScreenMode=Windowed hudScale=1.5
    UBLADE3_DISPLAY_PROBE_FRAME path=...\artifacts\native-display-1080p-probe.png frame=1920x1080 bytes=248023 hudScale=1.5
    UBLADE3_DISPLAY_PROBE_OK screenshot=... screen=1920x1080 fullScreenMode=Windowed hudScale=1.5 ...

The 8K fallback run logs:

    UBLADE3_DISPLAY_APPLIED requested=7680x4320 applied=3840x2160 mode=borderless fallback=True reason=unsupported-mode-resolution preset=custom
    UBLADE3_DISPLAY_FALLBACK requested=7680x4320 applied=3840x2160 reason=unsupported-mode-resolution

and the persisted file still contains the requested 7680x4320 (the applied fallback is a runtime
result, not a rewrite of the requested settings).

Artifacts from these runs (untracked evidence, 1280x720 to 3840x2160 PNGs):

* artifacts/native-display-default-probe.png
* artifacts/native-display-720p-probe.png
* artifacts/native-display-1080p-probe.png
* artifacts/native-display-1080p-args-probe.png
* artifacts/native-display-1440p-probe.png
* artifacts/native-display-4k-probe.png
* artifacts/native-display-8k-fallback-probe.png

### 6.3 Persistence

The probed settings files are canonical JSON and are saved by the player after applying. Example
after the 1080p CLI-args run:

    {
        "Version": 1,
        "Width": 1920,
        "Height": 1080,
        "Mode": "windowed",
        "Preset": "custom"
    }

The default no-file run also created `AppData/LocalLow/C7-IMI/UBlade3/ublade3-display.json` with the
default 1280x720 windowed settings, and the log reported `UBLADE3_DISPLAY_SETTINGS ...
status=not-found` before applying the default.

### 6.4 Gameplay and regressions preserved

* `UBlade3.EditorTools.UBlade3MatchPlay.CheckN7`: exit 0, `UBLADE3_NATIVE_MATCH_N7_CHECK_OK`
  (human/AI possession, head camera, goal flow unchanged).
* N5 `UBlade3MatchGameplay.Check`: exit 0, mismatches=0, score 2-1.
* N5 seven Editor captures regenerated and byte-compared to the committed blobs: all identical
  (the `MatchHud` `UiScale` default is 1).
* N4 `UBlade3Match.Check`: exit 0, digest f54169057f5800dd unchanged.
* `python scripts/check_all.py`: PASS at the verification time (183 text files in this shared
  working tree, which also contains concurrent N10/N11 work from other teammates; the N11 display
  slice itself adds two Native sources and this document).
* No Unity/20XX process remained; the generated `build-match/` player directory was removed after
  the probes.

## 7. Honest gaps

* ProjectSettings are not modified (per scope). The display layer is runtime-only: the compiled
  player's first frame before the BeforeSceneLoad hook is still the old window, then the requested
  resolution is applied before the scene builds. The probe verifies the final client.
* The physical verification host is a 4K desktop at 200% scaling, not a 1080p host; 1440p/4K were
  validated natively, while the 1080p fallback rules are covered by the pure validation test and the
  runtime 8K fallback probe. The exact fallback rules are in section 2.4.
* Window/client dimensions are DPI-sensitive: a DPI-unaware Win32 query sees virtualized coordinates
  (960x540 for a 1920x1080 client at 200% scaling). The recorded evidence uses
  `SetProcessDPIAware`; this is documented for reproducible checks.
* HUD scaling covers the original HUD art, interim glyph text and the HUD composite path; there is no
  menu/settings UI, radar or other UI screen to scale. HUD scale is a runtime value, not yet a
  persisted setting.
* F10/F11/F12 display controls are implemented, persist settings and log, but the automated probes
  do not inject keystrokes; no menu-driven display UI was added.
* Fullscreen exclusive and multi-monitor/hotplug/DPI-change behavior were not exhaustively tested;
  the presets are validated against `Screen.resolutions` at startup.
* No 1440p/4K performance claim: the probe is a static frame from a live match; frame rate/capture
  parity was not measured.
* Not a parity or release claim; the native plugin and release path were not touched.

## 8. File list

New:

* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchDisplaySettings.cs (+ .meta)
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchDisplayController.cs (+ .meta)
* docs/NATIVE-DISPLAY.md (this file)
* artifacts/native-display-*-probe.png (7 evidence frames)

Edited:

* unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs: CheckDisplay, BuildWindowsDisplay, optional output
  path in BuildPlayer.
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHud.cs: default-off UiScale field and scaling of
  layout/sprite/glyph drawing (required by the "scale HUD/UI" part; the single existing Native file
  touched, and default 1 preserves N5/N6 capture bytes).
