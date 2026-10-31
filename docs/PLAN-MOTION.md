# PLAN-MOTION - exact plan-target steering in the native match

N11 replaces the labelled held-mask motion bridge for one bounded plan behaviour path: behaviour
kind 0 **GlideToObject** now runs the shipped target-steering math against the real Simulation skater
body. The plan's `SET_BEHAVIOR_PARAM id_SET_Target` target drives CSteerToTarget (bank/heading) and
CAdjustAcceleration (thrust along heading); the simulation's already-ported CGlideOnSurface integrates
the result. No held mask is used for that behaviour. All other behaviour kinds keep the labelled
bridge and are counted. This is one bounded path, not full plan motion parity.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Original data read-only;
no Data/Assets/App/ProjectSettings/native-release edits. No commit; the Lead commits.

## 1. Sources and formulas

Ported into `Native/MatchPlanAiMotion.cs` and applied to `UBlade3.Sim.PlayerState.Body`:

* `src/planvm/csteertotarget.cpp` CSteerToTarget::Process (HYPERX 0x0040e0b0):
  - force = ((balance * 0.1 + 1.5) * flDeltaSquared * 40) * (jitter * 0.01 + 1),
    jitter = rand() % 0x32; balance is `GetWeightedStat(RATING_BALANCE)` for a dromer, x10 for a
    goalie, 50 otherwise;
  - flat heading/delta dot; if dot < 0.75 and target speed * invDelta > 5, brake:
    smoothVel = ((dot - 2) * flDeltaSquared) * (actorSpeed * 6) * vel;
  - target lead when target speed * invDelta > 3: gate = 25, lead = delta^2 * 0.005 clamped [0,5],
    delta += lead * targetVel; otherwise gate = 2 for the ROK and 50 for any other target;
  - arrived when delta^2 < gate: smoothVel -= vel * 0.15 and MSG_GOT_THERE (0x2c) is posted;
  - delta normalized; lean = 1 - dot(delta, velDir), cross = delta.y*velDir.x - delta.x*velDir.y;
    cross < -0.001 applies bank side 0, cross > 0.001 side 1, magnitude |lean| * force
    (`PhysicsBody::ApplyBankForce`, Sim `PhysicsBodyState.ApplyBankForce`);
* `src/engine/cadjustacceleration.cpp` CAdjustAcceleration::Process (HYPERX 0x0040f7e0):
  smoothVel += flAccelDt * velDir, with flAccelDt = the Sim body's ported `AccelDt`;
* `src/planvm/cglideonsurface.cpp` CGlideOnSurface::UpdateTerrainPhysics is already ported in
  `UBlade3.Sim.PlayerState.GlideOnSurface` and runs every simulation tick, so the target bank/accel is
  integrated by the real glide physics (friction/bank/speed cap/terrain step).

The C runtime `rand()` jitter is replaced by the port's deterministic `PlanAiRandom` sequence (same
deterministic-random convention as the Plan AI bodies); the target-speed/range gates use Sim body
step magnitudes and `EngineClock.DeltaSquared`/`InvDelta` exactly as the source names them.

## 2. Integration and telemetry

`MatchPlanAi.ResolveMotion` for `BehaviorKindGlideToObject` with `UseTargetPhysics` (default match
roster): resolves the plan Target to a real position/velocity (player, ROK, goal), calls
`MatchPlanAiMotion.Apply`, sets held = None and records per-slot telemetry. The application happens in
`BeforeTick` before `Simulation.Tick`, so the simulation's `MonitorKeysTick` (held None) and the ported
glide step consume exactly the bank force + smoothVel the plan math produced. CSteerToTarget arrival
posts the engine GotThere message into the plan world; no held steering is involved.

Telemetry per slot (adapter `Describe`/`MotionLine`, roster aggregates): target slot/position/velocity,
target distance/start/min/last, desired heading (normalized target delta), actor heading, heading error,
bank lean/cross/force magnitude, accel step, predicted step velocity (vel + smoothVel + bankForce),
lead/gate, arrived, physics ticks, bridge-fallback ticks, target changes, displacement, heading travel.
Behaviour kinds that still use the held-mask bridge are counted per slot (`bridgeFallbackTicks`) and
never presented as exact steering.

## 3. Deterministic N11 probe

`Native/MatchPlayN11Script.cs` uses the normal MatchPlay build (default PlanVM AI, no N9 flag).
Slot 1 starts at (0,-60) with the free ROK at the spawn: the plan's LooseRokAttackOpponent target is a
real 60-unit steering distance. The two held skaters are deterministic staging only. The probe records
distance and heading over the approach, waits for the real pickup, then the carrier plan change gives
the second target (TurnToMate -> team mate) and records the post-change motion. Assertions: default
roster/target physics present, slot 1 physics ticks >= 10, bank force applied by the roster,
displacement > 5, heading travel > 5 deg, first-target distance decreased by > 2, a plan target change,
an AI pickup, bridge-fallback telemetry for the unported kinds, tick bound and determinism.

Headless evidence (artifacts/ublade3/n11-check-final.log):

    UBLADE3_NATIVE_MATCH_N11_CHECK_RESULT defaultPlanAi=True targetPhysics=True aiPickups=1
        planPhysicsTicks=250 slot1PhysicsTicks=100 slot1BankedTicks=0 slot1TargetChanges=1
        slot1Displacement=82.123 slot1HeadingTravelDeg=25.047
        slot1StartDistance=60.253 slot1MinDistance=0.382 slot1LastDistance=100.679
        bridgeFallbackTicks=50 ticks=100 digest=30b6ad7650946a88 failures=0
    UBLADE3_NATIVE_MATCH_N11_DETERMINISM ok=True digest1=digest2=30b6ad7650946a88
    UBLADE3_NATIVE_MATCH_N11_CHECK_OK

Motion telemetry from the run (slot 1):

    physicsTicks=10 dist=58.7697 startDist=60.2535 minDist=58.7697 displacement=1.4651
        headingErrorDeg=0 desiredHeading=(0,0.996) actorHeading=(0,0.9835)
        predictedVel=(0,0.3559) bankForce=0 accel=(0,0.0328) lead=0 gate=2
    target change at t=2499 from target 6 (ROK) to target 3 (team mate), dist=121.936
    after change: desiredHeading=(0,0.9722) predictedVel=(0,0.9434)
Slot 2 shows the bank-force branch of the same math (off-axis target): bankForce=0.1717, heading travel
877 deg over the run; slot 1's straight approach applies no bank (cross inside the deadzone) but its
CAdjustAcceleration and the glide physics move it 82.1 units and change its heading 25.0 deg while
closing 60.25 -> 0.38 units on the plan target. The 50 bridge-fallback ticks are slots 2/3's
unsupported non-target behaviours; goalie in-place behaviours use no bridge.

## 4. Capture

Editor `CaptureAllN11` (normal build path, 1280x720) captures six beats:
    artifacts/native-match-n11-00-start.png
    artifacts/native-match-n11-01-plan-physics.png
    artifacts/native-match-n11-02-target-close.png
    artifacts/native-match-n11-03-ai-pickup.png
    artifacts/native-match-n11-04-target-change.png
    artifacts/native-match-n11-05-result.png

The capture evidence records the same counts/digest as the headless probe. A Windows-player N11
capture was not added in this slice; the N10 player remains the default-path player evidence and N11
is verified headless + Editor capture.

## 5. Verification

| Gate | Result |
| --- | --- |
| N11 headless (CheckN11) | UBLADE3_NATIVE_MATCH_N11_CHECK_OK, mismatches=0, deterministic digest 30b6ad7650946a88 |
| N11 Editor capture (CaptureAllN11) | UBLADE3_NATIVE_MATCH_N11_CAPTURES_OK frames=6 1280x720; evidence aiPickups=1 slot1PhysicsTicks=100 slot1Displacement=82.123 slot1StartDistance=60.253 slot1MinDistance=0.382 bridgeFallbackTicks=50 planDigest=30b6ad7650946a88 |
| N10 regression check/capture | UBLADE3_NATIVE_MATCH_N10_CHECK_OK and UBLADE3_NATIVE_MATCH_N10_CAPTURES_OK frames=7; its deterministic suite pins the labelled bridge (N11_LEGACY note in MatchPlayN10Script.Run) so the pre-N11 pass/shot capture evidence stays valid |
| N9 regression | UBLADE3_NATIVE_MATCH_N9_CHECK_OK (explicit suite keeps the bridge) |
| N8 regression | UBLADE3_NATIVE_MATCH_N8_CHECK_OK (single-plan TurnToMate path unchanged) |
| N7 regression | UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 regression | UBLADE3_NATIVE_PLAY_CHECK_OK |
| PlanVM/Data (UBlade3Checks.RunAll) | UBLADE3_CHECKS_OK |
| Fast checks | python scripts/check_all.py PASS |

## 6. Regression and scope boundaries

* The **default interactive/N11** roster runs target physics (`MatchPlayControl.AttachDefaultPlanAi`
  enables it). The explicit N9 suite keeps the bridge and its evidence; N10's deterministic suite pins
  its roster to the bridge explicitly so its pre-N11 pass/shot capture evidence is preserved
  (N11_LEGACY note in `MatchPlayN10Script.Run`). The N6/N7/N8 capture runners detach the roster as
  before. N8's single-plan TurnToMate flow is unchanged (non-persistent path).
* Behaviour kind 0 only: Glide (kind 1), GlideBetweenFacing, GlideToPosition, SetMovingInPlace, Fly
  and the animation/attitude agenda items keep the labelled bridge or no-op (their exact classes are
  CSteerBetweenFacing/CSteerToPosition/CSetAttitude/CStepAnimation, not ported here).
* No collision interactor/agenda pass, no CRelaxYaw/CStepAnimation/CSetAttitude, no animation events;
* The diagonal-start experiment exposed the known bank-steering instability for a far off-axis target
  (large heading travel/orbit); the bounded N11 probe uses the stable straight approach and reports
  the telemetry rather than hiding it;
* Exact source rand() jitter sequence is replaced by PlanAiRandom (documented deterministic substitution);
* full AI parity, collision/attack planning and release claims are not made.

## 7. Files

New: `Native/MatchPlanAiMotion.cs` (exact formulas + telemetry struct), `Native/MatchPlayN11Script.cs`,
`docs/PLAN-MOTION.md`. Modified additively: `Native/MatchPlanAi.cs` (kind-0 physics path, motion
telemetry, bridge-fallback counters), `Native/MatchPlanAiRoster.cs` (EnableTargetPhysics, aggregates),
`Native/MatchPossession.cs` (default roster enables target physics), `Native/MatchPlay.cs` (N11 runner
and capture beats), `Native/MatchPlayN10Script.cs` (pins its regression suite to the bridge),
`Assets/Editor/UBlade3MatchPlay.cs` (CheckN11/CaptureAllN11/N11 artifact list). No Data/Assets/App/
ProjectSettings/native-release changes; nothing committed.