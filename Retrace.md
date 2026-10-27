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
