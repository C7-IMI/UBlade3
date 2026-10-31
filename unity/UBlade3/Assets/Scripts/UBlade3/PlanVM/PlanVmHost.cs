using System;

namespace UBlade3.PlanVM
{
    /// <summary>Who drives the object: src/engine/objectphase.h ObjectPhase (kLocal 1, kBuilding 2, kNetwork 3).</summary>
    public enum PlanPhase
    {
        Local = 1,
        Building = 2,
        Network = 3,
    }

    /// <summary>A plan instruction the VM cannot carry out, or a plan stream that breaks the format.</summary>
    public class PlanVmException : Exception
    {
        public PlanVmException(string message)
        : base(message)
        {
        }

        public PlanVmException(string message, int opcode)
        : base(message + " (opcode " + PlanOpcodes.KeywordOf(opcode) + ")")
        {
        }
    }

    /// <summary>
    /// A handler id with no ported handler. The VM reports it as telemetry instead of guessing; the
    /// original calls through the table's null slot (an unhandled action would crash it).
    /// </summary>
    public sealed class PlanVmUnsupportedHandlerException : PlanVmException
    {
        public readonly string HandlerKind;

        public readonly int HandlerId;

        public PlanVmUnsupportedHandlerException(string handlerKind, int handlerId)
        : base("Unsupported " + handlerKind + " handler id " + handlerId)
        {
            HandlerKind = handlerKind;
            HandlerId = handlerId;
        }
    }

    /// <summary>
    /// Every seam between the plan machine and the engine. The ported VM core owns the instruction
    /// model, the node walk, the decision log, behaviour activation and the interactor state; the
    /// engine owns object state, actions, conditions and agenda items. The Probe supplies a recording
    /// host so the whole surface can be measured without a scene.
    /// </summary>
    public interface IPlanVmHost : IPlanNameResolver
    {
        PlanPhase Phase { get; }

        short OwnerSlot { get; }

        IPlanArgResolver ArgResolver { get; }

        PlanBehavior CreateBehavior(short kind);

        void ExecuteAction(int actionId, PlanNode node, PlanInteractor context, ref short state, int nArg);

        void SetBehaviorParam(int behaviorParamId, PlanNode node, PlanBehavior behavior);

        void SetInteractorParam(int interactorParamId, PlanNode node, PlanBehavior behavior, PlanInteractor context);

        /// <summary>PlanHandlerFn: returns the branch a DECIDE takes. The engine's g_pfnPlanOpcodeHandlers tail.</summary>
        short Decide(int decisionId, PlanNode node, PlanInteractor context, int nArg);

        void AddAgendaItem(int agendaItemId, PlanNode node, PlanBehavior behavior, int nArg);

        void SetTimeoutMessage(PlanNode node, PlanBehavior behavior, int nArg);

        void RemoveAgendaItem(int agendaItemId, PlanBehavior behavior);

        /// <summary>GameBehaviorObject::TickAndSortMessages (HYPERX 0x0041c380); the agenda system owns this pass.</summary>
        void TickAndSortMessages(PlanBehavior behavior);

        /// <summary>GameBehaviorObject::SendTimedMessage (HYPERX 0x0041c320): posts kMsgInitiate and starts playback.</summary>
        void SendTimedMessage(PlanBehavior behavior);

        /// <summary>EngineObject::DispatchSceneMessage(kMsgExitBehavior, ...) on a behaviour change.</summary>
        void DispatchExitBehavior();

        /// <summary>EngineObject::ChangePlan (HYPERX 0x00471da0 caller): CHANGE_OF_PLAN's resolved plan and behaviour.</summary>
        void ChangePlan(short planIndex, short behaviorIndex);

        bool HasPhysicsState { get; }

        /// <summary>MsgEvent::Pack for a collision outcome, enqueued for the network/local pipeline.</summary>
        void EnqueueCollisionEvent(short eventKind, short otherSlot, short ownSlot, short outcome, int nArg);

        /// <summary>PlanState::Teardown's observer-list pass (0x0047b900): drops the plan's agenda items.</summary>
        void ClearAgendaObserver(PlanState state);

        /// <summary>GameBehaviorObject::Reset's return to the behaviour pool.</summary>
        void OnBehaviorReset(PlanBehavior behavior);
    }
}
