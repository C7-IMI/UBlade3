using System;
using System.Collections.Generic;
using System.Text;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// A compiled plan: the array PlanState::Compile (HYPERX 0x0047b2d0) builds from a .PBN stream.
    /// TopLevelNodes is that array, one node per top-level instruction including END_PLAN.
    /// </summary>
    public sealed class PlanProgram
    {
        public PlanFileImage Source;

        public PlanNode[] TopLevelNodes;

        public int WordCount;

        public int ConsumedWords;

        /// <summary>The count PlanState::Compile computes for m_nNamedInteractors (see the quirk in the parser).</summary>
        public int NamedInteractorCount;

        public int BehaviorDeclarationCount;

        public int GlobalInteractorBlockCount;

        public int LocalRealCount;

        public int MaxStringLength;

        public int StringsRead;

        public bool HasChangeOfPlan;
    }

    /// <summary>
    /// The PBN stream decoder: PlanState::ParseNodeStream (HYPERX 0x0047b430) and the top-level
    /// Compile loop (0x0047b2d0), ported exactly. A file is a word count then a stream of compound
    /// nodes: kPlanMarkerNode, opcode, child count (masked with 0x7fff), then that many children;
    /// each child is another compound, a kPlanMarkerString string (one char per word, zero-terminated),
    /// or a leaf. END_PLAN is the one compound with no child-count word.
    /// </summary>
    public static class PlanStreamParser
    {
        public static PlanProgram Parse(PlanFileImage image, IPlanArgResolver argResolver, IPlanNameResolver nameResolver)
        {
            PlanParserState state = new PlanParserState(image, argResolver, nameResolver);

            return state.ParseTopLevel();
        }
    }

    internal sealed class PlanParserState
    {
        private const int MaxPlanStringLength = 31;

        private readonly PlanWord[] words;

        private readonly IPlanArgResolver argResolver;

        private readonly IPlanNameResolver nameResolver;

        private int cursor;

        private int namedInteractors;

        private int maxStringLength;

        private int stringsRead;

        private bool sawChangeOfPlan;

        public PlanParserState(PlanFileImage image, IPlanArgResolver argResolver, IPlanNameResolver nameResolver)
        {
            words = image.Words;
            this.argResolver = argResolver;
            this.nameResolver = nameResolver;
        }

        public PlanProgram ParseTopLevel()
        {
            List<PlanNode> tops = new List<PlanNode>();
            bool planReference = false;

            for (;;)
            {
                if (cursor >= words.Length)
                {
                    // STRING: HYPERX 0x004c68c0
                    throw new PlanVmException("ERROR: No Instruction returned from DecodeArg at Top Level");
                }

                PlanOperand operand;
                bool isNode = ParseNode(1, ref planReference, out operand);

                if (!isNode)
                {
                    throw new PlanVmException("ERROR: No Instruction returned from DecodeArg at Top Level (top-level word at " + (cursor - 1) + " is a leaf)");
                }

                tops.Add(operand.Node);

                if (operand.Node.IsEndPlan)
                {
                    break;
                }
            }

            PlanProgram program = new PlanProgram();
            program.Source = null;
            program.TopLevelNodes = tops.ToArray();
            program.WordCount = words.Length;
            program.ConsumedWords = cursor;
            program.NamedInteractorCount = namedInteractors;
            program.MaxStringLength = maxStringLength;
            program.StringsRead = stringsRead;
            program.HasChangeOfPlan = sawChangeOfPlan;

            for (int i = 0; i < program.TopLevelNodes.Length; i++)
            {
                PlanNode node = program.TopLevelNodes[i];

                if (node.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
                {
                    program.BehaviorDeclarationCount++;
                }
                else if (node.Opcode == PlanOpcodes.OpcodeGlobalInteractors)
                {
                    program.GlobalInteractorBlockCount++;
                }
                else if (node.Opcode == PlanOpcodes.OpcodeDeclareLocalReals)
                {
                    program.LocalRealCount += node.ChildCount;
                }
            }

            return program;
        }

        /// <summary>Port of PlanState::ParseNodeStream. Returns true when the word opened a compound node.</summary>
        private bool ParseNode(int depth, ref bool planReference, out PlanOperand operand)
        {
            if (cursor >= words.Length)
            {
                throw new PlanVmException("Plan stream ended inside an instruction (word " + cursor + ").");
            }

            PlanWord word = words[cursor];

            if (word.Value == PlanWord.MarkerNode)
            {
                cursor++;

                if (cursor >= words.Length)
                {
                    throw new PlanVmException("Plan stream ends after a node marker (word " + (cursor - 1) + ").");
                }

                PlanNode node = new PlanNode();
                node.Opcode = words[cursor].Value;
                cursor++;

                if (node.Opcode == PlanWord.EndPlanOpcode)
                {
                    node.Args = new PlanOperand[0];
                    operand = PlanOperand.ForNode(node);

                    return true;
                }

                if (cursor >= words.Length)
                {
                    throw new PlanVmException("Plan stream ends before a node's child count (word " + (cursor - 1) + ").");
                }

                node.ArgCount = unchecked((short)words[cursor].Value);
                cursor++;

                int childCount = node.ChildCount;
                node.Args = childCount == 0 ? new PlanOperand[0] : new PlanOperand[childCount];

                for (int child = 0; child < childCount; child++)
                {
                    if (node.Category == (int)PlanOpcodes.Category.PlanChange && node.Member == (int)PlanOpcodes.PlanChangeMember.ChangeOfPlan)
                    {
                        planReference = true;
                    }
                    else if (node.Opcode == PlanOpcodes.OpcodeSetInteractorParam)
                    {
                        // PlanState::ParseNodeStream tests the PARENT node's opcode and first operand while
                        // walking its children, so a SET_INTERACTOR_PARAM counts once per child whose id is
                        // SetInteractorName. The decomp reads the first operand before it is parsed; 20XX's
                        // value-initialised array reads zero there, which this port follows. No shipped plan
                        // contains SET_INTERACTOR_PARAM, so the quirk does not affect the retail data.
                        if (child == 0 || (node.Args[0] != null && node.Args[0].Kind == PlanOperandKind.Leaf && node.Args[0].IntValue == 0))
                        {
                            namedInteractors++;
                        }
                    }

                    ParseNode(depth + 1, ref planReference, out node.Args[child]);
                }

                if (node.Opcode == PlanOpcodes.OpcodeChangeOfPlan)
                {
                    sawChangeOfPlan = true;
                }

                operand = PlanOperand.ForNode(node);

                return true;
            }

            if (word.Value == PlanWord.MarkerString)
            {
                cursor++;

                List<PlanWord> chars = new List<PlanWord>();
                StringBuilder text = new StringBuilder();

                while (cursor < words.Length && words[cursor].Value != 0)
                {
                    chars.Add(words[cursor]);
                    text.Append((char)(words[cursor].Value & 0xFF));
                    cursor++;
                }

                if (cursor >= words.Length)
                {
                    throw new PlanVmException("Plan string is not zero-terminated (word " + (cursor - 1) + ").");
                }

                cursor++;
                string literal = text.ToString();

                if (literal.Length > MaxPlanStringLength)
                {
                    // The original copies into char szName[32] in ParseNodeStream; longer strings are
                    // undefined behaviour there. No shipped plan exceeds 24 characters.
                    throw new PlanVmException("Plan string is " + literal.Length + " chars; ParseNodeStream's 32-byte buffer allows 31.");
                }

                if (literal.Length > maxStringLength)
                {
                    maxStringLength = literal.Length;
                }

                stringsRead++;

                PlanOperand stringOperand = PlanOperand.ForString(chars.ToArray(), literal);

                if (planReference)
                {
                    planReference = false;
                    stringOperand.IsPlanReference = true;
                    stringOperand.Word = new PlanWord(nameResolver == null ? -1 : nameResolver.FindPlanByName(literal));
                }

                operand = stringOperand;

                return false;
            }

            planReference = false;
            cursor++;

            PlanWord raw = word;

            if (raw.IsPronoun)
            {
                if (argResolver == null)
                {
                    throw new PlanVmException("Plan word is the pronoun " + PlanOpcodes.PronounName(raw.Low) + " but no argument resolver was supplied.");
                }

                operand = PlanOperand.ForLeaf(raw, new PlanWord(argResolver.ResolvePronoun(raw.Low, raw)));

                return false;
            }

            operand = PlanOperand.ForLeaf(raw, raw);

            return false;
        }
    }
}
