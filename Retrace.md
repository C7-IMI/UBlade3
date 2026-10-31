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

