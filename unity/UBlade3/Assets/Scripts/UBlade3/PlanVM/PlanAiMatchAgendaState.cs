using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>Plan-side state for the N13 behaviour-parameter bodies (keyed by the behaviour).</summary>
    public sealed class PlanAiBehaviorState
    {
        public int ObjectA = -1;

        public int ObjectB = -1;

        public int AimObject = -1;

        public float BlendStep;

        public int TargetSlot = -1;

        public int TargetGoal = PlanOpcodes.SlotNgoal;

        public bool TargetGoalDirty;

        public int AnimationIndex = -1;

        public int PersistentAnimationIndex = -1;

        public int Applied;

        public int AnimationEvents;

        public int LastAnimationEventKind = -1;
    }

    public static class PlanAiBehaviorStateStore
    {
        private static readonly Dictionary<PlanBehavior, PlanAiBehaviorState> States =
            new Dictionary<PlanBehavior, PlanAiBehaviorState>();

        public static PlanAiBehaviorState Get(PlanBehavior behavior)
        {
            PlanAiBehaviorState state;

            if (!States.TryGetValue(behavior, out state))
            {
                state = new PlanAiBehaviorState();
                States[behavior] = state;
            }

            return state;
        }
    }

    /// <summary>Plan-side state an agenda item writes and MatchPlanAi applies to the real body.</summary>
    public sealed class PlanAiAgendaState
    {
        public PlanAiVec3 SmoothVelAccum;

        public bool PositionDirty;

        public PlanAiVec3 Position;

        public bool HeadingDirty;

        public PlanAiVec3 Heading;

        public int AvoidAdvanceEvents;

        public int RelaxAdvanceEvents;

        public int BlockedEvents;

        public int BreakawayEvents;

        public short WrongSideFrames;

        public PlanAiVec3 NetTarget;

        public bool NetTargetDirty;

        public int ReleaseEvents;

        public int CameraBiasEvents;
    }

    public static class PlanAiAgendaStateStore
    {
        private static readonly Dictionary<PlanAiObject, PlanAiAgendaState> States =
            new Dictionary<PlanAiObject, PlanAiAgendaState>();

        public static PlanAiAgendaState Get(PlanAiObject actor)
        {
            PlanAiAgendaState state;

            if (!States.TryGetValue(actor, out state))
            {
                state = new PlanAiAgendaState();
                States[actor] = state;
            }

            return state;
        }
    }
}
