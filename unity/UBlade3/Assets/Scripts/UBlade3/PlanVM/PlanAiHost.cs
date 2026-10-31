using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The engine side of the bounded AI slice: an IPlanVmHost over a PlanAiWorld. It ports the
    /// engine glue the selected CARRYROK flow uses - the message handler table and dispatch
    /// (EngineObject 0x00472090/0x00472110/0x00472200), the agenda list and re-arm pass
    /// (GameBehaviorObject::TickAndSortMessages 0x0041c380), the timed-message post
    /// (GameBehaviorObject::SendTimedMessage 0x0041c320), the exit-behaviour broadcast, the plan
    /// change seam and the action/decision dispatch tables. Handler ids outside the implemented
    /// subset are recorded as explicit telemetry, never guessed.
    /// </summary>
    public sealed class PlanAiHost : IPlanVmHost
    {
        public readonly PlanAiWorld World;

        public readonly Dictionary<int, int> ActionCalls = new Dictionary<int, int>();

        public readonly Dictionary<int, int> DecisionCalls = new Dictionary<int, int>();

        public readonly Dictionary<int, int> BehaviorParamCalls = new Dictionary<int, int>();

        public readonly Dictionary<int, int> InteractorParamCalls = new Dictionary<int, int>();

        public readonly List<string> UnsupportedHandlers = new List<string>();

        public readonly List<string> PlanChangeRequests = new List<string>();

        public readonly Dictionary<int, int> AgendaAdds = new Dictionary<int, int>();

        public readonly Dictionary<int, int> AgendaRemoves = new Dictionary<int, int>();

        public int TimerArmCount;

        public int BehaviorResetCount;

        public PlanState State;

        private readonly IPlanArgResolver argResolver;

        public PlanAiHost(PlanAiWorld world, short ownerSlot)
        {
            World = world;
            World.Host = this;
            OwnerSlot = ownerSlot;
            argResolver = PlanArgResolver.ForSlot(ownerSlot);
        }

        public PlanPhase Phase
        {
            get { return PlanPhase.Local; }
        }

        public short OwnerSlot { get; private set; }

        public IPlanArgResolver ArgResolver
        {
            get { return argResolver; }
        }

        public bool HasPhysicsState
        {
            get { return true; }
        }

        public PlanAiObject Actor
        {
            get { return World.GetObject(OwnerSlot); }
        }

        /// <summary>EngineObject::FindPlanByName (0x00471150): an exact strcmp over the owner's plan table.</summary>
        public int FindPlanByName(string name)
        {
            for (int index = 0; index < World.PlanTable.Count; index++)
            {
                if (string.Equals(World.PlanTable[index], name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            World.Log("plan-name.miss name=" + name);

            return -1;
        }

        /// <summary>Compile and instantiate the shipped plan image for the actor, exactly as EngineObject::InstantiateAIPlan does.</summary>
        public PlanState InstantiatePlan(PlanFileImage image)
        {
            PlanState state = new PlanState(this, "PlanAi:" + OwnerSlot);

            if (!state.SetAndInit(image))
            {
                throw new PlanVmException("PlanState::SetAndInit failed for " + image.Name);
            }

            if (!state.Instantiate())
            {
                throw new PlanVmException("PlanState::Instantiate failed for " + image.Name);
            }

            State = state;
            Actor.PlanState = state;
            World.Log("instantiate plan=" + image.Name + " behaviours=" + state.BehaviorNodeCount +
                      " namedInteractors=" + state.NamedInteractorCount);

            return state;
        }

        public PlanBehavior CreateBehavior(short kind)
        {
            PlanBehavior behavior = new PlanBehavior();
            behavior.Kind = kind;
            behavior.OwnerSlot = OwnerSlot;

            return behavior;
        }

        public void ExecuteAction(int actionId, PlanNode node, PlanInteractor context, ref short state, int nArg)
        {
            PlanAiActionHandler handler;

            if (!PlanAiActions.TryGet(actionId, out handler))
            {
                RecordUnsupported("action", actionId);
                World.Log("unsupported.action id=" + actionId + " " + PlanOpcodes.ActionName(actionId) + " argc=" + node.ArgCount);
                return;
            }

            Increment(ActionCalls, actionId);
            handler(this, node, ref state, nArg);
        }

        public void SetBehaviorParam(int behaviorParamId, PlanNode node, PlanBehavior behavior)
        {
            Increment(BehaviorParamCalls, behaviorParamId);

            if (!PlanAiBehaviorParams.TryApply(this, behaviorParamId, node, behavior))
            {
                RecordUnsupported("behavior-parameter", behaviorParamId);
                World.Log("unsupported.behavior-param id=" + behaviorParamId + " " + PlanOpcodes.BehaviorParamName(behaviorParamId));
            }
        }

        public void SetInteractorParam(int interactorParamId, PlanNode node, PlanBehavior behavior, PlanInteractor context)
        {
            Increment(InteractorParamCalls, interactorParamId);
            RecordUnsupported("interactor-parameter", interactorParamId);
            World.Log("unsupported.interactor-param id=" + interactorParamId);
        }

        public short Decide(int decisionId, PlanNode node, PlanInteractor context, int nArg)
        {
            Increment(DecisionCalls, decisionId);

            PlanAiDecisionHandler handler;

            if (!PlanAiDecisions.TryGet(decisionId, out handler) &&
                !PlanAiDecisionExtensions.TryGet(decisionId, out handler))
            {
                RecordUnsupported("decision", decisionId);
                World.Log("unsupported.decision id=" + decisionId + " " + PlanOpcodes.DecisionName(decisionId));

                throw new PlanAiUnportedHandlerException("decision", decisionId);
            }

            short branch = handler(this, node, nArg);
            World.Log("decide id=" + decisionId + " " + PlanOpcodes.DecisionName(decisionId) + " arg=" + nArg + " branch=" + branch);

            return branch;
        }

        public void AddAgendaItem(int agendaItemId, PlanNode node, PlanBehavior behavior, int nArg)
        {
            Increment(AgendaAdds, agendaItemId);

            if (agendaItemId == PlanAiTimerActivity.AgendaTimerActivity)
            {
                PlanAiTimerActivity timer = new PlanAiTimerActivity();

                if (node != null)
                {
                    timer.BindWithNode(this, behavior, node, nArg);
                }
                else
                {
                    timer.Bind(this, behavior);
                }

                timer.Activate();
                TimerArmCount++;
                World.Log("agenda-add timer kind=" + agendaItemId);

                return;
            }

            RecordUnsupported("agenda-item", agendaItemId);
            World.Log("unsupported.agenda-add id=" + agendaItemId + " " + PlanOpcodes.AgendaItemName(agendaItemId));
        }

        public void SetTimeoutMessage(PlanNode node, PlanBehavior behavior, int nArg)
        {
            PlanAiTimerActivity timer = new PlanAiTimerActivity();
            timer.BindWithNode(this, behavior, node, nArg);
            timer.Activate();
            TimerArmCount++;
            Increment(AgendaAdds, PlanAiTimerActivity.AgendaTimerActivity);
            World.Log("agenda-add timer (SET_TIMEOUTMSG)");
        }

        public void RemoveAgendaItem(int agendaItemId, PlanBehavior behavior)
        {
            Increment(AgendaRemoves, agendaItemId);
            PlanAiObject actor = Actor;
            int removed = 0;

            if (agendaItemId == PlanAiTimerActivity.AgendaTimerActivity)
            {
                PlanAiAgendaItem item = actor.Observers;

                while (item != null)
                {
                    PlanAiAgendaItem next = item.Next;

                    if (item.Kind == agendaItemId)
                    {
                        item.Detach();
                        removed++;
                    }

                    item = next;
                }

                World.Log("agenda-remove timer kind=" + agendaItemId + " removed=" + removed);

                return;
            }

            RecordUnsupported("agenda-item", agendaItemId);
            World.Log("unsupported.agenda-remove id=" + agendaItemId + " " + PlanOpcodes.AgendaItemName(agendaItemId));
        }

        /// <summary>
        /// GameBehaviorObject::TickAndSortMessages (0x0041c380): purge the owner's agenda list, then
        /// reset and re-register the scene message handlers. The behaviour's own ten agenda slots are
        /// a subclass concern this slice does not create, so that merge is skipped (documented gap).
        /// </summary>
        public void TickAndSortMessages(PlanBehavior behavior)
        {
            PlanAiObject actor = Actor;
            int detached = 0;
            PlanAiAgendaItem item = actor.Observers;

            while (item != null)
            {
                PlanAiAgendaItem next = item.Next;
                bool purged = behavior.AgendaPurged[item.Kind];

                if (purged)
                {
                    item.Detach();
                    detached++;

                    if (!item.Persistent)
                    {
                        item = next;
                        continue;
                    }
                }

                if ((item.GetReadyFlag() || !item.Persistent) && !behavior.Digressive)
                {
                    item.Detach();
                    detached++;
                }

                item = next;
            }

            actor.ResetSceneMessageHandlers();
            RegisterMessageHandlers(actor, State == null ? null : State.MsgInteractors);
            RegisterMessageHandlers(actor, behavior.MsgInteractors);

            World.Log("activate behavior=" + behavior.Name + " kind=" + behavior.Kind + " digressive=" + (behavior.Digressive ? 1 : 0) +
                      " timersDetached=" + detached + " handlers=" + (actor.SceneHandlerCount - 1));
        }

        private void RegisterMessageHandlers(PlanAiObject actor, PlanInteractor head)
        {
            for (PlanInteractor at = head; at != null; at = at.Next)
            {
                int member = at.GetState();
                bool allowed = member == 0 || (member == 1 && Phase == PlanPhase.Local) || (member == 2 && Phase == PlanPhase.Network);

                if (allowed)
                {
                    actor.RegisterSceneMessageHandler((PlanMessageInteractor)at);
                }
                else
                {
                    World.Log("handler.not-registered message=" + PlanAiWorld.DescribeMessage(((PlanMessageInteractor)at).GetNamedSlot()) +
                              " member=" + member);
                }
            }
        }

        /// <summary>GameBehaviorObject::SendTimedMessage (0x0041c320): post kMsgInitiate to the owner.</summary>
        public void SendTimedMessage(PlanBehavior behavior)
        {
            World.Log("send-timed-message behavior=" + behavior.Name);
            World.PostSceneMessage(OwnerSlot, PlanAiConstants.MessageInitiate, 0, OwnerSlot, 0);
        }

        /// <summary>EngineObject::DispatchSceneMessage(kMsgExitBehavior, ...) before a behaviour change.</summary>
        public void DispatchExitBehavior()
        {
            World.Log("exit-behavior-dispatch behavior=" + (State == null || State.Current == null ? "?" : State.Current.Name));
            World.DispatchSceneMessage(OwnerSlot, PlanAiConstants.MessageExitBehavior, 0, DecisionLog.Fresh, OwnerSlot, 0);
        }

        /// <summary>EngineObject::ChangePlan: the owner's plan table slot and the behaviour index.</summary>
        public void ChangePlan(short planIndex, short behaviorIndex)
        {
            string planName = planIndex >= 0 && planIndex < World.PlanTable.Count ? World.PlanTable[planIndex] : "<none>";
            string line = "plan=" + planIndex + "(" + planName + ") behavior=" + behaviorIndex;

            PlanChangeRequests.Add(line);
            World.Log("change-plan-request " + line + " (mock does not instantiate another plan)");
        }

        public void EnqueueCollisionEvent(short eventKind, short otherSlot, short ownSlot, short outcome, int nArg)
        {
            World.Log("collision-event kind=" + eventKind + " other=" + otherSlot + " own=" + ownSlot + " outcome=" + outcome + " arg=" + nArg);
        }

        /// <summary>PlanState::Teardown's observer pass: every item leaves the list.</summary>
        public void ClearAgendaObserver(PlanState state)
        {
            PlanAiObject actor = Actor;
            int count = 0;
            PlanAiAgendaItem item = actor.Observers;

            while (item != null)
            {
                PlanAiAgendaItem next = item.Next;
                item.OnListChanged();
                count++;
                item = next;
            }

            actor.Observers = null;
            World.Log("clear-agenda count=" + count);
        }

        public void OnBehaviorReset(PlanBehavior behavior)
        {
            BehaviorResetCount++;
            World.Log("behavior-reset kind=" + behavior.Kind + " name=" + (behavior.Name == null ? "?" : behavior.Name));
        }

        public void RecordAction(int actionId, PlanNode node, int nArg, string detail)
        {
            World.Log("action id=" + actionId + " " + PlanOpcodes.ActionName(actionId) + " arg=" + nArg + " " + detail);
        }

        public void RecordUnsupported(string kind, int id)
        {
            string key = kind + ":" + id;

            if (!UnsupportedHandlers.Contains(key))
            {
                UnsupportedHandlers.Add(key);
            }
        }

        private static void Increment(Dictionary<int, int> table, int key)
        {
            int value;

            table.TryGetValue(key, out value);
            table[key] = value + 1;
        }
    }

    /// <summary>
    /// BehParam handlers for the selected flow. The bounded flow reaches two: SetTarget (3) and
    /// SetPersistentAnimation (9). Both are behaviour-subclass state (agenda slot targets and the
    /// animation registry), which this slice does not create, so they stay explicit telemetry rather
    /// than being faked. The registry exists so a later round can add the bodies without touching
    /// the host.
    /// </summary>
    public static class PlanAiBehaviorParams
    {
        public static bool TryApply(PlanAiHost host, int behaviorParamId, PlanNode node, PlanBehavior behavior)
        {
            return false;
        }
    }
}
