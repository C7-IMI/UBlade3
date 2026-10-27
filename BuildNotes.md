# Build notes

Current version: 2026.10.27.001

UBlade3 uses a script-computed version sequence. Versions 001+ are this port; the 1996 game version is unrelated.

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
  `sim slice: 196/196 checks passed`, `UBLADE3_CHECKS_OK`; `UBLADE3_GAME_BUILD_OK`; Port Lab capture
  `UBLADE3_GAME_CAPTURE_OK` (artifacts/port-lab.png). Port ledger: 144 mapped / 20 stubs / 3,977 unmapped,
  4.72% of game code. This is a foundation milestone, not a completed exact clone.

