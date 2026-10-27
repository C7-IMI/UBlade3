// UBlade3 - exact HyperBlade (1996) simulation core.
// The headless self-check: ticks the fixed 30 Hz clock, asserts the sourced constants and the
// arena/camera/steering/rule geometry, and reports what passed. It is the acceptance check for
// the first simulation slice (U3-M2) and is called from Unity by App/PortLab.cs through
// SimSelfCheck.Describe(); it has no UnityEngine dependency so it can also run under dotnet.

using System;
using System.Collections.Generic;
using System.Text;

namespace UBlade3.Sim
{
    public static class SimSelfCheck
    {
        private static bool s_ran;
        private static bool s_passed;
        private static string s_summary = "sim slice: not checked yet";
        private static string s_report = string.Empty;
        private static int s_checkCount;

        public const float Tolerance = 1e-5f;

        // PortLab's status line. Runs the checks once on first use and caches the answer.
        public static string Describe()
        {
            EnsureChecked();
            return s_summary;
        }

        // PortLab/nUnit-style entry point: true when every check passed.
        public static bool Check()
        {
            EnsureChecked();
            return s_passed;
        }

        // The full report, one line per failing check plus the pass count.
        public static string Report()
        {
            EnsureChecked();
            return s_report;
        }

        public static bool RunAllChecks()
        {
            List<string> failures = new List<string>();
            s_checkCount = 0;

            CheckConstants(failures);
            CheckClock(failures);
            CheckArena(failures);
            CheckArenaMarkers(failures);
            CheckGeometry(failures);
            CheckStats(failures);
            CheckPhysics(failures);
            CheckGlide(failures);
            CheckSpeedBurst(failures);
            CheckMultiplier(failures);
            CheckGoalRules(failures);
            CheckPeriods(failures);
            CheckCamera(failures);
            CheckCameraTrack(failures);
            CheckRok(failures);

            s_ran = true;
            s_passed = failures.Count == 0;

            StringBuilder builder = new StringBuilder();
            builder.Append("sim slice: ").Append(s_checkCount - failures.Count).Append('/').Append(s_checkCount).Append(" checks passed");
            if (s_passed)
            {
                builder.Append("; arena 105/187.5/105 fric 0.01 elas 0.95; fov 85 bungee 12; tick 30 Hz");
            }
            else
            {
                builder.AppendLine();
                foreach (string failure in failures)
                {
                    builder.Append("FAIL ").AppendLine(failure);
                }
            }

            s_summary = builder.ToString();
            s_report = s_summary;
            return s_passed;
        }

        private static void EnsureChecked()
        {
            if (!s_ran)
            {
                RunAllChecks();
            }
        }

        private static bool Check(List<string> failures, bool condition, string name)
        {
            s_checkCount++;
            if (!condition)
            {
                failures.Add(name);
            }
            return condition;
        }

        private static bool Near(float actual, float expected, float tolerance)
        {
            return Math.Abs(actual - expected) <= tolerance;
        }

        private static void CheckConstants(List<string> failures)
        {
            Check(failures, ArenaConstants.RadiusX == 105.0f, "arena radius X 105");
            Check(failures, ArenaConstants.RadiusY == 187.5f, "arena radius Y 187.5");
            Check(failures, ArenaConstants.RadiusZ == 105.0f, "arena radius Z 105");
            Check(failures, ArenaConstants.TrackingEllipsoidScale == 1.5f, "tracking ellipsoid scale 1.5");
            Check(failures, ArenaConstants.Friction == 0.01f, "arena friction 0.01");
            Check(failures, ArenaConstants.Elasticity == 0.95f, "arena elasticity 0.95");
            Check(failures, ArenaConstants.CamTrackRadiusX == 102.8f, "cam track radius X 102.8");
            Check(failures, ArenaConstants.CamTrackRadiusY == 185.3f, "cam track radius Y 185.3");
            Check(failures, ArenaConstants.CamTrackElasticity == 0.5f, "cam track elasticity 0.5");
            Check(failures, Near(CameraConstants.HalfFieldOfView, 0.7417649f, 1e-6f), "camera half fov 85/2 degrees");
            Check(failures, CameraConstants.BungeeLength == 12.0f, "bungee length 12");
            Check(failures, CameraConstants.HistoryCapacity == 25, "camera history capacity 25");
            Check(failures, ClockConstants.RingLength == 5, "clock ring 5");
            Check(failures, ClockConstants.FixedTickMs == 1000.0f / 30.0f, "fixed tick 1000/30 ms");
            Check(failures, ClockConstants.DefaultPeriodLengthMs == 300000, "default period 5 min");
            Check(failures, GoalConstants.FrameWidth == 1.0f, "goal frame width 1");
            Check(failures, GoalConstants.SpanWidth == 9.0f, "goal span width 9");
            Check(failures, GoalConstants.ZoneHeight == 5.0f, "goal zone height 5");
            Check(failures, GoalConstants.ScoreDebounceMs == 400, "goal debounce 400 ms");
            Check(failures, PhysicsConstants.Gravity == 32.0f, "gravity 32");
            Check(failures, PhysicsConstants.MovementAxisDecay == 0.15f, "movement axis decay 0.15");
            Check(failures, PhysicsConstants.TurnScale == 0.072f, "turn scale 0.072");
            Check(failures, PhysicsConstants.TurnLimit == 0.2f, "turn limit 0.2");
            Check(failures, PhysicsConstants.ShootDriveTailMs == 1000, "shoot drive tail 1000 ms");
            Check(failures, GlideConstants.RokCarrierSpeedScale == 0.9f, "rok carrier speed scale 0.9");
            Check(failures, GlideConstants.BankFreeSpeed == 30.0f, "bank free speed 30");
            Check(failures, SpeedBurstConstants.CarrierRate == 1.5f, "speed burst carrier rate 1.5");
            Check(failures, SpeedBurstConstants.StaminaFloor == 0.05f, "speed burst stamina floor 0.05");
            Check(failures, ThrowConstants.ThrowSpeedBase == 110.0f, "throw speed 110");
            Check(failures, RokConstants.RokValueMax == 4 && RokConstants.RokValueMin == -4, "rok value clamp +-4");
            Check(failures, ClockConstants.CountdownStartMs == 10000 && ClockConstants.CountdownSecs == 9, "period countdown 10 s / 9 s");
            Check(failures, ClockConstants.OvertimeEarlyMs == 10000 && ClockConstants.OvertimeFinalMs == 30000, "overtime 10/30 s");
        }

        private static void CheckClock(List<string> failures)
        {
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);

            Check(failures, clock.Period == 1, "clock starts in period 1");
            Check(failures, clock.PauseMark == -1, "clock pause mark starts at -1");
            Check(failures, Near(clock.Delta, 1.0f / 18.0f, 1e-6f), "clock starts at 1/18 s");
            Check(failures, clock.InvDelta == 18.0f, "clock starts at 18 f/s");

            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            Check(failures, Near(clock.Delta, 1.0f / 30.0f, 1e-6f), "fixed 30 Hz ticks average to 1/30 s");
            Check(failures, Near(clock.InvDelta, 30.0f, 1e-4f), "fixed 30 Hz inverse delta 30");
            Check(failures, clock.PrevDelta > 0.0f, "clock keeps the previous delta");

            FixedStepClock fixedClock = new FixedStepClock();
            fixedClock.InitMatchTimers();
            for (int i = 0; i < 6; i++)
            {
                fixedClock.Advance();
            }
            Check(failures, Near(fixedClock.Clock.Delta, 1.0f / 30.0f, 1e-6f), "FixedStepClock averages 1/30 s");
            Check(failures, fixedClock.Clock.TickMs == 199u, "FixedStepClock tick stamp 6 * 33.33 ms truncates to 199 ms");

            EngineClock host = new EngineClock();
            host.InitMatchTimers(0);
            host.AdvanceHostFrame(2000);
            Check(failures, host.LastHostFrameRejected, "host frame over 200 ms is rejected");
            host.AdvanceHostFrame(1600);
            Check(failures, host.Delta == 1.0f / 18.0f, "rejected frame leaves the average alone");
            for (uint now = 2033; now <= 2198; now += 33)
            {
                host.AdvanceHostFrame(now);
            }
            Check(failures, Near(host.Delta, 1.0f / 30.0f, 2e-3f), "33 ms host frames converge near 1/30 s");
        }

        private static void CheckArena(List<string> failures)
        {
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            Check(failures, arena.GetRadiusX() == 105.0f && arena.GetRadiusY() == 187.5f && arena.GetRadiusZ() == 105.0f, "arena statics radii");
            Check(failures, Near(arena.GetInvXX(), 1.0f / (105.0f * 105.0f), 1e-9f), "arena inv XX");
            Check(failures, Near(arena.Friction, 0.01f, 0f) && Near(arena.Elasticity, 0.95f, 0f), "arena shell friction/elasticity");

            Check(failures, arena.PointInside(Vec3.Zero), "centre is inside the arena");
            Check(failures, arena.PointInside(new Vec3(105.0f, 0.0f, 0.0f)), "x semi-axis point is on the shell");
            Check(failures, !arena.PointInside(new Vec3(105.1f, 0.0f, 0.0f)), "outside x is outside the arena");
            Check(failures, arena.PointNearSurface(new Vec3(0.0f, 186.0f, 0.0f)), "point inside the y end is near the surface");

            Check(failures, Near(arena.GetZ(0.0f, 0.0f), -105.0f, 1e-4f), "floor at the centre is z=-105");
            Check(failures, arena.GetZ(106.0f, 0.0f) == 1.0f, "GetZ outside the ellipse answers 1");

            // Exactly on the shell the float remainder can round either way: a tiny positive value
            // gives z ~= 0, a tiny negative one makes GetZ return the 1.0 "outside" answer. Both are
            // the shipped float behaviour; the y end is asserted one unit inside for an exact z.
            float yEndZ = arena.GetZ(0.0f, 187.5f);
            Check(failures, Math.Abs(yEndZ) < 1e-2f || yEndZ == 1.0f, "GetZ at the y end is 0 or the outside flag");
            Check(failures, Near(arena.GetZ(0.0f, 187.0f), ExpectedYEndInsideZ(), 0.05f), "GetZ one unit inside the y end");
            Check(failures, arena.GetZ(0.0f, 187.0f) < 0.0f, "GetZ inside the y end is below the ice");

            Vec3 normal;
            Check(failures, arena.NormalAtPoint(new Vec3(105.0f, 0.0f, 0.0f), out normal), "normal at the x pole exists");
            Check(failures, Near(normal.x, -1.0f, 1e-4f) && Near(normal.y, 0.0f, 1e-4f), "normal at the x pole points inward");
            Check(failures, !arena.NormalAtPoint(Vec3.Zero, out normal), "normal at the centre is null");

            Vec3 near;
            Vec3 far;
            int hit = arena.IntersectRay(new Vec3(0.0f, 0.0f, -1.0f), Vec3.Zero, out near, out far);
            Check(failures, hit == 1, "vertical ray hits the arena");
            Check(failures, Near(near.z, -105.0f, 1e-3f) && Near(far.z, 105.0f, 1e-3f), "vertical ray near/far are +-105");

            Check(failures, Near(arena.RadiusForDirection(new Vec3(1.0f, 0.0f, 0.0f)), 35.0f, 1e-3f), "radius for x direction = 105/3");
            Check(failures, Near(EllipsoidStatics.CamTrack.GetRadiusX(), 102.8f, 1e-4f), "cam track radius X 102.8");
            Check(failures, Near(EllipsoidStatics.CamTrack.Elasticity, 0.5f, 0f), "cam track elasticity 0.5");
        }

        private static void CheckArenaMarkers(List<string> failures)
        {
            ArenaMarkerSet markers = ArenaMarkers.Compute(EllipsoidStatics.Arena);

            Check(failures, Near(markers.GoalNorth.y, 187.5f * 0.75f, 1e-3f), "marker north goal y = 0.75 * radius Y");
            Check(failures, Near(markers.GoalSouth.y, -187.5f * 0.75f, 1e-3f), "marker south goal y");
            Check(failures, Near(markers.RimEast.x, 107.0f, 1e-4f) && Near(markers.RimEast.z, 10.0f, 0f), "marker rim east");
            Check(failures, Near(markers.GoalMouthNorth.y, 184.5f, 1e-3f) && Near(markers.GoalMouthNorth.z, -14.0f, 0f), "north goal mouth marker");
            Check(failures, Near(markers.GoalMouthSouth.y, -184.5f, 1e-3f) && Near(markers.GoalMouthSouth.z, -14.0f, 0f), "south goal mouth marker");
            Check(failures, Near(markers.SurfaceYPlus.x, 104.8f, 1e-4f) && Near(markers.SurfaceYPlus.y, 6.0f, 0f), "surface y-plus marker");
            Check(failures, Near(markers.SurfaceYMinus.z, markers.SurfaceYPlus.z, 1e-6f), "south surface marker reuses the +6 height (shipped bug)");
        }

        private static void CheckGeometry(List<string> failures)
        {
            ArenaMarkerSet markers = ArenaMarkers.Compute(EllipsoidStatics.Arena);
            GoalFrame north = GoalFrame.Create(GoalDefinition.CreateNorth(), markers);
            GoalFrame south = GoalFrame.Create(GoalDefinition.CreateSouth(), markers);

            Check(failures, Near(north.PostA.Min.x, 3.5f, 1e-5f) && Near(north.PostA.Max.x, 4.5f, 1e-5f), "north post A x span 3.5..4.5");
            Check(failures, Near(north.PostB.Min.x, -4.5f, 1e-5f) && Near(north.PostB.Max.x, -3.5f, 1e-5f), "north post B x span");
            Check(failures, Near(north.Mouth.Max.x, 3.5f, 1e-5f) && Near(north.Mouth.Max.y, 5.0f, 1e-5f), "north mouth box 3.5 wide, 5 high");
            Check(failures, Near(north.UpperBar.Min.y, 4.0f, 1e-5f) && Near(north.UpperBar.Max.y, 9.0f, 1e-5f), "north upper bar y 4..9");
            Check(failures, Near(north.MouthCentre.y, 184.5f, 1e-3f), "north goal centre on the mouth marker");
            Check(failures, Near(south.MouthCentre.y, -184.5f, 1e-3f), "south goal centre on the mouth marker");

            ArenaBuild build = new ArenaBuild();
            build.Load(new DromeDefinition(), EllipsoidStatics.Arena);
            Check(failures, build.State.Ellipsoid == EllipsoidStatics.Arena, "arena build uses the arena ellipsoid");
            Check(failures, build.GoalForSlot(ObjectConstants.SlotNGoal) == build.NorthGoal, "north goal lookup");
            Check(failures, build.GoalForSlot(ObjectConstants.SlotSGoal) == build.SouthGoal, "south goal lookup");
        }

        private static void CheckStats(List<string> failures)
        {
            PlayerStats stats = new PlayerStats();

            Check(failures, stats.GetScalar(ObjectConstants.RatingDamage) == 100.0f, "default damage rating 100");
            Check(failures, stats.GetScalar(ObjectConstants.RatingSpeed) == 7.0f, "default speed rating 7");
            Check(failures, Near(stats.GetWeightedStat(ObjectConstants.RatingSpeed), 7.0f, 1e-5f), "weighted speed at full energy is 7");

            stats.SpeedRatio = 1.0f;
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            bool draining = false;
            stats.UpdateMomentumAndEnergy(clock, true, ref draining);
            Check(failures, stats.SpeedRatio > 0.0f && stats.SpeedRatio <= 1.0f, "momentum update leaves the speed ratio in (0,1]");
        }

        private static void CheckPhysics(List<string> failures)
        {
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            ObjectMotionParams def = new ObjectMotionParams
            {
                Position = new Vec3(0.0f, 0.0f, -105.0f),
                Speed = 100.0f,
                Acceleration = 50.0f,
                AzimuthDegrees = 0.0f,
                Friction = 0.0f,
                Elasticity = 0.0f,
            };

            PhysicsBodyState body = new PhysicsBodyState();
            body.BindToObject(0, EngineObjectKind.Dromer, def, clock, PhysicsConstants.Gravity * clock.DeltaSquared);

            Check(failures, Near(body.ForwardSpeedDt, clock.Delta * 100.0f, 1e-5f), "BindToObject premultiplies speed by delta");
            Check(failures, Near(body.AccelDt, clock.DeltaSquared * 50.0f, 1e-6f), "BindToObject premultiplies acceleration by delta^2");
            Check(failures, Near(body.VelDir.x, 1.0f, 1e-6f) && Near(body.VelDir.y, 0.0f, 1e-6f), "azimuth 0 faces +x");
            Check(failures, Near(body.GravAccel, 32.0f, 0f), "bound body gravity 32");

            def.AzimuthDegrees = 90.0f;
            PhysicsBodyState side = new PhysicsBodyState();
            side.BindToObject(1, EngineObjectKind.Dromer, def, clock, PhysicsConstants.Gravity * clock.DeltaSquared);
            Check(failures, Near(side.VelDir.x, 0.0f, 1e-6f) && Near(side.VelDir.y, -1.0f, 1e-6f), "azimuth 90 faces -y");

            PlayerStats stats = new PlayerStats();
            body.HeldBindings = HeldBinding.Forward;
            body.PrevHeldBindings = HeldBinding.None;
            body.Speed = 10.0f;
            body.UpdateMovementAxes(stats, clock, PhysicsTuning.Shipped);
            Check(failures, body.Axis.x > 0.0f, "forward binding pushes the acceleration axis");
            Check(failures, Math.Abs(body.Axis.y) < 1e-6f, "forward binding leaves the steering axis alone");

            body.SurfNormal = new Vec3(0.0f, 0.0f, 1.0f);
            body.VelDir = new Vec3(1.0f, 0.0f, 0.0f);
            body.Axis = new Vec3(0.0f, 1.0f, 0.0f);
            body.SteerScale = 1.0f;
            body.ApplySteeringForce(false);
            Check(failures, float.IsNaN(body.SmoothVel.x) == false && body.SmoothVel.y != 0.0f, "steering force adds a lateral term");

            body.ResetOnSurface();
            Check(failures, Near(body.Pos.z, -105.0f, 1e-4f), "ResetOnSurface snaps to z=-105 at the centre");
            Check(failures, Near(body.VelDir.z, 0.0f, 1e-4f), "ResetOnSurface projects the heading onto the flat floor");
            Check(failures, !float.IsNaN(body.VelDir.x) && Near(body.VelDir.x, 1.0f, 1e-3f), "ResetOnSurface heading stays +x");

            body.Vel = new Vec3(10.0f, 0.0f, 0.0f);
            body.Speed = 10.0f;
            Check(failures, Near(body.Vel.LengthSquared(), 100.0f, 1e-4f), "velocity length squared");
        }

        private static void CheckGlide(List<string> failures)
        {
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            PlayerState player = new PlayerState(0);
            player.Body.Shell = EllipsoidStatics.Arena;
            player.Body.Pos = new Vec3(0.0f, 0.0f, -105.0f);
            player.Body.PrevPos = player.Body.Pos;
            player.Body.SurfNormal = new Vec3(0.0f, 0.0f, 1.0f);
            player.Body.Vel = new Vec3(100.0f, 0.0f, 0.0f);
            player.Body.VelDir = new Vec3(1.0f, 0.0f, 0.0f);
            player.Body.Friction = 0.0f;
            player.Body.ForwardSpeedDt = clock.Delta * 100.0f;
            player.Body.SteerScale = 1.0f;
            player.Body.EngineGravityDt = 32.0f * clock.DeltaSquared;
            player.Body.GravDeltaVel = player.Body.EngineGravityDt;
            player.AimPosition = player.Body.Pos;

            GlideOnSurface.UpdateTerrainPhysics(player, player.Body, clock, EllipsoidStatics.Arena,
                ObjectConstants.SlotNone, PhysicsTuning.Shipped, null, false, true);

            Check(failures, !float.IsNaN(player.Body.Vel.x) && !float.IsNaN(player.Body.Pos.z), "glide physics stays finite");
            float cap = player.Body.ForwardSpeedDt * (player.Stats.GetWeightedStat(ObjectConstants.RatingSpeed) * GlideConstants.SpeedStatScale + GlideConstants.SpeedStatBias) * PhysicsTuning.Shipped.MaxSpeed;
            Check(failures, player.Body.Speed <= cap + 1e-3f, "glide speed is capped by base speed * stat scale");
            Check(failures, player.Body.Pos.z < 0.0f, "glide stays inside the arena bowl");
        }

        private static void CheckSpeedBurst(List<string> failures)
        {
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            PlayerStats stats = new PlayerStats();
            stats.SpeedRatio = 1.0f;

            SpeedBurstState burst = new SpeedBurstState();
            burst.Bind(0);
            burst.Boost = 2.0f;
            burst.Active = true;

            float steerScale = 1.0f;
            burst.Process(clock, stats, ObjectConstants.SlotNone, ref steerScale);

            Check(failures, stats.SpeedRatio < 1.0f, "speed burst drains the stamina ratio");
            Check(failures, steerScale >= 1.0f, "speed burst never lowers the steering scale");

            burst.Boost = 1.0f;
            burst.Active = false;
            steerScale = 1.0f;
            SpeedBurstResult result = burst.Process(clock, stats, ObjectConstants.SlotNone, ref steerScale);
            Check(failures, !result.Finished, "idle speed burst does not finish early");
        }

        private static void CheckMultiplier(List<string> failures)
        {
            MultiplierRules.StepResult up = MultiplierRules.IncRokValue(0, RokConstants.ArenaTrackUp, true, 144, 144);
            Check(failures, up.Value == 2 && up.RaisedEvent, "rok value up step to 2 raises the event");
            Check(failures, up.Material2Colour == MultiplierRules.ColourPositive, "rok value 2 lights the positive colour");

            MultiplierRules.StepResult clampHigh = MultiplierRules.IncRokValue(4, RokConstants.ArenaTrackUp, true, 144, 144);
            Check(failures, clampHigh.Value == 4, "rok value clamps at 4");
            Check(failures, !clampHigh.RaisedEvent, "rok value at the clamp does not raise the event");

            MultiplierRules.StepResult down = MultiplierRules.IncRokValue(0, RokConstants.ArenaTrackDown, true, 144, 144);
            Check(failures, down.Value == -2 && down.RaisedEvent && down.EventParam == 1, "rok value down step");
            Check(failures, MultiplierRules.IncRokValue(-4, RokConstants.ArenaTrackDown, true, 144, 144).Value == -4, "rok value clamps at -4");
            Check(failures, MultiplierRules.IncRokValue(1, 0, true, 144, 144).Value == 0, "rok value request 0 clears, and 1 normalises before it");

            Check(failures, MultiplierRules.RenderSlotBone(2, false, -1) == 7, "rok value 2 shows bone 7");
            Check(failures, MultiplierRules.RenderSlotBone(4, false, -1) == 8, "rok value 4 shows bone 8");
            Check(failures, MultiplierRules.RenderSlotBone(0, true, -1) == 0, "rok at the spawn slot shows bone 0");
            Check(failures, MultiplierRules.RenderSlotBone(0, false, 0) == 2, "a killed rok shows bone 2");
        }

        private static void CheckGoalRules(List<string> failures)
        {
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotSGoal, -4, false, false) == 0, "south goal clamps up to the 1x code");
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotSGoal, 2, false, false) == 1, "south goal with value 2 is 2x");
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotSGoal, 4, false, false) == 2, "south goal with value 4 is 4x");
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotNGoal, -2, false, false) == 1, "north goal negates value -2 to 2x");
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotNGoal, 4, false, false) == 0, "north goal negates value 4 down to 1");
            Check(failures, GoalRules.RokScoreValue(ObjectConstants.SlotSGoal, 4, false, true) == 3, "rok at a spawn slot is code 3");
            Check(failures, GoalRules.IsDoubleScore(true, 7) == 2, "double score reports 2 when the rok is carried");
            Check(failures, GoalRules.CheckScoreOnOwnGoal(7, 2, 7) == 0, "own-goal check: carrier shoots at this goal");
            Check(failures, GoalRules.CheckScoreOnOwnGoal(7, 2, 8) == 1, "own-goal check: other goal");
            Check(failures, GoalRules.CheckScoreOnOwnGoal(7, 9, 7) == 1, "own-goal check: non skater carrier answers yes");
            Check(failures, GoalRules.BouncedInGoal(true, false) == 1 && GoalRules.BouncedInGoal(false, true) == 2 && GoalRules.BouncedInGoal(false, false) == 0, "bounced-in-goal answers");
            Check(failures, GoalRules.PointsForScoreValue(2) == 4 && GoalRules.PointsForScoreValue(1) == 2, "score value codes map to 2x/4x points (plan VM TODO)");
        }

        private static void CheckPeriods(List<string> failures)
        {
            MatchContext context = new MatchContext();
            context.PeriodLengthMs = 1000;
            MatchRules rules = new MatchRules(context);

            List<int> messages = new List<int>();
            context.Broadcast = gameEvent => messages.Add(gameEvent.Message);

            FixedStepClock fixedClock = new FixedStepClock();
            fixedClock.InitMatchTimers();
            rules.Clock.InitMatchTimers(0);

            // SetRunState(1) from the initial pause mark -1 starts the period clock (pause mark 1,
            // pause start now, period start unchanged because s_dwPauseBase is still 0).
            rules.SetRunState(1, 0);

            bool periodEnded = false;
            bool jumbotron = false;
            for (int tick = 0; tick < 2000 && !jumbotron; tick++)
            {
                uint now = fixedClock.Advance();
                rules.TickClock(now, false, true);
                if (rules.PeriodEnded)
                {
                    periodEnded = true;
                }
                if (rules.JumbotronPending)
                {
                    jumbotron = true;
                }
            }

            Check(failures, periodEnded, "period ends when its length elapses");
            Check(failures, messages.Contains(MessageIds.EndOfPeriod), "end-of-period event broadcast in period 1");
            Check(failures, jumbotron, "period end requests the jumbotron state");
            Check(failures, rules.Clock.Period == 2, "period advances to 2 after the run-on");
            Check(failures, !messages.Contains(MessageIds.EndOfGame), "no end-of-game event before the final period");

            int endOfPeriodCount = 0;
            foreach (int message in messages)
            {
                if (message == MessageIds.EndOfPeriod)
                {
                    endOfPeriodCount++;
                }
            }
            Check(failures, endOfPeriodCount == 1, "one end-of-period broadcast for one period");

            // RestartMatch -> BeginMatchPlay -> BroadcastEvent(MSG_START_DRAG): the drag zeroes both
            // clock stamps and leaves the pause mark at 0.
            rules.BeginMatchPlay();
            rules.SceneEvent(MessageIds.StartDrag, 0, ObjectConstants.SlotGod, rules.Clock.TickMs);
            Check(failures, !rules.PeriodEnded && !rules.EndPeriodRequested, "BeginMatchPlay resets the period flags");
            Check(failures, rules.Clock.PeriodStartMs == rules.Clock.TickMs, "BeginMatchPlay restarts the period clock");
            Check(failures, rules.Clock.PauseStartMs == rules.Clock.PeriodStartMs, "start drag zeroes both clock stamps");
            Check(failures, rules.Clock.PauseMark == 0, "start drag leaves the pause mark at 0");

            // The period clock only counts while the pause mark is 1; the plans broadcast a shot to
            // start it (gameengine.cpp BroadcastEvent). A second period therefore starts with a shot
            // rebroadcast after the drag, and its run-on timer advances the period to 3.
            rules.JumbotronPending = false;
            rules.SceneEvent(MessageIds.Shoot, 0, ObjectConstants.SlotGod, rules.Clock.TickMs);
            bool jumbotron2 = false;
            for (int tick = 0; tick < 2000 && !jumbotron2; tick++)
            {
                uint now = fixedClock.Advance();
                rules.TickClock(now, false, true);
                jumbotron2 = rules.JumbotronPending;
            }
            Check(failures, jumbotron2 && rules.Clock.Period == 3, "second period reaches the jumbotron on the run-on timer");

            // The broadcast clock arithmetic: a shot sets the pause start and folds the interval
            // since the drag into the period start; a goal then stops the clock.
            uint beforeShot = rules.Clock.PeriodStartMs;
            uint shotNow = rules.Clock.TickMs;
            rules.Clock.PauseMark = 0;
            rules.SceneEvent(MessageIds.Shoot, 0, ObjectConstants.SlotGod, shotNow);
            Check(failures, rules.Clock.PauseMark == 1, "a shot restarts the period clock");
            Check(failures, rules.Clock.PeriodStartMs == shotNow, "a shot folds the stopped interval into the period start");
            Check(failures, rules.Clock.PauseStartMs == shotNow, "a shot moves the pause start to the shot");
            Check(failures, beforeShot <= shotNow, "the shot happened after the period start");
            rules.SceneEvent(MessageIds.GoalScored, 0, ObjectConstants.SlotGod, shotNow + 300);
            Check(failures, rules.Clock.PauseMark == 0, "a goal stops the period clock");

            MatchContext pauseContext = new MatchContext();
            MatchRules pauseRules = new MatchRules(pauseContext);
            pauseRules.Clock.InitMatchTimers(1000);
            pauseRules.SetRunState(0, 2000);
            Check(failures, pauseRules.RunState == ClockConstants.RunPaused, "SetRunState(0) pauses");
            Check(failures, pauseRules.Clock.PauseMark == -1, "pausing clears the pause mark to -1");
            uint before = pauseRules.Clock.PeriodStartMs;
            pauseRules.SetRunState(1, 5000);
            Check(failures, pauseRules.RunState == ClockConstants.RunPlaying, "SetRunState(1) resumes");
            Check(failures, pauseRules.Clock.PauseMark == 1, "resuming sets the pause mark to 1");
            Check(failures, pauseRules.Clock.PeriodStartMs == before + 3000u, "resume folds the paused interval into the period start");
            Check(failures, pauseRules.IncrementHomeScore() == 0 && pauseRules.ScoreHome == 0, "first home point increments -1 to 0");
            Check(failures, pauseRules.IncrementAwayScore() == 0 && pauseRules.ActiveScoreLine == 1, "away point selects the away score line");
            pauseRules.Reset();
            Check(failures, pauseRules.ScoreHome == -1 && pauseRules.ScoreAway == -1, "Reset returns both scores to -1");
        }

        private static void CheckCamera(List<string> failures)
        {
            CameraState camera = new CameraState();
            Check(failures, Near(camera.HalfFov, CameraConstants.HalfFieldOfView, 0f), "camera half fov is 85/2 degrees");
            Check(failures, camera.BungeeLength == 12.0f, "camera bungee starts at 12");
            Check(failures, camera.PosHistory.Count == 6 && camera.DirHistory.Count == 4, "camera history starts at 6 and 4");
            Check(failures, Near(camera.PosHistory.Weight, 1.0f / 6.0f, 1e-6f), "camera position weight 1/6");

            LayoutState layout = new LayoutState { PrevWidth = 1920, PrevHeight = 1080, PrevRight = 1920.0f, PrevBottom = 1080.0f };
            CameraProjectionState projection = new CameraProjectionState();
            projection.CreateProjectionState(true, layout, CameraConstants.HalfFieldOfView);

            Check(failures, Near(projection.HalfWidthPx, 960.0f, 0f) && Near(projection.HalfHeightPx, 540.0f, 0f), "projection half extents");
            Check(failures, Near(projection.CmPerPixel, 1.0f / (72.0f / 2.54f), 1e-7f), "projection cm per pixel at 72 dpi");
            Check(failures, Near(projection.HalfWidthCm, projection.CmPerPixel * 960.0f, 1e-4f), "projection half width in cm");
            Check(failures, Near(projection.NearClip, 1.0f, 0f) && Near(projection.FarClip, 1000.0f, 0f), "projection near 1 far 1000");
            Check(failures, Near(projection.TanHalfFovFloat, (float)Math.Tan(CameraConstants.HalfFieldOfView), 1e-5f), "projection tan half fov");
            Check(failures, Math.Abs(projection.Matrix[1, 1] - 960.0f) < 1e-2f, "aspect compose scales row one by width/height");
            Check(failures, Math.Abs(projection.Matrix[2, 2] - 0.9172484f) < 1e-3f, "projection depth scale");
            Check(failures, Math.Abs(projection.Matrix[2, 3] + 0.9172484f) < 1e-3f, "projection depth bias");
            Check(failures, Math.Abs(projection.Matrix[0, 2] - 879.67786f) < 1e-2f, "projection w-per-z times the x offset");
            Check(failures, Math.Abs(projection.Matrix[3, 2] - 0.9163311f) < 1e-5f, "projection w-per-z row");
            camera.InitViewTransforms(layout);
            Check(failures, Math.Abs(camera.Projection.Matrix[1, 1] - 494.81876f) < 1e-2f, "view projection tilts by pi/2");

            projection.SetEyePoint(new Vec3(1.0f, 2.0f, 3.0f));
            Check(failures, Near(projection.Eye.x, 1.0f, 0f) && Near(projection.Translate.z, -3.0f, 0f), "SetEyePoint negates the eye into the translation");
            projection.SubtractTranslation(0.0f, 1.0f, 0.0f);
            Check(failures, Near(projection.Translate.y, -3.0f, 0f), "SubtractTranslation moves the translation");

            camera.Body.Pos = new Vec3(10.0f, 20.0f, 30.0f);
            camera.Body.VelDir = new Vec3(1.0f, 0.0f, 0.0f);
            Vec3 beforeOut = camera.PosHistory.Output;
            camera.UpdateSmoothing();
            Check(failures, camera.PosHistory.Index == 1, "UpdateSmoothing advances the position ring");
            Check(failures, !camera.PosHistory.Output.Equals(beforeOut), "UpdateSmoothing moves the output");

            Check(failures, camera.SetPosHistorySize(0) == false, "position history rejects the zero divide");
            Check(failures, camera.SetPosHistorySize(25), "position history accepts 25");
            Check(failures, camera.SetDirHistorySize(25), "direction history accepts 25");
            Check(failures, camera.SetDirHistorySize(26) == false, "direction history rejects 26");
            Check(failures, Near(camera.PosHistory.Weight, 1.0f / 25.0f, 1e-6f), "position weight follows the window");

            camera.ResetCameraBufferSize(clockForCameraBuffer(), CameraConstants.SteadyCamBuffer);
            Check(failures, camera.PosHistory.Count >= 1 && camera.PosHistory.Count <= 25, "camera buffer size stays in [1,25]");

            Check(failures, Near(CameraConstants.HalfFieldOfView * 2.0f, 1.4835298f, 1e-5f), "camera FOV is 85 degrees");
            Check(failures, Near(HeadCamShotPower.Amount(0, 500, 7.0f), 38.0f, 1e-3f), "head cam shot power 38 at 0.5 s with rating 7");
            Check(failures, HeadCamShotPower.Amount(0, 100000, 7.0f) == CameraConstants.ShotPowerMax, "head cam shot power caps at 100");
        }

        // The arena floor height one unit inside the y end: -sqrt(1 - (187/187.5)^2) * 105.
        private static float ExpectedYEndInsideZ()
        {
            double y = 187.0;
            double radiusY = ArenaConstants.RadiusY;
            double remainder = 1.0 - (y * y) / (radiusY * radiusY);
            return (float)(-Math.Sqrt(remainder) * ArenaConstants.RadiusZ);
        }

        private static EngineClock clockForCameraBuffer()
        {
            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }
            return clock;
        }

        private static void CheckCameraTrack(List<string> failures)
        {
            CameraState camera = new CameraState();
            PlayerState player = new PlayerState(0);
            player.PlanBehaviorKind = ObjectConstants.BehaviorGlide;
            player.AimPosition = new Vec3(0.0f, 0.0f, -105.0f);
            player.Body.Pos = new Vec3(0.0f, 0.0f, -105.0f);
            player.Body.VelDir = new Vec3(1.0f, 0.0f, 0.0f);
            player.Body.SurfNormal = new Vec3(0.0f, 0.0f, 1.0f);
            camera.SetTrackedObject(player);
            camera.Body.Pos = new Vec3(-20.0f, 0.0f, -90.0f);

            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            bool ok = CameraTrack.Process(camera, clock,
                slot => slot == 0 ? (ITrackedEntity)player : null,
                () => null,
                0);

            Check(failures, ok, "chase camera processes a tracked player");
            Check(failures, !float.IsNaN(camera.Body.Pos.x) && !float.IsNaN(camera.Body.Pos.z), "chase camera position stays finite");
            Check(failures, camera.LastTracked == 0, "chase camera records the tracked slot");
            Check(failures, camera.Countdown > 0, "chase camera settles on its first frames");

            CameraOffsetModes.Offsets[(int)CameraOffsetMode.OverShoulder] = new Vec3(1.0f, 2.0f, 3.0f);
            CameraOffsetModes.SetTrackingOffset(camera, CameraOffsetMode.OverShoulder);
            Check(failures, camera.AxisScale.x == 1.0f && camera.AxisScale.y == 2.0f && camera.AxisScale.z == 3.0f, "offset mode copy");
            CameraOffsetModes.IncrementTrackingOffset(camera, CameraOffsetMode.OverShoulder);
            Check(failures, camera.AxisScale.x == 2.0f, "offset mode increment");
            CameraOffsetModes.SetBungeeLength(camera, 100);
            Check(failures, camera.BungeeLength == 10.0f, "bungee length setter takes tenths");
            Vec3 rearView = CameraOffsetModes.RearViewTarget(player.Body, player.Body.Pos);
            Check(failures, Near(rearView.y, player.Body.Pos.y, 1e-5f), "rear view leads by three velocity frames");
        }

        private static void CheckRok(List<string> failures)
        {
            RokState rok = new RokState();
            rok.ResetToSpawnState();
            Check(failures, rok.RokValue == 0 && rok.KillState == -1, "rok resets to value 0 and kill state -1");
            Check(failures, rok.OddCarrierSlot == 1 && rok.OddPrevCarrierSlot == 1, "rok odd carrier history starts at 1");

            List<int> messages = new List<int>();
            AttachPak pak = new AttachPak { Slot = 2, Offset = new Vec3(0.0f, 0.0f, 1.0f) };
            rok.Attach(pak, (slot, message, param) => messages.Add(message));
            Check(failures, rok.IsCarried && rok.CarrierSlot == 2 && rok.PrevCarrierSlot == 0, "attach records the carrier history");
            Check(failures, messages.Contains(MessageIds.GotIt) && messages.Contains(MessageIds.Attach), "attach posts got-it and attach");

            EngineClock clock = new EngineClock();
            clock.InitMatchTimers(0);
            for (int i = 0; i < 6; i++)
            {
                clock.AdvanceFixedTick((uint)((i + 1) * ClockConstants.FixedTickMs));
            }

            PlayerStats carrierStats = new PlayerStats();
            rok.Body.Pos = new Vec3(10.0f, 20.0f, -100.0f);
            bool thrown = rok.Throw(clock, carrierStats, 7.0f, new Vec3(1.0f, 0.0f, 0.0f), 2, 2);
            Check(failures, thrown, "rok throws when the carrier is the viewed slot");
            Check(failures, Near(rok.Body.Speed, (7.0f * 0.04f + 0.8f) * 110.0f * clock.Delta, 1e-3f), "throw speed formula");
            Check(failures, rok.Body.VelDir.z > 0.0f, "throw raises the heading");

            bool gated = rok.Throw(clock, carrierStats, 7.0f, new Vec3(1.0f, 0.0f, 0.0f), 3, 3);
            Check(failures, !gated, "throw refuses a carrier other than the attached one");

            RokLaunch.UnaimedLaunch launch = RokLaunch.LaunchUnaimed(rok, carrierStats, ObjectConstants.SlotNGoal, clock,
                EllipsoidStatics.Arena, false, 0.5f, 0.5f);
            Check(failures, !float.IsNaN(launch.Dir.x) && launch.Speed > 0.0f, "unaimed launch produces a finite velocity");

            uint shotWord = RokState.PackShotWord(0.5f, 60, 0.25f, out int reset);
            Check(failures, RokState.ShotWordAimX(shotWord) == 50 && RokState.ShotWordPower(shotWord) == 60, "shot word packs aim and power");
            Check(failures, reset == CameraConstants.ShotPowerReset, "shot word resets the meter to 50");
        }
    }
}
