# AGENTS.md - UBlade3

UBlade3 is a new, source-level Unity 6000.6.7f1 port of HyperBlade (1996). This is not UBlade2 and not a reskin:
the port must follow the byte-matching decompilation at `third_party/hyperblade-decomp` and the original game data.

## Source of truth

1. `third_party/hyperblade-decomp` - byte-matching C++ decompilation of HYPERX.EXE v1.0.0.1. Read-only submodule.
2. `third_party/hyperblade-20XX` - modern C++ reference port; use for data-format and behaviour cross-checks.
3. Original game data from a local install, e.g. `D:\Games\HyperBlade\HYB\data`. Proprietary: never commit it.

## Non-negotiable rules

- The port is **exact**: every ported function cites its decomp file/function/address and has a test or parity check.
- Do not replace original data with invented art or invented rules. Placeholder bootstrap visuals must be labelled.
- Do not copy original assets, executables, audio or models into the repository.
- Do not modify `third_party/` submodules. Do not edit `D:\Games\HyperBlade`.
- Unity version is pinned to 6000.6.7f1 (`ProjectSettings/ProjectVersion.txt`).
- Read `docs/EXACTNESS.md` and `docs/PORT-PLAN.md` before writing code.
- Use UTF-8 without BOM, LF line endings, no tabs. Read back what you write.
- Verify with `python scripts/check_all.py` and the Unity build/capture commands; never claim an unrun check.

## Current scope

Milestone U3-M0/M1: repo, Unity project, original-data importers, exact core port ledger/constants, first simulation-core
slice, and a Port Lab bootstrap. The full game is not claimed until the port ledger is complete and parity tests pass.

## Commands

    python scripts/check_all.py
    powershell -File scripts/ublade3_unity.ps1 -Mode inspect
    powershell -File scripts/ublade3_unity.ps1 -Mode build
    powershell -File scripts/ublade3_unity.ps1 -Mode capture

Game data resolution: `HYPERBLADE_GAME_DATA` or `Assets/StreamingAssets/game-data-path.txt`.
