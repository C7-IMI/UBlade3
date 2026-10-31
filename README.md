# UBlade3

UBlade3 is a new, source-level Unity port of the 1996 PC game **HyperBlade** (Wizbang! / Activision), built from
the byte-matching decompilation [`zach2good/hyperblade-decomp`](https://github.com/zach2good/hyperblade-decomp) and
validated against the modern port [`zach2good/hyperblade-20XX`](https://github.com/zach2good/hyperblade-20XX).

The goal is an **exact clone** in C#/Unity: same data files, same rules, same arena, camera, controls, AI and
presentation, then editable in Unity. Original game assets are **not** committed. The project reads them from a
local HyperBlade installation (for example `D:\Games\HyperBlade`) through `HYPERBLADE_GAME_DATA` or
`Assets/StreamingAssets/game-data-path.txt`.

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
- Port ledger: 145 mapped / 20 stubs / 3,976 unmapped = 4.75% of the 3,054 game functions. **The exact full clone is
  not complete yet**; the ledger is the progress measure.
See [docs/PORT-PLAN.md](./docs/PORT-PLAN.md), [docs/CORE-NOTES.md](./docs/CORE-NOTES.md),
[docs/PORT-LEDGER.md](./docs/PORT-LEDGER.md), [docs/PORT-LEDGER.json](./docs/PORT-LEDGER.json) and
[docs/EXACTNESS.md](./docs/EXACTNESS.md).
- Gypsy integration: [docs/GYPSY-UPGRADE-INTEGRATION.md](./docs/GYPSY-UPGRADE-INTEGRATION.md).
- Performance/resolution plan: [docs/PERFORMANCE-RESOLUTION-PLAN.md](./docs/PERFORMANCE-RESOLUTION-PLAN.md).
- Native engine plan: [docs/NATIVE-ENGINE-PLAN.md](./docs/NATIVE-ENGINE-PLAN.md).
- Original asset formats: [docs/ASSET-FORMATS.md](./docs/ASSET-FORMATS.md).

## Requirements

- Unity **6000.6.7f1** (pinned in `ProjectSettings/ProjectVersion.txt`).
- A local retail HyperBlade install with `data\` (teams, dromes, models, textures, sounds) and `HYPERX.EXE`.
- Windows x64 for the current build gate; the C# core is intended to stay cross-platform.

## Legal

The ported code derives from the LGPL-3.0 decompilation; see `LICENSE`, `THIRD-PARTY-NOTICES.md` and
`third_party/`. The original game data, executable, artwork and audio are proprietary and are not distributed
here. You must own the game and point UBlade3 at your own copy.
