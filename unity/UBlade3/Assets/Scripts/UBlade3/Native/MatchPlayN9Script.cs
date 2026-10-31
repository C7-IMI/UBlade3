using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>Result of the deterministic N9 whole-match plan roster run.</summary>
    public sealed class MatchPlayN9ScriptResult
    {
        public int Failures;

        public string FailureText = string.Empty;

        public int TicksRun;

        public int AiPickups;

        public int AiPasses;

        public int AiShots;

        public int PassTick;

        public int PickupTick;

        public int MaxActiveSlots;

        public int PlanIds;

        public int PlanPassRequests;

        public int PlanShotRequests;

        public int Rebinds;

        public int MaxRebindsPerSlot;

        public int Relays;

        public int RelayBudgetHits;

        public int PossessionEvents;

        public int UnsupportedDecisions;

        public string PlanIdList = string.Empty;

        public string RosterDescribe = string.Empty;

        public string RosterSlots = string.Empty;

        public string Digest = string.Empty;

        public readonly List<string> Steps = new List<string>();

        public readonly List<string> Trace = new List<string>();
    }

    /// <summary>
    /// Deterministic N9 run for the whole six-skater native match: one persistent PlanVM instance per
    /// non-human slot from the shipped team file plan tables (Defense/Goalie entries per
    /// EngineObject::SelectAndActivatePlan), real pickup, carrier plan change to CARRYROK with the
    /// documented TurnToMate entry, a plan pass through the shipped Throw, LostIt-driven role change
    /// back to a non-carrier plan, and every goalie running its GOALIE plan. Test staging only holds the
    /// human and pass-target skaters (labelled); everything the plans decide is their own.
    /// </summary>
    public static class MatchPlayN9Script
    {
        public const int MaxTicks = 240;

        public const int PostPassTicks = 90;

        public const int MaxRebindsPerSlot = 8;

        public const float StagedHumanX = -28.0f;

        public const float StagedHumanY = 28.0f;

        public const float StagedTargetX = 0.0f;

        public const float StagedTargetY = 120.0f;

        public static readonly int[] PlanSlots = { 1, 2, 3, 4, 5 };

        public static MatchPlayN9ScriptResult Run(MatchPlayControl control, MatchWorldData world, string dataPath,
            Action<string> capture, Action<HeldBinding> sceneTick = null)
        {
            MatchPlayN9ScriptResult result = new MatchPlayN9ScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());

            Stage(control, world, dataPath);
            capture?.Invoke("start");

            int passSeenAt = -1;
            int guard = 0;
            bool planSwitchCaptured = false;

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

                if (control.PlanRoster != null && result.MaxActiveSlots < control.PlanRoster.BoundCount())
                {
                    result.MaxActiveSlots = control.PlanRoster.BoundCount();
                }

                if (control.PlanRoster != null && control.PlanRoster.TotalPlanPassRequests > 0 && passSeenAt < 0)
                {
                    passSeenAt = control.Ticks;
                    capture?.Invoke("plan-pass");
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
                    }

                    break;
                }

                if (control.PlanRoster != null && !planSwitchCaptured)
                {
                    MatchPlanAi carrier = control.PlanRoster.Adapter(1);

                    if (carrier != null && UsesPlan(carrier, MatchPlanAi.DefaultPlanName))
                    {
                        planSwitchCaptured = true;
                        capture?.Invoke("plan-switch");
                    }
                }

                if (guard == 20)
                {
                    capture?.Invoke("plans-bound");
                }
            }

            result.TicksRun = control.Ticks;
            result.AiPickups = control.AiPickups;
            result.AiPasses = control.AiPasses;
            result.AiShots = control.AiShots;
            result.PassTick = passSeenAt;

            MatchPlanAiRoster roster = control.PlanRoster;

            if (roster == null)
            {
                result.Failures++;
                result.FailureText += "no plan roster attached; ";
                capture?.Invoke("result");

                return result;
            }

            result.MaxActiveSlots = Math.Max(result.MaxActiveSlots, roster.MaxBoundSlots);
            result.PlanPassRequests = roster.TotalPlanPassRequests;
            result.PlanShotRequests = roster.TotalPlanShotRequests;
            result.Rebinds = roster.TotalRebinds();
            result.MaxRebindsPerSlot = roster.MaxRebindsPerSlot();
            result.Relays = roster.RelayCount;
            result.RelayBudgetHits = roster.RelayBudgetHits;
            result.PossessionEvents = roster.PossessionEvents;
            result.UnsupportedDecisions = roster.TotalUnsupportedDecisions;
            result.PlanIdList = roster.PlanIds();
            result.PlanIds = roster.PlanIds().Length == 0 ? 0 : roster.PlanIds().Split(',').Length;
            result.RosterDescribe = roster.Describe();
            result.RosterSlots = roster.DescribeSlots();
            result.Digest = roster.DigestHex();
            result.Steps.Add(result.RosterDescribe);
            result.Steps.Add(result.RosterSlots);

            for (int index = 0; index < roster.Trace.Count; index++)
            {
                result.Trace.Add(roster.Trace[index]);
            }

            CheckResult(control, roster, result);
            capture?.Invoke("result");

            return result;
        }

        /// <summary>Creates a fresh headless scenario with the whole-match roster wiring.</summary>
        public static MatchPlayControl CreateScenario(MatchWorldData world, string dataPath,
            out MatchSimulation sim, out MatchPlayControl control, out MatchAudioDirector audio)
        {
            sim = MatchSimulation.Create(world);
            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            audio = new MatchAudioDirector(null);
            control = new MatchPlayControl(sim.Sim, config, audio);
            control.HumanPickupEnabled = false;
            control.AttachAi(PlanSlots);
            control.AiEnabled = true;
            MatchPlayControl localControl = control;
            sim.Sim.InputForSlot = slot => localControl.InputForSlot(slot);

            return control;
        }

        /// <summary>Configures the whole-match roster and the deterministic staging.</summary>
        public static void Stage(MatchPlayControl control, MatchWorldData world, string dataPath)
        {
            Simulation sim = control.Sim;
            control.HumanPickupEnabled = false;
            control.AiEnabled = true;

            if (control.Ai == null)
            {
                control.AttachAi(PlanSlots);
            }

            control.Ai.Slots.Clear();
            control.Ai.Slots.Add(1);
            control.Ai.Slots.Add(2);
            control.Ai.Slots.Add(3);
            control.Ai.Enabled = true;
            control.Ai.SuppressSlot = slot => control.PlanRoster != null && control.PlanRoster.Handles(slot);

            // The N9 suite is the explicit capture path; detach a default roster from the build and
            // create the N9 roster with its original semantics (no initial free-ROK message).
            if (control.PlanRoster != null)
            {
                control.DetachPlanRoster();
            }

            // The default N10 roster seeds PlanAiRandom; the explicit N9 suite restores its pre-N10
            // starting state so its deterministic evidence is unchanged by the default-AI work.
            PlanAiRandom.Seed(0);

            MatchPlanAiRoster roster = new MatchPlanAiRoster(control, sim, control.Config, dataPath,
                world, PlanSlots);
            control.AttachPlanRoster(roster);
            // DetachPlanRoster cleared the heuristic suppression; restore it for the explicit suite.
            control.Ai.SuppressSlot = slot => control.PlanRoster != null && control.PlanRoster.Handles(slot);

            for (int slot = 0; slot < sim.Players.Length; slot++)
            {
                sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            MatchScoreFixture.PlaceRokAtSpawn(sim);
            control.ResetRokToSpawn();
            sim.InputForSlot = slot => control.InputForSlot(slot);

            SimVec3 rok = sim.Rok.Body.Pos;
            SetPlayerAt(sim, 0, StagedHumanX, StagedHumanY, 0.0f, 1.0f);
            SetPlayerAt(sim, 1, rok.x, rok.y, 0.0f, 1.0f);
            SetPlayerAt(sim, 2, 30.0f, -20.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 3, StagedTargetX, StagedTargetY, 0.0f, 1.0f);
            SetPlayerAt(sim, 4, 0.0f, -180.0f, 0.0f, 1.0f);
            SetPlayerAt(sim, 5, 0.0f, 180.0f, 0.0f, -1.0f);
            HoldStaged(sim);
        }

        /// <summary>Holds the two staged non-plan-driven skaters for determinism (human + pass target).</summary>
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

        private static void CheckResult(MatchPlayControl control, MatchPlanAiRoster roster, MatchPlayN9ScriptResult result)
        {
            if (result.AiPickups < 1)
            {
                result.Failures++;
                result.FailureText += "no AI pickup; ";
            }

            if (result.AiPasses < 1 || result.PlanPassRequests < 1)
            {
                result.Failures++;
                result.FailureText += "no plan pass (aiPasses=" + result.AiPasses
                                      + " planPassRequests=" + result.PlanPassRequests + "); ";
            }

            if (result.MaxActiveSlots < PlanSlots.Length)
            {
                result.Failures++;
                result.FailureText += "only " + result.MaxActiveSlots + " of " + PlanSlots.Length
                                      + " plan slots active at once; ";
            }

            for (int index = 0; index < PlanSlots.Length; index++)
            {
                MatchPlanAi adapter = roster.Adapter(PlanSlots[index]);

                if (adapter == null)
                {
                    result.Failures++;
                    result.FailureText += "slot " + PlanSlots[index] + " has no plan adapter; ";
                    continue;
                }

                if (!adapter.Bound || adapter.BindCount < 1 || adapter.PlanTicks < 1)
                {
                    result.Failures++;
                    result.FailureText += "slot " + adapter.Slot + " plan not active (bound=" + adapter.Bound
                                          + " binds=" + adapter.BindCount + " ticks=" + adapter.PlanTicks + "); ";
                }
            }

            MatchPlanAi carrier = roster.Adapter(1);

            if (carrier == null || !UsesPlan(carrier, MatchPlanAi.DefaultPlanName))
            {
                result.Failures++;
                result.FailureText += "carrier slot never ran CARRYROK; ";
            }

            bool sawGoalie = false;

            for (int index = 0; index < PlanSlots.Length; index++)
            {
                MatchPlanAi adapter = roster.Adapter(PlanSlots[index]);

                if (adapter != null && adapter.PlanName.IndexOf("GOALIE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sawGoalie = true;
                }
            }

            if (!sawGoalie)
            {
                result.Failures++;
                result.FailureText += "no goalie plan active; ";
            }

            if (result.Rebinds < 2)
            {
                result.Failures++;
                result.FailureText += "plan/role rebinds too few: " + result.Rebinds + "; ";
            }

            if (result.MaxRebindsPerSlot > MaxRebindsPerSlot)
            {
                result.Failures++;
                result.FailureText += "infinite/orphan rebind loop on a slot: " + result.MaxRebindsPerSlot + "; ";
            }

            if (result.TicksRun > MaxTicks)
            {
                result.Failures++;
                result.FailureText += "tick guard exceeded; ";
            }

            if (result.RelayBudgetHits > 0)
            {
                result.Failures++;
                result.FailureText += "relay budget hit (possible message loop); ";
            }

            if (result.Relays < 1)
            {
                result.Failures++;
                result.FailureText += "no cross-slot plan messages relayed; ";
            }
        }
    }
}
