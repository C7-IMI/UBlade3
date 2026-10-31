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

        // g_nTeammateMode default: 0 (single player) per engine/gameengine.cpp ReadInt default.
        public const int DefaultTeammateMode = 0;

        // The shipped team rating table does not carry id_RAT_CarryRokThrowDist in the Sim slice; the
        // plan decision reads it, so the adapter supplies the team-file default as a labelled input.
        public const float FixtureCarryRokThrowDistance = 150.0f;

        private static readonly string[] PlanTableNames = { "CarryRok", "Defense", "Human", "OffWing", "Sideln" };

        private static readonly Dictionary<string, PlanFileImage> ImageCache =
            new Dictionary<string, PlanFileImage>(StringComparer.OrdinalIgnoreCase);

        private static readonly int[] ShotActionIds = { 3, 37, 38, 139 };

        public readonly int Slot;

        public readonly string PlanName;

        public readonly string DataPath;

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

        private readonly PlanFileImage image;

        private PlanAiWorld world;

        private PlanAiHost host;

        private PlanState state;

        private HeldBinding held;

        private int scannedEvents;

        private bool passRequested;

        private bool shotRequested;

        private ulong digest = PlanVmDigest.Offset;

        private bool digestDirty = true;

        public MatchPlanAi(MatchPlayControl control, Simulation sim, MatchPlayConfig config, int slot, string planName, string dataPath)
        {
            Control = control;
            this.sim = sim;
            this.config = config;
            Slot = slot;
            PlanName = string.IsNullOrEmpty(planName) ? DefaultPlanName : planName;
            DataPath = dataPath;
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
                state.ActivateBehavior(InitialBehavior);

                scannedEvents = world.Events.Count;
                Bound = true;
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

    /// <summary>The plan slot constants the adapter mirrors, kept local so the adapter reads like the engine tables.</summary>
    internal static class PlanConstants
    {
        public const int PlayerObjectCount = 6;

        public const int SlotRok = 6;

        public const int SlotNgoal = 7;

        public const int SlotSgoal = 8;
    }
}
