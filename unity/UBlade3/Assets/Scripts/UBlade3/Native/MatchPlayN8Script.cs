using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Sim;
using UBlade3.PlanVM;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>Result of the deterministic N8 plan-driven AI run.</summary>
    public sealed class MatchPlayN8ScriptResult
    {
        public int Failures;

        public string FailureText = string.Empty;

        public int AiPickups;

        public int AiShots;

        public int AiPasses;

        public int PlanBoundTick;

        public int PassingPlayTick;

        public int PlanPassTick;

        public int PlannedPickupTick;

        public int PlanTicks;

        public int PlanBehaviorChanges;

        public int PlanTargetChanges;

        public int PlanPassRequests;

        public int PlanShotRequests;

        public int PlanFallbackMotionTicks;

        public int UnsupportedCount;

        public string PlanDescribe = string.Empty;

        public string PlanImpl = string.Empty;

        public string PlanOpcodeLine = string.Empty;

        public string PlanDigest = string.Empty;

        public string UnsupportedIds = string.Empty;

        public readonly List<string> BehaviorPath = new List<string>();

        public readonly List<string> Steps = new List<string>();

        public readonly List<string> Trace = new List<string>();
    }

    /// <summary>
    /// Deterministic N8 run: one AI skater (slot 1) picks the ROK up through the match's real
    /// RokInRange/Attach rule, the CARRYROK plan then binds to its live Simulation state, its
    /// TurnToMate -> PassingPlay behaviours and timers run, and its own pass decision becomes a real
    /// MatchPossessionRules.Pass through MatchPlayControl. The non-plan skaters are held at their
    /// staged positions for determinism (labelled staging); everything the plan decides is its own.
    ///
    /// It is the same state machine the Editor and Windows captures drive.
    /// </summary>
    public static class MatchPlayN8Script
    {
        public const int PlanSlot = ObjectConstants.SlotDromer1;

        public const int N8HumanSlot = ObjectConstants.SlotDromer0;

        public const string PlanName = MatchPlanAi.DefaultPlanName;

        public const float StagedHumanX = -28.0f;

        public const float StagedHumanY = 28.0f;

        public const float StagedTargetX = 0.0f;

        public const float StagedTargetY = 120.0f;

        public const int PickupGuardTicks = 180;

        public const int PlanGuardTicks = 240;

        public readonly struct StagePoint
        {
            public readonly float X;

            public readonly float Y;

            public readonly float FaceX;

            public readonly float FaceY;

            public StagePoint(float x, float y, float faceX, float faceY)
            {
                X = x;
                Y = y;
                FaceX = faceX;
                FaceY = faceY;
            }
        }

        /// <summary>Configures the control for the N8 plan-AI scenario (AI slot 1, real match rules kept).</summary>
        public static void Stage(MatchPlayControl control, string dataPath)
        {
            Simulation sim = control.Sim;
            control.HumanPickupEnabled = false;
            control.AiEnabled = true;

            if (control.Ai == null)
            {
                control.AttachAi(PlanSlot);
            }

            if (!control.Ai.Slots.Contains(PlanSlot))
            {
                control.Ai.Slots.Add(PlanSlot);
            }

            control.Ai.Enabled = true;
            control.Ai.Slots.Clear();
            control.Ai.Slots.Add(PlanSlot);
            control.Ai.SuppressSlot = slot => slot == PlanSlot;
            control.AttachPlanAi(PlanSlot, PlanName, dataPath);

            SetPlayerAt(sim, N8HumanSlot, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, ObjectConstants.SlotDromer2, -28.0f, -28.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, ObjectConstants.SlotDromer3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
            SetPlayerAt(sim, PlanSlot, 0.0f, -40.0f, 0.0f, 1.0f);

            for (int slot = 0; slot < sim.Players.Length; slot++)
            {
                sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            MatchScoreFixture.PlaceRokAtSpawn(sim);
            control.ResetRokToSpawn();
            HoldStaged(sim);
        }

        /// <summary>Keeps the staged non-plan skaters at their positions (deterministic test staging).</summary>
        public static void HoldStaged(Simulation sim)
        {
            SetPlayerAt(sim, N8HumanSlot, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, ObjectConstants.SlotDromer3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
        }

        public static void SetPlayerAt(Simulation sim, int slot, float x, float y, float faceX, float faceY)
        {
            PlayerState player = sim.Players[slot];
            CollisionEllipsoid arena = EllipsoidStatics.Arena;
            float z = arena.GetZ(x, y);
            SimVec3 position = new SimVec3(x, y, z);
            SimVec3 normal;

            if (!arena.NormalAtPoint(position, out normal))
            {
                normal = new SimVec3(0.0f, 0.0f, 1.0f);
            }

            float length = (float)Math.Sqrt((faceX * faceX) + (faceY * faceY));

            if (length < 1e-6f)
            {
                faceX = 0.0f;
                faceY = 1.0f;
                length = 1.0f;
            }

            SimVec3 direction = new SimVec3(faceX / length, faceY / length, 0.0f);

            player.Body.Pos = position;
            player.Body.PrevPos = position;
            player.Body.Vel = SimVec3.Zero;
            player.Body.SmoothVel = SimVec3.Zero;
            player.Body.Axis = SimVec3.Zero;
            player.Body.Speed = 0.0f;
            player.Body.VelDir = direction;
            player.Body.VelDirLatched = direction;
            player.Body.SurfNormal = normal;
            player.AimPosition = new SimVec3(x, y, z + MatchWorldData.HeadHeight);
        }

        public static MatchPlayN8ScriptResult Run(MatchPlayControl control, string dataPath, Action<string> capture,
            Action<HeldBinding> sceneTick = null)
        {
            MatchPlayN8ScriptResult result = new MatchPlayN8ScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());

            Stage(control, dataPath);
            capture?.Invoke("start");

            int pickupGuard = 0;

            while (control.AiPickups == 0 && pickupGuard < PickupGuardTicks)
            {
                control.Advance(HeldBinding.None, held =>
                {
                    tick(held);
                    HoldStaged(sim);
                });
                pickupGuard++;
            }

            result.AiPickups = control.AiPickups;
            result.PlannedPickupTick = control.Ticks;

            if (control.AiPickups == 0)
            {
                result.Failures++;
                result.FailureText += "AI plan skater did not pick up through the match rule; ";
            }

            capture?.Invoke("ai-pickup");

            int bindGuard = 0;

            while ((control.PlanAi == null || control.PlanAi.BindCount == 0) && bindGuard < 30)
            {
                Advance(control, tick, sim);
                bindGuard++;
            }

            result.PlanBoundTick = control.Ticks;

            if (control.PlanAi == null || control.PlanAi.BindCount == 0)
            {
                result.Failures++;
                result.FailureText += "CARRYROK plan did not bind after pickup; ";
            }

            capture?.Invoke("plan-bound");

            int behaviorGuard = 0;

            while (behaviorGuard < PlanGuardTicks)
            {
                if (control.PlanAi != null && control.PlanAi.BehaviorPath.Contains("PassingPlay"))
                {
                    break;
                }

                Advance(control, tick, sim);
                behaviorGuard++;
            }

            result.PassingPlayTick = control.Ticks;

            if (control.PlanAi == null || !control.PlanAi.BehaviorPath.Contains("PassingPlay"))
            {
                result.Failures++;
                result.FailureText += "plan did not run TurnToMate into PassingPlay; ";
            }

            capture?.Invoke("passing-play");

            int passGuard = 0;

            while (control.AiPasses == 0 && passGuard < PlanGuardTicks)
            {
                Advance(control, tick, sim);
                passGuard++;
            }

            result.PlanPassTick = control.Ticks;
            result.AiShots = control.AiShots;
            result.AiPasses = control.AiPasses;

            if (control.AiPasses == 0)
            {
                result.Failures++;
                result.FailureText += "plan pass decision did not become a match pass; ";
            }

            capture?.Invoke("pass");

            for (int i = 0; i < 6; i++)
            {
                Advance(control, tick, sim);
            }

            capture?.Invoke("result");

            MatchPlanAi plan = control.PlanAi;

            if (plan != null)
            {
                result.PlanDescribe = plan.Describe();
                result.PlanImpl = plan.ImplLine();
                result.PlanOpcodeLine = plan.OpcodeLine();
                result.PlanDigest = PlanVmDigest.Hex(plan.TraceDigest());
                result.PlanTicks = plan.PlanTicks;
                result.PlanBehaviorChanges = plan.BehaviorChanges;
                result.PlanTargetChanges = plan.TargetChanges;
                result.PlanPassRequests = plan.PlanPassRequests;
                result.PlanShotRequests = plan.PlanShotRequests;
                result.PlanFallbackMotionTicks = plan.FallbackMotionTicks;
                result.UnsupportedCount = plan.UnsupportedIds.Count;
                result.UnsupportedIds = JoinSet(plan.UnsupportedIds);
                result.BehaviorPath.AddRange(plan.BehaviorPath);

                for (int index = 0; index < plan.Trace.Count && index < 400; index++)
                {
                    result.Trace.Add(plan.Trace[index]);
                }
            }
            else
            {
                result.Failures++;
                result.FailureText += "no PlanAi adapter was attached; ";
            }

            CheckPlanResult(control, plan, result);
            result.Steps.Add(result.PlanDescribe);

            return result;
        }

        private static void Advance(MatchPlayControl control, Action<HeldBinding> tick, Simulation sim)
        {
            control.Advance(HeldBinding.None, held =>
            {
                tick(held);
                HoldStaged(sim);
            });
        }

        private static void CheckPlanResult(MatchPlayControl control, MatchPlanAi plan, MatchPlayN8ScriptResult result)
        {
            if (plan == null)
            {
                return;
            }

            if (plan.BindCount != 1)
            {
                result.Failures++;
                result.FailureText += "plan binds=" + plan.BindCount + "; ";
            }

            if (plan.BehaviorPath.Count < 2 || plan.BehaviorPath[0] != "TurnToMate" || !plan.BehaviorPath.Contains("PassingPlay"))
            {
                result.Failures++;
                result.FailureText += "plan behaviour path=" + string.Join(">", plan.BehaviorPath.ToArray()) + "; ";
            }

            if (plan.PlanPassRequests < 1 || control.AiPasses < 1)
            {
                result.Failures++;
                result.FailureText += "plan pass request=" + plan.PlanPassRequests + " match passes=" + control.AiPasses + "; ";
            }

            if (plan.PlanActionCounts.Count == 0 || plan.PlanDecisionCounts.Count == 0)
            {
                result.Failures++;
                result.FailureText += "no plan actions/decisions were executed; ";
            }

            int[] requiredDecisions = { PlanOpcodes.DecisionIds.DecidePosition, PlanOpcodes.DecisionIds.OkToPassToTeammate, PlanOpcodes.DecisionIds.RecvrInRangeNView };
            StringBuilder missing = new StringBuilder();

            for (int i = 0; i < requiredDecisions.Length; i++)
            {
                if (!plan.PlanDecisionCounts.ContainsKey(requiredDecisions[i]))
                {
                    missing.Append(requiredDecisions[i]).Append(',');
                }
            }

            if (missing.Length != 0)
            {
                result.Failures++;
                result.FailureText += "required plan decisions not executed: " + missing + "; ";
            }

            if (!plan.PlanActionCounts.ContainsKey(PlanOpcodes.ActionIds.SendMessage) ||
                !plan.PlanActionCounts.ContainsKey(PlanOpcodes.ActionIds.SetKnockedDownState))
            {
                result.Failures++;
                result.FailureText += "required plan actions not executed; ";
            }

            if (plan.TargetChanges < 1 || plan.BehaviorChanges < 2)
            {
                result.Failures++;
                result.FailureText += "plan target/behaviour changes too few (" + plan.TargetChanges + "/" + plan.BehaviorChanges + "); ";
            }

            if (plan.UnsupportedIds.Count == 0)
            {
                result.Failures++;
                result.FailureText += "unsupported plan telemetry is empty; ";
            }

            if (control.AiShots != 0)
            {
                result.Failures++;
                result.FailureText += "plan path took " + control.AiShots + " heuristic shots; ";
            }
        }

        /// <summary>
        /// Same staged scenario driven by the N7 heuristic controller only (no plan). It reports the
        /// fallback's outcome so the capture/check can state the contrast: the plan path passes on a
        /// plan decision and reports behaviour/target telemetry; the heuristic path has none.
        /// </summary>
        public static string CompareHeuristic(MatchWorldData world, string dataPath, out int planPickups,
            out int planShots, out int planPasses)
        {
            MatchSimulation sim = MatchSimulation.Create(world);
            ConfigureGoals(sim);

            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            MatchAudioDirector audio = new MatchAudioDirector(null);
            MatchPlayControl control = new MatchPlayControl(sim.Sim, config, audio);
            control.TraceFlight = false;
            control.AttachAi(PlanSlot);
            control.HumanPickupEnabled = false;
            sim.Sim.InputForSlot = slot => control.InputForSlot(slot);

            StagePlayers(sim.Sim);
            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            control.ResetRokToSpawn();
            HoldStaged(sim.Sim);

            int guard = 0;

            while (guard < 480)
            {
                control.Advance(HeldBinding.None, held =>
                {
                    sim.Sim.Tick();
                    HoldStaged(sim.Sim);
                });
                guard++;

                if (control.AiPasses > 0)
                {
                    break;
                }
            }

            planPickups = control.AiPickups;
            planShots = control.AiShots;
            planPasses = control.AiPasses;

            return "heuristic: aiPickups=" + control.AiPickups + " aiShots=" + control.AiShots
                   + " aiPasses=" + control.AiPasses + " ticks=" + control.Ticks
                   + " planAi=null (no behaviour/decision telemetry)";
        }

        private static void ConfigureGoals(MatchSimulation sim)
        {
            for (int slot = 0; slot < sim.Sim.Players.Length; slot++)
            {
                sim.Sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            sim.Sim.Rules.SetHomeScore(0);
            sim.Sim.Rules.SetAwayScore(0);
            SimVec3 north = MatchAiController.GoalPosition(sim.Sim, ObjectConstants.SlotNGoal);
            SimVec3 south = MatchAiController.GoalPosition(sim.Sim, ObjectConstants.SlotSGoal);
            _ = north;
            _ = south;
        }

        /// <summary>Creates a fresh headless scenario with the control/plan wiring the N8 check uses.</summary>
        public static MatchPlayControl CreateScenario(MatchWorldData world, string dataPath,
            out MatchSimulation sim, out MatchPlayControl control, out MatchAudioDirector audio)
        {
            sim = MatchSimulation.Create(world);
            ConfigureGoals(sim);
            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            audio = new MatchAudioDirector(null);
            control = new MatchPlayControl(sim.Sim, config, audio);
            control.HumanPickupEnabled = false;
            control.AttachAi(PlanSlot);
            control.AiEnabled = true;
            MatchPlayControl localControl = control;
            sim.Sim.InputForSlot = slot => localControl.InputForSlot(slot);
            StagePlayers(sim.Sim);
            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            control.ResetRokToSpawn();
            HoldStaged(sim.Sim);
            control.AttachPlanAi(PlanSlot, PlanName, dataPath);
            control.Ai.SuppressSlot = slot => slot == PlanSlot;

            return control;
        }

        private static void StagePlayers(Simulation sim)
        {
            SetPlayerAt(sim, N8HumanSlot, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, ObjectConstants.SlotDromer2, -28.0f, -28.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, ObjectConstants.SlotDromer3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
            SetPlayerAt(sim, PlanSlot, 0.0f, -40.0f, 0.0f, 1.0f);
        }

        private static string JoinSet(HashSet<string> values)
        {
            List<string> sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);

            return string.Join(",", sorted.ToArray());
        }
    }
}
