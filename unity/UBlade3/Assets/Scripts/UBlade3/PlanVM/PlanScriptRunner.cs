using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// Interactor::ExecuteScript (HYPERX 0x0045d860): the plan machine's instruction walker.
    /// It is the whole runtime opcode dispatch the decompilation documents, with the engine pieces
    /// behind IPlanVmHost: DO_* actions, SET_*_PARAM handlers, DECIDE handlers, agenda items and the
    /// plan/behaviour changes a script ends with.
    /// </summary>
    public static class PlanScriptRunner
    {
        /// <summary>Runs count node operands from operands[startIndex..]; ports ExecuteScript's pCursor/nCount walk.</summary>
        public static void ExecuteScript(PlanOperand[] operands, int startIndex, int count, PlanInteractor context, ref short state, int nArg, IPlanVmHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }

            PlanState planState = context.State;

            if (planState == null)
            {
                throw new PlanVmException("Script has no plan state (Interactor::m_pOwner would be null).");
            }

            for (int index = 0; index < count; index++)
            {
                PlanOperand operand = operands[startIndex + index];

                if (operand == null || operand.Kind != PlanOperandKind.Node)
                {
                    throw new PlanVmException("Script cursor points at a non-instruction operand (ExecuteScript reads pCursor->pNode).");
                }

                PlanNode node = operand.Node;
                int category = node.Category;

                switch (category)
                {
                    case (int)PlanOpcodes.Category.Action:
                        ExecuteActionNode(node, context, planState, ref state, nArg, host);
                        break;

                    case (int)PlanOpcodes.Category.Behaviour:
                        ExecuteBehaviourNode(node, context, planState, host);
                        break;

                    case (int)PlanOpcodes.Category.PlanChange:
                        if (node.Member == (int)PlanOpcodes.PlanChangeMember.ChangeOfPlan)
                        {
                            context.Slot = node.Args[0].ShortValue;
                            context.Behaviour = node.Args[1].ShortValue;
                        }

                        break;

                    case (int)PlanOpcodes.Category.Decide:
                        ExecuteDecideNode(node, context, planState, ref state, nArg, host);
                        break;

                    case (int)PlanOpcodes.Category.Agenda:
                        ExecuteAgendaNode(node, context, planState, nArg, host);
                        break;

                    case (int)PlanOpcodes.Category.Block:
                        ExecuteScript(node.Args, 0, node.ChildCount, context, ref state, nArg, host);
                        break;

                    default:
                        // STRING: HYPERX 0x004c4494
                        throw new PlanVmException("Invalid Instruction object in Interactor Process", node.Opcode);
                }
            }
        }

        private static void ExecuteActionNode(PlanNode node, PlanInteractor context, PlanState planState, ref short state, int nArg, IPlanVmHost host)
        {
            int actionId;

            switch (node.Member)
            {
                case (int)PlanOpcodes.ActionMember.DoNetAction:
                    if (host.Phase == PlanPhase.Local)
                    {
                        return;
                    }

                    actionId = node.Args[0].IntValue;
                    DispatchAction(actionId, node, context, ref state, nArg, host);
                    return;

                case (int)PlanOpcodes.ActionMember.DoLocalAction:
                    if (host.Phase == PlanPhase.Network)
                    {
                        return;
                    }

                    // Falls through: DO_LOCAL_ACTION runs the same handler as DO_ACTION off the network.
                    actionId = node.Args[0].IntValue;
                    DispatchAction(actionId, node, context, ref state, nArg, host);
                    return;

                case (int)PlanOpcodes.ActionMember.DoAction:
                    actionId = node.Args[0].IntValue;
                    DispatchAction(actionId, node, context, ref state, nArg, host);
                    return;

                case (int)PlanOpcodes.ActionMember.SetInteractorParam:
                    host.SetInteractorParam(node.Args[0].IntValue, node, planState.Current, context);
                    return;

                default:
                    // The original's inner switch has no default arm, so an unknown ACTION member runs nothing.
                    return;
            }
        }

        private static void DispatchAction(int actionId, PlanNode node, PlanInteractor context, ref short state, int nArg, IPlanVmHost host)
        {
            if (!PlanOpcodes.IsSupportedActionId(actionId))
            {
                // The original would call a null slot here; this port reports the id instead of guessing.
                throw new PlanVmUnsupportedHandlerException("action", actionId);
            }

            host.ExecuteAction(actionId, node, context, ref state, nArg);
        }

        private static void ExecuteBehaviourNode(PlanNode node, PlanInteractor context, PlanState planState, IPlanVmHost host)
        {
            switch (node.Member)
            {
                case (int)PlanOpcodes.BehaviourMember.ChangeToBehavior:
                    context.BehaviourIndex = node.Args[0].IntValue;
                    return;

                case (int)PlanOpcodes.BehaviourMember.SetBehaviorParam:
                    host.SetBehaviorParam(node.Args[0].IntValue, node, planState.Current);
                    return;

                default:
                    // The original's inner switch has no default arm.
                    return;
            }
        }

        private static void ExecuteDecideNode(PlanNode node, PlanInteractor context, PlanState planState, ref short state, int nArg, IPlanVmHost host)
        {
            short branch;

            if (DecisionLog.IsRecording(state))
            {
                int decisionId = node.Args[0].IntValue;

                if (!PlanOpcodes.IsSupportedDecisionId(decisionId))
                {
                    throw new PlanVmUnsupportedHandlerException("decision", decisionId);
                }

                branch = host.Decide(decisionId, node, context, nArg);
                DecisionLog.Push(ref state, branch);
            }
            else
            {
                branch = 0;
                DecisionLog.Pop(ref state, ref branch);
            }

            if (node.ArgCount - 1 > branch)
            {
                ExecuteScript(node.Args, 1 + branch, 1, context, ref state, nArg, host);
            }
        }

        private static void ExecuteAgendaNode(PlanNode node, PlanInteractor context, PlanState planState, int nArg, IPlanVmHost host)
        {
            switch (node.Opcode)
            {
                case PlanOpcodes.OpcodeAddAgendaItem:
                    host.AddAgendaItem(node.Args[0].IntValue, node, planState.Current, nArg);
                    return;

                case PlanOpcodes.OpcodeSetTimeoutMsg:
                    host.SetTimeoutMessage(node, planState.Current, nArg);
                    return;

                case PlanOpcodes.OpcodeRemoveAgendaItem:
                    host.RemoveAgendaItem(node.Args[0].IntValue, planState.Current);
                    return;

                default:
                    // The original's switch has no default arm.
                    return;
            }
        }
    }
}
