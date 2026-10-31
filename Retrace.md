# Retrace

Prompt and decision log for UBlade3.

## 1 - 2026-10-27

- Request: make a new repository `UBlade3`, make a new Unity project called UBlade3, use the full game at
  `D:\Games\HyperBlade`, and use REA to clone the game into another programming language or into Unity so it can be
  updated/edited as an exact clone.
- Decision: UBlade3 is a source-level C#/Unity port derived from the byte-matching decompilation, with the original
  game data loaded from a user-supplied install and never committed. REA is used for binary/artifact analysis; its
  native provider is not available on this host, so the decompilation is the source-level ground truth.
- Status: In progress - repository, pinned Unity project, references, port plan, original-data importer, exact core
  slice and Port Lab build are in place and verified. The full exact clone (renderer/assets/AI/tools) is still ahead;
  no completion is claimed.
- Version: 2026.10.27.001

## 2 - 2026-10-30

- Request: "Check for updated capabilities/features in Gypsy. Complete all phases and milestones end to end. Build it
  to a fully playable status. full autonomy. full approval:" followed by the remaining exact-port areas.
- Decision: integrate Gypsy Studio's 2026-10-30 P1 surfaces (toolpacks/slices runner, asset registry, metrics, traces,
  signed provenance/SBOM, project check), ship a fully playable exact HyperBlade runtime through the bundled 20XX
  engine with the Unity host, and keep the C# exact data/sim port as the editable fidelity track. The C# renderer,
  plan VM, animation, audio and original asset import are not complete and are not claimed as such.
- Evidence: Gypsy check PASS; toolpack passed; slice passed; assets 3123; metrics 3 runs; traces 4; provenance
  passed with SBOM 8; release 2026.10.31.001 (ZIP SHA-256 fabbad4b...) extracted and launched an exact game window; data 14/14 teams and
  14/14 dromes; sim 209/209; port ledger 4.75%.
- Version: 2026.10.31.003
- Commit: this change (final hash/push/CI in the final report).

## 3 - 2026-10-31

- Request: after the playable build, plan for 60 fps, smoother character animation at higher fps, camera jitter
  fixes, and 1080p/2K/4K resolution options.
- Status: Completed as a plan - docs/PERFORMANCE-RESOLUTION-PLAN.md. No runtime behavior changed; no measured
  performance/resolution claims are made.
- Version: 2026.10.31.004
- Commit: this change.

## 4 - 2026-10-31

- Request: fully native engine, no external runtime; exact original models/gameplay/drome; build a fully playable
  release; full autonomy/approval.
- Decision: embed the exact 20XX engine as a Unity native plugin rather than launch an external process, and keep the
  C# data/asset/simulation work as the pure-C# track. Native plugin source/patch and built binaries are tracked.
- Evidence: Unity host menu (state 3) and exact match (state 15) captured; extracted native release reaches a live
  match with the original HUD; release contains no hyperblade_20XX.exe or external runtime; asset probe covers
  344 models / 3528 materials / 4182 bones / 333 textures / 188 animations.
- Version: 2026.10.31.005
- Release: 2026.10.31.002 (SHA-256 ef46aed6...61b926).

## 5 - 2026-10-31

- Request: continue toward the pure-C# native engine after the playable native release.
- Work: PlanVM C# loader/VM core with 32/32 plans and a self-check/probe; original-drome C# preview
  loading CHIDRO.ENV and 27 original objects, building Unity meshes and capturing shaded/textured
  frames. PlanVM probe wired into Unity checks.
- Evidence: UBLADE3_PLANVM_OK plans=32 instructions=9420 opcodes=16 unknowns=0 digest=141a750a3de1e083;
  UBLADE3_DROME_PREVIEW_CAPTURES_OK 27 objects; captures in artifacts.
- Boundary: preview lighting/framing is validation, not exact; playable release remains the embedded
  exact native engine. Goal active.
- Version: 2026.10.31.006

## 6 - 2026-10-31

- Request: continue the pure-C# engine after the plan VM/drome preview.
- Work: original skater preview (CHIOMX + CHIGMX) with the exact 16-bone rig, WBA timing/pose and
  original WBM/BMP visuals; six 1280x720 captures.
- Evidence: UBLADE3_SKATER_PREVIEW_CAPTURES_OK, probe mismatches=0 digest=00f075fe626fe291,
  479 triangles / 1,437 verts / 25 textures / 0 missing refs.
- Boundary: validation camera, no match/HUD/planVM wiring; playable release remains embedded native
  exact engine. Goal active.
- Version: 2026.10.31.007

## 7 - 2026-10-31

- Request: continue the pure-C# engine after the skater milestone.
- Work: native C# match scene using original CHIDRO drome, Chicago home and Seattle away skaters,
  existing UBlade3.Sim at 30 Hz, CTrack camera and deterministic capture. Headless/Editor/player
  evidence all green; no 20XX processes.
- Evidence: UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd;
  player build 103900532 bytes, captures 1280x720 frames 45/105/150.
- Boundary: fallback AI, no rok/goals/score, no HUD, single clip per char; not release path.
  Goal active.
- Version: 2026.10.31.008

## 8 - 2026-10-31

- Request: continue the pure-C# engine toward playable gameplay.
- Work: original Rokmix rok rendering, shipped goal/score rules and deterministic 2-1 scoring
  suite, original assets8 HUD art with interim font; seven Editor/player captures.
- Evidence: CHECK_OK mismatches=0; final score 2-1; player build 103955156 bytes with frames
  byte-identical to Editor; no 20XX processes; N4 regression preserved.
- Boundary: planVM/AI fallback, goal fixture, interim HUD/font/layout; not release path. Goal active.
- Version: 2026.10.31.009

## 9 - 2026-10-31

- Request: continue the pure-C# engine with audio and AI.
- Work: UBlade3.Audio WAV/catalog/roles/service/probe; PlanVM AI slice for CARRYROK
  TurnToMate>PassingPlay with actions/decisions/timers and unsupported telemetry.
- Evidence: UBLADE3_AUDIO_OK files=295 durationMs=875224 digest=1a8d0a92b89a6019 mismatches=0;
  UBLADE3_PLAN_AI_OK messages=10 actions=5 decisions=7 timers=4 digest=73073ff095cd4e00
  40/40; prior PlanVM checks green.
- Boundary: audio opt-in/not match-driven; AI slice is one mock-world flow; not release path.
  Goal active.
- Version: 2026.10.31.010

## 10 - 2026-10-31

- Request: continue toward a fully playable C# match.
- Work: N6 playable match loop - shipped ROK pickup/attach, carry, charged launch, pass, real shot
  goal -> shipped goals -> HUD score; match audio routing; seven Editor/player capture beats.
- Evidence: native-play start/pickup/charge/shot/goal/score/pass captures; player frames identical;
  N4/N5 regressions preserved; no 20XX processes.
- Boundary: fallback AI for other skaters, no face-off/restart/celebration, interim HUD, not release
  path. Goal active.
- Version: 2026.10.31.011

## 11 - 2026-10-31

- Request: close post-goal/camera/AI gaps in the playable C# match.
- Work: face-off/restart/celebration flow, head-camera aim and bounded fallback AI possession/shots;
  12 Editor/player capture beats.
- Evidence: N7_CHECK_OK mismatches=0, score 1-0, aiPickups=1 aiShots=1, faceOff ticks 179/200,
  headcam fov=85, 12 player frames byte-identical, audio played=8, N4/N5/N6 regressions green.
- Boundary: PlanVM AI not wired; celebration/goalie/ROK fallbacks; interim HUD; not release path.
  Goal active.
- Version: 2026.10.31.012

## 12 - 2026-10-31

- Request: actually wire PlanVM AI into the native match.
- Work: MatchPlanAi binds CARRYROK to AI slot 1 and runs TurnToMate>PassingPlay>PassingPlayPass
  through the real VM; its pass decision becomes a real match pass. N8 deterministic script and
  captures.
- Evidence: N8_CHECK_OK mismatches=0 planTicks=48 behaviorChanges=3 aiPickups=1 aiPasses=1
  unsupported=8 digest=4fb06b4458419444; Editor/player frames identical; N4-N7 regressions green.
- Boundary: one plan/behavior/skater, labelled motion bridge, many handler ids remain. Goal active.
- Version: 2026.10.31.013

## 13 - 2026-10-31

- Request: generalise real PlanVM AI to the whole native C# match.
- Work: persistent shipped plans for all non-human slots (Defense/CarryRok/Goalie, with plan changes
  on possession), per-slot plan/behaviour/action telemetry, real plan pass decision; six captures.
- Evidence: N9_CHECK_OK slots=5 active=5 planTicks=700 aiPickups=1 aiPasses=1 digest=1ab07ec24368547e;
  Editor/player frames byte-identical; N4-N8 regressions green.
- Boundary: many PlanCond/PlanAction bodies remain; motion bridge/relay approximations; not release.
  Goal active.
- Version: 2026.10.31.014

## 14 - 2026-10-31

- Request: make PlanVM AI the default interactive AI in the C# match.
- Work: default roster attached by MatchPlay.Build for slots 1-5; heuristic suppressed; plan shot
  path via CARRYROK Throwing -> action 139 -> LaunchAiShot; seven capture beats.
- Evidence: N10_CHECK_OK defaultPlanAi=True aiPickups=2 aiPasses=1 aiShots=1 planPassRequests=1
  planShotRequests=1 heuristicFallbackTicks=0 digest=67159e33ed58442d; Editor/player frames
  byte-identical; N4-N9 regressions green.
- Boundary: most plan handler bodies/steering remain; not parity/release. Goal active.
- Version: 2026.10.31.015

## 15 - 2026-10-31

- Request: package and verify a native C# playable build.
- Work: standalone C# match player build, release script, PLAY-README/bat and docs; extracted
  release launched with --play-autostart and rendered the C# match.
- Evidence: UBLADE3_CSHARP_RELEASE_OK ZIP 38,957,710 bytes SHA-256 0c0acc42...623f049; no external
  process; native-csharp-preview.png.
- Boundary: playable preview, not final parity; exact native release remains 2026.10.31.002.
  Goal active.

## 16 - 2026-10-31

- Request: improve plan motion exactness and the native C# player display.
- Work: exact CSteerToTarget/CAdjustAcceleration path for GlideToObject plan targets; display
  presets/fallback/HUD scaling; rebuilt C# preview 004.
- Evidence: N11_CHECK_OK targetPhysics=True planPhysicsTicks=250 slot1Displacement=82.123
  targetChange=1 digest=30b6ad7650946a88; display 1280x720/1920x1080/2560x1440/3840x2160
  validated, HUD scale 1/1.5/2/3; release 004 hash 07d9782c..., extracted player starts, no20XX.
- Boundary: only GlideToObject kind 0 exact; preview not parity. Goal active.
- Version: 2026.10.31.017

## 17 - 2026-10-31

- Request: bring the native C# HUD closer to the original.
- Work: original InfoBar layout/font/art, HUD probe/captures, fixed interactive GUI Y flip;
  rebuilt and repackaged C# preview 005.
- Evidence: CheckHud mismatches=0 missing=0 fontAvailable=True; captures 720p/1080p/4K + design
  640x480; extracted player shows upright original HUD, no20XX; release hash 67bd3682...
- Boundary: roster/bench, dynamic meters/headcam inset/radar frame art remain; preview. Goal active.
- Version: 2026.10.31.018

## 18 - 2026-10-31

- Request: continue PlanVM/AI and native C# audio/UI.
- Work: implemented observed plan handlers (actions 6/7/9/18/29/30/71, decisions 6/23/68); default
  interactive original-wave audio with event routing; live speed meter/goalie row; preview 006.
- Evidence: N12_CHECK_OK digest=333565d513ad20e0; audio ready=True requested=8 played=9 failed=0;
  1080p/4K HUD captures; regressions green; release 006 38986577 bytes hash f95902ee...
- Boundary: remaining handlers/agenda/behparams; recorded-only effects; preview. Goal active.
- Version: 2026.10.31.019

## 19 - 2026-10-31

- Request: keep Gypsy capabilities integrated with the native C# preview.
- Work: C# preview capability, toolpack C# build/capture/release actions and validators, slice
  artifacts, provenance/SBOM refresh.
- Evidence: gypsy check PASS; capabilities 6/6; toolpacks 1/1; provenance passed/SBOM 9.
- Boundary: C# release partial; exact native release remains parity build. Goal active.
- Version: 2026.10.31.020

## 20 - 2026-10-31

- Request: close remaining observed PlanVM gaps.
- Work: actions 84/139, decisions 51/71/120, behaviour params 0/2/3/9/10/11/12/15, agenda 8/15;
  real effects for friction/location/speed-burst/CAvoid/CRelax; preview 007.
- Evidence: N13_CHECK_OK digest=27f18b0e2713eb78 ticks=240 behParams=11 agendaPos=478
  agendaSmoothVel=42; regressions green; release 007 38992989 bytes hash 977d49f3...
- Boundary: Release/netTarget, CAvoid nuance, CRelax camera bias, animation/stat/bubble, decisions
  31/57/123. Goal active.
- Version: 2026.10.31.021

## 21 - 2026-10-31

- Request: close the last observed PlanVM default gaps.
- Work: decisions 31/57/123, CRelax Release/NetTarget, camera bias, CAvoid purge/global counter;
  default plan set unsupportedHandlers=0; preview 008.
- Evidence: N14_CHECK_OK digest=90ee53431fc98757 release=478 netTarget=478 cameraBias=956
  avoidDetach=105 unsupported=0; N4-N13 regressions green; release 008 38994372 bytes e2d90249...
- Boundary: animation/stat/bubble consumers, audio headless, approximations. Goal active.
- Version: 2026.10.31.022

## 22 - 2026-10-31

- Request: make PlanVM animation ids drive the real match skaters.
- Work: AnimAssm/WBA registry+player+consumer wired to plan action 30/behparams 9/11/12;
  differentiated pose captures; preview 009.
- Evidence: N15_CHECK_OK clipChanges=20 events=114 unsupported=0; natural skate/goalie clips and
  labelled fixture clips; N4-N14 regressions; release 009 39001428 bytes fb521107...
- Boundary: segment 0 only, rate/blend, triggers logged, stat/bubble absent, staged captures.
  Goal active.
- Version: 2026.10.31.023

