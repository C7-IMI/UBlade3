# UBlade3 performance, 60 fps, animation and resolution plan

Status: implementation plan, 2026-10-31. No runtime behavior was changed by this document.

## 1. Scope and current baseline

UBlade3 currently ships two runtime paths:

1. **Playable exact path** - the Unity host launches the bundled HyperBlade 20XX runtime, which runs the exact
   HyperBlade simulation and renderer (LGPL-3.0) using the user's original data. This is what the release plays.
2. **C# exact port** - the editable Unity data/simulation track. Its data layer and 209-check core do not yet
   render a full match; see `docs/PORT-LEDGER.md`.

The performance work must land in the **exact runtime** first because it is the playable engine; the C# port then
implements the same `DisplaySettings` and frame-pacing contracts.

### What the source actually does

- The simulation is **30 Hz** (`FramePacing::kTickMs`, tick scheduling in `src/engine/framepacing.cpp`).
- Matches draw **uncapped by default** (`GraphicsSettings::Switch::UNCAPPED` defaults true). The match branch calls
  `Game::Draw(FramePacing::RunTicks())` with no frame sleep (`src/app/gameapp.cpp:785-809`).
- Outside a match the old `FrameTimer::Pace()` path is still **hard-capped at 30**
  (`src/app/frametimer.cpp:120-147`).
- Render frames blend bone world matrices and the camera between the last two 30 Hz ticks
  (`src/engine/framepacing.cpp`, `Blend`, `PlaceObjects`, `PlaceCamera`, `DrawViews`).
- The camera blend currently **lerps the whole projection matrix**, including its rotation and view translation
  (`PlaceCamera`); the source itself notes this can squash a fast pan and should be decomposed.
- Character clips are sampled at **integer animation frames** (`AnimPlayer::UpdateFrame`,
  `src/anim/animplayer.cpp:283-440`). The engine adds no sub-tick animation key beyond the tick snapshots.
- The 3D back buffer renders at the window client size, clamped to **width 320..4096 (multiple of 4)** and
  **height 200..2160** (`Window::OnSize`, `src/app/window.cpp:114-155`).
- The window starts at 1920x1080 (`Window::ApplyFullscreenStyle`, `src/app/window.cpp:38-59`); there is no
  resolution selector in the shipped UI. `ScreenSize` in BOOIE.INI is a gameplay/view setting, not the display
  resolution.

This means 60 fps rendering is already technically possible, but it is not a **stable, selectable, measured** mode,
the camera interpolation is not jitter-safe, and animation/camera smoothness at 60+ has not been the target.

## 2. Goals and non-goals

### Goals

- 1080p60 as the default on capable PCs, with **30 / 60 / 120 / uncapped** render-rate selection.
- Stable frame pacing: 60.0 fps target, 16.7 ms budget, no periodic 33 ms hitches; optional VSync and a
  latency-favoring no-VSync mode.
- Remove camera judder at 60+ without changing the exact 30 Hz simulation.
- Smooth skater/goalie animation at 60+ using interpolation (runtime first; optional baked 60 Hz clips).
- Resolution presets for **1920x1080 (1080p)**, **2560x1440 (2K/QHD)**, **3840x2160 (4K UHD)**, Native and
  Windowed/Borderless/Exclusive fullscreen, with render-scale for 4K performance.
- Persist display and performance settings and expose them from both the UBlade3 launcher and the exact runtime's
  settings UI.
- Keep the original 30 Hz/uncapped behavior available as a regression mode.

### Non-goals

- Raising the **simulation** tick above 30 Hz. The exact clone keeps the original physics/rules tick; 60 fps is a
  render interpolation feature, not a different game simulation.
- Re-authoring character meshes. Models are skinned meshes; 'animation frames' live in the WBA clips, not the mesh.
- Shipping original assets in the repository or release. Game data stays user-supplied.
- 4K on the software renderer. 4K is hardware renderer with a measured performance floor.

## 3. Frame-rate and pacing design

### 3.1 DisplaySettings (single source of truth)

Add a persisted model with schema `ublade3.display/v1`:

| Field | Values | Default |
| --- | --- | --- |
| `resolution` | `1920x1080`, `2560x1440`, `3840x2160`, `native`, `custom WxH` | `1920x1080` |
| `windowMode` | `windowed`, `borderless`, `exclusive` | `borderless` |
| `refreshHz` | monitor modes; `auto` | `auto` |
| `renderScale` | 0.50, 0.67, 1.00 (upscaled to output) | `1.00` |
| `frameCap` | `30`, `60`, `120`, `uncapped` | `60` |
| `vsync` | `off`, `on`, `adaptive` (where available) | `on` |
| `smoothCamera` | `off`, `interpolate`, `interpolate+smooth` | `interpolate+smooth` |
| `smoothAnimation` | `off`, `sub-tick` | `sub-tick` |
| `originalMode` | bool (forces 30 Hz/uncapped-off/original caps) | `false` |

Storage: `[Display]` and `[Performance]` sections in the 20XX `booie.ini` (next to the user's data), with a UBlade3
copy under `%LOCALAPPDATA%\UBlade3\display.json` so the host can show/set options before the exact runtime starts.

### 3.2 Pacing changes in 20XX

- Replace the hard 30 Hz `FrameTimer::Pace()` with a target interval driven by `frameCap` and `refreshHz`,
  using the existing precise-wait path; never busy-spin for the last millisecond.
- Match loop: keep `FramePacing::RunTicks()` (30 Hz accumulator) and add a render cap after `Game::Draw`.
  Uncapped stays available; `60` and `120` become first-class targets.
- Use a **monotonic accumulator**: render alpha = accumulator / tick, clamped to `[0,1]`, with a 1-tick max
  carry and no alpha reset from a single hitch; carry error across frames; resync only after >250 ms.
- VSync through the presenter; when VSync is on, let VSync own presentation and use the cap only as a safety.
- Add instrumentation: present interval, fps, 1% low, tick drift, alpha min/max, render/game CPU split; write a
  short telemetry JSON for the performance gate.

### 3.3 Frame budget

- 1080p60: CPU logic ≤ 6 ms, render ≤ 10 ms, total under 16.7 ms on the reference T600 laptop.
- 1440p60: ≤ 16.7 ms; 4K60: ≤ 16.7 ms only with hardware renderer and render scale/quality fallback; otherwise
  4K30 is the documented fallback.
- Measure with PresentMon/ETW plus the engine counters. Acceptance: average 60.0 ± 0.5, 1% low ≥ 55, no repeated
  33 ms spikes.

## 4. Camera jitter plan

### Root causes to verify

1. Camera pose is sampled at 30 Hz and the render blends a **projection matrix** rather than a camera pose;
   rotation interpolation through a matrix is not a rigid-body interpolation and can visibly swim/pop.
2. `PlaceCamera` has no rotational smoothing and no history beyond one tick; a fast aim/collision trim at a tick
   boundary shows a step.
3. `FramePacing::RunTicks()` resets owed time after a gap and clamps it; a late frame changes alpha abruptly.
4. The exact runtime window is currently uncapped; on a compositor/DWM desktop, variable frame times look like
   judder even when average fps is high.
5. Camera culling/projection uses the interpolated matrix but the underlying cull views may be captured from the
   tick pose; moving the camera without moving cull state can pop objects at 60+.

### Implementation

- Replace matrix lerp with a **pose lerp**:
  - decompose camera eye, look target, up, FOV/near/far at tick snapshots;
  - interpolate position linearly, orientation with quaternion slerp (shortest arc), FOV/near/far linearly;
  - rebuild the projection/view matrices at render time.
- Add an optional **critically damped** camera follow at the render rate, applied after tick interpolation, with
  a deadband and maximum velocity so the exact 30 Hz feel is preserved; disabled in `originalMode`.
- Keep a 2-3 tick camera history; use **extrapolate only when a tick is late**, never on a teleport/camera cut
  (existing snap distance can remain the cut detector).
- Interpolate the cull frustum/camera basis from the same pose used to draw; do not use the old tick cull matrix.
- Fix alpha: monotonic accumulator; no reset for single-frame stalls; smooth alpha over a short window when
  `smoothCamera` is on; test with a 120 Hz display.
- Platform: choose borderless fullscreen with VSync by default to avoid Windows fullscreen optimization/DWM
  judder; exclusive mode only if the user selects it. Add a short in-game camera test (fixed pan + strafe) that
  writes per-frame eye/quaternion deltas for the jitter gate.

### Acceptance

- Scripted 10 s camera pan/strafe at 60 fps: no frame-to-frame rotation step > 0.5° at normal chase speed, and
  99th percentile angular delta variance below the 30 Hz baseline; visual A/B video at 60 fps.
- No camera pop when the camera transitions between tick-interpolated and smoothed modes (one-tick blend).
- Original 30 Hz mode produces the same camera path (within tolerance) as the source.

## 5. Animation smoothness and 'extra frames'

### What to add (and what not to)

Models are skinned meshes. They do not need more model frames; **animation clips** need more sample points or
render-time interpolation. Two layers:

1. **Render-time sub-tick interpolation (preferred, exact-data safe).**
   - Expose the fractional animation position from `AnimPlayer` (`m_state.flPosition`) and sample the clip's
     keyframes at `frame + alpha` instead of snapping to an integer frame.
   - Interpolate bone local transforms with slerp; then let the existing `FramePacing` bone snapshot blend handle
     the tick boundary. This produces 60/120 Hz poses from the original data without changing the simulation
     tick or the clip format.
   - Disable in `originalMode` to preserve the 30 Hz stepping for exactness comparisons.
2. **Optional 60 Hz baked clips (only if runtime interpolation is visually insufficient).**
   - Batch export WBA/WBP animation clips, resample keyframes to 60 Hz with a documented linear/spline rule in
     Blender or a dedicated converter, rebake to the engine's animation format, and keep the original files.
   - This is an asset pipeline change, not a model change. It increases asset size and risks drift from the
     original; it is a fallback, not the first fix.
   - Any baked clips must be generated from the user's original data locally; no extracted animation data is
     committed.

### Additional animation fixes

- Separate **animation update rate** from simulation update; evaluate at render time with the same alpha as the
  camera, so skater feet and camera stay in sync.
- Blend clips at render time across state transitions; ensure hit/event frames still trigger on the 30 Hz tick
  (events must not be duplicated by sub-tick sampling).
- Add an animation test: sample a known clip at 30 and 60 render rates; compare bone positions at ticks, verify
  no foot sliding, no popping at loop/transition points, and event-frame parity.
- Performance: interpolation cost is per-bone and small compared with 4K fill; measure on 8 skaters + goalies;
  keep a no-interpolation fallback for low-end targets.

## 6. Resolution and display options

### Presets

| Label | Output | Notes |
| --- | --- | --- |
| 1080p | 1920x1080 | default; hardware and software; 60 fps target |
| 2K/QHD | 2560x1440 | hardware recommended; software fallback may use 1080p render scale |
| 4K UHD | 3840x2160 | hardware only; render scale 0.50-1.00; 30/60 target with measured budget |
| Native | monitor preferred mode | use the monitor's current mode; no forced scaling |
| Custom | WxH | width multiple of 4; height 200..2160; validate aspect |

'2K' is defined as **2560x1440** here; DCI 2048x1080 can be added as a separate advanced preset if required.

### Implementation

- 20XX: add `[Display]` settings (resolution, window mode, refresh, render scale, vsync) and a Display tab in the
  existing debug menu. Apply through `SDL_SetWindowSize`, `SDL_SetWindowFullscreen`/borderless mode, and a new
  render-scale path in `Window::OnSize`/presenter. Keep the current back-buffer clamps and width multiple-of-4 rule.
- The HUD is designed at 640x480 and scales; verify text/health/radar readability at 4K and 16:9. Add a UI scale
  option because the original HUD is proportionally tiny at 4K.
- UBlade3 host: add a **Display** panel to the Port Lab before launch, with the same presets. `ExactPlayBridge`
  writes the selected values to the staged runtime config and/or passes `-width/-height/-display-mode/-fps/-vsync`
  arguments to 20XX. If the exact runtime cannot accept a setting yet, the host resizes the created SDL window
  with Win32 as a compatibility fallback and labels it 'host-applied'.
- Persist per-monitor choices; on startup validate the selected mode against the current monitor, fall back to
  1080p and report the fallback.
- Refresh-rate selection: use the monitor's supported modes; never force a refresh rate the display does not
  advertise. VSync default on in borderless, off/adaptive in exclusive.
- Capture/gate: each preset must start, report the actual back-buffer dimensions, render the HUD, and produce a
  1280x720 evidence thumbnail plus the native-dimension frame.

### 4K constraints

- Hardware renderer only for 4K by default. The software renderer path is CPU-bound and should cap at 1080p/1440p
  (or lower render scale) with a clear message, not silently drop frames.
- T600 4 GB VRAM: 4K textures/shadows are not assumed; render scale and shadow settings must be measured.
- Keep the original flat-shaded look; do not upscale the 1996 art by default. Nearest/linear and UI scaling are
  explicit settings.

## 7. Settings UX

- UBlade3 Port Lab: Display panel (preset, window mode, refresh, render scale, frame cap, VSync, smoothing
  toggles), plus current FPS/frame time/tick-rate status while the exact runtime is running (read from its
  telemetry file).
- Exact runtime: Debug menu (F2) Display + Timing tabs, showing actual render size, refresh, cap, ticks/s, 1% low
  and jitter counters. A pause-menu graphics page is a follow-up if the debug menu is not sufficient.
- Reset to defaults and an 'Original mode' button (30 Hz, uncapped off, no smoothing, 1080p).

## 8. Verification plan

| Gate | Method | Pass criterion |
| --- | --- | --- |
| 60 fps pacing | 10-minute match + PresentMon/ETW + engine telemetry | avg 60 ± 0.5, 1% low ≥ 55, no 33 ms spike train |
| 120/uncapped | same at 120 Hz/uncapped | no unbounded catch-up; ticks/s = 30 ± 1; drift near 0 |
| Camera jitter | scripted pan/strafe, 60 fps, per-frame pose trace + visual A/B | no step >0.5°, smoothness metric improves vs baseline |
| Animation | frame-by-frame 30 vs 60; bone/event parity | no foot slide/pop; events trigger once on tick |
| 1080p/1440p/4K | launch each preset; capture native + 720p thumbnail | dimensions/aspect/HUD correct; 4K hardware-only or documented fallback |
| Original mode | regression run | matches current 20XX 30 Hz behavior; no setting leaks |
| Host path | launch exact runtime from extracted release with each preset | exact game starts; settings persist; capture evidence |
| Port C# | same display contract tested in Port Lab | settings model round-trips; no Unity-only fake claims |

Record per-run evidence under `artifacts/performance/`: `display.json`, `fps.csv`, screenshots, PresentMon/ETW
captures, and a short 60 fps clip where useful. Never claim a frame rate without the measured file.

## 9. Delivery phases and effort

| Phase | Work | Effort |
| --- | --- | --- |
| P0 | Install/verify source-build toolchain for 20XX (VS2022 Build Tools + CMake/Ninja), baseline PresentMon/ETW, add telemetry JSON | 1-2 days |
| P1 | DisplaySettings model + config + 30/60/120/uncapped pacing + VSync/borderless | 2-3 days |
| P2 | Pose-based camera interpolation, smoothing, cull interpolation, jitter gate | 2-3 days |
| P3 | Sub-tick animation sampling; optional 60 Hz clip bake pipeline and A/B | 3-5 days |
| P4 | 1080p/1440p/4K presets, render scale, host Display panel, persistence/fallback | 2-3 days |
| P5 | Performance matrix, release, docs/records, regression | 2-3 days |

Schedule assumes the exact 20XX source can be built locally; the current release uses the prebuilt runtime. If the
toolchain cannot be installed, P1-P4 land as a UBlade3 host-side compatibility layer first (window resize, cap
wrapper, launch args) with the source changes tracked as blockers, not claimed.

## 10. Risks and mitigations

- **30 Hz simulation / 60 Hz render mismatch:** keep the tick authoritative; render interpolation only. Event
  timing and collision stay on the tick; tests assert parity.
- **Camera matrix/slerp artifacts:** decompose to pose; fallback to no interpolation in original mode; visual A/B.
- **Animation interpolation reveals low-key clips:** first try sub-tick interpolation; only bake clips if the source
  lacks keys. Baking changes assets; label and keep originals.
- **4K performance:** hardware-only 4K, render scale and quality fallback, measured budgets; no fake 4K from a
  1080p upscale unless labelled.
- **Compositor judder:** borderless/VSync default; test exclusive; document per-OS behavior.
- **Source-build blocker:** current prebuilt 20XX means no local compiler is installed; P0 verifies the toolchain
  before claiming source-level changes. Host-only work can proceed independently.
- **Exactness:** all smoothing/interpolation off in original mode; compare camera/event/animation parity against
  the unmodified 20XX reference.

## 11. Definition of done

- 1080p60 is the default on capable hardware; 60/120/uncapped and original 30 modes are selectable and measured.
- Camera jitter gate passes; skater/goalie animation is smooth at 60+ without simulation changes.
- All three requested resolutions (1080p, 2K/QHD, 4K UHD) start, display correctly, persist and have evidence.
- UBlade3 host exposes the same settings and launches the exact runtime with them.
- No original assets are committed, no source data is silently modified, and the C# port contract is updated
  with the same display settings.

### Appendix: files expected to change

- `third_party/hyperblade-20XX/src/app/frametimer.cpp`, `src/app/gameapp.cpp`, `src/engine/framepacing.cpp`,
  `src/engine/camera*.cpp`, `src/anim/animplayer.*`, `src/app/window.cpp`, `src/graphics/graphicssettings.*`,
  `src/ui/debugmenu.cpp`, `src/graphics/cameraprojectionstate.*`.
- UBlade3 `unity/UBlade3/Assets/Scripts/UBlade3/App/ExactPlayBridge.cs`, Port Lab UI, host display settings.
- `scripts/ublade3_exact_play.ps1`, new performance capture scripts, docs/records.
- The 20XX submodule is read-only in this repo; implementation should use a tracked patch/fork policy and a
  rebuilt runtime, not untracked edits inside `third_party/`.
