using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The U3 plan-VM gate: a scene-free probe that loads every shipped .PBN, checks the node tree
    /// and its digest against independently derived expected values, instantiates each plan, sweeps
    /// every interactor script through the exact ExecuteScript dispatch with a recording host, runs
    /// the synthetic self-checks and prints traces for known plans. It never needs a scene and never
    /// writes into the repository.
    ///
    /// Output contract:
    ///   UBLADE3_PLANVM_OK plans=32 instructions=9420 opcodes=16 unknowns=0 digest=141a750a3de1e083
    ///   UBLADE3_PLANVM_DETAIL data=&lt;dir&gt; plans=&lt;dir&gt; catalog=32 fixes=0
    ///   UBLADE3_PLANVM_COUNTS ...
    ///   UBLADE3_PLANVM_OPS opcodes=16 ...
    ///   UBLADE3_PLANVM_PLAN name=... words=... nodes=... ... digest=...      (one per plan)
    ///   UBLADE3_PLANVM_COVERAGE ...
    ///   UBLADE3_PLANVM_TRACE ...                                             (known-plan traces)
    ///   UBLADE3_PLANVM_SELFTEST passed/total
    ///   UBLADE3_PLANVM_FAIL ...                                              (only on failure)
    /// </summary>
    public static class PlanVmProbe
    {
        public const int ExpectedPlanCount = 32;

        public const int ExpectedInstructionCount = 9420;

        public const int ExpectedOpcodeCount = 16;

        public const int ExpectedUnknownCount = 0;

        public const string ExpectedOkDigest = "141a750a3de1e083";

        public const int ExpectedCoverageBehaviors = 185;

        public const int ExpectedCoverageGlobalCollision = 14;

        public const int ExpectedCoverageGlobalMessage = 227;

        public const int ExpectedCoverageMessageDispatched = 1173;

        public const int ExpectedCoverageCollisionDispatched = 256;

        public const int ExpectedCoverageUnsupportedCalls = 1;

        public const string OkPrefix = "UBLADE3_PLANVM_OK";

        public const string FailPrefix = "UBLADE3_PLANVM_FAIL";

        /// <summary>Independently derived by a separate reader of the retail files (tools/plan_graph.py); one line per plan.</summary>
        private static readonly string[] ExpectedPlanLines = new string[]
        {
            "UBLADE3_PLANVM_PLAN name=ARMORY.PBN words=243 nodes=49 leaves=69 strings=3 behaviors=3 globals=0 locals=0 coll=4 msg=8 enable=11 decisions=1 actions=6 agenda=1 namedQ=0 maxdepth=4 unknown=0 digest=a0f611199900218c",
            "UBLADE3_PLANVM_PLAN name=BODYPART.PBN words=1540 nodes=362 leaves=395 strings=6 behaviors=6 globals=1 locals=0 coll=30 msg=23 enable=44 decisions=31 actions=85 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=1d3d7bb63e2966fa",
            "UBLADE3_PLANVM_PLAN name=CAMERA.PBN words=1124 nodes=239 leaves=305 strings=7 behaviors=7 globals=1 locals=0 coll=0 msg=33 enable=30 decisions=13 actions=83 agenda=2 namedQ=0 maxdepth=5 unknown=0 digest=cd0fc4c87ec74b93",
            "UBLADE3_PLANVM_PLAN name=CARRYROK.PBN words=4431 nodes=889 leaves=1323 strings=43 behaviors=13 globals=1 locals=0 coll=2 msg=127 enable=105 decisions=57 actions=281 agenda=22 namedQ=0 maxdepth=7 unknown=0 digest=46c109accf61ea5b",
            "UBLADE3_PLANVM_PLAN name=COACH.PBN words=56 nodes=9 leaves=6 strings=2 behaviors=2 globals=0 locals=0 coll=0 msg=2 enable=2 decisions=0 actions=0 agenda=2 namedQ=0 maxdepth=3 unknown=0 digest=7cf39e46c03f0e41",
            "UBLADE3_PLANVM_PLAN name=CRSHDUM0.PBN words=175 nodes=33 leaves=32 strings=3 behaviors=3 globals=1 locals=0 coll=0 msg=8 enable=6 decisions=1 actions=4 agenda=0 namedQ=0 maxdepth=4 unknown=0 digest=367c6143247e9593",
            "UBLADE3_PLANVM_PLAN name=DEFENSE.PBN words=6163 nodes=1252 leaves=1910 strings=46 behaviors=17 globals=1 locals=0 coll=14 msg=191 enable=154 decisions=71 actions=393 agenda=44 namedQ=0 maxdepth=7 unknown=0 digest=acb5dedefc6ebafa",
            "UBLADE3_PLANVM_PLAN name=EXPLODE.PBN words=68 nodes=14 leaves=17 strings=1 behaviors=1 globals=0 locals=0 coll=0 msg=4 enable=4 decisions=0 actions=4 agenda=0 namedQ=0 maxdepth=3 unknown=0 digest=ef8ec36880c6c2b3",
            "UBLADE3_PLANVM_PLAN name=GOALIE.PBN words=2816 nodes=580 leaves=771 strings=19 behaviors=19 globals=1 locals=0 coll=11 msg=113 enable=79 decisions=44 actions=114 agenda=12 namedQ=0 maxdepth=5 unknown=0 digest=622b07010f53d1b1",
            "UBLADE3_PLANVM_PLAN name=GRANDOM.PBN words=1032 nodes=212 leaves=379 strings=2 behaviors=2 globals=1 locals=0 coll=16 msg=13 enable=29 decisions=10 actions=90 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=9e4c2f20c982a97e",
            "UBLADE3_PLANVM_PLAN name=HELMCAM.PBN words=473 nodes=94 leaves=108 strings=7 behaviors=5 globals=1 locals=0 coll=2 msg=17 enable=11 decisions=5 actions=22 agenda=7 namedQ=0 maxdepth=5 unknown=0 digest=89c028c63cd9fa76",
            "UBLADE3_PLANVM_PLAN name=HUMAN.PBN words=5973 nodes=1200 leaves=1917 strings=41 behaviors=20 globals=1 locals=0 coll=2 msg=159 enable=137 decisions=65 actions=534 agenda=13 namedQ=0 maxdepth=7 unknown=0 digest=6e347801c044dfb7",
            "UBLADE3_PLANVM_PLAN name=HURDLE.PBN words=240 nodes=53 leaves=72 strings=1 behaviors=1 globals=0 locals=0 coll=9 msg=1 enable=10 decisions=5 actions=9 agenda=1 namedQ=0 maxdepth=4 unknown=0 digest=37e44cdba84d2a83",
            "UBLADE3_PLANVM_PLAN name=INPUT.PBN words=686 nodes=137 leaves=251 strings=2 behaviors=2 globals=1 locals=0 coll=0 msg=9 enable=8 decisions=9 actions=61 agenda=9 namedQ=0 maxdepth=7 unknown=0 digest=b36d9443680a6efb",
            "UBLADE3_PLANVM_PLAN name=INSCRTBL.PBN words=285 nodes=63 leaves=89 strings=1 behaviors=1 globals=1 locals=0 coll=14 msg=2 enable=16 decisions=0 actions=28 agenda=0 namedQ=0 maxdepth=3 unknown=0 digest=588d4a6e85326f70",
            "UBLADE3_PLANVM_PLAN name=LASERHRD.PBN words=486 nodes=99 leaves=172 strings=2 behaviors=2 globals=1 locals=0 coll=8 msg=8 enable=10 decisions=4 actions=32 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=85361a043aabaab0",
            "UBLADE3_PLANVM_PLAN name=LIMBOPO.PBN words=328 nodes=69 leaves=112 strings=1 behaviors=1 globals=0 locals=0 coll=8 msg=2 enable=9 decisions=5 actions=21 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=d228054bdda5bdfa",
            "UBLADE3_PLANVM_PLAN name=LISTEN.PBN words=1888 nodes=411 leaves=642 strings=2 behaviors=2 globals=1 locals=0 coll=0 msg=26 enable=26 decisions=63 actions=247 agenda=0 namedQ=0 maxdepth=6 unknown=0 digest=def9ac87ea86980c",
            "UBLADE3_PLANVM_PLAN name=MINE.PBN words=935 nodes=194 leaves=269 strings=7 behaviors=7 globals=1 locals=0 coll=16 msg=26 enable=42 decisions=5 actions=57 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=a4e0689cc7abf8e5",
            "UBLADE3_PLANVM_PLAN name=MINIMAL.PBN words=16 nodes=2 leaves=1 strings=1 behaviors=1 globals=0 locals=0 coll=0 msg=0 enable=0 decisions=0 actions=0 agenda=0 namedQ=0 maxdepth=0 unknown=0 digest=ecf9e95671318497",
            "UBLADE3_PLANVM_PLAN name=MULTPLR.PBN words=1081 nodes=227 leaves=319 strings=7 behaviors=7 globals=1 locals=0 coll=28 msg=23 enable=47 decisions=13 actions=61 agenda=3 namedQ=0 maxdepth=6 unknown=0 digest=a98735d9c83cd34b",
            "UBLADE3_PLANVM_PLAN name=NGOAL.PBN words=626 nodes=120 leaves=213 strings=3 behaviors=3 globals=1 locals=0 coll=0 msg=13 enable=11 decisions=7 actions=56 agenda=0 namedQ=0 maxdepth=7 unknown=0 digest=26cf8cdd1e9df420",
            "UBLADE3_PLANVM_PLAN name=OFFWING.PBN words=4969 nodes=976 leaves=1468 strings=56 behaviors=14 globals=1 locals=0 coll=12 msg=145 enable=134 decisions=54 actions=288 agenda=35 namedQ=0 maxdepth=7 unknown=0 digest=9714b73f049f2ce3",
            "UBLADE3_PLANVM_PLAN name=RAMP.PBN words=137 nodes=31 leaves=35 strings=1 behaviors=1 globals=0 locals=0 coll=4 msg=0 enable=4 decisions=4 actions=8 agenda=0 namedQ=0 maxdepth=4 unknown=0 digest=295c6cc8164272af",
            "UBLADE3_PLANVM_PLAN name=ROK.PBN words=3124 nodes=661 leaves=982 strings=13 behaviors=13 globals=1 locals=0 coll=28 msg=70 enable=72 decisions=39 actions=227 agenda=10 namedQ=0 maxdepth=7 unknown=0 digest=65fb78df09f51231",
            "UBLADE3_PLANVM_PLAN name=RRANDOM.PBN words=867 nodes=190 leaves=273 strings=3 behaviors=3 globals=1 locals=0 coll=16 msg=9 enable=25 decisions=9 actions=67 agenda=2 namedQ=0 maxdepth=5 unknown=0 digest=d9b3825b8e6c0f27",
            "UBLADE3_PLANVM_PLAN name=SAUSAGE.PBN words=47 nodes=9 leaves=10 strings=1 behaviors=1 globals=0 locals=0 coll=0 msg=2 enable=2 decisions=0 actions=3 agenda=0 namedQ=0 maxdepth=3 unknown=0 digest=736309c7d1e9a3c9",
            "UBLADE3_PLANVM_PLAN name=SGOAL.PBN words=626 nodes=120 leaves=213 strings=3 behaviors=3 globals=1 locals=0 coll=0 msg=13 enable=11 decisions=7 actions=56 agenda=0 namedQ=0 maxdepth=7 unknown=0 digest=6f2f2019a8e21a53",
            "UBLADE3_PLANVM_PLAN name=SIDELN.PBN words=3737 nodes=733 leaves=1307 strings=23 behaviors=10 globals=1 locals=0 coll=0 msg=71 enable=71 decisions=48 actions=374 agenda=4 namedQ=0 maxdepth=7 unknown=0 digest=30b66457887a124c",
            "UBLADE3_PLANVM_PLAN name=STARTGUN.PBN words=368 nodes=76 leaves=106 strings=4 behaviors=4 globals=1 locals=0 coll=0 msg=20 enable=9 decisions=0 actions=22 agenda=0 namedQ=0 maxdepth=3 unknown=0 digest=d188d68290d0e4e4",
            "UBLADE3_PLANVM_PLAN name=SWARF.PBN words=638 nodes=134 leaves=156 strings=6 behaviors=6 globals=1 locals=0 coll=4 msg=19 enable=23 decisions=3 actions=31 agenda=0 namedQ=0 maxdepth=5 unknown=0 digest=cf4659be6e7361f2",
            "UBLADE3_PLANVM_PLAN name=TURNSTIL.PBN words=890 nodes=182 leaves=285 strings=5 behaviors=5 globals=1 locals=0 coll=28 msg=17 enable=41 decisions=5 actions=59 agenda=1 namedQ=0 maxdepth=5 unknown=0 digest=5e6fc7734df75beb",
        };

        private static string cachedDescribe;

        /// <summary>PortLab/reflection entry: the cached one-line status, never throws and needs no scene.</summary>
        public static string Describe()
        {
            if (cachedDescribe != null)
            {
                return cachedDescribe;
            }

            try
            {
                string data = DiscoverDataPath();

                if (data == null)
                {
                    cachedDescribe = "UBLADE3_PLANVM_ERROR GAME_DATA_NOT_FOUND";
                    return cachedDescribe;
                }

                List<string> failures;
                string report = BuildReport(data, out failures);
                cachedDescribe = FirstLine(report);
            }
            catch (Exception exception)
            {
                cachedDescribe = "UBLADE3_PLANVM_ERROR " + Flatten(exception.Message);
            }

            return cachedDescribe;
        }

        /// <summary>Batch entry: prints the report and throws when the probe failed (Unity -executeMethod).</summary>
        public static void Check()
        {
            string data = DiscoverDataPath();

            if (data == null)
            {
                Console.Out.WriteLine("UBLADE3_PLANVM_FAIL GAME_DATA_NOT_FOUND (set HYPERBLADE_GAME_DATA to the HyperBlade install root)");
                throw new PlanVmException("UBLADE3_PLANVM_FAILED: game data not found");
            }

            List<string> failures;
            string report = BuildReport(data, out failures);
            Console.Out.Write(report);

            if (failures.Count > 0)
            {
                throw new PlanVmException("UBLADE3_PLANVM_FAILED: " + failures[0]);
            }
        }

        public static int Run()
        {
            return Run(new string[0]);
        }

        /// <summary>Process entry: args[0] may name the install root; otherwise HYPERBLADE_GAME_DATA is used.</summary>
        public static int Run(string[] args)
        {
            string data = args != null && args.Length > 0 ? args[0] : DiscoverDataPath();

            if (data == null)
            {
                Console.Out.WriteLine("UBLADE3_PLANVM_FAIL GAME_DATA_NOT_FOUND (pass the install root or set HYPERBLADE_GAME_DATA)");
                return 2;
            }

            List<string> failures;

            try
            {
                data = PlanFileLoader.ResolveDataDirectory(data);
            }
            catch (Exception exception)
            {
                Console.Out.WriteLine("UBLADE3_PLANVM_FAIL " + Flatten(exception.Message));
                return 2;
            }

            string report = BuildReport(data, out failures);
            Console.Out.Write(report);

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>The install root or data dir from HYPERBLADE_GAME_DATA, else the working directory's data folder.</summary>
        public static string DiscoverDataPath()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("HYPERBLADE_GAME_DATA");

            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                try
                {
                    return PlanFileLoader.ResolveDataDirectory(fromEnvironment);
                }
                catch (PlanFileException)
                {
                }
            }

            try
            {
                return PlanFileLoader.ResolveDataDirectory(Directory.GetCurrentDirectory());
            }
            catch (PlanFileException)
            {
                return null;
            }
        }

        // ---- report ---------------------------------------------------------------------------

        private sealed class LoadedPlan
        {
            public PlanFileImage Image;

            public PlanProgram Program;

            public PlanStatistics Stats;
        }

        private sealed class PreparedPlan
        {
            public PlanVmRecordingHost Host;

            public PlanState State;
        }

        private static string BuildReport(string dataPath, out List<string> failures)
        {
            failures = new List<string>();
            StringBuilder body = new StringBuilder();
            List<string> planLines = new List<string>();
            List<string> traceLines = new List<string>();
            string okLine = null;
            string detailLine = null;
            string countsLine = null;
            string opsLine = null;
            string coverageLine = null;
            string selftestLine = null;

            try
            {
                string data = PlanFileLoader.ResolveDataDirectory(dataPath);
                string plansDirectory = PlanFileLoader.ResolvePlanDirectory(data);
                string[] files = PlanFileLoader.EnumeratePlanFiles(data);
                string[] catalog = PlanFileLoader.ReadCatalog(data);
                int fixes = PlanFileLoader.CountFixFiles(data);

                detailLine = "UBLADE3_PLANVM_DETAIL data=" + data + " plans=" + plansDirectory + " catalog=" + catalog.Length + " fixes=" + fixes;

                if (files.Length != ExpectedPlanCount)
                {
                    failures.Add("plan file count is " + files.Length + ", expected " + ExpectedPlanCount + " under " + plansDirectory);
                }

                List<LoadedPlan> plans = new List<LoadedPlan>();
                List<string> names = new List<string>();
                List<string> keys = new List<string>();
                Dictionary<string, LoadedPlan> byName = new Dictionary<string, LoadedPlan>(StringComparer.OrdinalIgnoreCase);

                for (int index = 0; index < files.Length; index++)
                {
                    names.Add(Path.GetFileNameWithoutExtension(files[index]).ToUpperInvariant());
                    keys.Add(Path.GetFileName(files[index]).ToUpperInvariant());
                }

                ShippedPlanNames parseNames = new ShippedPlanNames();

                for (int index = 0; index < names.Count; index++)
                {
                    parseNames.Add(names[index], index);
                }

                for (int index = 0; index < files.Length; index++)
                {
                    string name = names[index];
                    PlanFileImage image = PlanFileLoader.Load(files[index], keys[index]);
                    PlanProgram program = PlanStreamParser.Parse(image, PlanArgResolver.ForSlot(0), parseNames);
                    PlanStatistics statistics = PlanAnalyzer.Analyze(image, program);

                    LoadedPlan loaded = new LoadedPlan();
                    loaded.Image = image;
                    loaded.Program = program;
                    loaded.Stats = statistics;
                    plans.Add(loaded);
                    byName[keys[index]] = loaded;

                    planLines.Add(statistics.PlanLine());

                    if (index < ExpectedPlanLines.Length)
                    {
                        if (!string.Equals(statistics.PlanLine(), ExpectedPlanLines[index], StringComparison.Ordinal))
                        {
                            failures.Add("plan mismatch " + name + ": actual [" + statistics.PlanLine() + "]");
                        }
                    }
                    else
                    {
                        failures.Add("plan " + name + " has no expected line");
                    }
                }

                if (ExpectedPlanLines.Length != plans.Count)
                {
                    failures.Add("expected line count is " + ExpectedPlanLines.Length + ", loaded " + plans.Count);
                }

                // Catalog cross-check: every PLANS.CAT name must back a shipped file, and every shipped file a catalog row.
                HashSet<string> catalogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> shippedNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int index = 0; index < catalog.Length; index++)
                {
                    catalogNames.Add(catalog[index]);
                }

                for (int index = 0; index < names.Count; index++)
                {
                    shippedNameSet.Add(names[index]);
                }

                for (int index = 0; index < catalog.Length; index++)
                {
                    if (!shippedNameSet.Contains(catalog[index]))
                    {
                        failures.Add("PLANS.CAT names " + catalog[index] + " but no plan file loads it");
                    }
                }

                for (int index = 0; index < names.Count; index++)
                {
                    if (!catalogNames.Contains(names[index]))
                    {
                        failures.Add("plan file " + names[index] + " is not in PLANS.CAT");
                    }
                }

                if (catalog.Length != ExpectedPlanCount)
                {
                    failures.Add("PLANS.CAT has " + catalog.Length + " names, expected " + ExpectedPlanCount);
                }

                // Totals and aggregate digest over the exact per-plan lines.
                int words = 0, nodes = 0, leaves = 0, strings = 0, behaviors = 0, globals = 0, locals = 0;
                int coll = 0, msg = 0, enable = 0, decisions = 0, actions = 0, agenda = 0, namedQuirk = 0, maxDepth = 0, unknown = 0;
                int unresolvedPlanReferences = 0;
                HashSet<int> opcodes = new HashSet<int>();
                Dictionary<int, int> opcodeHistogram = new Dictionary<int, int>();
                ulong aggregate = PlanVmDigest.Offset;

                for (int index = 0; index < plans.Count; index++)
                {
                    PlanStatistics statistics = plans[index].Stats;
                    words += statistics.Words;
                    nodes += statistics.Nodes;
                    leaves += statistics.Leaves;
                    strings += statistics.Strings;
                    behaviors += statistics.Behaviors;
                    globals += statistics.Globals;
                    locals += statistics.Locals;
                    coll += statistics.CollInteractors;
                    msg += statistics.MsgInteractors;
                    enable += statistics.EnableBlocks;
                    decisions += statistics.Decisions;
                    actions += statistics.Actions;
                    agenda += statistics.AgendaNodes;
                    namedQuirk += statistics.NamedQuirk;
                    maxDepth = Math.Max(maxDepth, statistics.MaxDepth);
                    unknown += statistics.UnknownOpcodes;
                    unresolvedPlanReferences += statistics.UnresolvedPlanReferences;

                    foreach (KeyValuePair<int, int> entry in statistics.OpcodeHistogram)
                    {
                        if (entry.Key == PlanWord.EndPlanOpcode)
                        {
                            continue;
                        }

                        opcodes.Add(entry.Key);
                        int value;
                        opcodeHistogram.TryGetValue(entry.Key, out value);
                        opcodeHistogram[entry.Key] = value + entry.Value;
                    }

                    aggregate = PlanVmDigest.Append(aggregate, planLines[index] + "\n");
                }

                countsLine = "UBLADE3_PLANVM_COUNTS words=" + words + " nodes=" + nodes + " leaves=" + leaves + " strings=" + strings +
                             " behaviors=" + behaviors + " globals=" + globals + " locals=" + locals + " coll=" + coll + " msg=" + msg +
                             " enable=" + enable + " decisions=" + decisions + " actions=" + actions + " agenda=" + agenda +
                             " namedQ=" + namedQuirk + " maxDepth=" + maxDepth + " unresolvedPlanRefs=" + unresolvedPlanReferences;

                if (nodes != ExpectedInstructionCount)
                {
                    failures.Add("instruction count is " + nodes + ", expected " + ExpectedInstructionCount);
                }

                if (opcodes.Count != ExpectedOpcodeCount)
                {
                    failures.Add("distinct opcode count is " + opcodes.Count + ", expected " + ExpectedOpcodeCount);
                }

                if (unknown != ExpectedUnknownCount)
                {
                    failures.Add("unknown opcode count is " + unknown + ", expected " + ExpectedUnknownCount);
                }

                if (plans.Count != ExpectedPlanCount)
                {
                    failures.Add("plan count is " + plans.Count + ", expected " + ExpectedPlanCount);
                }

                if (!string.Equals(PlanVmDigest.Hex(aggregate), ExpectedOkDigest, StringComparison.Ordinal))
                {
                    failures.Add("aggregate digest is " + PlanVmDigest.Hex(aggregate) + ", expected " + ExpectedOkDigest);
                }

                if (unresolvedPlanReferences != 0)
                {
                    failures.Add("unresolved CHANGE_OF_PLAN references: " + unresolvedPlanReferences);
                }

                okLine = OkPrefix + " plans=" + plans.Count + " instructions=" + nodes + " opcodes=" + opcodes.Count +
                         " unknowns=" + unknown + " digest=" + PlanVmDigest.Hex(aggregate);

                List<int> opcodeKeys = new List<int>(opcodeHistogram.Keys);
                opcodeKeys.Sort();
                StringBuilder ops = new StringBuilder("UBLADE3_PLANVM_OPS opcodes=" + opcodes.Count + " list=");

                for (int index = 0; index < opcodeKeys.Count; index++)
                {
                    if (index > 0)
                    {
                        ops.Append(',');
                    }

                    ops.Append(PlanOpcodes.KeywordOf(opcodeKeys[index])).Append('=').Append(opcodeHistogram[opcodeKeys[index]]);
                }

                opsLine = ops.ToString();

                ShippedPlanNames shippedNames = new ShippedPlanNames();

                for (int index = 0; index < names.Count; index++)
                {
                    shippedNames.Add(names[index], index);
                }

                coverageLine = RunCoverage(plans, shippedNames, failures);

                RunTraces(byName, shippedNames, traceLines, failures);

                PlanVmSelfCheckResult selfCheck = PlanVmSelfCheck.Run();
                selftestLine = "UBLADE3_PLANVM_SELFTEST checks=" + selfCheck.Passed + "/" + selfCheck.Total;

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

            // Compose: the OK line leads only when everything passed; otherwise every failure is named.
            StringBuilder report = new StringBuilder();

            for (int index = 0; index < failures.Count; index++)
            {
                report.Append(FailPrefix).Append(' ').Append(Flatten(failures[index])).Append('\n');
            }

            if (failures.Count == 0 && okLine != null)
            {
                report.Append(okLine).Append('\n');
            }

            if (detailLine != null)
            {
                report.Append(detailLine).Append('\n');
            }

            if (countsLine != null)
            {
                report.Append(countsLine).Append('\n');
            }

            if (opsLine != null)
            {
                report.Append(opsLine).Append('\n');
            }

            for (int index = 0; index < planLines.Count; index++)
            {
                report.Append(planLines[index]).Append('\n');
            }

            if (coverageLine != null)
            {
                report.Append(coverageLine).Append('\n');
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

        private static string FirstLine(string report)
        {
            int newline = report.IndexOf('\n');

            return newline < 0 ? report : report.Substring(0, newline);
        }

        private static string Flatten(string text)
        {
            return text == null ? string.Empty : text.Replace('\r', ' ').Replace('\n', ' ');
        }

        private sealed class ShippedPlanNames : IPlanNameResolver
        {
            private readonly Dictionary<string, int> byUpper = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public readonly List<string> Misses = new List<string>();

            public void Add(string name, int index)
            {
                string upper = name.ToUpperInvariant();

                if (upper.EndsWith(".PBN", StringComparison.OrdinalIgnoreCase))
                {
                    upper = upper.Substring(0, upper.Length - 4);
                }

                byUpper[upper] = index;
            }

            public int FindPlanByName(string name)
            {
                int index;

                if (byUpper.TryGetValue(name, out index))
                {
                    return index;
                }

                // The retail EngineObject plan tables hold the authored mixed-case names;
                // plan_graph.py shows the shipped references are CarryRok, Defense, Human, OffWing
                // and Sideln. This resolver maps them onto the shipped files and records a miss.
                Misses.Add(name);

                return -1;
            }
        }

        // ---- coverage sweep -------------------------------------------------------------------

        private static string RunCoverage(List<LoadedPlan> plans, ShippedPlanNames names, List<string> failures)
        {
            int instantiated = 0;
            int totalBehaviors = 0;
            int expectedBehaviors = 0;
            int globalCollision = 0;
            int globalMessage = 0;
            int messageDispatched = 0;
            int collisionDispatched = 0;
            int timeouts = 0;
            int agendaAdds = 0;
            int agendaRemoves = 0;
            int changes = 0;
            int exitBehaviors = 0;
            int timedMessages = 0;
            int tickSorts = 0;
            int postUpdateCalls = 0;
            int teardowns = 0;
            HashSet<int> actionIds = new HashSet<int>();
            HashSet<int> decisionIds = new HashSet<int>();
            HashSet<int> behaviorParamIds = new HashSet<int>();
            HashSet<int> interactorParamIds = new HashSet<int>();
            HashSet<string> unsupported = new HashSet<string>();
            List<string> sweepErrors = new List<string>();
            int unsupportedCalls = 0;

            for (int index = 0; index < plans.Count; index++)
            {
                LoadedPlan plan = plans[index];

                try
                {
                    int expectedColl = 0;
                    int expectedMsg = 0;
                    int expectedBehaviorCount = 0;

                    for (int top = 0; top < plan.Program.TopLevelNodes.Length; top++)
                    {
                        PlanNode node = plan.Program.TopLevelNodes[top];

                        if (node.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
                        {
                            expectedBehaviorCount++;
                        }
                        else if (node.Opcode == PlanOpcodes.OpcodeGlobalInteractors)
                        {
                            for (int child = 0; child < node.ChildCount; child++)
                            {
                                int category = node.Args[child].Node.Category;

                                if (category == (int)PlanOpcodes.Category.CollisionInteractor)
                                {
                                    expectedColl++;
                                }
                                else if (category == (int)PlanOpcodes.Category.MessageInteractor)
                                {
                                    expectedMsg++;
                                }
                            }
                        }
                    }

                    expectedBehaviors += expectedBehaviorCount;

                    PlanVmRecordingHost host = new PlanVmRecordingHost((short)0, PlanArgResolver.ForSlot(0), names);
                    host.HasPhysicsState = true;
                    PlanState state = new PlanState(host, plan.Image.Name);
                    state.SetAndInit(plan.Image);

                    if (!state.Instantiate())
                    {
                        throw new PlanVmException("Instantiate returned false.");
                    }

                    instantiated++;
                    globalCollision += expectedColl;
                    globalMessage += expectedMsg;

                    int actualBehaviors = 0;
                    StringBuilder chainNames = new StringBuilder();

                    for (PlanBehavior behavior = state.Head; behavior != null; behavior = behavior.Standby)
                    {
                        actualBehaviors++;

                        if (actualBehaviors > 1)
                        {
                            chainNames.Append(',');
                        }

                        chainNames.Append(behavior.Name);
                    }

                    totalBehaviors += actualBehaviors;

                    if (actualBehaviors != expectedBehaviorCount)
                    {
                        failures.Add(plan.Image.Name + " instantiated " + actualBehaviors + " behaviours, compiled " + expectedBehaviorCount);
                    }

                    if (state.InteractorCount != expectedColl)
                    {
                        failures.Add(plan.Image.Name + " global collision interactors " + state.InteractorCount + ", counted " + expectedColl);
                    }

                    // Every message interactor's Enable script, then every collision interactor's.
                    for (PlanBehavior behavior = state.Head; behavior != null; behavior = behavior.Standby)
                    {
                        for (PlanInteractor interactor = behavior.MsgInteractors; interactor != null; interactor = interactor.Next)
                        {
                            try
                            {
                                short decisionState = DecisionLog.Fresh;
                                ((PlanMessageInteractor)interactor).Dispatch(0, ref decisionState, host);
                                messageDispatched++;
                            }
                            catch (PlanVmUnsupportedHandlerException unsupportedHandler)
                            {
                                unsupported.Add(unsupportedHandler.HandlerKind + ":" + unsupportedHandler.HandlerId);
                                unsupportedCalls++;
                            }
                            catch (Exception exception)
                            {
                                sweepErrors.Add(plan.Image.Name + " msg script: " + Flatten(exception.Message));
                            }
                        }

                        for (PlanInteractor interactor = behavior.CollInteractors; interactor != null; interactor = interactor.Next)
                        {
                            try
                            {
                                short outcome = DecisionLog.Fresh;
                                ((PlanCollisionInteractor)interactor).Process(0, ref outcome, 0, host);
                                collisionDispatched++;
                            }
                            catch (PlanVmUnsupportedHandlerException unsupportedHandler)
                            {
                                unsupported.Add(unsupportedHandler.HandlerKind + ":" + unsupportedHandler.HandlerId);
                                unsupportedCalls++;
                            }
                            catch (Exception exception)
                            {
                                sweepErrors.Add(plan.Image.Name + " collision script: " + Flatten(exception.Message));
                            }
                        }
                    }

                    for (PlanInteractor interactor = state.MsgInteractors; interactor != null; interactor = interactor.Next)
                    {
                        try
                        {
                            short decisionState = DecisionLog.Fresh;
                            ((PlanMessageInteractor)interactor).Dispatch(0, ref decisionState, host);
                            messageDispatched++;
                        }
                        catch (PlanVmUnsupportedHandlerException unsupportedHandler)
                        {
                            unsupported.Add(unsupportedHandler.HandlerKind + ":" + unsupportedHandler.HandlerId);
                            unsupportedCalls++;
                        }
                        catch (Exception exception)
                        {
                            sweepErrors.Add(plan.Image.Name + " global msg script: " + Flatten(exception.Message));
                        }
                    }

                    for (PlanInteractor interactor = state.CollInteractors; interactor != null; interactor = interactor.Next)
                    {
                        try
                        {
                            short outcome = DecisionLog.Fresh;
                            ((PlanCollisionInteractor)interactor).Process(0, ref outcome, 0, host);
                            collisionDispatched++;
                        }
                        catch (PlanVmUnsupportedHandlerException unsupportedHandler)
                        {
                            unsupported.Add(unsupportedHandler.HandlerKind + ":" + unsupportedHandler.HandlerId);
                            unsupportedCalls++;
                        }
                        catch (Exception exception)
                        {
                            sweepErrors.Add(plan.Image.Name + " global collision script: " + Flatten(exception.Message));
                        }
                    }

                    state.CollisionRearmTicks = 0;

                    for (int tick = 0; tick < 3; tick++)
                    {
                        state.PostUpdateCleanup();
                        postUpdateCalls++;
                    }

                    timeouts += host.Timeouts.Count;
                    agendaAdds += host.AgendaAdds.Count;
                    agendaRemoves += host.AgendaRemoves.Count;
                    changes += host.Changes.Count;
                    exitBehaviors += host.ExitBehaviorCount;
                    timedMessages += host.TimedMessageCount;
                    tickSorts += host.TickAndSortCount;

                    for (int at = 0; at < host.Actions.Count; at++)
                    {
                        actionIds.Add(host.Actions[at]);
                    }

                    for (int at = 0; at < host.Decisions.Count; at++)
                    {
                        decisionIds.Add(host.Decisions[at]);
                    }

                    for (int at = 0; at < host.BehaviorParams.Count; at++)
                    {
                        behaviorParamIds.Add(host.BehaviorParams[at]);
                    }

                    for (int at = 0; at < host.InteractorParams.Count; at++)
                    {
                        interactorParamIds.Add(host.InteractorParams[at]);
                    }

                    state.Teardown();
                    teardowns++;
                }
                catch (PlanVmUnsupportedHandlerException unsupportedHandler)
                {
                    unsupported.Add(unsupportedHandler.HandlerKind + ":" + unsupportedHandler.HandlerId);
                    unsupportedCalls++;

                    // The plan instantiated but its setup called the documented missing handler.
                    instantiated++;
                }
                catch (Exception exception)
                {
                    failures.Add("coverage " + plan.Image.Name + ": " + Flatten(exception.Message));
                }
            }

            for (int index = 0; index < sweepErrors.Count && index < 8; index++)
            {
                failures.Add("sweep " + sweepErrors[index]);
            }

            if (instantiated != ExpectedPlanCount)
            {
                failures.Add("coverage instantiated " + instantiated + ", expected " + ExpectedPlanCount);
            }

            if (totalBehaviors != ExpectedCoverageBehaviors || expectedBehaviors != ExpectedCoverageBehaviors)
            {
                failures.Add("coverage behaviours " + totalBehaviors + "/" + expectedBehaviors + ", expected " + ExpectedCoverageBehaviors);
            }

            if (globalCollision != ExpectedCoverageGlobalCollision || globalMessage != ExpectedCoverageGlobalMessage)
            {
                failures.Add("coverage global interactors " + globalCollision + "/" + globalMessage + ", expected " + ExpectedCoverageGlobalCollision + "/" + ExpectedCoverageGlobalMessage);
            }

            if (messageDispatched != ExpectedCoverageMessageDispatched || collisionDispatched != ExpectedCoverageCollisionDispatched)
            {
                failures.Add("coverage dispatched " + messageDispatched + "/" + collisionDispatched + ", expected " + ExpectedCoverageMessageDispatched + "/" + ExpectedCoverageCollisionDispatched);
            }

            if (unsupportedCalls != ExpectedCoverageUnsupportedCalls || !unsupported.Contains("action:14"))
            {
                failures.Add("coverage unsupported handlers [" + JoinInts(unsupported) + "] calls=" + unsupportedCalls + ", expected [action:14] once");
            }

            if (teardowns != instantiated)
            {
                failures.Add("coverage teardowns " + teardowns + " != instantiated " + instantiated);
            }

            return "UBLADE3_PLANVM_COVERAGE instantiated=" + instantiated + "/" + ExpectedPlanCount +
                   " behaviours=" + totalBehaviors + "/" + expectedBehaviors +
                   " globalColl=" + globalCollision + " globalMsg=" + globalMessage +
                   " msgDispatched=" + messageDispatched + " collDispatched=" + collisionDispatched +
                   " actionIds=" + actionIds.Count + "/135 decisions=" + decisionIds.Count + "/139" +
                   " behaviorParams=" + behaviorParamIds.Count + "/17 interactorParams=" + interactorParamIds.Count + "/2" +
                   " timeouts=" + timeouts + " agendaAdds=" + agendaAdds + " agendaRemoves=" + agendaRemoves +
                   " changePlan=" + changes + " exitBehavior=" + exitBehaviors +
                   " timedMessages=" + timedMessages + " tickSort=" + tickSorts + " postUpdate=" + postUpdateCalls +
                   " teardowns=" + teardowns +
                   " unsupported=[" + JoinInts(unsupported) + "] unsupportedCalls=" + unsupportedCalls;
        }

        private static string JoinInts(HashSet<string> values)
        {
            List<string> sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);

            return string.Join(",", sorted.ToArray());
        }

        // ---- known-plan traces ----------------------------------------------------------------

        private static void RunTraces(Dictionary<string, LoadedPlan> byName, ShippedPlanNames names, List<string> traceLines, List<string> failures)
        {
            RunMinimalTrace(byName, names, traceLines, failures);
            RunCoachTrace(byName, names, traceLines, failures);
            RunCarryRokTrace(byName, traceLines, failures);
            RunCrashDummyTrace(byName, names, traceLines, failures);
            RunDefenseTrace(byName, traceLines, failures);
            RunListenTrace(byName, names, traceLines, failures);
        }

        private static LoadedPlan PlanOf(Dictionary<string, LoadedPlan> byName, string name, List<string> failures)
        {
            LoadedPlan plan;

            if (!byName.TryGetValue(name, out plan))
            {
                failures.Add("trace " + name + ": plan not loaded");
                return null;
            }

            return plan;
        }

        private static bool Expect(List<string> failures, string where, bool condition, string detail)
        {
            if (!condition)
            {
                failures.Add("trace " + where + ": " + detail);
            }

            return condition;
        }

        private static void WalkNodes(PlanNode node, Action<PlanNode> visit)
        {
            visit(node);

            for (int index = 0; index < node.ChildCount; index++)
            {
                PlanOperand operand = node.Args[index];

                if (operand != null && operand.Kind == PlanOperandKind.Node)
                {
                    WalkNodes(operand.Node, visit);
                }
            }
        }

        private static List<PlanNode> FindNodes(LoadedPlan plan, Predicate<PlanNode> predicate)
        {
            List<PlanNode> found = new List<PlanNode>();

            for (int index = 0; index < plan.Program.TopLevelNodes.Length; index++)
            {
                WalkNodes(plan.Program.TopLevelNodes[index], delegate(PlanNode node)
                {
                    if (predicate(node))
                    {
                        found.Add(node);
                    }
                });
            }

            return found;
        }

        private static PreparedPlan Prepare(LoadedPlan plan, IPlanNameResolver names)
        {
            PlanVmRecordingHost host = new PlanVmRecordingHost((short)0, PlanArgResolver.ForSlot(0), names);
            host.HasPhysicsState = true;
            PlanState state = new PlanState(host, plan.Image.Name);
            state.SetAndInit(plan.Image);

            if (!state.Instantiate())
            {
                throw new PlanVmException("Instantiate returned false for " + plan.Image.Name);
            }

            PreparedPlan prepared = new PreparedPlan();
            prepared.Host = host;
            prepared.State = state;

            return prepared;
        }

        private static void RunMinimalTrace(Dictionary<string, LoadedPlan> byName, ShippedPlanNames names, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "MINIMAL.PBN", failures);

            if (plan == null)
            {
                return;
            }

            bool ok = Expect(failures, "MINIMAL", plan.Stats.Behaviors == 1, "behavior count " + plan.Stats.Behaviors);
            PlanNode declare = plan.Program.TopLevelNodes[0];
            ok &= Expect(failures, "MINIMAL", declare.Opcode == PlanOpcodes.OpcodeDeclareBehavior, "first top-level node is not a behaviour");
            ok &= Expect(failures, "MINIMAL", declare.Args[0].IntValue == 6, "kind is not SetInPlace(6)");
            ok &= Expect(failures, "MINIMAL", declare.Args[1].Text == "SitThere", "name is not SitThere");

            PreparedPlan prepared = Prepare(plan, names);
            prepared.State.ActivateBehavior(0);
            ok &= Expect(failures, "MINIMAL", prepared.State.Current.Name == "SitThere" && prepared.State.Index == 0, "activation did not settle on SitThere");
            ok &= Expect(failures, "MINIMAL", prepared.Host.TickAndSortCount == 1 && prepared.Host.TimedMessageCount == 1, "activation did not tick/send once");

            traceLines.Add("UBLADE3_PLANVM_TRACE MINIMAL ok=" + (ok ? "1" : "0") + " behaviors=1 kinds=[SetInPlace] names=[SitThere] activate=0 current=SitThere passOn=0");
        }

        private static void RunCoachTrace(Dictionary<string, LoadedPlan> byName, ShippedPlanNames names, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "COACH.PBN", failures);

            if (plan == null)
            {
                return;
            }

            bool ok = Expect(failures, "COACH", plan.Stats.Behaviors == 2, "behavior count " + plan.Stats.Behaviors);
            List<string> behaviorNames = new List<string>();
            List<int> behaviorKinds = new List<int>();

            for (int index = 0; index < plan.Program.TopLevelNodes.Length; index++)
            {
                PlanNode node = plan.Program.TopLevelNodes[index];

                if (node.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
                {
                    behaviorNames.Add(node.Args[1].Text);
                    behaviorKinds.Add(node.Args[0].IntValue);
                }
            }

            ok &= Expect(failures, "COACH", behaviorNames.Count == 2 && behaviorNames[0] == "SitThere" && behaviorNames[1] == "CoachTheTeam", "names " + string.Join(",", behaviorNames.ToArray()));
            ok &= Expect(failures, "COACH", behaviorKinds.Count == 2 && behaviorKinds[0] == 6 && behaviorKinds[1] == 21, "kinds " + string.Join(",", behaviorKinds.ConvertAll(delegate(int value) { return value.ToString(CultureInfo.InvariantCulture); }).ToArray()));

            PreparedPlan prepared = Prepare(plan, names);
            prepared.Host.ResetTelemetry();
            prepared.State.ActivateBehavior(0);

            ok &= Expect(failures, "COACH", prepared.State.Current.Name == "CoachTheTeam", "pass-on did not reach CoachTheTeam");
            ok &= Expect(failures, "COACH", prepared.Host.TickAndSortCount == 2 && prepared.Host.TimedMessageCount == 2, "pass-on tick/send counts " + prepared.Host.TickAndSortCount + "/" + prepared.Host.TimedMessageCount);

            PlanBehavior coach = prepared.State.Current;
            List<int> messageIds = new List<int>();

            for (PlanInteractor interactor = coach.MsgInteractors; interactor != null; interactor = interactor.Next)
            {
                messageIds.Add(interactor.OwnSlotOrMessage.Value);
            }

            ok &= Expect(failures, "COACH", messageIds.Count == 2 && messageIds[0] == 76 && messageIds[1] == 57, "message ids " + string.Join(",", messageIds.ConvertAll(delegate(int value) { return value.ToString(CultureInfo.InvariantCulture); }).ToArray()));

            prepared.Host.ResetTelemetry();
            PlanInteractor message = coach.MsgInteractors;
            short decisionState = DecisionLog.Fresh;
            ((PlanMessageInteractor)message).Dispatch(0, ref decisionState, prepared.Host);
            message = message.Next;
            decisionState = DecisionLog.Fresh;
            ((PlanMessageInteractor)message).Dispatch(0, ref decisionState, prepared.Host);
            ok &= Expect(failures, "COACH", prepared.Host.AgendaAdds.Count == 1 && prepared.Host.AgendaAdds[0] == 33, "agenda add is not CCheckAI(33)");
            ok &= Expect(failures, "COACH", prepared.Host.AgendaRemoves.Count == 1 && prepared.Host.AgendaRemoves[0] == 33, "agenda remove is not CCheckAI(33)");

            traceLines.Add("UBLADE3_PLANVM_TRACE COACH ok=" + (ok ? "1" : "0") + " behaviors=2 kinds=[SetInPlace,CoachTeam] names=[SitThere,CoachTheTeam] activate=0 current=CoachTheTeam passOn=1 msgIds=[76,57] agendaAdd=[33] agendaRemove=[33]");
        }

        private static void RunCarryRokTrace(Dictionary<string, LoadedPlan> byName, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "CARRYROK.PBN", failures);

            if (plan == null)
            {
                return;
            }

            bool ok = Expect(failures, "CARRYROK", plan.Stats.Behaviors == 13, "behavior count " + plan.Stats.Behaviors);

            int goForGoal = -1;
            int declared = 0;

            for (int index = 0; index < plan.Program.TopLevelNodes.Length; index++)
            {
                PlanNode node = plan.Program.TopLevelNodes[index];

                if (node.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
                {
                    if (node.Args[1].Text == "GoForGoal")
                    {
                        goForGoal = declared;
                    }

                    declared++;
                }
            }

            ok &= Expect(failures, "CARRYROK", goForGoal == 1, "GoForGoal index " + goForGoal);

            int dataZeroTimers = 0;
            List<PlanNode> timers = FindNodes(plan, delegate(PlanNode node)
            {
                return node.Opcode == PlanOpcodes.OpcodeSetTimeoutMsg && node.ChildCount == 3;
            });

            for (int index = 0; index < timers.Count; index++)
            {
                PlanNode timer = timers[index];

                if (timer.Args[0].IntValue == 15 && timer.Args[1].IntValue == 69 && timer.Args[2].Raw.IsPronoun && timer.Args[2].Raw.Low == 0)
                {
                    dataZeroTimers++;
                }
            }

            ok &= Expect(failures, "CARRYROK", dataZeroTimers >= 1, "GoForGoal SET_TIMEOUTMSG 15, TimeOutMessage, Me not found");
            ok &= Expect(failures, "CARRYROK", plan.Stats.ActionIds.Contains(5) && plan.Stats.DecisionIds.Contains(50), "expected GoForGoal actions/decisions missing");

            traceLines.Add("UBLADE3_PLANVM_TRACE CARRYROK ok=" + (ok ? "1" : "0") + " behaviors=13 goForGoal=1 data0Timer=[15,69,PRN_ME]=" + dataZeroTimers + " defect=MESSAGE_DATA0");
        }

        private static void RunCrashDummyTrace(Dictionary<string, LoadedPlan> byName, ShippedPlanNames names, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "CRSHDUM0.PBN", failures);

            if (plan == null)
            {
                return;
            }

            List<PlanNode> action14 = FindNodes(plan, delegate(PlanNode node)
            {
                return node.Category == (int)PlanOpcodes.Category.Action && node.Member == (int)PlanOpcodes.ActionMember.DoAction && node.ChildCount >= 1 && node.Args[0].IntValue == 14;
            });

            bool ok = Expect(failures, "CRSHDUM0", action14.Count == 1, "id_ACF_CrashTestSetup(14) site count " + action14.Count);
            ok &= Expect(failures, "CRSHDUM0", !PlanOpcodes.IsSupportedActionId(14), "action 14 unexpectedly supported");

            PreparedPlan prepared = Prepare(plan, names);
            bool threw = false;

            for (PlanInteractor interactor = prepared.State.MsgInteractors; interactor != null && !threw; interactor = interactor.Next)
            {
                if (interactor.OwnSlotOrMessage.Value != 77)
                {
                    continue;
                }

                try
                {
                    short decisionState = DecisionLog.Fresh;
                    ((PlanMessageInteractor)interactor).Dispatch(0, ref decisionState, prepared.Host);
                }
                catch (PlanVmUnsupportedHandlerException unsupported)
                {
                    threw = unsupported.HandlerId == 14 && unsupported.HandlerKind == "action";
                }
            }

            ok &= Expect(failures, "CRSHDUM0", threw, "id_MSG_Test did not raise the missing CrashTestSetup handler");

            traceLines.Add("UBLADE3_PLANVM_TRACE CRSHDUM0 ok=" + (ok ? "1" : "0") + " action14=ACF_CrashTestSetup unsupported=1 msg=Test(77)");
        }

        private static void RunDefenseTrace(Dictionary<string, LoadedPlan> byName, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "DEFENSE.PBN", failures);

            if (plan == null)
            {
                return;
            }

            List<PlanNode> coll = FindNodes(plan, delegate(PlanNode node)
            {
                return node.Opcode == PlanOpcodes.OpcodeSetCollisionInteractor && node.ChildCount >= 3 &&
                       node.Args[0].Raw.IsPronoun && (node.Args[0].Raw.Low == 2 || node.Args[0].Raw.Low == 3);
            });

            bool ok = Expect(failures, "DEFENSE", coll.Count == 6, "slot-pronoun collision interactors " + coll.Count + ", expected 6 (plan_bugs_skaters.md section 2)");

            traceLines.Add("UBLADE3_PLANVM_TRACE DEFENSE ok=" + (ok ? "1" : "0") + " slotPronounCollisionInteractors=6 defect=slot-used-as-COB");
        }

        private static void RunListenTrace(Dictionary<string, LoadedPlan> byName, ShippedPlanNames names, List<string> traceLines, List<string> failures)
        {
            LoadedPlan plan = PlanOf(byName, "LISTEN.PBN", failures);

            if (plan == null)
            {
                return;
            }

            PreparedPlan record = Prepare(plan, names);
            PlanMessageInteractor endOfPeriod = null;

            for (PlanInteractor interactor = record.State.MsgInteractors; interactor != null; interactor = interactor.Next)
            {
                if (interactor.OwnSlotOrMessage.Value == 135)
                {
                    endOfPeriod = (PlanMessageInteractor)interactor;
                    break;
                }
            }

            bool ok = Expect(failures, "LISTEN", endOfPeriod != null, "global id_MSG_EndOfPeriod(135) handler missing");

            if (endOfPeriod == null)
            {
                traceLines.Add("UBLADE3_PLANVM_TRACE LISTEN ok=0");
                return;
            }

            record.Host.ResetTelemetry();
            short recorded = DecisionLog.Fresh;
            endOfPeriod.Dispatch(0, ref recorded, record.Host);
            List<string> recordedEvents = new List<string>(record.Host.Events);
            List<int> recordedActions = new List<int>(record.Host.Actions);
            ok &= Expect(failures, "LISTEN", recordedActions.Count == 2 && recordedActions[0] == 7 && recordedActions[1] == 5, "recording actions " + string.Join(",", recordedActions.ConvertAll(delegate(int value) { return value.ToString(CultureInfo.InvariantCulture); }).ToArray()));
            ok &= Expect(failures, "LISTEN", record.Host.Decisions.Count == 1 && record.Host.Decisions[0] == 129, "recording decisions not WhoWonTheGame(129)");
            ok &= Expect(failures, "LISTEN", DecisionLog.IsRecording(recorded), "decision log did not record");

            PreparedPlan replay = Prepare(plan, names);
            PlanMessageInteractor replayInteractor = null;

            for (PlanInteractor interactor = replay.State.MsgInteractors; interactor != null; interactor = interactor.Next)
            {
                if (interactor.OwnSlotOrMessage.Value == 135)
                {
                    replayInteractor = (PlanMessageInteractor)interactor;
                    break;
                }
            }

            replay.Host.ResetTelemetry();
            short replayWord = unchecked((short)(recorded & 0x3FFF));
            replayInteractor.Dispatch(0, ref replayWord, replay.Host);
            ok &= Expect(failures, "LISTEN", replay.Host.Actions.Count == 2 && replay.Host.Actions[0] == 7 && replay.Host.Actions[1] == 5, "replay actions differ");
            ok &= Expect(failures, "LISTEN", replay.Host.Decisions.Count == 0, "replay ran a decision handler");
            ok &= Expect(failures, "LISTEN", replayWord == 0, "replay log did not drain");
            ok &= Expect(failures, "LISTEN", recordedEvents.Count > 0, "no recorded events");

            traceLines.Add("UBLADE3_PLANVM_TRACE LISTEN ok=" + (ok ? "1" : "0") + " msg=EndOfPeriod(135) record.actions=[PlaySound,SendMessage] record.decisions=[WhoWonTheGame:0] replay.same=1 log=0x" + ((ushort)recorded).ToString("x4", CultureInfo.InvariantCulture));
        }
    }
}
