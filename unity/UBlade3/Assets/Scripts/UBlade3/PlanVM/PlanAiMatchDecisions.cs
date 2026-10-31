using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N12 decision bodies for the ids the N9/N10 default roster reached:
    ///   * 6 IsSenderTeammate (plancond.cpp 0x004626c0): reads MsgInteractor::GetTargetSlot, which the
    ///     adapter publishes as PlanAiMatchContext.CurrentMessageSender before dispatching a message;
    ///   * 23 ClosestUncoveredOppn (plancond.cpp 0x0045...): the teammate's active behaviour kind/aim
    ///     object are published by each adapter into PlanAiMatchContext (the mock worlds are per-slot),
    ///     otherwise the source's near/far opponent distance branch runs;
    ///   * 68 LooseRokWhatToDo (plancond.cpp 0x0046...): uses the match score via PlanAiMatchContext
    ///     (GameEngine::IsTeamWinning) and the four free-ROK ratings copied into the mirror stats.
    /// Every branch is the source's branch; no result is guessed.
    /// </summary>
    public static class PlanAiMatchDecisions
    {
        private const int RatingWinCloseFreeRok = 33;

        private const int RatingLoseCloseFreeRok = 34;

        private const int RatingWinFarFreeRok = 35;

        private const int RatingLoseFarFreeRok = 36;

        private const int BranchMax = 3;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.IsSenderTeammate, IsSenderTeammate);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.ClosestUncoveredOppn, ClosestUncoveredOppn);
            PlanAiDecisionExtensions.Register(PlanOpcodes.DecisionIds.LooseRokWhatToDo, LooseRokWhatToDo);
        }

        public static int[] ImplementedIds()
        {
            EnsureRegistered();

            return new int[]
            {
                PlanOpcodes.DecisionIds.IsSenderTeammate,
                PlanOpcodes.DecisionIds.ClosestUncoveredOppn,
                PlanOpcodes.DecisionIds.LooseRokWhatToDo,
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

        // plancond.cpp PlanCond::IsSenderTeammate (HYPERX 0x004626..).
        private static short IsSenderTeammate(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 1;
            }

            short ownSlot = (short)actor.Slot;
            int mate = ownSlot < PlanAiConstants.PlayerObjectCount ? ownSlot + 2 : ownSlot;
            mate &= 3;

            int sender = PlanAiMatchContext.CurrentMessageSender;
            short result = (short)(sender != mate ? 1 : 0);

            if (sender == ownSlot)
            {
                result = 2;
            }

            host.World.Log("decide.IsSenderTeammate own=" + ownSlot + " mate=" + mate + " sender=" + sender
                           + " result=" + result);

            return result;
        }

        // plancond.cpp PlanCond::ClosestUncoveredOppn (HYPERX 0x0045f...).
        private static short ClosestUncoveredOppn(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 2;
            }

            int ownSlot = actor.Slot;
            int nearSlot = OpposingFirst(ownSlot);
            int farSlot = OpposingSecond(ownSlot);
            PlanAiObject near = Player(host, nearSlot, "ClosestUncoveredOppn");
            PlanAiObject far = Player(host, farSlot, "ClosestUncoveredOppn");
            float nearDamage = near.Stats.GetScalar(PlanAiConstants.RatingDamage);
            float farDamage = far.Stats.GetScalar(PlanAiConstants.RatingDamage);
            short result;

            if (nearDamage <= host.World.DamageGate && farDamage <= host.World.DamageGate)
            {
                result = 2;
            }
            else
            {
                int carrier = host.World.Rok == null ? -1 : host.World.Rok.Body.AttachTargetSlot;

                if (carrier < 0 || carrier >= PlanAiConstants.PlayerObjectCount)
                {
                    result = 2;
                }
                else
                {
                    int mateSlot = PlanOpcodes.TeammateSlot(ownSlot);
                    int mateKind;
                    int aimSlot;
                    PlanAiMatchContext.GetBehavior(mateSlot, out mateKind, out aimSlot);

                    if (mateKind == PlanOpcodes.BehaviorIds.GlideBetweenFacing
                        && aimSlot >= 0 && aimSlot <= PlanAiConstants.PlayerObjectCount - 1)
                    {
                        int masked = PlanOpcodes.TeammateSlot(aimSlot);

                        result = (short)(masked >= aimSlot ? 1 : 0);
                    }
                    else
                    {
                        PlanAiObject nearAt = Player(host, OpposingFirst(ownSlot), "ClosestUncoveredOppn");
                        PlanAiObject farAt = Player(host, OpposingSecond(ownSlot), "ClosestUncoveredOppn");
                        float nearDx = nearAt.Body.Position.X - actor.Body.Position.X;
                        float nearDy = nearAt.Body.Position.Y - actor.Body.Position.Y;
                        float nearDz = nearAt.Body.Position.Z - actor.Body.Position.Z;
                        float farDx = farAt.Body.Position.X - actor.Body.Position.X;
                        float farDy = farAt.Body.Position.Y - actor.Body.Position.Y;
                        float farDz = farAt.Body.Position.Z - actor.Body.Position.Z;
                        float nearDistance = (float)Math.Sqrt(nearDx * nearDx + nearDy * nearDy + nearDz * nearDz);
                        float farDistance = (float)Math.Sqrt(farDx * farDx + farDy * farDy + farDz * farDz);

                        result = (short)(nearDistance < farDistance ? 0 : 1);
                    }
                }
            }

            if (result == 0 && nearDamage <= host.World.DamageGate)
            {
                result = 1;
            }
            else if (result == 1 && farDamage <= host.World.DamageGate)
            {
                result = 0;
            }

            host.World.Log("decide.ClosestUncoveredOppn actor=" + ownSlot + " nearSlot=" + nearSlot
                           + " farSlot=" + farSlot + " result=" + result);
            return result;
        }

        // plancond.cpp PlanCond::LooseRokWhatToDo (HYPERX 0x0046...).
        private static short LooseRokWhatToDo(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return 0;
            }

            PlanAiObject rok = host.World.Rok;
            PlanAiObject mate = host.World.GetPlayer(PlanOpcodes.TeammateSlot(actor.Slot));

            if (rok == null || mate == null)
            {
                return 0;
            }

            float ownX = rok.Body.Position.X - actor.Body.Position.X;
            float ownY = rok.Body.Position.Y - actor.Body.Position.Y;
            float ownZ = rok.Body.Position.Z - actor.Body.Position.Z;
            float mateX = rok.Body.Position.X - mate.Body.Position.X;
            float mateY = rok.Body.Position.Y - mate.Body.Position.Y;
            float mateZ = rok.Body.Position.Z - mate.Body.Position.Z;
            short result;
            int winFlag = PlanAiMatchContext.IsTeamWinning(actor.Slot) ? 1 : 0;

            if (ownX * ownX + ownY * ownY + ownZ * ownZ < mateX * mateX + mateY * mateY + mateZ * mateZ)
            {
                result = (short)(int)actor.Stats.GetScalar(
                    winFlag != 0 ? RatingWinCloseFreeRok : RatingLoseCloseFreeRok);
            }
            else
            {
                result = (short)(int)actor.Stats.GetScalar(
                    winFlag != 0 ? RatingWinFarFreeRok : RatingLoseFarFreeRok);
            }

            if (result < 0 || result > BranchMax)
            {
                result = 0;
            }

            PlanAiObject near = host.World.GetPlayer(OpposingFirst(actor.Slot));
            PlanAiObject far = host.World.GetPlayer(OpposingSecond(actor.Slot));

            if (near != null && far != null
                && near.Stats.GetScalar(PlanAiConstants.RatingDamage) <= host.World.DamageGate
                && far.Stats.GetScalar(PlanAiConstants.RatingDamage) <= host.World.DamageGate)
            {
                result = 0;
            }

            host.World.Log("decide.LooseRokWhatToDo actor=" + actor.Slot + " winFlag=" + winFlag
                           + " ownNearer=" + (ownX * ownX + ownY * ownY + ownZ * ownZ < mateX * mateX + mateY * mateY + mateZ * mateZ)
                           + " result=" + result);
            return result;
        }

        private static int OpposingFirst(int slot)
        {
            return (~slot) & 1;
        }

        private static int OpposingSecond(int slot)
        {
            return OpposingFirst(slot) | 2;
        }
    }
}
