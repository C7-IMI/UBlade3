// UBlade3 - exact HyperBlade (1996) simulation core.
// Constants are transcribed from third_party/hyperblade-decomp (byte-matching HYPERX.EXE v1.0.0.1
// decompilation) and, where noted, third_party/hyperblade-20XX. Every value cites the decomp file,
// global or function it comes from. Do not change a value without updating docs/SIM-SLICE.md.

namespace UBlade3.Sim
{
    // The game's unit system, as the decomp uses it:
    //   * length: "game units" (cm-scale; the projection code converts at 72 dpi / 2.54 cm per inch)
    //   * time:   seconds for the clock's flDelta, milliseconds for the EngineClock tick stamps
    //   * angle:  radians in the simulation math, degrees only at data boundaries (azimuth/elevation)
    //   * z is up: the arena ellipsoid's Y semi-axis is the long axis between the goals.
    public static class SimUnits
    {
        // decomp src/app/config.cpp: "the speed and acceleration are premultiplied by the frame delta";
        // no meter/centimetre conversion is applied anywhere in the simulation itself.
        public const string Length = "game unit (cm-scale)";
        public const string Angle = "radian";
        public const string Time = "second (flDelta), millisecond (nTickMs)";
    }

    // decomp src/engine/arenaellipsoidstatic.cpp (0x004b345c..0x004b3460, 0x004ceff8)
    // and src/engine/trackingellipsoidstatics.cpp (0x004cb3d0), src/common/staticinitglobals.cpp.
    public static class ArenaConstants
    {
        // The three semi-axes before the tracking scale: 70 / 125 / 70 game units.
        public const float BaseRadiusX = 70.0f;
        public const float BaseRadiusY = 125.0f;
        public const float BaseRadiusZ = 70.0f;

        // src/common/staticinitglobals.cpp: g_flTrackingEllipsoidScale = 1.5f.
        public const float TrackingEllipsoidScale = 1.5f;

        // arenaellipsoidstatic.cpp: g_flArenaEllipsoidX/Y/Z = base * g_flTrackingEllipsoidScale.
        public const float RadiusX = BaseRadiusX * TrackingEllipsoidScale; // 105
        public const float RadiusY = BaseRadiusY * TrackingEllipsoidScale; // 187.5
        public const float RadiusZ = BaseRadiusZ * TrackingEllipsoidScale; // 105

        // src/globals.cpp: g_flArenaEllipsoidFriction = 0.01f, g_flArenaEllipsoidElasticity = 0.95f.
        public const float Friction = 0.01f;
        public const float Elasticity = 0.95f;

        // trackingellipsoidstatics.cpp: the camera trackers' shell, 2.2 inside the arena's.
        public const float CamTrackInset = 2.2f;
        public const float CamTrackRadiusX = RadiusX - CamTrackInset; // 102.8
        public const float CamTrackRadiusY = RadiusY - CamTrackInset; // 185.3
        public const float CamTrackRadiusZ = RadiusZ - CamTrackInset; // 102.8
        public const float CamTrackFriction = 0.01f;
        public const float CamTrackElasticity = 0.5f;
    }

    // decomp src/engine/cameraobject.cpp (0x00421980), src/planvm/planactiontracking.cpp,
    // src/planvm/cheadtrack.cpp, src/globals.cpp, src/ui/hudglobals.h.
    public static class CameraConstants
    {
        // cameraobject.cpp: m_flHalfFov = g_flDegToRadCamera * 85.0f / g_flTwoCamera.
        public const float FieldOfViewDegrees = 85.0f;
        // src/globals.cpp: g_flDegToRadCamera = 0.0174532923847436905f, g_flTwoCamera = 2.
        public const float DegreesToRadiansCamera = 0.0174532923847436905f;
        public const float HalfFieldOfView = DegreesToRadiansCamera * FieldOfViewDegrees / 2.0f;
        // cameraobject.cpp: m_flUnused = g_flDegToRadCamera * 0.25f (written, never read).
        public const float UnusedQuarterDegree = DegreesToRadiansCamera * 0.25f;
        // src/globals.cpp: g_flPiCamera = 3.1415927410125732f.
        public const float PiCamera = 3.1415927410125732f;
        // cameraobject.cpp: m_flBungeeLength = 12.0f.
        public const float BungeeLength = 12.0f;
        // cameraobject.cpp: the constructor seeds both rings from a look at (0, 0, -100).
        public const float CameraStartLookZ = -100.0f;
        // cameraobject.cpp: m_wPosCount = 6, m_wDirCount = 4, weights 1/6 and 1/4.
        public const int InitialPosHistory = 6;
        public const int InitialDirHistory = 4;
        public const float InitialPosWeight = 0.16666667f;
        public const float InitialDirWeight = 0.25f;
        // cameraobject.h: CAMERA_HISTORY_CAPACITY = 25 (callers clamp to [1, 25]).
        public const int HistoryCapacity = 25;
        // cameraobject.cpp: g_flCameraShakeZ = 3.0f; shake x/y are scaled -0.01 and -0.001.
        public const float ShakeZ = 3.0f;
        public const float ShakeXScale = -0.01f;
        public const float ShakeYScale = -0.001f;
        // src/globals.cpp: camera tracking globals; plan actions may overwrite them (planactiontracking.cpp).
        public const float SolarWindFactor = 30.0f;      // g_flSolarWindFactor
        public const float SteadyCamBuffer = 0.9f;      // g_flSteadyCamBuffer
        public const float LookHeight = 2.0f;           // g_flCameraLookHeight
        public const float TrackingHeight = 0.5f;       // g_flCameraTrackingHeight
        public const float MinHeight = 1.0f;            // g_flCameraMinHeight
        // src/globals.cpp: g_flCamTrackVelocityGain = 2.0f, g_flCamFracFull = 1.0f.
        public const float TrackVelocityGain = 2.0f;
        public const float FracFull = 1.0f;
        // src/planvm/planactiontracking.cpp: POS_WINDOW_SECONDS / DIR_WINDOW_SECONDS and the clamps.
        public const float PosWindowSeconds = 0.5f;
        public const float DirWindowSeconds = 0.4f;
        // src/engine/engineglobals.h: g_flCamWatchMidpointScale = 0.5f.
        public const float WatchMidpointScale = 0.5f;
        // src/globals.cpp: g_flChaseSpeedBias = 3.0f, g_flRearViewLeadFrames = 3.0f.
        public const float ChaseSpeedBias = 3.0f;
        public const float RearViewLeadFrames = 3.0f;
        // src/planvm/cheadtrack.cpp: the reticle states (cameraobject.h RETICLE_*).
        public const int ReticleOff = 0;
        public const int ReticleAim = 1;
        public const int ReticleCommit = 2;
        // src/planvm/cupdateshotpower.cpp: SHOT_POWER_MAX = 100, reset = 0x32 = 50.
        public const int ShotPowerMax = 100;
        public const int ShotPowerReset = 0x32;
        // src/planvm/cupdateshotpower.cpp: nAmount = (tick - started) * (power + 12) * 0.004.
        public const float ShotPowerRate = 0.004f;
        public const float ShotPowerStatBias = 12.0f;
        // src/planvm/planactionlaunch.cpp: the packed shot word scale (x/y fractions * 100).
        public const float ShotFracScale = 100.0f;
    }

    // decomp src/engine/engineclock.h, src/engine/gameengine.cpp InitMatchTimers (0x00481d60),
    // TickClock (0x00481130); third_party/hyperblade-20XX src/engine/framepacing.h kTickMs.
    public static class ClockConstants
    {
        // engineclock.h: CLOCK_RING_LENGTH = 5.
        public const int RingLength = 5;
        // gameengine.cpp: MAX_FRAME_TIME_MS = 200.0f; MS_TO_SEC = 0.001f.
        public const float MaxFrameTimeMs = 200.0f;
        public const float MsToSec = 0.001f;
        // gameengine.cpp TickClock: g_flFrameDelta = g_flFifthGameEngine(0.2) * ms * 0.001.
        public const float Fifth = 0.2f;
        // gameengine.cpp InitMatchTimers: MATCH_FRAME_RATE = 18.0f, MATCH_RATE_SQUARED = 324.0f,
        // MATCH_STEP = 1 / 18.
        public const float MatchFrameRate = 18.0f;
        public const float MatchRateSquared = 324.0f;
        public const float MatchStep = 1.0f / 18.0f;
        // 20XX src/engine/framepacing.h: the port steps the simulation at a fixed 1000/30 ms tick.
        // The decomp has no fixed tick: it smooths the real host frame time through the ring.
        public const float FixedTickMs = 1000.0f / 30.0f;
        // gameengine.h: ENGINE_RUN_* (m_wRunState).
        public const int RunStopped = 0;
        public const int RunPlaying = 1;
        public const int RunPaused = 2;
        public const int RunRestart = 3;
        // gameengine.cpp TickClock: pause marks. -1 means the period clock is parked.
        public const int PauseMarkNone = -1;
        // TickClock: countdown starts at 10000 ms, then the last nine seconds are announced.
        public const int CountdownStartMs = 10000;
        public const int CountdownSecs = 9;
        // TickClock: FIRST_PERIOD = 1, FINAL_PERIOD_INDEX = 3, PERIOD_COUNT = 4.
        public const int FirstPeriod = 1;
        public const int FinalPeriodIndex = 3;
        public const int PeriodCount = 4;
        // TickClock: OVERTIME_EARLY_MS = 10000, OVERTIME_FINAL_MS = 30000.
        public const int OvertimeEarlyMs = 10000;
        public const int OvertimeFinalMs = 30000;
        // src/engine/gameoptions.cpp: ReadPeriodLength default 0x493e0 = 300000 ms (5 minutes);
        // the options screen offers 3, 5 and 7 minute periods (docs/sim_results.md).
        public const int DefaultPeriodLengthMs = 0x493e0;
        public const int Period3MinMs = 180000;
        public const int Period5MinMs = 300000;
        public const int Period7MinMs = 420000;
    }

    // decomp src/physics/physicsbody.h / physicsbody.cpp (0x00407520..0x00408a60),
    // src/globals.cpp, src/app/config.cpp, src/planvm/cglideonsurface.cpp, src/engine/player.cpp.
    public static class PhysicsConstants
    {
        // src/globals.cpp: g_flGravityGameEngine = 32.0f; g_flGravity = 32.0f; g_flGravityHalf = 0.5f.
        public const float Gravity = 32.0f;
        public const float GravityHalf = 0.5f;
        // src/globals.cpp: g_flMinVelDirSpeed = 0.001f.
        public const float MinVelocityDirectionSpeed = 0.001f;
        // src/globals.cpp: g_flMovementAxisDecay = 0.15f.
        public const float MovementAxisDecay = 0.15f;
        // src/globals.cpp: g_flFrameRateReference = 80.0f.
        public const float FrameRateReference = 80.0f;
        // physicsbody.h: BODY_RING_LENGTH = 4 (the velocity history ring).
        public const int VelocityRingLength = 4;
        // physicsbody.cpp UpdateMovementAxes: SHOOT_DRIVE_TAIL_MS = 1000, AXIS_FLOOR_IDLE = 0.005,
        // TURN_SCALE = 0.072, TURN_LIMIT = 0.2.
        public const int ShootDriveTailMs = 1000;
        public const float AxisFloorIdle = 0.005f;
        public const float TurnScale = 0.072f;
        public const float TurnLimit = 0.2f;
        // physicsbody.cpp UpdateMovementAxes: the balance-rating helpers.
        public const float AccelPushBalanceScale = 0.14f;
        public const float AccelPushBias = 0.5f;
        public const float AccelPullBalanceScale = 0.12f;
        public const float AccelPullBias = 0.4f;
        public const float AccelCapBalanceScale = 0.06f;
        public const float AccelCapBias = 0.2f;
        // physicsbody.cpp ApplySteeringForce: the double threshold on flSteerScale.
        public const double SteerScaleThreshold = 1.000001;
        // physicsbody.cpp IntegrateMotion: a step over 50 snaps the body to the move target;
        // the ring mean is scaled by 0.25 and the step by delta and 1.232.
        public const float MotionSnapDistance = 50.0f;
        public const float MotionRingMeanScale = 0.25f;
        public const float ForwardSpeedDtGain = 1.232f;
        // physicsbody.cpp LaunchDiagonal: the 45-degree diagonal, 0.707f.
        public const float Diagonal = 0.707f;
        // physicsbody.cpp UpdateBanking: g_flBankingAngleRad = 0.1f; CAMERA_BONE = 9.
        public const float BankingAngleRadians = 0.1f;
        public const int CameraBone = 9;
        // physicsbody.cpp SetFlatOnTerrain / UpdateVelocityDirection epsilons.
        public const float FlatAxisEpsilon = 0.001f;
        // src/app/config.cpp: the physics dialog globals' initialisers in the decomp are all
        // g_flOneSteering = 1.0f; the preferences device file then overwrites them.
        public const float LoaderForceIncrement = 1.0f;
        public const float LoaderMaxAcceleration = 1.0f;
        public const float LoaderMaxSpeed = 1.0f;
        public const float LoaderForceRelaxation = 1.0f;
        public const float LoaderBandwidth = 1.0f;
        public const float LoaderNetTracking = 1.0f;
        public const float LoaderStopForceFactor = 1.0f;
        public const float LoaderSpeedBurstFactor = 1.0f;

        // The shipped configuration: DATA/PREFS.TXT selects Data/DftStick.cfg; the parser reads
        // 1.0 / 2.3 / 0.7 / 1.0 for forceIncrement / maxAcceleration / maxSpeed / forceRelaxation,
        // then bandwidth 1.0, netTracking 1.0, stopForceFactor 1.0, speedBurstFactor 0.8 and
        // viewSteepness.z 2.0. See docs/CORE-NOTES.md section 4 (port-analyst, retail install's
        // DftStick.cfg); the decomp's parser order is src/app/config.cpp:159-164.
        public const float ShippedForceIncrement = 1.0f;
        public const float ShippedMaxAcceleration = 2.3f;
        public const float ShippedMaxSpeed = 0.7f;
        public const float ShippedForceRelaxation = 1.0f;
        public const float ShippedBandwidth = 1.0f;
        public const float ShippedNetTracking = 1.0f;
        public const float ShippedStopForceFactor = 1.0f;
        public const float ShippedSpeedBurstFactor = 0.8f;
        // src/globals.cpp: g_vViewSteepness = (0, 0, 2); the shipped file leaves z at 2.0.
        public const float ShippedViewSteepnessZ = 2.0f;
    }

    // decomp src/planvm/cglideonsurface.cpp (0x0040c270 UpdateTerrainPhysics, 0x0040cb20 Release),
    // src/globals.cpp, src/engine/statids.h, src/engine/objectstats.cpp.
    public static class GlideConstants
    {
        // cglideonsurface.cpp: ROK_CARRIER_SPEED_SCALE = 0.9f.
        public const float RokCarrierSpeedScale = 0.9f;
        // cglideonsurface.cpp: g_flBankFreeSpeed = 30.0f.
        public const float BankFreeSpeed = 30.0f;
        // cglideonsurface.cpp: flSum = speed rating * 0.05 + 0.65.
        public const float SpeedStatScale = 0.05f;
        public const float SpeedStatBias = 0.65f;
        // cglideonsurface.cpp: flDouble = friction * 2; the bank term scales by delta * bankScale *
        // normal.z * flDouble * 12.
        public const float FrictionDouble = 2.0f;
        public const float BankTermGain = 12.0f;
        // cglideonsurface.cpp: the inv-delta * speed gate below which the bank vector is skipped.
        public const float GlideSpeedEpsilon = 0.001f;
        // src/globals.cpp: g_flSpeedRateBase = 2.0f; g_flHalfRokObject = 0.5f; g_flBaseStatsSentinel = 1.
        public const float SpeedRateBase = 2.0f;
        public const float HalfRokObject = 0.5f;
        public const float BaseStatsSentinel = 1.0f;
        // objectstats.cpp: GetWeightedStat multiplies by (energy * 0.0075 + 0.25).
        public const float StatWeightScale = 0.0075f;
        public const float StatWeightBias = 0.25f;
        // objectstats.cpp: UpdateMomentumAndEnergy: (stamina - 4) * 0.15 + 0.5 for the speed ratio.
        public const float StaminaMomentumScale = 0.15f;
        public const float StaminaMomentumBias = 0.5f;
        // objectstats.cpp: the energy drain/charge rate: 2 + stamina * 0.15.
        public const float StaminaDrainScale = 0.15f;
        // objectstats.cpp InitDefaults: energy 100, all ratings 5, speed 7, damage 100.
        public const float DefaultEnergy = 100.0f;
        public const float DefaultRating = 5.0f;
        public const float DefaultSpeedRating = 7.0f;
        public const float DefaultDamageRating = 100.0f;
    }

    // decomp src/engine/cspeedburst.cpp (0x004113f0 Process).
    public static class SpeedBurstConstants
    {
        // cspeedburst.cpp macros.
        public const float CarrierRate = 1.5f;
        public const float DecayRate = 0.8f;
        public const float StaminaFloor = 0.05f;
        public const float BoostEpsilon = 1e-5f;
        // The boost starts at the physics reset value, one (physicsbody.cpp g_flOnePhysicsReset).
        public const float BoostStart = 1.0f;
    }

    // decomp src/planvm/planactionlaunch.cpp (0x00403290 Launch, 0x00403a30 Throw),
    // src/planvm/cupdateshotpower.cpp, src/planvm/cheadtrack.cpp.
    public static class ThrowConstants
    {
        // planactionlaunch.cpp Launch: flThird = pi / 3; flLimit = (250 - dist) * flThird * 0.004 + flThird.
        public const float ThreeThrow = 3.0f;
        public const float ThrowPi = 3.1415927410125732f;
        public const float RangeLimitDistance = 250.0f;
        public const float RangeLimitScale = 0.004f;
        // Launch aimed path: drop = (0.5 - aimY * 0.01) * 16; z = drop - z - 10; swing = (spin-0.5)*16.
        public const float AimDropScale = 16.0f;
        public const float AimDropBias = 0.5f;
        public const float AimFraction = 0.01f;
        public const float AimDropOffset = 10.0f;
        public const float SpinScale = 16.0f;
        public const float SpinBias = 0.5f;
        // Launch aimed path: flHalf = (range + 175) * 0.5; flScale = (flHalf*0.015 + 0.1) * reference/length.
        public const float RangeBias = 175.0f;
        public const float RangeScaleA = 0.015f;
        public const float RangeScaleB = 0.1f;
        // Launch aimed path: long shots cap at 2 * arena radius Y and reference speed = maxSpeed * 5;
        // other shots cap at 150 and reference = maxSpeed.
        public const float LongShotCap = 150.0f;
        public const float LongShotSpeedFactor = 5.0f;
        // Launch unaimed path: speed = (rand*0.5 + 0.5) * offensive ability * 10 + 75, times delta;
        // network sessions scale by 1.5; the flight-time gravity uses delta^2 * 32.
        public const float UnaimedRandScale = 0.5f;
        public const float UnaimedAbilityScale = 10.0f;
        public const float UnaimedBias = 75.0f;
        public const float UnaimedNetworkScale = 1.5f;
        public const float UnaimedJitter = 10.0f;
        public const float UnaimedDrop = 20.0f;
        public const float UnaimedGravity = 32.0f;
        // Launch bounce branch: flBump = delta * 10; a slower body is raised to speed * 0.5.
        public const float BounceBumpRate = 10.0f;
        public const float BounceBumpScale = 0.5f;
        // planactionlaunch.cpp Throw: speed = (shot power * 0.04 + 0.8) * 110 * delta; velDir.z += 0.5.
        public const float ThrowPowerScale = 0.04f;
        public const float ThrowPowerBias = 0.8f;
        public const float ThrowSpeedBase = 110.0f;
        public const float ThrowRise = 0.5f;
        // planactionlaunch.cpp SendLaunchMessage: the shot word packs three signed bytes.
        public const float ShotWordFracScale = 100.0f;
        // object.cpp CopyPhysicsStateTo: the jump/boost copy scales velocity by 1.2 and flips z up.
        public const float JumpVelocityBoost = 1.2f;
    }

    // decomp src/engine/rokobject.h/.cpp, src/planvm/planactionanim.cpp Set_AttachPak (0x00401460),
    // Unset_AttachPak (0x004015c0), src/planvm/plancond.cpp IncRokValue (0x00463300),
    // RokScoreValue (0x00465150), src/planvm/planstate.cpp ActivateBehavior.
    public static class RokConstants
    {
        // rokobject.h: SPAWN_SLOT_ANY = 0xffffffff; carrier history starts at 0/0 and odd 1/1.
        public const uint SpawnSlotAny = 0xffffffffu;
        public const int InitialCarrierSlot = 0;
        public const int InitialPrevCarrierSlot = 0;
        public const int InitialOddCarrierSlot = 1;
        public const int InitialOddPrevCarrierSlot = 1;
        public const int InitialKillState = -1;
        // pactionanim.cpp Set_AttachPak posts MSG_GOT_IT / MSG_ATTACH; Unset clears pAttach.
        public const float AttachOffsetEpsilon = 0.001f;
        // plancond.cpp IncRokValue: ARENA_TRACK_UP = 7, ARENA_TRACK_DOWN = 8; steps of 2, clamp +-4.
        public const int ArenaTrackUp = 7;
        public const int ArenaTrackDown = 8;
        public const int RokValueStep = 2;
        public const int RokValueMax = 4;
        public const int RokValueMin = -4;
        // plancond.cpp RokScoreValue: the goal's plan result codes (1x/2x/4x) and the carrier bit.
        public const int ScoreValueResult1x = 0;
        public const int ScoreValueResult2x = 1;
        public const int ScoreValueResult4x = 2;
        public const int ScoreValueResultSpawn = 3;
        public const int ScoreValueResultCarried = 4;
        // rokobject.cpp SetRenderSlotFlags: bone slot by |value|: 0/1 -> 0, 2 -> 7, 4 -> 8,
        // carried/kill state -> 2.
        public const int RenderBonePlain = 0;
        public const int RenderBoneDouble = 7;
        public const int RenderBoneQuad = 8;
        public const int RenderBoneCarried = 2;
    }

    // decomp src/physics/collisioncheck.cpp (0x004521d0 goal frame/post test, 0x00469ef0
    // BouncedInGoal, 0x00469f90 DromerMultiplierCollision), src/engine/object.cpp (0x00404670
    // ComputePoleGateNormal), src/globals.cpp.
    public static class GoalConstants
    {
        // src/globals.cpp: g_flGoalFrameWidth = 1.0f, g_flGoalSpanWidth = 9.0f.
        public const float FrameWidth = 1.0f;
        public const float SpanWidth = 9.0f;
        // collisioncheck.cpp: g_flGoalZoneHeight = 5.0f, g_nGoalScoreDebounceMs = 400.
        public const float ZoneHeight = 5.0f;
        public const uint ScoreDebounceMs = 400;
        // collisioncheck.cpp: the goal post box insets: flInner = (span - frame) * 0.5;
        // flOuter = (span - 2 * frame) * 0.5; the upper bar box is y [zone - frame, zone + 5].
        public const float PostInner = (SpanWidth - FrameWidth) * 0.5f;   // 4
        public const float PostOuter = (SpanWidth - 2.0f * FrameWidth) * 0.5f; // 3.5
        public const float UpperBarExtraHeight = 5.0f;
        // collisioncheck.cpp: the multiplier capsule radii/heights (self 1.0/10, other 1.0/4).
        public const float MultiplierSelfRadius = 1.0f;
        public const float MultiplierSelfHeight = 10.0f;
        public const float MultiplierOtherRadius = 1.0f;
        public const float MultiplierOtherHeight = 4.0f;
        // collisioncheck.cpp: the generic capsule pair (self 0.5/10, other 1.0/4).
        public const float CapsuleSelfRadius = 0.5f;
        public const float CapsuleSelfHeight = 10.0f;
        public const float CapsuleOtherRadius = 1.0f;
        public const float CapsuleOtherHeight = 4.0f;
        // collisioncheck.cpp: the velocity box check: size 1x1x4, drop 4, speed gate 50.
        public const float VelBoxSizeX = 1.0f;
        public const float VelBoxSizeZ = 1.0f;
        public const float VelBoxSizeY = 4.0f;
        public const float VelBoxDropY = 4.0f;
        public const float VelHitSpeedGate = 50.0f;
        // object.cpp: the pole gate geometry: post width 3, span 9, height 10, zone = post * 10.
        public const float DromePostWidth = 3.0f;
        public const float DromeSpanWidth = 9.0f;
        public const float DromeHeight = 10.0f;
        public const float PoleZoneScale = 10.0f;
        // game.cpp InitMatchMarkers (0x0047d1c0): the marker rules.
        public const float MarkerGoalNorthYFactor = 0.75f;
        public const float MarkerGoalSouthYFactor = -0.75f;
        public const float MarkerRimEastXOffset = 2.0f;
        public const float MarkerRimEastZ = 10.0f;
        public const float MarkerSurfaceY = 6.0f;
        public const float MarkerSurfaceXInset = 0.2f;
        public const float MarkerSurfaceCentreInset = 4.0f;
        public const float MarkerGoalMouthYInset = 3.0f;
        public const float MarkerGoalMouthZ = -14.0f;
    }

    // decomp src/engine/objectclasses.h, src/engine/gameengine.h, src/engine/statids.h.
    public static class ObjectConstants
    {
        // objectclasses.h: the fixed slots.
        public const int SlotDromer0 = 0;
        public const int SlotDromer1 = 1;
        public const int SlotDromer2 = 2;
        public const int SlotDromer3 = 3;
        public const int GoalieSlotA = 4;
        public const int GoalieSlotB = 5;
        public const int PlayerObjectCount = 6;
        public const int PlayerSlotCount = 4;
        public const int PlayerSlotMask = 3;
        public const int SlotRok = 6;
        public const int SlotNone = -1;
        public const int SlotWildcard = 999;
        public const int SlotNGoal = 7;
        public const int SlotSGoal = 8;
        public const int CameraMainSlot = 9;
        public const int SlotDrome = 10;
        public const int SlotGod = 23;
        public const int SlotSoundMan = 24;
        public const int CameraHeadSlot = 25;
        public const int SlotMultiplier = 27;
        public const int SlotMine = 54;
        public const int SlotSwarf = 55;
        public const int SlotKeyboard = 86;
        public const int SlotGamepad0 = 87;
        // gameengine.h defines ENGINE_SLOT_COUNT 0x66 (gameengine.cpp line 4129).
        public const int EngineSlotCount = 0x66;
        // teamplayerrow.h: TEAM_ROW_COUNT = 4.
        public const int TeamRowCount = 4;
        // statids.h: the rating and match-stat counts.
        public const int RatingCount = 0x3f;
        public const int GoalieRatingCount = 16;
        public const int MatchStatCount = 0x31;
        // statids.h: the ratings the first slice reads.
        public const int RatingWeightClass = 0x00;
        public const int RatingStamina = 0x01;
        public const int RatingChecking = 0x02;
        public const int RatingBalance = 0x03;
        public const int RatingSpeed = 0x04;
        public const int RatingShotPower = 0x05;
        public const int RatingShotAccuracy = 0x06;
        public const int RatingOffensiveAbility = 0x08;
        public const int RatingLongShotEnable = 0x09;
        public const int RatingMultiPointSpread = 0x0c;
        public const int RatingRokHandling = 0x0d;
        public const int RatingDamage = 0x0f;
        public const int RatingMultiplier0 = 0x27;
        public const int RatingMultiplier1 = 0x28;
        public const int RatingSlotOffset = 1; // m_aStats[nRating + 1]
        // objectclasses.h + behaviorids.h: the behavior kinds a camera or player can be in.
        public const int BehaviorGlide = 1;
        public const int BehaviorFly = 3;
        public const int BehaviorRailSlide = 2;
        public const int BehaviorFlyToward = 12;
        public const int BehaviorTrackTrajectory = 10;
        public const int BehaviorHeadTracking = 20;
    }

    // decomp src/physics/physicsbody.h: the ten held input bindings.
    [System.Flags]
    public enum HeldBinding
    {
        None = 0,
        Forward = 0x001,
        Stop = 0x002,
        RotateLeft = 0x004,
        RotateRight = 0x008,
        SpeedBurst = 0x010,
        Attack = 0x020,
        Shoot = 0x040,
        Pass = 0x080,
        QuickShot = 0x100,
        Jump = 0x200,
        Throttle = Forward | Stop,
        Rotate = RotateLeft | RotateRight,
        Movement = Throttle | Rotate,
    }

    // decomp src/engine/engineclock.h (EngineClock::m_nPeriod).
    public enum MatchPeriod
    {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3,
    }
}
