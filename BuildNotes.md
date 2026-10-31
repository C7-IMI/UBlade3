# Build notes

Current version: 2026.10.27.002

UBlade3 uses a script-computed version sequence. Versions 001+ are this port; the 1996 game version is unrelated.

## 2026.10.31.004

**Add the 60 fps, camera/animation smoothness and 1080p/2K/4K resolution plan.**

- Date: 2026-10-31
- Change: wrote `docs/PERFORMANCE-RESOLUTION-PLAN.md`, grounded in the 20XX source: current 30 Hz simulation,
  uncapped render default, hard-30 `FrameTimer::Pace` outside matches, whole-matrix camera interpolation,
  integer animation frame sampling, 1920x1080 start window, 4096x2160 back-buffer clamps and the existing Uncapped
  switch. The plan defines a display-settings contract (1920x1080, 2560x1440, 3840x2160, native/custom; windowed,
  borderless, exclusive; 30/60/120/uncapped; VSync; render scale), pose-based camera interpolation with smoothing,
  sub-tick animation sampling (with optional 60 Hz baked clips), settings persistence/UX, acceptance gates for
  frame pacing/camera jitter/animation/resolutions, effort, risks and a definition of done.
- No runtime behavior changed. The playable release remains 2026.10.31.001; this is a plan only.
- Verification: plan reviewed against source citations; docs link added; no performance or resolution claims made.
- Not done yet: source-build toolchain for 20XX, implementation phases P0-P5, measured 60 fps/camera/4K evidence.
- Note: 'extra animation frames' are animation-clip sample points, not skinned-mesh geometry; sub-tick
  interpolation is the preferred path, with baked 60 Hz clips as an optional asset-pipeline fallback.

## 2026.10.31.003

**Integrate the updated Gypsy P1 surfaces and ship a fully playable UBlade3 release: Unity host + exact 20XX runtime,
with the C# exact port continuing underneath.**

- Date: 2026-10-31
- Change: ran Gypsy's new project init and adopted its updated P1 surfaces (toolpacks/slices, action cache/parallel
  scheduler through the runner, asset registry, metrics, traces, signed provenance/SBOM, project check). Added
  `project.gypsy.json`, `scripts/gypsy_u3.py`, the `ublade3.unity` toolpack, the `ublade3.playable` slice and four
  UBlade3 capabilities. Added the bundled LGPL-3.0 HyperBlade 20XX runtime and a Unity `ExactPlayBridge` that stages
  it beside the user's original data, launches the fully playable exact game, and returns to the Unity host when it
  quits; the Port Lab exposes a PLAY button. Updated docs, records, release packaging and evidence.
- Fully playable status: the release launches the exact HyperBlade engine from the Unity host; verified by extracting
  the release and starting a game window from it (973x576) and by the Gypsy exact-play action.
- C# port status: the exact data layer (14 teams/14 dromes, 0 warnings) and simulation core (209/209 checks) remain
  verified; the port ledger is 145 mapped / 20 stubs / 3,976 unmapped = 4.75% of game code. The C# renderer/plan-VM
  port is the editable fidelity track and is not claimed complete; the exact 20XX runtime is the playable engine.
- Gypsy verification: `check` PASS (capabilities 50, tools 16, project UBlade3); toolpack `passed=True`;
  slice `passed=True`; assets `files=3123`; metrics `runs=3`; traces `traces=4`; provenance
  `passed=True` and SBOM 8 components.
- Release: `UBLADE3_RELEASE_OK version=2026.10.31.001`; ZIP 38,514,377 bytes SHA-256
  `fabbad4b1be3afa1e19b8e022cff14139b245cbea4972d00ff6d4598e0b4a9fe`; EXE SHA-256
  `9806abde4a406ed68647e9505852c49f6aa72a372f4f084e2aa2821affea57a2`.
- Honest note: the exact native engine is the playable runtime; a 100% C#/Unity renderer/plan-VM clone is still
  incomplete. Original game data is never committed.

## 2026.10.27.002

**Land the exact original-data layer and the exact core-simulation slice.**

- Date: 2026-10-27
- Change: the original-data importer now covers 14/14 teams, 14/14 dromes, 140 team objects, 464 drome objects and
  every shipped config/catalog/list (0 warnings); the core simulation slice ports the clock/match frame, arena
  ellipsoid/collision queries, steering/glide physics, speed burst, rok attach/throw/score-value, goal points 1/2/4/4,
  camera tracking/projection and marker geometry, with a 209-check headless self-check. Added `docs/DATA-FORMATS.md`,
  `docs/SIM-SLICE.md`, regenerated the port ledger (145 mapped / 20 stubs / 3,976 unmapped = 4.75% of game code), and
  re-verified the Unity build/capture. Original assets remain local-only.
- Verification: `python scripts/check_all.py` PASS; Unity `UBlade3.EditorTools.UBlade3Checks.RunAll` ->
  `UBLADE3_DATA_OK teams=14 dromes=14 digest=0135846dd22b3e3d`, `sim slice: 209/209 checks passed`, `UBLADE3_CHECKS_OK`;
  `UBLADE3_GAME_BUILD_OK`; `UBLADE3_GAME_CAPTURE_OK` (artifacts/port-lab.png).
- Exactness note: the original `TickClock` is variable-rate; the fixed 30 Hz path is the 20XX modernisation and is
  documented as a port choice, while the decomp host path is implemented alongside it. ADLIB plan VM/animations/
  collision interactors/renderer/audio are the largest remaining port areas (see PORT-LEDGER and SIM-SLICE).
- No exact-clone completion is claimed; this is the data+core milestone.

## 2026.10.27.001

**Bootstrap the UBlade3 exact port: new repository, Unity 6000.6.7f1 project, decompilation references, data/port
foundation, and Port Lab build gate.**

- Date: 2026-10-27
- Change: created C7-IMI/UBlade3 and a new Unity project `unity/UBlade3`; pinned Unity 6000.6.7f1; added read-only
  submodules for hyperblade-decomp (LGPL-3.0 source of truth) and hyperblade-20XX (reference port); added exactness/port
  plan docs and the LGPL license/third-party notices; added the Port Lab bootstrap and Windows build/capture wrapper;
  added the original-data path resolution contract (env var or local config; assets never committed).
- Not yet claimed: full game port. At this version the Unity build is a port-lab foundation; the exact data/sim core
  slices arrive in subsequent versions and the port ledger measures coverage.
- Verification: `python scripts/check_all.py` PASS (static/encoding/link/layout checks); Unity 6000.6.7f1 batchmode
  compile + `UBlade3.EditorTools.UBlade3Checks.RunAll` -> `UBLADE3_DATA_OK teams=14 dromes=14`,
  `sim slice: 209/209 checks passed`, `UBLADE3_CHECKS_OK`; `UBLADE3_GAME_BUILD_OK`; Port Lab capture
  `UBLADE3_GAME_CAPTURE_OK` (artifacts/port-lab.png). Port ledger: 144 mapped / 20 stubs / 3,977 unmapped,
  4.72% of game code. This is a foundation milestone, not a completed exact clone.

