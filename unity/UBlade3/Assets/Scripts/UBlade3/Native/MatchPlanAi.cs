using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    /// <summary>
    /// The first real PlanVM-in-match adapter (N8). It binds a shipped CARRYROK plan state to one AI
    /// skater's live Simulation state, runs the plan's own behaviours, scripts, decisions and timers
    /// through the verified UBlade3.PlanVM core (the action/decision bodies ported in PlanAiActions /
    /// PlanAiDecisions), steers the skater toward the plan behaviour's target and turns the plan's own
    /// pass decision into the match's shipped PlanAction::Throw pass.
    ///
    /// It is deliberately bounded:
    ///   * the plan result (behaviour, target, action, decision, pass request) comes from the plan
    ///     bytes and the ported bodies - it is never synthesized here;
    ///   * the only fallback is a LABELLED motion bridge for behaviour kinds this slice does not
    ///     model (it fills movement toward the skater's goal, never a decision/action result);
    ///   * every unported plan id stays in the explicit unsupported telemetry of PlanAiHost.
    ///
    /// The behaviour subclasses' own steering physics (CSteerToTarget and friends) are not ported yet;
    /// the adapter converts the plan's target into the same held-mask steering the N4/N5 match already
    /// uses for the fallback AI, and docs/NATIVE-MATCH-N8.md labels that bridge.
    /// </summary>
    public sealed class MatchPlanAi
    {
        public const string DefaultPlanName = "CARRYROK";

        // CARRYROK behaviour indexes from the shipped plan (PLANS/CARRYROK.PBN, tools/plan_dump.py):
        // 5 TurnToMate, 7 PassingPlay, 9 PassingPlayPass.
        public const int InitialBehavior = 5;

        public const int BehaviorTurnToMate = 5;

        public const int BehaviorPassingPlay = 7;

        public const int BehaviorPassingPlayPass = 9;

        // id_MSG_Incoming: the shipped "receiver is open, pass to me" message the PassingPlay arms use.
        public const int MessageIncoming = 20;

        // Behavior_IDs values used by the motion mapping (planvm/behaviorids.h).
        public const int BehaviorKindGlideToObject = 0;

        public const int BehaviorKindGlide = 1;

        public const int BehaviorKindSetFlatInPlace = 16;

        public const int BehaviorKindDoNothing = 17;

        public const int BehaviorKindSetMovingInPlace = 18;

        // src/engine/messageids.h / DATA/ENUMIDS.H id_MSG_ list.
        public const int MessageLostIt = 2;

        public const int MessageGotIt = 3;

        public const int MessageRokIsFree = 13;

        // EngineObject::SelectAndActivatePlan maps a non-goalie AI skater to "Defense" behaviour 0 and
        // a goalie to plan table index 0 behaviour 0 (docs/NATIVE-MATCH-N9.md section 2).
        public const string InitialSkaterPlan = "Defense";

        public const int InitialSkaterBehavior = 0;

        public const int InitialGoalieBehavior = 0;

        // The carrier's engine request is CHANGE_OF_PLAN "CarryRok" 1 "GoForGoal" (the GotIt handler in
        // every skater plan). GoForGoal needs the unported attack/weapon decision bodies, so the roster
        // maps its entry to the verified TurnToMate behaviour 5 pass path (documented mapping, N9).
        public const int CarryRokRequestedBehavior = 1;

        public const int CarrierEntryBehavior = BehaviorTurnToMate;

        // g_nTeammateMode default: 0 (single player) per engine/gameengine.cpp ReadInt default.
        public const int DefaultTeammateMode = 0;

        // The shipped team rating table does not carry id_RAT_CarryRokThrowDist in the Sim slice; the
        // plan decision reads it, so the adapter supplies the team-file default as a labelled input.
        public const float FixtureCarryRokThrowDistance = 150.0f;

        // The Plan AI slice's fixed owner table; the N9 roster replaces it with the slot's shipped
        // ObjectDefinition.Plans from the team file (docs/NATIVE-MATCH-N9.md).
        private static readonly string[] DefaultPlanTable = { "CarryRok", "Defense", "Human", "OffWing", "Sideln" };

        private static readonly Dictionary<string, PlanFileImage> ImageCache =
            new Dictionary<string, PlanFileImage>(StringComparer.OrdinalIgnoreCase);

        private static readonly int[] ShotActionIds = { 3, 37, 38, 139 };

        public readonly int Slot;

        public string PlanName;

        public readonly string DataPath;

        public readonly bool Persistent;

        public readonly bool RosterMode;

        public readonly string[] PlanTableNames;

        public readonly int EntryBehavior;

        // N9 roster telemetry, all plan results are the plan's own.
        public int RebindCount;

        public int PlanChangeRequestsSeen;

        public int PlanChangeRequestsAdopted;

        public int UnsupportedDecisionErrors;

        public int RuntimeErrors;

        public int InjectedMessages;

        public int InjectedMessagesDropped;

        public int RelayedMessages;

        public int RelayBudgetHits;

        public int LastRequestedBehavior = -1;

        public string LastUnsupportedDecision = string.Empty;

        public readonly List<string> PlanHistory = new List<string>();

        public readonly List<string> PlanIdsUsed = new List<string>();

        public readonly HashSet<int> UnsupportedDecisionIds = new HashSet<int>();

        public readonly Dictionary<int, int> UnsupportedDecisionCounts = new Dictionary<int, int>();

        public bool Enabled = true;

        public bool Bound;

        public string Failure = string.Empty;

        public int BindCount;

        public int UnbindCount;

        public int PlanTicks;

        public int PlanPassRequests;

        public int PlanShotRequests;

        public int FallbackMotionTicks;

        public int PlanChangeRequests;

        public int BehaviorChanges;

        public int TargetChanges;

        public int LastTargetSlot = int.MinValue;

        public int LastBehaviorIndex = -1;

        public string LastBehaviorName = string.Empty;

        public int LastBehaviorKind = -1;

        public int LastBindTick = -1;

        public int LastPassTick = -1;

        public readonly List<string> BehaviorPath = new List<string>();

        public readonly List<string> TargetTrace = new List<string>();

        public readonly List<string> Trace = new List<string>();

        public readonly HashSet<string> UnsupportedIds = new HashSet<string>(StringComparer.Ordinal);

        public readonly Dictionary<int, int> PlanActionCounts = new Dictionary<int, int>();

        public readonly Dictionary<int, int> PlanOpcodeCounts = new Dictionary<int, int>();

        public int PlanNodeCount;

        public readonly Dictionary<int, int> PlanDecisionCounts = new Dictionary<int, int>();

        public readonly MatchPlayControl Control;

        private readonly Simulation sim;

        private readonly MatchPlayConfig config;

        private PlanFileImage image;

        private int processedPlanChangeRequests;

        private int relayScanned;

        private int lastRelayInjectionCount;

        private PlanAiWorld world;

        private PlanAiHost host;

        private PlanState state;

        private HeldBinding held;

        private int scannedEvents;

        private bool passRequested;

        private bool shotRequested;

        private ulong digest = PlanVmDigest.Offset;

        private bool digestDirty = true;

        /// <summary>The N8 single-plan, possession-bound adapter (CARRYROK TurnToMate flow).</summary>
        public MatchPlanAi(MatchPlayControl control, Simulation sim, MatchPlayConfig config, int slot, string planName, string dataPath)
            : this(control, sim, config, slot, planName, dataPath, DefaultPlanTable, InitialBehavior, false)
        {
        }

        /// <summary>
        /// The N9 roster adapter: a persistent plan instance for one slot over the slot's shipped plan
        /// table from the team file, with plan changes adopted from the plan's own CHANGE_OF_PLAN requests.
        /// </summary>
        public MatchPlanAi(MatchPlayControl control, Simulation sim, MatchPlayConfig config, int slot, string planName,
            string dataPath, string[] planTableNames, int initialBehavior, bool persistent)
        {
            Control = control;
            this.sim = sim;
            this.config = config;
            Slot = slot;
            DataPath = dataPath;
            PlanTableNames = planTableNames == null || planTableNames.Length == 0 ? DefaultPlanTable : planTableNames;
            EntryBehavior = initialBehavior;
            Persistent = persistent;
            RosterMode = persistent;
            PlanName = string.IsNullOrEmpty(planName) ? DefaultPlanName : planName;
            image = LoadPlanImage(DataPath, PlanName);
        }

        public static PlanFileImage LoadPlanImage(string dataPath, string planName)
        {
            if (string.IsNullOrEmpty(dataPath))
            {
                return null;
            }

            string key = dataPath + "|" + planName;
            PlanFileImage cached;

            if (ImageCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            try
            {
                cached = PlanFileLoader.LoadShipped(dataPath, planName);
                cached.Name = planName.ToUpperInvariant() + ".PBN";
            }
            catch (Exception exception)
            {
                return null;
            }

            ImageCache[key] = cached;

            return cached;
        }

        /// <summary>Called once per live control tick, before the match's own pending AI requests run.</summary>
        public void BeforeMatchTick(uint nowMs)
        {
            held = HeldBinding.None;

            if (!Enabled)
            {
                return;
            }

            if (Persistent)
            {
                PersistentMatchTick(nowMs);
                return;
            }

            bool carrying = sim.Rok.IsCarried && sim.Rok.AttachedSlot == Slot;

            if (!carrying)
            {
                if (Bound)
                {
                    Unbind(nowMs, "lost-rok");
                }

                return;
            }

            if (!Bound)
            {
                if (!Bind(nowMs))
                {
                    return;
                }
            }

            PlanTicks++;
            SyncMirror(nowMs);
            world.ClockMs = (int)nowMs;
            world.TickAgenda();
            world.DrainPendingMessages(256);
            ScanPlanEvents(nowMs);
            TrackBehavior(nowMs);
            held = ResolveMotion(nowMs);
        }

        /// <summary>
        /// N9 roster tick: the plan stays instantiated for the whole match and every tick runs the
        /// agenda, the pending plan messages and the plan's own behaviour machine. Unported decision
        /// bodies are explicit telemetry and abort only the current script step (the plan keeps its
        /// current behaviour; no branch is guessed). Plan changes the plan itself requests through
        /// CHANGE_OF_PLAN are adopted by rebinding this slot to the requested shipped plan/behaviour.
        /// </summary>
        private void PersistentMatchTick(uint nowMs)
        {
            if (!Bound)
            {
                if (!Bind(nowMs))
                {
                    return;
                }
            }

            PlanTicks++;
            SyncMirror(nowMs);
            world.ClockMs = (int)nowMs;

            try
            {
                world.TickAgenda();
            }
            catch (PlanAiUnportedHandlerException exception)
            {
                RecordUnsupportedDecision(exception);
            }
            catch (PlanVmException exception)
            {
                RuntimeErrors++;
                digestDirty = true;
                Trace.Add("t=" + nowMs + " plan-runtime-error " + exception.GetType().Name + ": " + exception.Message);
            }

            DrainPlanMessages(nowMs, 256);
            ScanPlanEvents(nowMs);
            TrackBehavior(nowMs);
            ProcessPlanChanges(nowMs);
            TrackBehavior(nowMs);
            held = ResolveMotion(nowMs);
        }

        /// <summary>Posts an engine-level plan message into this slot's plan instance (possession, free ROK).</summary>
        public void InjectSceneMessage(int message, int arg, int sender)
        {
            if (!Bound || world == null)
            {
                InjectedMessagesDropped++;

                return;
            }

            world.PostSceneMessage(Slot, message, arg, sender, 0);
            InjectedMessages++;
        }

        /// <summary>
        /// Collects this slot's cross-slot message traffic (explicit targets and broadcasts) since the
        /// last scan. The roster relays it into the target plan instances; the original network copy is
        /// already logged by the mock. Engine-level messages (both possession events) are injected by the
        /// roster directly, not relayed here.
        /// </summary>
        public void ScanRelayEvents(List<PlanRelayMessage> output)
        {
            if (!Bound || world == null)
            {
                return;
            }

            for (int index = relayScanned; index < world.Events.Count; index++)
            {
                string line = world.Events[index];

                if (line.Contains("broadcast message="))
                {
                    PlanRelayMessage message;
                    message.TargetSlot = -1;
                    message.Message = ParseIntToken(line, "message=");
                    message.Arg = ParseIntToken(line, "arg=");
                    message.Sender = ParseIntToken(line, "sender=");

                    if (message.Message >= 0)
                    {
                        output.Add(message);
                    }

                    continue;
                }

                if (line.Contains("post target="))
                {
                    int target = ParseIntToken(line, "post target=");

                    if (target >= 0 && target < PlanConstants.PlayerObjectCount && target != Slot)
                    {
                        PlanRelayMessage message;
                        message.TargetSlot = target;
                        message.Message = ParseIntToken(line, "message=");
                        message.Arg = ParseIntToken(line, "arg=");
                        message.Sender = ParseIntToken(line, "sender=");

                        if (message.Message >= 0)
                        {
                            output.Add(message);
                        }
                    }
                }
            }

            relayScanned = world.Events.Count;
        }

        /// <summary>Rebinds this slot to another shipped plan at the entry behaviour the plan requested.</summary>
        private bool RebindPlan(uint nowMs, string planName, int requestedBehavior, out int mappedBehavior, string why)
        {
            mappedBehavior = requestedBehavior;
            PlanFileImage target = LoadPlanImage(DataPath, planName);

            if (target == null)
            {
                Trace.Add("t=" + nowMs + " plan-change-missed plan=" + planName + " why=" + why);
                digestDirty = true;

                return false;
            }

            mappedBehavior = MapEntryBehavior(planName, requestedBehavior);
            PlanState fresh;

            try
            {
                if (state != null)
                {
                    state.Teardown();
                }

                PlanName = planName;
                image = target;
                fresh = host.InstantiatePlan(target);
                int behaviors = fresh.BehaviorNodeCount;

                if (behaviors <= 0)
                {
                    mappedBehavior = 0;
                }
                else if (mappedBehavior < 0 || mappedBehavior >= behaviors - 1)
                {
                    mappedBehavior = 0;
                }

                fresh.ActivateBehavior(unchecked((uint)mappedBehavior));
                state = fresh;
            }
            catch (Exception exception)
            {
                RuntimeErrors++;
                Trace.Add("t=" + nowMs + " plan-change-failed plan=" + planName + " behaviour=" + requestedBehavior
                          + " why=" + why + " error=" + exception.GetType().Name + ": " + exception.Message);
                digestDirty = true;

                return false;
            }

            RebindCount++;
            PlanChangeRequestsAdopted++;
            LastRequestedBehavior = requestedBehavior;
            LastBehaviorIndex = -1;
            LastBehaviorName = string.Empty;
            LastBehaviorKind = -1;
            LastTargetSlot = int.MinValue;
            passRequested = false;
            shotRequested = false;
            processedPlanChangeRequests = host.PlanChangeRequests.Count;
            PlanHistory.Add("t=" + nowMs + " why=" + why + " requested=" + planName + ":" + requestedBehavior
                            + " mapped=" + mappedBehavior
                            + " behaviours=" + (state == null ? 0 : state.BehaviorNodeCount));
            AddPlanId(planName);
            RefreshPlanStatistics();
            digestDirty = true;

            return true;
        }

        private static int MapEntryBehavior(string planName, int requestedBehavior)
        {
            if (string.Equals(planName, DefaultPlanName, StringComparison.OrdinalIgnoreCase) &&
                requestedBehavior == CarryRokRequestedBehavior)
            {
                return CarrierEntryBehavior;
            }

            return requestedBehavior;
        }

        private void ProcessPlanChanges(uint nowMs)
        {
            if (host == null || host.PlanChangeRequests.Count == processedPlanChangeRequests)
            {
                return;
            }

            PlanChangeRequestsSeen += host.PlanChangeRequests.Count - processedPlanChangeRequests;
            string request = host.PlanChangeRequests[host.PlanChangeRequests.Count - 1];
            processedPlanChangeRequests = host.PlanChangeRequests.Count;

            string planName;
            int behavior;

            if (!TryParsePlanChangeRequest(request, out planName, out behavior))
            {
                Trace.Add("t=" + nowMs + " plan-change-unparsed " + request);
                digestDirty = true;

                return;
            }

            int mapped;
            bool adopted = RebindPlan(nowMs, planName, behavior, out mapped, "plan-change");

            Trace.Add("t=" + nowMs + " plan-change-adopted=" + (adopted ? "True" : "False")
                      + " request=" + request + " mappedBehavior=" + mapped);
            digestDirty = true;
        }

        private static bool TryParsePlanChangeRequest(string request, out string planName, out int behavior)
        {
            planName = null;
            behavior = 0;

            int at = request.IndexOf("plan=", StringComparison.Ordinal);

            if (at < 0)
            {
                return false;
            }

            int indexAt = at + 5;
            int open = request.IndexOf('(', indexAt);
            int close = open < 0 ? -1 : request.IndexOf(')', open);

            if (open < 0 || close < 0)
            {
                return false;
            }

            planName = request.Substring(open + 1, close - open - 1);

            int behaviorAt = request.IndexOf("behavior=", close, StringComparison.Ordinal);

            if (behaviorAt < 0)
            {
                return false;
            }

            int start = behaviorAt + "behavior=".Length;
            int end = start;

            while (end < request.Length && (char.IsDigit(request[end]) || request[end] == '-'))
            {
                end++;
            }

            return int.TryParse(request.Substring(start, end - start), out behavior);
        }

        /// <summary>
        /// Drains the plan's pending scene messages one at a time. An unported handler body aborts only
        /// that message and is recorded in the explicit unsupported telemetry; the rest of the queue
        /// still runs, so a missing decision cannot stall the whole slot forever (the stock
        /// DrainPendingMessages keeps a failed message at the head and would replay it every tick).
        /// </summary>
        private void DrainPlanMessages(uint nowMs, int budget)
        {
            int dispatched = 0;

            while (world.Pending.Count > 0 && dispatched < budget)
            {
                PlanAiPendingMessage message = world.Pending[0];
                world.Pending.RemoveAt(0);
                dispatched++;

                try
                {
                    world.DispatchSceneMessage(message.TargetSlot, message.Message, message.Arg,
                        DecisionLog.Fresh, message.Sender, message.FromInput);
                }
                catch (PlanAiUnportedHandlerException exception)
                {
                    RecordUnsupportedDecision(exception);
                }
                catch (PlanVmException exception)
                {
                    RuntimeErrors++;
                    digestDirty = true;
                    Trace.Add("t=" + nowMs + " plan-runtime-error " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            if (world.Pending.Count > 0)
            {
                int dropped = world.Pending.Count;
                world.Pending.Clear();
                digestDirty = true;
                Trace.Add("t=" + nowMs + " pending-overflow dropped=" + dropped + " budget=" + budget);
            }
        }

        private void RecordUnsupportedDecision(PlanAiUnportedHandlerException exception)
        {
            UnsupportedDecisionErrors++;
            UnsupportedDecisionIds.Add(exception.HandlerId);
            LastUnsupportedDecision = "decision:" + exception.HandlerId + " " + PlanOpcodes.DecisionName(exception.HandlerId);

            int count;
            UnsupportedDecisionCounts.TryGetValue(exception.HandlerId, out count);
            UnsupportedDecisionCounts[exception.HandlerId] = count + 1;
            UnsupportedIds.Add("decision:" + exception.HandlerId);
            UnsupportedIds.Add("unsupported.decision id=" + exception.HandlerId);
            Trace.Add("t=" + (world == null ? 0 : world.ClockMs) + " unsupported-decision id=" + exception.HandlerId
                      + " " + PlanOpcodes.DecisionName(exception.HandlerId)
                      + " behavior=" + (state == null || state.Current == null ? "?" : state.Current.Name)
                      + " (no branch guessed; current behaviour kept)");
            digestDirty = true;
        }

        private static int ParseIntToken(string line, string token)
        {
            int at = line.IndexOf(token, StringComparison.Ordinal);

            if (at < 0)
            {
                return -1;
            }

            int start = at + token.Length;
            int end = start;

            while (end < line.Length && char.IsDigit(line[end]))
            {
                end++;
            }

            int value;

            return int.TryParse(line.Substring(start, end - start), out value) ? value : -1;
        }

        private bool IsShotAction(int actionId)
        {
            for (int index = 0; index < ShotActionIds.Length; index++)
            {
                if (ShotActionIds[index] == actionId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The held mask the match simulation uses for this slot. When no plan is bound the heuristic keeps the movement (pickup approach).</summary>
        public HeldBinding GetInput(Simulation liveSim, int slot)
        {
            if (!Enabled || slot != Slot)
            {
                return HeldBinding.None;
            }

            if (!Bound)
            {
                return Control != null && Control.Ai != null ? Control.Ai.GetInput(liveSim, slot) : HeldBinding.None;
            }

            return held;
        }

        private bool Bind(uint nowMs)
        {
            if (image == null)
            {
                Failure = "plan " + PlanName + " could not be loaded from " + DataPath;
                Trace.Add("t=" + nowMs + " bind-failed " + Failure);
                Enabled = false;

                return false;
            }

            try
            {
                PlanAiWorld mirror = new PlanAiWorld();
                mirror.TeammateMode = DefaultTeammateMode;
                mirror.Difficulty = PlanAiDifficulty.Medium;

                for (int index = 0; index < PlanTableNames.Length; index++)
                {
                    mirror.PlanTable.Add(PlanTableNames[index]);
                }

                for (int slot = 0; slot < PlanConstants.PlayerObjectCount; slot++)
                {
                    PlanAiObject player = mirror.AddObject(slot, "Slot" + slot, PlanNameForSlot(slot));
                    CopyPlayer(player, sim.Players[slot]);
                }

                PlanAiObject rok = mirror.AddObject(PlanConstants.SlotRok, "Rok", null);
                CopyRok(rok);

                AddGoal(mirror, PlanConstants.SlotNgoal, config.NorthGoalPosition);
                AddGoal(mirror, PlanConstants.SlotSgoal, config.SouthGoalPosition);

                world = mirror;
                host = new PlanAiHost(mirror, (short)Slot);

                PlanProgram program = PlanStreamParser.Parse(image, PlanArgResolver.ForSlot(Slot), host);
                PlanStatistics statistics = PlanAnalyzer.Analyze(image, program);
                PlanNodeCount = statistics.Nodes;
                PlanOpcodeCounts.Clear();

                foreach (KeyValuePair<int, int> entry in statistics.OpcodeHistogram)
                {
                    PlanOpcodeCounts[entry.Key] = entry.Value;
                }

                state = host.InstantiatePlan(image);
                state.ActivateBehavior(unchecked((uint)EntryBehavior));

                scannedEvents = world.Events.Count;
                relayScanned = world.Events.Count;
                Bound = true;
                AddPlanId(PlanName);
                BindCount++;
                LastBindTick = (int)nowMs;
                LastTargetSlot = int.MinValue;
                LastBehaviorIndex = -1;
                LastBehaviorName = string.Empty;
                BehaviorPath.Clear();
                TargetTrace.Clear();
                passRequested = false;
                shotRequested = false;
                PlanTicks = 0;
                Trace.Add("t=" + nowMs + " bind plan=" + PlanName + " slot=" + Slot
                          + " behaviours=" + (state.Behaviors == null ? 0 : state.Behaviors.Length)
                          + " slotPlans=" + PlanTableNames.Length + " persistent=" + (Persistent ? "True" : "False")
                          + " planDigest=" + PlanVmDigest.Hex(statistics.Digest));
                digestDirty = true;

                return true;
            }
            catch (Exception exception)
            {
                Failure = "bind failed: " + exception.GetType().Name + ": " + exception.Message;
                Trace.Add("t=" + nowMs + " bind-failed " + Failure);
                world = null;
                host = null;
                state = null;
                Bound = false;
                Enabled = false;

                return false;
            }
        }

        private void AddPlanId(string planName)
        {
            for (int index = 0; index < PlanIdsUsed.Count; index++)
            {
                if (string.Equals(PlanIdsUsed[index], planName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            PlanIdsUsed.Add(planName);
        }

        private void RefreshPlanStatistics()
        {
            if (image == null || host == null)
            {
                return;
            }

            PlanProgram program = PlanStreamParser.Parse(image, PlanArgResolver.ForSlot(Slot), host);
            PlanStatistics statistics = PlanAnalyzer.Analyze(image, program);
            PlanNodeCount = statistics.Nodes;
            PlanOpcodeCounts.Clear();

            foreach (KeyValuePair<int, int> entry in statistics.OpcodeHistogram)
            {
                PlanOpcodeCounts[entry.Key] = entry.Value;
            }
        }

        private void Unbind(uint nowMs, string reason)
        {
            if (state != null)
            {
                try
                {
                    state.Teardown();
                }
                catch (Exception)
                {
                    // Teardown is best-effort at the endpoint; the plan instance is discarded.
                }
            }

            if (Bound)
            {
                Trace.Add("t=" + nowMs + " unbind reason=" + reason + " behaviors=[" + string.Join(">", BehaviorPath.ToArray()) + "]"
                          + " targets=" + TargetTrace.Count + " planActions=" + DescribeCounts(PlanActionCounts)
                          + " planDecisions=" + DescribeCounts(PlanDecisionCounts)
                          + " passRequests=" + PlanPassRequests);
            }

            UnbindCount++;
            Bound = false;
            state = null;
            host = null;
            world = null;
            held = HeldBinding.None;
            digestDirty = true;
        }

        private void SyncMirror(uint nowMs)
        {
            for (int slot = 0; slot < PlanConstants.PlayerObjectCount; slot++)
            {
                CopyPlayer(world.GetObject(slot), sim.Players[slot]);
            }

            CopyRok(world.Rok);
        }

        private static PlanAiVec3 ToPlan(SimVec3 value)
        {
            return new PlanAiVec3(value.x, value.y, value.z);
        }

        private static void CopyPlayer(PlanAiObject target, PlayerState source)
        {
            if (target == null)
            {
                return;
            }

            target.Body.Position = ToPlan(source.Body.Pos);
            target.Body.Velocity = ToPlan(source.Body.Vel);
            target.Body.VelocityDirection = ToPlan(source.Body.VelDir);
            target.ClockStoppedSemaphore = source.ClockStoppedSemaphore;
            target.FlyingSemaphore = source.FlyingSemaphore;
            target.KnockedDownState = source.KnockedDownState;
            target.DesiredSubstitute = source.DesiredSubstitute;
            target.Stats.Set(PlanAiConstants.RatingDamage, source.Stats.GetScalar(ObjectConstants.RatingDamage));
            target.Stats.Set(PlanAiConstants.RatingRokHandling, source.Stats.GetScalar(ObjectConstants.RatingRokHandling));
            target.Stats.Set(PlanAiConstants.RatingLongPassEnable, 0.0f);
            target.Stats.Set(PlanAiConstants.RatingCarryRokThrowDist, FixtureCarryRokThrowDistance);
        }

        private void CopyRok(PlanAiObject target)
        {
            if (target == null)
            {
                return;
            }

            target.Body.Position = ToPlan(sim.Rok.Body.Pos);
            target.Body.Velocity = ToPlan(sim.Rok.Body.Vel);
            target.Body.VelocityDirection = ToPlan(sim.Rok.Body.VelDir);
            target.Body.AttachTargetSlot = sim.Rok.IsCarried ? sim.Rok.AttachedSlot : -1;
        }

        private void AddGoal(PlanAiWorld mirror, int slot, SimVec3 position)
        {
            PlanAiObject goal = mirror.AddObject(slot, "Goal" + slot, null);
            goal.Body.Position = ToPlan(position);
        }

        private static string PlanNameForSlot(int slot)
        {
            if (slot == 0)
            {
                return "Human";
            }

            if (slot == ObjectConstants.GoalieSlotA || slot == ObjectConstants.GoalieSlotB)
            {
                return "Goalie";
            }

            return "Defense";
        }

        private void ScanPlanEvents(uint nowMs)
        {
            for (int index = scannedEvents; index < world.Events.Count; index++)
            {
                string line = world.Events[index];
                Trace.Add(line);

                if (line.Contains("action id=5 ") || line.Contains("action id=93 "))
                {
                    if (line.Contains("message=" + MessageIncoming + "("))
                    {
                        RequestPass(nowMs, line);
                    }
                }

                for (int i = 0; i < ShotActionIds.Length; i++)
                {
                    if (line.Contains("action id=" + ShotActionIds[i] + " "))
                    {
                        RequestShot(nowMs, line);
                    }
                }

                // The unported launch/throw action bodies still name the carrier's shot intent; the
                // carrier-only condition keeps a goalie LaunchAt (a launch, not a shot) out of the mapping.
                if (line.Contains("unsupported.action id="))
                {
                    int actionId = ParseIntToken(line, "unsupported.action id=");

                    if (IsShotAction(actionId) && sim.Rok.IsCarried && sim.Rok.AttachedSlot == Slot)
                    {
                        RequestShot(nowMs, line);
                    }
                }

                if (line.Contains("change-plan-request"))
                {
                    PlanChangeRequests++;
                }
            }

            scannedEvents = world.Events.Count;
            digestDirty = true;
        }

        private void RequestPass(uint nowMs, string evidence)
        {
            if (passRequested)
            {
                return;
            }

            passRequested = true;
            PlanPassRequests++;
            LastPassTick = (int)nowMs;
            Trace.Add("t=" + nowMs + " plan-pass-request slot=" + Slot + " evidence=\"" + evidence + "\"");

            if (Control != null)
            {
                Control.RequestAiPass(Slot);
            }
        }

        private void RequestShot(uint nowMs, string evidence)
        {
            if (shotRequested)
            {
                return;
            }

            shotRequested = true;
            PlanShotRequests++;
            Trace.Add("t=" + nowMs + " plan-shot-request slot=" + Slot + " evidence=\"" + evidence + "\"");

            if (Control != null)
            {
                Control.RequestAiShot(Slot);
            }
        }

        private void TrackBehavior(uint nowMs)
        {
            PlanBehavior current = state == null ? null : state.Current;

            if (current == null)
            {
                return;
            }

            if (LastBehaviorIndex != current.SyncValue || !string.Equals(LastBehaviorName, current.Name, StringComparison.Ordinal))
            {
                LastBehaviorIndex = current.SyncValue;
                LastBehaviorName = current.Name;
                LastBehaviorKind = current.Kind;
                BehaviorChanges++;
                BehaviorPath.Add(current.Name);
                Trace.Add("t=" + nowMs + " plan-behavior index=" + current.SyncValue + " name=" + current.Name
                          + " kind=" + current.Kind + " target=" + ResolveTargetSlot(current));
                digestDirty = true;
            }

            int target = ResolveTargetSlot(current);

            if (target != LastTargetSlot)
            {
                LastTargetSlot = target;
                TargetChanges++;
                string targetText = DescribeTarget(target);
                TargetTrace.Add(targetText);
                Trace.Add("t=" + nowMs + " plan-target behavior=" + current.Name + " slot=" + target + " " + targetText);
                digestDirty = true;
            }

            SynchronizeCounts();
        }

        private void SynchronizeCounts()
        {
            if (host == null)
            {
                return;
            }

            PlanActionCounts.Clear();

            foreach (KeyValuePair<int, int> entry in host.ActionCalls)
            {
                PlanActionCounts[entry.Key] = entry.Value;
            }

            PlanDecisionCounts.Clear();

            foreach (KeyValuePair<int, int> entry in host.DecisionCalls)
            {
                PlanDecisionCounts[entry.Key] = entry.Value;
            }

            for (int index = 0; index < host.UnsupportedHandlers.Count; index++)
            {
                UnsupportedIds.Add(host.UnsupportedHandlers[index]);
            }
        }

        /// <summary>
        /// Reads a behaviour's shipped SET_BEHAVIOR_PARAM target (id 3, the Target parameter) straight
        /// from the compiled plan block. The original BehParam::SetTarget handler body is still an
        /// unported behaviour-subclass concern (plan behaviour agenda slots), so it remains visible as
        /// unsupported telemetry; the motion bridge below applies the same target operand.
        /// </summary>
        private int ResolveTargetSlot(PlanBehavior behavior)
        {
            if (state == null || state.Behaviors == null || behavior == null)
            {
                return -1;
            }

            int declared = -1;

            for (int index = 0; index < state.Behaviors.Length; index++)
            {
                PlanNode node = state.Behaviors[index];

                if (node == null || node.Opcode != PlanOpcodes.OpcodeDeclareBehavior)
                {
                    continue;
                }

                declared++;

                if (declared != behavior.SyncValue)
                {
                    continue;
                }

                for (int child = 0; child < node.ChildCount; child++)
                {
                    PlanOperand operand = node.Args[child];

                    if (operand == null || operand.Kind != PlanOperandKind.Node)
                    {
                        continue;
                    }

                    PlanNode item = operand.Node;

                    if (item.Opcode != PlanOpcodes.OpcodeSetBehaviorParam || item.ChildCount < 2)
                    {
                        continue;
                    }

                    if (item.Args[0].Kind != PlanOperandKind.Leaf || item.Args[0].IntValue != 3)
                    {
                        continue;
                    }

                    if (item.Args[1].Kind != PlanOperandKind.Leaf)
                    {
                        return -1;
                    }

                    if (item.Args[1].Word.IsPronoun)
                    {
                        return -1;
                    }

                    return item.Args[1].IntValue;
                }

                return -1;
            }

            return -1;
        }

        private string DescribeTarget(int target)
        {
            if (target >= 0 && target < PlanConstants.PlayerObjectCount)
            {
                return "player";
            }

            if (target == PlanConstants.SlotNgoal || target == PlanConstants.SlotSgoal)
            {
                return "goal";
            }

            if (target == PlanConstants.SlotRok)
            {
                return "rok";
            }

            return "none";
        }

        private HeldBinding ResolveMotion(uint nowMs)
        {
            PlanBehavior current = state == null ? null : state.Current;

            if (current == null)
            {
                return HeldBinding.None;
            }

            PlayerState actor = sim.Players[Slot];
            int target = ResolveTargetSlot(current);
            bool fallback = false;

            switch (current.Kind)
            {
                case BehaviorKindGlideToObject:
                case BehaviorKindGlide:
                    break;

                case BehaviorKindSetFlatInPlace:
                case BehaviorKindSetMovingInPlace:
                case BehaviorKindDoNothing:
                    return HeldBinding.None;

                default:
                    fallback = true;
                    target = -1;
                    break;
            }

            SimVec3 position;

            if (!TargetPosition(actor, target, current.Kind, out position))
            {
                fallback = true;
                position = MatchAiController.GoalPosition(sim, actor.TargetGoalSlot);
            }

            if (fallback)
            {
                FallbackMotionTicks++;
                Trace.Add("t=" + nowMs + " motion-fallback behavior=" + current.Name + " kind=" + current.Kind
                          + " note=labelled-input-mask-bridge");
            }

            return SteerToward(actor, position);
        }

        private bool TargetPosition(PlayerState actor, int target, int behaviorKind, out SimVec3 position)
        {
            position = SimVec3.Zero;

            if (target >= 0 && target < PlanConstants.PlayerObjectCount)
            {
                position = sim.Players[target].Body.Pos;

                return true;
            }

            if (target == PlanConstants.SlotNgoal || target == PlanConstants.SlotSgoal)
            {
                position = MatchAiController.GoalPosition(sim, target);

                return true;
            }

            if (target == PlanConstants.SlotRok && !sim.Rok.IsCarried)
            {
                position = sim.Rok.Body.Pos;

                return true;
            }

            if (behaviorKind == BehaviorKindGlideToObject || behaviorKind == BehaviorKindGlide)
            {
                position = MatchAiController.GoalPosition(sim, actor.TargetGoalSlot);

                return true;
            }

            return false;
        }

        // The N4/N5 held-mask steering bridge (MatchAiController.SteerToward): the exact
        // CSteerToTarget physics of the original behaviour subclasses is not ported yet, so the plan
        // target is expressed with the same input mask the native match already drives. This is a
        // labelled motion bridge, not a plan result.
        private static HeldBinding SteerToward(PlayerState actor, SimVec3 target)
        {
            float dx = target.x - actor.Body.Pos.x;
            float dy = target.y - actor.Body.Pos.y;
            float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < 1e-6f)
            {
                return HeldBinding.None;
            }

            float desiredX = dx / distance;
            float desiredY = dy / distance;
            float vx = actor.Body.VelDir.x;
            float vy = actor.Body.VelDir.y;
            float vlength = (float)Math.Sqrt((vx * vx) + (vy * vy));

            if (vlength < 1e-6f)
            {
                return HeldBinding.Forward;
            }

            vx /= vlength;
            vy /= vlength;

            float cross = (vx * desiredY) - (vy * desiredX);
            float dot = (vx * desiredX) + (vy * desiredY);
            HeldBinding input = distance > 2.0f ? HeldBinding.Forward : HeldBinding.None;

            if (cross > 0.02f)
            {
                input |= HeldBinding.RotateLeft;
            }
            else if (cross < -0.02f)
            {
                input |= HeldBinding.RotateRight;
            }
            else if (Math.Abs(cross) <= 0.02f && dot < 0.0f)
            {
                // Target directly behind: turn rather than drive straight past it.
                input |= HeldBinding.RotateLeft;
            }
            else if (dot < 0.98f && input == HeldBinding.None)
            {
                input = HeldBinding.Forward;
            }

            return input;
        }

        public ulong TraceDigest()
        {
            if (digestDirty)
            {
                digest = PlanVmDigest.Offset;

                for (int index = 0; index < Trace.Count; index++)
                {
                    digest = PlanVmDigest.Append(digest, Trace[index] + "\n");
                }

                digestDirty = false;
            }

            return digest;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_PLAN_AI_MATCH slot=").Append(Slot);
            text.Append(" plan=").Append(PlanName);
            text.Append(" bound=").Append(Bound ? "True" : "False");
            text.Append(" binds=").Append(BindCount);
            text.Append(" unbinds=").Append(UnbindCount);
            text.Append(" planTicks=").Append(PlanTicks);
            text.Append(" behaviors=[").Append(string.Join(">", BehaviorPath.ToArray())).Append(']');
            text.Append(" behaviorChanges=").Append(BehaviorChanges);
            text.Append(" targetChanges=").Append(TargetChanges);
            text.Append(" targets=[").Append(string.Join(",", TargetTrace.ToArray())).Append(']');
            text.Append(" planActions=").Append(DescribeCounts(PlanActionCounts));
            text.Append(" planDecisions=").Append(DescribeCounts(PlanDecisionCounts));
            text.Append(" passRequests=").Append(PlanPassRequests);
            text.Append(" shotRequests=").Append(PlanShotRequests);
            text.Append(" fallbackMotionTicks=").Append(FallbackMotionTicks);
            text.Append(" planChangeRequests=").Append(PlanChangeRequests);
            text.Append(" unsupported=[").Append(DescribeSet(UnsupportedIds)).Append(']');
            text.Append(" traceLines=").Append(Trace.Count);
            text.Append(" digest=").Append(PlanVmDigest.Hex(TraceDigest()));

            if (Persistent)
            {
                text.Append(" persistent=True");
                text.Append(" planIds=[").Append(string.Join(",", PlanIdsUsed.ToArray())).Append(']');
                text.Append(" rebinds=").Append(RebindCount);
                text.Append(" planChangesSeen=").Append(PlanChangeRequestsSeen);
                text.Append(" planChangesAdopted=").Append(PlanChangeRequestsAdopted);
                text.Append(" requestedBehavior=").Append(LastRequestedBehavior);
                text.Append(" unsupportedDecisions=").Append(UnsupportedDecisionErrors);
                text.Append(" unsupportedDecisionIds=[");
                List<int> decisionIds = new List<int>(UnsupportedDecisionIds);
                decisionIds.Sort();

                for (int index = 0; index < decisionIds.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append(',');
                    }

                    text.Append(decisionIds[index]).Append('x')
                        .Append(UnsupportedDecisionCounts[decisionIds[index]]);
                }

                text.Append(']');
                text.Append(" runtimeErrors=").Append(RuntimeErrors);
                text.Append(" injected=").Append(InjectedMessages);
                text.Append(" injectedDropped=").Append(InjectedMessagesDropped);
                text.Append(" relayed=").Append(RelayedMessages);
                text.Append(" relayBudgetHits=").Append(RelayBudgetHits);
            }

            if (!string.IsNullOrEmpty(Failure))
            {
                text.Append(" failure=\"").Append(Failure).Append('"');
            }

            return text.ToString();
        }

        /// <summary>The compiled plan's instruction/opcode counts, the static half of the plan-driven evidence.</summary>
        public string OpcodeLine()
        {
            List<int> keys = new List<int>(PlanOpcodeCounts.Keys);
            keys.Sort();
            StringBuilder text = new StringBuilder("UBLADE3_PLAN_AI_MATCH_OPCODES plan=");
            text.Append(PlanName).Append(" nodes=").Append(PlanNodeCount).Append(" opcodes=");

            for (int index = 0; index < keys.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append(PlanOpcodes.KeywordOf(keys[index])).Append('=').Append(PlanOpcodeCounts[keys[index]]);
            }

            return text.ToString();
        }

        public string ImplLine()
        {
            StringBuilder text = new StringBuilder("UBLADE3_PLAN_AI_MATCH_IMPL actions=");

            int[] actions = PlanAiActions.ImplementedIds();

            for (int index = 0; index < actions.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append(actions[index]);
            }

            text.Append(" decisions=");

            int[] decisions = PlanAiDecisions.ImplementedIds();

            for (int index = 0; index < decisions.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append(decisions[index]);
            }

            text.Append(" motion=glideToObject(0),glide(1),inPlace(16..18) fallback=labelled-input-mask-bridge");

            return text.ToString();
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

    /// <summary>One cross-slot plan message the roster relays into another slot's plan instance.</summary>
    public struct PlanRelayMessage
    {
        public int TargetSlot; // -1 is a broadcast
        public int Message;
        public int Arg;
        public int Sender;
    }

    /// <summary>The plan slot constants the adapter mirrors, kept local so the adapter reads like the engine tables.</summary>
    internal static class PlanConstants
    {
        public const int PlayerObjectCount = 6;

        public const int SlotRok = 6;

        public const int SlotNgoal = 7;

        public const int SlotSgoal = 8;
    }
}
