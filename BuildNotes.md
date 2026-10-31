# Build notes

Current version: 2026.10.27.002

UBlade3 uses a script-computed version sequence. Versions 001+ are this port; the 1996 game version is unrelated.

## 2026.10.31.018

**Original InfoBar HUD layout and MS Sans Serif text; C# preview 005.**

- Date: 2026-10-31
- Change: added MatchHudFont/MatchHudOriginal/MatchHudProbe. The interactive native C# player
  now draws the original InfoBar layout (period/clock/lamps, speed frame, team frames/logos,
  damage bars, scores/ROK, message/status, radar, shot camera/power/reticle, rosters) with the
  installed Windows GDI "MS Sans Serif" face via the original CreateFontA call, plus HUD scaling
  at 720p/1080p/1440p/4K.
- Verified: CheckHud `mismatches=0 missing=0 fontAvailable=True`, exact 640x480 design rects and
  layer scales, captures at 720p/1080p/4K plus a 640x480 design frame. Fixed the interactive GUI
  upload orientation (HUD was vertically flipped) before packaging.
- Release 2026.10.31.005-csharp-preview: ZIP 38,979,221 bytes SHA-256
  `67bd36825ad27955bbd2098a9a48d996966ea6376df5458b9917610e25046cb3`; extracted player launches
  with --play-autostart, original HUD upright, UBLADE3_NATIVE_PLAY_READY, no external process.
- Boundary: roster names/bench seats, dynamic speed/FPS/network/headcam inset, radar frame art and
  host GDI height mapping remain gaps; existing N4-N11 deterministic captures keep the legacy HUD.
- Docs: docs/NATIVE-HUD.md.

## 2026.10.31.017

**Plan-target steering physics and display/resolution options; C# preview 004.**

- Date: 2026-10-31
- Change: MatchPlanAiMotion ports CSteerToTarget/CAdjustAcceleration for behaviour kind 0
  GlideToObject and drives the Sim skater through the exact glide physics (target force, braking,
  lead/clamp, arrival, bank forces; deterministic jitter substitution). Other kinds remain labelled
  bridge fallback. Added MatchDisplaySettings/Controller with presets 720p/1080p/1440p/4K/Native,
  windowed/borderless/fullscreen, validation/fallback, HUD scaling (1/1.5/2/3), persistence and
  F10/F11/F12 controls. Rebuilt and repackaged the native C# preview.
- Verified motion: N11_CHECK_OK targetPhysics=True planPhysicsTicks=250 slot1PhysicsTicks=100
  slot1Displacement=82.123 slot1StartDistance=60.253 slot1MinDistance=0.382 targetChange=1
  bridgeFallbackTicks=50 digest=30b6ad7650946a88; Editor captures 6.
- Verified display: default client 1280x720; 1080p 1920x1080; 1440p 2560x1440; 4K borderless
  3840x2160; unsupported 8K falls back to 4K; HUD scale 1/1.5/2/3; CheckDisplay failures=0.
- Release 2026.10.31.004-csharp-preview: ZIP 38,969,894 bytes SHA-256
  `07d9782cf69adb9f273cae902345c42a111a6c2ed61923a4c1bcdb725c06721c`; extracted player
  launched with --play-autostart, display applied 1280x720, no external process.
- Boundary: only GlideToObject kind 0 exact; other motion kinds bridge/no-op; display is a runtime
  layer with first-frame caveat and limited exclusive/multi-monitor/DPI testing. Preview, not parity.
- Docs: docs/PLAN-MOTION.md, docs/NATIVE-DISPLAY.md.

## 2026.10.31.016

**Native C# playable preview release packaged and verified.**

- Date: 2026-10-31
- Change: added scripts/ublade3_csharp_release.ps1 and packaged the standalone C# match player
  (UBlade3NativeMatchPlay.exe) with the player data, docs, PLAY-README, data-path instructions and
  Play-UBlade3-CSharp.bat. No external runtime.
- Verified: UBLADE3_CSHARP_RELEASE_OK; ZIP 38,957,710 bytes SHA-256
  `0c0acc42d1bb6a6b9fcfcfc285c1030b09883b728d8a3c8964a6f3009623f049`; extracted ZIP launched
  with --play-autostart reaches UBLADE3_NATIVE_PLAY_READY and renders the native C# match
  (artifacts/native-csharp-preview.png); no hyperblade_20XX process.
- Boundary: playable preview, not final parity. PlanVM AI default on non-human slots, but many plan
  handler bodies, exact steering/collision physics, retail celebration/goalie plans, HUD details
  and pixel parity remain. The exact native release 2026.10.31.002 remains the parity build.
- Docs: PLAY-README in the ZIP; NATIVE-MATCH-N10/NATIVE-PLAY docs.

## 2026.10.31.015

**Default interactive native C# match now runs PlanVM AI; plan shot path added.**

- Date: 2026-10-31
- Change: MatchPlay.Build now attaches the persistent MatchPlanAiRoster for slots 1-5 on the
  normal player path. Every non-human skater and goalie runs its shipped Defense/CarryRok/Goalie
  plan with possession changes; heuristic AI is suppressed for plan slots (fallback only when a
  plan cannot bind). CARRYROK's Throwing animation bridge delivers the authored event and the plan
  executes action 139 -> RequestAiShot -> shipped LaunchAiShot.
- Verified: N10_CHECK_OK on the default path defaultPlanAi=True aiPickups=2 aiPasses=1 aiShots=1
  heuristicFallbackTicks=0 suppressedUpdateTicks=480 slots=5 activeAtEnd=5 planTicks=800
  planDigest=67159e33ed58442d determinism=True; 7 Editor + 7 player frames byte-identical;
  N4-N9 regressions and captures preserved; no20xx processes.
- Boundary: most PlanCond/PlanAction bodies remain unported; exact steering/collision/agenda
  classes, relay approximation, OffWing/Sideln adoption and shot animation bridge are gaps.
  Not full AI parity or a release claim.
- Docs: docs/NATIVE-MATCH-N10.md.

## 2026.10.31.014

**Whole-match PlanVM AI roster: five plans active simultaneously in the C# match.**

- Date: 2026-10-31
- Change: added MatchPlanAiRoster/MatchPlanAiDecisions and PlanAiDecisionExtensions. Every
  non-human slot now runs a persistent shipped plan from its HTM SetPlans table: AI skaters start
  Defense, the carrier changes to CARRYROK (documented 1->5 entry) and TurnToMate/PassingPlay
  becomes a real match pass, LostIt returns it to Defense, and both goalies run GOALIE. Plans,
  behaviours, agenda/timers, actions/decisions and plan changes stay in the real PlanVM.
- Verified: `N9_CHECK_OK` frames=6; headless slots=5 active=5 maxActive=5 planIds=CarryRok/Defense/
  Goalie ticks=140 planTicks=700 aiPickups=1 aiPasses=1 planPassTick=50 rebinds=2 relays=4
  unsupportedDecisions=9 digest=1ab07ec24368547e determinism=True; Editor/player frames
  byte-identical (digest 25e2607dcad029e3); N4-N8 regressions green; no20xx processes.
- Boundary: most PlanCond/PlanAction bodies still unported (observed ids listed in the doc);
  SET_Target motion uses the labelled held-mask bridge, no exact CSteerToTarget/collision/goal-
  celebration chain; plan changes coalesced per tick; relays approximated; no plan shot path.
  Interactive N4-N8 default remains heuristic until coverage broadens. Not parity/release.
- Docs: docs/NATIVE-MATCH-N9.md.

## 2026.10.31.013

**First real PlanVM-driven AI inside the native C# match.**

- Date: 2026-10-31
- Change: added MatchPlanAi adapter and the N8 play script. AI slot 1 picks up the ROK through the
  match possession rule, binds the shipped CARRYROK plan to live Simulation state, runs
  TurnToMate -> PassingPlay -> PassingPlayPass through the real PlanVM agenda/timers/messages, and
  its own pass decision becomes a real MatchPossessionRules pass. See docs/NATIVE-MATCH-N8.md.
- Verified: `UBLADE3_NATIVE_MATCH_N8_CHECK_OK mismatches=0`; plan nodes=889 planTicks=48
  behaviorChanges=3 targetChanges=2 aiPickups=1 aiPasses=1 planPassTick=99 unsupported=8
  digest=4fb06b4458419444; determinism ok; heuristic comparison documented; 6 Editor + 6 player
  frames byte-identical; N4/N5/N6/N7 regressions green; no20xx processes.
- Boundary: one plan/behavior/skater; other AI is fallback; movement uses a labelled held-mask
  bridge (CSteerToTarget/glide not ported); behaviour subclasses/behparams and most action/decision
  bodies remain. Not full AI parity or release.
- Docs: docs/NATIVE-MATCH-N8.md.

## 2026.10.31.012

**N7 playable match: face-off/restart, head camera and fallback-AI possession.**

- Date: 2026-10-31
- Change: added MatchFlow/MatchFaceOff, MatchHeadCamera, MatchAiController and the deterministic
  N7 play script. Goals now stop play, show a celebration beat, set face-off corners, drag the ROK,
  restart and resume; the human gets an 85-degree head-camera aim path with a shot word; fallback AI
  slots pursue the free ROK and take a shot/pass.
- Verified: `UBLADE3_NATIVE_MATCH_N7_CHECK_OK mismatches=0`, humanPickups=1 humanShots=1
  humanGoals=1 aiPickups=1 aiShots=1 score=1-0, faceOff setup/resume ticks 179/200, headcam
  fov=85 aimedViewFracX=0.622; 12 Editor + 12 player frames byte-identical; audio played=8; N4/N5/N6
  regressions preserved; no20xx=true.
- Boundary: PlanVM AI remains unported; celebration is a paused HUD/audio beat, ROK drop and
  goalies are fallbacks; head-camera eye is the sim aim position, not HEAD_BONE; AI is heuristic;
  no receiver/catch; interim HUD/ROK; not release/parity.
- Docs: docs/NATIVE-MATCH-N7.md.

## 2026.10.31.011

**First playable native C# match loop: pickup, shoot, real goal, HUD score and audio routing.**

- Date: 2026-10-31
- Change: added MatchPlay/MatchPossession/MatchAudioDirector and the Editor play gate. The human
  home skater now picks up the ROK through the shipped RokInRange/Attach/AttachPak path, carries,
  charges and shoots via PlanAction::Launch, passes via Throw, and a real shot into the goal
  triggers the shipped goal rules, HUD score update and original-wave audio routing. Other skaters
  remain the N4/N5 fallback AI.
- Verified: seven Editor and seven player captures (start, pickup, charge, shot, goal, score,
  pass); player frames byte-identical; N4/N5 regressions preserved; no 20XX processes; controls
  and evidence documented in docs/NATIVE-PLAY.md.
- Boundary: fallback AI for non-human skaters, no face-off/restart/celebration, HUD interim
  font/details, no pixel-parity gate; this is not the final release path. Native exact release
  remains 2026.10.31.002.
- Docs: docs/NATIVE-PLAY.md.

## 2026.10.31.010

**Original audio data path and first PlanVM-driven AI flow.**

- Date: 2026-10-31
- Change: added `UBlade3.Audio` with a dependency-free RIFF/WAVE PCM reader, original
  `SNDFILES.LST` sound catalog, nine cited gameplay roles, an 8-channel Unity AudioService behind
  the opt-in `--ublade3-audio` hook, and AudioProbe. Added the PlanVM AI slice for
  CARRYROK behavior 5->7->9 (TurnToMate/PassingPlay), with action ids 5/93/103, decision ids
  15/50/78/111, timer agenda, real message/agenda glue and explicit unsupported telemetry.
- Verified audio: `UBLADE3_AUDIO_OK files=295 entries=363 durationMs=875224 digest=1a8d0a92b89a6019`,
  mismatches=0; 23/23 parser checks; Unity compile green.
- Verified AI: `UBLADE3_PLAN_AI_OK plan=CARRYROK behavior=TurnToMate>PassingPlay messages=10
  actions=5 decisions=7 timers=4 unknowns=0 unsupported=7 digest=73073ff095cd4e00`, 40/40
  self-checks, prior PlanVM 32/32/123 checks regression green.
- Boundary: audio is opt-in and not match-driven; 87 listed waves are absent on this install;
  AdLib/CD music and mixer parity are out of scope. The AI slice is one behavior flow against a
  mock world, not match AI; 132 action and 135 decision bodies remain. Native exact release
  remains 2026.10.31.002.
- Docs: docs/NATIVE-AUDIO.md, docs/PLAN-AI.md.

## 2026.10.31.009

**C# native gameplay: original rok, goal rules, score and HUD, verified.**

- Date: 2026-10-31
- Change: added MatchGameplayRules/MatchRokView/MatchHud/MatchGameplay. The C# match now renders
  the original Rokmix rig (9 bones, 257 triangles, render-bone plain/double/quad mapping), detects
  goals with the shipped scoring rules (entry/value/points/side/bank/400 ms debounce), tracks score
  and shows the original assets8 HUD art (frper/frdamgA/frdamgB/hudChi/hudsea) with an interim font.
  Deterministic suite scores a home 2-point goal and an away 1-point goal (final 2-1).
- Verified: headless CHECK_OK mismatches=0; Editor 7 captures 1280x720; Windows player
  PLAYER_BUILD_OK bytes=103,955,156 and PLAYER_CAPTURES_OK frames=7 score=2-1, player frames
  byte-identical to Editor; no20xxProcesses=0; N4 regression digest preserved.
- Boundary: plan VM/AI fallback, goal fixture rather than full shot/plan path, no
  face-off/restart/celebration, rok rotation stand-in, interim HUD font/simplified layout, not the
  playable release path. Native exact release remains 2026.10.31.002.
- Docs: docs/NATIVE-GAMEPLAY.md.

## 2026.10.31.008

**C# native match scene with original drome and skaters.**

- Date: 2026-10-31
- Change: added `UBlade3.Native` MatchData/MatchSimulation/MatchSkaterView/MatchScene/MatchProbe
  and the Editor capture/build entry. The C# match loads CHIDRO, home Chicago (CHIOMX/CHIDMX/
  CHIGMX) and away Seattle (SEADMX x2/SEAGMX) original WBM/BMP assets, binds the existing
  UBlade3.Sim simulation, runs 30 Hz ticks, poses six original skaters with their WBA skate/goalie
  clips, drives a CTrack camera and captures deterministic frames. No 20XX process is started.
- Verified: headless check ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd; Editor
  capture frames 45/105/150 (1280x720) and Windows player `UBLADE3_NATIVE_MATCH_PLAYER_BUILD_OK`
  bytes=103,900,532 with player frames byte-identical to the Editor; no20xxProcesses=0.
- Boundary: plan VM/AI not wired (labelled fallback), no rok/goal/score, HUD pending, one clip per
  character, camera shake/tilt incomplete; this is a C# milestone, not the playable release.
  Native exact release remains 2026.10.31.002.
- Docs: docs/NATIVE-MATCH.md.

## 2026.10.31.007

**C# original skater model, skeleton and animation milestone.**

- Date: 2026-10-31
- Change: added `UBlade3.Native` skater preview rendering the real CHIOMX skater ("Evans", Chicago
  The Machine) and CHIGMX goalie ("Viveros") from the player's own Mdltxt WBM + BMP textures with
  the original 16-bone rig and WBA animation: AA_Skate_Std (azkskat2, frames 0..26 @30fps) and
  AA_Goalidle (aplread2, frames 0..80). Reconstructed the shipped pose path (bind T(rest), header
  rotation, format 1/2 clip transforms, packed-rotation quantisation, PropagateTransforms scale
  rule) and captured six 1280x720 frames. Added docs/SKATER-PREVIEW.md.
- Verified: `UBLADE3_SKATER_PREVIEW_CAPTURES_OK`; probe `mismatches=0 digest=00f075fe626fe291`;
  counts models=2 meshes=2 bones=32 clips=32 displayedFrames=108 triangles=479 verts=1437
  textures=25 missingRefs=0; skater poses at 0/0.3/0.6 s visibly differ (frame0-vs-1 max delta
  1.5029 units).
- Boundary: CPU vertex posing, validation camera, no match/HUD/plan-VM wiring, no GPU skinning,
  no pixel-parity gate. This is the exact visual asset path, not the playable C# renderer yet.
  The native exact release remains 2026.10.31.002.
- Docs: docs/SKATER-PREVIEW.md; assets docs unchanged.

## 2026.10.31.006

**C# Plan VM core and original-drome preview milestone.**

- Date: 2026-10-31
- Change: added the C# `UBlade3.PlanVM` loader/VM core (32/32 shipped PBN plans, 9,420 instructions,
  16 opcodes, 0 unknowns, digest 141a750a3de1e083, 123/123 self-checks) and the original-asset
  `UBlade3.Native` drome preview (CHIDRO.ENV, 27 objects, original WBM/BMP data, Unity mesh
  generation, shader/material, Editor capture/probe). Wired the PlanVM probe into the Unity
  check gate; added docs/PLAN-VM.md and docs/DROME-PREVIEW.md.
- Verified: Unity checks `UBLADE3_CHECKS_OK` include `UBLADE3_PLANVM_OK plans=32 ...`; editor
  `CaptureAll` logs 27 `UBLADE3_DROME_PREVIEW_OBJECT` lines and `UBLADE3_DROME_PREVIEW_CAPTURES_OK`;
  captures at 1280x720.
- Boundary: the drome preview uses validation lighting/camera and is not pixel-identical yet; the
  playable release remains the embedded exact native engine. PlanVM action/decision bodies and
  renderer/game wiring remain port gaps.
- Release unchanged: 2026.10.31.002 native exact engine.

## 2026.10.31.005

**Native exact engine inside Unity: no external process, native release verified.**

- Date: 2026-10-31
- Change: built the exact HyperBlade 20XX C++ engine as the **ublade3_native** Unity native plugin. The Unity player
  now owns the engine (menu, drome, skaters, rok, HUD, match, input) instead of launching a second executable.
  Added `NativeEngineHost.cs`, the `NativeEngineScene` build, the plugin/SDL3 bundle, native menu/match capture
  automation, `scripts/ublade3_release.ps1` (native package), the source patch/copy under `native/`, and the
  C# original-asset readers from the asset workstream (`UBlade3.Assets` + docs/ASSET-FORMATS.md).
- Verification: native Unity host menu state 3 and match state 15 captured; extracted release
  `UBlade3-2026.10.31.002-win64.zip` starts the embedded engine, reaches a live match with HUD (640x480 exact
  original frame), and contains **no hyperblade_20XX.exe** / external runtime. C# asset probe:
  `UBLADE3_ASSETS_OK models=344 materials=3528 bones=4182 textures=333 animations=188 warnings=5`.
- Release: `UBLADE3_NATIVE_RELEASE_OK version=2026.10.31.002 ... external=false`; ZIP 38,766,617 bytes
  SHA-256 `ef46aed6a525fb949513955be65053f6ce53c5bcaa975d23db7fd4ca1171b926`; EXE SHA-256
  `9806abde4a406ed68647e9505852c49f6aa72a372f4f084e2aa2821affea57a2`.
- Honest boundary: the **playable exact engine is native C++ embedded in Unity**, not a 100% C# engine. The C#
  data/simulation/asset-reader track is the next step for a pure-C# renderer; the native build is exact and
  editable through the tracked patch/source under `native/`. Original game assets are never committed.

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

