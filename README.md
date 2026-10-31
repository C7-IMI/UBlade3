# UBlade3

UBlade3 is a new, source-level Unity port of the 1996 PC game **HyperBlade** (Wizbang! / Activision), built from
the byte-matching decompilation [`zach2good/hyperblade-decomp`](https://github.com/zach2good/hyperblade-decomp) and
validated against the modern port [`zach2good/hyperblade-20XX`](https://github.com/zach2good/hyperblade-20XX).

The goal is an **exact clone** in C#/Unity: same data files, same rules, same arena, camera, controls, AI and
presentation, then editable in Unity. Original game assets are **not** committed. The project reads them from a
local HyperBlade installation (for example `D:\Games\HyperBlade`) through `HYPERBLADE_GAME_DATA` or
`Assets/StreamingAssets/game-data-path.txt`.

## Status

**Fully playable now:** the Unity host launches the exact HyperBlade engine through the bundled LGPL-3.0 20XX runtime,
staged beside your own original game data. Press **PLAY EXACT HYPERBLADE** in the Port Lab; the exact game opens in
its own window and quitting returns to UBlade3. Release: `dist/UBlade3-2026.10.31.001-win64.zip` (38,514,377 bytes,
SHA-256 `fabbad4b1be3afa1e19b8e022cff14139b245cbea4972d00ff6d4598e0b4a9fe`), verified by extracting it and starting a
game window.

The C# exact-port track is editable alongside it: 14/14 teams and 14/14 dromes parse from the original data, the
simulation core passes 209/209 checks, and the port ledger is 145 mapped / 20 stubs / 3,976 unmapped = 4.75% of the
3,054 game functions. The remaining C# renderer, ADLIB plan VM, animation, audio and asset import are not complete;
the exact native runtime is the playable engine until they are.

## Port status

This is a fresh port, not a reskin of UBlade2. It is deliberately being built from the decompiled source and the
original data formats. **Current verified milestone:**
- Unity 6000.6.7f1 project compiles and builds; Port Lab capture passes.
- Exact original-data probe: 14 teams, 14 dromes, 140 team objects, 464 drome objects, 0 warnings (from
  `D:\Games\HyperBlade\HYB`; assets are local only).
- Simulation-core self-check: 209/209 passed; arena 105 x 187.5 x 105, camera FOV 85/bungee 12, rules/steering
  constants cited from the decompilation.
- Port ledger: 145 mapped / 20 stubs / 3,976 unmapped = 4.75% of the 3,054 game functions. **The exact full clone is
  not complete yet**; the ledger is the progress measure.
See [docs/PORT-PLAN.md](./docs/PORT-PLAN.md), [docs/CORE-NOTES.md](./docs/CORE-NOTES.md),
[docs/PORT-LEDGER.md](./docs/PORT-LEDGER.md), [docs/PORT-LEDGER.json](./docs/PORT-LEDGER.json) and
[docs/EXACTNESS.md](./docs/EXACTNESS.md).

## Requirements

- Unity **6000.6.7f1** (pinned in `ProjectSettings/ProjectVersion.txt`).
- A local retail HyperBlade install with `data\` (teams, dromes, models, textures, sounds) and `HYPERX.EXE`.
- Windows x64 for the current build gate; the C# core is intended to stay cross-platform.

## Legal

The ported code derives from the LGPL-3.0 decompilation; see `LICENSE`, `THIRD-PARTY-NOTICES.md` and
`third_party/`. The original game data, executable, artwork and audio are proprietary and are not distributed
here. You must own the game and point UBlade3 at your own copy.
