using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;

namespace UBlade3.Native
{
    /// <summary>
    /// N9: the whole-match PlanVM AI roster. One persistent plan instance runs per non-human slot -
    /// the three AI skaters and the two goalies - each over the slot's own shipped plan table from the
    /// team file (ObjectDefinition.Plans, DATA-FORMATS.md). Per EngineObject::SelectAndActivatePlan a
    /// non-goalie AI skater starts on "Defense" behaviour 0 and a goalie on plan table index 0
    /// ("Goalie") behaviour 0; the roster relays the engine possession events (GotIt/LostIt/RokIsFree)
    /// into the plan instances, relays their cross-slot messages, and each adapter adopts the plan's own
    /// CHANGE_OF_PLAN requests by rebinding to the named shipped plan/behaviour.
    ///
    /// Bounded and honest: unported decision bodies stay per-slot explicit telemetry and abort only the
    /// current script step; no branch is guessed. The number of relays/rebinds is bounded and reported.
    /// </summary>
    public sealed class MatchPlanAiRoster
    {
        public const int RelayPerTickBudget = 64;

        public const int RelayTotalBudget = 2048;

        // The default match starts the plan RNG from a fixed seed so two normal-build runs replay the
        // same plan decisions; the explicit N9 roster keeps the pre-existing sequence (bridge disabled).
        public const uint DefaultRandomSeed = 0x4E313020;

        public readonly MatchPlanAi[] Slots = new MatchPlanAi[ObjectConstants.PlayerObjectCount];

        public readonly List<int> PlanSlots = new List<int>();

        public bool Enabled = true;

        public int TickCount;

        public int MaxBoundSlots;

        public int RelayCount;

        public int RelayBudgetHits;

        public int PossessionEvents;

        public int LostItEvents;

        public int GotItEvents;

        public int RokIsFreeEvents;

        public int TotalPlanPassRequests;

        public int TotalPlanShotRequests;

        public int TotalUnsupportedDecisions;

        public int TotalRuntimeErrors;

        public int LastAttachedSlot = -1;

        public int HeuristicFallbackTicks;

        public int AnimationBridgeEvents;

        public int PlanPhysicsTicks;

        public int BridgeFallbackTicks;

        public int PhysicsTargetChanges;

        public bool TargetPhysicsEnabled;

        public bool InjectInitialRokEvent;

        private bool possessionInitialised;

        public string Failure = string.Empty;

        public readonly List<string> Trace = new List<string>();

        private readonly Simulation sim;

        // N17: the live MatchStats table is owned by the default roster and bound into PlanVM.
        public readonly MatchStatAccumulator MatchStats = new MatchStatAccumulator();

        private readonly MatchPlayControl control;

        private ulong digest = PlanVmDigest.Offset;

        private bool digestDirty = true;

        public MatchPlanAiRoster(MatchPlayControl control, Simulation sim, MatchPlayConfig config, string dataPath,
            MatchWorldData world, bool injectInitialRokEvent, params int[] planSlots)
        {
            InjectInitialRokEvent = injectInitialRokEvent;

            if (injectInitialRokEvent)
            {
                PlanAiRandom.Seed(DefaultRandomSeed);
            }
            this.control = control;
            this.sim = sim;

            if (planSlots == null || planSlots.Length == 0)
            {
                Failure = "no plan slots requested";
                Trace.Add("roster-failed " + Failure);

                return;
            }

            MatchPlanAiDecisions.EnsureRegistered();

            for (int index = 0; index < planSlots.Length; index++)
            {
                int slot = planSlots[index];

                if (slot < 0 || slot >= ObjectConstants.PlayerObjectCount || slot == MatchSimulation.HumanSlot)
                {
                    continue;
                }

                MatchPlayerSlot player = world == null || world.Players == null ? null : world.Players[slot];
                string[] plans = player == null || player.Definition == null || player.Definition.Plans == null
                    ? null
                    : player.Definition.Plans.ToArray();

                if (plans == null || plans.Length == 0)
                {
                    Failure += "slot " + slot + " has no shipped SetPlans table; ";
                    Trace.Add("slot-missing-plans slot=" + slot);

                    continue;
                }

                bool goalie = player.IsGoalie;
                string initialPlan = goalie ? plans[0] : FindPlan(plans, MatchPlanAi.InitialSkaterPlan);

                if (initialPlan == null)
                {
                    initialPlan = plans[0];
                }

                int behavior = goalie ? MatchPlanAi.InitialGoalieBehavior : MatchPlanAi.InitialSkaterBehavior;
                MatchPlanAi adapter = new MatchPlanAi(control, sim, config, slot, initialPlan, dataPath,
                    plans, behavior, true);

                Slots[slot] = adapter;
                PlanSlots.Add(slot);
                adapter.EnsureBound(0);
                Trace.Add("slot-plan slot=" + slot + " role=" + (goalie ? "goalie" : "skater")
                          + " table=[" + string.Join(",", plans) + "] initial=" + initialPlan + ":" + behavior);
            }

            LastAttachedSlot = sim.Rok.IsCarried ? sim.Rok.AttachedSlot : -1;
            digestDirty = true;
        }

        /// <summary>The existing N9-style constructor: no initial free-ROK message.</summary>
        public MatchPlanAiRoster(MatchPlayControl control, Simulation sim, MatchPlayConfig config, string dataPath,
            MatchWorldData world, params int[] planSlots)
            : this(control, sim, config, dataPath, world, false, planSlots)
        {
        }

        public bool Handles(int slot)
        {
            return slot >= 0 && slot < Slots.Length && Slots[slot] != null;
        }

        /// <summary>Enables the documented N10 animation bridge for every plan instance (default match only).</summary>
        /// <summary>Enables the exact N11 target-steering physics for every plan instance (default match).</summary>
        public void EnableN14Handlers(bool enabled)
        {
            PlanVM.PlanAiMatchDecisionsN14.Enabled = enabled;

            if (enabled)
            {
                PlanVM.PlanAiMatchAgendaItems.Reset();
            }

            Trace.Add("n14-handlers enabled=" + (enabled ? "True" : "False"));
        }

        public void EnableN13Handlers(bool enabled)
        {
            PlanVM.PlanAiMatchBehaviorParams.Enabled = enabled;
            PlanVM.PlanAiMatchAgendaItems.Enabled = enabled;
            Trace.Add("n13-handlers enabled=" + (enabled ? "True" : "False"));
        }

        public void EnableTargetPhysics(bool enabled)
        {
            TargetPhysicsEnabled = enabled;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null)
                {
                    adapter.EnableTargetPhysics(enabled);
                }
            }
        }

        public void EnableAnimationBridge(bool enabled)
        {
            AnimationBridgeEvents = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null)
                {
                    adapter.AnimationBridge = enabled;
                }
            }
        }

        public MatchPlanAi Adapter(int slot)
        {
            return Handles(slot) ? Slots[slot] : null;
        }

        public HeldBinding GetInput(int slot)
        {
            MatchPlanAi adapter = Adapter(slot);

            return adapter == null ? HeldBinding.None : adapter.GetInput(sim, slot);
        }

        /// <summary>Runs after the heuristic update and before the match's pending AI requests.</summary>
        public void BeforeMatchTick(uint nowMs)
        {
            MatchStats.Tick(this, sim);
            if (!Enabled)
            {
                return;
            }

            TickCount++;
            InjectPossessionEvents(nowMs);

            List<PlanRelayMessage> relays = new List<PlanRelayMessage>();

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter == null)
                {
                    continue;
                }

                adapter.BeforeMatchTick(nowMs);
                adapter.ScanRelayEvents(relays);
            }

            int bound = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null && adapter.Bound)
                {
                    bound++;
                }
            }

            if (bound > MaxBoundSlots)
            {
                MaxBoundSlots = bound;
            }

            Relay(relays, nowMs);
            SampleTotals();
        }

        private void InjectPossessionEvents(uint nowMs)
        {
            int attached = sim.Rok.IsCarried ? sim.Rok.AttachedSlot : -1;

            if (!possessionInitialised)
            {
                possessionInitialised = true;
                LastAttachedSlot = InjectInitialRokEvent ? int.MinValue : attached;
            }

            if (attached == LastAttachedSlot)
            {
                return;
            }

            int previous = LastAttachedSlot;
            LastAttachedSlot = attached;
            PossessionEvents++;

            if (previous >= 0 && Handles(previous))
            {
                LostItEvents++;
                Slots[previous].InjectSceneMessage(MatchPlanAi.MessageLostIt, 0, previous);
                Trace.Add("t=" + nowMs + " possession lost slot=" + previous + " -> inject LostIt");
            }

            if (attached >= 0 && Handles(attached))
            {
                GotItEvents++;
                Slots[attached].InjectSceneMessage(MatchPlanAi.MessageGotIt, 0, attached);
                Trace.Add("t=" + nowMs + " possession got slot=" + attached + " -> inject GotIt");
            }

            if (attached < 0)
            {
                RokIsFreeEvents++;

                for (int index = 0; index < PlanSlots.Count; index++)
                {
                    MatchPlanAi adapter = Slots[PlanSlots[index]];

                    if (adapter == null)
                    {
                        continue;
                    }

                    adapter.InjectSceneMessage(MatchPlanAi.MessageRokIsFree, 0,
                        previous >= 0 ? previous : MatchPlanAi.DefaultTeammateMode);
                }

                Trace.Add("t=" + nowMs + " rok free -> inject RokIsFree to " + PlanSlots.Count + " plan slots");
            }
        }

        private void Relay(List<PlanRelayMessage> relays, uint nowMs)
        {
            if (relays.Count == 0)
            {
                return;
            }

            int perTick = 0;

            for (int index = 0; index < relays.Count; index++)
            {
                if (perTick >= RelayPerTickBudget || RelayCount >= RelayTotalBudget)
                {
                    RelayBudgetHits++;
                    Trace.Add("t=" + nowMs + " relay-budget-hit after=" + perTick + " total=" + RelayCount);
                    break;
                }

                PlanRelayMessage message = relays[index];

                if (message.Message < 0)
                {
                    continue;
                }

                if (message.TargetSlot < 0)
                {
                    for (int slotIndex = 0; slotIndex < PlanSlots.Count; slotIndex++)
                    {
                        int target = PlanSlots[slotIndex];
                        MatchPlanAi adapter = Slots[target];

                        if (adapter == null || target == message.Sender)
                        {
                            continue;
                        }

                        adapter.InjectSceneMessage(message.Message, message.Arg, message.Sender);
                        RelayCount++;
                        perTick++;
                    }
                }
                else if (Handles(message.TargetSlot) && message.TargetSlot != message.Sender)
                {
                    Slots[message.TargetSlot].InjectSceneMessage(message.Message, message.Arg, message.Sender);
                    RelayCount++;
                    perTick++;
                }
            }

            digestDirty = true;
        }

        private void SampleTotals()
        {
            TotalPlanPassRequests = 0;
            TotalPlanShotRequests = 0;
            TotalUnsupportedDecisions = 0;
            TotalRuntimeErrors = 0;
            HeuristicFallbackTicks = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter == null)
                {
                    continue;
                }

                TotalPlanPassRequests += adapter.PlanPassRequests;
                TotalPlanShotRequests += adapter.PlanShotRequests;
                TotalUnsupportedDecisions += adapter.UnsupportedDecisionErrors;
                TotalRuntimeErrors += adapter.RuntimeErrors;
                HeuristicFallbackTicks += adapter.HeuristicFallbackTicks;
                AnimationBridgeEvents += adapter.AnimationBridgeEvents;
                PlanPhysicsTicks += adapter.PlanPhysicsTicks;
                BridgeFallbackTicks += adapter.BridgeFallbackTicks;
                PhysicsTargetChanges += adapter.PhysicsTargetChanges;
                adapter.RelayedMessages = RelayCount;
            }

            digestDirty = true;
        }

        public int BoundCount()
        {
            int bound = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null && adapter.Bound)
                {
                    bound++;
                }
            }

            return bound;
        }

        public int TotalRebinds()
        {
            int rebinds = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null)
                {
                    rebinds += adapter.RebindCount;
                }
            }

            return rebinds;
        }

        public int TotalPlanTicks()
        {
            int ticks = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null)
                {
                    ticks += adapter.PlanTicks;
                }
            }

            return ticks;
        }

        public string PlanIds()
        {
            List<string> ids = new List<string>();

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter == null)
                {
                    continue;
                }

                for (int id = 0; id < adapter.PlanIdsUsed.Count; id++)
                {
                    bool seen = false;

                    for (int at = 0; at < ids.Count; at++)
                    {
                        if (string.Equals(ids[at], adapter.PlanIdsUsed[id], StringComparison.OrdinalIgnoreCase))
                        {
                            seen = true;
                            break;
                        }
                    }

                    if (!seen)
                    {
                        ids.Add(adapter.PlanIdsUsed[id]);
                    }
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);

            return string.Join(",", ids.ToArray());
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder("UBLADE3_PLAN_AI_ROSTER slots=");
            text.Append(PlanSlots.Count);
            text.Append(" active=").Append(BoundCount());
            text.Append(" maxActive=").Append(MaxBoundSlots);
            text.Append(" planIds=[").Append(PlanIds()).Append(']');
            text.Append(" ticks=").Append(TickCount);
            text.Append(" planTicks=").Append(TotalPlanTicks());
            text.Append(" rebinds=").Append(TotalRebinds());
            text.Append(" relays=").Append(RelayCount);
            text.Append(" relayBudgetHits=").Append(RelayBudgetHits);
            text.Append(" possessionEvents=").Append(PossessionEvents);
            text.Append(" gotIt=").Append(GotItEvents);
            text.Append(" lostIt=").Append(LostItEvents);
            text.Append(" rokIsFree=").Append(RokIsFreeEvents);
            text.Append(" planPassRequests=").Append(TotalPlanPassRequests);
            text.Append(" planShotRequests=").Append(TotalPlanShotRequests);
            text.Append(" unsupportedDecisions=").Append(TotalUnsupportedDecisions);
            text.Append(" runtimeErrors=").Append(TotalRuntimeErrors);
            text.Append(" heuristicFallbackTicks=").Append(HeuristicFallbackTicks);
            text.Append(" animationBridgeEvents=").Append(AnimationBridgeEvents);
            text.Append(" planPhysicsTicks=").Append(PlanPhysicsTicks);
            text.Append(" bridgeFallbackTicks=").Append(BridgeFallbackTicks);
            text.Append(" physicsTargetChanges=").Append(PhysicsTargetChanges);
            text.Append(" initialRokEvent=").Append(InjectInitialRokEvent ? "True" : "False");
            text.Append(" maxRebindsPerSlot=").Append(MaxRebindsPerSlot());

            if (!string.IsNullOrEmpty(Failure))
            {
                text.Append(" failure=\"").Append(Failure).Append('"');
            }

            return text.ToString();
        }

        public int MaxRebindsPerSlot()
        {
            int max = 0;

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                MatchPlanAi adapter = Slots[PlanSlots[index]];

                if (adapter != null && adapter.RebindCount > max)
                {
                    max = adapter.RebindCount;
                }
            }

            return max;
        }

        public string DescribeSlots()
        {
            StringBuilder text = new StringBuilder();

            for (int index = 0; index < PlanSlots.Count; index++)
            {
                int slot = PlanSlots[index];
                MatchPlanAi adapter = Slots[slot];

                if (adapter == null)
                {
                    continue;
                }

                if (index > 0)
                {
                    text.Append('\n');
                }

                text.Append("UBLADE3_PLAN_AI_SLOT slot=").Append(slot);
                text.Append(" plan=").Append(adapter.PlanName);
                text.Append(" bound=").Append(adapter.Bound ? "True" : "False");
                text.Append(" planIds=[").Append(string.Join(",", adapter.PlanIdsUsed.ToArray())).Append(']');
                text.Append(" behaviors=[").Append(string.Join(">", adapter.BehaviorPath.ToArray())).Append(']');
                text.Append(" planTicks=").Append(adapter.PlanTicks);
                text.Append(" rebinds=").Append(adapter.RebindCount);
                text.Append(" planChangesAdopted=").Append(adapter.PlanChangeRequestsAdopted);
                text.Append(" actions=").Append(DescribeCounts(adapter.PlanActionCounts));
                text.Append(" decisions=").Append(DescribeCounts(adapter.PlanDecisionCounts));
                text.Append(" passRequests=").Append(adapter.PlanPassRequests);
                text.Append(" shotRequests=").Append(adapter.PlanShotRequests);
                text.Append(" targets=").Append(adapter.TargetChanges);
                text.Append(" fallbackMotion=").Append(adapter.FallbackMotionTicks);
                text.Append(" heuristicFallback=").Append(adapter.HeuristicFallbackTicks);
                text.Append(" planPhysicsTicks=").Append(adapter.PlanPhysicsTicks);
                text.Append(" bridgeFallbackTicks=").Append(adapter.BridgeFallbackTicks);
                text.Append(" unsupportedDecisions=").Append(adapter.UnsupportedDecisionErrors);
                text.Append(" unsupported=[").Append(DescribeSet(adapter.UnsupportedIds)).Append(']');
                text.Append(" digest=").Append(PlanVmDigest.Hex(adapter.TraceDigest()));
            }

            return text.ToString();
        }

        public ulong Digest()
        {
            if (digestDirty)
            {
                digest = PlanVmDigest.Offset;
                digest = PlanVmDigest.Append(digest, Describe() + "\n");
                digest = PlanVmDigest.Append(digest, DescribeSlots() + "\n");

                for (int index = 0; index < Trace.Count; index++)
                {
                    digest = PlanVmDigest.Append(digest, Trace[index] + "\n");
                }

                digestDirty = false;
            }

            return digest;
        }

        public string DigestHex()
        {
            return PlanVmDigest.Hex(Digest());
        }

        private static string FindPlan(string[] plans, string wanted)
        {
            for (int index = 0; index < plans.Length; index++)
            {
                if (string.Equals(plans[index], wanted, StringComparison.Ordinal))
                {
                    return plans[index];
                }
            }

            for (int index = 0; index < plans.Length; index++)
            {
                if (string.Equals(plans[index], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return plans[index];
                }
            }

            return null;
        }

        private static string DescribeCounts(Dictionary<int, int> counts)
        {
            List<int> keys = new List<int>(counts.Keys);
            keys.Sort();
            StringBuilder text = new StringBuilder();

            for (int index = 0; index < keys.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append(keys[index]).Append('x').Append(counts[keys[index]]);
            }

            return "[" + text + "]";
        }

        private static string DescribeSet(HashSet<string> values)
        {
            List<string> sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);

            return string.Join(",", sorted.ToArray());
        }
    }
}
