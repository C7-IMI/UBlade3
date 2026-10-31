using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// GameBehaviorObject in src/planvm/gamebehaviorobject.h: one behaviour of a plan instance.
    /// The ten agenda slots, the animation side and the loaded physics body are engine handles in
    /// the original; here they are typed placeholders while the ported state machine keeps the
    /// exact fields, defaults and chains (constructor 0x0041bf50, Init 0x0041bff0).
    /// </summary>
    public sealed class PlanBehavior
    {
        public const int SlotCount = 10;

        public const short DefaultLatency = 0x0A;

        /// <summary>kMsgNomsg, src/engine/messageids.h.</summary>
        public const int MessageNoMsg = 0x8E;

        public object[] Slots = new object[SlotCount];

        public PlanState State;

        public short OwnerSlot;

        public string Name;

        public PlanMessageInteractor MsgInteractors;

        public PlanCollisionInteractor CollInteractors;

        public PlanBehavior Standby;

        public short Kind;

        public short SyncValue;

        public short PollCountdown;

        public short Latency;

        public bool Digressive;

        public float AnimationRate;

        public float PersistentRate;

        public int AnimationFlags;

        public int PersistentFlags;

        public int ChannelSlot;

        public int EventKind;

        public int EventTarget;

        public int EventParam;

        /// <summary>One byte per agenda id: a raised byte takes that item out (m_abAgendaPurged).</summary>
        public bool[] AgendaPurged = new bool[PlanOpcodes.AgendaItemCount];

        public PlanBehavior()
        {
            AnimationRate = -1.0f;
            AnimationFlags = -1;
            PersistentRate = -1.0f;
            PersistentFlags = -1;
            EventKind = MessageNoMsg;
            PollCountdown = -1;
            Latency = DefaultLatency;
        }

        /// <summary>GameBehaviorObject::Init (HYPERX 0x0041bff0): the behaviour's setup pass over its compiled block.</summary>
        public void Init(IPlanVmHost host, PlanNode block, int flags)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }

            if (block.Opcode == PlanOpcodes.OpcodeDeclareBehavior)
            {
                if (block.ArgCount < 2)
                {
                    throw new PlanVmException("DECLARE_BEHAVIOR has " + block.ArgCount + " operands; Init skips two.");
                }

                if (block.Args[1].Kind == PlanOperandKind.String)
                {
                    Name = block.Args[1].Text;
                }
            }

            int start = block.Opcode == PlanOpcodes.OpcodeDeclareBehavior ? 2 : 0;
            int count = block.ArgCount - start;

            if (count < 0)
            {
                throw new PlanVmException("Behaviour block has a negative instruction count (" + block.ArgCount + ").");
            }

            PlanInteractor previousMessage = null;
            PlanInteractor previousCondition = null;
            short scratch = 0;

            for (int index = 0; index < count; index++)
            {
                PlanOperand operand = block.Args[start + index];

                if (operand == null || operand.Kind != PlanOperandKind.Node)
                {
                    throw new PlanVmException("Behaviour setup expected an instruction where a leaf sits.");
                }

                PlanNode item = operand.Node;

                switch (item.Category)
                {
                    case (int)PlanOpcodes.Category.MessageInteractor:
                        previousMessage = PlanInteractorFactory.CreateMessageInteractor(State, this, item, previousMessage, host);
                        previousMessage.SetMember(item.Member);
                        break;

                    case (int)PlanOpcodes.Category.CollisionInteractor:
                        previousCondition = PlanInteractorFactory.CreateCollisionInteractor(State, this, item, previousCondition, host);
                        previousCondition.SetMember(item.Member);
                        break;

                    case (int)PlanOpcodes.Category.Behaviour:
                        if (item.Member == (int)PlanOpcodes.BehaviourMember.SetBehaviorParam)
                        {
                            host.SetBehaviorParam(item.Args[0].IntValue, item, this);
                        }

                        break;

                    case (int)PlanOpcodes.Category.Agenda:
                        if (item.Opcode == PlanOpcodes.OpcodeAddAgendaItem)
                        {
                            // Init binds without a node: AgendaItem::Bind (0x0040f830), not BindWithNode.
                            host.AddAgendaItem(item.Args[0].IntValue, null, this, 0);
                        }
                        else if (item.Opcode == PlanOpcodes.OpcodeRemoveAgendaItem)
                        {
                            host.RemoveAgendaItem(item.Args[0].IntValue, this);
                        }

                        break;

                    case (int)PlanOpcodes.Category.Action:
                        switch (item.Member)
                        {
                            case (int)PlanOpcodes.ActionMember.DoNetAction:
                                if (host.Phase != PlanPhase.Local)
                                {
                                    host.ExecuteAction(item.Args[0].IntValue, item, null, ref scratch, 0);
                                }

                                break;

                            case (int)PlanOpcodes.ActionMember.DoLocalAction:
                                if (host.Phase != PlanPhase.Network)
                                {
                                    host.ExecuteAction(item.Args[0].IntValue, item, null, ref scratch, 0);
                                }

                                break;

                            case (int)PlanOpcodes.ActionMember.DoAction:
                                host.ExecuteAction(item.Args[0].IntValue, item, null, ref scratch, 0);
                                break;

                            case (int)PlanOpcodes.ActionMember.SetInteractorParam:
                                host.SetInteractorParam(item.Args[0].IntValue, item, this, null);
                                break;
                        }

                        break;

                    case (int)PlanOpcodes.Category.Debug:
                        break;

                    default:
                        // STRING: HYPERX 0x004bc1d0
                        throw new PlanVmException("Invalid Instruction object in Behavior setup.", item.Opcode);
                }
            }
        }

        /// <summary>GameBehaviorObject::AssignPriorities (HYPERX 0x0041c5d0).</summary>
        public void AssignPriorities(int start)
        {
            PlanInteractor at = CollInteractors;

            if (at == null)
            {
                return;
            }

            int priority = start;

            while (at != null)
            {
                at.EventKind = unchecked((short)priority);
                priority++;
                at = at.Next;
            }
        }

        /// <summary>GameBehaviorObject::SetInteractorsActive (HYPERX 0x0041c2c0); minus one stays untouched.</summary>
        public void SetInteractorsActive(int arg)
        {
            PlanInteractor first = CollInteractors;
            PlanInteractor second = MsgInteractors;

            while (first != null)
            {
                if (first.GetActiveState() != -1)
                {
                    first.SetActiveState(arg);
                }

                first = first.Next;
            }

            while (second != null)
            {
                if (second.GetActiveState() != -1)
                {
                    second.SetActiveState(arg);
                }

                second = second.Next;
            }
        }

        /// <summary>GameBehaviorObject::CanPassOn (gamebehaviorobject.h): no interactors and a standby.</summary>
        public bool CanPassOn()
        {
            return CollInteractors == null && MsgInteractors == null && Standby != null;
        }

        /// <summary>GameBehaviorObject::SendTimedMessage (HYPERX 0x0041c320): post kMsgInitiate and arm the poll wait.</summary>
        public void SendTimedMessage(IPlanVmHost host)
        {
            host.SendTimedMessage(this);
            PollCountdown = Latency;
        }

        /// <summary>GameBehaviorObject::UpdateBehaviorPollCountdown (HYPERX 0x0041c360).</summary>
        public void UpdateBehaviorPollCountdown()
        {
            if (PollCountdown-- == 0)
            {
                SetInteractorsActive(1);
            }
        }

        /// <summary>GameBehaviorObject::TickAndSortMessages (HYPERX 0x0041c380); the agenda system owns the pass.</summary>
        public void TickAndSortMessages(IPlanVmHost host)
        {
            host.TickAndSortMessages(this);
        }

        /// <summary>GameBehaviorObject::Reset (HYPERX 0x0041c6c0), including the standby cascade.</summary>
        public void Reset(IPlanVmHost host)
        {
            if (Standby != null)
            {
                Standby.Reset(host);
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

            for (int index = 0; index < Slots.Length; index++)
            {
                Slots[index] = null;
            }

            AnimationFlags = -1;
            PersistentFlags = -1;
            PollCountdown = -1;
            State = null;
            AnimationRate = -1.0f;
            PersistentRate = -1.0f;
            EventTarget = 0;
            EventKind = MessageNoMsg;
            EventParam = 0;
            ChannelSlot = 0;
            Digressive = false;
            Latency = DefaultLatency;
            Standby = null;

            host.OnBehaviorReset(this);
        }

        public void FillAgendaPurged()
        {
            for (int index = 0; index < AgendaPurged.Length; index++)
            {
                AgendaPurged[index] = true;
            }
        }

        public void SetAgendaPurged(int at, bool value)
        {
            if (at < 0 || at >= AgendaPurged.Length)
            {
                throw new ArgumentOutOfRangeException("at");
            }

            AgendaPurged[at] = value;
        }
    }
}
