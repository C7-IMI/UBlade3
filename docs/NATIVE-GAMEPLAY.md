# Native gameplay - original ROK, goal scoring and original-style HUD

Status: N5 gameplay follow-up on the N4 native match. The match scene now renders the drome's own
ROK model (CHIDRO.ENV's `type=Rok name=AndRoll modfile=Rokmix`) from the player's WBM data,
detects goals through the existing exact `UBlade3.Sim` goal rules, keeps the home/away score with
`GameEngine::IncrementHomeScore`/`IncrementAwayScore`, and draws an original-style HUD from the
shipped HUD art (`Dataassets8`) with a clearly labelled interim text font. A deterministic test
produces a real score change (2-1) and is captured from both the Editor and a built Windows player.
The plan VM/AI fallback and the N4 match behaviour are unchanged; the existing MatchScene and
existing match files were not edited.

The HUD is a hybrid: its frames and team logos are the original shipped bitmaps, but the text face
is a substitute because the original HUD text was Windows GDI "MS Sans Serif" (not shipped art).
The ROK model's `Mdltxt` materials name no BMP textures (it is a palette-material model), so no
texture files are involved for the ROK; this is stated again in section 1 and section 8.

## What was verified

Unity 6000.6.7f1, local retail install `D:GamesHyperBladeHYB`; no 20XX process or native plugin
is used. All three verification paths below ran on 2026-10-31.

### 1. Headless check (no graphics device)

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.Check \
      -logFile <temp>\ublade3-gameplay-check-final2.log

Result: exit 0, `UBLADE3_NATIVE_GAMEPLAY_CHECK_OK`, mismatches=0:

    UBLADE3_NATIVE_GAMEPLAY_CHECK_RENDER_BONES ok=True mapping=plain=0 double=7 quad=8 kill=2
    UBLADE3_NATIVE_GAMEPLAY_CHECK_GOAL side=0 valid=True points=2 goalSlot=8 scoreCode=1 home=2 away=0 renderBone=7 tickMs=233
    UBLADE3_NATIVE_GAMEPLAY_CHECK_GOAL side=1 valid=True points=1 goalSlot=7 scoreCode=0 home=2 away=1 renderBone=0 tickMs=833
    UBLADE3_NATIVE_GAMEPLAY_CHECK_HUD home="THE MACHINE" away="THE FURY" scoreHome=2 scoreAway=1 period=1 clock=4:59 rok=x0
    UBLADE3_NATIVE_GAMEPLAY_CHECK_CARRIER ok=True carried=True distance=0
    UBLADE3_NATIVE_GAMEPLAY_CHECK_NO_20XX running20xxProcesses=0 no20xx=True
    UBLADE3_NATIVE_GAMEPLAY_CHECK_COUNTS rokTriangles=257 rokBones=9 hudSprites=5 homeScore=2 awayScore=1 goals=2 mismatches=0

The check loads the drome, both teams, the ROK model and the HUD art, runs a home goal (ROK value 2)
and an away goal (ROK value 1) through the exact goal rules, proves the 400 ms debounce blocks a
second count from a short dwell, checks the render-bone mapping and the carried-ROK follow path,
and checks that no 20XX process runs. A fresh run prints the same results.

### 2. Editor batch-mode capture (graphics device required - run WITHOUT `-nographics`)

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.CaptureAll \
      -logFile <temp>\ublade3-gameplay-capture5.log

Result: exit 0, seven 1280x720 frames, score 2-1:

    UBLADE3_NATIVE_GAMEPLAY_GOAL side=home points=2 goalSlot=8 scoreCode=1 rokValue=2 carried=False atSpawnSlot=False renderBone=7 tickMs=5233 homeScore=2 awayScore=0 rokPos=(0,-184,-11.5)
    UBLADE3_NATIVE_GAMEPLAY_GOAL side=away points=1 goalSlot=7 scoreCode=0 rokValue=1 carried=False atSpawnSlot=False renderBone=0 tickMs=5799 homeScore=2 awayScore=1 rokPos=(0,184,-11.5)
    UBLADE3_NATIVE_GAMEPLAY_SUMMARY goals=2 homeGoal=(2) awayGoal=(1) homeScore=2 awayScore=1 debounceExtra=0 carrierFollow=ok carrierDistance=0 renderBones=plain=0 double=7 quad=8 kill=2 hud="home="THE MACHINE" away="THE FURY" scoreHome=2 scoreAway=1 period=1 clock=4:54 rok=x0" no20xxProcesses=0 rokTriangles=257 rokRenderBone=0
    UBLADE3_NATIVE_GAMEPLAY_CAPTURES_OK frames=7 width=1280 height=720 score=2-1

### 3. Windows player build and `--gameplay-capture` run

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.BuildAndRunWindows \
      -logFile <temp>\ublade3-gameplay-player-build2.log

Result: exit 0; `UBLADE3_NATIVE_GAMEPLAY_PLAYER_BUILD_OK bytes=103955156`, the player ran with
`-batchmode -screen-width 1280 -screen-height 720 -screen-fullscreen 0 --gameplay-capture=...`
and wrote seven frames, `UBLADE3_NATIVE_GAMEPLAY_PLAYER_CAPTURES_OK frames=7 score=2-1`.
The player frames are byte-identical to the Editor frames (same SHA-256 for all seven).
The generated `build-match/` player directory was removed after the evidence was read; the
command above rebuilds it.

### 4. Existing match regression (preserve N4 behaviour)

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3Match.Check \
      -logFile <temp>\ublade3-match-regression-check.log

Result: exit 0, unchanged: `UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd`,
`UBLADE3_NATIVE_MATCH_CHECK_OK`, `UBLADE3_NATIVE_MATCH_PROBE_PASSED`. The N4 match scene, capture
path and probe are untouched by this layer.

### Captures (all 1280x720, read back after the run)

| File | Tick | Sim time | Score | Period/clock | ROK render bone | ROK position (original x,y,z) | Bytes | SHA-256 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `artifacts/native-gameplay-00-match-5s.png` | 150 | 5.000 s | 0-0 | P1 4:55 | 0 (plain) | (0, 0, -105) | 90,377 | `87E0D9E0...A9172F` |
| `artifacts/native-gameplay-01-goal-home-2.png` | 157 | 5.233 s | 2-0 | P1 4:54 | 7 (double) | (0, -184, -11.5) | 85,647 | `00C45800...E648CD` |
| `artifacts/native-gameplay-02-rok-plain.png` | 167 | 5.567 s | 2-0 | P1 4:54 | 0 (plain) | (0, 0, -105) | 21,903 | `5FB2A112...5ADA9EB` |
| `artifacts/native-gameplay-03-rok-double.png` | 167 | 5.567 s | 2-0 | P1 4:54 | 7 (double) | (0, 0, -105) | 22,080 | `036EECEC...9C42EB` |
| `artifacts/native-gameplay-04-rok-quad.png` | 167 | 5.567 s | 2-0 | P1 4:54 | 8 (quad) | (0, 0, -105) | 21,921 | `22D0ADB8...73768F` |
| `artifacts/native-gameplay-05-goal-away-1.png` | 174 | 5.800 s | 2-1 | P1 4:54 | 0 (plain) | (0, 184, -11.5) | 82,735 | `40CBAB7D...BB027F` |
| `artifacts/native-gameplay-06-scoreboard-2-1.png` | 179 | 5.967 s | 2-1 | P1 4:54 | 0 (plain) | (0, 0, -105) | 66,514 | `E315B7CA...913DF3` |

Full hashes are in the run logs and in `artifacts/native-gameplay-player-*` (identical). What the
read-back frames show:

* 00: the N4 chase view of the Chicago skater with the HUD over the drome: period frame + clock +
  period lamps at the top centre, the original `frdamgA`/`frdamgB` team frames with the original
  `hudChi`/`hudsea` logo strips at the bottom left, team names, 0-0 scores and the ROK
  `x0` label.
* 01: the same chase view after the home goal; the HUD reads 2-0 and `x2`; the ROK is at the south
  goal mouth `(0, -184, -11.5)` (the chase camera is on the skater, so the ROK itself is in the
  next two frames).
* 02/03/04: close-ups of the original Rokmix model at its arena spawn point, drawn with render bone
  0 (plain: grey/white faces and yellow spikes), 7 (double: orange/red centre) and 8 (quad: yellow
  centre with orange spikes); the HUD shows the matching `x0`/`x2`/`x4` ROK label and 2-0.
* 05: the away goal (HUD 2-1, `x1`).
* 06: the final 2-1 scoreboard with the ROK back at its spawn point.
* No HUD element is procedurally substituted except the text glyphs (clearly labelled by the
  bottom-right "AI FALLBACK / INTERIM HUD FONT" line); the frames/logos are the original bitmaps.

## 1. The original ROK model

CHIDRO.ENV's own ROK definition is loaded, not a substitute:

    DefineObject type=Rok name=AndRoll modfile=Rokmix
    SetMotionParams pos = 0 0 0 accel=64 speed=70 azim=90 elev=0 fric=.175 elas=.37

The ROK data is read from that drome object through the verified WBM reader
(`SkaterRig.FromModel` + `SkaterMeshBuilder`, the same rig/mesh/material path the N3 skater preview
uses) and the drome palette. Measured by the headless check:

    UBLADE3_NATIVE_GAMEPLAY_ROK model=Rokmix name="AndRoll" set=Mdltxt lod=0 bones=9 meshSlots=9 materials=6 submeshes=1 triangles=257 verts=771 textures=0 missingTextures=0 warnings=0 boneTriangles=0:20,1:40,2:48,3:24,4:32,5:20,6:33,7:20,8:20

Note the `textures=0`: Rokmix's six `Mdltxt` materials name no BMP texture, so this model is drawn
from its WBM material records and the original CUSTOM.PAL colours through the same verified material
conversion and flat/gouraud face builder (`EngineObject::InitModel` HYPERX 0x00470790,
`MeshBone::DrawFaces` 0x0046f060 and 20XX `src/graphics/hwmesh.cpp HwMesh::Build`). No texture was
invented or substituted; there simply is none in the shipped model.

The mesh blocks are the render-slot variants. `RokObject::RokObject` (HYPERX 0x00473a80) hides every
mesh bone past the first and `RokObject::SetRenderSlotFlags` (0x00473b90) shows exactly one:

| State | Render bone | Triangles in that block |
| --- | --- | --- |
| plain ROK (abs value 0 or 1, or at the spawn slot) | 0 (`RokConstants.RenderBonePlain`) | 20 |
| double (abs value 2) | 7 (`RenderBoneDouble`) | 20 |
| quad (abs value 4) | 8 (`RenderBoneQuad`) | 20 |
| kill/carried state (kill state not -1) | 2 (`RenderBoneCarried`) | 48 |
| any uncovered value | -1 (all hidden) | - |

The scene reads `RokState.RenderBone` every tick and rebuilds the mesh's triangle lists for that
bone only; posing uses the rig's bind pose (the ROK has no animation player). The headless check
asserts the mapping matches `MultiplierRules.RenderSlotBone` exactly
(`UBLADE3_NATIVE_GAMEPLAY_CHECK_RENDER_BONES ok=True mapping=plain=0 double=7 quad=8 kill=2`);
capture frames 02/03/04 show the three visible variants.

Position comes from `RokState.Body.Pos` (bind position = the ENV `0 0 0` definition, snapped to the
arena shell with the same `CollisionEllipsoid::GetZ`/`NormalAtPoint` calls the loader uses - GetZ at
the centre is -105, so the ROK sits on the arena floor at the centre). Rotation: `RokState` has no
orientation field, so the presented yaw follows the body's velocity direction while it has speed and
holds the last facing at rest (the header rotation is still applied); the original render-slot
transform for a plain ROK is the static model header rotation, so this is a documented presentation
stand-in (see section 8). When the ROK is attached, `RokState.Body.Pos` is updated by the existing
`Simulation.UpdateCarriedRok` path (CBeAttached) and the view follows it; the carried follow is
checked headlessly (`CHECK_CARRIER ok=True carried=True distance=0`).

## 2. Goal detection and scoring

No rule is invented. The detection and scoring chain uses only the existing exact pieces:

* **Goal mouth entry**: `GoalFrame.Mouth` is the box `collisioncheck.cpp 0x004521d0` builds (inner
  goal width 7 = 9 - 2*1, height 5, plane half-depth 0.5; the goal-local axes are x across, y up and z
  through the plane, origin at the mouth centre (0, +/-184.5, -14)). `MatchGoalDetector.InsideGoalMouth`
  maps the ROK world position into that frame and tests the shipped box.
* **Which port**: `GoalRules.BouncedInGoal(north, south)`, the C# port of
  `CollisionCheck::BouncedInGoal` HYPERX 0x00469ef0 (1 = north, 2 = south).
* **Points**: `GoalRules.RokScoreValue(goalSlot, rowValue, carried, atSpawnSlot)`
  (`PlanCond::RokScoreValue` HYPERX 0x00465150) selects the shipped 1x/2x/4x/spawn result code; the
  points are the goal plan arms' counts (NGoal.PBN/SGoal.PBN send MSG_NGoalScored / MSG_SGoalScored
  once/twice/four times for the 1x/2x/4x arms; docs/CORE-NOTES.md section 7 and the ported
  `GoalRules.PointsForScoreValue`).
* **Side**: the shipped goal plans and InfoBar decide it, not a guess: NGoal.PBN/SGoal.PBN raise
  `id_ACF_IncrementScore`, `INSCRTBL.PBN` turns MSG_NGoalScored into `PlanAction::IncrementScore(0)`
  and MSG_SGoalScored into `IncrementScore(1)` (HYPERX 0x00464720), and `InfoBar::IncrementScore`
  (0x0047ae70) maps non-zero to `SCORE_LINE_TEAM1` = the home score and zero to
  `SCORE_LINE_TEAM2` = the away score (`ui/infobar.h:27-28`). So a **north goal scores for the away
  side, a south goal for the home side**, which also credits an own goal to the right team because the
  goal plan arm sends that goal's line regardless of who carried it.
* **Score state**: the existing `GameEngine::IncrementHomeScore/IncrementAwayScore` doubles in
  `MatchRules` are called once per point (the plan arms' repeated increments), and one
  `MSG_GOAL_SCORED` is recorded through `Simulation.BroadcastSceneEvent`.
* **Debounce**: `GoalConstants.ScoreDebounceMs` = 400 (`collisioncheck.cpp
  g_nGoalScoreDebounceMs`) is enforced by the detector; the fixture dwells inside the mouth for ten
  30 Hz ticks (333 ms) after scoring and `debounceExtra=0`.

The deterministic score fixture is explicit about what it replaces: the plan VM's shot physics
(`CProjectAndBounce`) is not ported, so the test places the ROK body on the mouth's mid-height line
and moves it through the goal line instead of firing a plan shot. Everything after the entry test -
goal port, score value, points, side, score bank and debounce - is the real shipped rule code.

Measured result (Editor capture; the headless check measures the same fixtures from a fresh sim):

    home goal: goalSlot=8 (SGoal) scoreCode=1 -> 2 points, side=home, renderBone=7, ROK value 2,
               tickMs=5233, rokPos=(0,-184,-11.5), homeScore=2 awayScore=0
    away goal: goalSlot=7 (NGoal) scoreCode=0 -> 1 point,  side=away, renderBone=0, ROK value 1,
               tickMs=5799, rokPos=(0,184,-11.5),  homeScore=2 awayScore=1
    final score 2-1; goals=2; debounceExtra=0; carrierFollow=ok

Scores start at 0 because `GameEngine::Reset` leaves them at -1 and the original InfoBar's
`IncrementScore(5)`/`IncrementScore(4)` move them to 0 when play begins; the scene writes that same
displayed start state (`SetHomeScore(0)/SetAwayScore(0)`) and the exact increments then apply.

## 3. The HUD

The HUD reuses the original shipped art and layout rules:

| Element | Shipped source | Measured |
| --- | --- | --- |
| period/clock frame | `Data\assets8\frper.bmp` (InfoBar::InfoBar LoadByName("frper"), infobar.cpp:982) | 83x60, 3,923 keyed pixels |
| home team frame | `frdamgA.bmp` (LoadByName("frdamgA"), infobar.cpp:1003) | 220x47, 8,364 keyed |
| away team frame | `frdamgB.bmp` (LoadByName("frdamgB"), infobar.cpp:1010) | 220x47, 8,451 keyed |
| home logo strip | `hudchi.bmp` (chico.htm idlogo=hudChi; HUD::SetHomeLogo hud.cpp:1125 SetFile(name,1)) | 73x21, 1,216 keyed |
| away logo strip | `hudsea.bmp` (seattle.htm idlogo=hudsea; HUD::SetAwayLogo hud.cpp:1134) | 73x21, 1,197 keyed |

All five are MDM1-wrapped 8-bit DIBs decoded by the verified `BmpReader`. The shipped colour-key
rule (`CDDBitMap::CreateFromHBitmap` / `BringUp::CornerColourKey`) keys the corner pixel, which for
these files is white (255,255,255); the keyed pixels are alpha 0 and everything else is drawn as-is.

Layout follows `InfoBar::LayoutSurfaces` (HYPERX 0x00450e60): period frame centred at the top with
the clock inside at frame+(25,9); team frames at the bottom left, team A above team B, logo inside at
x+2, score at frame width-0x20. The original laid this out on the 640x480 back buffer; this scene
draws the frames at 2x for the 1280x720 deployment, and simplifies the parts the N5 scope does not
cover (damage bars, radar, roster lists, shot power, head-cam, messages are not drawn).

Text: the original HUD text is Windows GDI "MS Sans Serif" (`ddraw.cpp:2532
AssignName(&g_strHudDefaultName, "MS Sans Serif")`; `infobar.cpp:970 strcpy(g_abInfoBarFontName,
"MS Sans Serif")`), which is not shipped art, so a clearly labelled interim 3x5 bitmap font supplies
the numbers, clock, team names and the ROK value label. The HUD draws: clock (e.g. 4:55),
period label P1 and the period frame, team frames/logos, team names "THE MACHINE"/"THE FURY",
home/away score, the InfoBar-style ROK value `x<abs(value)>`, and a bottom-right
"AI FALLBACK / INTERIM HUD FONT" line that states both substitutions. In play mode the HUD is drawn
in `OnGUI` with the original art textures; in captures the same layout is composited on the CPU into
the rendered framebuffer pixels before the PNG is written, so a 1280x720 frame contains the HUD just
as the original's software framebuffer did. Both paths share one layout function
(`MatchHud`) and headless checks the same data/geometry.

## 4. The deterministic test

The gameplay capture suite (Editor `CaptureAll` and the built player's `--gameplay-capture`) runs in
this order:

1. the unchanged N4 deterministic five seconds of match play (150 ticks), captured as 00;
2. a home goal fixture: ROK value 2 moved through the south goal mouth -> 2 points, render bone 7;
3. captures of the ROK at its spawn point with render bones 0/7/8 (frames 02/03/04);
4. an away goal fixture: ROK value 1 moved through the north goal mouth -> 1 point, render bone 0;
5. the ROK back at its spawn point and the final scoreboard (frames 05/06 from the goal and final
   state).

The test is deterministic: the same ticks, positions, scores and pixel bytes on every run; the
Editor and Windows-player frames are byte-identical. It also asserts: exactly two goals, final score
2-1, no double count in the mouth (400 ms debounce), render-bone mapping plain=0/double=7/quad=8/
kill=2, carried-ROK follow distance 0, and no 20XX process running.

## 5. Commands

All commands below were run from the repository root on 2026-10-31 with Unity 6000.6.7f1. The
headless checks use `-nographics`; captures run WITHOUT it.

    # gameplay headless check
    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.Check \
      -logFile <temp>\ublade3-gameplay-check.log

    # gameplay editor capture (7 frames)
    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.CaptureAll \
      -logFile <temp>\ublade3-gameplay-capture.log

    # gameplay Windows player build + run + capture verification
    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.BuildAndRunWindows \
      -logFile <temp>\ublade3-gameplay-player-build.log

    # the existing N4 match gates, unchanged
    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3Match.Check \
      -logFile <temp>\ublade3-match-regression-check.log

The built gameplay player (`unity/UBlade3/build-match/UBlade3NativeMatchGameplay.exe`) also supports
`--gameplay-autostart` and `--gameplay-capture=<png>`; without a capture argument it opens the
keyboard-controlled match with the HUD drawn in OnGUI. The menu paths are
`UBlade3 > Native Gameplay > ...` for the same four actions.

## 6. New code

All new source is additive; no existing Data/Assets/Sim/PlanVM/App source, existing preview file,
match file, ProjectSettings, native plugin or release file was edited.

| File | Contents |
| --- | --- |
| `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchGameplayRules.cs` | `MatchGoalDetector` (goal mouth/port/value/points/side/debounce using the existing GoalRules/MatchRules), `MatchHudModel` (team names, scores, period, clock, ROK value) and `MatchScoreFixture` (deterministic goal, dwell, carrier-follow and render-bone fixtures) |
| `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchRokView.cs` | `MatchRokData` (Rokmix WBM rig/mesh/materials/per-triangle bone map/texture resolution) and `MatchRokView` (original mesh + materials, render-bone triangle selection, bind-pose placement from Sim.Rok) |
| `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHud.cs` | `MatchHudData` (original assets8 frper/frdamgA/frdamgB/hudChi/hudsea art through BmpReader) and `MatchHud` (shared layout, original art, interim 3x5 font, OnGUI interactive drawing, CPU framebuffer compositing for captures) |
| `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchGameplay.cs` | The additive gameplay component: builds ROK view + HUD on an existing MatchScene, syncs from Sim.Rok, detects goals, runs the deterministic two-goal suite and captures frames, and drives interactive keyboard play when no capture command is present |
| `unity/UBlade3/Assets/Editor/UBlade3MatchGameplay.cs` | Menu and `-executeMethod` entry points: `Check`, `CaptureAll`, `BuildWindows`, `BuildAndRunWindows` (the player build uses a transient scene asset that is deleted afterwards) |
| `docs/NATIVE-GAMEPLAY.md` | This record |

## 7. Log and probe contract

The game logs, all prefixed `UBLADE3_NATIVE_GAMEPLAY_`:

    UBLADE3_NATIVE_GAMEPLAY_ROK model=Rokmix ... triangles=257 ... boneTriangles=...
    UBLADE3_NATIVE_GAMEPLAY_HUD_ART frper=83x60 ... frdamgA=220x47 ... homeLogo=73x21 ...
    UBLADE3_NATIVE_GAMEPLAY_HUD_SPRITE role=... size=... colourKey=... keyedPixels=... wrapped=mdm1
    UBLADE3_NATIVE_GAMEPLAY_HUD_BUILT ... textFont=interim3x5
    UBLADE3_NATIVE_GAMEPLAY_SCENE_OK model=Rokmix rokTriangles=257 rokBones=9 hudArt=ok renderBones=...
    UBLADE3_NATIVE_GAMEPLAY_RULES note="..." (goal/score rule provenance)
    UBLADE3_NATIVE_GAMEPLAY_AI_FALLBACK note="..."
    UBLADE3_NATIVE_GAMEPLAY_GOAL side=home|away points=... goalSlot=... scoreCode=... rokValue=...
        carried=... atSpawnSlot=... renderBone=... tickMs=... homeScore=... awayScore=... rokPos=(...)
    UBLADE3_NATIVE_GAMEPLAY_CAPTURE index=... label=... tick=... seconds=... path=... width=... height=...
        bytes=... homeScore=... awayScore=... period=... clock=... rokRenderBone=... rokPos=(...) hud="..."
    UBLADE3_NATIVE_GAMEPLAY_SUMMARY goals=... homeGoal=(...) awayGoal=(...) homeScore=... awayScore=...
        debounceExtra=... carrierFollow=... carrierDistance=... renderBones=... hud="..." no20xxProcesses=...
    UBLADE3_NATIVE_GAMEPLAY_CAPTURES_OK frames=7 score=2-1
    UBLADE3_NATIVE_GAMEPLAY_CHECK_OK    (headless gate)

The headless gate additionally logs `CHECK_RENDER_BONES`, `CHECK_GOAL`, `CHECK_HUD`,
`CHECK_CARRIER`, `CHECK_NO_20XX` and `CHECK_COUNTS` lines. A mismatch prints
`UBLADE3_NATIVE_GAMEPLAY_MISMATCH <reason>` and fails the gate; a missing WBM, HUD art or texture is
fatal and is never substituted.

## 8. Known gaps and honest limits

1. **The plan VM/AI is still not ported.** The N4 labelled fallback stands: non-human skaters hold
   Forward, goalies hold no input. The new layer does not change that; the HUD's
   "AI FALLBACK / INTERIM HUD FONT" line states it in captured frames.
2. **Goals come from a deterministic fixture, not a plan shot.** The goal entry test, score value,
   points, side, score bank and debounce are the real shipped rules, but the pose of the ROK in the
   test is written by the fixture because `CProjectAndBounce`/the plan agenda are not ported. No
   face-off, drag, goal celebration, ROK reset or period restart after a goal runs; the test restores
   the ROK itself. The ROK value (1x/2x/4x) is the plan/randomizer state, set directly in the test.
3. **ROK rotation is a presentation stand-in.** `RokState` has no orientation field and the render
   port has no plan-driven object transform, so the yaw follows the body's velocity direction (held
   at rest) with the model header rotation; no bounce spin is presented.
4. **The ROK model has no textures.** Its shipped `Mdltxt` materials name no BMP texture (the
   colors are palette material records). The scene renders the original WBM mesh and material
   colours; it does not invent textures for it.
5. **HUD text is an interim font and the layout is simplified.** The original HUD text face is
   Windows GDI "MS Sans Serif" (not shipped art). The scene draws the original frames/logos plus a
   3x5 substitute font; damage bars, radar, roster lists, shot power, head-cam, messages and the
   original safe-area/640x480 scaling are not reproduced. Scores/period/clock/team names/ROK value
   are present and data-driven.
6. **HUD capture is CPU-composited.** In the Editor batch capture path the HUD is blended into the
   framebuffer pixels before the PNG is written (shared layout with the interactive OnGUI HUD).
   Interactive play draws it with OnGUI; there is no per-pixel comparison against the 1996 software
   framebuffer yet.
7. **Only two goals, one directional fixture and one difficulty.** The test shows a home 2-point
   (south) and an away 1-point (north) goal; the 4x arm and own-goal/score-code-3/4 arms are covered
   by the existing rule code but not individually captured. Only Easy difficulty, exhibition mode,
   Chicago home and Seattle away, CHIDRO drome.
8. **The ROK is not carried in any capture.** The carried follow path is verified headlessly, but
   no capture shows the ROK attached to a skater (the carried render bone is also only checked in
   the mapping test).
9. **No audio, no 20XX, no native plugin.** The scene runs entirely in C# with the original data;
   the shipped exact-engine native plugin/release is untouched and remains the playable path.
10. **No reference parity gate.** Acceptance here is the exact rule/state evidence and deterministic
    byte-identical captures, not a per-pixel match against a 1996 record.

## 9. Legal

The original game data, including the HUD bitmaps and ROK model used here, is read from the user's
own HyperBlade install (D:\Games\HyperBlade\HYB locally) and is never copied into this repository.
`hyperblade-decomp` and `hyperblade-20XX` are read-only references for format/behaviour research;
the C# here is written from that research.
