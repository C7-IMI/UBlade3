using System;
using System.Collections.Generic;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>Result of the deterministic N10 default-match run (normal build path, no N9 flag).</summary>
    public sealed class MatchPlayN10ScriptResult
    {
        public int Failures;

        public string FailureText = string.Empty;

        public int TicksRun;

        public int AiPickups;

        public int AiPasses;

        public int AiShots;

        public int PickupTick;

        public int PassTick;

        public int MaxActiveSlots;

        public int ActiveAtEnd;

        public int PlanIds;

        public int PlanPassRequests;

        public int PlanShotRequests;

        public int Rebinds;

        public int Relays;

        public int RelayBudgetHits;

        public int UnsupportedDecisions;

        public int HeuristicFallbackTicks;

        public int SuppressedUpdateTicks;

        public string PlanIdList = string.Empty;

        public string RosterDescribe = string.Empty;

        public string RosterSlots = string.Empty;

        public string Digest = string.Empty;

        public readonly List<string> Steps = new List<string>();
    }

    /// <summary>
    /// N10: deterministic run of the **default** match AI. Unlike the N9 suite this script never
    /// attaches or replaces the roster - it requires that the normal MatchPlay.Build already attached
    /// the default PlanVM AI. It only stages the deterministic positions and drives the same control
    /// the interactive player uses, proving that the default (non-N9-flag) path has plan instances on
    /// slots 1-5, suppressed heuristic updates, explicitly logged fallback ticks and real plan events
    /// (AI pickup -> plan pass).
    /// </summary>
    public static class MatchPlayN10Script
    {
        public const int MaxTicks = 220;

        public const int PostPassTicks = 110;

        public const float StagedHumanX = -28.0f;

        public const float StagedHumanY = 28.0f;

        public const float StagedTargetX = 0.0f;

        public const float StagedTargetY = 120.0f;

        public static readonly int[] PlanSlots = { 1, 2, 3, 4, 5 };

        public static MatchPlayN10ScriptResult Run(MatchPlayControl control, Action<string> capture,
            Action<HeldBinding> sceneTick = null)
        {
            MatchPlayN10ScriptResult result = new MatchPlayN10ScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());

            MatchPlanAiRoster roster = control.PlanRoster;

            if (roster == null || !control.DefaultPlanAi)
            {
                result.Failures++;
                result.FailureText = "default PlanVM AI is not attached by the normal build path; ";
                result.RosterDescribe = "defaultPlanAi=False";
                capture?.Invoke("result");

                return result;
            }

            Stage(control);
            capture?.Invoke("start");

            bool planSwitchCaptured = false;
            bool planShotCaptured = false;
            int passSeenAt = -1;
            int guard = 0;

            while (guard < MaxTicks)
            {
                control.Advance(HeldBinding.None, held =>
                {
                    tick(held);
                    HoldStaged(sim);
                });

                guard++;

                if (control.AiPickups > 0 && result.PickupTick == 0)
                {
                    result.PickupTick = control.Ticks;
                    capture?.Invoke("ai-pickup");
                }

                if (result.MaxActiveSlots < roster.BoundCount())
                {
                    result.MaxActiveSlots = roster.BoundCount();
                }

                if (!planSwitchCaptured)
                {
                    MatchPlanAi carrier = roster.Adapter(1);

                    if (carrier != null && UsesPlan(carrier, MatchPlanAi.DefaultPlanName))
                    {
                        planSwitchCaptured = true;
                        capture?.Invoke("plan-switch");
                    }
                }

                if (roster.TotalPlanPassRequests > 0 && passSeenAt < 0)
                {
                    passSeenAt = control.Ticks;
                    capture?.Invoke("plan-pass");
                }

                if (control.AiShots > 0 && !planShotCaptured)
                {
                    planShotCaptured = true;
                    capture?.Invoke("plan-shot");
                }

                if (control.AiPasses > 0 && passSeenAt > 0)
                {
                    for (int index = 0; index < PostPassTicks && guard < MaxTicks; index++)
                    {
                        control.Advance(HeldBinding.None, held =>
                        {
                            tick(held);
                            HoldStaged(sim);
                        });

                        guard++;

                        if (control.AiShots > 0 && !planShotCaptured)
                        {
                            planShotCaptured = true;
                            capture?.Invoke("plan-shot");
                        }
                    }

                    break;
                }

                if (guard == 20)
                {
                    capture?.Invoke("plans-active");
                }
            }

            result.TicksRun = control.Ticks;
            result.AiPickups = control.AiPickups;
            result.AiPasses = control.AiPasses;
            result.AiShots = control.AiShots;
            result.PassTick = passSeenAt;
            result.MaxActiveSlots = Math.Max(result.MaxActiveSlots, roster.MaxBoundSlots);
            result.ActiveAtEnd = roster.BoundCount();
            result.PlanPassRequests = roster.TotalPlanPassRequests;
            result.PlanShotRequests = roster.TotalPlanShotRequests;
            result.Rebinds = roster.TotalRebinds();
            result.Relays = roster.RelayCount;
            result.RelayBudgetHits = roster.RelayBudgetHits;
            result.UnsupportedDecisions = roster.TotalUnsupportedDecisions;
            result.HeuristicFallbackTicks = roster.HeuristicFallbackTicks;
            result.SuppressedUpdateTicks = control.Ai == null ? 0 : control.Ai.SuppressedUpdateTicks;
            result.PlanIdList = roster.PlanIds();
            result.PlanIds = result.PlanIdList.Length == 0 ? 0 : result.PlanIdList.Split(',').Length;
            result.RosterDescribe = roster.Describe();
            result.RosterSlots = roster.DescribeSlots();
            result.Digest = roster.DigestHex();
            result.Steps.Add(result.RosterDescribe);
            result.Steps.Add(result.RosterSlots);

            CheckResult(control, roster, result);
            capture?.Invoke("result");

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

            SimVec3 rok = sim.Rok.Body.Pos;
            SetPlayerAt(sim, 0, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, 1, rok.x, rok.y, 0.0f, 1.0f);
            SetPlayerAt(sim, 2, 30.0f, -20.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
            SetPlayerAt(sim, 4, 0.0f, -180.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 5, 0.0f, 180.0f, 0.0f, -1.0f);
            HoldStaged(sim);
        }

        public static void HoldStaged(Simulation sim)
        {
            SetPlayerAt(sim, 0, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, 3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
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

        private static void CheckResult(MatchPlayControl control, MatchPlanAiRoster roster, MatchPlayN10ScriptResult result)
        {
            if (!control.DefaultPlanAi || roster == null)
            {
                result.Failures++;
                result.FailureText += "default PlanVM AI missing; ";
            }

            if (roster.PlanSlots.Count != PlanSlots.Length)
            {
                result.Failures++;
                result.FailureText += "default roster slots=" + roster.PlanSlots.Count + " not " + PlanSlots.Length + "; ";
            }

            if (result.MaxActiveSlots != PlanSlots.Length)
            {
                result.Failures++;
                result.FailureText += "only " + result.MaxActiveSlots + " of " + PlanSlots.Length
                                      + " default plan slots active; ";
            }

            for (int index = 0; index < PlanSlots.Length; index++)
            {
                MatchPlanAi adapter = roster.Adapter(PlanSlots[index]);

                if (adapter == null)
                {
                    result.Failures++;
                    result.FailureText += "slot " + PlanSlots[index] + " not PlanVM-driven; ";
                    continue;
                }

                if (!adapter.Bound || adapter.BindCount < 1 || adapter.PlanTicks < 1)
                {
                    result.Failures++;
                    result.FailureText += "slot " + adapter.Slot + " plan inactive (bound=" + adapter.Bound
                                          + " binds=" + adapter.BindCount + " ticks=" + adapter.PlanTicks + "); ";
                }
            }

            if (result.AiPickups < 1)
            {
                result.Failures++;
                result.FailureText += "no default-AI pickup; ";
            }

            if (result.AiPasses < 1 || result.PlanPassRequests < 1)
            {
                result.Failures++;
                result.FailureText += "no default-AI plan pass (aiPasses=" + result.AiPasses
                                      + " planPassRequests=" + result.PlanPassRequests + "); ";
            }

            if (result.HeuristicFallbackTicks > PlanSlots.Length * 2)
            {
                result.Failures++;
                result.FailureText += "heuristic fallback took over " + result.HeuristicFallbackTicks + " slot ticks; ";
            }

            if (result.SuppressedUpdateTicks < result.TicksRun)
            {
                result.Failures++;
                result.FailureText += "heuristic update not suppressed for plan slots ("
                                      + result.SuppressedUpdateTicks + " < " + result.TicksRun + "); ";
            }

            if (result.RelayBudgetHits > 0)
            {
                result.Failures++;
                result.FailureText += "plan-message relay budget hit; ";
            }

            if (result.TicksRun > MaxTicks)
            {
                result.Failures++;
                result.FailureText += "tick guard exceeded; ";
            }
        }

        private static bool UsesPlan(MatchPlanAi adapter, string planName)
        {
            for (int index = 0; index < adapter.PlanIdsUsed.Count; index++)
            {
                if (string.Equals(adapter.PlanIdsUsed[index], planName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
