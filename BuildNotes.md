# Build notes

Current version: 2026.10.27.002

UBlade3 uses a script-computed version sequence. Versions 001+ are this port; the 1996 game version is unrelated.

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

