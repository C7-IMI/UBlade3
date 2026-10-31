# Skater preview - original WBM model, skeleton and WBA animation in Unity

Status: N3 original-skater milestone. One original HyperBlade skater (CHIOMX, the Chicago
"The Machine" home side's human controlled Dromer "Evans") and that team's goalie (CHIGMX,
"Viveros") are loaded from the player's own retail install, posed with a reconstruction of the
shipped 16 bone skeleton and animation player, and rendered with the original WBM geometry, UVs
and 24 bit BMP textures. Nothing is procedural, no UBlade2 asset is used, and no original data is
copied into the repository.

This is a validation build of the C# renderer track, not the playable renderer: the exact
engine/native plugin path, the match camera, HUD, rules and plan VM are not part of this preview.

## What was verified

Command (Unity 6000.6.7f1, batch mode **without** -nographics because the captures need a
graphics device; this is the same command shape the N2 drome preview used):

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3SkaterPreview.CaptureAll \
      -logFile <temp>\ublade3-skater-preview-capture.log

Result: exit 0, `UBLADE3_SKATER_PREVIEW_CAPTURES_OK`, probe `mismatches=0`, preview digest
`00f075fe626fe291`. The command runs the scene-free probe first and then builds the preview
in an empty in-memory scene and captures six 1280x720 frames. No scene asset, prefab or copied
asset is written; the only written files are the six PNGs under `artifacts/`.

Probe-only command (no graphics device needed):

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3SkaterPreview.Check \
      -logFile <temp>\ublade3-skater-preview-check.log

Result: exit 0, `UBLADE3_SKATER_PREVIEW_PROBE_PASSED`, `UBLADE3_SKATER_PREVIEW_CHECK mismatches=0`,
digest `00f075fe626fe291`. Two fresh probe runs print the same digest.

Captures (all 1280x720, read back after the run):

| File | Character | Playback time | Frame | Size |
| --- | --- | --- | --- | --- |
| `artifacts/skater-preview-chiomx-frame000.png` | CHIOMX skater | 0.000 s | AA_Skate_Std frame 0 | 143,846 B |
| `artifacts/skater-preview-chiomx-frame009.png` | CHIOMX skater | 0.300 s | frame 9 | 124,006 B |
| `artifacts/skater-preview-chiomx-frame018.png` | CHIOMX skater | 0.600 s | frame 18 | 127,566 B |
| `artifacts/skater-preview-chigmx-frame000.png` | CHIGMX goalie | 0.000 s | AA_Goalidle frame 0 | 105,236 B |
| `artifacts/skater-preview-chigmx-frame040.png` | CHIGMX goalie | 1.333 s | AA_Goalidle frame 40 | 115,192 B |
| `artifacts/skater-preview-lineup-frame009.png` | both | 0.300 s | skater 9 / goalie 9 | 160,478 B |

The read-back frames show the blue/yellow Chicago skater and the goalie (chest logo, helmet, pads,
stick/hand plates) standing in the animation poses, with the original texture atlas faces,
torso logos and armour plates. The three skater frames are visibly different poses; the goalie
frames are the stand-ready pose. The `UBLADE3_SKATER_PREVIEW_MOVED` line quantifies it: the
largest corner displacement between skater frames 0 and 1 is 1.5029 model units.

## 1. Character and animation mapping

The mapping is read from the shipped data, not guessed:

1. `chico.htm` (Data root) declares `DefineTeam name=The_Machine ... environment=chidro.env`
   and five `DefineObject type=Dromer` blocks. The first, "Evans", is
   `modfile=CHIOMX` and carries the `SetPlans Human ...` line, i.e. the home side's
   human controlled skater. The third, "Viveros", is `modfile=CHIGMX UseMyPallette` and holds
   the file's `SetSpecialGoalieRatings` block, i.e. the goalie. The fourth and fifth
   ("Leppek" CHISDM, "Coffland" CHISOM) are the two reserves.
2. `GameEngine::LoadTeamFromFile` (HYPERX 0x004858b0, gameengine.cpp lines 3467-3651) walks the
   ten team slots in file order: slots 0-4 are Dromers, 5-8 body parts, 9 the team's Rok. The first
   three Dromers are created on the ice (`TEAM_PLAYERS_ON_DROME` 3, lines 3463/3551); the
   fourth and fifth become reserves (`GameEngine::SubstitutePlayer`, HYPERX 0x00485b90, uses
   `nDefIndex` 0, 1, 3, 4 and never index 2 - the goalie). The Dromer object ids start at
   `nRunningId = nTeam` (0 for the home side) and step by two per Dromer (lines 3493, 3543),
   so home object ids are 0 (skater), 2, 4 (goalie): `(id & 1) == 0` and
   `EngineObject::InitModel` (HYPERX 0x00470790) does **not** team-remap the palette. The away
   side forces the remap (`m_bForceRemap = 1`, lines 3546-3549).
3. `Data\Models\Mdltxt\chiomx.wbm` and `chigmx.wbm` are the textured set models the
   Direct3D path draws (the retail game's normal path). `Mdlshd` carries the same rigs
   (16 bones each; verified by the probe's `UBLADE3_SKATER_PREVIEW_MODELSET` line).
4. The animation is chosen by name from `Data\AnimData\AnimAssm.txt`, the script
   `AnimScriptTable::ParseScript` (HYPERX 0x00418210) loads at start-up:
   * the skater's persistent skate is `AA_Skate_Std` - the name
     `BehParam::SetPersistentAnimation` (HYPERX 0x0041cb20) resolves for a skating player and
     `GameBehaviorObject::UpdateLimpAnim` (HYPERX 0x0041c220) swaps for the limp variant when
     hurt. Its segment 0 is `azkskat2` "normal skate", frames 0..26, standard rate 30 fps,
     loop count 0 (AnimPlayer's "loops forever").
   * the goalie's stand is `AA_Goalidle`; its segment 0 is `aplread2` "stand ready",
     frames 0..80, 30 fps, loop count 1.

The preview loads `AnimAssm.txt` through the same `UBlade3.Assets.AnimationAssemblyReader`
the data layer already verified (249 assemblies / 388 segments), resolves the assembly by name,
takes segment 0 (the clip `AnimPlayer::SetClip` starts at), and loads the named
`Data\AnimData\<file>.wba` with `WbaReader`. A missing assembly, a missing .wba, an
out-of-range segment or a clip format the shipped loader cannot transform is fatal, never
substituted.

`animate.lst` and `model.lst` were checked as catalogues and are deliberately not used: both ship
on the disc, but no shipped executable reads either (`docs/ASSET-FORMATS.md` sections 10.1-10.2)
and their set semantics cannot be reconstructed. The name mapping this preview uses is the HTM
team files (which Dromers exist and which WBM each `modfile=` names) plus `AnimAssm.txt` (which
.wba each animation assembly segment names); neither catalogue is needed for CHIOMX/CHIGMX or
for AA_Skate_Std/AA_Goalidle.

## 2. Model set and measured counts

Mdltxt LOD 0, measured by the probe on this disc:

| Measure | CHIOMX skater | CHIGMX goalie |
| --- | --- | --- |
| LOD levels | 2 | 2 |
| Bones / component slots | 16 | 16 |
| Mesh blocks in LOD 0 (root slot has no geometry) | 15 | 15 |
| Mesh materials | 14 | 13 |
| Texture names referenced | 13 | 12 |
| Source vertices | 224 | 153 |
| Source faces | 195 | 96 |
| Built triangles (fan) | 314 | 165 |
| Built corners (flat shaded) | 942 | 495 |
| Sub-meshes (texture groups) | 14 (13 textures + 1 no-UV group) | 12 |
| Two sided faces | 0 | 0 |
| Resolved textures | 13 | 12 |
| Missing references | 0 | 0 |

Skeleton reference (all 16 bones, rest translations in model units; per-bone lines are
`UBLADE3_SKATER_PREVIEW_BONE` in the run log):

| slot | parent | children | rest (x, y, z) | role |
| --- | --- | --- | --- | --- |
| 0 | - | 1 | (0, 3.9484, 0) | rig root (translated by the format 1 clip, scaled by 0.0035) |
| 1 | 0 | 3 | (0, 0, 0) | pelvis / spine chain root for legs (2..4), torso (8), arms (10..15) |
| 2 | 1 | 1 | (0.6476, -0.5734, 0.1575) | hip |
| 3 | 2 | 1 | (0, -1.3173, 0) | knee |
| 4 | 3 | 0 | (0, -1.5603, 0) | foot |
| 5..7 | mirror of 2..4 | | | |
| 8 | 1 | 3 | (0, 0, 0) | chest |
| 9 | 8 | 0 | (0, 2.0338, 0) | head |
| 10 | 8 | 1 | (0.6667, 1.3695, 0) | shoulder |
| 11 | 10 | 1 | (0.9605, 0, 0) | elbow |
| 12 | 11 | 0 | (1.1089, 0, 0) | hand |
| 13..15 | mirror of 10..12 | | | goalie shoulder rest is 1.1667/1.5195 |

Every bone's MeshBoneDesc scale is (0.0035, 0.0035, 0.0035). The model header angle is
(90, 0, 180) degrees for both characters (`EngineObject::ApplyHeaderRotation` composes it
Z then X then Y; on this rig model +Y is height and becomes the world's +Z up).

## 3. Skeleton and pose reconstruction

The pose pipeline reproduces the shipped functions:

1. `MeshBoneDesc` (docs/ASSET-FORMATS.md section 5.4): `rest` translation,
   `scale` and the child slot ids. `SkaterRig` validates one root at slot 0,
   `linkId == slot` and a single parent per child.
2. `EngineObject::ApplyHeaderRotation` (HYPERX 0x00472620): the model component's angle, in
   radians (the loader converted degrees with g_flDegreesToRadians), applied Z then X then Y into
   the identity transform (`m_transformA`).
3. `MeshBone::BindToLODMesh` (HYPERX 0x0046fba0) sets every bone's local matrix to
   `T(rest)` with an identity rotation. This is the bind pose the preview exposes as
   `SkaterRig.BindPose`.
4. `AnimPlayer::UpdateFrame` (HYPERX 0x00424880) writes one clip's transform per bone into
   `m_local` through `AnimClip::GetBoneTransform` (HYPERX 0x00416800; the first
   frame after a clip change uses `GetBoneTransformFull`, HYPERX 0x00416930). For the
   shipped skater clips:
   * clip 0 is format 1 (translation only, one Vec3 per frame): it replaces the local translation
     with the frame's translation and leaves the bind rotation;
   * clips 1..15 are format 2 (one stored translation + Euler angles per frame): they write the
     Euler rotation into the local 3x3 and **do not** write the translation.
5. The one non-obvious rule: `GetBoneTransform` answers 1 for format 1 and 0 for format 2,
   and `AnimPlayer::UpdateFrame` only sets `SLOT_FLAG_MOVED` (=
   `MESHBONE_ANIMATION_DIRTY`, 0x04) when the answer is 1.
   `MeshBone::PropagateTransforms` (HYPERX 0x0046ef10) applies `MeshBoneDesc.Scale`
   to the local translation **only** while that flag is set. So on the skater rigs the root's
   animated translation is converted by the 0.0035 scale (a frame 0 root translation of
   (199.62, 1029.7444, 0) becomes (0.6987, 3.6041, 0) - the character's hip height), while every
   format 2 bone keeps its rest translation unconverted. Applying the scale to the rest offsets
   would collapse the skeleton to about a hundredth of its size; the flag semantics are why the
   shipped game does not. This is implemented in `SkaterRig.BuildBoneLocal`.
6. The Euler rotation is built exactly as `Anim::EulerToMatrix` (HYPERX 0x00416ba0) does:
   the axis letters are read from `acAxisOrder` with the last letter applied first, the
   degrees are converted with 0.017453292f, the rotation is staged with the same TransformObject
   in-place formulas, and then the nine cells are quantised into the 16 bit
   `PackedRotation` words `Anim::UnpackFixed16ToFloat` (HYPERX 0x00416cf0) reads
   back. The preview therefore uses the same quantised matrix the shipped renderer used, not the
   raw float one (`SkaterRig.EulerToMatrix`, `PackFixed16`,
   `UnpackFixed16`).
7. World matrices: `world[root] = objectTransform * local[root]`, then
   `world[child] = world[parent] * local[child]`
   (`MeshBone::PropagateTransforms`). The object transform is
   `EngineObject::UpdateRenderSlotTransform`'s `m_transformB * m_transformA` +
   `PhysicsBody::pos`; the preview uses the header rotation plus a preview-only yaw and
   keeps `m_bHasScale` false (only `Object::ScaleBones`, used by a
   cheat/effect, sets it).
8. Vertices: every mesh block's vertices are in its bone's local space. The preview stores one
   flat-shaded corner per triangle vertex with its bone slot and transforms it by that bone's
   world matrix on the CPU; the original `MeshBone::DrawFaces` (HYPERX 0x0046f060) does
   the same per vertex, so the geometry is identical, not an approximation. Model space (Z up) is
   converted to Unity (Y up) with `(x, y, z) -> (x, z, -y)`, the proper rotation used by
   the verified N2 drome preview (winding preserved).
9. Faces are fanned `(0, n, n-1), (0, n-1, n-2), ...` and two sided materials get the
   reversed fan with the back material, exactly the 20XX hardware builder
   (`third_party/hyperblade-20XX/src/graphics/hwmesh.cpp` `HwMesh::Build`) and
   `D3DRend::DrawFace` (HYPERX 0x004328c0) rules.

A Python re-derivation of the same matrices (independent of the C#) was used while settling the
pipeline: the posed skeleton is contiguous (the largest gap between any parent/child limb block's
posed bounding boxes is 0.12 model units, at one goalie shoulder; every other chain is 0), feet
stay on the ground line and the pose changes every frame. These are the checks the probe now runs.

## 4. Animation playback and frame timing

`AnimPlayer::SetClip` (HYPERX 0x00425220) starts a clip at its assembly segment's start
frame, sets the end frame from the segment and steps
`m_state.flStep = clock delta * standardFps * playbackRate`
(`AnimPlayer::UpdateFrame` reads `int(position)`;
`CheckLoopPoint`, HYPERX 0x00424770, wraps to the start frame once the frame has reached
the segment end). The preview's playback clock is therefore
`frame = start + int(elapsed * standardFps * rate) mod displayedFrames` with rate 1.0
(`SkaterAnimationClip.FrameAtSeconds`). `SkaterPreviewScene.SampleTime`/`Update`
is that clock; the Editor capture steps the same clock to each capture time, so the captured frame
is the frame the shipped player would show at that elapsed time. The probe checks the mapping for
every frame of both clips plus the wrap:

    UBLADE3_SKATER_PREVIEW_TIMING role=skater standardFps=30 span=27 start=0 end=26 duration=0.9 samples="0:0 1:1 2:2 3:3 4:4 5:5 26:26 27:0"
    UBLADE3_SKATER_PREVIEW_TIMING role=goalie standardFps=30 span=81 start=0 end=80 duration=2.7 samples="0:0 1:1 2:2 3:3 4:4 5:5 80:80 81:0"

The capture times are whole frames at those rates: 0.0 s -> frame 0, 0.3 s -> frame 9, 0.6 s ->
frame 18 for the skate (`azkskat2`); 40/30 s -> frame 40 for the goalie
(`aplread2`).

Not reproduced: the engine's first-frame blend
(`AnimPlayer::ComputeScale`/`Advance`, HYPERX 0x00424b40 / 0x00424e60, capped at
0.35 s), per-plan playback rates and clip selection, and the animation events/triggers that switch
clips. The preview plays one named assembly at rate 1.0 from its first segment, which is the
default shipped behaviour a plan gets when it does not set a rate.

## 5. Materials, UVs and lighting

* Materials: `EngineObject::InitModel`'s Direct3D conversion. The material's low palette
  byte goes through `ColorPalette::GetRemappedEntry`; home-side object ids are even, so
  `bRemap` is false and the base palette row is used (an away-side preview would pass
  `SkaterPreviewOptions.RemapPalette = true`).
* UVs: `MeshBlock.Coords` indexed by `MeshFace.UVIndices` when the face carries
  flag 0x20; the stored `ScaledWidth` is a software-renderer rescale value and is not
  applied (docs/ASSET-FORMATS.md section 13).
* Textures: the model's one-based texture-name table resolves each material to a
  `Data\Textures\<name>.bmp`; all 25 distinct textures of the two characters resolve, load
  with `BmpReader`, and upload from `RgbaBottomUp` (bottom-left origin, Unity's
  convention), point filtered, clamped. The preview reuses the N2 `UBlade3/DromePreview`
  shader: unlit `texture * vertex colour`, which is exactly
  `D3DRend::DrawFace`'s MODULATE.
* Per face lighting: `MeshMaterial::CalcLightIntensity` (HYPERX 0x00433820) with
  `g_lightDirs[0]` (the 303 degree light `MeshBone::DrawFaces` uses on ordinary
  frames; `DromeMeshBuilder.LightDirectionWorld`). Textured faces use the light's grey
  level as the vertex colour; untextured faces (no UVs or no texture) use the material's converted
  0xAARRGGBB scaled by the level. A face whose material has no texture but whose face has UVs is
  drawn untextured, as `D3DRend::DrawFace`'s textured-branch condition does.
* Sub-mesh / texture mapping measured for the skater (triangle counts per texture):

      0:chihip/45   1:chithig/20  2:(no texture)/7  3:chiknei/10  4:chikneo/10
      5:chiankl/18  6:chitorb/71  7:chitorf/48      8:chihead/19 9:chiface/18
      10:chishou/16 11:chielb/12  12:chilhnd/6      13:chirhnd/14

  `chitorb`/`chitorf` are the torso back/front atlas faces (the chest logo is on
  the front face), `chiface`/`chihead` the head, `chiankl` the ankle,
  `chiknei`/`chikneo` the knee, `chishou` the shoulder,
  `chielb` the elbow, `chilhnd`/`chirhnd` the hands,
  `chithig` the thigh and `chihip` the hip.

## 6. New code

All runtime files are under `unity/UBlade3/Assets/Scripts/UBlade3/Native/`:

| File | Public surface |
| --- | --- |
| `SkaterRig.cs` | `SkaterBone`, `SkaterRig` (`Load`, `FromModel`, `PoseFrame`, `BindPose`, `EulerToMatrix`, `PackFixed16`, `UnpackFixed16`, `TransformPoint`, `TransformDirection`, `BuildHeaderRotation`, `ObjectTransform`), `SkaterHash` (FNV-1a 64) |
| `SkaterAnimationClip.cs` | `SkaterAnimationClip` (`Load`, `LoadAssemblies`, `FrameAtSeconds`, `DisplayedFrameCount`, `DurationSeconds`, `Describe`): assembly -> segment 0 -> .wba |
| `SkaterMeshBuilder.cs` | `SkaterMeshData` (`Build`, `CreateMesh`, `Pose`, `ApplyTo`): materials, flat-shaded corners, per-frame CPU pose, mesh upload |
| `SkaterPreviewData.cs` | `SkaterPreviewOptions`, `SkaterCharacter` (pose, placement, pose digest), `SkaterPreviewData` (`Load`, `Describe`, `SampleFrames`, digest) |
| `SkaterPreviewScene.cs` | MonoBehaviour: build, layout, playback clock, camera framing, materials/textures |
| `SkaterPreviewProbe.cs` | scene-free `Run`/`Check`: counts, skeleton, animation mapping, timing, poses, digests, missing refs |

One Editor file, `unity/UBlade3/Assets/Editor/UBlade3SkaterPreview.cs`: menu entries and the
batch-mode `CaptureAll`, `Check` and capture-only entry points. The scene is built in
memory; nothing outside `artifacts/` is written. The probe is scene free and does not depend
on the capture path.

## 7. Probe output contract

`SkaterPreviewProbe.Run` prints, in order:

    UBLADE3_SKATER_PREVIEW_PROBE root=<Data folder> set=Mdltxt
    UBLADE3_SKATER_PREVIEW_DATA ...
    UBLADE3_SKATER_PREVIEW_OBJECT role=<skater|goalie> model=... set=... lod=... bones=... meshSlots=...
        materials=... submeshes=... textures=... missingTextures=... sourceVerts=... sourceFaces=...
        verts=... triangles=... twoSided=... skippedFaces=... warnings=...
    UBLADE3_SKATER_PREVIEW_ANIM role=... assembly=... wba=... clips=... frames=... range=... fps=... loop=...
    UBLADE3_SKATER_PREVIEW_SUMMARY ...
    UBLADE3_SKATER_PREVIEW_SKELETON / _BONE role=... index=... parent=... rest=... scale=...
    UBLADE3_SKATER_PREVIEW_SUBMESHES / _TEXTURES role=...
    UBLADE3_SKATER_PREVIEW_TIMING role=...
    UBLADE3_SKATER_PREVIEW_FRAME role=... frame=... boundsMin=... boundsMax=... poseDigest=...
    UBLADE3_SKATER_PREVIEW_POSE role=... root=... head=... leftHand=... rightHand=...
    UBLADE3_SKATER_PREVIEW_MOVED role=... maxVertexDelta=...
    UBLADE3_SKATER_PREVIEW_MODELSET role=... set=Mdlshd path=... bones=...
    UBLADE3_SKATER_PREVIEW_COUNTS models=2 meshes=2 bones=32 clips=32 displayedFrames=108
        triangles=479 verts=1437 textures=25 missingRefs=0 digest=00f075fe626fe291
    UBLADE3_SKATER_PREVIEW_COUNTS_OK mismatches=0
    UBLADE3_SKATER_PREVIEW_CHECK mismatches=0 digest=00f075fe626fe291
    UBLADE3_SKATER_PREVIEW_PROBE_OK

A failure prints `UBLADE3_SKATER_PREVIEW_MISMATCH <reason>` and fails the run; a missing
install, model, animation assembly or texture is fatal, never substituted.

The digest is FNV-1a 64 over: the data root, model set, both characters' model paths, bone
rest/scale/child data, material records, clip formats/axis orders/sizes, segment range and rate,
texture names/paths, every corner's base position/UV/bone/material/normal/reversed/textured flags,
and four posed frames per character (positions, normals, colours). It is stable across runs on the
same install: the probe and the capture run printed `00f075fe626fe291` for every log line of
both.

## 8. Known gaps and unresolved points

1. **No GPU skinning yet.** The mesh is posed on the CPU from the exact original bone matrices and
   re-uploaded per frame; a `SkinnedMeshRenderer` with a 16 bone palette is the next step.
   The geometry, UVs and materials will not change.
2. **No blending, plan rates or triggers.** The preview plays one assembly at rate 1.0; the
   engine's 0.35 s capped first-frame blend, per-plan playback rates and
   `id_MSG_SyncFrame` clip transitions are not implemented.
3. **Only the textured (Mdltxt) set is drawn.** The Mdlshd copies load and their rigs are verified,
   but the shaded/palette-ramp path (`SWRend::DrawFace`) is not wired into the skater
   renderer.
4. **Home palette only.** The preview uses the unremapped palette row (home-side even object id).
   The away-side remap (`bRemap` true) is implemented in the data path but not captured.
5. **No reference frame comparison gate.** The capture poses are read back and checked for the
   expected model, textures and pose change, but there is no per-pixel comparison against the 1996
   executable or a 20XX reference frame yet. The acceptance for this milestone is structure and
   provenance, not pixel parity.
6. **Euler trig.** The original computed the matrix with the VC6 CRT's cos/sin; C# uses
   Math.Cos/Math.Sin. Any last-ULP difference is then quantised by the original's own 16 bit pack,
   so the stored joint rotations agree with the shipped runtime except at a quantisation boundary.
   This is the only numeric step not byte-proven.
7. **Object/effect scale.** `Object::ScaleBones` (HYPERX 0x004499d0, `m_bHasScale`)
   is not modelled; it is only used by a cheat/effect, not by match rendering.
8. **Shadow/attachment blocks.** `ModelComponent.Shadow` and the collision
   `DromerModelBubble` data are exposed but not drawn or used; there is no ground shadow in
   the preview.
9. **The full playable scene is not changed.** This preview is the C# renderer track; the released
   game still runs the exact embedded engine/native path. No release, player build or scene asset
   was produced for N3.

## 9. Legal

The original game data is read from the user's own HyperBlade install
(D:\Games\HyperBlade\HYB locally) and is never copied into this repository.
`hyperblade-decomp` and `hyperblade-20XX` are read-only references for format
research; the C# here is written from that research.
