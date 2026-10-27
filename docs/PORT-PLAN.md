# UBlade3 port plan

## Source of truth

`hyperblade-decomp` is a byte-matching decompilation of `HYPERX.EXE` v1.0.0.1. Its class names, globals, data
formats and functions are the specification for this port. `hyperblade-20XX` is the modern C++ reference build
and the oracle for behaviour/data checks.

## Strategy

1. **Exact data first.** Parse `Data\*.HTM`, `*.ENV`, `*.LST`, `*.CAT`, `BOOIE.INI`, `DFTKEYBD.CFG`,
   `DFTSTICK.CFG`, `GAME.CFG`, `COMMON.CFG`, `GAUNTLET.CFG` with the same field order and unit conventions as the
   decomp. Store the raw values and unit notes; never round-trip through an art asset.
2. **Exact simulation core in C#.** Port the fixed 30 Hz game loop, ellipsoid arena collision, player/steering
   physics, rok physics, goal/multiplier rules, AI and camera from their decomp functions. Keep a one-to-one
   mapping ledger from decomp function addresses/files to C# files so coverage is auditable.
3. **Assets imported, not redrawn.** Load the original `WBM`/`WBA`/`PBN`/`BMP`/`WAV` data through an importer
   that builds Unity meshes, textures, animation clips and audio at import/runtime. Do not replace original art
   with placeholder dromes.
4. **Parity harness.** Automated comparisons against `hyperblade_20XX -sim`/its data checks for team ratings,
   match stats and sim results. A ported subsystem is only 'exact' when the parity test passes.
5. **Unity presentation last.** Wire the ported simulation into a Unity scene once the core matches; then the
   game is editable in the Unity editor without changing the exactness contract.

## Milestones

| Milestone | Description | Acceptance |
| --- | --- | --- |
| U3-M0 | Repository, pinned Unity project, legal/reference structure | Unity project opens and compiles |
| U3-M1 | Original data model/importer | all 14 teams and 14 dromes parse with source line references |
| U3-M2 | Exact sim core slice | arena/camera/input/tick parity notes; playable bootstrap |
| U3-M3 | Physics + rules port | goals, multiplier, periods, collisions match decomp |
| U3-M4 | AI and original assets | full match runs with original models/textures/audio |
| U3-M5 | Complete port | port ledger shows every decomp function mapped/tested |

## What is deliberately not rushed

The first playable Unity frame is a bootstrap for the port, not a claim that the clone is complete. Exact
visuals and the whole AI/rules surface arrive in U3-M3/M4. The repo keeps a machine-readable ledger so progress
is measured, not asserted.
