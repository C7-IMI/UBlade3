# NATIVE-PLAY - the first playable native C# match loop (N6)

This is the implementation, controls, evidence and honest limits of the N6 playable match layer on
top of the N4 native match and the N5 gameplay rules. The goal is a **first truly playable C# match
loop**, not a parity claim: the human home skater picks the ROK up through the shipped possession
path, carries it, charges and shoots it with the ported launch action, passes it with the exact
throw action, and a real shot that enters the goal mouth scores through the shipped goal rules and
updates the HUD. Match events route to the original-wave player in `UBlade3.Audio`.

Nothing in this milestone copies an original asset, and no original data is committed. The existing
N4 match check and the N5 gameplay check/captures are unchanged (the N5 artifact PNGs were
byte-compared after the change; section 6.4).

## 1. Sources of truth

| Source | Use |
| --- | --- |
| `third_party/hyperblade-decomp` | Byte-matching decompilation of `HYPERX.EXE` v1.0.0.1. The pickup, launch, throw, flight, goal and audio routing below cite the exact functions. |
| `third_party/hyperblade-20XX` and `tools/plan_dump.py` | The compiled plans were decoded to pseudo-source to recover the message routes: `INPUT.PBN`, `HUMAN.PBN`, `CARRYROK.PBN` and `ROK.PBN`. |
| `D:\Games\HyperBlade\HYB\data` | The shipped install the runs read: HTM/ENV/CFG, drome models, HUD art and `Data\Sounds`. Read-only, never committed. |
| `unity/UBlade3/Assets/Scripts/UBlade3/Sim` and `.../Native/Match*` | The N4/N5 layers this builds on: `MatchScene`, `MatchSimulation`, `MatchGameplay`, `MatchGameplayRules`, `MatchRokView`, `MatchHud`. |
| `unity/UBlade3/Assets/Scripts/UBlade3/Audio` | `AudioService`, `AudioBootstrap`, `SoundCatalog`, `AudioRole` and `AudioProbe` from `docs/NATIVE-AUDIO.md`. |

## 2. Controls (interactive player)

| Key | Action |
| --- | --- |
| Up / Down | accelerate / brake |
| Left / Right | steer |
| D / S | strafe |
| Space (hold) | charge the shot meter while carrying; release = shoot (`PlanAction::Launch`) |
| C | pass (`PlanAction::Throw`) |
| Escape | pause/unpause; plays the menu click role |
| Skate into the ROK | pick it up; first contact within the shipped `RokInRange` gate attaches |

The viewed slot is the human home skater (slot 0, `MatchSimulation.HumanSlot`). The status line under
the HUD shows `ROK FREE`, `ROK CARRIED - POWER n%`, `ROK IN FLIGHT` or
`GOAL - score - resetting`. The other skaters still run the N4/N5 AI fallback (their plan behaviour
is unchanged; they do not pick the ROK up).

## 3. What the playable path uses (and what is new)

New files, all in `unity/UBlade3/Assets/Scripts/UBlade3/Native/`:

| File | Contents |
| --- | --- |
| `MatchPlay.cs` | The scene component: builds the play layer on `MatchScene`/`MatchGameplay`, owns the fixed-tick driver, the match camera/HUD sync, the deterministic played suite, and the seven capture beats. |
| `MatchPossession.cs` | The Unity-free play rules: pickup (`RokInRange`/attach), the launch port, the pass call, the free-ROK flight integrator, the swept goal test, the deterministic script. |
| `MatchAudioDirector.cs` | Routes goal/crowd/impact/menu events to `UBlade3.Audio.AudioService` and logs `UBLADE3_MATCH_AUDIO_*`. |

Two existing N5 files got a narrowly additive change, each default-off so the N5 captures stay
byte-identical:

* `MatchGameplay.cs`: `ExternalTickDriver` (default false). When the N6 layer is built it sets the
  flag and drives the fixed ticks itself; otherwise the N5 component behaves exactly as before.
* `MatchHud.cs`: `StatusLine` (default empty). Non-empty draws the N6 possession/control line
  centered at the bottom; empty leaves the layout unchanged.

One Editor gate file: `unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs` (Check, CaptureAll,
BuildWindows, BuildAndRunWindows). No file outside `Native/Match*`, that Editor file and this
document was edited.

### 3.1 Pickup

The decoded plans show the real route: `ROK`'s `GlidingFree` behaviour broadcasts
`id_MSG_OkToGetRok` to all skater slots with the `RokLookForDromers` data 120; a `HUMAN` skater
answers with `id_MSG_Attach` when `RokInRange` says yes; the `ROK`'s `MSG_Attach` handler calls
`Set_AttachPak` after `OkToGetRok`.

`MatchPossessionRules.RokInRange` is a direct port of `PlanCond::RokInRange` (HYPERX 0x00468c40,
`plancond.cpp:8508`): squared distance in 3D against
`((damage*rokHandling*0.01 + rokHandling)*0.5 - 7)*0.05 + 1` scaled by the range argument, capped
at 150, then the facing test `dot(velocity, self-rok) > -0.8`. The range argument is 1 and the
data-120 flag makes it `(sqrt(1)+1)^2 = 4`, which is what the shipped plans pass.

The pickup itself is `RokState.Attach` with an `AttachPak` (the exact `Set_AttachPak` action,
`planactionanim.cpp:0x00401460`); the carried follow is the existing N5 `Simulation.UpdateCarriedRok`
(`AimPosition + offset`). The offset is a documented stand-in: the retail action gets it from the
carrier's ROK render bone, which the slice does not have; the scene uses chest/hand height
`(0,0,-1.2)` relative to the 5.9822 head offset. The engine's `OkToGetRok` carrier-history quirk
(initial carrier slot 0, last carrier cannot re-grab) would block the single viewed home slot 0 in
a one-human scene; the scene uses the same condition's free-ROK answer (`pAttach == 0`) plus the
exact `RokInRange` gate, and records the deviation. Pickup also has a 300 ms scene catch cooldown so
a player cannot re-grab the ROK on the same tick it leaves their hand; the plan VM's catch/lost-it
rules are not ported.

### 3.2 Shot and pass

Decoded route: `INPUT.PBN` (human) handles the pad/key press, `CARRYROK.PBN` forwards
`id_MSG_Shoot` to slot 6 (the ROK), and the ROK decides `OkToLaunchRok`, calls `Launch`, then
`Unset_AttachPak`. The shot word is packed by `PlanAction::SendLaunchMessage` (0x004031e0) from
the head camera's `viewFracX`, shot-power level and `viewFracY`.

* The scene has no head camera, so the shot word is `PackShotWord(0.5, power, 0.5)`: screen-centre
  aim, power from the real shot meter. The meter uses the shipped `HeadCamShotPower.Amount` curve
  (`CameraState.cs`) while Space is held; releasing fires with the measured level (minimum 1).
* `MatchPossessionRules.LaunchShot` ports the aimed branch of `PlanAction::Launch` (HYPERX
  0x00403290, `planactionlaunch.cpp:118-388`): the facing guard
  `acos(dot(goal-carrier, carrierDir)) > limit` weak-fling branch, the
  `pRok = (-rok.x, side - rok.y, drop - rok.z - 10)` aim vector, the up-axis swing for `aimX`, the
  `half = (range+175)*0.5`, `reference = forwardSpeedDt` (`x5` for long shots), `scale`, the
  `cap = 150 / 2*radiusY` branch and the rise term, then `RokState.Detach`. The weak branch
  substitutes the decomp's `rand()` with the fixed 0.5 and says so in the log.
* `RokState.Throw` is the exact existing port of `PlanAction::Throw` (0x00403a30): speed
  `(weighted shot power*0.04 + 0.8)*110*delta`, direction = carrier velocity direction with
  `z += 0.5`, then `Unset_AttachPak`. C is mapped to `HeldBinding.Pass`, the same key the plan input
  path reads.
* The loader binds bodies with the pre-play clock delta (1/18 s in this slice), so the ROK's
  `flForwardSpeedDt` and gravity step are stale for the fixed 30 Hz match. The launch re-scales
  those two `PhysicsBody` values to the current delta the way `PhysicsBody::PreUpdateTick` does; the
  velocity is not scaled because the launch sets it outright.

### 3.3 Free-ROK flight

After a shot/pass the ROK is integrated by `MatchRokFlight.Step`, a bounded port of
`CProjectAndBounce::ProjectAndBounce` (HYPERX 0x0040d510, `cprojectandbounce.cpp:86-277`) plus
`PhysicsBody::IntegrateGravity`:

* gravity step with the re-scaled `GravDeltaVel`;
* below the ice, the ellipsoid segment intersection projects the ROK back to the shell and
  `PhysicsBody::UpdateVelocityDirection` reflects the velocity (the arena `CollisionEllipsoid`
  primitives);
* above the ice the radial x/y boundary projection + vertical-velocity preservation branch;
* angular speed cap `2 * forwardSpeedDt`, velocity direction, surface normal, step.

The original agenda item also runs the spring/net-target mode, the landing agenda chain and the
partial re-projection; those are not ported (section 8). Every impact (a bounce, a pickup, a
shot/pass release) is routed to the audio impact role as a labelled surrogate until skater-hit
collision rules exist.

### 3.4 Goal and score

`MatchGoalDetector` from N5 already contains the shipped goal rules: score-region
`COB_*_GOAL_PORT` boxes (`GoalFrame.Mouth`), `RokScoreValue`, `PointsForScoreValue`, the 400 ms
debounce, `MatchRules.IncrementHomeScore`/`IncrementAwayScore` and the scene broadcast. N6 adds the
missing piece for a fast shot: a 16-sample segment test from the pre-boundary flight path
(`MatchPossessionRules.SegmentEntersGoal`), because a 30 Hz shot can move several units a tick and
the N5 point test could tunnel through the 1 unit deep mouth. The segment is tested before the wall
projection; that matters because the same tick can cross the goal line and hit the back wall.

When a score is detected the layer increments the score through `MatchRules`, plays the goal and
crowd roles, locks the goal until the reset, stops the flight and after 45 ticks drops the ROK back
on the arena floor at the drome's spawn point (`MatchScoreFixture.PlaceRokAtSpawn`) and clears the
goal lock. The retail goal/face-off restart plans are not ported; this reset is the labelled fallback
that makes the loop playable again. The score itself is the shipped score path: the deterministic run
ends 1-0 and the HUD reads `CHI 1 / SEA 0`.

### 3.5 HUD

N5's original-art HUD (`frper`/`frdamgA`/`frdamgB` period frame, team badges, `hudChi`/`hudsea`
logos, interim 3x5 text) now shows the live `MatchHudModel` scores, period and clock from the same
`Sync` path. The N6 status line is the only new text (labelled `ROK ... / SPACE ... / C PASS`); it is
empty in the N5 suite, so those frames do not change.

## 4. Match-driven audio

`MatchPlay` installs `UBlade3.Audio.AudioService` only when the run opted in with `--ublade3-audio`
or `UBLADE3_AUDIO=1` (the `AudioBootstrap` contract from `docs/NATIVE-AUDIO.md`). A
`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` hook points the audio layer's `GameDataRoot` at the
same install `MatchScene.ResolveRoot()` found before the bootstrap auto-install runs; the build path
sets the same environment variable as an edit-mode fallback. Default runs are record-only: the same
event plan runs, counts are logged, and nothing touches audio, so captures stay deterministic and
headless checks do not need an audio device.

| Match event | Call | Original role (from NATIVE-AUDIO) |
| --- | --- | --- |
| goal (1/2/4 points) | `PlayGoal(points)` | 74/75/76 `Sxrscra2/3/4` |
| crowd reaction | `PlayGoalCrowd(southGoal)` | 18 cheer for the home (south) goal, 21 boo for the away (north) goal |
| ROK pickup / shot release / pass release / arena bounce | `PlayImpact()` | alternating hit group 0 (labelled surrogate; retail uses these for skater hits) |
| Escape pause/unpause | `PlayMenuUi()` | 289 `Sxbutt1` |
| match start | `PlayCrowdAmbient()` | looping crowd 15 `Sxxambb2` |

Every routed event logs `UBLADE3_MATCH_AUDIO_EVENT kind=... played=...`; the summary is
`UBLADE3_MATCH_AUDIO_COUNTS requested=... played=... goal=... crowd=... impact=... menu=...
ambient=... service=ready/not-ready`. The Editor capture and the Windows player below ran with audio
on: `UBLADE3_AUDIO_SERVICE_READY entries=363 files=295`, service `ready=True`, and 7
`AudioService` play calls were made (goal, crowd, four impacts, ambient). The headless check runs
record-only: `requested=5 played=0`, which proves the routing/counts without an audio device.

## 5. Deterministic played test

`MatchPlayScript.Run` is the same state machine the interactive game uses. The test stages the human
on the centre line near the south goal (a documented test staging; the plan VM's movement/face-off
that would bring a player there is not ported), then:

1. skates the human forward at a ROK placed 30 units ahead (`PlaceRokForApproachTest`) and
   runs the real `RokInRange`/`RokState.Attach` pickup when contact happens
   (42 approach ticks; 2 pickups over the whole test);
2. holds Shoot for 15 ticks (500 ms) so the real shot meter reaches 26%;
3. releases Shoot -> aimed `PlanAction::Launch` port;
4. ticks the free-ROK flight (bounded `CProjectAndBounce` port) until the swept goal test fires; the
   score goes 0-0 -> 1-0 through `MatchRules`;
5. waits the 45-tick post-goal reset and captures the updated HUD;
6. re-picks the ROK and presses C -> exact `PlanAction::Throw` pass; the ROK is free and flying.

Headless result (`UBLADE3_NATIVE_PLAY_CHECK_OK`): approachTicks=42 pickups=2 shots=1
passes=1 goals=1 bounces=0 score=1-0 realFlightGoal=True determinism=True; a second fresh world/sim
produces the same counts and score. Editor capture (`UBLADE3_NATIVE_PLAY_CAPTURES_OK frames=7`) and the Windows player
(`UBLADE3_NATIVE_PLAY_PLAYER_CAPTURES_OK`) run the same script with the seven beats:

| Beat | File | What it shows |
| --- | --- | --- |
| start | `artifacts/native-play-00-start.png` | staged match, ROK on the floor, HUD 0-0 |
| pickup | `artifacts/native-play-01-pickup.png` | ROK carried at the skater, status 0%, HUD 0-0 |
| charge | `artifacts/native-play-02-charge.png` | carried, power 26%, HUD 0-0 |
| shot | `artifacts/native-play-03-shot.png` | ROK in flight, status ROK IN FLIGHT |
| goal | `artifacts/native-play-04-goal.png` | real goal, HUD 1-0, GOAL status |
| score | `artifacts/native-play-05-score.png` | after reset, HUD still 1-0 |
| pass | `artifacts/native-play-06-pass.png` | exact Throw pass, ROK flying, HUD 1-0 |

## 6. Verification

All commands were run from the UBlade3 repository root unless noted. The Windows player is launched
with a deterministic input script (the same script the Editor run uses), never by hand.

### 6.1 Headless check

    Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.Check -logFile <temp>\ublade3-play-check.log

Exit 0. Key lines:

    UBLADE3_NATIVE_PLAY_AUDIO_PROBE ok=True
    UBLADE3_NATIVE_PLAY_CHECK_RESULT approachTicks=42 pickups=2 shots=1 passes=1 goals=1 bounces=0 score=1-0 realFlightGoal=True failures=0
    UBLADE3_NATIVE_PLAY_DETERMINISM ok=True run1=pickups=2 shots=1 passes=1 goals=1 bounces=0 score=1-0 ...
    UBLADE3_NATIVE_PLAY_CHECK_RENDER_BONES ok=True mapping=plain=0 double=7 quad=8 kill=2
    UBLADE3_NATIVE_PLAY_CHECK_NO_20XX running20xxProcesses=0 no20xx=True
    UBLADE3_NATIVE_PLAY_CHECK_COUNTS rokTriangles=257 hudSprites=5 audioProbeMismatches=0 pickups=2 shots=1 passes=1 goals=1 bounces=0 score=1-0 mismatches=0
    UBLADE3_NATIVE_PLAY_CHECK_OK

### 6.2 Editor capture (graphics; no `-nographics`)

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CaptureAll ^
        -logFile <temp>\ublade3-play-capture.log --ublade3-audio

Exit 0, 7 PNGs at 1280x720; the beats are in the table above. The log shows
`UBLADE3_MATCH_AUDIO_SERVICE ready=True channels=8` and `UBLADE3_MATCH_AUDIO_COUNTS requested=6
played=7 failed=0 goal=1 crowd=1 impact=4 menu=0 ambient=1 service=ready`, then
`UBLADE3_NATIVE_PLAY_CAPTURES_OK frames=7`.

### 6.3 Windows player capture

Built with `BuildAndRunWindows` (transient one-scene build, temporary scene deleted after the build)
and launched with `-batchmode -screen-width 1280 -screen-height 720 -screen-fullscreen 0
--play-capture=<base> --ublade3-audio`. Result (from the run log):

    UBLADE3_NATIVE_PLAY_PLAYER_BUILD_OK bytes=104060883 output=.../build-match/UBlade3NativeMatchPlay.exe
    UBLADE3_NATIVE_PLAY_PLAYER_CAPTURE ... native-play-player-00-start.png bytes=89508
    ... (7 frames, all 1280x720)
    UBLADE3_NATIVE_PLAY_PLAYER_CAPTURES_OK frames=7

The player run also logs `UBLADE3_NATIVE_PLAY_RESULT pickups=2 shots=1 passes=1 goals=1 bounces=0
scoreBefore=0-0 scoreAfter=1-0 realFlightGoal=True` and `UBLADE3_MATCH_AUDIO_COUNTS requested=6
played=7 failed=0 goal=1 crowd=1 impact=4 menu=0 ambient=1 service=ready`. All seven player frames
are byte-identical to the Editor frames (section 7).

### 6.4 N5 regression

N4 match headless check, run after the N6 change, still exits 0 with
`UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd` (the same N4
digest as the N5 milestone). N5 headless check still exits 0 with `UBLADE3_NATIVE_GAMEPLAY_CHECK_OK`,
mismatches=0, score 2-1 and the same seven-step N5 fixture. The seven N5 Editor capture PNGs were
regenerated and byte-compared to the committed blobs with `git hash-object`/`git rev-parse HEAD:...`:
all seven are identical (blob prefixes `eba95d39b80b, 83214652aa83, dca323d1f83f, 2c6918f693a0,
f7fe4b684617, 5d013b7e0954, 39e5fb73ada6`). This is the regression guarantee: the N6 additive flags
are default-off.

### 6.5 Honest scope

Only `Native/Match*`, `Assets/Editor/UBlade3MatchPlay.cs` and this document were edited. `Data/`,
`Assets/` (other than the one Editor file), `Sim/`, `PlanVM/`, `App/`, `ProjectSettings/`, the native
plugin and the release were not touched. Nothing was committed; the Lead owns the commit. The
generated `build-match/` player directory was removed after the run.

## 7. Evidence summary

| Check | Result |
| --- | --- |
| Headless play check | exit 0, `UBLADE3_NATIVE_PLAY_CHECK_OK`, mismatches=0 |
| Real pickup/carry | 2 pickups, `carrying=True` in the pickup/charge captures |
| Real shot | 1 shot after a 42-tick forward approach, powered 26%, aimed launch port, ROK speed 4.002 units/tick at release |
| Real goal | 1 goal, 0-0 -> 1-0, score through `MatchRules`, HUD 1-0 |
| Pass | 1 exact Throw pass, ROK free/flight after |
| Determinism | two fresh runs identical counts/score |
| Audio (headless) | record-only, requested=5 played=0, goal/crowd/impact routed |
| Audio (capture/player) | service ready, played=7 calls (goal, crowd, 4 impacts, ambient), no failures |
| N4 match regression | `UBLADE3_NATIVE_MATCH_CHECK_OK`, digest f54169057f5800dd |
| N5 headless regression | `UBLADE3_NATIVE_GAMEPLAY_CHECK_OK`, mismatches=0 |
| N5 capture regression | 7/7 PNGs byte-identical to committed blobs |
| N6 Editor vs player | 7/7 PNGs byte-identical (`a8e398e69f9f, d9bca9a65682, 9a33249f1494, c41a5433de13, b509c7cef9e9, 6791722f5559, d38bbbbeda7b` SHA-256 prefixes) |
| ROK model | Rokmix 9 bones / 257 triangles / render-bone map plain=0 double=7 quad=8 kill=2 |
| HUD art | period 83x60, team frames 220x47, logos 73x21; interim 3x5 text |
| 20XX processes | 0 |
| Commits | none (Lead commits) |

## 8. Honest gaps (carry these forward)

* The ADLIB plan VM is still not ported. Other skaters run the N5 AI fallback; they do not pick up,
  shoot, pass or celebrate, and the human pickup/shot/pass are the direct ports of the plan
  conditions/actions those plans invoke, not the plans themselves.
* No head camera. The shot word uses screen-centre aim with the real shot power meter; the retail
  `viewFracX/Y` head-cam aim (and the head-cam visual) is not reproduced. The aimed branch's weak-shot
  facing guard uses a deterministic 0.5 instead of `rand()`.
* The launch port covers the ROK carrier branch of `PlanAction::Launch`; the non-ROK branches and the
  plan-side `MSG_SHOT_ON_GOAL` side effects are not emitted. `OkToLaunchRok`'s plan-name exceptions
  are not needed by the scene and are not reproduced.
* `OkToGetRok`'s carrier-history quirk (initial slot 0 / last-carrier re-grab) is replaced by the
  free-ROK test as documented above.
* The pass uses the exact `Throw` action but there is no receiving/catch plan: the ROK flies forward
  along the carrier's velocity direction; a teammate would need the plan VM's
  `PassTo`/`Incoming`/catch conditions to receive it.
* Flight is a bounded `CProjectAndBounce` subset: no spring/net-target mode, no landing agenda chain,
  no partial re-projection, no goal-frame/bubble collision objects beyond the arena ellipsoid. The ROK
  does not collide with skaters or the goal posts.
* Goal restart/face-off/celebration plans are not ported. The 45-tick post-goal drop and the 300 ms
  catch cooldown are labelled scene fallbacks; the clock keeps running and the HUD score is real.
* The retail impact sounds are the skater-hit group; the only match events the scene has today are
  ROK pickup, shot/pass release and arena bounce, so those call `PlayImpact` as a labelled surrogate.
  The shoot/catch sound groups are not wired (the audio role set has no shoot/catch role). Menu audio
  is wired to Escape pause/unpause only.
* The ROK is still untextured palette art and its spin/rotation is the N5 stand-in; the carried offset
  is not the animation bone offset; the HUD text is the interim 3x5 font, not shipped art.
* The N5 fixtures remain in `MatchGameplay.RunDeterministicSuite` for its test suite, but the
  interactive N6 path never calls them; the N6 path's score comes from the flight/swept-goal test. The
  N5 capture path is still the N5 suite and remains byte-identical.
* This is not a parity gate and not a release. The native plugin and release path were not modified.

## 9. File list

New:

* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPlay.cs` (+ .meta)
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPossession.cs` (+ .meta)
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchAudioDirector.cs` (+ .meta)
* `unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs` (+ .meta)
* `artifacts/native-play-00-start.png` ... `-06-pass.png`
* `artifacts/native-play-player-00-start.png` ... `-06-pass.png`
* `docs/NATIVE-PLAY.md` (this file)

Edited (additive, default-off):

* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchGameplay.cs`: `ExternalTickDriver` property +
  Update early-return.
* `unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHud.cs`: `StatusLine` field drawn only when set.
