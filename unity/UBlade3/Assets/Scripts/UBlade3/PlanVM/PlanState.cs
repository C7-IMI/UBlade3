using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// PlanState in src/planvm/planstate.{h,cpp}: one plan instance compiled for one actor. It owns
    /// the compiled top-level node array, the behaviour chain, the global interactor chains, the
    /// local reals, the behaviour cursor/index and the collision re-arm counter. The field names and
    /// meanings follow planstate.h; the engine-owned pieces (owner pointer, agenda items, animation)
    /// go through IPlanVmHost.
    /// </summary>
    public sealed class PlanState
    {
        /// <summary>Ticks after any interactor runs before every collision interactor is set active again.</summary>
        public const short CollisionRearmTicksAfterRun = 0x0C;

        /// <summary>Ticks after instantiation before every collision interactor is set active again.</summary>
        public const short CollisionRearmTicksInitial = 10;

        /// <summary>The nine-word plan PlanState::Reinitialize compiles (GLOBAL HYPERX 0x004c689c).</summary>
        public static readonly int[] ReinitializeImage = new int[]
        {
            PlanWord.MarkerNode,
            unchecked((int)0x00070000), // CATEGORY_BEHAVIOUR | MEMBER_DECLARE_BEHAVIOR
            2,
            6,
            PlanWord.MarkerString,
            'x',
            0,
            PlanWord.MarkerNode,
            unchecked((int)0xFFFF0000), // CATEGORY_END_PLAN
        };

        public readonly IPlanVmHost Host;

        /// <summary>The compiled top-level node array m_ppBehaviors, END_PLAN included.</summary>
        public PlanNode[] Behaviors;

        /// <summary>The owner, an engine object in the original; a Probe identifies it by tag.</summary>
        public object OwnerTag;

        /// <summary>m_pszName: the owner's plan table names the instance; PlanState never writes it.</summary>
        public string Name;

        /// <summary>m_wSyncValue: the engine tick copies it into the owner.</summary>
        public short SyncValue;

        /// <summary>m_ppCursor, kept as an index into Behaviors; only Instantiate and ActivateBehavior move it.</summary>
        public int CursorIndex;

        /// <summary>m_pHead: the head of the behaviour chain, linked by m_pStandby.</summary>
        public PlanBehavior Head;

        /// <summary>m_pCurrent: the active behaviour.</summary>
        public PlanBehavior Current;

        /// <summary>m_nIndex: the active behaviour's order.</summary>
        public short Index;

        /// <summary>m_nPrevious: where ActivateBehavior(_PREVIOUS_BEH_) returns to.</summary>
        public short Previous;

        /// <summary>m_pCollInteractors: the plan's global collision interactors.</summary>
        public PlanCollisionInteractor CollInteractors;

        /// <summary>m_pMsgInteractors: the plan's global message interactors.</summary>
        public PlanMessageInteractor MsgInteractors;

        /// <summary>m_nInteractorCount: the split between the plan's and the current behaviour's collision lists.</summary>
        public short InteractorCount;

        /// <summary>m_nCollisionRearmTicks: counts down; when it reaches zero the global collision interactors re-arm.</summary>
        public short CollisionRearmTicks;

        /// <summary>m_pflLocalReals: DECLARE_LOCAL_REALS storage, zeroed at instantiation.</summary>
        public float[] LocalReals;

        /// <summary>m_nLocalRealCount.</summary>
        public short LocalRealCount;

        /// <summary>m_apNamedInteractors: the SET_INTERACTOR_NAME slots.</summary>
        public PlanInteractor[] NamedInteractors;

        /// <summary>m_nNamedInteractors, including the ParseNodeStream counting quirk documented in PlanStreamParser.</summary>
        public short NamedInteractorCount;

        public PlanProgram Program;

        /// <summary>The last compile's top-level count, m_nBehaviorCount (END_PLAN included).</summary>
        public int BehaviorNodeCount
        {
            get { return Behaviors == null ? 0 : Behaviors.Length; }
        }

        public PlanState(IPlanVmHost host, object ownerTag)
        {
            Host = host;
            OwnerTag = ownerTag;
            Index = 0;
            Previous = 0;
            SlotReset();
        }

        private void SlotReset()
        {
            CursorIndex = 0;
            InteractorCount = 0;
            CollisionRearmTicks = 0;
            LocalRealCount = 0;
            NamedInteractorCount = 0;
        }

        /// <summary>PlanState::SetOwnerAndReset (HYPERX 0x0047b0e0).</summary>
        public void SetOwnerAndReset(object ownerTag, PlanNode[] behaviors)
        {
            OwnerTag = ownerTag;

            if (!ReferenceEquals(Behaviors, behaviors))
            {
                Behaviors = behaviors;
            }

            CursorIndex = 0;
        }

        /// <summary>PlanState::SetAndInit (HYPERX 0x0047b110): compile the stream for this actor and adopt it.</summary>
        public bool SetAndInit(PlanFileImage image)
        {
            PlanNode[] compiled = Compile(image);

            if (compiled == null)
            {
                return false;
            }

            Behaviors = compiled;
            CursorIndex = 0;

            return true;
        }

        /// <summary>
        /// PlanState::Compile (HYPERX 0x0047b2d0): parse the stream with this actor's argument
        /// resolver and count the named interactors. Returns the top-level node array, END_PLAN included.
        /// </summary>
        public PlanNode[] Compile(PlanFileImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            Program = PlanStreamParser.Parse(image, Host == null ? null : Host.ArgResolver, Host);
            NamedInteractorCount = unchecked((short)Program.NamedInteractorCount);
            NamedInteractors = NamedInteractorCount == 0 ? null : new PlanInteractor[NamedInteractorCount];

            return Program.TopLevelNodes;
        }

        /// <summary>PlanState::GetBehaviorByIndex (HYPERX 0x0047b150): the _PREVIOUS_BEH_ token walks back; no bound.</summary>
        public PlanBehavior GetBehaviorByIndex(uint index)
        {
            short left;

            if (index == unchecked((uint)PlanWord.TokenPreviousBehaviour))
            {
                left = Previous;
            }
            else
            {
                left = unchecked((short)index);
            }

            PlanBehavior at = Head;

            while (left-- > 0)
            {
                if (at == null)
                {
                    throw new PlanVmException("Walked off the behaviour chain before index " + index + ".");
                }

                at = at.Standby;
            }

            return at;
        }

        /// <summary>PlanState::WalkLinkedList (HYPERX 0x0047b180): an index continuous across the two collision lists.</summary>
        public PlanCollisionInteractor WalkLinkedList(short index)
        {
            PlanInteractor node;
            short at;

            if (index < InteractorCount)
            {
                node = CollInteractors;
                at = 0;
            }
            else
            {
                node = Current == null ? null : Current.CollInteractors;
                at = InteractorCount;
            }

            while (at++ < index)
            {
                if (node != null)
                {
                    node = node.Next;
                }
            }

            return (PlanCollisionInteractor)node;
        }

        /// <summary>
        /// PlanState::ActivateBehavior (HYPERX 0x0047b1c0): step the behaviour chain, tick and sort the
        /// new behaviour's messages, send its timed message, then follow every pass-on behaviour.
        /// </summary>
        public void ActivateBehavior(uint index)
        {
            if (Current == null)
            {
                throw new PlanVmException("ActivateBehavior before Instantiate (m_pCurrent is null).");
            }

            short slot = unchecked((short)index);

            if (index == unchecked((uint)PlanWord.TokenPreviousBehaviour))
            {
                slot = Previous;
            }

            if (slot < 0 && index != unchecked((uint)PlanWord.TokenPreviousBehaviour))
            {
                throw new PlanVmException("Negative behaviour index " + index + " (the original's counted walk would run past the chain).");
            }

            CursorIndex = 0;

            if (!Current.Digressive)
            {
                Previous = Index;
            }

            Index = slot;
            Current.SetInteractorsActive(0);
            Current = Head;

            while (slot-- != 0)
            {
                if (Current.Standby != null)
                {
                    CursorIndex++;
                    Current = Current.Standby;
                }
                else
                {
                    Current = Head;
                    CursorIndex = 0;
                }
            }

            Current.TickAndSortMessages(Host);
            Current.SendTimedMessage(Host);

            while (Current.CanPassOn())
            {
                CursorIndex++;
                Current = Current.Standby;

                Current.TickAndSortMessages(Host);
                Current.SendTimedMessage(Host);
            }
        }

        /// <summary>PlanState::PostUpdateCleanup (HYPERX 0x0047b290): re-arm the plan's collision interactors.</summary>
        public void PostUpdateCleanup()
        {
            if (CollisionRearmTicks-- == 0)
            {
                PlanInteractor node = CollInteractors;

                while (node != null)
                {
                    node.SetActiveState(1);
                    node = node.Next;
                }
            }

            if (Current != null)
            {
                Current.UpdateBehaviorPollCountdown();
            }
        }

        /// <summary>
        /// PlanState::Instantiate (HYPERX 0x0047b680): build the behaviour chain, the global interactor
        /// chains and the local reals. Returns false only when there is no compiled plan.
        /// </summary>
        public bool Instantiate()
        {
            if (Behaviors == null)
            {
                return false;
            }

            PlanBehavior previous = null;
            PlanInteractor previousCondition = null;
            PlanInteractor previousMessage = null;
            int behaviours = 0;
            int conditions = 0;

            for (int at = 0; at < Behaviors.Length; at++)
            {
                PlanNode node = Behaviors[at];

                if (node == null)
                {
                    throw new PlanVmException("Compiled plan has a null top-level node at " + at + ".");
                }

                switch (node.Category)
                {
                    case (int)PlanOpcodes.Category.PlanChange:
                        if (node.ArgCount < 0)
                        {
                            throw new PlanVmException("DECLARE_LOCAL_REALS has a negative count (" + node.ArgCount + ").");
                        }

                        LocalRealCount = node.ArgCount;
                        LocalReals = new float[node.ArgCount];

                        for (int slot = 0; slot < LocalRealCount; slot++)
                        {
                            LocalReals[slot] = 0.0f;
                        }

                        break;

                    case (int)PlanOpcodes.Category.Behaviour:
                        if (node.Member != (int)PlanOpcodes.BehaviourMember.DeclareBehavior)
                        {
                            // STRING: HYPERX 0x004c6960
                            throw new PlanVmException("Expected a Behavior Declaration");
                        }

                        if (node.ChildCount < 2 || node.Args[0].Kind != PlanOperandKind.Leaf || node.Args[1].Kind != PlanOperandKind.String)
                        {
                            throw new PlanVmException("DECLARE_BEHAVIOR operands are not (kind, name).", node.Opcode);
                        }

                        int kind = node.Args[0].IntValue;

                        if (!PlanOpcodes.IsSupportedBehaviorKind(kind))
                        {
                            throw new PlanVmException("Behaviour kind " + kind + " is outside the 25-entry behavior registry.");
                        }

                        PlanBehavior behaviour = Host.CreateBehavior(unchecked((short)kind));

                        if (behaviour == null)
                        {
                            throw new PlanVmException("Behaviour registry did not create kind " + kind + ".");
                        }

                        behaviour.State = this;
                        behaviour.Digressive = false;
                        behaviour.Init(Host, node, 0);
                        behaviour.AssignPriorities(conditions);
                        behaviour.SetInteractorsActive(0);
                        behaviour.Name = node.Args[1].Text;
                        behaviour.SyncValue = unchecked((short)behaviours);
                        behaviours++;

                        if (previous != null)
                        {
                            previous.Standby = behaviour;
                        }
                        else
                        {
                            Head = behaviour;
                        }

                        previous = behaviour;
                        break;

                    case (int)PlanOpcodes.Category.GlobalInteractors:
                        for (int child = 0; child < node.ChildCount; child++)
                        {
                            PlanOperand operand = node.Args[child];

                            if (operand == null || operand.Kind != PlanOperandKind.Node)
                            {
                                throw new PlanVmException("GLOBAL_INTERACTORS child is not an instruction.");
                            }

                            PlanNode item = operand.Node;

                            switch (item.Category)
                            {
                                case (int)PlanOpcodes.Category.CollisionInteractor:
                                {
                                    PlanCollisionInteractor condition = PlanInteractorFactory.CreateCollisionInteractor(this, null, item, previousCondition, Host);
                                    condition.EventKind = unchecked((short)conditions);
                                    conditions++;
                                    condition.SetMember(item.Member);
                                    previousCondition = condition;
                                    break;
                                }

                                case (int)PlanOpcodes.Category.MessageInteractor:
                                {
                                    PlanMessageInteractor message = PlanInteractorFactory.CreateMessageInteractor(this, null, item, previousMessage, Host);
                                    message.SetMember(item.Member);
                                    previousMessage = message;
                                    break;
                                }

                                default:
                                    // STRING: HYPERX 0x004c6928
                                    throw new PlanVmException("Invalid Instruction object in global interactor setup", item.Opcode);
                            }
                        }

                        break;

                    case (int)PlanOpcodes.Category.EndPlan:
                        break;

                    default:
                        // STRING: HYPERX 0x004c68fc
                        throw new PlanVmException("Invalid Instruction in Plan Instantiation", node.Opcode);
                }

                CursorIndex = at + 1;

                if (node.Category == (int)PlanOpcodes.Category.EndPlan)
                {
                    if (previous == null)
                    {
                        // The original dereferences the null behaviour here; no shipped plan does this.
                        throw new PlanVmException("END_PLAN before any DECLARE_BEHAVIOR (the original would dereference null).");
                    }

                    previous.Standby = null;
                    Index = 0;
                    Previous = 0;
                    CursorIndex = 0;
                    Current = Head;
                    InteractorCount = unchecked((short)conditions);
                    CollisionRearmTicks = CollisionRearmTicksInitial;

                    return true;
                }
            }

            throw new PlanVmException("Compiled plan ended without END_PLAN (Compile should have rejected it).");
        }

        /// <summary>PlanState::Teardown (HYPERX 0x0047b900): drop the instance's interactors, agenda items and behaviours.</summary>
        public void Teardown()
        {
            if (LocalReals != null)
            {
                LocalReals = null;
                LocalRealCount = 0;
            }

            PlanInteractor node = CollInteractors;

            while (node != null)
            {
                PlanInteractor next = node.Next;
                node.Discard();
                node = next;
            }

            node = MsgInteractors;
            CollInteractors = null;

            while (node != null)
            {
                PlanInteractor next = node.Next;
                node.Discard();
                node = next;
            }

            MsgInteractors = null;
            InteractorCount = 0;

            if (Host != null)
            {
                Host.ClearAgendaObserver(this);
            }

            if (Head != null)
            {
                Head.Reset(Host);
            }

            Head = null;
            Current = null;
        }

        /// <summary>PlanState::Reinitialize (HYPERX 0x0047b990): compile the nine-word default plan.</summary>
        public void Reinitialize()
        {
            PlanWord[] words = new PlanWord[ReinitializeImage.Length];

            for (int i = 0; i < words.Length; i++)
            {
                words[i] = new PlanWord(ReinitializeImage[i]);
            }

            PlanFileImage image = new PlanFileImage();
            image.Name = "REINIT";
            image.Path = string.Empty;
            image.Words = words;
            image.DeclaredWordCount = words.Length - 1;
            image.ByteLength = (words.Length + 1) * 4;

            SetAndInit(image);
        }
    }
}
