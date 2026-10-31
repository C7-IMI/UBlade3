using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    public delegate short PlanAiDecisionHandler(PlanAiHost host, PlanNode node, int nArg);

    /// <summary>
    /// The DECIDE handler bodies the selected CARRYROK flow needs, each ported from src/planvm/plancond.cpp:
    /// DecidePosition 0x00462d20, OkToPassToTeammate 0x00462c40, RecvrInRangeNView 0x00466b70 and
    /// ShootSituation 0x00468650. Every other decision id is explicit unsupported telemetry in the host.
    /// </summary>
    public static class PlanAiDecisions
    {
        // plancond.cpp constants.
        private const int SlotOppositeStep = 2;

        private const int SlotOppositeWrap = 3;

        private const int SlotOppositeBelow = 4;

        private const int SlotFarHalf = 2;

        private const int RokTargeted = 2;

        private const int StateHeld = 4;

        private const int StateGated = 5;

        private static readonly Dictionary<int, PlanAiDecisionHandler> Handlers = BuildTable();

        public static bool TryGet(int decisionId, out PlanAiDecisionHandler handler)
        {
            return Handlers.TryGetValue(decisionId, out handler);
        }

        public static int[] ImplementedIds()
        {
            List<int> ids = new List<int>(Handlers.Keys);
            ids.Sort();

            return ids.ToArray();
        }

        private static Dictionary<int, PlanAiDecisionHandler> BuildTable()
        {
            Dictionary<int, PlanAiDecisionHandler> table = new Dictionary<int, PlanAiDecisionHandler>();
            table.Add(PlanOpcodes.DecisionIds.DecidePosition, DecidePosition);
            table.Add(PlanOpcodes.DecisionIds.OkToPassToTeammate, OkToPassToTeammate);
            table.Add(PlanOpcodes.DecisionIds.RecvrInRangeNView, RecvrInRangeNView);
            table.Add(PlanOpcodes.DecisionIds.ShootSituation, ShootSituation);

            return table;
        }

        private static PlanAiObject RequirePlayer(PlanAiHost host, int slot, string decision)
        {
            PlanAiObject player = host.World.GetPlayer(slot);

            if (player == null)
            {
                throw new PlanAiFatalException(decision + " needs player slot " + slot + " and the mock world has none.");
            }

            return player;
        }

        /// <summary>PlanCond::DecidePosition (HYPERX 0x00462d20).</summary>
        private static short DecidePosition(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = RequirePlayer(host, host.Actor.Slot, "DecidePosition");
            float damage = actor.Stats.GetScalar(PlanAiConstants.RatingDamage);

            if (damage <= host.World.DamageGate)
            {
                return StateGated;
            }

            if (actor.ClockStoppedSemaphore != 0)
            {
                return StateHeld;
            }

            PlanAiObject rok = host.World.Rok;

            if (rok != null && rok.Body.AttachTargetSlot != -1)
            {
                int target = rok.Body.AttachTargetSlot;

                if (target < PlanAiConstants.PlayerObjectCount)
                {
                    int own = host.Actor.Slot;

                    if (target != own)
                    {
                        int other = host.Actor.Slot;
                        int opposite = other < SlotOppositeBelow ? other + SlotOppositeStep : other;

                        if (target != (opposite & SlotOppositeWrap))
                        {
                            return (short)(own >= SlotFarHalf ? 1 : 0);
                        }
                    }

                    return (short)((target != own ? 1 : 0) + RokTargeted);
                }
            }

            return (short)(host.Actor.Slot >= SlotFarHalf ? 1 : 0);
        }

        /// <summary>PlanCond::OkToPassToTeammate (HYPERX 0x00462c40).</summary>
        private static short OkToPassToTeammate(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = RequirePlayer(host, host.Actor.Slot, "OkToPassToTeammate");
            int teammateSlot = PlanOpcodes.TeammateSlot(actor.Slot);
            PlanAiObject other = RequirePlayer(host, teammateSlot, "OkToPassToTeammate");

            short result = 1;
            float damage = other.Stats.GetScalar(PlanAiConstants.RatingDamage);

            if (damage <= host.World.DamageGate || actor.ClockStoppedSemaphore != 0 ||
                other.ClockStoppedSemaphore != 0 || other.DesiredSubstitute != -1)
            {
                result = 0;
            }

            return result;
        }

        /// <summary>PlanCond::RecvrInRangeNView (HYPERX 0x00466b70).</summary>
        private static short RecvrInRangeNView(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanWord param = new PlanWord(nArg);
            int operand;

            if (param.IsPronoun)
            {
                throw new PlanAiUnportedHandlerException("stat pronoun " + PlanOpcodes.PronounName(param.Low), param.Low);
            }

            operand = param.Value;

            if ((operand & 0xFFFF) > PlanOpcodes.SlotDromer3)
            {
                // STRING: HYPERX 0x004bb348
                throw new PlanAiFatalException("Fatal failure to initialize the receiver of a pass!");
            }

            PlanAiObject actor = RequirePlayer(host, host.Actor.Slot, "RecvrInRangeNView");
            PlanAiObject target = RequirePlayer(host, operand, "RecvrInRangeNView");

            short result = 0;
            float damage = target.Stats.GetScalar(PlanAiConstants.RatingDamage);
            PlanAiVec3 delta = target.Body.Position - actor.Body.Position;
            float longPassEnable = target.Stats.GetScalar(PlanAiConstants.RatingLongPassEnable);

            if (longPassEnable != 0.0f)
            {
                result = 0;
            }
            else if (PlanAiVec3.Dot(delta, delta) < PlanAiConstants.PassRangeSquared)
            {
                result = 0;
            }
            else
            {
                result = 1;
            }

            float length = delta.Length;
            float scale = PlanAiConstants.PiPlan / PlanAiConstants.ThreePlan;
            float falloff = (PlanAiConstants.ScoreRange - length) * scale * PlanAiConstants.ScoreFalloff;
            float minDot = (float)Math.Cos((double)(falloff + scale));

            if (actor.GetDistIfFacing(target, minDot) == 0.0f)
            {
                result = 1;
            }

            PlanAiObject mate = host.World.GetPlayer(PlanOpcodes.TeammateSlot(actor.Slot));

            if (host.World.TeammateMode == 0 && mate != null && mate.PlanName == "Human")
            {
                result = 0;
            }

            if (damage <= host.World.DamageGate)
            {
                result = 1;
            }

            return result;
        }

        /// <summary>PlanCond::ShootSituation (HYPERX 0x00468650).</summary>
        private static short ShootSituation(PlanAiHost host, PlanNode node, int nArg)
        {
            PlanAiObject actor = RequirePlayer(host, host.Actor.Slot, "ShootSituation");
            int range = (int)actor.Stats.GetScalar(PlanAiConstants.RatingCarryRokThrowDist);

            if (range < PlanAiConstants.ShootRangeMin || range > PlanAiConstants.ShootRangeMax)
            {
                range = PlanAiConstants.ShootRangeDefault;
            }

            int goalSlot = (actor.Slot & 1) != 0 ? PlanAiConstants.SlotNgoal : PlanAiConstants.SlotSgoal;
            PlanAiObject goal = host.World.GetObject(goalSlot);

            if (goal == null)
            {
                throw new PlanAiFatalException("ShootSituation needs goal slot " + goalSlot + " and the mock world has none.");
            }
            PlanAiVec3 diff = goal.Body.Position - actor.Body.Position;
            float distance = diff.Length;
            PlanAiVec3 axis = actor.Body.VelocityDirection;
            float limit = range;
            PlanAiVec3 direction = diff;
            short result;

            if (distance <= limit)
            {
                axis.NormalizeInPlace();
                direction.NormalizeInPlace();

                if (PlanAiVec3.Dot(direction, axis) > 0.5f)
                {
                    limit *= 0.5f;
                    float roll = ((float)PlanAiRandom.Rand() * (1.0f / (float)PlanAiRandom.RandMax)) * limit;

                    result = roll + limit > distance ? (short)0 : (short)1;
                }
                else
                {
                    result = 1;
                }
            }
            else
            {
                result = 1;
            }

            PlanAiObject mate = host.World.GetPlayer(PlanOpcodes.TeammateSlot(actor.Slot));

            if (host.World.TeammateMode == 0 && mate != null && mate.PlanName == "Human")
            {
                result = 1;
            }

            return result;
        }
    }
}
