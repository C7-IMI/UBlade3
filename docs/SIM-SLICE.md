# UBlade3 simulation slice (U3-M2, first drop)

This is the first simulation-core slice of the exact HyperBlade (1996) port. It is a headless,
dependency-free C# core (no UnityEngine types) under
`unity/UBlade3/Assets/Scripts/UBlade3/Sim/`. It is **not** the complete clone: the ADLIB plan VM,
the animation/render side, collision bubbles and the original data readers are owned elsewhere or
still TODO. Every ported function below cites the decompilation file/function that is its
specification.

Source of truth: `third_party/hyperblade-decomp` (byte-matching decompilation of `HYPERX.EXE`
v1.0.0.1). Modern reference and the fixed-step convention: `third_party/hyperblade-20XX`.

## Verification

**The original TickClock is variable-rate, not a fixed tick.** The decomp has no fixed timestep:
`GameEngine::TickClock` (0x00481130) measures the real host frame time, rejects frames over 200 ms,
and smooths the last five scaled deltas, with 18 Hz only as the initial and nominal reference.
This is recorded by the port-analyst in `docs/CORE-NOTES.md` section 2. The fixed 30 Hz step used by
this slice's headless checks and by the Unity bootstrap is the 20XX modernisation
(`third_party/hyperblade-20XX/src/engine/framepacing.h:14` `kTickMs = 1000.0f / 30.0f`): it is a
deliberate port choice for determinism, **not** a claim that the original ran a fixed 30 Hz tick.
The decomp path is kept as `EngineClock.AdvanceHostFrame` and is the path a frame-trace parity run
against the original must use; `AdvanceFixedTick` exists only for the 20XX-style batch pipeline and
the Unity bootstrap. The 30 Hz self-check asserts the fixed path's arithmetic, not original timing.

Compile check (zero warnings, zero errors; no UnityEngine references):

    # temp project outside the repo, C# 9 (Unity 6000.x language level), all Sim/*.cs + a Main
    dotnet build
    dotnet run
    sim slice: 209/209 checks passed; arena 105/187.5/105 fric 0.01 elas 0.95; fov 85 bungee 12; tick 30 Hz

The same checks are reachable from Unity through `UBlade3.Sim.SimSelfCheck`:

- `Describe()` - cached one-line status (called by `App/PortLab.cs`).
- `Check()` - runs the checks, true when all pass.
- `RunAllChecks()` / `Report()` - full run and report text.

The checks tick the fixed 30 Hz clock, assert the sourced constants, and exercise the arena
ellipsoid (inside/outside, ray near/far, normals, floor height, radius for direction), the match
markers and goal boxes, the physics body (bind, axes, steering, surface reset), glide terrain
physics, the speed burst, the Rok value/multiplier state machine, goal score codes, period/pause/
restart clock rules, the camera projection matrix at 1920x1080, camera history windows, the chase
camera and offset modes, and Rok carry/throw.

## Unit conventions

| Quantity | Convention | Source |
| --- | --- | --- |
| length | game unit (cm-scale; only the projection converts, at 72 dpi / 2.54 cm per inch) | `cameraprojectionstate.cpp` Configure |
| angle | radians in simulation math; degrees at data boundaries (azimuth, elevation, 360-turn) | `globals.cpp` `g_flDegToRad`, `playerobject.cpp` |
| time | seconds in `EngineClock.flDelta`; 32-bit milliseconds in `nTickMs` (wraps after 49 days like `timeGetTime`) | `engineclock.h`, 20XX `clock.h` |
| up axis | +z up; the arena long semi-axis is Y (between the goals) | `physics/upaxis.h` `g_vUpAxis = (0,0,1)`, `arenaellipsoidstatic.cpp` |

## Constants (all sourced)

| Constant | Value | Decomp source |
| --- | --- | --- |
| arena base semi-axes | 70 / 125 / 70 | `src/engine/arenaellipsoidstatic.cpp` |
| tracking ellipsoid scale | 1.5 | `src/common/staticinitglobals.cpp` `g_flTrackingEllipsoidScale` |
| **runtime arena ellipsoid** | **105 / 187.5 / 105** | arenaellipsoidstatic.cpp multiplies the base axes by the scale |
| arena friction / elasticity | 0.01 / 0.95 | `src/globals.cpp` `g_flArenaEllipsoidFriction/Elasticity` |
| camera-track shell | 102.8 / 185.3 / 102.8, friction 0.01, elasticity 0.5 | `trackingellipsoidstatics.cpp` (arena - 2.2) |
| camera FOV | 85 degrees (half 0.7417649 rad) | `cameraobject.cpp` `g_flDegToRadCamera * 85 / 2` |
| bungee length | 12 | `cameraobject.cpp` constructor |
| camera history | 6 pos / 4 dir at start, capacity 25, weights 1/6 and 1/4 | `cameraobject.cpp`, `cameraobject.h` |
| camera heights | look 2.0, tracking 0.5, min 1.0, solar wind 30, track gain 2, frac full 1 | `globals.cpp` |
| shake z | 3.0 (x/y scales -0.01 / -0.001) | `cameraobject.cpp` |
| clock ring | 5 frames, max host frame 200 ms, fifth-of-frame weight 0.2 | `engineclock.h`, `gameengine.cpp` TickClock |
| match clock seed | 18 f/s, 1/18 s step | `gameengine.cpp` InitMatchTimers |
| fixed simulation tick | 1000/30 ms (30 Hz) | 20XX `src/engine/framepacing.h` `kTickMs` (decomp has no fixed tick) |
| periods | first 1, final index 3, count 4 | `gameengine.cpp` TickClock |
| period length default | 300000 ms (5 min); options 3/5/7 min | `gameoptions.cpp` ReadPeriodLength default; 20XX `docs/sim_results.md` |
| countdown / overtime | 10000 ms and 9 s; overtime 10000 / 30000 ms | `gameengine.cpp` TickClock |
| gravity | 32; half 0.5; min velocity direction 0.001 | `globals.cpp` |
| movement axes | decay 0.15, shoot drive tail 1000 ms, axis floor 0.005 | `physicsbody.cpp` UpdateMovementAxes |
| turn | scale 0.072, limit 0.2 | `physicsbody.cpp` UpdateMovementAxes |
| balance pushes | 0.14/0.5, 0.12/0.4, 0.06/0.2 | `physicsbody.cpp` UpdateMovementAxes |
| steering threshold | 1.000001 (double) | `physicsbody.cpp` ApplySteeringForce |
| motion integrate | ring 4, mean 0.25, snap 50, forward gain 1.232 | `physicsbody.cpp` IntegrateMotion |
| banking | 0.1 rad; camera bone 9 | `physicsbody.cpp` UpdateBanking |
| physics loader initialisers | force increment / max accel / max speed / force relaxation / bandwidth / net tracking / stop force / burst = 1.0 | `src/app/config.cpp` (`g_flOneSteering`) |
| physics shipped values | forceIncrement 1.0, maxAcceleration 2.3, maxSpeed 0.7, forceRelaxation 1.0, bandwidth 1.0, netTracking 1.0, stopForceFactor 1.0, speedBurstFactor 0.8, viewSteepness.z 2.0 | retail `Data/DftStick.cfg` read by `src/app/config.cpp`; recorded in `docs/CORE-NOTES.md` section 4 |
| glide | carrier speed scale 0.9, bank-free speed 30, speed stat 0.05*rating+0.65, friction*2, bank gain 12 | `cglideonsurface.cpp` |
| stamina / energy | stat weight 0.0075+0.25, speed rate base 2, half-rok 0.5, defaults energy 100, ratings 5, speed 7, damage 100 | `objectstats.cpp` |
| speed burst | carrier 1.5, decay 0.8, floor 0.05, epsilon 1e-5 | `cspeedburst.cpp` |
| throw | (shot power * 0.04 + 0.8) * 110 * delta; heading z + 0.5 | `planactionlaunch.cpp` Throw |
| launch (unaimed) | (rand*0.5+0.5)*ability*10+75, delta; network *1.5; jitter 10; drop -20; gravity 32 | `planactionlaunch.cpp` Launch |
| launch (aimed) | drop (0.5-aimY*0.01)*16, -10, spin (spin-0.5)*16, range (range+175)*0.5, 0.015/0.1, caps 150 / 2*radiusY | `planactionlaunch.cpp` Launch (constants only; TODO) |
| shot word | three signed bytes: x*100, power, y*100; reset power to 50 | `planactionlaunch.cpp` SendLaunchMessage |
| head-cam meter | (tick-start)*(shotPower+12)*0.004, cap 100; bind sets 1 | `cupdateshotpower.cpp` |
| Rok value | steps of 2, clamp +-4, request 0 clears, value 1 normalises to 0 | `plancond.cpp` IncRokValue |
| goal frame | post width 1, span 9, zone height 5, debounce 400 ms | `globals.cpp`, `collisioncheck.cpp` |
| goal boxes | posts x +-(span-frame)/2 +-frame/2, mouth +-(span-2*frame)/2, upper bar y [zone-frame, zone-frame+5] | `collisioncheck.cpp` 0x004521d0 |
| multiplier capsule | self radius 1 height 10; other radius 1 height 4 | `collisioncheck.cpp` |
| control mapping | Forward/Stop/RotateLeft/RotateRight/Attack(S)/SpeedBurst(D)/Shoot(Space)/Pass(C)/QuickShot(F)/Jump(E); held bit order swaps Attack/SpeedBurst | `keyboarddeviceobj.cpp` defaults + `g_anKeyBindingOrder`, `physicsbody.h` HeldBinding |

## Mapping ledger: decomp -> C#

### Arena, math, collision

| Decomp file / function | Address | C# file / member | Status |
| --- | --- | --- | --- |
| `src/engine/arenaellipsoidstatic.cpp` globals | 0x004b345c, 0x004ceff8 | `SimConstants.ArenaConstants`, `EllipsoidStatics.Arena` | exact |
| `src/engine/trackingellipsoidstatics.cpp` | 0x004cb3d0 | `EllipsoidStatics.CamTrack` | exact |
| `src/engine/arenastate.h` ArenaState | - | `ArenaState` | exact fields; loader hook accepts plain `DromeDefinition` |
| `src/physics/collisionellipsoid.cpp` ctor | 0x0042e9b0 | `CollisionEllipsoid` ctor | exact |
| PointInside / PointNearSurface | 0x00408ea0 / 0x00408ee0 | `PointInside` / `PointNearSurface` | exact |
| SegmentIntersection / IntersectSegment / RayIntersection | 0x0042ea30 / 0x0042ea80 / 0x0042ead0 | same names | exact |
| IntersectRay | 0x0042eb00 | `IntersectRay` | exact (double solve, first-safe-axis parameterisation, the discarded FindClosest call kept as a note) |
| ComputeRadialBoundaryXY | 0x0042f040 | `ComputeRadialBoundaryXY` | exact, shipped axis bug kept |
| GetZ | 0x0042f0d0 | `GetZ` | exact; on the shell the float remainder may round either way (see deviations) |
| NormalAtPoint | 0x0042f120 | `NormalAtPoint(bool, out Vec3)` | exact maths; returns a copy instead of the shared `m_vNormal` pointer (null-at-centre preserved) |
| RadiusForDirection | 0x0042f350 | `RadiusForDirection` | exact |
| `src/math/vec3.cpp` NormalizeTo / FindClosest / Dot / Length / Cross | 0x00430180, 0x00430340, 0x0044b050, 0x00466df0, 0x004726b0 | `Vec3`, `MathOps` | exact |
| `src/math/transformobject.cpp` Compose / Multiply / ApplyScale / TransformPoint | 0x00426f40 / 0x00427400 / 0x00426b20 / 0x00403110 | `Transform4` | exact full four-by-four arm (the camera transforms carry row 3) |

### Clock, match rules, periods

| Decomp file / function | Address | C# file / member | Status |
| --- | --- | --- | --- |
| `src/engine/engineclock.h` EngineClock | - | `EngineClock` fields, `DeltaRing` | exact field order/meaning |
| `GameEngine::TickClock` clock half | 0x00481130 | `EngineClock.AdvanceHostFrame` | exact (200 ms guard, negative guard, fifth weight, ring average, reciprocals) |
| TickClock period half | 0x00481130 | `MatchRules.TickClock` | exact statement order; injury predicates are context delegates |
| `GameEngine::InitMatchTimers` | 0x00481d60 | `EngineClock.InitMatchTimers` | exact |
| `GameEngine::SetRunState` | 0x00486de0 | `MatchRules.SetRunState` | exact (pause mark and period-start fold; OS priority/screensaver side effects omitted) |
| `GameEngine::BroadcastEvent` clock effects | 0x00486a10 | `MatchRules.SceneEvent` | exact for shoot/goal/start-drag; sender filtering is a presentation concern |
| `GameEngine::PerFrameUpdate` | 0x00486b20 | `Simulation.Tick` | exact three run-state cases and playing order; renderer/view call is a hook |
| `GameEngine::BeginMatchPlay` / `RestartMatch` / `Reset` / `ResetPeriodToFirst` | 0x00481a20 / 0x00486ff0 / 0x00487550 / 0x00487580 | `MatchRules` methods | flags/scores/period exact; object and stats hooks delegated |
| `GameEngine::IncrementHomeScore/AwayScore`, `InfoBar::IncrementScore` | 0x00487530 / 0x00487540 / 0x0047ae70 | `MatchRules.IncrementHomeScore/AwayScore` | exact |
| `GameEngine::ShowRokValue` | 0x00463220 | `MultiplierRules.IncRokValue` colour outputs | state exact; material assignment is presentation |
| 20XX `FramePacing::kTickMs`, `Clock::Now` simulated stamp | - | `FixedStepClock`, `EngineClock.AdvanceFixedTick` | port convention, documented deviation below |

### Camera

| Decomp file / function | Address | C# file / member | Status |
| --- | --- | --- | --- |
| `CameraObject::CameraObject` | 0x00421980 | `CameraState.ResetToInitialState` | exact defaults/rings/FOV/bungee |
| `CameraObject::UpdateSmoothing` | 0x00421d80 | `CameraState.UpdateSmoothing` | exact |
| `CameraObject::UpdateDirSmoothing` | 0x00421ef0 | `CameraState.UpdateDirSmoothing` | exact incl. shipped y-index quirk |
| `SetPosHistorySize` / `SetDirHistorySize` | 0x00422490 / 0x004225a0 | same names | exact; zero-count divide rejected rather than allowed |
| `SetReticleState` / `SetTrackedObject` family | 0x00422450 / 0x00422c90..0x00422d30 | `CameraState` | exact |
| `CameraObject::InitViewTransforms` | 0x00422750 | `CameraState.InitViewTransforms` | projection reset and pi/2 tilt compose exact; scratch transforms omitted |
| `CameraProjectionState::CreateProjectionState` | 0x004303b0 | `CameraProjectionState.CreateProjectionState` | exact |
| `CameraProjectionState::Configure` | 0x004304a0 | `CameraProjectionState.Configure` | exact perspective arm (72 dpi, 1..1000 clip, layout extents) |
| `CameraProjectionState::BuildMatrix` | 0x00430690 | `CameraProjectionState.BuildMatrix` | perspective arm and aspect compose exact; the orthographic arm is also carried |
| `SetEyePoint` / `SubtractTranslation` | 0x00430620 / 0x00430cc0 | same names | exact |
| `CTrack::Process` | 0x00408fd0 | `CameraTrack.Process` | full port: countdown, bungee spring, smart-cam branch, offset basis, out-of-view snap, swing and secondary tracking |
| `CHeadTrack::Process` | 0x0040a100 | `HeadCamShotPower` + camera view fractions | shot-power meter ported; head bone/viewextent/reticle placement is TODO |
| `CTrackTraj::Process` | 0x0040a7b0 | - | TODO (Spline not ported) |
| `planactiontracking.cpp` SetTrackingOffset / IncrementTrackingOffset / SetBungeeLength / IncrementBungeeLength / InitRearViewPos / ResetCameraBufferSize | 0x004019a0..0x00401f80 | `CameraOffsetModes`, `CameraState.ResetCameraBufferSize` | exact |
| `game.cpp` InitMatchMarkers | 0x0047d1c0 | `ArenaMarkers.Compute` | exact incl. the shipped south-marker +6 bug |

### Player, physics, Rok, rules

| Decomp file / function | Address | C# file / member | Status |
| --- | --- | --- | --- |
| `PhysicsBody::PhysicsBody` / `BindToObject` / `ResetSpeeds` / `ResetDynamics` | 0x00407520 / 0x00407780 / 0x00407890 / 0x00407920 | `PhysicsBodyState` | exact |
| `PreUpdateTick` | 0x00407960 | `PhysicsBodyState.PreUpdateTick` | exact |
| `PhysicsBody::PerFrameTick` | 0x004079f0 | `Simulation.TickPlayer` | order ported; plan post-cleanup hook TODO |
| `UpdateMovementAxes` | 0x00407c40 | `PhysicsBodyState.UpdateMovementAxes` | exact, all constants cited |
| `ApplySteeringForce` | 0x00408080 | `PhysicsBodyState.ApplySteeringForce` | exact |
| `InitOnSurface` | 0x00408190 | `PhysicsBodyState.InitOnSurface` | exact |
| `IntegrateMotion` | 0x004082c0 | `PhysicsBodyState.IntegrateMotion` | exact |
| `LaunchDiagonal` / `IntegrateGravity` / `StepWithGravity` | 0x00408540 / 0x004085d0 / 0x004086a0 | same names | exact |
| `UpdateVelocityDirection` | 0x00408760 | `PhysicsBodyState.UpdateVelocityDirection` | exact |
| `ApplyBankForce` / `UpdateBanking` | 0x00408880 / 0x00408940 | `PhysicsBodyState` | velocity/bank maths exact; camera bone snap is a hook (bone transforms TODO) |
| `ResetOnSurface` / `TakeSurfaceNormal` | 0x00407ab0 / 0x00407a60 | `PhysicsBodyState` | exact incl. plane GetZ |
| `Object::CopyPhysicsStateTo` (jump/boost) | 0x00402dc0 | `PlayerJumpHooks.ApplyJumpCopy` | exact |
| `ObjectStats` InitDefaults / GetWeightedStat / GetScalar / ScaleTo / CopyBaseStats / UpdateMomentumAndEnergy | 0x004748e0..0x00474bb0 | `PlayerStats` | exact |
| `Player::ProcessTrickInput` | 0x004110f0 | `PlayerSteering.ProcessTrickInput` | exact |
| `CMonitorKeys::Process` | 0x00410e90 | `PlayerSteering.MonitorKeysTick`, `ApplyGoalieControl` | drive/glide paths exact; MON_ARCADE_STYLE only repeats the drive path (as in decomp) |
| `CGlideOnSurface::UpdateTerrainPhysics` | 0x0040c270 | `GlideOnSurface.UpdateTerrainPhysics` | full port; the 20XX stat-read guard is used (deviation 2) |
| `CGlideOnSurface::Release` | 0x0040cb20 | - | TODO (network integration path) |
| `CSpeedBurst::Process` | 0x004113f0 | `SpeedBurstState.Process` | exact |
| `PlanAction::Set_SpeedBurst` | 0x0045eb30 | `SpeedBurstState.Activate` | exact: period gate, target 80/period * stamina gate + 1, boost 1.05, axis.x + 0.25, friction 0.1, stationary velocity fallback |
| `RokObject::RokObject` / SetRenderSlotFlags / ActivateAtSpawnSlot | 0x00473a80 / 0x00473b90 / 0x00473c50 | `RokState` | state exact; mesh-bone hiding deferred to the presentation port |
| `PlanAction::Set_AttachPak` / `Unset_AttachPak` | 0x00401460 / 0x004015c0 | `RokState.Attach` / `Detach` | exact for the Rok (carrier history + messages); mine/weapon arm TODO |
| `CBeAttached::Process` | 0x0040dc80 | `Simulation.UpdateCarriedRok` | carried follow position/velocity exact when a transform is supplied; transform-less fallback is a documented stand-in |
| `PlanAction::SendLaunchMessage` | 0x004031e0 | `RokState.PackShotWord` | exact |
| `PlanAction::Throw` | 0x00403a30 | `RokState.Throw` | exact |
| `PlanAction::Launch` unaimed + bounce branches | 0x00403290 | `RokLaunch.LaunchUnaimed` / `LaunchBounce` | exact branches |
| `PlanAction::Launch` aimed branch | 0x00403290 | `RokLaunch.LaunchAimed` | TODO (returns false; constants transcribed) |
| `PlanCond::RokScoreValue` | 0x00465150 | `GoalRules.RokScoreValue` | exact incl. the overwritten carrier code quirk |
| `PlanCond::IsDoubleScore` / `CheckScoreOnOwnGoal` | 0x00461c00 / 0x00468b90 | `GoalRules` | exact |
| `PlanAction::IncRokValue` | 0x00463300 | `MultiplierRules.IncRokValue` | exact state, clamps, events |
| `CollisionCheck::BouncedInGoal` | 0x00469ef0 | `GoalRules.BouncedInGoal` | exact mapping (bubble intersection is data-driven) |
| `CollisionCheck` goal frame test | 0x004521d0 | `GoalFrame.Create`, `GoalConstants` | exact box geometry and answers; the decomp symbol is named `DromerLaserHurdleCollision` although its body builds the goal frame |
| `Object::ComputePoleGateNormal` | 0x00404670 | `GoalConstants` (pole gate sizes) | constants exact; the full nearest-normal query is TODO |
| `src/globals.cpp`, `src/common/staticinitglobals.cpp`, `src/app/config.cpp` constants | - | `SimConstants.*` + `PhysicsTuning` | exact values, cited per constant |

## What is exact, what is TODO

**Exact ports (statement order preserved):** the collision ellipsoid/solve, the clock ring maths,
the period/pause/restart rule machine and broadcast clock effects, the physics body integration and
movement axes, the glide terrain step, the speed burst, the trick input sequence, the object stats
maths, the Rok value/multiplier rules, the chase camera (CTrack), camera smoothing/history, the
camera projection numbers and matrix, the match markers, and the goal frame box geometry.

**Exact constants only (the full function is not yet ported):** aimed shot launch, camera track
trajectory, head-camera bone tracking, `CProjectAndBounce`, `CBeAttached` with animation
transforms, plan-action launch messages, camera pole gate normal, render-slot bone hiding.

**Hooks/TODO (not ported, clearly gated):**
- The ADLIB plan VM (all of `src/planvm`): PBN loading, behaviours, interactors, agenda items and
  all plan actions/conditions. The C++ exposes no portable representation of the plans' timing;
  the plan VM is a separate milestone and the largest remaining piece.
- Collision bubbles/interactors (`src/physics/boundbubble.cpp`, the other checks in
  `collisioncheck.cpp`): the goal/multiplier/rok hit rules currently accept loader data and
  delegate hooks.
- Animation/bones: `AimPosition`, `LocalForward`, attach transforms and the banking camera snap
  are hooks until the model/animation port supplies bone 9's world transform.
- `CGlideOnSurface::Release` network integration, `CProjectAndBounce`, `CRelaxYaw`,
  `CTrackTraj`, head-camera body, camera `ViewIt` cull/render pass.
- The original data readers (Data port) and all rendering/audio.

## Documented deviations from the decomp

1. **The original clock is variable-rate; the fixed 30 Hz step is a port choice.** The decomp's
   `GameEngine::TickClock` smooths the measured host frame time through the five-slot ring (18 Hz
   initial/nominal, 200 ms frame guard) - see `docs/CORE-NOTES.md` section 2. 20XX added
   `FramePacing::kTickMs = 1000/30` and feeds that constant into TickClock
   (`src/engine/gameengine.cpp:565`). This slice keeps both: `AdvanceHostFrame` is the decomp path;
   `AdvanceFixedTick` + `FixedStepClock` are the 20XX batch/Unity path (ring average exactly 1/30 s).
   The 30 Hz self-check is therefore a port verification, not an exactness claim about the original
   frame rate, and parity runs must use the host-frame path or a captured original frame trace.

## Files

| File | Contents |
| --- | --- |
| `SimConstants.cs` | every sourced constant, units, held-binding and period enums |
| `SimMath.cs` | Vec3, MathOps, Transform4 |
| `CollisionEllipsoid.cs` | the collision ellipsoid and the two static shells |
| `ArenaState.cs` | arena record, drome loader data structs, markers, goal frames |
| `EngineClock.cs` | the 0x4c-byte clock, host/fixed paths, fixed-step driver |
| `PhysicsBodyState.cs` | the 0x150-byte physics body and its integrators |
| `PlayerState.cs` | player stats/state, trick input, steering tick, glide, jump hook, speed burst |
| `RokState.cs` | Rok state, carry history, attach/detach, throw, unaimed launch |
| `CameraState.cs` | camera state/history/projection, CTrack port, offset modes, head-cam meter |
| `MatchRules.cs` | match context, period/pause/restart clock, run states, goal/multiplier rules |
| `Simulation.cs` | the 30 Hz frame/update structure and object wiring |
| `SimSelfCheck.cs` | headless self-check (Describe/Check/RunAllChecks/Report) |

## Next slices

1. **Plan VM + behaviours** (largest): PBN reader, behaviour/interactor state machine, agenda items
   including CGlideOnSurface::Release, CProjectAndBounce, CRelaxYaw, CTrackTraj.
2. **Animation/render bridge:** bone 9 world transforms (camera aim, banking snap, carried Rok),
   render-slot poses, GetWorldPos.
3. **Collision bubbles and rule interactors:** bound-bubble overlap, goal/multiplier/randomizer
   checks, damage gate.
4. **Data port wiring:** DromeDefinition/team ObjectMotionParams from the original config files,
   then a playable bootstrap scene.
5. **Parity harness:** compare self-check constants and later sim results against 20XX -sim.
