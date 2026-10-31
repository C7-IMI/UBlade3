using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N14: the last observed default-roster decision bodies.
    ///   31 RokInWhichTeamsPoss (plancond.cpp HYPERX 0x00461b20)
    ///   57 MeOrTeammateClosestToRok (plancond.cpp HYPERX 0x004663f0)
    ///   123 CheckPassingDistance (plancond.cpp HYPERX 0x00466a80)
    /// Enabled only by the N14/default roster so the pinned N4-N13 suites keep the N13 handler set.
    /// </summary>
    public static class PlanAiMatchDecisionsN14
    {
        public static bool Enabled;

        private const float PassNearRangeSquared = 22000.0f;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.RokInWhichTeamsPoss, RokInWhichTeamsPoss);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.MeOrTeammateClosestToRok, MeOrTeammateClosestToRok);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.CheckPassingDistance, CheckPassingDistance);
        }

        public static int[] ImplementedIds()
        {
            EnsureRegistered();

            return new int[]
            {
                PlanOpcodes.DecisionIds.RokInWhichTeamsPoss,
                PlanOpcodes.DecisionIds.MeOrTeammateClosestToRok,
                PlanOpcodes.DecisionIds.CheckPassingDistance,
            };
        }

        private static PlanAiObject Player(PlanAiHost host, int slot, string decision)
        {
            PlanAiObject player = host.World.GetPlayer(slot);

            if (player == null)
            {
                throw new PlanAiFatalException(decision + " needs player slot " + slot + " and the mock world has none.");
            }

            return player;
        }

        private static void RequireEnabled(int decisionId)
        {
            if (!Enabled)
            {
                throw new PlanAiUnportedHandlerException("decision", decisionId);
            }
        }

        // plancond.cpp PlanCond::RokInWhichTeamsPoss (HYPERX 0x00461b20).
        private static short RokInWhichTeamsPoss(PlanAiHost host, PlanNode node, int nArg)
        {
            RequireEnabled(PlanOpcodes.DecisionIds.RokInWhichTeamsPoss);
            int carrier = host.World.Rok == null ? -1 : host.World.Rok.Body.AttachTargetSlot;

            if (carrier >= 0 && carrier < PlanAiConstants.PlayerObjectCount)
            {
                return (short)(carrier & 1);
            }

            return 2;
        }

        // plancond.cpp PlanCond::MeOrTeammateClosestToRok (HYPERX 0x004663f0).
        private static short MeOrTeammateClosestToRok(PlanAiHost host, PlanNode node, int nArg)
        {
            RequireEnabled(PlanOpcodes.DecisionIds.MeOrTeammateClosestToRok);
            PlanAiObject actor = host.Actor;

            if (actor == null || host.World.Rok == null)
            {
                return 1;
            }

            int mateSlot = PlanOpcodes.TeammateSlot(actor.Slot);
            PlanAiObject mate = Player(host, mateSlot, "MeOrTeammateClosestToRok");
            PlanAiVec3 rok = host.World.Rok.Body.Position;
            PlanAiVec3 own = rok - actor.Body.Position;
            PlanAiVec3 other = rok - mate.Body.Position;
            short result = PlanAiVec3.Dot(own, own) < PlanAiVec3.Dot(other, other) ? (short)0 : (short)1;
            float mateDamage = mate.Stats.GetScalar(PlanAiConstants.RatingDamage);

            if (result == 1 && mateDamage <= host.World.DamageGate)
            {
                result = 0;
            }

            int carrier = host.World.Rok.Body.AttachTargetSlot;

            if (carrier >= 0 && carrier < PlanAiConstants.PlayerObjectCount
                && (carrier == actor.Slot || carrier == mateSlot))
            {
                result = 1;
            }

            host.World.Log("decide.MeOrTeammateClosestToRok actor=" + actor.Slot + " mate=" + mateSlot
                           + " result=" + result);
            return result;
        }

        // plancond.cpp PlanCond::CheckPassingDistance (HYPERX 0x00466a80).
        private static short CheckPassingDistance(PlanAiHost host, PlanNode node, int nArg)
        {
            RequireEnabled(PlanOpcodes.DecisionIds.CheckPassingDistance);
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 1;
            }

            PlanAiObject mate = Player(host, PlanOpcodes.TeammateSlot(actor.Slot), "CheckPassingDistance");
            PlanAiVec3 delta = mate.Body.Position - actor.Body.Position;

            if (mate.Stats.GetScalar(PlanAiConstants.RatingLongPassEnable) != 0.0f)
            {
                return 0;
            }

            if (PlanAiVec3.Dot(delta, delta) < PassNearRangeSquared && mate.KnockedDownState == 0)
            {
                return 0;
            }

            host.World.Log("decide.CheckPassingDistance actor=" + actor.Slot + " distanceSq="
                           + PlanAiVec3.Dot(delta, delta).ToString("0.###") + " result=1");
            return 1;
        }
    }
}
