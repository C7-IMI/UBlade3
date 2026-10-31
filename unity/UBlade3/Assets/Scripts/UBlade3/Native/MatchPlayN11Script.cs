using System;
using System.Collections.Generic;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>Result of the deterministic N11 plan-target physics probe/capture.</summary>
    public sealed class MatchPlayN11ScriptResult
    {
        public int Failures;

        public string FailureText = string.Empty;

        public int TicksRun;

        public int AiPickups;

        public int TargetChanges;

        public int PlanPhysicsTicks;

        public int Slot1PhysicsTicks;

        public int Slot1BankedTicks;

        public int RosterBankedTicks;

        public int Slot1BridgeFallbackTicks;

        public int RosterBridgeFallbackTicks;

        public int Slot1TargetChanges;

        public float Slot1StartDistance = -1.0f;

        public float Slot1MinDistance = float.MaxValue;

        public float Slot1LastDistance = -1.0f;

        public float Slot1Displacement;

        public float Slot1HeadingTravelDeg;

        public string StartMotion = string.Empty;

        public string PhysicsMotion = string.Empty;

        public string BeforeChangeMotion = string.Empty;

        public string AfterChangeMotion = string.Empty;

        public string SlotLines = string.Empty;

        public string RosterDescribe = string.Empty;

        public string Digest = string.Empty;

        public readonly List<string> TargetTrace = new List<string>();

        public readonly List<string> Steps = new List<string>();
    }

    /// <summary>
    /// N11 deterministic probe: the default match's PlanVM roster (normal MatchPlay build path) runs
    /// with the exact target-steering physics for behaviour kind 0. Slot 1 starts away from the free ROK:
    /// the shipped LooseRokAttackOpponent target drives CSteerToTarget (bank) + CAdjustAcceleration
    /// (thrust) through the real Simulation body, the probe records target distance/heading/displacement,
    /// then the possession plan change (TurnToMate) gives a second target and the probe records the
    /// post-change steering. Behaviour kinds with no ported class stay on the labelled held-mask bridge
    /// and are counted.
    /// </summary>
    public static class MatchPlayN11Script
    {
        public const int MaxTicks = 150;

        public const float StagedHumanX = -28.0f;

        public const float StagedHumanY = 28.0f;

        public const float StagedTargetX = 0.0f;

        public const float StagedTargetY = 120.0f;

        public static readonly int[] PlanSlots = { 1, 2, 3, 4, 5 };

        public static MatchPlayN11ScriptResult Run(MatchPlayControl control, Action<string> capture,
            Action<HeldBinding> sceneTick = null)
        {
            MatchPlayN11ScriptResult result = new MatchPlayN11ScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());
            MatchPlanAiRoster roster = control.PlanRoster;

            if (roster == null || !control.DefaultPlanAi)
            {
                result.Failures++;
                result.FailureText = "default PlanVM AI is not attached by the normal build path; ";
                capture?.Invoke("result");

                return result;
            }

            if (!roster.TargetPhysicsEnabled)
            {
                result.Failures++;
                result.FailureText = "default roster target physics is not enabled; ";
                capture?.Invoke("result");

                return result;
            }

            Stage(control);
            capture?.Invoke("start");

            MatchPlanAi slot1 = roster.Adapter(1);
            float phaseStartDistance = -1.0f;
            float phaseMinDistance = float.MaxValue;
            bool targetChangeCaptured = false;
            bool targetCloseCaptured = false;
            bool resultCaptured = false;
            int pickupTick = 0;
            int guard = 0;

            while (guard < MaxTicks)
            {
                control.Advance(HeldBinding.None, held =>
                {
                    tick(held);
                    HoldStaged(sim);
                });

                guard++;

                if (slot1 != null && slot1.PlanPhysicsTicks > 0 && slot1.LastMotion.TargetSlot == 6)
                {
                    if (phaseStartDistance < 0.0f)
                    {
                        phaseStartDistance = slot1.LastMotion.TargetDistance;
                    }

                    if (slot1.LastMotion.TargetDistance < phaseMinDistance)
                    {
                        phaseMinDistance = slot1.LastMotion.TargetDistance;
                    }
                }

                if (result.StartMotion.Length == 0 && slot1 != null && slot1.PlanPhysicsTicks >= 2)
                {
                    result.StartMotion = slot1.MotionLine();
                }

                if (!targetCloseCaptured && phaseStartDistance > 0.0f && phaseMinDistance <= phaseStartDistance - 5.0f)
                {
                    targetCloseCaptured = true;
                    capture?.Invoke("target-close");
                }

                if (result.PhysicsMotion.Length == 0 && slot1 != null && slot1.PlanPhysicsTicks >= 10)
                {
                    result.PhysicsMotion = slot1.MotionLine();
                    capture?.Invoke("plan-physics");
                }

                if (control.AiPickups > 0 && pickupTick == 0)
                {
                    pickupTick = control.Ticks;
                    capture?.Invoke("ai-pickup");
                }

                if (slot1 != null && slot1.PhysicsTargetChanges > 0 && !targetChangeCaptured)
                {
                    targetChangeCaptured = true;
                    result.BeforeChangeMotion = slot1.MotionLine();
                    capture?.Invoke("target-change");
                }

                if (targetChangeCaptured && result.AfterChangeMotion.Length == 0 && slot1 != null
                    && slot1.LastMotion.TargetSlot != 6 && slot1.LastMotion.TargetSlot >= 0)
                {
                    result.AfterChangeMotion = slot1.MotionLine();
                }

                if (targetChangeCaptured && guard >= (pickupTick > 0 ? pickupTick + 25 : 70))
                {
                    capture?.Invoke("result");
                    resultCaptured = true;
                    break;
                }
            }

            result.TicksRun = control.Ticks;
            result.AiPickups = control.AiPickups;
            result.RosterDescribe = roster.Describe();

            int planPhysics = 0;
            int bridgeFallback = 0;
            int rosterBanked = 0;
            StringBuilder slotLines = new StringBuilder();

            for (int index = 0; index < PlanSlots.Length; index++)
            {
                MatchPlanAi adapter = roster.Adapter(PlanSlots[index]);

                if (adapter == null)
                {
                    continue;
                }

                planPhysics += adapter.PlanPhysicsTicks;
                bridgeFallback += adapter.BridgeFallbackTicks;
                rosterBanked += adapter.PhysicsBankedTicks;

                if (PlanSlots[index] == 1)
                {
                    result.Slot1PhysicsTicks = adapter.PlanPhysicsTicks;
                    result.Slot1BankedTicks = adapter.PhysicsBankedTicks;
                    result.Slot1BridgeFallbackTicks = adapter.BridgeFallbackTicks;
                    result.Slot1TargetChanges = adapter.PhysicsTargetChanges;
                    result.Slot1Displacement = adapter.PhysicsDisplacement;
                    result.Slot1HeadingTravelDeg = (float)(adapter.PhysicsHeadingTravelRad * 180.0 / Math.PI);
                    result.Slot1LastDistance = adapter.LastMotion.TargetDistance;
                    result.TargetTrace.AddRange(adapter.PhysicsTargetTrace);
                }

                slotLines.Append("UBLADE3_PLAN_AI_MOTION_SLOT slot=").Append(PlanSlots[index])
                    .Append(" plan=").Append(adapter.PlanName)
                    .Append(" useTargetPhysics=").Append(adapter.UseTargetPhysics ? "True" : "False")
                    .Append(" target=").Append(adapter.LastMotion.TargetSlot)
                    .Append(" physicsTicks=").Append(adapter.PlanPhysicsTicks)
                    .Append(" bridgeFallbackTicks=").Append(adapter.BridgeFallbackTicks)
                    .Append(" targetChanges=").Append(adapter.PhysicsTargetChanges)
                    .Append(" displacement=").Append(F(adapter.PhysicsDisplacement))
                    .Append(" headingTravelDeg=")
                    .Append(F((float)(adapter.PhysicsHeadingTravelRad * 180.0 / Math.PI)))
                    .Append(" distStart=").Append(F(adapter.PhysicsDistanceStart))
                    .Append(" distMin=").Append(F(adapter.PhysicsDistanceMin))
                    .Append(" distLast=").Append(F(adapter.PhysicsDistanceLast))
                    .Append(" desiredHeading=(").Append(F(adapter.LastMotion.DesiredHeading.x))
                    .Append(',').Append(F(adapter.LastMotion.DesiredHeading.y)).Append(')')
                    .Append(" predictedVel=(").Append(F(adapter.LastMotion.PredictedStepVelocity.x))
                    .Append(',').Append(F(adapter.LastMotion.PredictedStepVelocity.y)).Append(')')
                    .Append(" bankForce=").Append(F(adapter.LastMotion.BankForceMagnitude))
                    .Append(" accel=").Append(F(adapter.LastMotion.AccelStep.x))
                    .Append(',').Append(F(adapter.LastMotion.AccelStep.y))
                    .Append('\n');
            }

            result.PlanPhysicsTicks = planPhysics;
            result.RosterBankedTicks = rosterBanked;
            result.RosterBridgeFallbackTicks = bridgeFallback;
            result.SlotLines = slotLines.ToString();
            result.TargetChanges = result.TargetTrace.Count;
            result.Slot1StartDistance = phaseStartDistance;
            result.Slot1MinDistance = phaseMinDistance;

            if (result.StartMotion.Length == 0 && slot1 != null)
            {
                result.StartMotion = slot1.MotionLine();
            }

            if (result.PhysicsMotion.Length == 0 && slot1 != null)
            {
                result.PhysicsMotion = slot1.MotionLine();
            }

            if (result.BeforeChangeMotion.Length == 0 && slot1 != null)
            {
                result.BeforeChangeMotion = slot1.MotionLine();
            }

            if (result.AfterChangeMotion.Length == 0 && slot1 != null)
            {
                result.AfterChangeMotion = slot1.MotionLine();
            }

            result.Digest = PlanVmDigest.Hex(roster.Digest());
            result.Steps.Add(result.RosterDescribe);
            result.Steps.Add(result.SlotLines);
            CheckResult(roster, slot1, result);

            if (!resultCaptured)
            {
                capture?.Invoke("result");
            }

            return result;
        }

        public static void Stage(MatchPlayControl control)
        {
            Simulation sim = control.Sim;
            control.HumanPickupEnabled = false;
            control.AiEnabled = true;

            if (control.Ai == null)
            {
                throw new InvalidOperationException("the default match AI has no heuristic fallback controller");
            }

            control.Ai.Slots.Clear();
            control.Ai.Slots.Add(1);
            control.Ai.Slots.Add(2);
            control.Ai.Slots.Add(3);
            control.Ai.Enabled = true;
            control.Ai.SuppressSlot = slot => control.PlanRoster != null && control.PlanRoster.Handles(slot);

            for (int slot = 0; slot < sim.Players.Length; slot++)
            {
                sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            MatchScoreFixture.PlaceRokAtSpawn(sim);
            control.ResetRokToSpawn();

            // Slot 1 starts 60 units south of the free ROK so the LooseRokAttackOpponent target is a real
            // steering distance, not the N10 touch range. The two held skaters are staging only.
            SetPlayerAt(sim, 0, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, 1, 0.0f, -60.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 2, 80.0f, 80.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
            SetPlayerAt(sim, 4, 0.0f, -180.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 5, 0.0f, 180.0f, 0.0f, -1.0f);
            HoldStaged(sim);
        }

        public static void HoldStaged(Simulation sim)
        {
            MatchPlayN10Script.SetPlayerAt(sim, 0, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            MatchPlayN10Script.SetPlayerAt(sim, 3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
        }

        private static void SetPlayerAt(Simulation sim, int slot, float x, float y, float faceX, float faceY)
        {
            MatchPlayN10Script.SetPlayerAt(sim, slot, x, y, faceX, faceY);
        }

        private static void CheckResult(MatchPlanAiRoster roster, MatchPlanAi slot1, MatchPlayN11ScriptResult result)
        {
            if (!roster.TargetPhysicsEnabled)
            {
                result.Failures++;
                result.FailureText += "roster target physics disabled; ";
            }

            if (slot1 == null)
            {
                result.Failures++;
                result.FailureText += "slot 1 has no plan adapter; ";

                return;
            }

            if (!slot1.UseTargetPhysics || result.Slot1PhysicsTicks < 10)
            {
                result.Failures++;
                result.FailureText += "slot1 target physics did not run (" + result.Slot1PhysicsTicks + " ticks); ";
            }

            if (result.Slot1BankedTicks < 1 && result.RosterBankedTicks < 1)
            {
                result.Failures++;
                result.FailureText += "no bank force was applied by any plan instance; ";
            }

            if (result.Slot1Displacement < 5.0f)
            {
                result.Failures++;
                result.FailureText += "slot1 displacement too small (" + F(result.Slot1Displacement) + "); ";
            }

            if (result.Slot1HeadingTravelDeg < 5.0f)
            {
                result.Failures++;
                result.FailureText += "slot1 heading did not change enough ("
                                      + F(result.Slot1HeadingTravelDeg) + " deg); ";
            }

            if (result.Slot1StartDistance < 5.0f || result.Slot1MinDistance >= result.Slot1StartDistance - 2.0f)
            {
                result.Failures++;
                result.FailureText += "first-target distance did not decrease (start="
                                      + F(result.Slot1StartDistance) + " min=" + F(result.Slot1MinDistance) + "); ";
            }

            if (slot1.PhysicsTargetChanges < 1 || result.TargetChanges < 1)
            {
                result.Failures++;
                result.FailureText += "no plan target change was recorded; ";
            }

            if (result.AiPickups < 1)
            {
                result.Failures++;
                result.FailureText += "no AI pickup during the target-steering run; ";
            }

            if (result.RosterBridgeFallbackTicks < 1)
            {
                result.Failures++;
                result.FailureText += "no bridge-fallback ticks reported for the unported behaviour kinds; ";
            }

            if (result.TicksRun > MaxTicks)
            {
                result.Failures++;
                result.FailureText += "tick guard exceeded; ";
            }
        }

        private static string F(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
