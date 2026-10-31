using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N13 decision bodies for the remaining observed default-roster ids:
    ///   * 51 WhichOppnIsClosestToMe (plancond.cpp HYPERX 0x00460f20);
    ///   * 71 OffWingJob (plancond.cpp HYPERX 0x004682d0);
    ///   * 120 InRangeToAttack (plancond.cpp HYPERX 0x00460240).
    /// The source branches, damage gate and two distance fallbacks are ported; OffWingJob uses the
    /// Win/LoseOffWingJob ratings (ids 37/38) copied into the mirror stats by MatchPlanAi.
    /// </summary>
    public static class PlanAiMatchDecisionsN13
    {
        private const int RatingWinOffWingJob = 37;

        private const int RatingLoseOffWingJob = 38;

        private const int OffWingJobMax = 2;

        private const float FarRangeSquared = 500.0f;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.WhichOppnIsClosestToMe, WhichOppnIsClosestToMe);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.OffWingJob, OffWingJob);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.InRangeToAttack, InRangeToAttack);
        }

        public static int[] ImplementedIds()
        {
            EnsureRegistered();

            return new int[]
            {
                PlanOpcodes.DecisionIds.WhichOppnIsClosestToMe,
                PlanOpcodes.DecisionIds.OffWingJob,
                PlanOpcodes.DecisionIds.InRangeToAttack,
            };
        }

        private static int OpposingFirst(int slot)
        {
            return (~slot) & 1;
        }

        private static int OpposingSecond(int slot)
        {
            return OpposingFirst(slot) | 2;
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

        // plancond.cpp PlanCond::WhichOppnIsClosestToMe (HYPERX 0x00460f20).
        private static short WhichOppnIsClosestToMe(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 2;
            }

            int nearSlot = OpposingFirst(actor.Slot);
            int farSlot = OpposingSecond(actor.Slot);
            PlanAiObject near = Player(host, nearSlot, "WhichOppnIsClosestToMe");
            PlanAiObject far = Player(host, farSlot, "WhichOppnIsClosestToMe");
            float damageA = near.Stats.GetScalar(PlanAiConstants.RatingDamage);
            float damageB = far.Stats.GetScalar(PlanAiConstants.RatingDamage);
            float distanceA = actor.GetDistIfFacing(near, PlanAiConstants.HalfRokObjectFacing);
            float distanceB = actor.GetDistIfFacing(far, PlanAiConstants.HalfRokObjectFacing);
            short result;

            if (damageA <= host.World.DamageGate)
            {
                distanceA = 0.0f;
            }

            if (damageB <= host.World.DamageGate)
            {
                distanceB = 0.0f;
            }

            if (distanceA == 0.0f && distanceB == 0.0f)
            {
                PlanAiVec3 offsetA = near.Body.Position - actor.Body.Position;
                PlanAiVec3 offsetB = far.Body.Position - actor.Body.Position;

                if (damageA > host.World.DamageGate)
                {
                    distanceA = (float)Math.Sqrt(offsetA.X * offsetA.X + offsetA.Y * offsetA.Y + offsetA.Z * offsetA.Z);
                }

                if (damageB > host.World.DamageGate)
                {
                    distanceB = (float)Math.Sqrt(offsetB.X * offsetB.X + offsetB.Y * offsetB.Y + offsetB.Z * offsetB.Z);
                }
            }

            if (distanceA == 0.0f)
            {
                result = (short)(distanceB == 0.0f ? 2 : 1);
            }
            else if (distanceB != 0.0f && distanceA < distanceB)
            {
                result = 0;
            }
            else if (distanceB == 0.0f)
            {
                result = 0;
            }
            else
            {
                result = 1;
            }

            if (damageA <= host.World.DamageGate && damageB <= host.World.DamageGate)
            {
                result = 2;
            }

            host.World.Log("decide.WhichOppnIsClosestToMe actor=" + actor.Slot + " near=" + nearSlot
                           + " far=" + farSlot + " distA=" + distanceA.ToString("0.###")
                           + " distB=" + distanceB.ToString("0.###") + " result=" + result);
            return result;
        }

        // plancond.cpp PlanCond::OffWingJob (HYPERX 0x004682d0).
        private static short OffWingJob(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 0;
            }

            bool winning = PlanAiMatchContext.IsTeamWinning(actor.Slot);
            int result = (int)actor.Stats.GetScalar(winning ? RatingWinOffWingJob : RatingLoseOffWingJob);

            if (result < 0 || result > OffWingJobMax)
            {
                result = 0;
            }

            PlanAiObject near = Player(host, OpposingFirst(actor.Slot), "OffWingJob");
            PlanAiObject far = Player(host, OpposingSecond(actor.Slot), "OffWingJob");
            float damageA = near.Stats.GetScalar(PlanAiConstants.RatingDamage);
            float damageB = far.Stats.GetScalar(PlanAiConstants.RatingDamage);

            if (result == 1 && damageA <= host.World.DamageGate && damageB <= host.World.DamageGate)
            {
                result = 2;
            }

            host.World.Log("decide.OffWingJob actor=" + actor.Slot + " winning=" + (winning ? 1 : 0)
                           + " result=" + result);
            return (short)result;
        }

        // plancond.cpp PlanCond::InRangeToAttack (HYPERX 0x00460240).
        private static short InRangeToAttack(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 1;
            }

            PlanAiObject near = Player(host, OpposingFirst(actor.Slot), "InRangeToAttack");
            PlanAiObject far = Player(host, OpposingSecond(actor.Slot), "InRangeToAttack");
            PlanAiVec3 nearDelta = near.Body.Position - actor.Body.Position;
            PlanAiVec3 farDelta = far.Body.Position - actor.Body.Position;
            float nearSquared = PlanAiVec3.Dot(nearDelta, nearDelta);
            float farSquared = PlanAiVec3.Dot(farDelta, farDelta);
            short result = (short)(nearSquared >= FarRangeSquared && farSquared >= FarRangeSquared ? 0 : 1);

            host.World.Log("decide.InRangeToAttack actor=" + actor.Slot + " nearSq=" + nearSquared.ToString("0.###")
                           + " farSq=" + farSquared.ToString("0.###") + " result=" + result);
            return result;
        }
    }
}
