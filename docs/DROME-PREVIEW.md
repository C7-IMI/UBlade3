# Drome preview - original assets in the C# renderer

Status: N2 preview milestone. This renders the original 1996 chidro drome with the original WBM
geometry and BMP textures from the user's own install. It is not yet the complete match renderer
and the preview lighting/camera is not claimed to be pixel-identical to the shipped game.

## What was verified

Command:

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3DromePreview.CaptureAll \
      -logFile <temp>\ublade3-drome-preview.log

Result: `UBLADE3_DROME_PREVIEW_CAPTURES_OK`; 27 original objects loaded from `CHIDRO.ENV`, all
from the `Mdltxt` set, including the CHIDRO drome (775 source vertices / 682 faces -> 3,216 Unity
vertices / 1,072 triangles / 6 submeshes), Rokmix, goals, ramps, multipliers, turnstiles, start gun,
hurdles, limbo poles, randomizers, mine, swarf, explosion and the drome props. The capture and
probe paths are:

- `artifacts/drome-preview-chidro-shaded.png` (1280x720)
- `artifacts/drome-preview-chidro-textured.png` (1280x720)
- `Assets/Scenes/DromePreview.unity`
- `Assets/Editor/UBlade3DromePreview.cs` (CreateScene/CaptureAll/Check/BuildWindows/...)

The object log names the model, model set, slot, source vertex/face counts and generated Unity
counts. No UBlade2 asset or procedural drome substitute is used; the loader resolves the player's
own `Data\Models\Mdltxt` / `Data\Textures` through `OriginalAssetRoot` (case-insensitive,
`HYPERBLADE_GAME_DATA` or StreamingAssets path file).

## Known gaps before this is the exact match renderer

- The preview frame is still lit/framed as a validation view, not yet the original camera/lighting.
  Textured output shows the drome shell and props but the flat shaded look and texture response
  need comparison against the original reference and 20XX oracle.
- Only the main camera views and the static drome are present; skaters, rok input, animation,
  HUD, plan VM wiring and the game rules are not in this preview.
- The C# renderer does not yet replace the native plugin in the playable release. The playable
  release remains the embedded exact engine (see README); this preview is the C# renderer track.
- Exact per-face material/winding, original texture palettes and camera projection need a
  pixel-comparison gate; the current probe validates loading and mesh generation, not pixel parity.

## Implementation notes

- `OriginalAssetRoot`: data/model/texture folders and case resolution.
- `DromeSceneLoader` / `DromeMeshBuilder`: DromeFile -> WbmReader -> Unity Mesh; object placement,
  LOD/slot selection, source counts.
- `DromePreviewScene`: scene/assembly/camera/lighting for the validation captures.
- `DromePreviewProbe`: scene-free object/mesh/missing-texture checks and
  `UBLADE3_DROME_PREVIEW_*` log lines.
- `DromePreview.shader` and `DromePreviewMaterial.mat`: the current preview material; not the final
  exact shading contract.
- `UBlade3DromePreview.cs`: editor create/capture/build entry points.
- Sources: `UBlade3.Data.DromeFile/ObjectDefinition`, `UBlade3.Assets.WbmReader/WbmModel/MeshBlock`,
  `BmpReader`, and the decomp/20XX citations already recorded in `docs/ASSET-FORMATS.md`.

## Next gate

Replace the validation lighting/camera with the original camera/lighting contract, compare each
object/frame against the 20XX reference, and add skaters/animation before this renderer can replace
the native plugin in a release.
