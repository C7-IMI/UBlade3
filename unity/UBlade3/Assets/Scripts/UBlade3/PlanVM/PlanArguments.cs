using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// A compile-time plan argument resolver: the g_apPlanArgFns bank in
    /// src/planvm/planopcodetable.h (HYPERX 0x004d187c). PlanState::ParseNodeStream (0x0047b430)
    /// calls one for every pronoun leaf while it compiles a plan for one actor, so the compiled
    /// node holds a plain slot number, never the pronoun.
    /// </summary>
    public interface IPlanArgResolver
    {
        /// <summary>Resolves one pronoun with the actor's slot; the raw word is the leaf as it sits in the file.</summary>
        int ResolvePronoun(int pronoun, PlanWord rawWord);
    }

    /// <summary>
    /// The exact compile-time resolver table from src/planvm/planarg.cpp and planarg.h, ported for
    /// an actor slot. Every entry the original can resolve at compile time is arithmetic on the slot;
    /// the view and Rok pronouns return the raw word (GetViewOwner etc. copy pStream-&gt;n).
    /// </summary>
    public sealed class PlanArgResolver : IPlanArgResolver
    {
        public readonly int ActorSlot;

        public PlanArgResolver(int actorSlot)
        {
            ActorSlot = actorSlot;
        }

        public static PlanArgResolver ForSlot(int actorSlot)
        {
            return new PlanArgResolver(actorSlot);
        }

        public int ResolvePronoun(int pronoun, PlanWord rawWord)
        {
            int me = ActorSlot;
            int parity = me & 1;
            int oppositeParity = (~me) & 1;

            switch (pronoun)
            {
                case 0: // PRN_ME, GetMe 0x0047c780
                    return me;
                case 1: // PRN_MY_TEAMMATE, GetMyTeammate 0x0047c7a0
                    return PlanOpcodes.TeammateSlot(me);
                case 2: // PRN_MY_LEAD_OPPONENT, GetMyLeadOpponent 0x0047c7d0
                    return oppositeParity;
                case 3: // PRN_MY_OTHER_OPPONENT, GetMyOtherOpponent 0x0047c7f0
                    return oppositeParity | 2;
                case 4: // PRN_MY_GOAL, GetMyGoal 0x0047c810
                    return parity != 0 ? PlanOpcodes.SlotSgoal : PlanOpcodes.SlotNgoal;
                case 5: // PRN_MY_GOAL_SIDE, GetMyGoalSide 0x0047c860
                    return parity != 0 ? PlanOpcodes.CollisionSgoalSide : PlanOpcodes.CollisionNgoalSide;
                case 6: // PRN_MY_GOAL_ZONE, GetMyGoalZone 0x0047c890
                    return (parity << 2) | PlanOpcodes.CollisionNgoalZone;
                case 7: // PRN_OPPONENTS_GOAL, GetOpponentsGoal 0x0047c8c0
                    return parity != 0 ? PlanOpcodes.SlotNgoal : PlanOpcodes.SlotSgoal;
                case 8: // PRN_OPPN_GOAL_SIDE, GetOppnGoalSide 0x0047c8f0
                    return parity != 0 ? PlanOpcodes.CollisionNgoalSide : PlanOpcodes.CollisionSgoalSide;
                case 9: // PRN_OPPN_GOAL_ZONE, GetOppnGoalZone 0x0047c920
                    return (oppositeParity << 2) | PlanOpcodes.CollisionNgoalZone;
                case 10: // PRN_MY_COLLISION_BUB, GetMyCollisionBub 0x0047c950
                    return me * PlanOpcodes.CollisionPerDromer;
                case 11: // PRN_MY_LEAD_OPPN_COLL_BUB, GetMyLeadOppnCollBub 0x0047c9c0
                    return oppositeParity * PlanOpcodes.CollisionPerDromer;
                case 12: // PRN_MY_OTHER_OPPN_COLL_BUB, GetMyOtherOppnCollBub 0x0047c9e0
                    return (oppositeParity | 2) * PlanOpcodes.CollisionPerDromer;
                case 13: // PRN_MY_BACK_DEF_WG_POS, GetMyBackDefWgPos 0x0047cac0
                    return parity | PlanOpcodes.PositionNGoalDefender;
                case 14: // PRN_MY_GOAL_POS, GetMyGoalPos 0x0047cae0
                    return parity | PlanOpcodes.PositionNGoalCenter;
                case 15: // PRN_VIEW_OWNER, GetViewOwner 0x0047cb20: copies the argument word
                    return rawWord.Value;
                case 16: // PRN_VIEW_OWNERS_GOAL
                    return rawWord.Value;
                case 17: // PRN_VIEW_OWNERS_TEAMMATE
                    return rawWord.Value;
                case 18: // PRN_ROK_CARRIER
                    return rawWord.Value;
                case 19: // PRN_ROK_CARRIER_COB
                    return rawWord.Value;
                case 20: // PRN_MY_DRAG_START_POS, GetMyDragStartPos 0x0047cb00
                    return parity | PlanOpcodes.PositionHomeDragStartPos;
                case 21: // PRN_LAST_ROK_CARRIER
                    return rawWord.Value;
                case 22: // PRN_PREV_ROK_CARRIER
                    return rawWord.Value;
                case 23: // PRN_MY_AWARENESS_BUB, GetMyAwarenessBub 0x0047c9a0
                    return me + PlanOpcodes.CollisionDr0Awareness;
                case 24: // PRN_NON_ROK_CARRYING_OPPN
                    return rawWord.Value;
                case 25: // PRN_VIEW_ONRS_OPPN1
                    return rawWord.Value;
                case 26: // PRN_VIEW_ONRS_OPPN2
                    return rawWord.Value;
                case 27: // PRN_VIEW_OWNER_COB
                    return rawWord.Value;
                case 28: // PRN_VIEW_ONRS_OPPN1_COB
                    return rawWord.Value;
                case 29: // PRN_VIEW_ONRS_OPPN2_COB
                    return rawWord.Value;
                case 30: // PRN_WATCHED_OBJECT
                    return rawWord.Value;
                case 31: // PRN_VW_OWNR_OPPN_GOALIE
                    return rawWord.Value;
                case 32: // PRN_MY_BODY_BUBBLE, GetMyBodyBubble 0x0047ca00
                    return me * PlanOpcodes.CollisionPerDromer + PlanOpcodes.CollisionDromerBody0;
                case 33: // PRN_LEAD_OPPN_BODY_BUB, GetLeadOppnBodyBub 0x0047ca20
                    return oppositeParity * PlanOpcodes.CollisionPerDromer + PlanOpcodes.CollisionDromerBody0;
                case 34: // PRN_OTHER_OPPN_BODY_BUB, GetOtherOppnBodyBub 0x0047ca40
                    return (oppositeParity | 2) * PlanOpcodes.CollisionPerDromer + PlanOpcodes.CollisionDromerBody0;
                case 35: // PRN_MY_GOALIE, GetMyGoalie (planarg.h inline)
                    return parity | PlanOpcodes.GoalieSlotA;
                case 36: // PRN_MY_LEAD_TEAMMATE, GetMyLeadTeammate 0x0047c840
                    return parity;
                case 37: // PRN_TRACKED_OBJECT
                    return rawWord.Value;
                case 38: // PRN_TRACKED_OBJS_TEAMMATE
                    return rawWord.Value;
                case 39: // PRN_VW_OWNR_AWARNS_BUB
                    return rawWord.Value;
                case 40: // PRN_VW_OWNRS_OPPN_GOAL
                    return rawWord.Value;
                case 41: // PRN_THIS_MULTIPLIER_COB, GetMultiplierCollisionObject 0x0047ca80
                    return me + (PlanOpcodes.CollisionMultiplier - PlanOpcodes.SlotMultiplier);
                case 42: // PRN_THIS_TURNSTILE_COB, GetTurnstileCollisionObject (folded into GetMultiplier's body)
                    return me + (PlanOpcodes.CollisionTurnstile - PlanOpcodes.SlotTurnstile);
                case 43: // PRN_THIS_LIMBO_POLE_COB, GetLimboPoleCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionLimboPole - PlanOpcodes.SlotLimboPole);
                case 44: // PRN_THIS_LASER_HURDLE_COB, GetLaserHurdleCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionLaserHurdle - PlanOpcodes.SlotLaserHurdle);
                case 45: // PRN_THIS_FAST_LANE_COB, GetFastLaneCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionFastLane - PlanOpcodes.SlotFastLane);
                case 46: // PRN_THIS_EXPLOSION_COB, GetExplosionCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionExplosion - PlanOpcodes.SlotExplosion);
                case 47: // PRN_THIS_HURDLE_COB, GetHurdleCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionHurdle - PlanOpcodes.SlotHurdle);
                case 48: // PRN_THIS_GREEN_RANDOMIZER_COB, GetGreenRandomizerCollisionObject (folded into GetRed's body)
                    return me + (PlanOpcodes.CollisionGreenRandomizer - PlanOpcodes.SlotGreenRandomizer);
                case 49: // PRN_THIS_RED_RANDOMIZER_COB, GetRedRandomizerCollisionObject 0x0047caa0
                    return me + (PlanOpcodes.CollisionRedRandomizer - PlanOpcodes.SlotRedRandomizer);
                case 50: // PRN_THIS_RAMP_COB, GetRampCollisionObject 0x0047ca60
                    return me + (PlanOpcodes.CollisionRamp - PlanOpcodes.SlotRamp);
                case 51: // PRN_MY_TEAMMATES_COB, GetMyTeammatesCOB 0x0047c970
                    return PlanOpcodes.TeammateSlot(me) * PlanOpcodes.CollisionPerDromer;
                case 52: // PRN_THIS_ARMORY_COB, GetArmoryCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionArmory - PlanOpcodes.SlotArmory);
                case 53: // PRN_THIS_SLOW_LANE_COB, GetSlowLaneCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionSlowLane - PlanOpcodes.SlotSlowLane);
                case 54: // PRN_THIS_MINE_COB, GetMineCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionMine - PlanOpcodes.SlotMine);
                case 55: // PRN_THIS_SWARF_COB, GetSwarfCollisionObject (folded)
                    return me + (PlanOpcodes.CollisionSwarf - PlanOpcodes.SlotSwarf);
                case 56: // PRN_THIS_BODY_PART_COB, GetBodyPartCollisionObject 0x0047d050
                    return me + (PlanOpcodes.CollisionBodyPart0 - PlanOpcodes.SlotBodyPart);
                default:
                    throw new PlanVmException("No plan argument resolver for pronoun " + pronoun + " (planarg.cpp table).");
            }
        }
    }

    /// <summary>
    /// The owner's plan-name lookup: EngineObject::FindPlanByName (HYPERX 0x00471150). The compiled
    /// CHANGE_OF_PLAN string becomes the index this returns, -1 when the name is not in the object's
    /// plan table. The engine plan table comes from the derived object's data; the Probe uses a
    /// shipped-file resolver and documents that substitution as a gap.
    /// </summary>
    public interface IPlanNameResolver
    {
        int FindPlanByName(string name);
    }

    /// <summary>A resolver that answers -1 for every name, for parser unit checks.</summary>
    public sealed class NullPlanNameResolver : IPlanNameResolver
    {
        public int FindPlanByName(string name)
        {
            return -1;
        }
    }

    /// <summary>A case-sensitive fixed table, the way strcmp compares names in FindPlanByName.</summary>
    public sealed class FixedPlanNameResolver : IPlanNameResolver
    {
        private readonly Dictionary<string, int> names = new Dictionary<string, int>(StringComparer.Ordinal);

        public void Add(string name, int index)
        {
            names[name] = index;
        }

        public int FindPlanByName(string name)
        {
            int index;

            if (names.TryGetValue(name, out index))
            {
                return index;
            }

            return -1;
        }
    }
}
