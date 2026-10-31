# UBlade3

UBlade3 is a new, source-level Unity port of the 1996 PC game **HyperBlade** (Wizbang! / Activision), built from
the byte-matching decompilation [`zach2good/hyperblade-decomp`](https://github.com/zach2good/hyperblade-decomp) and
validated against the modern port [`zach2good/hyperblade-20XX`](https://github.com/zach2good/hyperblade-20XX).

The goal is an **exact clone** in C#/Unity: same data files, same rules, same arena, camera, controls, AI and
presentation, then editable in Unity. Original game assets are **not** committed. T
## Status

**Fully playable native release:** the exact HyperBlade engine is compiled into the Unity player as the
`ublade3_native` plugin. There is **no external process**: the menus, drome, skaters, rok, HUD and match are the
original engine's own frames, hosted by Unity. Release: `dist/UBlade3-2026.10.31.002-win64.zip` (38,766,617 bytes,
SHA-256 `ef46aed6a525fb949513955be65053f6ce53c5bcaa975d23db7fd4ca1171b926`). Verified by extracting the ZIP, launching
`UBlade3Native.exe` against the local HyperBlade data, entering a live match and capturing the original HUD frame
(`artifacts/native-release-match.png`). The package contains no `hyperblade_20XX.exe` and starts no second process.

The playable exact engine is native C++ embedded in Unity, not a 100% C# engine yet. The C# track is being built
alongside it: exact original data (14/14 teams, 14/14 dromes), 209/209 simulation checks, and the new original-asset
readers (`UBlade3.Assets`) parse 344 models, 3,528 materials, 4,182 bones, 333 textures and 188 animation sets.
The C# renderer, ADLIB plan VM, animation and audio integration remain in progress; the native build above is the
fully playable exact engine.

## Port status

This is a fresh port, not a reskin of UBlade2. It is deliberately being built from the decompiled source and the
original data formats. **Current verified milestone:**
- Unity 6000.6.7f1 project compiles and builds; Port Lab capture passes.
- Exact original-data probe: 14 teams, 14 dromes, 140 team objects, 464 drome objects, 0 warnings (from
  `D:\Games\HyperBlade\HYB`; assets are local only).
- Simulation-core self-check: 209/209 passed; arena 105 x 187.5 x 105, camera FOV 85/bungee 12, rules/steering
  constants cited from the decompilation.
- C# Plan VM: `UBlade3.PlanVM` loads 32/32 shipped PBN plans, 9,420 instructions, 16 opcodes, 0 unknowns, digest
  `141a750a3de1e083`; 123/123 self-checks. Action/decision bodies and game wiring are still port gaps.
- C# original-drome preview: loads `CHIDRO.ENV` + 27 original objects through the C# WBM/BMP readers and builds
  Unity meshes; captures are in `artifacts/drome-preview-chidro-*.png`. Lighting/camera are validation views, not
  yet pixel-identical, so this preview is not the playable release.
- C# native match milestone: original CHIDRO + Chicago/Seattle skaters, UBlade3.Sim at 30 Hz, six posed original
  characters and a CTrack camera; headless check `ticks=150 mismatches=0`, player build 103,900,532 bytes with
  captures in `artifacts/native-match-frame-*.png`. Fallback AI; see [docs/NATIVE-MATCH.md](./docs/NATIVE-MATCH.md).
- C# native gameplay milestone: original Rokmix ROK (9 bones/257 tris, render-bone variants), shipped goal/score
  rules with a deterministic 2-1 suite, original assets8 HUD art (interim font) and seven Editor/player captures
  in `artifacts/native-gameplay-*.png`; player build 103,955,156 bytes with frame parity. PlanVM/AI, full shot
  physics and HUD details remain gaps. See [docs/NATIVE-GAMEPLAY.md](./docs/NATIVE-GAMEPLAY.md).
- C# audio data/player: 295 original WAV files / 363 catalog entries, 9 cited roles, 8-channel AudioService behind
  an opt-in hook; probe digest `1a8d0a92b89a6019`. See [docs/NATIVE-AUDIO.md](./docs/NATIVE-AUDIO.md).
- C# PlanVM AI slice: CARRYROK TurnToMate>PassingPlay with actions 5/93/103, decisions 15/50/78/111, timers,
  message/agenda glue; `UBLADE3_PLAN_AI_OK ... digest=73073ff095cd4e00`, 40/40 self-checks. One flow only; not
  match AI. See [docs/PLAN-AI.md](./docs/PLAN-AI.md).
- C# playable match loop: human skater picks up the ROK (shipped RokInRange/Attach), carries, charges/shoots
  (PlanAction::Launch), passes (Throw), and a real shot enters the goal -> shipped score rules/HUD/audio. Headless
  `approachTicks=42 pickups=2 shots=1 goals=1 score=1-0 realFlightGoal=True`; seven Editor/player captures with
  byte-identical player output and 7 audio plays. See [docs/NATIVE-PLAY.md](./docs/NATIVE-PLAY.md).
- C# N7 playable follow-up: post-goal celebration/face-off drag/restart, 85-degree head-camera aim and fallback-AI
  ROK pursuit/shots; `N7_CHECK_OK mismatches=0 score=1-0 aiPickups=1 aiShots=1`, 12 Editor/player capture beats with
  byte-identical player frames and 8 audio plays. See [docs/NATIVE-MATCH-N7.md](./docs/NATIVE-MATCH-N7.md).
- C# N8 real PlanVM AI: AI slot 1 picks up the ROK, binds CARRYROK, runs TurnToMate>PassingPlay>PassingPlayPass
  through the real VM and its plan decision becomes a real pass. `N8_CHECK_OK mismatches=0 planTicks=48
  aiPickups=1 aiPasses=1 digest=4fb06b4458419444`. See [docs/NATIVE-MATCH-N8.md](./docs/NATIVE-MATCH-N8.md).
- C# N9 whole-match PlanVM roster: every non-human slot runs a persistent shipped plan (Defense/CarryRok/Goalie)
  with plan changes on possession; `N9_CHECK_OK slots=5 active=5 planTicks=700 aiPickups=1 aiPasses=1
  digest=1ab07ec24368547e`; six Editor/player frames byte-identical. Many handler bodies/motion physics remain.
  See [docs/NATIVE-MATCH-N9.md](./docs/NATIVE-MATCH-N9.md).
- Port ledger: 145 mapped / 20 stubs / 3,976 unmapped = 4.75% of the 3,054 game functions. **The exact full clone is
  not complete yet**; the ledger is the progress measure.
See [docs/PORT-PLAN.md](./docs/PORT-PLAN.md), [docs/CORE-NOTES.md](./docs/CORE-NOTES.md),
[docs/PORT-LEDGER.md](./docs/PORT-LEDGER.md), [docs/PORT-LEDGER.json](./docs/PORT-LEDGER.json) and
[docs/EXACTNESS.md](./docs/EXACTNESS.md).
- Gypsy integration: [docs/GYPSY-UPGRADE-INTEGRATION.md](./docs/GYPSY-UPGRADE-INTEGRATION.md).
- Performance/resolution plan: [docs/PERFORMANCE-RESOLUTION-PLAN.md](./docs/PERFORMANCE-RESOLUTION-PLAN.md).
- Native engine plan: [docs/NATIVE-ENGINE-PLAN.md](./docs/NATIVE-ENGINE-PLAN.md).
- C# Plan VM: [docs/PLAN-VM.md](./docs/PLAN-VM.md).
- C# drome preview: [docs/DROME-PREVIEW.md](./docs/DROME-PREVIEW.md).
- C# native gameplay: [docs/NATIVE-GAMEPLAY.md](./docs/NATIVE-GAMEPLAY.md).
- C# playable loop: [docs/NATIVE-PLAY.md](./docs/NATIVE-PLAY.md).
- C# N7 playable follow-up: [docs/NATIVE-MATCH-N7.md](./docs/NATIVE-MATCH-N7.md).
- C# N8 PlanVM AI: [docs/NATIVE-MATCH-N8.md](./docs/NATIVE-MATCH-N8.md).
- C# N9 PlanVM roster: [docs/NATIVE-MATCH-N9.md](./docs/NATIVE-MATCH-N9.md).
- C# audio: [docs/NATIVE-AUDIO.md](./docs/NATIVE-AUDIO.md).
- C# PlanVM AI: [docs/PLAN-AI.md](./docs/PLAN-AI.md).
- Original asset formats: [docs/ASSET-FORMATS.md](./docs/ASSET-FORMATS.md).

## Requirements

- Unity **6000.6.7f1** (pinned in `ProjectSettings/ProjectVersion.txt`).
- A local retail HyperBlade install with `data\` (teams, dromes, models, textures, sounds) and `HYPERX.EXE`.
- Windows x64 for the current build gate; the C# core is intended to stay cross-platform.

## Legal

The ported code derives from the LGPL-3.0 decompilation; see `LICENSE`, `THIRD-PARTY-NOTICES.md` and
`third_party/`. The original game data, executable, artwork and audio are proprietary and are not distributed
here. You must own the game and point UBlade3 at your own copy.
