using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// Interactor in src/planvm/interactor.h: the base of the message and collision interactors.
    /// Fields and offsets follow the header: m_pOwner 0x004, m_pNode 0x00c, pNext 0x010,
    /// m_nActiveState 0x014, m_nBehaviour 0x018, m_nMember 0x01c, m_wSlot 0x020, m_wBehaviour 0x022,
    /// m_bEnabled 0x024, m_otherSlot 0x028, m_ownSlotOrMessage 0x02c, m_wHandlerIndex 0x030,
    /// m_wEventKind 0x032.
    /// </summary>
    public abstract class PlanInteractor
    {
        public PlanState State;

        public PlanBehavior Behavior;

        public short OwnerSlot;

        public PlanInteractor Next;

        public int ActiveState;

        public int BehaviourIndex;

        public int Member;

        public short Slot;

        public short Behaviour;

        public short HandlerIndex;

        public short EventKind;

        public bool Enabled;

        public PlanWord OtherSlot;

        public PlanWord OwnSlotOrMessage;

        /// <summary>The Enable block, or null when the interactor has none (m_pNode).</summary>
        public PlanNode EnableNode;

        protected PlanInteractor()
        {
            Enabled = true;
        }

        public int GetActiveState()
        {
            return ActiveState;
        }

        public void SetActiveState(int state)
        {
            ActiveState = state;
        }

        public int GetState()
        {
            return Member;
        }

        public void SetMember(int member)
        {
            Member = member;
        }

        public short GetOtherSlot()
        {
            if (OtherSlot.IsPronoun)
            {
                throw new PlanVmUnsupportedHandlerException("stat pronoun", OtherSlot.Low);
            }

            return unchecked((short)OtherSlot.Value);
        }

        public short GetModelSlot()
        {
            if (OwnSlotOrMessage.IsPronoun)
            {
                throw new PlanVmUnsupportedHandlerException("stat pronoun", OwnSlotOrMessage.Low);
            }

            return unchecked((short)OwnSlotOrMessage.Value);
        }

        /// <summary>
        /// Interactor::Discard as both pools implement it (CollInteractor 0x0045dc00, MsgInteractor
        /// 0x0045dc20): the interactor is returned to its pool cleared. The pool itself is engine
        /// memory management; the ported state resets exactly the fields both bodies clear.
        /// </summary>
        public virtual void Discard()
        {
            ActiveState = 0;
            Enabled = true;
            EnableNode = null;
            Next = null;
        }

        /// <summary>The script the interactor's Setup collected, or null when there is no Enable node.</summary>
        protected PlanNode EnableOf()
        {
            return EnableNode;
        }

        /// <summary>The four instruction classes Setup accepts: Debug, a CHANGE_TO_BEHAVIOR, SET_INTERACTOR_PARAM, and Enable.</summary>
        protected void SetupChild(PlanNode child, IPlanVmHost host)
        {
            switch (child.Category)
            {
                case (int)PlanOpcodes.Category.Debug:
                    return;

                case (int)PlanOpcodes.Category.Behaviour:
                    if (child.Member == (int)PlanOpcodes.BehaviourMember.ChangeToBehavior)
                    {
                        BehaviourIndex = child.Args[0].IntValue;
                    }

                    return;

                case (int)PlanOpcodes.Category.Action:
                    if (child.Member == (int)PlanOpcodes.ActionMember.SetInteractorParam)
                    {
                        host.SetInteractorParam(child.Args[0].IntValue, child, State.Current, this);
                    }

                    return;

                case (int)PlanOpcodes.Category.Enable:
                    EnableNode = child;
                    return;
            }
        }

        protected PlanNode ChildNode(PlanNode parent, int index)
        {
            PlanOperand operand = parent.Args[index];

            if (operand == null || operand.Kind != PlanOperandKind.Node)
            {
                throw new PlanVmException("Interactor setup expected an instruction where a leaf sits.");
            }

            return operand.Node;
        }
    }

    /// <summary>MsgInteractor: src/planvm/msginteractor.{h,cpp}. Setup 0x0045dfc0, Dispatch 0x0045e0d0.</summary>
    public sealed class PlanMessageInteractor : PlanInteractor
    {
        private const int ArgMessageFirst = 1;

        public PlanMessageInteractor()
        {
            OtherSlot = new PlanWord(0);
            OwnSlotOrMessage = new PlanWord(0);
        }

        public void Setup(PlanNode planArg, IPlanVmHost host)
        {
            ActiveState = 0;
            OwnSlotOrMessage = planArg.Args[0].Word;
            BehaviourIndex = PlanWord.TokenNoBehaviourChange;
            OtherSlot = new PlanWord(host.OwnerSlot);

            int count = planArg.ArgCount - ArgMessageFirst;

            for (int index = 0; index < count; index++)
            {
                PlanNode child = ChildNode(planArg, ArgMessageFirst + index);

                switch (child.Category)
                {
                    case (int)PlanOpcodes.Category.Debug:
                        break;

                    case (int)PlanOpcodes.Category.Behaviour:
                        if (child.Member == (int)PlanOpcodes.BehaviourMember.ChangeToBehavior)
                        {
                            BehaviourIndex = child.Args[0].IntValue;
                        }

                        break;

                    case (int)PlanOpcodes.Category.Action:
                        if (child.Member == (int)PlanOpcodes.ActionMember.SetInteractorParam)
                        {
                            host.SetInteractorParam(child.Args[0].IntValue, child, State.Current, this);
                        }

                        break;

                    case (int)PlanOpcodes.Category.Enable:
                        EnableNode = child;
                        break;

                    default:
                        // STRING: HYPERX 0x004c44fc
                        throw new PlanVmException("Invalid Instruction object in MsgInteractor setup", child.Opcode);
                }
            }
        }

        public short GetTargetSlot()
        {
            return unchecked((short)OtherSlot.Value);
        }

        /// <summary>MsgInteractor::SetSenderAndDispatch (0x0045e0b0).</summary>
        public void SetSenderAndDispatch(int nArg, ref short state, short sender, IPlanVmHost host)
        {
            OtherSlot = new PlanWord(sender);
            Dispatch(nArg, ref state, host);
        }

        /// <summary>MsgInteractor::Dispatch (0x0045e0d0).</summary>
        public void Dispatch(int nArg, ref short state, IPlanVmHost host)
        {
            Slot = -1;
            Behaviour = 0;

            PlanNode enable = EnableNode;

            if (enable != null)
            {
                PlanScriptRunner.ExecuteScript(enable.Args, 0, enable.ChildCount, this, ref state, nArg, host);
            }

            if (unchecked((uint)BehaviourIndex) < unchecked((uint)PlanWord.TokenNoBehaviourChange) && Enabled)
            {
                host.DispatchExitBehavior();
                State.ActivateBehavior(unchecked((uint)BehaviourIndex));
            }

            State.CollisionRearmTicks = PlanState.CollisionRearmTicksAfterRun;

            if (Slot >= 0)
            {
                host.DispatchExitBehavior();
                host.ChangePlan(Slot, Behaviour);
            }
        }
    }

    /// <summary>CollInteractor: src/planvm/collinteractor.{h,cpp}. Setup 0x0045dd00, Process 0x0045ddf0.</summary>
    public sealed class PlanCollisionInteractor : PlanInteractor
    {
        private const int ArgKey = 0;

        private const int ArgTag = 1;

        private const int ArgConditionFirst = 2;

        public PlanCollisionInteractor()
        {
            OtherSlot = new PlanWord(0);
            OwnSlotOrMessage = new PlanWord(0);
        }

        public void Setup(PlanNode planArg, IPlanVmHost host)
        {
            ActiveState = 0;
            OtherSlot = planArg.Args[ArgKey].Word;
            OwnSlotOrMessage = planArg.Args[ArgTag].Word;
            BehaviourIndex = PlanWord.TokenNoBehaviourChange;

            int count = planArg.ArgCount - ArgConditionFirst;

            for (int index = 0; index < count; index++)
            {
                PlanNode child = ChildNode(planArg, ArgConditionFirst + index);

                switch (child.Category)
                {
                    case (int)PlanOpcodes.Category.Debug:
                        break;

                    case (int)PlanOpcodes.Category.Behaviour:
                        if (child.Member == (int)PlanOpcodes.BehaviourMember.ChangeToBehavior)
                        {
                            BehaviourIndex = child.Args[0].IntValue;
                        }

                        break;

                    case (int)PlanOpcodes.Category.Action:
                        if (child.Member == (int)PlanOpcodes.ActionMember.SetInteractorParam)
                        {
                            host.SetInteractorParam(child.Args[0].IntValue, child, State.Current, this);
                        }

                        break;

                    case (int)PlanOpcodes.Category.Enable:
                        EnableNode = child;
                        break;

                    default:
                        // STRING: HYPERX 0x004c44c8
                        throw new PlanVmException("Invalid Instruction object in CollInteractor setup", child.Opcode);
                }
            }
        }

        /// <summary>
        /// CollInteractor::Process (0x0045ddf0). Mode one runs only for a network object, mode zero
        /// only for the others. The collision outcome and event are handed to the host.
        /// </summary>
        public void Process(int nArg, ref short outcome, int nMode, IPlanVmHost host)
        {
            ActiveState = 0;

            bool modeMatches = (nMode == 1 && host.Phase == PlanPhase.Network) || (nMode == 0 && host.Phase != PlanPhase.Network);

            if (!modeMatches)
            {
                return;
            }

            Slot = -1;
            Behaviour = 0;

            PlanNode enable = EnableNode;

            if (enable != null)
            {
                PlanScriptRunner.ExecuteScript(enable.Args, 0, enable.ChildCount, this, ref outcome, nArg, host);
            }

            if (unchecked((uint)BehaviourIndex) < unchecked((uint)PlanWord.TokenNoBehaviourChange) && Enabled)
            {
                host.DispatchExitBehavior();
                State.ActivateBehavior(unchecked((uint)BehaviourIndex));
            }

            if (nMode == 0 && host.Phase != PlanPhase.Network && host.HasPhysicsState)
            {
                host.EnqueueCollisionEvent(EventKind, unchecked((short)OtherSlot.Value), unchecked((short)OwnSlotOrMessage.Value), outcome, nArg);
            }

            State.CollisionRearmTicks = PlanState.CollisionRearmTicksAfterRun;

            if (Slot >= 0)
            {
                host.DispatchExitBehavior();
                host.ChangePlan(Slot, Behaviour);
            }
        }
    }

    /// <summary>
    /// PlanInteractorFactories::CreateMsgInteractor / CreateCollInteractor (HYPERX 0x0045dca0 /
    /// 0x0045df60): setup, then link after the previous interactor, else head the behaviour's chain,
    /// else the plan state's.
    /// </summary>
    public static class PlanInteractorFactory
    {
        public static PlanMessageInteractor CreateMessageInteractor(PlanState state, PlanBehavior behavior, PlanNode node, PlanInteractor previous, IPlanVmHost host)
        {
            PlanMessageInteractor interactor = new PlanMessageInteractor();
            interactor.State = state;
            interactor.Behavior = behavior;
            interactor.OwnerSlot = host.OwnerSlot;
            interactor.Setup(node, host);

            if (previous == null)
            {
                if (behavior != null)
                {
                    behavior.MsgInteractors = interactor;
                }
                else
                {
                    state.MsgInteractors = interactor;
                }
            }
            else
            {
                previous.Next = interactor;
            }

            return interactor;
        }

        public static PlanCollisionInteractor CreateCollisionInteractor(PlanState state, PlanBehavior behavior, PlanNode node, PlanInteractor previous, IPlanVmHost host)
        {
            PlanCollisionInteractor interactor = new PlanCollisionInteractor();
            interactor.State = state;
            interactor.Behavior = behavior;
            interactor.OwnerSlot = host.OwnerSlot;
            interactor.Setup(node, host);

            if (previous == null)
            {
                if (behavior != null)
                {
                    behavior.CollInteractors = interactor;
                }
                else
                {
                    state.CollInteractors = interactor;
                }
            }
            else
            {
                previous.Next = interactor;
            }

            return interactor;
        }
    }
}
