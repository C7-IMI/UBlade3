using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The Plan AI gate: loads the shipped CARRYROK plan, instantiates it for an AI carrier and runs
    /// a deterministic TurnToMate -&gt; PassingPlay event/timer trace through the ported action and
    /// decision bodies over the mock match fixture. It reports the implemented ids, the trace, the
    /// unsupported telemetry and a trace digest, and prints UBLADE3_PLAN_AI_OK only when every check
    /// passes. This is not the match AI: only the handlers this flow reaches are ported.
    /// </summary>
    public static class PlanAiProbe
    {
        public const string PlanName = "CARRYROK";

        public const string PlanDigest = "46c109accf61ea5b";

        public const int PlanNodes = 889;

        public const int PlanLeaves = 1323;

        public const int PlanStrings = 43;

        public const int PlanBehaviors = 13;

        public const uint Seed = 0x5EED1234u;

        /// <summary>Regression anchors from the verified run; a changed body, table or queue order fails the probe.</summary>
        public const string ExpectedTraceDigest = "73073ff095cd4e00";

        public const int ExpectedDispatched = 10;

        public const int ExpectedPosted = 9;

        public const int ExpectedActionCalls = 5;

        public const int ExpectedDecisionCalls = 7;

        public const int ExpectedTimerArms = 4;

        public const int ExpectedUnsupportedIds = 7;

        public const int ExpectedPending = 1;

        public const string OkPrefix = "UBLADE3_PLAN_AI_OK";

        public const string FailPrefix = "UBLADE3_PLAN_AI_FAIL";

        private static string cachedDescribe;

        public static string Describe()
        {
            if (cachedDescribe != null)
            {
                return cachedDescribe;
            }

            try
            {
                string data = PlanVmProbe.DiscoverDataPath();

                if (data == null)
                {
                    cachedDescribe = "UBLADE3_PLAN_AI_ERROR GAME_DATA_NOT_FOUND";
                    return cachedDescribe;
                }

                List<string> failures;
                string report = BuildReport(data, out failures);
                cachedDescribe = FirstLine(report);
            }
            catch (Exception exception)
            {
                cachedDescribe = "UBLADE3_PLAN_AI_ERROR " + Flatten(exception.Message);
            }

            return cachedDescribe;
        }

        public static void Check()
        {
            string data = PlanVmProbe.DiscoverDataPath();

            if (data == null)
            {
                Console.Out.WriteLine("UBLADE3_PLAN_AI_FAIL GAME_DATA_NOT_FOUND (set HYPERBLADE_GAME_DATA to the install root)");
                throw new PlanVmException("UBLADE3_PLAN_AI_FAILED: game data not found");
            }

            List<string> failures;
            string report = BuildReport(data, out failures);
            Console.Out.Write(report);

            if (failures.Count > 0)
            {
                throw new PlanVmException("UBLADE3_PLAN_AI_FAILED: " + failures[0]);
            }
        }

        public static int Run()
        {
            return Run(new string[0]);
        }

        public static int Run(string[] args)
        {
            string data = args != null && args.Length > 0 ? args[0] : PlanVmProbe.DiscoverDataPath();

            if (data == null)
            {
                Console.Out.WriteLine("UBLADE3_PLAN_AI_FAIL GAME_DATA_NOT_FOUND (pass the install root or set HYPERBLADE_GAME_DATA)");
                return 2;
            }

            List<string> failures;

            try
            {
                data = PlanFileLoader.ResolveDataDirectory(data);
            }
            catch (Exception exception)
            {
                Console.Out.WriteLine("UBLADE3_PLAN_AI_FAIL " + Flatten(exception.Message));
                return 2;
            }

            string report = BuildReport(data, out failures);
            Console.Out.Write(report);

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>The deterministic trace result, shared with the self-check.</summary>
        public sealed class TraceResult
        {
            public PlanAiWorld World;

            public PlanAiHost Host;

            public PlanState State;

            public string FinalBehavior;

            public int BehaviorCount;

            public readonly List<string> BehaviorNames = new List<string>();

            public int PendingMessages;

            public int DispatchedMessages;

            public int PostedMessages;

            public int DroppedMessages;

            public int TimerArms;

            public int TimerFires;

            public int UnsupportedEvents;

            public List<string> Events = new List<string>();

            public string Digest;

            public bool ModelCountsDiffer()
            {
                return DispatchedMessages != PlanAiProbe.ExpectedDispatched || PostedMessages != PlanAiProbe.ExpectedPosted ||
                       TimerArms != PlanAiProbe.ExpectedTimerArms || PendingMessages != PlanAiProbe.ExpectedPending;
            }
        }

        public static TraceResult RunTrace(PlanFileImage image)
        {
            PlanAiWorld world = PlanAiFixture.CreateCarryRokWorld();
            PlanAiRandom.Seed(Seed);
            PlanAiHost host = new PlanAiHost(world, PlanAiFixture.CarrierSlot);
            PlanState state = host.InstantiatePlan(image);
            TraceResult result = new TraceResult();
            result.World = world;
            result.Host = host;
            result.State = state;

            state.ActivateBehavior(5);
            Pump(host, state, result);

            for (int step = 0; step < 300; step++)
            {
                if (state.Current != null && state.Current.Name == "PassingPlayPass")
                {
                    break;
                }

                world.ClockMs += 10;
                world.TickAgenda();
                Pump(host, state, result);
            }

            result.FinalBehavior = state.Current == null ? "<none>" : state.Current.Name;
            result.PendingMessages = world.Pending.Count;

            for (PlanBehavior behavior = state.Head; behavior != null; behavior = behavior.Standby)
            {
                result.BehaviorNames.Add(behavior.Name);
                result.BehaviorCount++;
            }

            state.Teardown();
            result.Events = new List<string>(world.Events);
            result.DispatchedMessages = Count(result, " dispatch target=");
            result.PostedMessages = Count(result, " post target=");
            result.DroppedMessages = Count(result, "dropped");
            result.TimerArms = Count(result, "timer-arm");
            result.TimerFires = Count(result, "timer-fire");
            result.UnsupportedEvents = Count(result, "unsupported.");
            result.Digest = DigestTrace(result, image.Name);

            return result;
        }

        /// <summary>Drains the pending scene queue in FIFO order, stopping after the behaviour change endpoint.</summary>
        private static void Pump(PlanAiHost host, PlanState state, TraceResult result)
        {
            int budget = 500;

            while (host.World.Pending.Count > 0 && budget-- > 0)
            {
                PlanAiPendingMessage message = host.World.Pending[0];
                host.World.Pending.RemoveAt(0);
                host.World.DispatchSceneMessage(message.TargetSlot, message.Message, message.Arg, DecisionLog.Fresh, message.Sender, message.FromInput);

                if (state.Current != null && state.Current.Name == "PassingPlayPass")
                {
                    return;
                }
            }
        }

        private static string DigestTrace(TraceResult result, string planName)
        {
            ulong digest = PlanVmDigest.Offset;
            digest = PlanVmDigest.Append(digest, "PLANAI plan=" + planName + " seed=0x" + Seed.ToString("x8", CultureInfo.InvariantCulture) + "\n");

            for (int index = 0; index < result.Events.Count; index++)
            {
                digest = PlanVmDigest.Append(digest, result.Events[index] + "\n");
            }

            digest = PlanVmDigest.Append(digest, "END behavior=" + result.FinalBehavior + " pending=" + result.PendingMessages + "\n");

            return PlanVmDigest.Hex(digest);
        }

        public static int Count(TraceResult result, string needle)
        {
            int count = 0;

            for (int index = 0; index < result.Events.Count; index++)
            {
                if (result.Events[index].Contains(needle))
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildReport(string dataPath, out List<string> failures)
        {
            failures = new List<string>();
            StringBuilder body = new StringBuilder();
            string okLine = null;
            string implLine = null;
            string planLine = null;
            string countsLine = null;
            List<string> traceLines = new List<string>();
            string selftestLine = null;

            try
            {
                string data = PlanFileLoader.ResolveDataDirectory(dataPath);
                PlanFileImage image = PlanFileLoader.LoadShipped(data, PlanName);
                image.Name = PlanName + ".PBN";
                PlanProgram program = PlanStreamParser.Parse(image, PlanArgResolver.ForSlot(PlanAiFixture.CarrierSlot), new PlanAiPlanTableResolver(PlanAiFixture.CarryRokPlanTable));
                PlanStatistics statistics = PlanAnalyzer.Analyze(image, program);

                planLine = "UBLADE3_PLAN_AI_PLAN name=" + statistics.Name + " words=" + statistics.Words + " nodes=" + statistics.Nodes +
                           " leaves=" + statistics.Leaves + " strings=" + statistics.Strings + " behaviors=" + statistics.Behaviors +
                           " unknowns=" + statistics.UnknownOpcodes + " digest=" + PlanVmDigest.Hex(statistics.Digest);

                if (statistics.Nodes != PlanNodes || statistics.Leaves != PlanLeaves || statistics.Strings != PlanStrings ||
                    statistics.Behaviors != PlanBehaviors || PlanVmDigest.Hex(statistics.Digest) != PlanDigest ||
                    statistics.UnknownOpcodes != 0)
                {
                    failures.Add("CARRYROK plan stats differ: nodes=" + statistics.Nodes + " leaves=" + statistics.Leaves +
                                 " strings=" + statistics.Strings + " behaviors=" + statistics.Behaviors +
                                 " unknown=" + statistics.UnknownOpcodes + " digest=" + PlanVmDigest.Hex(statistics.Digest));
                }

                TraceResult trace = RunTrace(image);
                TraceResult again = RunTrace(image);

                if (trace.Digest != again.Digest || trace.Events.Count != again.Events.Count || trace.FinalBehavior != again.FinalBehavior || trace.PendingMessages != again.PendingMessages)
                {
                    failures.Add("trace is not deterministic: digest=" + trace.Digest + " vs " + again.Digest);
                }

                CheckTrace(trace, failures);

                int unsupported = trace.Host.UnsupportedHandlers.Count;
                okLine = OkPrefix + " plan=" + PlanName + " behavior=TurnToMate>PassingPlay messages=" + trace.DispatchedMessages +
                         " actions=" + CountActions(trace.Host) + " decisions=" + CountDecisions(trace.Host) +
                         " timers=" + trace.TimerArms + " unknowns=" + statistics.UnknownOpcodes +
                         " unsupported=" + unsupported + " pending=" + trace.PendingMessages +
                         " digest=" + trace.Digest;

                implLine = "UBLADE3_PLAN_AI_IMPL actions=" + JoinIds(PlanAiActions.ImplementedIds()) +
                           " decisions=" + JoinIds(PlanAiDecisions.ImplementedIds()) +
                           " behaviorParams=none agenda=timer(18)";

                countsLine = "UBLADE3_PLAN_AI_COUNTS messages=" + trace.DispatchedMessages + " posted=" + trace.PostedMessages +
                             " dropped=" + trace.DroppedMessages + " actions=" + CountActions(trace.Host) +
                             " decisions=" + CountDecisions(trace.Host) + " timersArmed=" + trace.TimerArms +
                             " timersFired=" + trace.TimerFires + " unsupported=" + unsupported +
                             " unsupportedIds=[" + JoinStrings(trace.Host.UnsupportedHandlers) + "]" +
                             " pending=" + trace.PendingMessages;

                for (int index = 0; index < trace.Events.Count; index++)
                {
                    traceLines.Add("UBLADE3_PLAN_AI_TRACE " + trace.Events[index]);
                }

                PlanAiSelfCheckResult selfCheck = PlanAiSelfCheck.Run();
                selftestLine = "UBLADE3_PLAN_AI_SELFTEST checks=" + selfCheck.Passed + "/" + selfCheck.Total;

                if (!selfCheck.Ok)
                {
                    for (int index = 0; index < selfCheck.Failures.Count; index++)
                    {
                        failures.Add("selftest " + selfCheck.Failures[index]);
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add("probe exception: " + exception.GetType().Name + ": " + Flatten(exception.Message));
            }

            StringBuilder report = new StringBuilder();

            for (int index = 0; index < failures.Count; index++)
            {
                report.Append(FailPrefix).Append(' ').Append(Flatten(failures[index])).Append('\n');
            }

            if (failures.Count == 0 && okLine != null)
            {
                report.Append(okLine).Append('\n');
            }

            if (implLine != null)
            {
                report.Append(implLine).Append('\n');
            }

            if (planLine != null)
            {
                report.Append(planLine).Append('\n');
            }

            if (countsLine != null)
            {
                report.Append(countsLine).Append('\n');
            }

            for (int index = 0; index < traceLines.Count; index++)
            {
                report.Append(traceLines[index]).Append('\n');
            }

            if (selftestLine != null)
            {
                report.Append(selftestLine).Append('\n');
            }

            return report.ToString();
        }

        private static void CheckTrace(TraceResult trace, List<string> failures)
        {
            if (trace.State.Behaviors.Length != 15)
            {
                failures.Add("compiled CARRYROK top-level nodes " + trace.State.Behaviors.Length + ", expected 13 behaviours plus 1 global-interactor block plus END_PLAN");
            }

            if (trace.BehaviorCount != PlanBehaviors)
            {
                failures.Add("instantiated behaviours " + trace.BehaviorCount + ", expected " + PlanBehaviors + " names=" + string.Join(",", trace.BehaviorNames.ToArray()));
            }

            if (trace.Digest != ExpectedTraceDigest)
            {
                failures.Add("trace digest " + trace.Digest + ", expected " + ExpectedTraceDigest);
            }

            if (trace.ModelCountsDiffer())
            {
                failures.Add("trace counts differ from the verified anchors");
            }

            if (trace.FinalBehavior != "PassingPlayPass")
            {
                failures.Add("trace did not reach PassingPlayPass (final=" + trace.FinalBehavior + ")");
            }

            if (trace.PendingMessages != 1)
            {
                failures.Add("pending messages at endpoint " + trace.PendingMessages + ", expected the one queued PassingPlayPass Initiate");
            }

            Expect(trace, "decide id=15 ", 2, failures);
            Expect(trace, "decide id=111 ", 2, failures);
            Expect(trace, "decide id=50 ", 2, failures);
            Expect(trace, "decide id=78 ", 1, failures);
            Expect(trace, "branch=2", 2, failures);
            Expect(trace, "branch=0", 1, failures);
            Expect(trace, "log=0x9002", 2, failures);
            Expect(trace, "log=0x9000", 1, failures);
            Expect(trace, "timer-arm event=69(TimeOut)", 3, failures);
            Expect(trace, "timer-fire event=69(TimeOut)", 3, failures);
            Expect(trace, "timer-fire event=124(Extra1)", 1, failures);
            Expect(trace, " dispatch target=", 10, failures);
            Expect(trace, " post target=", 9, failures);

            int dataZeroRearm = 0;

            for (int index = 0; index < trace.Events.Count; index++)
            {
                if (trace.Events[index].Contains("timer-arm event=69(TimeOut)") && trace.Events[index].Contains("data=0 fire=2020"))
                {
                    dataZeroRearm++;
                }
            }

            if (dataZeroRearm != 1)
            {
                failures.Add("documented data-0 timer re-arm not observed exactly once (saw " + dataZeroRearm + ")");
            }

            int unsupportedAgenda = 0;

            for (int index = 0; index < trace.Events.Count; index++)
            {
                if (trace.Events[index].Contains("unsupported.agenda-add id=8"))
                {
                    unsupportedAgenda++;
                }
            }

            if (unsupportedAgenda != 1)
            {
                failures.Add("CAvoidObjects agenda add telemetry count " + unsupportedAgenda + ", expected 1");
            }

            if (trace.Host.ActionCalls.ContainsKey(PlanOpcodes.ActionIds.SendMessage) == false || trace.Host.ActionCalls[PlanOpcodes.ActionIds.SendMessage] != 3)
            {
                failures.Add("SendMessage actions " + GetCount(trace.Host.ActionCalls, PlanOpcodes.ActionIds.SendMessage) + ", expected 3");
            }

            if (GetCount(trace.Host.ActionCalls, PlanOpcodes.ActionIds.ReallySendMsg) != 1 || GetCount(trace.Host.ActionCalls, PlanOpcodes.ActionIds.SetKnockedDownState) != 1)
            {
                failures.Add("ReallySendMsg/SetKnockedDownState counts differ from 1/1");
            }

            if (trace.Host.UnsupportedHandlers.Count != ExpectedUnsupportedIds)
            {
                failures.Add("unsupported handler id count " + trace.Host.UnsupportedHandlers.Count + ", expected " + ExpectedUnsupportedIds);
            }

            if (trace.Host.UnsupportedHandlers.Contains("agenda-item:8") == false)
            {
                failures.Add("the CAvoidObjects agenda id 8 must stay visible as unsupported telemetry");
            }
        }

        private static void Expect(TraceResult trace, string needle, int expected, List<string> failures)
        {
            int count = Count(trace, needle);

            if (count != expected)
            {
                failures.Add("trace count for [" + needle + "] is " + count + ", expected " + expected);
            }
        }

        private static int GetCount(Dictionary<int, int> table, int key)
        {
            int value;

            return table.TryGetValue(key, out value) ? value : 0;
        }

        private static int CountActions(PlanAiHost host)
        {
            int total = 0;

            foreach (KeyValuePair<int, int> entry in host.ActionCalls)
            {
                total += entry.Value;
            }

            return total;
        }

        private static int CountDecisions(PlanAiHost host)
        {
            int total = 0;

            foreach (KeyValuePair<int, int> entry in host.DecisionCalls)
            {
                total += entry.Value;
            }

            return total;
        }

        private static string JoinIds(int[] ids)
        {
            StringBuilder text = new StringBuilder();

            for (int index = 0; index < ids.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append(ids[index]);
            }

            return text.ToString();
        }

        private static string JoinStrings(List<string> values)
        {
            List<string> sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);

            return string.Join(",", sorted.ToArray());
        }

        private static string FirstLine(string report)
        {
            int newline = report.IndexOf('\n');

            return newline < 0 ? report : report.Substring(0, newline);
        }

        private static string Flatten(string text)
        {
            return text == null ? string.Empty : text.Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    /// <summary>IPlanNameResolver over a fixed owner plan table, matched with strcmp semantics.</summary>
    public sealed class PlanAiPlanTableResolver : IPlanNameResolver
    {
        private readonly List<string> table;

        public PlanAiPlanTableResolver(IEnumerable<string> names)
        {
            table = new List<string>(names);
        }

        public int FindPlanByName(string name)
        {
            for (int index = 0; index < table.Count; index++)
            {
                if (string.Equals(table[index], name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
