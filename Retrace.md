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

## 3 - 2026-10-31

- Request: after the playable build, plan for 60 fps, smoother character animation at higher fps, camera jitter
  fixes, and 1080p/2K/4K resolution options.
- Status: Completed as a plan - docs/PERFORMANCE-RESOLUTION-PLAN.md. No runtime behavior changed; no measured
  performance/resolution claims are made.
- Version: 2026.10.31.004
- Commit: this change.

## 4 - 2026-10-31

- Request: fully native engine, no external runtime; exact original models/gameplay/drome; build a fully playable
  release; full autonomy/approval.
- Decision: embed the exact 20XX engine as a Unity native plugin rather than launch an external process, and keep the
  C# data/asset/simulation work as the pure-C# track. Native plugin source/patch and built binaries are tracked.
- Evidence: Unity host menu (state 3) and exact match (state 15) captured; extracted native release reaches a live
  match with the original HUD; release contains no hyperblade_20XX.exe or external runtime; asset probe covers
  344 models / 3528 materials / 4182 bones / 333 textures / 188 animations.
- Version: 2026.10.31.005
- Release: 2026.10.31.002 (SHA-256 ef46aed6...61b926).

## 5 - 2026-10-31

- Request: continue toward the pure-C# native engine after the playable native release.
- Work: PlanVM C# loader/VM core with 32/32 plans and a self-check/probe; original-drome C# preview
  loading CHIDRO.ENV and 27 original objects, building Unity meshes and capturing shaded/textured
  frames. PlanVM probe wired into Unity checks.
- Evidence: UBLADE3_PLANVM_OK plans=32 instructions=9420 opcodes=16 unknowns=0 digest=141a750a3de1e083;
  UBLADE3_DROME_PREVIEW_CAPTURES_OK 27 objects; captures in artifacts.
- Boundary: preview lighting/framing is validation, not exact; playable release remains the embedded
  exact native engine. Goal active.
- Version: 2026.10.31.006

