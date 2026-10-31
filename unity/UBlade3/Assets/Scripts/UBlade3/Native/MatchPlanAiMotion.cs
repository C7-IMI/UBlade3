using System;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>One tick of the exact shipped CSteerToTarget + CAdjustAcceleration plan motion.</summary>
    public struct PlanTargetMotionTelemetry
    {
        public bool Applied;
        public int TargetSlot;
        public float TargetDistance;
        public float TargetDistanceSquared;
        public SimVec3 TargetPosition;
        public SimVec3 TargetVelocity;
        public SimVec3 DesiredHeading;
        public SimVec3 ActorPositionBefore;
        public SimVec3 ActorVelocityBefore;
        public SimVec3 ActorVelocityAfter;
        public SimVec3 ActorHeadingBefore;
        public SimVec3 ActorHeadingAfter;
        public SimVec3 BankForce;
        public SimVec3 AccelStep;
        public SimVec3 PredictedStepVelocity;
        public float HeadingDot;
        public float HeadingErrorRad;
        public float BankLean;
        public float BankCross;
        public float BankForceMagnitude;
        public float Speed;
        public float TargetSpeed;
        public float Lead;
        public float Gate;
        public int Jitter;
        public bool Banked;
        public bool Braked;
        public bool Arrived;
    }

    /// <summary>
    /// N11: the exact shipped target steering for behaviour kind 0 (BEH_GLIDE_TO_OBJECT), ported from
    /// src/planvm/csteertotarget.cpp CSteerToTarget::Process (HYPERX 0x0040e0b0) and
    /// src/engine/cadjustacceleration.cpp CAdjustAcceleration::Process (HYPERX 0x0040f7e0), applied to
    /// the real Simulation PlayerState body. The owner already runs the ported
    /// CGlideOnSurface::UpdateTerrainPhysics each tick, so the bank force and acceleration added here are
    /// integrated by the real glide physics (no held-mask steering).
    ///
    /// Source values and constants:
    ///   JITTER_MODULUS 0x32, RANGE_GATE_NEAR 2.0, RANGE_GATE_FAR 50.0, RANGE_GATE_LEAD 25.0,
    ///   OBJECT_BALANCE 50.0, BANK_DEADZONE_LOW -0.001, BANK_DEADZONE_HIGH 0.001.
    ///   Force: ((balance * 0.1 + 1.5) * flClock.flDeltaSquared * 40) * (jitter * 0.01 + 1).
    ///   Lead:  d2 * 0.005 clamped to [0,5] when target speed * invDelta > 3; gate 2 for the ROK,
    ///   50 for every other target; else 25 with the lead.
    ///   Arrive: d2 &lt; gate -&gt; smoothVel -= vel * 0.15 and MSG_GOT_THERE (posted once).
    ///   Bank: lean = 1 - dot(delta,velDir), cross = delta.y*axis.x - delta.x*axis.y; ApplyBankForce
    ///   with |lean|*force to side 0 for cross &lt; deadzone low, side 1 for cross &gt; high.
    ///   Then CAdjustAcceleration: smoothVel += flAccelDt * velDir (the ported body's AccelDt).
    ///
    /// The original force jitter uses the C runtime rand(); the port uses the deterministic
    /// PlanAiRandom sequence (the same deterministic-random convention as the Plan AI decision bodies).
    /// </summary>
    public static class MatchPlanAiMotion
    {
        public const int JitterModulus = 0x32;

        public const float RangeGateNear = 2.0f;

        public const float RangeGateFar = 50.0f;

        public const float RangeGateLead = 25.0f;

        public const float ObjectBalance = 50.0f;

        public const float BankDeadzoneLow = -0.001f;

        public const float BankDeadzoneHigh = 0.001f;

        /// <summary>id_MSG_GotThere (src/engine/messageids.h MSG_GOT_THERE 0x2c | DATA/ENUMIDS.H).</summary>
        public const int MessageGotThere = 0x2c;

        public static PlanTargetMotionTelemetry Apply(PlayerState actor, SimVec3 targetPosition, SimVec3 targetVelocity,
            float targetSpeed, int targetSlot, EngineClock clock)
        {
            PlanTargetMotionTelemetry telemetry = new PlanTargetMotionTelemetry();
            PhysicsBodyState body = actor.Body;

            telemetry.Applied = true;
            telemetry.TargetSlot = targetSlot;
            telemetry.TargetPosition = targetPosition;
            telemetry.TargetVelocity = targetVelocity;
            telemetry.TargetSpeed = targetSpeed;
            telemetry.ActorPositionBefore = body.Pos;
            telemetry.ActorVelocityBefore = body.Vel;
            telemetry.ActorHeadingBefore = body.VelDir;
            telemetry.Speed = body.Speed;

            float force;

            if (actor.Slot <= ObjectConstants.SlotDromer3)
            {
                force = actor.Stats.GetWeightedStat(ObjectConstants.RatingBalance);
            }
            else if (actor.Slot <= ObjectConstants.GoalieSlotB)
            {
                force = actor.Stats.GetWeightedStat(ObjectConstants.RatingBalance) * 10.0f;
            }
            else
            {
                force = ObjectBalance;
            }

            int jitter = PlanAiRandom.Rand() % JitterModulus;
            telemetry.Jitter = jitter;
            force = ((force * 0.1f + 1.5f) * clock.DeltaSquared * 40.0f) * ((float)jitter * 0.01f + 1.0f);

            SimVec3 delta = new SimVec3(
                targetPosition.x - body.Pos.x,
                targetPosition.y - body.Pos.y,
                targetPosition.z - body.Pos.z);

            SimVec3 heading = new SimVec3(body.VelDir.x, body.VelDir.y, 0.0f);
            SimVec3 flat = new SimVec3(delta.x, delta.y, 0.0f);

            Normalize(ref heading);
            Normalize(ref flat);

            float headingDot = heading.x * flat.x + heading.y * flat.y + heading.z * flat.z;
            telemetry.HeadingDot = headingDot;
            telemetry.HeadingErrorRad = (float)Math.Acos(Math.Max(-1.0, Math.Min(1.0, headingDot)));

            if (headingDot < 0.75f)
            {
                if (clock.InvDelta * targetSpeed > 5.0f)
                {
                    float gated = body.Speed * 6.0f;
                    float brake = ((headingDot - 2.0f) * clock.DeltaSquared) * gated;

                    body.SmoothVel = new SimVec3(brake * body.Vel.x, brake * body.Vel.y, brake * body.Vel.z);
                    telemetry.Braked = true;
                }
            }

            if (clock.InvDelta * targetSpeed > 3.0f)
            {
                float lead = (delta.x * delta.x + delta.y * delta.y + delta.z * delta.z) * 0.005f;

                if (lead < 1.5f)
                {
                    lead = 0.0f;
                }
                else if (lead > 5.0f)
                {
                    lead = 5.0f;
                }

                delta.x = lead * targetVelocity.x + delta.x;
                delta.y = lead * targetVelocity.y + delta.y;
                delta.z = lead * targetVelocity.z + delta.z;
                telemetry.Lead = lead;
                telemetry.Gate = RangeGateLead;
            }
            else
            {
                telemetry.Gate = targetSlot == PlanConstants.SlotRok ? RangeGateNear : RangeGateFar;
            }

            float distanceSquared = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
            telemetry.TargetDistanceSquared = distanceSquared;
            telemetry.TargetDistance = (float)Math.Sqrt(distanceSquared);

            if (distanceSquared < telemetry.Gate)
            {
                body.SmoothVel = new SimVec3(
                    body.SmoothVel.x - body.Vel.x * 0.15f,
                    body.SmoothVel.y - body.Vel.y * 0.15f,
                    body.SmoothVel.z - body.Vel.z * 0.15f);
                telemetry.Arrived = true;
            }

            Normalize(ref delta);
            telemetry.DesiredHeading = delta;

            float lean = 1.0f - (delta.x * body.VelDir.x + (delta.y * body.VelDir.y + delta.z * body.VelDir.z));
            float cross = delta.y * body.VelDir.x - delta.x * body.VelDir.y;

            telemetry.BankLean = lean;
            telemetry.BankCross = cross;

            if (cross < BankDeadzoneLow)
            {
                body.ApplyBankForce(0, Math.Abs(lean) * force);
                telemetry.Banked = true;
            }
            else if (cross > BankDeadzoneHigh)
            {
                body.ApplyBankForce(1, Math.Abs(lean) * force);
                telemetry.Banked = true;
            }

            SimVec3 accel = new SimVec3(
                body.AccelDt * body.VelDir.x,
                body.AccelDt * body.VelDir.y,
                body.AccelDt * body.VelDir.z);

            body.SmoothVel = new SimVec3(
                body.SmoothVel.x + accel.x,
                body.SmoothVel.y + accel.y,
                body.SmoothVel.z + accel.z);

            telemetry.AccelStep = accel;
            telemetry.BankForce = body.BankForce;
            telemetry.BankForceMagnitude = (float)Math.Sqrt(
                body.BankForce.x * body.BankForce.x + body.BankForce.y * body.BankForce.y + body.BankForce.z * body.BankForce.z);
            telemetry.PredictedStepVelocity = new SimVec3(
                body.Vel.x + body.SmoothVel.x + body.BankForce.x,
                body.Vel.y + body.SmoothVel.y + body.BankForce.y,
                body.Vel.z + body.SmoothVel.z + body.BankForce.z);
            telemetry.ActorVelocityAfter = body.Vel;
            telemetry.ActorHeadingAfter = body.VelDir;

            return telemetry;
        }

        private static void Normalize(ref SimVec3 value)
        {
            float length = (float)Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z);

            if (length > PlanAiConstants.NormalizeEpsilon)
            {
                value.x /= length;
                value.y /= length;
                value.z /= length;
            }
        }
    }
}
