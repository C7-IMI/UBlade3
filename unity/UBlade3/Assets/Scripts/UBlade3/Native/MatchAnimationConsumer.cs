using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UBlade3.PlanVM;
using UBlade3.Sim;
using UnityEngine;

namespace UBlade3.Native
{
    // N15: the plan-animation consumer. It reads the plan VM's latest SetAnimation action / Animation
    // and PersistentAnimation behaviour state, resolves the ids through AnimAssm, plays the clips on
    // the real MatchSkaterViews, and reports every clip change, animation trigger and unsupported id.
    //
    // Default mode is the interactive player only. The deterministic N4-N14 capture and display and
    // N13 preview paths keep their own clip clock, so their artifacts are unchanged. The N15 trace
    // and animation capture enable the consumer explicitly (ForceEnabled / SetEnabled), and a normal
    // interactive player or a --n15-probe run enables it by default.
    public sealed class MatchAnimationConsumer
    {
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_CONSUMER";

        public static bool ForceEnabled;

        public static bool ExplicitlyEnabled;

        private static readonly string[] CaptureArguments =
        {
            "--play-capture=", "--n7-capture=", "--hud-capture=", "--display-probe=",
            "--n13-probe=", "--gameplay-capture=", "--match-capture=", "--n13-capture=",
        };

        public readonly MatchAnimationRegistry Registry;

        public readonly MatchAnimationPlayer[] Players;

        public readonly List<string> Trace = new List<string>();

        public readonly List<string> Unsupported = new List<string>();

        public readonly List<string> EventLines = new List<string>();

        public MatchPlanAiRoster Roster;

        public int Ticks;

        public int PlanSlots;

        public int PlanAnimationRequests;

        public int PlanAnimationApplied;

        public int BehaviorChanges;

        public int PersistentChanges;

        public int DeferredQueued;

        public int DeferredApplied;

        public int EventCount;

        public readonly HashSet<int> ResolvedPlanIds = new HashSet<int>();

        public readonly HashSet<string> ResolvedPlanNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int ClipChanges;

        public int UnsupportedCount;

        public int TriggerEvents;

        public int FixtureRequests;

        public int FixtureApplied;

        private readonly Dictionary<int, int> lastActionEvents = new Dictionary<int, int>();
        private readonly Dictionary<int, MatchAnimationPlayer> playerBySlot =
            new Dictionary<int, MatchAnimationPlayer>();
        private readonly Dictionary<int, PlanBehavior> lastBehavior =
            new Dictionary<int, PlanBehavior>();
        private readonly Dictionary<int, int> lastBehaviorEventCount = new Dictionary<int, int>();
        private readonly Dictionary<int, string> lastEventBinding = new Dictionary<int, string>();
        private readonly Dictionary<int, int> lastPersistentIndex = new Dictionary<int, int>();
        private readonly Dictionary<int, int> lastAnimationIndex = new Dictionary<int, int>();

        public MatchAnimationConsumer(MatchWorldData world, Simulation sim, MatchSkaterView[] views)
        {
            if (world == null || views == null)
            {
                throw new ArgumentNullException(world == null ? "world" : "views");
            }

            Registry = MatchAnimationRegistry.Load(world.Root);
            Players = new MatchAnimationPlayer[views.Length];

            for (int slot = 0; slot < views.Length; slot++)
            {
                MatchSkaterView view = views[slot];

                if (view == null || view.Character == null)
                {
                    continue;
                }

                MatchAnimationPlayer player = new MatchAnimationPlayer(slot, view, Registry);
                player.EventFired = OnAnimationEvent;
                Players[slot] = player;
                playerBySlot[slot] = player;

                // The character's own loaded persistent clip is the default; the plan can replace it
                // through behaviour 9. The goalie's default is AA_Goalidle, the skater's AA_Skate_Std.
                Trace.Add("default slot=" + slot + " clip=" + player.CurrentName
                          + " persistent=" + player.PersistentName);
            }

            Debug.Log(Marker + "_BUILT " + Describe());
        }

        public static bool ShouldEnableByDefault()
        {
            if (ForceEnabled || ExplicitlyEnabled)
            {
                return true;
            }

            if (!Application.isPlaying || Application.isBatchMode)
            {
                return false;
            }

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];

                for (int c = 0; c < CaptureArguments.Length; c++)
                {
                    if (argument.StartsWith(CaptureArguments[c], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void AttachRoster(MatchPlanAiRoster roster)
        {
            Roster = roster;

            if (roster == null)
            {
                Trace.Add("plan-roster detached");
                return;
            }

            PlanSlots = roster.PlanSlots.Count;
            Trace.Add("plan-roster attached slots=" + PlanSlots
                      + " [" + string.Join(",", roster.PlanSlots) + "]");
            Debug.Log(Marker + "_PLAN_ROSTER slots=" + PlanSlots);
        }

        public void Tick(double deltaSeconds)
        {
            if (Roster != null)
            {
                for (int i = 0; i < Roster.PlanSlots.Count; i++)
                {
                    int slot = Roster.PlanSlots[i];
                    MatchPlanAi plan = Roster.Adapter(slot);

                    if (plan != null)
                    {
                        ConsumePlan(plan, slot);
                    }
                }
            }

            for (int slot = 0; slot < Players.Length; slot++)
            {
                MatchAnimationPlayer player = Players[slot];

                if (player != null)
                {
                    player.Tick(deltaSeconds);
                }
            }

            Ticks++;
        }

        public void ApplyRender()
        {
            for (int slot = 0; slot < Players.Length; slot++)
            {
                if (Players[slot] != null)
                {
                    Players[slot].Pose();
                }
            }
        }

        // Deterministic N15 fixture for the same consumer path. The natural plan trace observed
        // action 30 and behaviour 9; this fixture drives the other shipped animation categories
        // (celebration, throw, launch, fall, idle/skate) through the exact resolver/player used by the
        // wired action/behparam path, so the trace can show visible clip changes even when the local
        // plan decisions only request the persistent skate.
        public bool PlayFixture(int slot, int index, float rate, int flags, string source)
        {
            MatchAnimationPlayer player;

            if (!playerBySlot.TryGetValue(slot, out player))
            {
                return false;
            }

            FixtureRequests++;
            RecordPlanId(index);
            bool changed = player.TryPlay(index, rate, flags, source);

            if (changed)
            {
                FixtureApplied++;
                PlanAnimationApplied++;
            }

            Trace.Add("fixture source=" + source + " slot=" + slot + " id=" + index
                      + " changed=" + (changed ? "True" : "False")
                      + " clip=" + player.CurrentName);
            return changed;
        }

        public bool SetPersistentFixture(int slot, int index, float rate, int flags, string source)
        {
            MatchAnimationPlayer player;

            if (!playerBySlot.TryGetValue(slot, out player))
            {
                return false;
            }

            FixtureRequests++;
            RecordPlanId(index);
            bool changed = player.SetPersistent(index, rate, flags, source);

            if (changed)
            {
                FixtureApplied++;
            }

            Trace.Add("fixture-persistent source=" + source + " slot=" + slot + " id=" + index
                      + " changed=" + (changed ? "True" : "False")
                      + " persistent=" + player.PersistentName);
            return changed;
        }

        private void ConsumePlan(MatchPlanAi plan, int slot)
        {
            MatchAnimationPlayer player;

            if (!playerBySlot.TryGetValue(slot, out player))
            {
                return;
            }

            PlanAiActionState effects = plan.AnimationActionState;

            if (effects != null)
            {
                int previous;
                lastActionEvents.TryGetValue(slot, out previous);

                if (effects.AnimationEvents > previous)
                {
                    lastActionEvents[slot] = effects.AnimationEvents;
                    PlanAnimationRequests++;
                    RecordPlanId(effects.AnimationIndex);

                    bool changed = player.TryPlay(effects.AnimationIndex, effects.AnimationRate,
                        effects.AnimationFlags, "action30");

                    if (changed)
                    {
                        PlanAnimationApplied++;
                    }
                    else if (player.UnsupportedCount > 0)
                    {
                        UnsupportedCount = player.UnsupportedCount;
                    }

                    Trace.Add("plan-action30 slot=" + slot + " id=" + effects.AnimationIndex
                              + " rate=" + effects.AnimationRate.ToString("0.###",
                                  CultureInfo.InvariantCulture) + " flags=" + effects.AnimationFlags
                              + " changed=" + (changed ? "True" : "False")
                              + " clip=" + player.CurrentName);
                }
            }

            PlanBehavior behavior = plan.CurrentBehavior;
            PlanAiBehaviorState state = plan.CurrentBehaviorState;

            if (behavior != null)
            {
                PlanBehavior previousBehavior;
                lastBehavior.TryGetValue(slot, out previousBehavior);

                if (!ReferenceEquals(behavior, previousBehavior))
                {
                    lastBehavior[slot] = behavior;
                    BehaviorChanges++;
                    Trace.Add("behavior slot=" + slot + " name=" + behavior.Name
                              + " previous=" + (previousBehavior == null ? "none"
                                  : previousBehavior.Name));

                    if (state != null && state.PersistentAnimationIndex >= 0)
                    {
                        RecordPlanId(state.PersistentAnimationIndex);
                        PersistentChanges++;
                        lastPersistentIndex[slot] = state.PersistentAnimationIndex;
                        player.SetPersistent(state.PersistentAnimationIndex, behavior.PersistentRate,
                            behavior.PersistentFlags, "behparam9");
                    }

                    if (state != null && state.AnimationIndex >= 0)
                    {
                        RecordPlanId(state.AnimationIndex);
                        lastAnimationIndex[slot] = state.AnimationIndex;
                        bool applied = player.TryPlay(state.AnimationIndex, behavior.AnimationRate,
                            behavior.AnimationFlags, "behparam11");

                        if (applied)
                        {
                            PlanAnimationApplied++;
                        }
                    }
                    else if (previousBehavior != null)
                    {
                        lastAnimationIndex[slot] = -1;
                        player.RestorePersistent("behavior-exit");
                    }
                }
                else if (state != null)
                {
                    int previousPersistent;
                    lastPersistentIndex.TryGetValue(slot, out previousPersistent);

                    if (state.PersistentAnimationIndex >= 0
                        && state.PersistentAnimationIndex != previousPersistent)
                    {
                        lastPersistentIndex[slot] = state.PersistentAnimationIndex;
                        RecordPlanId(state.PersistentAnimationIndex);
                        PersistentChanges++;
                        player.SetPersistent(state.PersistentAnimationIndex, behavior.PersistentRate,
                            behavior.PersistentFlags, "behparam9");
                    }

                    int previousAnimation;
                    lastAnimationIndex.TryGetValue(slot, out previousAnimation);

                    if (state.AnimationIndex >= 0 && state.AnimationIndex != previousAnimation)
                    {
                        RecordPlanId(state.AnimationIndex);
                        lastAnimationIndex[slot] = state.AnimationIndex;
                        bool applied = player.TryPlay(state.AnimationIndex, behavior.AnimationRate,
                            behavior.AnimationFlags, "behparam11");

                        if (applied)
                        {
                            PlanAnimationApplied++;
                        }
                    }

                    if (state.AnimationEvents > 0)
                    {
                        int previousEventCount;
                        lastBehaviorEventCount.TryGetValue(slot, out previousEventCount);

                        if (state.AnimationEvents != previousEventCount)
                        {
                            lastBehaviorEventCount[slot] = state.AnimationEvents;
                            string binding = "channel=" + behavior.ChannelSlot + " target="
                                             + behavior.EventTarget + " kind=" + behavior.EventKind
                                             + " param=" + behavior.EventParam;
                            lastEventBinding[slot] = binding;
                            Trace.Add("animation-event-binding slot=" + slot + " behavior="
                                      + behavior.Name + " " + binding);
                        }
                    }
                }
            }
        }

        private void RecordPlanId(int id)
        {
            if (id < 0 || !ResolvedPlanIds.Add(id))
            {
                return;
            }

            string name;
            AnimationAssembly assembly;

            if (Registry.TryResolve(id, out name, out assembly))
            {
                ResolvedPlanNames.Add(name);
                Trace.Add("resolved id=" + id + " name=" + name + " segment=0 range="
                          + (assembly.Segments.Length == 0 ? "none"
                              : assembly.Segments[0].StartFrame + ".." + assembly.Segments[0].EndFrame));
            }
        }

        private void OnAnimationEvent(MatchAnimationPlayer player, AnimationAssemblyTrigger trigger,
            int frame)
        {
            EventCount++;
            TriggerEvents++;
            string binding = string.Empty;

            if (lastEventBinding.TryGetValue(player.Slot, out binding))
            {
                Trace.Add("animation-event slot=" + player.Slot + " frame=" + trigger.Frame
                          + " action=" + trigger.ActionName + " clip=" + player.CurrentName
                          + " binding=" + binding);
            }
            else
            {
                Trace.Add("animation-event slot=" + player.Slot + " frame=" + trigger.Frame
                          + " action=" + trigger.ActionName + " clip=" + player.CurrentName);
            }

            if (EventLines.Count < 128)
            {
                EventLines.Add("slot=" + player.Slot + " frame=" + trigger.Frame + " action="
                               + trigger.ActionName + " sync="
                               + (trigger.HasSync ? trigger.SyncAssembly + ":" + trigger.SyncSegment
                                   + ":" + trigger.SyncFrame : "none"));
            }
        }

        public void RefreshCounters()
        {
            ClipChanges = 0;
            UnsupportedCount = 0;
            DeferredQueued = 0;
            DeferredApplied = 0;

            for (int slot = 0; slot < Players.Length; slot++)
            {
                if (Players[slot] == null)
                {
                    continue;
                }

                ClipChanges += Players[slot].ClipChanges;
                UnsupportedCount += Players[slot].UnsupportedCount;
                DeferredQueued += Players[slot].DeferredPending;
                DeferredApplied += Players[slot].DeferredApplied;
            }

            for (int i = 0; i < Players.Length; i++)
            {
                if (Players[i] != null)
                {
                    for (int u = 0; u < Players[i].Unsupported.Count; u++)
                    {
                        if (!Unsupported.Contains(Players[i].Unsupported[u]))
                        {
                            Unsupported.Add(Players[i].Unsupported[u]);
                        }
                    }
                }
            }
        }

        public string Describe()
        {
            RefreshCounters();
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append("_STATE");
            text.Append(" tick=").Append(Ticks);
            text.Append(" registry=").Append(Registry.Count);
            text.Append(" planSlots=").Append(PlanSlots);
            text.Append(" planRequests=").Append(PlanAnimationRequests);
            text.Append(" planApplied=").Append(PlanAnimationApplied);
            text.Append(" behaviorChanges=").Append(BehaviorChanges);
            text.Append(" persistentChanges=").Append(PersistentChanges);
            text.Append(" clipChanges=").Append(ClipChanges);
            text.Append(" animationEvents=").Append(EventCount);
            text.Append(" deferredQueued=").Append(DeferredQueued);
            text.Append(" deferredApplied=").Append(DeferredApplied);
            text.Append(" unsupported=").Append(UnsupportedCount);
            text.Append(" fixtureRequests=").Append(FixtureRequests);
            text.Append(" fixtureApplied=").Append(FixtureApplied);
            text.Append(" resolvedPlanIds=").Append(ResolvedPlanIds.Count);
            text.Append(" resolvedPlanNames=[")
                .Append(string.Join(",", new List<string>(ResolvedPlanNames).ToArray())).Append(']');

            for (int slot = 0; slot < Players.Length; slot++)
            {
                if (Players[slot] != null)
                {
                    text.Append(" [slot=").Append(slot).Append(' ')
                        .Append(Players[slot].Snapshot()).Append(']');
                }
            }

            return text.ToString();
        }

        public string Snapshot()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append("_SNAPSHOT tick=").Append(Ticks);

            for (int slot = 0; slot < Players.Length; slot++)
            {
                if (Players[slot] != null)
                {
                    text.Append(" ").Append(Players[slot].Snapshot());
                }
            }

            return text.ToString();
        }

        public string TraceText(int max)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < Trace.Count && i < max; i++)
            {
                text.Append(Marker).Append("_TRACE ").AppendLine(Trace[i]);
            }

            return text.ToString();
        }
    }
}
