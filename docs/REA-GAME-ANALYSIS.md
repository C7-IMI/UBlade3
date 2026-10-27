# REA analysis of the full HyperBlade install

This records the REA pass over the full game at `D:\Games\HyperBlade` used to start UBlade3. The analysis is
read-only; no original binaries, data or assets are committed to this repository.

## Install identity

| Path | Bytes | SHA-256 | Meaning |
| --- | --- | --- | --- |
| `HYB\HyperX-XP.exe` | 860,160 | `514bcec6805f27136bed6e4de8796e75d99f224a94e3d7098a4477b029ea24cd` | **Matches the hyperblade-decomp target** (HYPERX.EXE v1.0.0.1) exactly. This is the code reference. |
| `HYB\hyperx.exe` | 1,359,872 | `08c7b7a462847089d41a4e3ac0d61df3409ec8c9107cb97deff08364c4c0de60` | Unpatched v1.0.0.0 executable. Do not use it for symbol/address work without accounting for the version delta. |
| `HYB.bin` | 413,803,824 | not hashed in the committed evidence | Mounted CD image used by the Collection Chamber bundle. |

The full install is the Collection Chamber / Peixoto patched release. It contains the real game data under
`HYB\data`: 3,018 files in `animdata` (189), `assets8` (1,767), `models` (402), `plans` (32), `sounds` (295) and
`textures` (333), plus 14 team `*.HTM` files, 14 drome `*.ENV` files and the config/catalog files.

## REA provider evidence

REA (the operator's `C7-IMI/rea` repository) was cloned and run through `npx -y rea-agents@latest`. On this Windows
host the provider audit exposed no native Ghidra/IDA provider; a Chocolatey Ghidra install failed on
administrative/chocolatey locks. REA therefore did not decompile the executable itself. The host-independent
artifact observations did succeed:

- `inspect-desktop-application D:\Games\HyperBlade --json`: 4,241 entries inventoried, directory snapshot SHA-256
  `48f2a519dbfc81ce2cc002ee849864f58d00c89f5a45ce4b3620f599eec27075`; PE machine facts confirmed for the
  launcher DLLs. Raw output (3.7 MB) is kept locally under `artifacts/rea/` and is not committed.
- `inspect-byte-entropy HYB\hyperx.exe --json`: 1,359,872 bytes, SHA-256
  `08c7b7a462847089d41a4e3ac0d61df3409ec8c9107cb97deff08364c4c0de60`, file entropy 7.6698. The full result is
  in `artifacts/rea/hyperx-entropy.json` (small, committed as evidence).
- `inspect-artifact HYB\hyperx.exe --json`: recognized the target as **PE**, bounded static observations only; no
  semantic decompilation was claimed. Raw result: `artifacts/rea/hyperx-inspect.json`.
- `inspect-byte-entropy HYB.bin`: refused by the artifact provider; this is recorded as a limitation, not silently
  retried.

The source-level ground truth for the port is therefore `third_party/hyperblade-decomp`, which is a byte-matching
decompilation of the 860,160-byte v1.0.0.1 executable above. `third_party/hyperblade-20XX` is the modern reference
port used for data-format and behaviour cross-checks.

## What this means for UBlade3

- The exact code target is unambiguous: `HYB\HyperX-XP.exe` v1.0.0.1.
- The exact data target is `HYB\data` and the file formats documented in `docs/DATA-FORMATS.md`.
- Original assets stay local. The Unity project loads them from `HYPERBLADE_GAME_DATA` or a local
  `Assets/StreamingAssets/game-data-path.txt`.
- Native decompilation of the unpatched 1.36 MB executable is not part of the port until a REA native provider is
  installed; the decompilation already gives the exact v1.0.0.1 code, so it is the higher-quality source.

Raw local evidence lives in `artifacts/rea/` (git-ignored except the small committed summaries) and
`.work/refs/` (git-ignored).
