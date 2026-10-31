using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Sim;
using Debug = UnityEngine.Debug;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The playable match configuration the possession layer needs: the viewed human slot and the two
    // goal object positions from the loaded drome (the ENV NGoal/SGoal definitions, which is what
    // PlanAction::Launch reads through the goal object's position pointer).
    public struct MatchPlayConfig
    {
        public int HumanSlot;
        public SimVec3 NorthGoalPosition;
        public SimVec3 SouthGoalPosition;
        public bool AutoResetAfterGoal;
        public int PostGoalResetTicks;

        public SimVec3 GoalPosition(int targetGoalSlot)
        {
            return targetGoalSlot == ObjectConstants.SlotNGoal ? NorthGoalPosition : SouthGoalPosition;
        }

        public static MatchPlayConfig FromWorld(MatchWorldData world, int humanSlot)
        {
            MatchPlayConfig config = new MatchPlayConfig();
            config.HumanSlot = humanSlot;
            config.AutoResetAfterGoal = true;
            config.PostGoalResetTicks = 45;
            config.NorthGoalPosition = new SimVec3(0.0f, 192.5f, -14.8f);
            config.SouthGoalPosition = new SimVec3(0.0f, -192.5f, -14.8f);

            for (int i = 0; i < world.SimDrome.Placements.Count; i++)
            {
                DromeObjectPlacement placement = world.SimDrome.Placements[i];

                if (placement.Type == GameObjectType.NGoal)
                {
                    config.NorthGoalPosition = placement.Position;
                }
                else if (placement.Type == GameObjectType.SGoal)
                {
                    config.SouthGoalPosition = placement.Position;
                }
            }

            return config;
        }
    }

    // One tick's outcome, so a scene can capture/step at the right moment and the headless gate can
    // assert the counts.
    public struct MatchPlayTickResult
    {
        public bool PickedUp;
        public bool Shot;
        public bool Passed;
        public bool Scored;
        public bool Bounced;
        public int Points;
        public int GoalSlot;
        public string Note;
    }

    // The possession/shooting rules, ported from the plan conditions/actions the shipped HUMAN,
    // CARRYROK and ROK plans call. Only the existing UBlade3.Sim types and actions are used; the
    // plan VM itself is not ported, so these are the named direct ports the plans invoke:
    //   PlanCond::RokInRange          HYPERX 0x00468c40 (plancond.cpp:8508)
    //   PlanCond::OkToGetRok          HYPERX 0x00468b30 (plancond.cpp:8443)
    //   PlanAction::Launch            HYPERX 0x00403290 (planactionlaunch.cpp:118)
    //   PlanAction::Throw             HYPERX 0x00403a30 (already ported as RokState.Throw)
    //   RokState::Attach/Detach       planactionanim.cpp Set_AttachPak/Unset_AttachPak
    //   CProjectAndBounce (subset)    HYPERX 0x0040d510 (cprojectandbounce.cpp:86)
    public static class MatchPossessionRules
    {
        public const int SlotWildcard = ObjectConstants.SlotWildcard;

        // plancond.cpp:8504-8505.
        public const float RangeCeiling = 150.0f;
        public const float FacingDot = -0.8f;

        // ROK.PBN GlidingFree arms id_MSG_RokLookForDromers with data 120; the handler forwards that
        // data in id_MSG_OkToGetRok, and HUMAN's RokInRange(1, data) uses it as the range-expand flag.
        public const int RokLookForDromersData = 120;

        // The attach offset the animation port cannot supply (Set_AttachPak gets the offset from the
        // carrier's rendered ROK bone). Chest height: the sim's carried follow adds it to the
        // carrier's AimPosition (head height 5.9822), so -1.2 puts the ROK near the hands/chest.
        public const float CarryOffsetZ = -1.2f;

        // The shipped engine's OkToGetRok carrier-history quirk (initial carrier slot 0 and
        // "the carrier cannot pick up again") would block the single viewed home slot 0; the scene
        // uses the same condition's free-ROK answer (attach == null) plus the exact RokInRange gate.
        // It is actor-generic for the N7 fallback AI possession: the same gate decides an AI pickup.
        public static bool CanPickup(Simulation sim, int actorSlot, bool cooldownReady, out float limitSquared)
        {
            RokState rok = sim.Rok;

            if (actorSlot < 0 || actorSlot >= sim.Players.Length)
            {
                limitSquared = 0.0f;
                return false;
            }

            PlayerState actor = sim.Players[actorSlot];

            limitSquared = 0.0f;

            if (rok.IsCarried)
            {
                return false;
            }

            if (!cooldownReady)
            {
                return false;
            }

            return RokInRange(actor, rok, 1, RokLookForDromersData, out limitSquared);
        }

        // PlanCond::RokInRange (0x00468c40): true when the actor's squared distance to the ROK is
        // within the rating-scaled range and the actor's heading is not pointing away (dot > -0.8).
        public static bool RokInRange(PlayerState actor, RokState rok, int rangeArg, int flag, out float limitSquared)
        {
            SimVec3 self = actor.Body.Pos;
            SimVec3 at = rok.Body.Pos;

            float dx = self.x - at.x;
            float dy = self.y - at.y;
            float dz = self.z - at.z;

            float range = rangeArg;

            if (flag != 0)
            {
                float side = (float)Math.Sqrt(range) + 1.0f;
                range = side * side;
            }

            float damage = actor.Stats.GetScalar(ObjectConstants.RatingDamage);
            float handling = actor.Stats.GetScalar(ObjectConstants.RatingRokHandling);
            float value = damage * handling;
            value = value * 0.01f + handling;
            value *= 0.5f;
            value -= 7.0f;
            value *= 0.05f;
            value += 1.0f;
            value *= range;

            if (value > RangeCeiling)
            {
                value = RangeCeiling;
            }

            limitSquared = value;

            if ((dx * dx) + (dy * dy) + (dz * dz) > value)
            {
                return false;
            }

            float headingX = actor.Body.Vel.x;
            float headingY = actor.Body.Vel.y;
            float headingZ = actor.Body.Vel.z;
            float headingLength = (float)Math.Sqrt((headingX * headingX) + (headingY * headingY) + (headingZ * headingZ));

            if (headingLength > 1e-6f)
            {
                headingX /= headingLength;
                headingY /= headingLength;
                headingZ /= headingLength;
            }
            else
            {
                headingX = 0.0f;
                headingY = 0.0f;
                headingZ = 0.0f;
            }

            float normalLength = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            float normalX = normalLength > 1e-6f ? dx / normalLength : 0.0f;
            float normalY = normalLength > 1e-6f ? dy / normalLength : 0.0f;
            float normalZ = normalLength > 1e-6f ? dz / normalLength : 0.0f;

            float dot = (headingX * normalX) + (headingY * normalY) + (headingZ * normalZ);

            return dot > FacingDot;
        }

        // PlanAction::Launch's ROK carrier branch (0x00403290). The shot word is the real human
        // head-camera word: PlanAction::SendLaunchMessage (0x004031e0) packs
        // PackShotWord(viewFracX, shotPower, viewFracY) from cameraobject slot 25, and the caller
        // supplies the MatchHeadCamera fractions. The viewed-slot gate of the plan/network path does
        // not apply to this player-controlled call (the scene has one human view).
        public static bool LaunchShot(Simulation sim, MatchPlayConfig config, int carrierSlot,
            float shotFracX, float shotFracY, int shotPowerLevel, out string note)
        {
            note = string.Empty;

            RokState rok = sim.Rok;

            if (rok.Body.Attach == null || rok.Body.Attach.Slot != carrierSlot)
            {
                note = "carrier is not slot " + carrierSlot;
                return false;
            }

            if (carrierSlot < 0 || carrierSlot >= sim.Players.Length)
            {
                note = "carrier slot " + carrierSlot;
                return false;
            }

            PlayerState carrier = sim.Players[carrierSlot];
            EngineClock clock = sim.Rules.Clock;
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            // The loader binds the body at the pre-play clock delta (1/18 s in this slice), so its
            // flForwardSpeedDt and gravity step are stale for the fixed 30 Hz match. Re-scale them
            // the way PhysicsBody::PreUpdateTick does when the ROK starts a flight (the velocity is
            // not rescaled because the launch sets it outright).
            rok.Body.GravDeltaVel = clock.GetDeltaSquared() * PhysicsConstants.Gravity;
            rok.Body.ForwardSpeedDt = clock.GetDelta() * rok.Body.BaseForwardSpeed;
            SimVec3 carrierPos = carrier.Body.Pos;
            SimVec3 goalPos = config.GoalPosition(carrier.TargetGoalSlot);

            float diffX = goalPos.x - carrierPos.x;
            float diffY = goalPos.y - carrierPos.y;
            float diffZ = goalPos.z - carrierPos.z;
            float distance = (float)Math.Sqrt((diffX * diffX) + (diffY * diffY) + (diffZ * diffZ));

            if (distance > 1e-6f)
            {
                diffX /= distance;
                diffY /= distance;
                diffZ /= distance;
            }

            SimVec3 carrierDir = carrier.Body.VelDir;
            float dirLength = (float)Math.Sqrt(
                (carrierDir.x * carrierDir.x) + (carrierDir.y * carrierDir.y) + (carrierDir.z * carrierDir.z));

            if (dirLength > 1e-6f)
            {
                carrierDir.x /= dirLength;
                carrierDir.y /= dirLength;
                carrierDir.z /= dirLength;
            }

            float limit = (ThrowConstants.RangeLimitDistance - distance)
                          * (ThrowConstants.ThrowPi / ThrowConstants.ThreeThrow) * ThrowConstants.RangeLimitScale
                          + (ThrowConstants.ThrowPi / ThrowConstants.ThreeThrow);

            float dot = (diffX * carrierDir.x) + (diffY * carrierDir.y) + (diffZ * carrierDir.z);
            float angle = (float)Math.Acos(Math.Max(-1.0f, Math.Min(1.0f, dot)));

            if (angle > limit)
            {
                // The decomp's facing guard: the carrier is not facing the goal, so the shot is a
                // weak fling. It uses rand(); the scene is deterministic, so it substitutes 0.5 and
                // labels the branch in the log.
                float delta = clock.GetDelta();
                float random01 = 0.5f;
                float speedScale = ((random01 * ThrowConstants.UnaimedRandScale + ThrowConstants.UnaimedRandScale)
                                    * carrier.Stats.GetScalar(ObjectConstants.RatingOffensiveAbility)
                                    * ThrowConstants.UnaimedAbilityScale + ThrowConstants.UnaimedBias) * delta;
                SimVec3 weakVelocity = new SimVec3(carrierDir.x * speedScale, carrierDir.y * speedScale, carrierDir.z * speedScale);
                rok.Body.Vel = weakVelocity;
                rok.Body.Speed = speedScale;
                if (speedScale > 1e-6f)
                {
                    rok.Body.VelDir = new SimVec3(carrierDir.x, carrierDir.y, carrierDir.z);
                }

                rok.Detach();
                rok.SetRenderSlotFlags();
                note = "weak fling (facing guard; deterministic random 0.5)";
                return true;
            }

            uint shotWord = RokState.PackShotWord(shotFracX, shotPowerLevel, shotFracY, out int resetShotPower);
            int range = RokState.ShotWordPower(shotWord);
            float spin = RokState.ShotWordAimX(shotWord) * ThrowConstants.AimFraction;
            float aimY = RokState.ShotWordAimY(shotWord);
            float drop = (ThrowConstants.AimDropBias - (aimY * ThrowConstants.AimFraction)) * ThrowConstants.AimDropScale;

            float baseX = -rok.Body.Pos.x;
            float baseY = carrier.TargetGoalSlot == ObjectConstants.SlotSGoal
                ? -arena.GetRadiusY() - rok.Body.Pos.y
                : arena.GetRadiusY() - rok.Body.Pos.y;
            float baseZ = drop - rok.Body.Pos.z - ThrowConstants.AimDropOffset;

            // Swing about the up axis (0,0,1) as the decomp's vAxis product gives.
            float swingX = baseY;
            float swingY = 0.0f;
            float swingZ = 0.0f;
            float swingLength = (float)Math.Sqrt((swingX * swingX) + (swingY * swingY) + (swingZ * swingZ));

            if (swingLength > 1e-6f)
            {
                swingX /= swingLength;
                swingY /= swingLength;
                swingZ /= swingLength;
            }

            float swingScale = (spin - ThrowConstants.SpinBias) * ThrowConstants.SpinScale;
            float velX = (swingX * swingScale) + baseX;
            float velY = (swingY * swingScale) + baseY;
            float velZ = (swingZ * swingScale) + baseZ;

            float half = (range + ThrowConstants.RangeBias) * 0.5f;
            float root = (float)Math.Sqrt((velX * velX) + (velY * velY) + (velZ * velZ));
            bool longShot = carrier.Stats.GetScalar(ObjectConstants.RatingLongShotEnable) != 0.0f;
            float cap = longShot ? arena.GetRadiusY() + arena.GetRadiusY() : ThrowConstants.LongShotCap;
            float reference = longShot
                ? rok.Body.ForwardSpeedDt * ThrowConstants.LongShotSpeedFactor
                : rok.Body.ForwardSpeedDt;

            if (root < 1e-6f || reference < 1e-6f)
            {
                note = "degenerate launch vector";
                return false;
            }

            float deltaS = clock.GetDelta();
            float scale = ((half * ThrowConstants.RangeScaleA) + ThrowConstants.RangeScaleB) * reference / root;
            float inverseScale = 1.0f / scale;

            velX *= scale;
            velY *= scale;

            if ((cap - root) < 0.0f)
            {
                velZ *= scale;
            }
            else
            {
                float rise = inverseScale * deltaS * deltaS * PhysicsConstants.Gravity;
                velZ = (rise * 0.5f) + (scale * velZ);
            }

            float speed = (float)Math.Sqrt((velX * velX) + (velY * velY) + (velZ * velZ));
            SimVec3 velocity = new SimVec3(velX, velY, velZ);
            rok.Body.Vel = velocity;
            rok.Body.Speed = speed;

            if (speed > 1e-6f)
            {
                rok.Body.VelDir = new SimVec3(velX / speed, velY / speed, velZ / speed);
            }

            rok.Detach();
            rok.SetRenderSlotFlags();

            note = "aimed launch goalSlot=" + carrier.TargetGoalSlot
                   + " power=" + shotPowerLevel
                   + " rangeByte=" + range
                   + " aimX=" + shotFracX.ToString("0.###", CultureInfo.InvariantCulture)
                   + " aimY=" + shotFracY.ToString("0.###", CultureInfo.InvariantCulture)
                   + " speed=" + speed.ToString("0.###", CultureInfo.InvariantCulture);
            return true;
        }

        // PlanAction::Launch's other shipped branch: SendLaunchMessage sends SHOT_UNAIMED
        // (0xffff0000) for every non-Human plan, and PlanAction::Launch then runs the unaimed branch
        // already ported as RokLaunch.LaunchUnaimed. The fallback AI has no head camera, so this is
        // its exact shot path. rand() is replaced by the fixed 0.5/0.5 pair and labelled as such.
        public static bool LaunchAiShot(Simulation sim, int carrierSlot, out float speed, out string note)
        {
            speed = 0.0f;
            note = string.Empty;

            RokState rok = sim.Rok;

            if (rok.Body.Attach == null || rok.Body.Attach.Slot != carrierSlot)
            {
                note = "carrier is not slot " + carrierSlot;
                return false;
            }

            if (carrierSlot < 0 || carrierSlot >= sim.Players.Length)
            {
                note = "carrier slot " + carrierSlot;
                return false;
            }

            PlayerState carrier = sim.Players[carrierSlot];
            EngineClock clock = sim.Rules.Clock;

            rok.Body.GravDeltaVel = clock.GetDeltaSquared() * PhysicsConstants.Gravity;
            rok.Body.ForwardSpeedDt = clock.GetDelta() * rok.Body.BaseForwardSpeed;

            RokLaunch.UnaimedLaunch launch = RokLaunch.LaunchUnaimed(rok, carrier.Stats,
                carrier.TargetGoalSlot, clock, EllipsoidStatics.Arena, sim.SessionActive, 0.5f, 0.5f);

            speed = launch.Speed;
            rok.Body.Speed = speed;
            rok.Body.VelDir = launch.Dir;
            rok.Detach();
            rok.SetRenderSlotFlags();
            note = "unaimed launch goalSlot=" + carrier.TargetGoalSlot
                   + " speed=" + speed.ToString("0.###", CultureInfo.InvariantCulture)
                   + " deterministicRand=0.5/0.5";
            return true;
        }

        // The existing exact PlanAction::Throw port (RokState.Throw) plus Unset_AttachPak. The Throw
        // gate's viewed-slot argument names the carrier itself for the AI fallback (the AI plan route
        // is the plan VM's own, not the human CHeadTrack view); documented in NATIVE-MATCH-N7.md.
        public static bool Pass(Simulation sim, int carrierSlot, out float speed, out string note)
        {
            speed = 0.0f;
            note = string.Empty;

            RokState rok = sim.Rok;

            if (rok.Body.Attach == null || rok.Body.Attach.Slot != carrierSlot)
            {
                note = "not carried by slot " + carrierSlot;
                return false;
            }

            PlayerState carrier = sim.Players[carrierSlot];
            float weightedShotPower = carrier.Stats.GetWeightedStat(ObjectConstants.RatingShotPower);

            bool thrown = rok.Throw(sim.Rules.Clock, carrier.Stats, weightedShotPower,
                carrier.Body.VelDir, carrierSlot, carrierSlot);

            if (!thrown)
            {
                note = "RokState.Throw gates rejected";
                return false;
            }

            speed = rok.Body.Speed;
            rok.Detach();
            rok.SetRenderSlotFlags();
            note = "PlanAction::Throw speed=" + speed.ToString("0.###", CultureInfo.InvariantCulture);
            return true;
        }

        // The deterministic test stages the human on the arena shell near the south goal so the
        // scripted shot is a plausible close-range shot. Everything after the staging - pickup,
        // shot meter, Launch math, flight, goal rules and score - is the real system; the plan VM's
        // movement/face-off that would normally bring the skater there is not ported.
        public static void StageHumanForTest(Simulation sim, int humanSlot, float x, float y)
        {
            PlayerState human = sim.Players[humanSlot];
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            float z = arena.GetZ(x, y);
            SimVec3 position = new SimVec3(x, y, z);
            SimVec3 normal;

            if (!arena.NormalAtPoint(position, out normal))
            {
                normal = new SimVec3(0.0f, 0.0f, 1.0f);
            }

            human.Body.Pos = position;
            human.Body.PrevPos = position;
            human.Body.Vel = SimVec3.Zero;
            human.Body.SmoothVel = SimVec3.Zero;
            human.Body.Axis = SimVec3.Zero;
            human.Body.Speed = 0.0f;
            human.Body.VelDir = new SimVec3(0.0f, -1.0f, 0.0f);
            human.Body.VelDirLatched = human.Body.VelDir;
            human.Body.SurfNormal = normal;
            human.AimPosition = new SimVec3(x, y, z + MatchWorldData.HeadHeight);
            human.TargetGoalSlot = (short)ObjectConstants.SlotSGoal;
        }

        // The test fixture's placement of the ROK one pickup radius in front of the human. The
        // pickup itself is the real RokInRange + Attach path; this only stages the deterministic
        // test (the plan VM's drag/face-off placement is not ported).
        public static void PlaceRokForPickupTest(Simulation sim, int humanSlot)
        {
            MatchScoreFixture.PlaceRokAtSpawn(sim);

            PlayerState human = sim.Players[humanSlot];
            SimVec3 dir = human.Body.VelDir;
            float length = (float)Math.Sqrt((dir.x * dir.x) + (dir.y * dir.y) + (dir.z * dir.z));

            if (length < 1e-6f)
            {
                dir = new SimVec3(0.0f, 1.0f, 0.0f);
            }
            else
            {
                dir = new SimVec3(dir.x / length, dir.y / length, dir.z / length);
            }

            SimVec3 position = new SimVec3(
                human.Body.Pos.x + (dir.x * 1.5f),
                human.Body.Pos.y + (dir.y * 1.5f),
                human.Body.Pos.z + (dir.z * 1.5f));

            RokState rok = sim.Rok;
            rok.Body.Pos = position;
            rok.Body.PrevPos = position;
            rok.Body.Vel = SimVec3.Zero;
            rok.Body.SmoothVel = SimVec3.Zero;
            rok.Body.Speed = 0.0f;
            rok.Body.VelDir = dir;
            rok.Body.VelDirLatched = dir;
            rok.Body.SurfNormal = human.Body.SurfNormal;
            rol_SetState(rok);
        }

        // The deterministic approach test: put the ROK on the ice straight ahead of the human so
        // scripted Forward input has to skate the human to it; the pickup itself is still the real
        // RokInRange/Attach path (the plan VM movement/face-off is not ported).
        public static void PlaceRokForApproachTest(Simulation sim, int humanSlot, float distance)
        {
            MatchScoreFixture.PlaceRokAtSpawn(sim);

            PlayerState human = sim.Players[humanSlot];
            SimVec3 forward = human.Body.VelDir;
            float length = (float)Math.Sqrt((forward.x * forward.x) + (forward.y * forward.y) + (forward.z * forward.z));

            if (length < 1e-6f)
            {
                forward = new SimVec3(0.0f, -1.0f, 0.0f);
            }
            else
            {
                forward = new SimVec3(forward.x / length, forward.y / length, forward.z / length);
            }

            float x = human.Body.Pos.x + (forward.x * distance);
            float y = human.Body.Pos.y + (forward.y * distance);
            CollisionEllipsoid arena = EllipsoidStatics.Arena;
            SimVec3 position = new SimVec3(x, y, arena.GetZ(x, y));
            SimVec3 normal;

            if (!arena.NormalAtPoint(position, out normal))
            {
                normal = human.Body.SurfNormal;
            }

            RokState rok = sim.Rok;
            rok.Body.Pos = position;
            rok.Body.PrevPos = position;
            rok.Body.Vel = SimVec3.Zero;
            rok.Body.SmoothVel = SimVec3.Zero;
            rok.Body.Speed = 0.0f;
            rok.Body.VelDir = forward;
            rok.Body.VelDirLatched = forward;
            rok.Body.SurfNormal = normal;
            rol_SetState(rok);
        }

        private static void rol_SetState(RokState rok)
        {
            rok.AtSpawnSlot = false;
            rok.KillState = RokConstants.InitialKillState;
            rok.SetRenderSlotFlags();
        }

        // collisioncheck.cpp GoalFrame.Mouth is a plane box; a fast shot moves several units a tick,
        // so the interactive path samples the segment before the point test to avoid tunnelling.
        public static bool SegmentEntersGoal(GoalFrame frame, SimVec3 previous, SimVec3 current, out SimVec3 point)
        {
            const int samples = 16;

            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                SimVec3 sample = new SimVec3(
                    previous.x + ((current.x - previous.x) * t),
                    previous.y + ((current.y - previous.y) * t),
                    previous.z + ((current.z - previous.z) * t));

                if (MatchGoalDetector.InsideGoalMouth(frame, sample))
                {
                    point = sample;
                    return true;
                }
            }

            point = current;
            return MatchGoalDetector.InsideGoalMouth(frame, current);
        }
    }

    // A bounded port of the free-ROK part of CProjectAndBounce (HYPERX 0x0040d510,
    // cprojectandbounce.cpp:86 ProjectAndBounce) plus PhysicsBody::IntegrateGravity. The original's
    // agenda item also runs the spring/net-target mode, the landing/agenda chain and the partial
    // re-projection; those are recorded as gaps in docs/NATIVE-PLAY.md. This integrator is what
    // makes the shot a real moving ROK rather than a scripted position.
    public sealed class MatchRokFlight
    {
        public bool Active;

        public int Bounces;

        public int Landings;

        public bool LastStepBounced;

        public bool LastStepLanded;

        public uint LastBounceTickMs;

        // The pre-boundary path of the last step: previous position and the position right after
        // IntegrateGravity (before the wall/floor projection). The goal sweep uses this true flight
        // path because a goal line crossing can happen on the same tick as an arena wall projection.
        public SimVec3 LastStepStart;

        public SimVec3 LastStepEnd;

        public void Begin()
        {
            Active = true;
        }

        public void Stop()
        {
            Active = false;
        }

        // Returns false when the ROK is not being integrated. The out flags describe this tick.
        public bool Step(Simulation sim, out bool bounced, out bool landed)
        {
            bounced = false;
            landed = false;
            LastStepBounced = false;
            LastStepLanded = false;

            if (!Active)
            {
                return false;
            }

            RokState rok = sim.Rok;
            PhysicsBodyState body = rok.Body;

            if (body.Attach != null)
            {
                Active = false;
                return false;
            }

            EngineClock clock = sim.Rules.Clock;
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            LastStepStart = body.Pos;
            body.IntegrateGravity(clock);
            LastStepEnd = body.Pos;

            if (body.Pos.z < 0.0f)
            {
                if (!arena.PointInside(body.Pos))
                {
                    SimVec3 near;
                    SimVec3 far;
                    int hit = arena.IntersectSegment(body.PrevPos, body.Pos, out near, out far);
                    SimVec3 closest;

                    if (hit != 1)
                    {
                        SimVec3 zero = SimVec3.Zero;
                        SimVec3 near2;
                        SimVec3 far2;
                        arena.IntersectSegment(zero, body.Pos, out near2, out far2);
                        closest = SimVec3.FindClosest(body.Pos, near2, far2);
                    }
                    else
                    {
                        closest = SimVec3.FindClosest(body.Pos, near, far);
                    }

                    SimVec3 normal;

                    if (!arena.NormalAtPoint(closest, out normal))
                    {
                        Active = false;
                        return false;
                    }

                    body.SurfNormal = normal;
                    body.Pos = new SimVec3(closest.x, closest.y, closest.z + 1.0f);
                    body.UpdateVelocityDirection();
                    bounced = true;
                }
            }
            else
            {
                // Above the ice: the decomp's radial boundary branch keeps the ROK inside the
                // ellipsoid's x/y cross-section and leaves the vertical velocity alone.
                float radial = (arena.GetInvXX() * body.Pos.x * body.Pos.x)
                               + (arena.GetInvYY() * body.Pos.y * body.Pos.y);

                if (radial > 1.0f)
                {
                    SimVec3 boundary;
                    arena.ComputeRadialBoundaryXY(body.Pos, out boundary);
                    boundary.z = body.Pos.z;

                    SimVec3 normal;

                    if (!arena.NormalAtPoint(boundary, out normal))
                    {
                        Active = false;
                        return false;
                    }

                    body.SurfNormal = normal;
                    float savedZ = body.Vel.z;
                    body.UpdateVelocityDirection();
                    body.Vel.z = savedZ;
                    body.Pos = boundary;
                    bounced = true;
                }
            }

            float cap = 2.0f * body.ForwardSpeedDt;
            float speed = (float)Math.Sqrt(
                (body.Vel.x * body.Vel.x) + (body.Vel.y * body.Vel.y) + (body.Vel.z * body.Vel.z));

            if (speed > cap && cap > 0.0f)
            {
                float scale = cap / speed;
                body.Vel = new SimVec3(body.Vel.x * scale, body.Vel.y * scale, body.Vel.z * scale);
                speed = cap;
            }

            body.Speed = speed;

            if (speed > 1e-5f)
            {
                body.VelDir = new SimVec3(body.Vel.x / speed, body.Vel.y / speed, body.Vel.z / speed);
            }

            float inverseDelta = clock.GetInvDelta();
            body.Step = new SimVec3(body.Vel.x * inverseDelta, body.Vel.y * inverseDelta, body.Vel.z * inverseDelta);

            SimVec3 normalAtPosition;

            if (arena.NormalAtPoint(body.Pos, out normalAtPosition))
            {
                body.SurfNormal = normalAtPosition;
            }

            body.Shell = arena;

            if (bounced)
            {
                Bounces++;
                LastBounceTickMs = clock.TickMs;
            }

            if (speed < 0.05f)
            {
                Active = false;
                landed = true;
                Landings++;
            }

            LastStepBounced = bounced;
            LastStepLanded = landed;

            return true;
        }
    }

    // The playable match control: pickup, carry, the head-camera shot meter/aim, shot, pass, free-ROK
    // flight, goal detection/scoring, the N7 goal celebration + face-off restart, fallback AI
    // possession and audio routing. It is Unity-free so the headless gate, the Editor capture and the
    // Windows player run exactly the same state machine; a scene only supplies the held-key mask and
    // a sim tick callback.
    public sealed class MatchPlayControl
    {
        public const int PickupCooldownTicks = 9; // 300 ms at 30 Hz, scene-level catch cooldown

        public readonly Simulation Sim;

        public readonly MatchPlayConfig Config;

        public readonly MatchAudioDirector Audio;

        public readonly MatchRokFlight Flight = new MatchRokFlight();

        public readonly MatchGoalDetector Detector = new MatchGoalDetector();

        public readonly MatchHeadCamera HeadCam = new MatchHeadCamera();

        public readonly List<string> Notes = new List<string>();

        public MatchAiController Ai;

        public bool AiEnabled;

        public bool HumanPickupEnabled = true;

        public MatchFlowPhase Phase = MatchFlowPhase.Live;

        public int PhaseEndTick = -1;

        public int Ticks;

        public HeldBinding PreviousHeld;

        public HeldBinding CurrentHeld;

        public bool Carrying;

        public int ChargeStartMs = -1;

        public int ShotPowerLevel;

        public int Pickups;

        public int HumanPickups;

        public int AiPickups;

        public int Shots;

        public int HumanShots;

        public int AiShots;

        public int Passes;

        public int HumanPasses;

        public int AiPasses;

        public int Goals;

        public int HumanGoals;

        public int AiGoals;

        public int Bounces;

        public int LastCarrierSlot = ObjectConstants.SlotNone;

        public uint LastHumanShotWord;

        public SimVec3 LastHumanShotVelocity;

        public SimVec3 LastHumanShotPosition;

        public SimVec3 LastAiShotVelocity;

        public bool LastShotWasAi;

        public MatchGoalEvent LastGoal;

        public string Status = string.Empty;

        public bool GoalLocked;

        public bool TraceFlight;

        private int traceTicks;

        private int pickupCooldownUntilTick = -1;

        private readonly List<int> pendingAiShots = new List<int>();

        private readonly List<int> pendingAiPasses = new List<int>();

        public MatchPlayControl(Simulation sim, MatchPlayConfig config, MatchAudioDirector audio)
        {
            Sim = sim;
            Config = config;
            Audio = audio ?? new MatchAudioDirector(null);
        }

        public bool PendingReset
        {
            get { return Phase != MatchFlowPhase.Live; }
        }

        public bool SimPaused
        {
            get { return Sim.Rules.RunState == ClockConstants.RunPaused; }
        }

        public void AttachAi(params int[] slots)
        {
            Ai = new MatchAiController();
            Ai.Enabled = true;

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    Ai.Slots.Add(slots[i]);
                }
            }

            AiEnabled = true;
        }

        public HeldBinding InputForSlot(int slot)
        {
            if (slot == Config.HumanSlot)
            {
                return CurrentHeld;
            }

            if (AiEnabled && Ai != null && Ai.Slots.Contains(slot))
            {
                return Ai.GetInput(Sim, slot);
            }

            if (slot == ObjectConstants.GoalieSlotA || slot == ObjectConstants.GoalieSlotB)
            {
                return HeldBinding.None;
            }

            return HeldBinding.Forward;
        }

        public void RequestAiShot(int slot)
        {
            if (!pendingAiShots.Contains(slot))
            {
                pendingAiShots.Add(slot);
            }
        }

        public void RequestAiPass(int slot)
        {
            if (!pendingAiPasses.Contains(slot))
            {
                pendingAiPasses.Add(slot);
            }
        }

        public void Advance(HeldBinding held, Action tickAction)
        {
            BeforeTick(held);

            if (tickAction != null)
            {
                tickAction();
            }

            AfterTick(held);
        }

        public void Advance(HeldBinding held, Action<HeldBinding> tickAction)
        {
            BeforeTick(held);

            if (tickAction != null)
            {
                tickAction(held);
            }

            AfterTick(held);
        }

        private void BeforeTick(HeldBinding held)
        {
            Ticks++;
            CurrentHeld = held;
            uint now = Sim.Rules.Clock.TickMs;
            Carrying = Sim.Rok.IsCarried;

            if (Phase != MatchFlowPhase.Live)
            {
                PreviousHeld = held;
                return;
            }

            bool shootHeld = (held & HeldBinding.Shoot) != 0;
            bool passHeld = (held & HeldBinding.Pass) != 0;
            bool passEdge = passHeld && (PreviousHeld & HeldBinding.Pass) == 0;
            bool humanCarrying = Carrying && Sim.Rok.AttachedSlot == Config.HumanSlot;

            if (humanCarrying)
            {
                HeadCam.TickAim(Sim, held, Config.HumanSlot);

                if (shootHeld)
                {
                    if (ChargeStartMs < 0)
                    {
                        ChargeStartMs = (int)now;
                    }

                    ShotPowerLevel = HeadCamShotPower.Amount((uint)ChargeStartMs, now,
                        Sim.Players[Config.HumanSlot].Stats.GetScalar(ObjectConstants.RatingShotPower));
                }
                else if (ChargeStartMs >= 0)
                {
                    int level = Math.Max(1, ShotPowerLevel);
                    string note;
                    uint shotWord = HeadCam.PackShotWord(level, out int resetShotPower);
                    SimVec3 position = Sim.Rok.Body.Pos;

                    if (MatchPossessionRules.LaunchShot(Sim, Config, Config.HumanSlot,
                            HeadCam.ViewFracX, HeadCam.ViewFracY, level, out note))
                    {
                        HumanShots++;
                        Shots++;
                        LastHumanShotWord = shotWord;
                        LastHumanShotPosition = position;
                        LastHumanShotVelocity = Sim.Rok.Body.Vel;
                        LastShotWasAi = false;
                        Flight.Begin();
                        pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
                        Audio.Impact("shot-release", now);
                        Notes.Add("UBLADE3_NATIVE_MATCH_N7_HUMAN_SHOT level=" + level
                                  + " shotWord=0x" + shotWord.ToString("x8", CultureInfo.InvariantCulture)
                                  + " aimX=" + F(HeadCam.ViewFracX)
                                  + " aimY=" + F(HeadCam.ViewFracY)
                                  + " note=" + note);

                        if (TraceFlight)
                        {
                            SimVec3 velocity = Sim.Rok.Body.Vel;
                            Debug.Log("UBLADE3_NATIVE_PLAY_SHOT_TRACE tick=" + Ticks
                                      + " level=" + level
                                      + " aimX=" + F(HeadCam.ViewFracX)
                                      + " aimY=" + F(HeadCam.ViewFracY)
                                      + " pos=(" + F(position.x) + "," + F(position.y) + "," + F(position.z) + ")"
                                      + " vel=(" + F(velocity.x) + "," + F(velocity.y) + "," + F(velocity.z) + ")"
                                      + " speed=" + F(Sim.Rok.Body.Speed));
                        }

                        Status = "SHOT RELEASED";
                    }
                    else
                    {
                        Notes.Add("UBLADE3_MATCH_PLAY_SHOT_FAILED " + note);
                    }

                    ChargeStartMs = -1;
                    ShotPowerLevel = 0;
                }

                if (passEdge)
                {
                    float speed;
                    string note;

                    if (MatchPossessionRules.Pass(Sim, Config.HumanSlot, out speed, out note))
                    {
                        HumanPasses++;
                        Passes++;
                        Flight.Begin();
                        pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
                        Audio.Impact("pass-release", now);
                        Notes.Add("UBLADE3_MATCH_PLAY_PASS speed=" + F(speed) + " note=" + note);
                        Status = "PASSED";
                    }
                }
            }
            else
            {
                ChargeStartMs = -1;
                ShotPowerLevel = 0;
            }

            if (AiEnabled && Ai != null)
            {
                Ai.Update(Sim, this);
            }

            ProcessAiRequests(now);
            PreviousHeld = held;
        }

        private void ProcessAiRequests(uint now)
        {
            for (int i = pendingAiShots.Count - 1; i >= 0; i--)
            {
                int slot = pendingAiShots[i];
                pendingAiShots.RemoveAt(i);

                if (Sim.Rok.AttachedSlot != slot)
                {
                    continue;
                }

                float speed;
                string note;

                if (MatchPossessionRules.LaunchAiShot(Sim, slot, out speed, out note))
                {
                    AiShots++;
                    Shots++;
                    LastAiShotVelocity = Sim.Rok.Body.Vel;
                    LastShotWasAi = true;
                    Flight.Begin();
                    pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
                    Audio.Impact("ai-shot-release", now);
                    Notes.Add("UBLADE3_NATIVE_MATCH_N7_AI_SHOT slot=" + slot + " speed=" + F(speed) + " note=" + note);
                    Status = "AI SHOT";
                }
            }

            for (int i = pendingAiPasses.Count - 1; i >= 0; i--)
            {
                int slot = pendingAiPasses[i];
                pendingAiPasses.RemoveAt(i);

                if (Sim.Rok.AttachedSlot != slot)
                {
                    continue;
                }

                float speed;
                string note;

                if (MatchPossessionRules.Pass(Sim, slot, out speed, out note))
                {
                    AiPasses++;
                    Passes++;
                    Flight.Begin();
                    pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
                    Audio.Impact("ai-pass-release", now);
                    Notes.Add("UBLADE3_NATIVE_MATCH_N7_AI_PASS slot=" + slot + " speed=" + F(speed) + " note=" + note);
                    Status = "AI PASS";
                }
            }
        }

        private void AfterTick(HeldBinding held)
        {
            uint now = Sim.Rules.Clock.TickMs;

            if (Phase == MatchFlowPhase.GoalCelebration)
            {
                if (Ticks >= PhaseEndTick)
                {
                    BeginFaceOffDrag();
                }

                UpdateStatus();
                return;
            }

            if (Phase == MatchFlowPhase.FaceOffDrag)
            {
                if (Ticks >= PhaseEndTick)
                {
                    FinishFaceOffDrag();
                }

                UpdateStatus();
                return;
            }

            bool scoredThisTick = false;

            if (!Sim.Rok.IsCarried && Flight.Active)
            {
                bool bounced;
                bool landed;
                bool stepped = Flight.Step(Sim, out bounced, out landed);

                if (stepped)
                {
                    if (TraceFlight && (traceTicks < 30 || (traceTicks % 5) == 0))
                    {
                        SimVec3 position = Sim.Rok.Body.Pos;
                        SimVec3 velocity = Sim.Rok.Body.Vel;
                        Debug.Log("UBLADE3_NATIVE_PLAY_FLIGHT_TRACE tick=" + Ticks
                                  + " pos=(" + F(position.x) + "," + F(position.y) + "," + F(position.z) + ")"
                                  + " vel=(" + F(velocity.x) + "," + F(velocity.y) + "," + F(velocity.z) + ")"
                                  + " speed=" + F(Sim.Rok.Body.Speed)
                                  + " bounce=" + (bounced ? "True" : "False"));
                    }

                    traceTicks++;

                    if (bounced)
                    {
                        Bounces++;
                        Audio.Impact("rok-bounce", now);
                    }

                    SimVec3 previous = Flight.LastStepStart;
                    SimVec3 current = Flight.LastStepEnd;
                    SimVec3 point;

                    if (MatchPossessionRules.SegmentEntersGoal(Sim.Arena.NorthGoal, previous, current, out point)
                        || MatchPossessionRules.SegmentEntersGoal(Sim.Arena.SouthGoal, previous, current, out point))
                    {
                        Sim.Rok.Body.Pos = point;
                        MatchGoalEvent gameEvent = Detector.TryScore(Sim, point);

                        if (gameEvent.Valid)
                        {
                            scoredThisTick = true;
                            CountGoal(gameEvent, now);
                        }
                    }
                }
            }

            if (!scoredThisTick && !GoalLocked)
            {
                MatchGoalEvent gameEvent = Detector.TryScore(Sim, Sim.Rok.Body.Pos);

                if (gameEvent.Valid)
                {
                    scoredThisTick = true;
                    CountGoal(gameEvent, now);
                }
            }

            if (!scoredThisTick && !Sim.Rok.IsCarried && !GoalLocked)
            {
                TryPickup(now);
            }

            UpdateStatus();
        }

        private void TryPickup(uint now)
        {
            bool cooldownReady = pickupCooldownUntilTick < 0 || Ticks >= pickupCooldownUntilTick;

            if (!cooldownReady)
            {
                return;
            }

            int bestSlot = ObjectConstants.SlotNone;
            float bestDistance = float.MaxValue;
            float bestLimit = 0.0f;

            if (HumanPickupEnabled)
            {
                float limit;

                if (MatchPossessionRules.CanPickup(Sim, Config.HumanSlot, true, out limit))
                {
                    float distance = DistanceSquared(Sim.Players[Config.HumanSlot].Body.Pos, Sim.Rok.Body.Pos);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestSlot = Config.HumanSlot;
                        bestLimit = limit;
                    }
                }
            }

            if (AiEnabled && Ai != null)
            {
                for (int i = 0; i < Ai.Slots.Count; i++)
                {
                    int slot = Ai.Slots[i];
                    float limit;

                    if (!MatchPossessionRules.CanPickup(Sim, slot, true, out limit))
                    {
                        continue;
                    }

                    float distance = DistanceSquared(Sim.Players[slot].Body.Pos, Sim.Rok.Body.Pos);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestSlot = slot;
                        bestLimit = limit;
                    }
                }
            }

            if (bestSlot == ObjectConstants.SlotNone)
            {
                return;
            }

            AttachPak pak = new AttachPak();
            pak.Slot = (short)bestSlot;
            pak.Offset = new SimVec3(0.0f, 0.0f, MatchPossessionRules.CarryOffsetZ);
            pak.LinkId = 0;

            Sim.Rok.Attach(pak, OnRokMessage);
            Pickups++;
            LastCarrierSlot = bestSlot;
            Carrying = true;
            Flight.Stop();
            pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
            ChargeStartMs = -1;
            ShotPowerLevel = 0;

            if (bestSlot == Config.HumanSlot)
            {
                HumanPickups++;
                HeadCam.ResetAim();
                Audio.Impact("rok-pickup", now);
                Notes.Add("UBLADE3_NATIVE_MATCH_PLAY_PICKUP slot=" + bestSlot + " tick=" + Ticks
                          + " limitSquared=" + F(bestLimit));
            }
            else
            {
                AiPickups++;
                Audio.Impact("ai-rok-pickup", now);
                Notes.Add("UBLADE3_NATIVE_MATCH_N7_AI_PICKUP slot=" + bestSlot + " tick=" + Ticks
                          + " limitSquared=" + F(bestLimit));
            }
        }

        private static float DistanceSquared(SimVec3 a, SimVec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        private void CountGoal(MatchGoalEvent gameEvent, uint now)
        {
            Goals++;

            if (LastCarrierSlot == Config.HumanSlot)
            {
                HumanGoals++;
            }
            else if (LastCarrierSlot >= 0 && AiEnabled && Ai != null && Ai.Slots.Contains(LastCarrierSlot))
            {
                AiGoals++;
            }

            LastGoal = gameEvent;
            GoalLocked = true;
            Phase = MatchFlowPhase.GoalCelebration;
            PhaseEndTick = Ticks + MatchFaceOff.GoalCelebrationTicks;
            Flight.Stop();
            Carrying = false;
            ChargeStartMs = -1;
            ShotPowerLevel = 0;
            HeadCam.ResetAim();
            MatchFaceOff.StopPlay(Sim);

            Audio.Goal(gameEvent.Points, gameEvent.GoalSlot == ObjectConstants.SlotSGoal,
                gameEvent.HomeScore, gameEvent.AwayScore, now);

            Notes.Add("UBLADE3_NATIVE_MATCH_N7_GOAL_FLOW side=" + (gameEvent.ScoringSide == 0 ? "home" : "away")
                      + " points=" + gameEvent.Points
                      + " goalSlot=" + gameEvent.GoalSlot
                      + " scoreCode=" + gameEvent.ScoreCode
                      + " homeScore=" + gameEvent.HomeScore
                      + " awayScore=" + gameEvent.AwayScore
                      + " scorerSlot=" + LastCarrierSlot
                      + " celebrationTicks=" + MatchFaceOff.GoalCelebrationTicks);
        }

        private void BeginFaceOffDrag()
        {
            MatchFaceOff.ApplyPlayerFaceOff(Sim);
            MatchFaceOff.ApplyRokFaceOff(Sim);
            Phase = MatchFlowPhase.FaceOffDrag;
            PhaseEndTick = Ticks + MatchFaceOff.DragTicks;
            GoalLocked = true;
            Flight.Stop();
            Carrying = false;
            ChargeStartMs = -1;
            ShotPowerLevel = 0;
            LastCarrierSlot = ObjectConstants.SlotNone;
            Notes.Add("UBLADE3_NATIVE_MATCH_N7_FACE_OFF_SETUP tick=" + Ticks
                      + " players=human+ai corners=" + F(MatchFaceOff.PlayerCorner)
                      + " rokDragHeight=" + F(MatchFaceOff.RokDragHeight)
                      + " dragTicks=" + MatchFaceOff.DragTicks);
        }

        private void FinishFaceOffDrag()
        {
            MatchFaceOff.DropRokForNextPossession(Sim);
            Detector.ResetDebounce();
            Phase = MatchFlowPhase.Live;
            GoalLocked = false;
            pickupCooldownUntilTick = Ticks + PickupCooldownTicks;
            HeadCam.ResetAim();
            MatchFaceOff.ResumePlay(Sim);
            Notes.Add("UBLADE3_NATIVE_MATCH_N7_FACE_OFF_RESUMED tick=" + Ticks
                      + " fallbackDrop=center-floor retailPlanAutoLaunchNotPorted");
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void OnRokMessage(int targetSlot, int message, int param)
        {
            Notes.Add("UBLADE3_MATCH_PLAY_ROK_MESSAGE target=" + targetSlot + " message=" + message + " param=" + param);
        }

        public void ResetRokToSpawn()
        {
            MatchScoreFixture.PlaceRokAtSpawn(Sim);
            Flight.Stop();
            Detector.ResetDebounce();
            Phase = MatchFlowPhase.Live;
            PhaseEndTick = -1;
            GoalLocked = false;
            Carrying = false;
            ChargeStartMs = -1;
            ShotPowerLevel = 0;
            pickupCooldownUntilTick = Ticks;
            HeadCam.ResetAim();
        }

        private void UpdateStatus()
        {
            if (Phase == MatchFlowPhase.GoalCelebration)
            {
                Status = "GOAL! " + Sim.Rules.ScoreHome + "-" + Sim.Rules.ScoreAway
                         + " - CELEBRATION - FACE-OFF NEXT";
            }
            else if (Phase == MatchFlowPhase.FaceOffDrag)
            {
                Status = "FACE-OFF - CENTRE - ROK DROPS AT THE DRAG END";
            }
            else if (GoalLocked)
            {
                Status = "GOAL - " + Sim.Rules.ScoreHome + "-" + Sim.Rules.ScoreAway;
            }
            else if (Sim.Rok.IsCarried && Sim.Rok.AttachedSlot == Config.HumanSlot)
            {
                Status = "ROK CARRIED - POWER " + ShotPowerLevel.ToString(CultureInfo.InvariantCulture)
                         + "% - AIM " + F(HeadCam.ViewFracX) + "/" + F(HeadCam.ViewFracY)
                         + " - RELEASE SPACE SHOOT - C PASS";
            }
            else if (Sim.Rok.IsCarried)
            {
                Status = "ROK CARRIED BY AI SLOT " + Sim.Rok.AttachedSlot;
            }
            else if (Flight.Active)
            {
                Status = "ROK IN FLIGHT";
            }
            else
            {
                Status = "ROK FREE - SKATE IT DOWN - HOLD SPACE TO SHOOT - C PASS";
            }
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_PLAY_COUNTS");
            text.Append(" ticks=").Append(Ticks);
            text.Append(" phase=").Append(Phase);
            text.Append(" pickups=").Append(Pickups);
            text.Append(" humanPickups=").Append(HumanPickups);
            text.Append(" aiPickups=").Append(AiPickups);
            text.Append(" shots=").Append(Shots);
            text.Append(" humanShots=").Append(HumanShots);
            text.Append(" aiShots=").Append(AiShots);
            text.Append(" passes=").Append(Passes);
            text.Append(" humanPasses=").Append(HumanPasses);
            text.Append(" aiPasses=").Append(AiPasses);
            text.Append(" goals=").Append(Goals);
            text.Append(" humanGoals=").Append(HumanGoals);
            text.Append(" aiGoals=").Append(AiGoals);
            text.Append(" bounces=").Append(Bounces);
            text.Append(" scoreHome=").Append(Sim.Rules.ScoreHome);
            text.Append(" scoreAway=").Append(Sim.Rules.ScoreAway);
            text.Append(" carrying=").Append(Sim.Rok.IsCarried ? "True" : "False");
            text.Append(" flight=").Append(Flight.Active ? "True" : "False");
            text.Append(" shotPower=").Append(ShotPowerLevel);
            text.Append(" headCam=\"").Append(HeadCam.Describe()).Append('"');
            text.Append(" lastHumanShotWord=0x").Append(LastHumanShotWord.ToString("x8", CultureInfo.InvariantCulture));
            text.Append(" lastHumanShotVel=(").Append(F(LastHumanShotVelocity.x)).Append(',')
                .Append(F(LastHumanShotVelocity.y)).Append(',')
                .Append(F(LastHumanShotVelocity.z)).Append(')');
            text.Append(" lastAiShotVel=(").Append(F(LastAiShotVelocity.x)).Append(',')
                .Append(F(LastAiShotVelocity.y)).Append(',')
                .Append(F(LastAiShotVelocity.z)).Append(')');
            text.Append(" audio=\"").Append(Audio.Describe()).Append('"');
            text.Append(" notes=").Append(Notes.Count);

            return text.ToString();
        }
    }

    // The deterministic played-match test: stage the ROK for a real pickup, carry it, charge a shot,
    // shoot it through the goal, score, then re-pick and pass. Every step advances the same
    // MatchPlayControl the interactive game uses. The capture callback lets the Editor/player write
    // the frames at each beat.
    public sealed class MatchPlayScriptResult
    {
        public int Failures;
        public int Pickups;
        public int Shots;
        public int Passes;
        public int Goals;
        public int Bounces;
        public int ApproachTicks;
        public int ScoreBeforeHome;
        public int ScoreBeforeAway;
        public int ScoreAfterHome;
        public int ScoreAfterAway;
        public bool GoalWasRealFlight;
        public string FailureText = string.Empty;
        public readonly List<string> Steps = new List<string>();
    }

    public static class MatchPlayScript
    {
        public const int ChargeTicks = 15;

        public static MatchPlayScriptResult Run(MatchPlayControl control, Action<string> capture,
            Action<HeldBinding> sceneTick = null)
        {
            MatchPlayScriptResult result = new MatchPlayScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());

            result.ScoreBeforeHome = sim.Rules.ScoreHome;
            result.ScoreBeforeAway = sim.Rules.ScoreAway;

            // Stage the skater on the centre line near the south goal (documented test staging),
            // then use only the real pickup/shot/flight/goal systems from here.
            MatchPossessionRules.StageHumanForTest(sim, control.Config.HumanSlot, 0.0f, -100.0f);

            // Let the chase camera and skater views settle on the staged position.
            for (int i = 0; i < 60; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            capture?.Invoke("start");

            // 1. Skate forward to the ROK; the pickup itself is the real RokInRange + Attach path.
            MatchPossessionRules.PlaceRokForApproachTest(sim, control.Config.HumanSlot, 30.0f);

            int approachTicks = 0;

            for (; approachTicks < 180 && !control.Carrying; approachTicks++)
            {
                control.Advance(HeldBinding.Forward, tick);
            }

            if (!control.Carrying)
            {
                SimVec3 human = sim.Players[control.Config.HumanSlot].Body.Pos;
                SimVec3 rok = sim.Rok.Body.Pos;
                result.Failures++;
                result.FailureText += "pickup did not happen after " + approachTicks + " approach ticks"
                                      + " human=(" + human.x.ToString("0.##", CultureInfo.InvariantCulture)
                                      + "," + human.y.ToString("0.##", CultureInfo.InvariantCulture)
                                      + "," + human.z.ToString("0.##", CultureInfo.InvariantCulture) + ")"
                                      + " rok=(" + rok.x.ToString("0.##", CultureInfo.InvariantCulture)
                                      + "," + rok.y.ToString("0.##", CultureInfo.InvariantCulture)
                                      + "," + rok.z.ToString("0.##", CultureInfo.InvariantCulture) + "); ";
            }

            result.Pickups = control.Pickups;
            result.ApproachTicks = approachTicks;
            capture?.Invoke("pickup");

            // 2. Charge the head-cam-style shot power meter with the documented Shoot key.
            for (int i = 0; i < ChargeTicks; i++)
            {
                control.Advance(HeldBinding.Shoot, tick);
            }

            if (!control.Carrying || control.ShotPowerLevel <= 0)
            {
                result.Failures++;
                result.FailureText += "charge did not build; ";
            }

            capture?.Invoke("charge");

            // 3. Release the shot (the next tick's BeforeTick sees the held edge) and fly.
            control.Advance(HeldBinding.None, tick);

            for (int i = 0; i < 4; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            result.Shots = control.Shots;

            if (control.Shots != 1)
            {
                result.Failures++;
                result.FailureText += "shot count " + control.Shots + "; ";
            }

            capture?.Invoke("shot");

            // 4. Fly the shot into the goal (the real ROK body, the real goal rules).
            for (int i = 0; i < 180 && control.Goals == 0; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            result.Goals = control.Goals;
            result.Bounces = control.Bounces;
            result.ScoreAfterHome = sim.Rules.ScoreHome;
            result.ScoreAfterAway = sim.Rules.ScoreAway;
            result.GoalWasRealFlight = control.Goals > 0 && result.ScoreAfterHome > result.ScoreBeforeHome;

            if (control.Goals != 1 || !result.GoalWasRealFlight)
            {
                result.Failures++;
                result.FailureText += "goal did not score from the shot (goals=" + control.Goals
                                      + " home=" + result.ScoreAfterHome + "); ";
            }

            capture?.Invoke("goal");

            // 5. The goal celebration + face-off drag restart, then the updated HUD.
            for (int i = 0; i < 100; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            capture?.Invoke("score");

            // 6. Pick up again and pass with the documented key (C / HeldBinding.Pass).
            MatchPossessionRules.StageHumanForTest(sim, control.Config.HumanSlot, 0.0f, -120.0f);
            MatchPossessionRules.PlaceRokForPickupTest(sim, control.Config.HumanSlot);

            for (int i = 0; i < 10 && !control.Carrying; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            if (control.Carrying)
            {
                control.Advance(HeldBinding.Pass, tick);
                control.Advance(HeldBinding.None, tick);
                result.Passes = control.Passes;

                if (control.Passes != 1)
                {
                    result.Failures++;
                    result.FailureText += "pass count " + control.Passes + "; ";
                }

                capture?.Invoke("pass");
            }
            else
            {
                result.Failures++;
                result.FailureText += "second pickup did not happen; ";
            }

            result.Pickups = control.Pickups;
            result.Goals = control.Goals;
            result.Bounces = control.Bounces;

            if (result.Pickups < 2)
            {
                result.Failures++;
                result.FailureText += "pickups " + result.Pickups + "; ";
            }

            if (sim.Rules.ScoreHome < 1)
            {
                result.Failures++;
                result.FailureText += "score stayed " + sim.Rules.ScoreHome + "-" + sim.Rules.ScoreAway + "; ";
            }

            result.Steps.Add("pickups=" + result.Pickups + " shots=" + result.Shots + " passes=" + result.Passes
                             + " goals=" + result.Goals + " bounces=" + result.Bounces
                             + " score=" + result.ScoreAfterHome + "-" + result.ScoreAfterAway);

            return result;
        }
    }
}
