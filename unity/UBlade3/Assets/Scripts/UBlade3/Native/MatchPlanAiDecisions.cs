using System;
using UBlade3.PlanVM;

namespace UBlade3.Native
{
    /// <summary>
    /// The decision bodies the N9 multi-slot plan roster needs, ported from src/planvm/plancond.cpp and
    /// registered through PlanAiDecisionExtensions (so the verified four-decision Plan AI slice keeps its
    /// own pinned table). Ids are src/planvm/planfunctionids.h: 5 Random, 14 IHaveRok, 18 TeammateHasRok,
    /// 33 MyID, 97 AnimationSemaphore, 98 ClockStoppedSemaphore.
    ///
    /// The bodies are read-only reads of the plan mirror / live-role state; anything not ported stays in
    /// PlanAiHost.UnsupportedHandlers and, in the match roster, in the per-slot unsupported telemetry.
    /// </summary>
    public static class MatchPlanAiDecisions
    {
        // planvm/planfunctionids.h.
        public const int Random = 5;

        public const int IHaveRok = 14;

        public const int TeammateHasRok = 18;

        public const int MyID = 33;

        public const int AnimationSemaphore = 97;

        public const int ClockStoppedSemaphore = 98;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PlanAiDecisionExtensions.Register(Random, RandomDecision);
            PlanAiDecisionExtensions.Register(IHaveRok, IHaveRokDecision);
            PlanAiDecisionExtensions.Register(TeammateHasRok, TeammateHasRokDecision);
            PlanAiDecisionExtensions.Register(MyID, MyIdDecision);
            PlanAiDecisionExtensions.Register(AnimationSemaphore, AnimationSemaphoreDecision);
            PlanAiDecisionExtensions.Register(ClockStoppedSemaphore, ClockStoppedSemaphoreDecision);
        }

        public static int[] ImplementedIds()
        {
            EnsureRegistered();

            return PlanAiDecisionExtensions.ImplementedIds();
        }

        // plancond.cpp PlanCond::Random (0x0045f6..): rand() * (argc - 1) / (RAND_MAX + 1).
        private static short RandomDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            int branches = node.ArgCount - 1;

            if (branches <= 0)
            {
                return 0;
            }

            long rolled = (long)PlanAiRandom.Rand() * branches;

            return (short)(rolled / ((long)PlanAiRandom.RandMax + 1L));
        }

        // plancond.cpp PlanCond::IHaveRok (0x00465d..): plain form answers 0 have / 1 not; the flagged
        // form answers 2 have / 3 not (used by the drop/kick handlers).
        private static short IHaveRokDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;
            PlanAiObject rok = host.World.Rok;
            bool have = rok != null && actor != null && rok.Body.AttachTargetSlot == actor.Slot;

            if (nArg != 0)
            {
                return (short)(have ? 2 : 3);
            }

            return (short)(have ? 0 : 1);
        }

        // plancond.cpp PlanCond::TeammateHasRok (0x00461f..): 0 when the same-parity team mate holds it.
        private static short TeammateHasRokDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;
            PlanAiObject rok = host.World.Rok;

            if (actor != null && rok != null && rok.Body.AttachTargetSlot >= 0 &&
                rok.Body.AttachTargetSlot < PlanAiConstants.PlayerObjectCount &&
                ((actor.Slot ^ rok.Body.AttachTargetSlot) & 1) == 0)
            {
                return 0;
            }

            return 1;
        }

        // plancond.cpp PlanCond::MyID (0x00462820): the player slot, or the viewed slot for non-players.
        private static short MyIdDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            return (short)(actor != null && actor.Slot < PlanAiConstants.PlayerObjectCount ? actor.Slot : 0);
        }

        // plancond.cpp PlanAction::AnimationSemaphore (0x00463dc0), also registered as decision 97:
        // two words write the semaphore, every other shape reads it.
        private static short AnimationSemaphoreDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 0;
            }

            if (node.ArgCount == 2)
            {
                actor.AnimationSemaphore = node.Args[1].IntValue;
            }

            return (short)(actor.AnimationSemaphore != 0 ? 1 : 0);
        }

        // plancond.cpp PlanAction::ClockStoppedSemaphore (0x00463e00), decision 98.
        private static short ClockStoppedSemaphoreDecision(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 0;
            }

            if (node.ArgCount == 2)
            {
                actor.ClockStoppedSemaphore = node.Args[1].IntValue;
            }

            return (short)(actor.ClockStoppedSemaphore != 0 ? 1 : 0);
        }
    }
}
