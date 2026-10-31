using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UBlade3.PlanVM
{
    /// <summary>FNV-1a 64 over a byte stream; the Probe's digest, the same function DataProbe uses.</summary>
    public static class PlanVmDigest
    {
        public const ulong Offset = 0xCBF29CE484222325UL;

        public const ulong Prime = 0x100000001B3UL;

        public static ulong Append(ulong state, string ascii)
        {
            for (int index = 0; index < ascii.Length; index++)
            {
                state ^= (byte)ascii[index];
                state *= Prime;
            }

            return state;
        }

        public static string Hex(ulong value)
        {
            return value.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The structural report for one compiled plan: instruction/operand counts, the opcode and leaf
    /// histograms, the handler ids the data names, and a canonical digest over the node tree. The
    /// digest is over the file words and the tree shape, not over any resolved engine value, so it is
    /// stable for every actor slot.
    /// </summary>
    public sealed class PlanStatistics
    {
        public string Name;

        public string Path;

        public int Bytes;

        public int DeclaredWordCount;

        public int Words;

        public int ConsumedWords;

        public int Nodes;

        public int Leaves;

        public int Strings;

        public int Behaviors;

        public int Globals;

        public int Locals;

        public int CollInteractors;

        public int MsgInteractors;

        public int EnableBlocks;

        public int Decisions;

        public int Actions;

        public int AgendaNodes;

        public int NamedQuirk;

        public int MaxDepth;

        public int UnknownOpcodes;

        public int UnresolvedPlanReferences;

        public ulong Digest;

        public readonly Dictionary<int, int> OpcodeHistogram = new Dictionary<int, int>();

        public readonly Dictionary<string, int> LeafKindHistogram = new Dictionary<string, int>();

        public readonly Dictionary<int, int> PronounHistogram = new Dictionary<int, int>();

        public readonly HashSet<int> ActionIds = new HashSet<int>();

        public readonly HashSet<int> DecisionIds = new HashSet<int>();

        public readonly HashSet<int> BehaviorParamIds = new HashSet<int>();

        public readonly HashSet<int> InteractorParamIds = new HashSet<int>();

        public readonly HashSet<int> AgendaItemIds = new HashSet<int>();

        public readonly List<string> PlanReferences = new List<string>();

        public string CanonicalText;

        public string PlanLine()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_PLANVM_PLAN name=").Append(Name);
            text.Append(" words=").Append(Words);
            text.Append(" nodes=").Append(Nodes);
            text.Append(" leaves=").Append(Leaves);
            text.Append(" strings=").Append(Strings);
            text.Append(" behaviors=").Append(Behaviors);
            text.Append(" globals=").Append(Globals);
            text.Append(" locals=").Append(Locals);
            text.Append(" coll=").Append(CollInteractors);
            text.Append(" msg=").Append(MsgInteractors);
            text.Append(" enable=").Append(EnableBlocks);
            text.Append(" decisions=").Append(Decisions);
            text.Append(" actions=").Append(Actions);
            text.Append(" agenda=").Append(AgendaNodes);
            text.Append(" namedQ=").Append(NamedQuirk);
            text.Append(" maxdepth=").Append(MaxDepth);
            text.Append(" unknown=").Append(UnknownOpcodes);
            text.Append(" digest=").Append(PlanVmDigest.Hex(Digest));

            return text.ToString();
        }
    }

    /// <summary>
    /// Walks a parsed PlanProgram and produces the structural report and canonical digest. The walk
    /// mirrors the decompilation tool tools/plan_graph.py: a leaf prints as its raw word, a string as
    /// its raw character words, a node as its opcode and 16-bit child count, and END_PLAN as itself.
    /// </summary>
    public static class PlanAnalyzer
    {
        public static PlanStatistics Analyze(PlanFileImage image, PlanProgram program)
        {
            PlanStatistics statistics = new PlanStatistics();
            statistics.Name = image.Name;
            statistics.Path = image.Path;
            statistics.Bytes = unchecked((int)image.ByteLength);
            statistics.DeclaredWordCount = image.DeclaredWordCount;
            statistics.Words = image.WordCount;
            statistics.ConsumedWords = program.ConsumedWords;
            statistics.NamedQuirk = program.NamedInteractorCount;

            List<string> canonical = new List<string>();

            for (int index = 0; index < program.TopLevelNodes.Length; index++)
            {
                WalkNode(program.TopLevelNodes[index], 0, statistics, canonical);
            }

            for (int index = 0; index < program.TopLevelNodes.Length; index++)
            {
                PlanNode node = program.TopLevelNodes[index];

                if (node.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
                {
                    statistics.Behaviors++;
                }
                else if (node.Opcode == PlanOpcodes.OpcodeGlobalInteractors)
                {
                    statistics.Globals++;
                }
                else if (node.Opcode == PlanOpcodes.OpcodeDeclareLocalReals)
                {
                    statistics.Locals += node.ChildCount;
                }
            }

            statistics.CanonicalText = BuildCanonicalText(image, program, canonical);
            statistics.Digest = PlanVmDigest.Append(PlanVmDigest.Offset, statistics.CanonicalText);

            return statistics;
        }

        /// <summary>The exact text the digest covers: a header, one line per node/leaf/string in preorder, then the word walk.</summary>
        public static string BuildCanonicalText(PlanFileImage image, PlanProgram program, List<string> canonical)
        {
            StringBuilder text = new StringBuilder();
            text.Append("PLAN\t").Append(image.Name).Append('\t').Append(image.DeclaredWordCount).Append('\n');

            for (int index = 0; index < canonical.Count; index++)
            {
                if (index > 0)
                {
                    text.Append('\n');
                }

                text.Append(canonical[index]);
            }

            text.Append("\nENDWALK\t").Append(program.ConsumedWords).Append('\t').Append(image.WordCount).Append('\n');

            return text.ToString();
        }

        private static void WalkNode(PlanNode node, int depth, PlanStatistics statistics, List<string> canonical)
        {
            statistics.Nodes++;
            statistics.MaxDepth = Math.Max(statistics.MaxDepth, depth);
            Increment(statistics.OpcodeHistogram, node.Opcode);

            if (!PlanOpcodes.IsKnownOpcode(node.Opcode))
            {
                statistics.UnknownOpcodes++;
            }

            if (node.IsEndPlan)
            {
                canonical.Add("END");
                return;
            }

            canonical.Add("NODE\t" + ((uint)node.Opcode).ToString("x8") + "\t" + (node.ArgCount & 0xFFFF).ToString(CultureInfo.InvariantCulture));

            int category = node.Category;

            if (category == (int)PlanOpcodes.Category.CollisionInteractor)
            {
                statistics.CollInteractors++;
            }
            else if (category == (int)PlanOpcodes.Category.MessageInteractor)
            {
                statistics.MsgInteractors++;
            }
            else if (node.Opcode == PlanOpcodes.OpcodeEnable)
            {
                statistics.EnableBlocks++;
            }
            else if (category == (int)PlanOpcodes.Category.Decide)
            {
                statistics.Decisions++;

                if (node.ChildCount > 0 && node.Args[0].Kind == PlanOperandKind.Leaf)
                {
                    statistics.DecisionIds.Add(node.Args[0].IntValue);
                }
            }
            else if (category == (int)PlanOpcodes.Category.Action)
            {
                if (node.Member == (int)PlanOpcodes.ActionMember.DoAction || node.Member == (int)PlanOpcodes.ActionMember.DoLocalAction || node.Member == (int)PlanOpcodes.ActionMember.DoNetAction)
                {
                    if (node.ChildCount >= 1)
                    {
                        statistics.Actions++;

                        if (node.Args[0].Kind == PlanOperandKind.Leaf)
                        {
                            statistics.ActionIds.Add(node.Args[0].IntValue);
                        }
                    }
                }
                else if (node.Member == (int)PlanOpcodes.ActionMember.SetInteractorParam && node.ChildCount >= 1 && node.Args[0].Kind == PlanOperandKind.Leaf)
                {
                    statistics.InteractorParamIds.Add(node.Args[0].IntValue);
                }
            }
            else if (node.Opcode == PlanOpcodes.OpcodeAddAgendaItem || node.Opcode == PlanOpcodes.OpcodeRemoveAgendaItem)
            {
                statistics.AgendaNodes++;

                if (node.ChildCount >= 1 && node.Args[0].Kind == PlanOperandKind.Leaf)
                {
                    statistics.AgendaItemIds.Add(node.Args[0].IntValue);
                }
            }
            else if (node.Opcode == PlanOpcodes.OpcodeSetBehaviorParam && node.ChildCount >= 1 && node.Args[0].Kind == PlanOperandKind.Leaf)
            {
                statistics.BehaviorParamIds.Add(node.Args[0].IntValue);
            }

            for (int index = 0; index < node.ChildCount; index++)
            {
                PlanOperand operand = node.Args[index];

                if (operand == null)
                {
                    throw new PlanVmException("Compiled node has a null operand.");
                }

                if (operand.Kind == PlanOperandKind.Node)
                {
                    WalkNode(operand.Node, depth + 1, statistics, canonical);
                }
                else if (operand.Kind == PlanOperandKind.String)
                {
                    statistics.Strings++;

                    StringBuilder line = new StringBuilder("STR\t");

                    for (int word = 0; word < operand.StringWords.Length; word++)
                    {
                        line.Append((operand.StringWords[word].Value & 0xFF).ToString("x2"));
                    }

                    canonical.Add(line.ToString());

                    if (operand.IsPlanReference)
                    {
                        statistics.PlanReferences.Add(operand.Text);

                        if (operand.Word.Value < 0)
                        {
                            statistics.UnresolvedPlanReferences++;
                        }
                    }
                }
                else
                {
                    statistics.Leaves++;
                    string kind = ClassifyLeaf(operand.Raw.Value);
                    Increment(statistics.LeafKindHistogram, kind);
                    canonical.Add("LEAF\t" + ((uint)operand.Raw.Value).ToString("x8"));

                    if (operand.Raw.IsPronoun)
                    {
                        Increment(statistics.PronounHistogram, operand.Raw.Low);
                    }
                }
            }
        }

        /// <summary>The decomp tools/plan_graph.py leaf naming order: pronoun, local real, interactor name, token, small int, float bits, else hex.</summary>
        public static string ClassifyLeaf(int raw)
        {
            int high = (raw >> 16) & 0xFFFF;

            if (high == PlanWord.PronounTag)
            {
                return "pronoun";
            }

            if (high == PlanWord.LocalRealTag)
            {
                return "localreal";
            }

            if (high == PlanWord.InteractorNameTag)
            {
                return "interactoref";
            }

            if (IsSentinel(raw))
            {
                return "sentinel";
            }

            if (raw > -0x10000 && raw < 0x800000)
            {
                return "int";
            }

            float value = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);

            if (!float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 1e30f)
            {
                string text = value.ToString("R", CultureInfo.InvariantCulture);
                float parsed;

                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    if (BitConverter.ToInt32(BitConverter.GetBytes(parsed), 0) == raw)
                    {
                        return "float";
                    }
                }
            }

            return "hex";
        }

        private static bool IsSentinel(int raw)
        {
            return raw == PlanWord.TokenStraightAhead || raw == PlanWord.TokenTemporarily || raw == PlanWord.TokenMessageData ||
                   raw == PlanWord.TokenNoBehaviourChange || raw == PlanWord.TokenPreviousBehaviour;
        }

        private static void Increment(Dictionary<int, int> table, int key)
        {
            int value;

            table.TryGetValue(key, out value);
            table[key] = value + 1;
        }

        private static void Increment(Dictionary<string, int> table, string key)
        {
            int value;

            table.TryGetValue(key, out value);
            table[key] = value + 1;
        }
    }
}
