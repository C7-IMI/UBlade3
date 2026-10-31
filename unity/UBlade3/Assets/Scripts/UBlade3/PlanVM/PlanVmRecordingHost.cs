using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// A scene-free IPlanVmHost that records every engine seam instead of simulating it. The Probe
    /// uses it to measure the whole plan surface without a match: every action, decision, parameter
    /// set, agenda item and plan/behaviour change the data names, plus the unsupported handler ids a
    /// guess would hide.
    /// </summary>
    public sealed class PlanVmRecordingHost : IPlanVmHost
    {
        public readonly List<string> Events = new List<string>();

        public readonly List<int> Actions = new List<int>();

        public readonly List<int> BehaviorParams = new List<int>();

        public readonly List<int> InteractorParams = new List<int>();

        public readonly List<int> Decisions = new List<int>();

        public readonly List<int> AgendaAdds = new List<int>();

        public readonly List<int> AgendaRemoves = new List<int>();

        public readonly List<int> Timeouts = new List<int>();

        public readonly List<string> Changes = new List<string>();

        public readonly List<string> CollisionEvents = new List<string>();

        public readonly HashSet<string> UnsupportedHandlers = new HashSet<string>();

        public readonly List<string> BehaviorKinds = new List<string>();

        public int ExitBehaviorCount;

        public int TimedMessageCount;

        public int TickAndSortCount;

        public int ResetCount;

        public int ClearObserverCount;

        public PlanPhase Phase { get; set; }

        public short OwnerSlot { get; set; }

        public IPlanArgResolver ArgResolver { get; set; }

        public IPlanNameResolver NameResolver { get; set; }

        public bool HasPhysicsState { get; set; }

        /// <summary>Returns the branch a DECIDE takes; by default 1 for a WITH_AMONG (slot nought is its DataBlock) and 0 otherwise.</summary>
        public Func<int, PlanNode, short> DecisionPolicy;

        public PlanVmRecordingHost(short ownerSlot, IPlanArgResolver argResolver, IPlanNameResolver nameResolver)
        {
            OwnerSlot = ownerSlot;
            ArgResolver = argResolver;
            NameResolver = nameResolver;
            Phase = PlanPhase.Local;
        }

        public void ResetTelemetry()
        {
            Events.Clear();
            Actions.Clear();
            BehaviorParams.Clear();
            InteractorParams.Clear();
            Decisions.Clear();
            AgendaAdds.Clear();
            AgendaRemoves.Clear();
            Timeouts.Clear();
            Changes.Clear();
            CollisionEvents.Clear();
            UnsupportedHandlers.Clear();
            BehaviorKinds.Clear();
            ExitBehaviorCount = 0;
            TimedMessageCount = 0;
            TickAndSortCount = 0;
            ResetCount = 0;
            ClearObserverCount = 0;
        }

        public int FindPlanByName(string name)
        {
            return NameResolver == null ? -1 : NameResolver.FindPlanByName(name);
        }

        public PlanBehavior CreateBehavior(short kind)
        {
            BehaviorKinds.Add(kind.ToString());

            PlanBehavior behavior = new PlanBehavior();
            behavior.Kind = kind;
            behavior.OwnerSlot = OwnerSlot;

            return behavior;
        }

        public void ExecuteAction(int actionId, PlanNode node, PlanInteractor context, ref short state, int nArg)
        {
            if (!PlanOpcodes.IsSupportedActionId(actionId))
            {
                UnsupportedHandlers.Add("action:" + actionId);
                throw new PlanVmUnsupportedHandlerException("action", actionId);
            }

            Actions.Add(actionId);
            Events.Add("action:" + PlanOpcodes.ActionName(actionId));
        }

        public void SetBehaviorParam(int behaviorParamId, PlanNode node, PlanBehavior behavior)
        {
            if (!PlanOpcodes.IsSupportedBehaviorParamId(behaviorParamId))
            {
                UnsupportedHandlers.Add("behaviorparam:" + behaviorParamId);
                throw new PlanVmUnsupportedHandlerException("behavior parameter", behaviorParamId);
            }

            BehaviorParams.Add(behaviorParamId);
            Events.Add("behaviorparam:" + PlanOpcodes.BehaviorParamName(behaviorParamId));
        }

        public void SetInteractorParam(int interactorParamId, PlanNode node, PlanBehavior behavior, PlanInteractor context)
        {
            if (!PlanOpcodes.IsSupportedInteractorParamId(interactorParamId))
            {
                UnsupportedHandlers.Add("interactorparam:" + interactorParamId);
                throw new PlanVmUnsupportedHandlerException("interactor parameter", interactorParamId);
            }

            InteractorParams.Add(interactorParamId);
            Events.Add("interactorparam:" + PlanOpcodes.InteractorParamName(interactorParamId));
        }

        public short Decide(int decisionId, PlanNode node, PlanInteractor context, int nArg)
        {
            if (!PlanOpcodes.IsSupportedDecisionId(decisionId))
            {
                UnsupportedHandlers.Add("decision:" + decisionId);
                throw new PlanVmUnsupportedHandlerException("decision", decisionId);
            }

            short branch;

            if (DecisionPolicy != null)
            {
                branch = DecisionPolicy(decisionId, node);
            }
            else
            {
                branch = node.Opcode == PlanOpcodes.OpcodeDecideByWithAmong ? (short)1 : (short)0;
            }

            Decisions.Add(decisionId);
            Events.Add("decide:" + PlanOpcodes.DecisionName(decisionId) + "=" + branch);

            return branch;
        }

        public void AddAgendaItem(int agendaItemId, PlanNode node, PlanBehavior behavior, int nArg)
        {
            if (agendaItemId < 0 || agendaItemId >= PlanOpcodes.AgendaItemCount)
            {
                UnsupportedHandlers.Add("agenda:" + agendaItemId);
                throw new PlanVmUnsupportedHandlerException("agenda item", agendaItemId);
            }

            AgendaAdds.Add(agendaItemId);
            Events.Add("agendaadd:" + PlanOpcodes.AgendaItemName(agendaItemId) + (node == null ? ":bind" : ":node"));
        }

        public void SetTimeoutMessage(PlanNode node, PlanBehavior behavior, int nArg)
        {
            Timeouts.Add(node.ChildCount);
            Events.Add("timeout:argc=" + node.ChildCount);
        }

        public void RemoveAgendaItem(int agendaItemId, PlanBehavior behavior)
        {
            AgendaRemoves.Add(agendaItemId);
            Events.Add("agendaremove:" + PlanOpcodes.AgendaItemName(agendaItemId));
        }

        public void TickAndSortMessages(PlanBehavior behavior)
        {
            TickAndSortCount++;
            Events.Add("tick:" + behavior.Name);
        }

        public void SendTimedMessage(PlanBehavior behavior)
        {
            TimedMessageCount++;
            Events.Add("timedmessage:" + behavior.Name);
        }

        public void DispatchExitBehavior()
        {
            ExitBehaviorCount++;
            Events.Add("exitbehavior");
        }

        public void ChangePlan(short planIndex, short behaviorIndex)
        {
            Changes.Add(planIndex + ":" + behaviorIndex);
            Events.Add("changeplan:" + planIndex + ":" + behaviorIndex);
        }

        public void EnqueueCollisionEvent(short eventKind, short otherSlot, short ownSlot, short outcome, int nArg)
        {
            CollisionEvents.Add(eventKind + ":" + otherSlot + ":" + ownSlot + ":" + outcome + ":" + nArg);
            Events.Add("collisionevent:" + eventKind + ":" + otherSlot + ":" + ownSlot + ":" + outcome);
        }

        public void ClearAgendaObserver(PlanState state)
        {
            ClearObserverCount++;
            Events.Add("clearobserver");
        }

        public void OnBehaviorReset(PlanBehavior behavior)
        {
            ResetCount++;
            Events.Add("reset:" + behavior.Kind);
        }
    }
}
