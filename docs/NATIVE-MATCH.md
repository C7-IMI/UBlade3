# Native match - original drome and six original skaters driven by the C# simulation

Status: N4 native-match milestone (first playable C# match scene). One original HyperBlade drome
(CHIDRO, loaded from the player's own install) and six original skaters - Chicago "The Machine"
home and Seattle "The Fury" away - are rendered from their original WBM models and BMP textures,
placed from the shipped HTM/ENV data, and driven by the existing exact C# simulation
(`UBlade3.Sim`) at a fixed 30 Hz with the simulation's own chase camera. The human-controlled home
skater moves with the keyboard, and a deterministic input script captures three 1280x720 frames
over five seconds of simulation.

This is the C# renderer + C# simulation match scene. It is **not** the playable release and it is
**not** the complete native engine: the ADLIB plan VM, AI behaviours, collision bubbles, rok
possession/shooting, goals/scoring, HUD, audio and the shaded software-render path are not wired
into it. Those gaps are listed in section 9. The shipped exact-engine native plugin/release is
untouched by this milestone.

## What was verified

Unity 6000.6.7f1. Three verification paths, all run on 2026-10-31; the data install is the local
retail HyperBlade at D:\Games\HyperBlade\HYB (`HYPERBLADE_GAME_DATA` is not needed).

### 1. Headless check (no graphics device)

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3Match.Check \
      -logFile <temp>\ublade3-match-check-final.log

Result: exit 0, `UBLADE3_NATIVE_MATCH_CHECK_OK`, mismatches=0, and

    UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd
    UBLADE3_NATIVE_MATCH_PROBE_RUN ticks=150 seconds=5 humanDisplacement=184.546 cameraDistance=8.653 playerSeparations=15
    UBLADE3_NATIVE_MATCH_PROBE_DROME_BOUNDS unityMin=(-375,-105,-371.25) unityMax=(375,300.003,371.25)
    UBLADE3_NATIVE_MATCH_PROBE_NO_20XX running20xxProcesses=0 no20xx=True
    UBLADE3_NATIVE_MATCH_PROBE_PASSED

The check loads the drome, both teams, all six characters and the drome's Rok definition, builds
the simulation, runs 150 fixed ticks with the deterministic script, and asserts the counts,
positions, camera distance/FOV, missing references and the absence of a 20XX process. A fresh run
prints the same match digest.

### 2. Editor batch-mode capture (graphics device required - run WITHOUT `-nographics`)

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3Match.CaptureAll \
      -logFile <temp>\ublade3-match-capture.log

Result: exit 0, `UBLADE3_NATIVE_MATCH_CAPTURES_OK frames=3 ticks=45,105,150 seconds=5 width=1280 height=720`,
and `UBLADE3_NATIVE_MATCH_CAPTURE_EVIDENCE frames=3 views=6 ticks=150 seconds=5 missingRefs=0
digest=f54169057f5800dd`.

### 3. Windows player build and `--match-capture` run

    "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -nographics \
      -projectPath unity/UBlade3 \
      -executeMethod UBlade3.EditorTools.UBlade3Match.BuildAndRunWindows \
      -logFile <temp>\ublade3-match-player-build.log

Result: exit 0 in 22.1 s; `UBLADE3_NATIVE_MATCH_PLAYER_BUILD_OK bytes=103900532
output=.../unity/UBlade3/build-match/UBlade3NativeMatch.exe`, the player ran with
`-batchmode -screen-width 1280 -screen-height 720 -screen-fullscreen 0 --match-capture=... --match-seconds=5`,
exited 0 and wrote three frames; `UBLADE3_NATIVE_MATCH_PLAYER_CAPTURES_OK frames=3`. The player
run's own log has the same load/scene lines and `UBLADE3_NATIVE_MATCH_NO_ENGINE_PROCESS no20xx=True
engines=csharp`. The generated `build-match/` player directory was removed after the evidence was
read; the command above rebuilds it.

**The Editor batch capture and the built Windows player produced byte-identical frames** (same
SHA-256 for all three), so the player and Editor paths run the same match code and produce the same
deterministic simulation.

### Captures (all 1280x720, read back after the run)

| File | Tick | Sim time | Human clip frame | Human position (original x,y,z) | Human displacement | Camera position (original x,y,z) | Bytes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `artifacts/native-match-frame-045.png` | 45 | 1.5 s | azkskat2 18 | (42.7564, 37.5954, -93.5609) | 37.99 | (32.8356, 40.9292, -94.0198) | 111,689 |
| `artifacts/native-match-frame-105.png` | 105 | 3.5 s | azkskat2 24 | (64.8311, -44.6262, -78.7236) | 122.49 | (55.8291, -44.8097, -81.5382) | 139,361 |
| `artifacts/native-match-frame-150.png` | 150 | 5.0 s | azkskat2 15 | (64.1364, -105.6298, -58.4164) | 184.55 | (58.0893, -100.903, -62.4125) | 87,064 |
| `artifacts/native-match-drome.png` | - | - | - | - | - | drome-preview framing | 240,646 |

SHA-256 (Editor frames; the three `native-match-player-*.png` frames are byte-identical):

    0D67467C61A4053DDDEDF31A37406BCEE6D06CE1AEB696D34A38A5B65A9FFF80  native-match-frame-045.png
    E9273C8E8B68EA0EE804176CDDC386B21EB7042967E9755042C70B0F1323513F  native-match-frame-105.png
    DE8081DC1C834295A36E653EF728BFB6CE8331E5A9D29EF12C1C11BA6D711B6A  native-match-frame-150.png

What the read-back frames show: a chase view from behind the human-controlled Chicago skater
(silver helmet, blue body, yellow pads, stick in hand) with the original CHIDRO drome around it -
the drome's red/blue track bands, the yellow multiplier pads, goal/ramp/turnstile props and, in
the 045 and 105 frames, other original skaters in the distance. The drome is the shipped CHIDRO
WBM/BMP set, which is a mostly dark-grey shell with coloured track markings; the 1996 game's
coloured floors come from each drome's own textures (the native release screenshot shows a
different drome's red/blue floor). `native-match-drome.png` is the N2 preview framing of the same
loaded drome for provenance: the play area sits inside the large drome shell, and the six skaters
are small figures in the bowl centre.

No HUD is drawn in the captures - see section 9.

## 1. Match composition and why these teams

The teams are read from the shipped `teams.cat` catalogue and their HTM files; nothing is
invented:

| Side | File | Team | On-ice Dromers (file slots 0,1,2) | Model ids | Palette |
| --- | --- | --- | --- | --- | --- |
| Home | `Chico.htm` | The Machine (Chicago) | Evans (CHIOMX, `SetPlans Human ...`), Ballard (CHIDMX), Viveros (CHIGMX, goalie) | 0, 2, 4 | home side: no remap |
| Away | `Seattle.htm` | The Fury (Seattle) | S._Vanacher (SEADMX), Kariotis (SEADMX), McFarland (SEAGMX, goalie) | 1, 3, 5 | away side: forced remap |

Why Seattle as the away team: `seattle.htm` is the only team file whose three on-ice
`SetMotionParams pos` entries are all authored on the south half of the drome
(S._Vanacher `40 -75 0`, Kariotis `-50 -65 0`, goalie McFarland `0 -192.5 -20.8`).
Chicago's are on the north half (Evans `40 75 0`, Ballard `-20 75 0`, Viveros
`0 192.5 -20.8`). Using each file's own data therefore starts the six skaters on their own
halves with **no invented mirroring**. Other teams' files author their spawns like a home side,
which would have put both teams on the same half.

The drome is `Chico.htm`'s `environment=chidro.env` (`CHIDRO.ENV`, 29 object definitions). The
match loads the textured model set (`Data\Models\Mdltxt`) and the original BMP textures, the same
path the verified N2 drome preview and N3 skater preview use.

The slot ids come from `GameEngine::LoadTeamFromFile` (HYPERX 0x004858b0,
`third_party/hyperblade-decomp/src/engine/gameengine.cpp:3467-3662`): three Dromers go on the
ice (`TEAM_PLAYERS_ON_DROME` 3, line 3463), `nRunningId` starts at the team index and steps by
two per Dromer (lines 3493/3543), and every away definition gets `m_bForceRemap = 1` (lines
3546-3549). That is exactly home ids 0,2,4 and away ids 1,3,5. `PlayerObject::PlayerObject`
(HYPERX 0x00474150, `playerobject.cpp:25-99`) then makes slots 0, 2 and 4 keyboard objects and
the others gamepad objects, which is why the human controls slot 0.

## 2. Data and placement

The drome/teams/characters are all loaded through the already-verified readers:

* `CHIDRO.ENV` through `UBlade3.Data.DromeFile` and `UBlade3.Native.DromeSceneLoader`:
  29 definitions, 25 with a model (the shipped file has four `modfile=null` objects: inscrut,
  Rob, Coach0, Coach1), 25 Unity meshes, 2,034 triangles, 32 BMP textures, 0 missing.
* Both HTM files through `UBlade3.Data.TeamFile`, with `BuildLoadLayout(side)` applying the
  shipped model-id/away-remap rules and `BuildOutfieldRows(0)` / `GetGoalieRatings(0)` giving
  the Easy (`m_nDifficulty` 0) tables `PlayerObject` reads. The home goalie uses the file's
  `SetSpecialGoalieRatings` table, as `PlayerObject` does for `GOALIE_SLOT_A` at difficulty 0.
* Six characters through `SkaterPreviewData.Load` (the N3 mapping): each is the WBM rig + the
  assembly name resolved from `AnimAssm.txt` + the named WBA clip. Skaters use
  `AA_Skate_Std` (segment 0 `azkskat2` "normal skate", 27 frames at 30 fps); goalies use
  `AA_Goalidle` (segment 0 `aplread2` "stand ready", 81 frames at 30 fps). Away players
  pass `RemapPalette = true`.
* The simulation's `DromeDefinition` is built from the same loaded environment objects (the
  drome's own `Rokmix` Rok definition, multipliers, north/south goals), and each player is bound
  with `ObjectMotionParams` from its HTM `SetMotionParams` (position, speed, acceleration,
  azimuth, elevation, friction, elasticity).

Measured character counts (probe lines `UBLADE3_NATIVE_MATCH_SKATER_LOADED`):

| Slot | Role | Model | Clip | Frames | Bones | Triangles | Verts | Textures |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | home human | CHIOMX | azkskat2 | 27 | 16 | 314 | 942 | 13 |
| 1 | away skater | SEADMX | azkskat2 | 27 | 16 | 291 | 873 | 16 |
| 2 | home teammate | CHIDMX | azkskat2 | 27 | 16 | 314 | 942 | 13 |
| 3 | away skater | SEADMX | azkskat2 | 27 | 16 | 291 | 873 | 16 |
| 4 | home goalie | CHIGMX | aplread2 | 81 | 16 | 165 | 495 | 12 |
| 5 | away goalie | SEAGMX | aplread2 | 81 | 16 | 197 | 591 | 11 |

Total skater triangles 1,572; missing references 0.

Placement before the first tick reproduces the engine's object construction + surface bind:
`PhysicsBody::BindToObject` (HYPERX 0x00407780,
`third_party/hyperblade-decomp/src/physics/physicsbody.cpp:114-146`) starts the body at
`ObjectDef::m_vPos`; the glide behaviour projects it to the arena shell via
`CollisionEllipsoid::GetZ`/`NormalAtPoint` (the same exact calls `PhysicsBody::InitOnSurface`
HYPERX 0x00408190 / `ResetOnSurface` 0x00407ab0 use). This scene does that once at build time and
keeps the ObjectDef azimuth as the heading (`ResetOnSurface` would overwrite it with +x; the
surface projection of `InitOnSurface` is used instead).

Two placement notes, both labelled in the logs (not hidden):

* **Goalie placement fallback.** A goalie's HTM `pos` is behind the goal line (e.g. Seattle's
  `0 -192.5 -20.8`, outside the 187.5-unit tracking ellipsoid). The original walks the goalie
  into the mouth with the plan VM's goalie agenda (`CBungeeToward`, `CMonitorKeys`
  MON_GOALIE_CTRL), which is not ported. The scene places each goalie at
  `Game::InitMatchMarkers` (HYPERX 0x0047d1c0,
  `third_party/hyperblade-decomp/src/engine/game.cpp:249-283`) goal-mouth marker
  (`radiusY - 3`, y = +/-184.5; then GetZ = -18.7077) and leaves it standing with the
  `AA_Goalidle` clip. Logged as `UBLADE3_NATIVE_MATCH_GOALIE_PLACEMENT`.
* **Placeholder AI.** The ADLIB plan VM is not ported, so no original plan runs. Every non-human
  outfield skater holds `HeldBinding.Forward`; goalies hold no input. Logged as
  `UBLADE3_NATIVE_MATCH_AI_FALLBACK active=True` and
  `UBLADE3_NATIVE_MATCH_PROBE_AI_FALLBACK`.

## 3. The exact simulation wiring

`UBlade3.Sim.Simulation` is used unchanged. The scene only supplies the hooks the engine's object
construction would run:

* `new Simulation(new MatchContext { PeriodLengthMs = 300000 (default), PlayerMode = exhibition })`.
* `Simulation.BindPlayer(slot, ObjectMotionParams)` for all six slots.
* Player stats: the Easy rating table is copied into `PlayerStats` (rating n at n+1) and
  `ObjectStats::ScaleTo` is called as `PlayerObject` does at load (period 1, exhibition).
* `Simulation.BindRok` with the drome's own `Rokmix` definition (the Rok is not rendered or
  possessed in this scene - see gaps).
* `Simulation.LoadDrome(def, beginMatch: true)` then `Simulation.Tick()`.
* `Simulation.InputForSlot` supplies the human mask for slot 0 and the labelled fallback for the
  other slots. `Simulation.MainCamera` is the chase camera.

Ticking: `MatchSimulation` steps `Simulation.Tick` at the fixed 30 Hz clock. The fixed step is
the 20XX modernisation (`third_party/hyperblade-20XX/src/engine/framepacing.h:14`
`kTickMs = 1000/30`), which the simulation slice already documents as the port convention
(`docs/SIM-SLICE.md`); the original `GameEngine::TickClock` (0x00481130) is variable-rate and is
kept in the sim as `AdvanceHostFrame` for a later frame-trace parity run. Measured after 150
ticks: `clockMs=4999` (the 20XX-style float accumulation truncates), `delta=0.0333` (1/30),
`period=1`, `runState=1` (playing), one `StartDrag` broadcast at restart.

Keyboard mapping is the shipped `BOOIE.INI` `[Keys]` table: Up = Forward, Down = Stop,
Left = RotateLeft, Right = RotateRight, S = Attack, D = SpeedBurst, Space = Shoot, C = Pass,
Escape = `GameEngine::TogglePause` (the buttons resolve to the `HeldBinding` bits
`PhysicsBody` reads; `PhysicsBody::UpdateMovementAxes` HYPERX 0x00407c40,
`ApplySteeringForce` 0x00408080 and `CGlideOnSurface::UpdateTerrainPhysics` 0x0040c270 are the
exact C# ports). The deterministic capture script is forward for the whole run plus six held
ticks of RotateLeft at tick 60 and six held ticks of RotateRight at tick 120 - a scripted
held-key sequence, not the fallback AI.

The simulation output is identical between the headless check and both capture paths: the same
match digest `f54169057f5800dd`, the same player positions and the same bytes on the three frames.

## 4. Camera

The camera is the simulation's own chase camera, not a preview frame:

* Position: `CameraState.Body.Pos` after `CameraTrack.Process` (`CTrack::Process`
  HYPERX 0x00408fd0, `src/engine/ctrack.cpp:90-640`) - the bungee-12 chase position, built
  from `CameraObject::CameraObject` (0x00421980) defaults including `m_flBungeeLength = 12`
  and `m_flHalfFov = 85/2 degrees` (`docs/SIM-SLICE.md` section 4).
* Look direction: `CameraState.Body.VelDir`, which `CameraTrack.Process` sets to its computed
  `vLook` towards the tracked head. The scene converts the sim's Z-up world to Unity with the
  same proper rotation the renderers use, `(x,y,z) -> (x,z,-y)`, and calls
  `Quaternion.LookRotation`; there is no invented yaw.
* `CameraProjectionState` (`CreateProjectionState` 0x004303b0, `Configure` 0x004304a0):
  Unity vertical field of view 85 degrees, near 1.0 and far 1000.0 (the `m_bHasRow3` Direct3D
  arm the sim self-check asserts).
* Camera target: `PlayerState.AimPosition` is the body position plus 5.9822 model units (the
  16-bone rig's rest root 3.9484 + chest-to-head 2.0338 from `docs/SKATER-PREVIEW.md` section 2),
  because the animation port cannot yet supply bone 9's world transform; this is the documented
  sim hook in `docs/SIM-SLICE.md`.
* Placement/facing and the camera position/direction are interpolated between the two most
  recent 30 Hz simulation states at render time ("render interpolation"); the skater's clip
  frame stays on the original 30 fps integer frame (`SkaterAnimationClip.FrameAtSeconds`, the
  shipped `AnimPlayer::SetClip` 0x00425220 mapping). The mesh is re-posed with the interpolated
  facing so a turning skater does not pop.

Not claimed exact: `CameraObject::ViewIt`'s shake basis and the pi/2 projection tilt are not
reproduced (Unity builds its view matrix); reticle, head-cam and rear view are not wired.

## 5. Rendering

* The drome is assembled by the verified N2 preview component
  (`UBlade3.Native.DromePreviewScene`) from `DromeSceneLoader`'s 25 original meshes and
  BMP textures. The match scene reuses it unchanged and takes its camera; the scene does not
  rewrite the drome/material/shader code.
* The skaters reuse the verified N3 pipeline: `SkaterPreviewData` loads the WBM rig/WBA clip,
  `SkaterRig.PoseFrame` reconstructs the 16-bone pose, `SkaterMeshData.Pose/ApplyTo` poses the
  flat-shaded corners on the CPU, and `MatchSkaterView` only adds the GameObject, original BMP
  texture objects and the placement/clock. Materials use the same unlit
  `UBlade3/DromePreview` MODULATE shader (`D3DRend::DrawFace` HYPERX 0x004328c0).
* Both drome and skaters use the original textured (`Mdltxt`) model set and the original 24-bit
  BMP textures; the away side's materials use the forced palette remap
  (`EngineObject::InitModel` 0x00470790, the verified N3 rule). Nothing is procedural and no
  UBlade2 asset is used.

## 6. New code

All runtime files are new and under `unity/UBlade3/Assets/Scripts/UBlade3/Native/`; no existing
file, Sim/Data/Assets/PlanVM/App source, scene, ProjectSettings, native plugin or release file
was edited.

| File | Contents |
| --- | --- |
| `MatchData.cs` | `MatchPlayerSlot` / `MatchTeamData` / `MatchWorldData` / `MatchCharacterData`: loads CHIDRO, `Chico.htm` home and `Seattle.htm` away through the verified Data/Assets/Native readers, builds the simulation `DromeDefinition`, the HTM spawn positions and goal-mouth marker fallback, and the six WBM/WBA characters |
| `MatchSimulation.cs` | The simulation binding/placement/input/AI-fallback/step wrapper and the `UBLADE3_NATIVE_MATCH_SIM`, `_STATE`, `_CAMERA` reporting |
| `MatchSkaterView.cs` | One original skater's Unity objects, BMP textures/materials, 30 Hz clip clock, CPU pose and interpolated placement/facing |
| `MatchScene.cs` | The in-memory match scene: `Build`, 30 Hz `Update` with keyboard, deterministic capture script, capture, chase camera, HUD-pending placeholder, `--match-capture` runtime handling |
| `MatchProbe.cs` | Headless `RunCheck` (loaded teams/skaters/drome, ticks, positions, camera, missing refs, no-20XX) and the capture-state `DescribeScene` probe |
| `Assets/Editor/UBlade3Match.cs` | Menu + `-executeMethod` entry points: `Check`, `CaptureAll`, `BuildWindows`, `BuildAndRunWindows` (the build uses a transient scene asset that is deleted afterwards) |

## 7. Probe output contract

`MatchProbe.RunCheck` prints, in order:

    UBLADE3_NATIVE_MATCH_CHECK root=... unity=... assetSet=Mdltxt no20xx=True
    UBLADE3_NATIVE_MATCH_DATA ...
    UBLADE3_NATIVE_MATCH_TEAM side=home|away ...
    UBLADE3_NATIVE_MATCH_PLAYER side=... slot=... role=... model=... assembly=... spawn=(...)
    UBLADE3_NATIVE_MATCH_PROBE_DROME file=... objects=29 modelObjects=25 ... digest=...
    UBLADE3_NATIVE_MATCH_PROBE_DROME_BOUNDS unityMin=... unityMax=...
    UBLADE3_NATIVE_MATCH_SKATER_LOADED slot=... model=... assembly=... wba=... frames=... bones=... triangles=... textures=...
    UBLADE3_NATIVE_MATCH_SIM ticks=... seconds=... clockMs=... period=... delta=...
    UBLADE3_NATIVE_MATCH_AI_FALLBACK active=True note="..."
    UBLADE3_NATIVE_MATCH_GOALIE_PLACEMENT note="..."
    UBLADE3_NATIVE_MATCH_CAMERA pos=(...) dir=(...) bungee=12 countdown=... fovDegrees=85 tracked=0
    UBLADE3_NATIVE_MATCH_STATE slot=... role=... pos=(...) speed=... velDir=(...) aimPos=(...)
    UBLADE3_NATIVE_MATCH_VIEW slot=... frame=... unityPos=(...) yawRadians=... clip=... frames=...
    UBLADE3_NATIVE_MATCH_PROBE_RUN ticks=150 seconds=5 humanDisplacement=... cameraDistance=... playerSeparations=15
    UBLADE3_NATIVE_MATCH_PROBE_NO_20XX running20xxProcesses=0 no20xx=True
    UBLADE3_NATIVE_MATCH_PROBE_HUD_PENDING ...
    UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=...
    UBLADE3_NATIVE_MATCH_CHECK_OK

The scene/capture probe (`MatchProbe.DescribeScene`) adds
`UBLADE3_NATIVE_MATCH_PROBE_CAPTURE index=... tick=... seconds=... path=... bytes=...` and
`UBLADE3_NATIVE_MATCH_PROBE captureState=complete frames=3 ticks=150 seconds=5 missingRefs=0 digest=...`.
A mismatch prints `UBLADE3_NATIVE_MATCH_MISMATCH <reason>` and fails the run; a missing install,
model, animation, texture or shader is fatal and is never substituted.

## 8. Editor entry points

| Method | Purpose |
| --- | --- |
| `UBlade3.EditorTools.UBlade3Match.Check` | headless `MatchProbe.RunCheck`; `-nographics` OK |
| `UBlade3.EditorTools.UBlade3Match.CaptureAll` | build the scene in memory, run 150 deterministic ticks, write the three match frames + the wide drome frame; requires a graphics device (no `-nographics`) |
| `UBlade3.EditorTools.UBlade3Match.BuildWindows` | build `unity/UBlade3/build-match/UBlade3NativeMatch.exe` from a transient scene |
| `UBlade3.EditorTools.UBlade3Match.BuildAndRunWindows` | build, then run the player with `--match-capture=` and verify the three player frames |
| Menu | `UBlade3 > Native Match > ...` for the same four actions |

The built player supports `--match-autostart`, `--match-capture=<png>` and
`--match-seconds=N`; without a capture argument it opens the keyboard-controlled match.

## 9. Known gaps and honest limits

1. **No plan VM / no original AI.** The ADLIB plan VM and all behaviours, interactors, agenda
   items and plan data are not ported (N5). The non-human skaters and goalies use the labelled
   placeholder AI above. No Rok possession, pass, shot, collision bubble, damage, goal or score
   rule runs in this scene; the score stays -1/-1 and only the match clock/period machine ticks.
2. **Goalie plan fallback.** Goalies stand at the `InitMatchMarkers` goal-mouth markers instead
   of being driven by the original goalie agenda (CMonitorKeys MON_GOALIE_CTRL, CBungeeToward).
3. **Animation is one clip, no blending.** Each character plays only the assembly's first
   segment (`AA_Skate_Std`/azkskat2 or `AA_Goalidle`/aplread2) at rate 1.0; plan-driven clip
   selection, playback rates, the first-frame blend, animation events and transitions are not
   implemented (N3 gaps). The model facing uses the physics body's velocity direction as a
   stand-in for the plan-driven object/render-slot rotation and banking (the render port has no
   `SetFlatOnTerrain`/bone-9 transform yet); pose frames advance at 30 Hz and only the placement
   and facing are interpolated.
4. **Camera is not the full ViewIt path.** Position and look direction come from the exact
   `CameraState`/`CTrack` port, but `CameraObject::ViewIt`'s shake basis, the pi/2 projection
   tilt, reticle, head-cam and rear view are not reproduced. FOV 85/near 1/far 1000 are the
   CameraProjectionState values; culling/qualities are Unity's.
5. **HUD is pending, not faked.** The original HUD is DATA bitmap fonts/art (InfoBar
   `IncrementScore` 0x0047ae70, HUD text) owned by the N6 audio/UI milestone. The runtime play
   mode draws one clearly labelled "HUD PENDING" `OnGUI` line; the captures contain no HUD at
   all. Team names, score and period are loaded/available and are logged by the probe.
6. **Only the textured model set is drawn; CPU skinning.** The retail software renderer's
   `Mdlshd` shaded path (`SWRend::DrawFace`) is not wired into the skater renderer; the drome
   may be switched to it, but the match scene uses `Mdltxt` for both. The established N3 gap
   (CPU-posed flat-shaded corners, no `SkinnedMeshRenderer` bone palette) remains. The drome is
   the shipped CHIDRO shell and appears mostly dark grey because of its own textures, not
   because a lighting pass was skipped.
7. **No reference parity gate.** There is no per-pixel comparison against 1996 records or 20XX
   for this scene yet; the acceptance here is structure/provenance, the exact simulation state
   and deterministic reproducible frames.
8. **Only Easy difficulty, exhibition mode.** `m_nDifficulty` is 0 and the match context is
   exhibition; tournament/session/other ratings tiers are not exposed.
9. **Not the playable release path.** The native plugin/release
   (`Assets/Plugins/x86_64/ublade3_native_v6.dll`, `build-native/`, `dist/`) is untouched and
   remains the playable exact engine. This scene is the C# renderer+simulation track; picking it
   up as the release needs the plan VM, collisions, goals, HUD/audio and the release pipeline
   (N5-N7).

## 10. Legal

The original game data is read from the user's own HyperBlade install
(D:\Games\HyperBlade\HYB locally) and is never copied into this repository.
`hyperblade-decomp` and `hyperblade-20XX` are read-only references for format/behaviour research;
the C# here is written from that research.
