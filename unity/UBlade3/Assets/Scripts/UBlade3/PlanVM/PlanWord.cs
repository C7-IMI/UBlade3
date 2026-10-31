using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// One plan word: the union in HYPERX decomp src/planvm/plannode.h (0x004b... union PlanWord).
    /// A word is an integer, a float's bits, a pronoun, a local-real slot, an interactor-name slot, a
    /// child-node pointer, or a string pointer; the parser below keeps the raw word and the compiled
    /// value side by side so a report can be produced without an engine.
    /// </summary>
    public readonly struct PlanWord : IEquatable<PlanWord>
    {
        // src/planvm/plannode.h: kPlanArgPronoun 0xfedc, kPlanArgLocalReal 0xedcb,
        // kPlanArgInteractorName 0xdcba, kPlanMarkerNode 0xabcdefff, kPlanMarkerString 0xabcdeffe.
        public const int PronounTag = 0xFEDC;

        public const int LocalRealTag = 0xEDCB;

        public const int InteractorNameTag = 0xDCBA;

        public const int MarkerNode = unchecked((int)0xABCDEFFF);

        public const int MarkerString = unchecked((int)0xABCDEFFE);

        // The compiler's seeded tokens, PlanVM::PlanVM (0x0047d340) and planvm.cpp tokens.
        public const int TokenBroadcast = 0x0000FFFF;

        public const int TokenStraightAhead = unchecked((int)0xABCDABCD);

        public const int TokenTemporarily = unchecked((int)0xABCDABCC);

        public const int TokenMessageData = unchecked((int)0xABCDABCE);

        public const int TokenNoBehaviourChange = unchecked((int)0xABCDABCB);

        public const int TokenPreviousBehaviour = unchecked((int)0xABCDABCA);

        public const int EndPlanOpcode = unchecked((int)0xFFFF0000);

        public readonly int Value;

        public PlanWord(int value)
        {
            Value = value;
        }

        public int High
        {
            get { return (Value >> 16) & 0xFFFF; }
        }

        public int Low
        {
            get { return Value & 0xFFFF; }
        }

        public bool IsPronoun
        {
            get { return High == PronounTag; }
        }

        public bool IsLocalReal
        {
            get { return High == LocalRealTag; }
        }

        public bool IsInteractorName
        {
            get { return High == InteractorNameTag; }
        }

        public bool IsNoBehaviourChange
        {
            get { return Value == TokenNoBehaviourChange; }
        }

        public bool IsPreviousBehaviour
        {
            get { return Value == TokenPreviousBehaviour; }
        }

        /// <summary>PlanWord::Pack in plannode.h: an index under a high-half tag.</summary>
        public static int Pack(int tag, int index)
        {
            return (index & 0xFFFF) | (tag << 16);
        }

        /// <summary>The bits read as a float, the way PlanWord::fl shares the union (planarg.cpp ResolveFloat callers).</summary>
        public float AsFloat
        {
            get { return BitConverter.ToSingle(BitConverter.GetBytes(Value), 0); }
        }

        public bool Equals(PlanWord other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is PlanWord && Equals((PlanWord)obj);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return "0x" + ((uint)Value).ToString("x8");
        }
    }

    public enum PlanOperandKind
    {
        Node,
        Leaf,
        String,
    }

    /// <summary>
    /// One entry of a compiled node's argument array (union PlanWord m_pArgs[] in plannode.h).
    /// HYPERX writes either a child-node pointer, a resolved leaf word, or a string pointer into it;
    /// the raw file word is kept here beside the compiled value for exact reporting.
    /// </summary>
    public sealed class PlanOperand
    {
        public PlanOperandKind Kind;

        /// <summary>The word as it sits in the file (leaves only).</summary>
        public PlanWord Raw;

        /// <summary>The word the VM reads: a pronoun resolved by PlanArg, a plan name resolved to its index, else the raw word.</summary>
        public PlanWord Word;

        public PlanNode Node;

        /// <summary>The literal text of a string child (each char word truncated to 8 bits, as ParseNodeStream does).</summary>
        public string Text;

        /// <summary>The raw character words of a string child, terminator excluded; the digest reads these.</summary>
        public PlanWord[] StringWords;

        /// <summary>Set when a CHANGE_OF_PLAN string named another plan; Word is then that plan's index.</summary>
        public bool IsPlanReference;

        public int IntValue
        {
            get { return Word.Value; }
        }

        public short ShortValue
        {
            get { return unchecked((short)Word.Value); }
        }

        public static PlanOperand ForNode(PlanNode node)
        {
            PlanOperand operand = new PlanOperand();
            operand.Kind = PlanOperandKind.Node;
            operand.Node = node;
            return operand;
        }

        public static PlanOperand ForLeaf(PlanWord raw, PlanWord compiled)
        {
            PlanOperand operand = new PlanOperand();
            operand.Kind = PlanOperandKind.Leaf;
            operand.Raw = raw;
            operand.Word = compiled;
            return operand;
        }

        public static PlanOperand ForString(PlanWord[] chars, string text)
        {
            PlanOperand operand = new PlanOperand();
            operand.Kind = PlanOperandKind.String;
            operand.StringWords = chars;
            operand.Text = text;
            return operand;
        }
    }

    /// <summary>
    /// One compiled instruction: PlanNode in src/planvm/plannode.h. Opcode is (category &lt;&lt; 16) | member;
    /// ArgCount is the file's 16-bit count stored as a short, and the child array is ArgCount &amp; 0x7fff
    /// entries, exactly as PlanState::ParseNodeStream (0x0047b430) masks it. END_PLAN has no count word.
    /// </summary>
    public sealed class PlanNode
    {
        public int Opcode;

        public short ArgCount;

        public PlanOperand[] Args;

        public PlanNode()
        {
            Args = new PlanOperand[0];
        }

        public int Category
        {
            get { return Opcode & unchecked((int)0xFFFF0000); }
        }

        public int Member
        {
            get { return Opcode & 0xFFFF; }
        }

        public int ChildCount
        {
            get { return ArgCount & 0x7FFF; }
        }

        public bool IsEndPlan
        {
            get { return Opcode == PlanWord.EndPlanOpcode; }
        }

        public string Keyword
        {
            get { return PlanOpcodes.KeywordOf(Opcode); }
        }

        public override string ToString()
        {
            return Keyword + " argc=" + ArgCount;
        }
    }
}
