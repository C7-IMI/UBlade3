# UBlade3 native-engine plan

Status: active plan, 2026-10-31. Target: **no external runtime**. The Unity application must contain the complete
HyperBlade engine and load the user's original data, with no 20XX process, DLL or launcher in the playable path.

## Current state

- A native Unity Windows player now builds from `Assets/Legacy/Scenes/Main.unity` and runs a complete match
  (`--match-capture`: 1280x720, UBLADE2_MATCH_CAPTURE_OK). This proves the native host/input/match loop works.
- That native match currently uses the UBlade2 presentation/gameplay scaffold: procedural drome/props/skaters and
  the UBlade2 AI/rules. It is **not yet the exact original look or simulation** and is not the target release.
- The exact data importer (14 teams, 14 dromes, 140/464 objects) and 209-check simulation core exist in UBlade3.
- The original asset importer is in progress (`UBlade3.Assets`); the formats are WBM models (MDM1/LZSS), WBP/WBA
  animations, BMP textures and WAV/ADLIB audio.
- The 20XX process bridge remains in the Port Lab only as a development/reference comparison; it must be removed
  from the final native distribution.

## Target architecture

    data/*.HTM, *.ENV, *.CFG       -> UBlade3.Data        (exact parsers)
    models/*.WBM, animdata/*.WBA   -> UBlade3.Assets      (exact asset readers)
    textures/*.BMP, sounds/*.WAV   -> UBlade3.Assets      (exact texture/audio readers)
    planvm/*.PBN                   -> UBlade3.PlanVM      (ADLIB bytecode VM + plan objects)
    original rules/physics         -> UBlade3.Sim         (exact 30 Hz simulation)
    Unity scene assembly/render    -> UBlade3.Native      (drome, skaters, camera, HUD, audio)

The Unity renderer constructs meshes from the original data at runtime (or from generated, non-committed import
caches). No original assets are committed; the player resolves `HYPERBLADE_GAME_DATA` or the StreamingAssets path.

## Milestones

| Milestone | Work | Acceptance |
| --- | --- | --- |
| N0 Native host | Unity Windows player with a match loop, no external runtime | `UBLADE3_NATIVE_BUILD_OK`; native match capture |
| N1 Asset readers | exact WBM/MDM1, WBP/WBA, BMP, WAV readers and AssetProbe | all shipped models/animations/textures parse with counts/digest |
| N2 Exact drome render | original drome meshes, placements, textures, lighting and goals | captured drome matches original reference frames; original object count |
| N3 Exact skaters | original skater meshes, materials, skeletons and animations | skaters animate with original clips; no procedural stand-ins |
| N4 Exact simulation wiring | UBlade3.Sim drives skaters/rok/camera/input; goals/periods/multiplier | native match replay parity with documented behaviour |
| N5 Plan VM/AI | port ADLIB VM/PBN, behaviours, interactors, collision bubbles, damage | AI matches play; no UBlade2 AI fallback |
| N6 Audio/UI | original BMP HUD/fonts, WAV/adlib audio, menus | original HUD/menu look, audio present |
| N7 Native release | package only Unity native player + user data; remove 20XX | extracted release plays exact native match; CI green |

## Immediate priority

1. Finish `UBlade3.Assets` (WBM/animations/textures) and its probe.
2. Build a Unity preview scene that loads one original drome's WBM set and textures and renders it with the original
   flat/gouraud path and camera. Do not replace the whole game until the drome preview is visually verified.
3. Replace UBlade2's procedural drome/players with the original assets one layer at a time; keep a fallback only for
   development builds, never as an exactness claim.
4. Move the native match loop from UBlade2's rules/AI to UBlade3.Sim, then to the plan VM.
5. Remove the external 20XX bridge from the playable scene/release when the native match reaches release quality.

## Exactness and editable sources

- C# namespaces: `UBlade3.Data`, `UBlade3.Assets`, `UBlade3.Sim`, `UBlade3.PlanVM`, `UBlade3.Native`, `UBlade3.UI`.
- Every loader/renderer component cites the decomp file/function/address it implements.
- Keep a parity harness against `hyperblade-20XX` and the original data; a native subsystem is only 'exact' when its
  output/behaviour is compared, not because it is in C#.
- The UBlade2-derived code under `Assets/Legacy` is a **temporary native scaffold**; it must be progressively
  replaced by exact components and its fallback paths labelled. It is not committed as the final engine.

## Build gates

- `scripts/ublade3_native.ps1 -Mode build|capture|release` (to be added) builds `build-native/UBlade3Native.exe`.
- AssetProbe, native match capture, drome/skater comparison captures, and a no-20XX process check.
- The final release contains no `hyperblade_20XX.exe`, no 20XX DLL and no external process call.

## Risks

- WBM/WBA formats are the largest unknown; the asset importer is the critical path.
- The plan VM is a full bytecode system; AI can be missing until N5 but then the match is not release-ready.
- Original assets are proprietary; only user-supplied data at runtime is allowed.
- UBlade2 code may pull in approximate rules; replace, don't polish, when the exact C# system is ready.
