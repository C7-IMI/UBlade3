# NATIVE-MATCH-N7 - playability follow-up on the native C# match (N7)

This milestone closes the three biggest playability gaps left by N6 without faking plan rules: a real
post-goal celebration + face-off/restart flow, a real head-camera aim/shot path for the human, and
fallback AI possession through the same possession/shoot/pass rules. The N6 human loop and the
N4/N5 regression evidence stay working. It is still a **playability milestone**, not a parity or
release claim: the ADLIB plan VM is not ported and the retail AI plans/agendas remain gaps.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Only `Native/Match*`, the one
Editor file `Assets/Editor/UBlade3MatchPlay.cs` and this document were edited. Nothing was
committed; the Lead owns the commit.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| `third_party/hyperblade-decomp` | Byte-matching decompilation of HYPERX.EXE v1.0.0.1. Goal plan flow, face-off positions, head-camera construction/CHeadTrack, the launch shot word and the AI shot branch cite the exact functions. |
| `third_party/hyperblade-20XX` / `tools/plan_dump.py` | The compiled plans were decoded to pseudo-source: `NGOAL.PBN`, `SGOAL.PBN`, `HUMAN.PBN`, `ROK.PBN`, `CARRYROK.PBN`, `INPUT.PBN`. |
| D:\Games\HyperBlade\HYB\data | The shipped install read by every run: HTM/ENV/CFG, drome models, HUD art, sounds. Read-only, never committed. |
| `unity/UBlade3/Assets/Scripts/UBlade3/Sim` and `Native/Match*` | The N4/N5/N6 layers this builds on. |
| `docs/NATIVE-MATCH.md`, `docs/NATIVE-GAMEPLAY.md`, `docs/NATIVE-PLAY.md`, `docs/NATIVE-AUDIO.md` | The earlier milestones' acceptance records and citations. |

## 2. What changed in N7

New files (all in `unity/UBlade3/Assets/Scripts/UBlade3/Native/`):

| File | Contents |
| --- | --- |
| `MatchFlow.cs` | `MatchFlowPhase` (Live / GoalCelebration / FaceOffDrag) and `MatchFaceOff`: the goal stop, the shipped timings and positions, the ROK drag placement and the documented drop fallback. |
| `MatchHeadCamera.cs` | The human head camera: cameraobject slot-25 FOV/pose, CHeadTrack view-fraction aim on the rotate/throttle held bits, shot-word packing, `Describe` for the logs. |
| `MatchAiController.cs` | The bounded fallback AI: pursue the free ROK with the existing input mask, request a shot when carrying near the goal, request a pass when forced (tested separately). |
| `MatchPlayN7Script.cs` | The deterministic N7 played run and the head-camera aim / AI pass checks. |

Modified (additive; N6 paths kept):

* `MatchPossession.cs`: the control state machine now has the celebration/face-off phases, the head
  camera aim state, human/AI pickup candidates, AI shot/pass requests and per-scorer counters.
  `LaunchShot` takes the head-camera view fractions; `LaunchAiShot` uses the shipped unaimed launch
  branch; `Pass` is actor-generic (the exact `RokState.Throw` math).
* `MatchPlay.cs`: builds and enables the fallback AI and the head camera, sets the sim input map to
  the control, adds the N7 deterministic capture suite (12 beats, including a real head-camera
  render), and keeps the N6 suite.
* `MatchHud.cs`: two default-off fields and one draw path for the head-camera reticle ("HEAD CAM"
  plus the CHeadTrack fractions); empty off means N5/N6 frames are unchanged.
* `Assets/Editor/UBlade3MatchPlay.cs`: `CheckN7`, `CaptureAllN7` and `BuildAndRunWindowsN7` (and the
  N7 artifact paths) alongside the N6 entry points.

## 3. Post-goal celebration, face-off and restart

The shipped flow is in the plans and the engine:

* `NGOAL.PBN` / `SGOAL.PBN` `PostScoreWait4Drag` (goal handlers): HeadCamOff, NoInputForSec,
  DropRok, `SET_TIMEOUTMSG 1400` -> `id_MSG_Stop` -> `Extra0` -> DropRok; the goal plans stop the
  clock with `ClockStoppedSemaphore(1)`.
* `HUMAN.PBN` `GettingThere` (the `SkateToFaceOff` target): per `id_DCF_MyID`
  `SetLocation(28,28)` / `(28,-28)` / `(-28,28)` / `(-28,-28)`, then `RelaxYawToFace(26)` and
  `SetAnimation(42)`.
* `ROK.PBN` `RokDrag`: `SetLocation(0,0,-95)`, render flag 0 (hidden), four `InDrag` timeouts of
  700 ms; at the timeout the retail plan unsets the attach pak and auto-launches the ROK.

N7 implements the bounded direct version (the plan VM's agenda/celebration animation chain is not
ported):

1. A real goal stops the sim and clock (the exact `MatchRules.SetRunState(0, tickMs)` pause path,
   which is what the goal handlers' clock semaphore does), plays the goal/crowd audio and enters
   `GoalCelebration` for 42 ticks (the 1400 ms post-score wait at 30 Hz).
2. `BeginFaceOffDrag` applies `MatchFaceOff`: dromers 0-3 to the HUMAN `GettingThere` corner
   positions (the four `SetLocation` pairs), zero velocity, surface normal and face-the-centre
   heading; the ROK to `(0,0,-95)` with every render bone hidden. Goalies 4/5 are left where their
   goal plans put them (their plans are a separate flow and are listed as a gap).
3. After 21 ticks (the 700 ms `InDrag` wait) `FinishFaceOffDrag` drops the ROK visibly at the drome's
   spawn point on the arena floor, resumes play, resets the goal/score debounce and lets the human or
   the fallback AI take the next possession.
4. The celebration and face-off phases pause the sim (nothing moves or scores), show a
   `GOAL! score - CELEBRATION - FACE-OFF NEXT` / `FACE-OFF - CENTRE` HUD status, and the
   `UBLADE3_NATIVE_MATCH_N7_GOAL_FLOW` / `..._FACE_OFF_SETUP` / `..._FACE_OFF_RESUMED` log lines
   record the exact ticks. Determinism: setup tick 179, resume tick 200 in the N7 run (the goal was
   at tick 137).

The plan argument `PRN_MyDragStartPos` resolves `g_apPositionMarkers[4]`/`[5]` (the SurfaceYPlus/
SurfaceYMinus markers), but the shipped `SkateToFaceOff` path that the `StartDrag` messages actually
enter uses the `SetLocation(28,28)` corner pairs; N7 follows the shipped plan path, not the unused
marker-array route.

**Documented deviations:** the retail ROK auto-launch after the drag is replaced by a visible drop at
the arena centre spawn point (not a PlanVM launch); the goal plans' celebration animations and their
full `MSG_GOAL_SCORED`/stat/timer side effects are not ported; the goalies are not repositioned; the
goal plans' 1400 ms + 700 ms timings are used as 42 + 21 fixed ticks.

## 4. Head-camera aim and shot path

The original head camera is CameraObject slot 25. `cameraobject.cpp`
`CameraObject::CameraObject` (0x00421980) sets `m_flHalfFov = degToRad * 85 / 2` (an 85 degree full
FOV); `CHeadTrack::Bind` (0x00409fc0) puts the camera on the tracked skater, `CHeadTrack::Process`
(0x0040a100) takes the watch position from the skater's HEAD_BONE=9 world transform and the look
direction from the skater's `velDir`, and moves the reticle's view fractions from the tracked
skater's rotate/throttle held bits:

    flStep = ((RATING_SHOT_POWER - 6) * 0.1 + g_flCamFracFull) * flDelta
    HELD_ROTATE:   viewFracX += (right ? +step : -step)      clamp [0,1]
    HELD_THROTTLE: viewFracY += (forward ? -step : +step)    clamp [0,1]
    g_flCamFracFull = 1.0f, g_flCamWatchMidpointScale = 0.5f

`PlanAction::SendLaunchMessage` (0x004031e0) packs that state into the shot word:
`PackShotWord(viewFracX, shotPowerLevel, viewFracY)`. N7's `MatchHeadCamera` is the bounded port: the
eye is the existing `AimPosition` (the slice's head height 5.9822; the animation HEAD_BONE transform
is not exposed), the forward is the skater's body `velDir`, the up basis comes from the surface
normal, the FOV is 85 degrees, and the view fractions are the real CHeadTrack state. The human's
`LaunchShot` now uses those fractions instead of the N6 centre-only word; the HUD shows `AIM x/y`
while carrying.

Controls: while the human is carrying, the shipped keyboard rotate/throttle bindings move the
reticle: Left/Right move X and Up/Down move Y (the same held bits also steer the skater, exactly as
the single held mask is shared in this reduced input path). The shot power meter is unchanged from
N6; release fires with the current fractions.

Verification is both a capture and a shot vector:

* the N7 Editor/player capture beat `headcam` renders the real head-camera pose with the FOV and the
  reticle ("HEAD CAM" + X at `(0.622, 0.5)`) and the log line `UBLADE3_NATIVE_MATCH_N7_HEAD_CAM`
  records eye/forward/FOV/fractions;
* the headless check runs two fresh matches: a centre-aim shot and an aimed-right shot (six ticks of
  `RotateRight` during the charge). Result:
  `centreWord=0x00321a32 aimWord=0x0032253e centreAimX=50 aimX=62 aimedViewFracX=0.622
  centreVel=(0,-2.3802,3.2167) aimVel=(-0.0646,-2.3963,3.3986) velocityDelta=0.1937`; the
  head-camera fractions change the shot word and the ROK's initial velocity.

## 5. Fallback AI possession

The retail AI plans (OffWing, Defense, Goalie, Sideln) are not ported. N7 adds a bounded fallback
that uses the existing movement input mask and the same possession rules:

* `MatchAiController` gives AI slots 1 (away forward) and 2 (home forward) the `Forward` /
  `RotateLeft` / `RotateRight` mask: it steers at the free ROK, or at its target goal while carrying,
  and requests actions from the control.
* The pickup is the same `RokInRange` + `RokState.Attach` path as the human; `MatchPlayControl` now
  compares the human and AI candidates by squared distance and attaches the nearest eligible skater.
  AI pickups are counted (`aiPickups`).
* The AI shot is the shipped **unaimed** `PlanAction::Launch` branch (`RokLaunch.LaunchUnaimed`,
  already in Sim): `SendLaunchMessage` sends SHOT_UNAIMED for non-Human plans, so an AI has no
  head-camera word. Its `rand()` jitter is the fixed 0.5/0.5 pair, labelled in the log. The AI shot
  is counted (`aiShots`).
* The AI pass uses the same exact `PlanAction::Throw` math as the human pass; the actor passes itself
  as the `viewedSlot` argument of the controlled `Throw` gate (the AI's retail pass route is the plan
  VM's, not the human CHeadTrack view). AI passes are counted (`aiPasses`) and verified in a separate
  forced-pass check.

In the deterministic N7 played run the AI picks up the face-off drop at tick 247 and shoots at tick
290; its shot bounces off the arena in this run (aiGoals=0) but the shot count and trace are real.
The forced-pass scenario reaches aiPickups=1, aiPasses=1, aiSlot=2 in 77 ticks.

**Documented deviations:** a heuristic steering controller, not the plan VM; the AI shot uses the
unaimed branch and deterministic jitter; the AI pass uses the human pass action with the actor as
the viewed slot; no AI receiver/catch plan, no goalie play and no plan statistics/agendas.

## 6. Deterministic N7 played test

`MatchPlayN7Script.Run` drives the same `MatchPlayControl` as the interactive game. It stages the
human near the south goal (the documented test staging used since N6), skates to the ROK (42 ticks),
charges the shot meter (26%), captures the head-camera aim beat (six `RotateRight` ticks), resets the
aim to centre for the goal shot, scores (1-0), holds the celebration beat, captures the shipped
face-off positions, resumes play, then disables human pickup and lets the fallback AI pursue the
face-off drop until it picks up and shoots. The twelve capture beats are:

| Beat | File (Editor) | What it shows |
| --- | --- | --- |
| start | `artifacts/native-match-n7-00-start.png` | staged match, ROK on the floor, HUD 0-0 |
| pickup | `...-01-pickup.png` | human carrying, power 0% |
| charge | `...-02-charge.png` | carrying, power 26% |
| headcam | `...-03-headcam.png` | real head-camera render, 85 FOV, HEAD CAM reticle at 0.622/0.5 |
| shot | `...-04-shot.png` | shot released, ROK in flight |
| goal | `...-05-goal.png` | real goal, HUD CHI 1 - SEA 0, GOAL/CELEBRATION status |
| celebration | `...-06-celebration.png` | the 1.4 s post-goal hold |
| faceoff | `...-07-faceoff.png` | skaters at the shipped +/-28 corners, ROK hidden at (0,0,-95) |
| restart | `...-08-restart.png` | the drag ends, ROK visible on the floor, play resumed |
| ai-pickup | `...-09-ai-pickup.png` | fallback AI slot 2 carrying the free ROK |
| ai-shot | `...-10-ai-shot.png` | AI unaimed launch, ROK in flight |
| score | `...-11-score.png` | final HUD after the AI shot |

Headless result (`UBLADE3_NATIVE_MATCH_N7_CHECK_RESULT`): approachTicks=42 humanPickups=1
humanShots=1 humanGoals=1 aiPickups=1 aiShots=1 aiGoals=0 score=1-0 failures=0; faceOffSetupTick=179,
faceOffResumeTick=200, aiPickupTick=247, aiShotTick=290; a second fresh run is identical
(`UBLADE3_NATIVE_MATCH_N7_DETERMINISM ok=True`). The head-camera vector check and the AI-pass check
are in sections 4 and 5. Audio in the headless run is record-only: requested=6, goal=1, crowd=1,
impact=5, service=none.

## 7. Verification

All commands were run from the UBlade3 repository root. No 20XX/hyperblade process was running in
any run; the captures do not use `-nographics`.

### 7.1 Headless N7 check

    Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CheckN7 -logFile <temp>/ublade3-n7-check.log

Exit 0. Key lines:

    UBLADE3_NATIVE_MATCH_N7_CHECK_RESULT approachTicks=42 humanPickups=1 humanShots=1 humanGoals=1 aiPickups=1 aiShots=1 aiGoals=0 score=1-0 failures=0
    UBLADE3_NATIVE_MATCH_N7_FACE_OFF celebrationTicks=42 dragTicks=21 restartTicks=63 playerCorner=28 rokDragHeight=-95
    UBLADE3_NATIVE_MATCH_N7_DETERMINISM ok=True ...
    UBLADE3_NATIVE_MATCH_N7_HEAD_CAM_AIM ok=True fovDegrees=85 centreWord=0x00321a32 aimWord=0x0032253e centreAimX=50 aimX=62 aimedViewFracX=0.622 velocityDelta=0.1937
    UBLADE3_NATIVE_MATCH_N7_AI_PASS ok=True aiPickups=1 aiPasses=1 aiShots=0 ticks=77 aiSlot=2
    UBLADE3_NATIVE_MATCH_N7_NO_20XX running20xxProcesses=0 no20xx=True
    UBLADE3_NATIVE_MATCH_N7_COUNTS humanPickups=1 humanShots=1 humanGoals=1 aiPickups=1 aiShots=1 aiPasses=0 goals=1 score=1-0 headCamFovDegrees=85 faceOffSetupTick=179 faceOffResumeTick=200 audioProbeMismatches=0 mismatches=0
    UBLADE3_NATIVE_MATCH_N7_CHECK_OK

### 7.2 Editor capture (graphics)

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CaptureAllN7 ^
        -logFile <temp>/ublade3-n7-capture.log --ublade3-audio

Exit 0, 12 PNGs at 1280x720. The log shows `UBLADE3_MATCH_AUDIO_SERVICE ready=True channels=8` and
`UBLADE3_MATCH_AUDIO_COUNTS requested=7 played=8 failed=0 goal=1 crowd=1 impact=5 menu=0 ambient=1
service=ready`, then `UBLADE3_NATIVE_MATCH_N7_CAPTURES_OK frames=12`.

### 7.3 Windows player capture

Built with `BuildAndRunWindowsN7` (transient one-scene build; the temporary scene is deleted after
the build). The player ran with:

    -batchmode -screen-width 1280 -screen-height 720 -screen-fullscreen 0
    --n7-capture=artifacts/native-match-n7-player.png --ublade3-audio -logFile <temp>/ublade3-native-match-n7-player.log

Result:

    UBLADE3_NATIVE_PLAY_PLAYER_BUILD_OK bytes=104083091
    ... 12 native-match-n7-player-*.png captures ...
    UBLADE3_NATIVE_MATCH_N7_PLAYER_CAPTURES_OK frames=12
    UBLADE3_NATIVE_MATCH_N7_RESULT ... humanPickups=1 humanShots=1 humanGoals=1 aiPickups=1 aiShots=1 score=1-0 ...
    UBLADE3_NATIVE_MATCH_N7_AUDIO requested=7 played=8 failed=0 goal=1 crowd=1 impact=5 ambient=1 service=ready

All twelve player frames are byte-identical to the Editor frames (SHA-256 prefixes:
`6f35e8dec390, 6160be9f576e, 4f259b221984, f896975c956f, c2a0b3016ae9, c1c8a03ad62b,
2b5940ce3288, 14ca3a696aa6, fa44d1c27b5c, c11726f05be9, 0553adcc70fb, 50513895a978`). The
head-camera log line in the player is identical:
`UBLADE3_NATIVE_MATCH_N7_HEAD_CAM ... fovDegrees=85 viewFrac=(0.622,0.5)`.

### 7.4 Regressions

* N4 match check: `UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd` (run by `UBlade3Match.Check` after the N7 change; the digest is unchanged).
* N5 gameplay check: `UBLADE3_NATIVE_GAMEPLAY_CHECK_OK` mismatches=0, score 2-1 (run by `UBlade3MatchGameplay.Check` after the N7 change; `UBLADE3_NATIVE_GAMEPLAY_CHECK_COUNTS ... homeScore=2 awayScore=1 goals=2 mismatches=0`).
* N5 Editor captures: the seven `native-gameplay-*.png` files are regenerated and byte-compared to the committed blobs with `git hash-object` / `git rev-parse HEAD:...`: all seven are byte-identical (`eba95d39b80b, 83214652aa83, dca323d1f83f, 2c6918f693a0, f7fe4b684617, 5d013b7e0954, 39e5fb73ada6`).
* N6 human loop: `UBlade3MatchPlay.Check` still exits 0 with `UBLADE3_NATIVE_PLAY_CHECK_OK` (pickups=2 shots=1 passes=1 goals=1 score=1-0, mismatches=0). The N6 capture bytes are not asserted identical because the N7 goal flow adds celebration/face-off beats; the N4/N5 regressions are the byte-level gates.

* `python scripts/check_all.py` (the repository fast check list): PASS, 166 text files after the N7 sources and this document were added.
* The generated `unity/UBlade3/build-match/` player directory was removed after the player run; no Unity or 20XX process was left running.

## 8. Honest gaps

* The ADLIB plan VM is still not ported. The goal plans' celebration animation/agenda chain, the goal
  plan stat/timer side effects, the goalie plans and the retail AI plans (OffWing/Defense/Goalie/
  Sideln) are not reproduced. The new logic applies the plan positions/timings directly and documents
  every place it substitutes.
* Post-goal: the retail ROK auto-launch after the 700 ms drag is replaced by a visible dropped ROK at
  the arena spawn; goalies are not repositioned; the celebration is a paused beat plus HUD/audio, not
  the retail celebration animation.
* Head camera: the eye is the sim's head-height `AimPosition`, not the animation HEAD_BONE world
  transform; the reticle is the interim 3x5 "X"/HEAD CAM overlay, not the shipped head-cam inset
  surface and shot-power/meter art; the head camera only drives the shot word and the N7 capture; the
  main view is still the chase camera.
* Fallback AI: heuristic steering, not the plan VM; unaimed launch (the retail AI plans' real branch)
  with deterministic 0.5 jitter; AI pass uses the controlled `Throw` with the actor as the viewed
  slot; no AI receiver/catch, no goalies, no plan statistics. The deterministic AI shot bounces
  (aiGoals=0) but its pickup/shot counts and pass count are verified.
* No skater-hit collision/impact rules; the impact audio remains the labelled surrogate from N6.
  Shot/catch sound roles are still absent from the audio role set.
* ROK untextured palette art and the N5 rotation stand-in; interim HUD text; no radar/stat screens.
* Not a parity gate and not a release. The native plugin and release path were not modified.

## 9. File list

New:

* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchFlow.cs` (+ .meta)
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHeadCamera.cs` (+ .meta)
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchAiController.cs` (+ .meta)
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPlayN7Script.cs` (+ .meta)
* `artifacts/native-match-n7-00-start.png` ... `-11-score.png`
* `artifacts/native-match-n7-player-00-start.png` ... `-11-score.png`
* `docs/NATIVE-MATCH-N7.md` (this file)

Edited (additive):

* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPossession.cs`
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPlay.cs`
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHud.cs`
* `unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs`
