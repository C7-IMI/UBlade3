using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    public delegate void PlanAiActionHandler(PlanAiHost host, PlanNode node, ref short state, int nArg);

    /// <summary>
    /// The action handler bodies the selected CARRYROK flow needs. Each is a port of its decompiled
    /// body; the engine operations it calls are on the mock PlanAiObject/PlanAiWorld seam. Any other
    /// action id is left to the host's explicit unsupported-handler telemetry.
    /// </summary>
    public static class PlanAiActions
    {
        private static readonly Dictionary<int, PlanAiActionHandler> Handlers = BuildTable();

        public static bool TryGet(int actionId, out PlanAiActionHandler handler)
        {
            return Handlers.TryGetValue(actionId, out handler);
        }

        public static int[] ImplementedIds()
        {
            List<int> ids = new List<int>(Handlers.Keys);
            ids.Sort();

            return ids.ToArray();
        }

        private static Dictionary<int, PlanAiActionHandler> BuildTable()
        {
            Dictionary<int, PlanAiActionHandler> table = new Dictionary<int, PlanAiActionHandler>();
            table.Add(PlanOpcodes.ActionIds.SendMessage, SendMsg);
            table.Add(PlanOpcodes.ActionIds.ReallySendMsg, ReallySendMsg);
            table.Add(PlanOpcodes.ActionIds.SetKnockedDownState, SetKnockedDownState);

            // N12: observed action bodies reached by the default Defense/CarryRok/Goalie plans.
            table.Add(PlanOpcodes.ActionIds.SetSpeedBurst, PlanAiMatchActions.SetSpeedBurst);
            table.Add(PlanOpcodes.ActionIds.PlaySound, PlanAiMatchActions.PlaySound);
            table.Add(PlanOpcodes.ActionIds.IncrementStat, PlanAiMatchActions.IncrementStat);
            table.Add(PlanOpcodes.ActionIds.SetLocation, PlanAiMatchActions.SetLocation);
            table.Add(PlanOpcodes.ActionIds.SetFriction, PlanAiMatchActions.SetFriction);
            table.Add(PlanOpcodes.ActionIds.SetAnimation, PlanAiMatchActions.SetAnimation);
            table.Add(PlanOpcodes.ActionIds.ScaleBubToRating, PlanAiMatchActions.ScaleBubToRating);

            // N13: remaining observed default-roster action bodies.
            table.Add(PlanOpcodes.ActionIds.SpeedBurstSituation, PlanAiMatchActionsN13.SpeedBurstSituation);
            table.Add(PlanOpcodes.ActionIds.SendLaunchMessage, PlanAiMatchActionsN13.SendLaunchMessage);

            // N17: documented MatchStats-writing action handlers.
            table.Add(PlanOpcodes.ActionIds.NoteStats4Possessors, PlanAiMatchStatsActions.NoteStats4Possessors);
            table.Add(PlanOpcodes.ActionIds.StartStatTimer, PlanAiMatchStatsActions.StartStatTimer);
            table.Add(PlanOpcodes.ActionIds.EndStatTimer, PlanAiMatchStatsActions.EndStatTimer);

            return table;
        }

        /// <summary>
        /// Resolves an action argument that names an object. A pronoun that survived the compile-time
        /// resolver (the view/Rok pronouns) would need g_apPlanStatFns, which is not ported; the host
        /// reports that id explicitly instead of guessing.
        /// </summary>
        private static int ResolveSlot(PlanAiHost host, PlanNode node, int index, int nArg, string actionName)
        {
            PlanWord word = node.Args[index].Word;

            if (word.IsPronoun)
            {
                host.World.Log("action." + actionName + ".stat-pronoun pronoun=" + PlanOpcodes.PronounName(word.Low));
                throw new PlanAiUnportedHandlerException("stat pronoun " + PlanOpcodes.PronounName(word.Low), word.Low);
            }

            if (word.Value == PlanWord.TokenMessageData)
            {
                return nArg;
            }

            return word.Value;
        }

        /// <summary>The optional fourth leaf overrides the interpreter's message data unless it is _MESSAGE_DATA_.</summary>
        private static int ResolveData(PlanNode node, int nArg)
        {
            if (node.ArgCount == 4 && node.Args[3].Word.Value != PlanWord.TokenMessageData)
            {
                return node.Args[3].IntValue;
            }

            return nArg;
        }

        /// <summary>PlanAction::SendMsg (HYPERX 0x0045e6a0): post the scene message to the target.</summary>
        private static void SendMsg(PlanAiHost host, PlanNode node, ref short state, int nParam)
        {
            int target = ResolveSlot(host, node, 1, nParam, "SendMessage");
            int message = node.Args[2].IntValue;
            int data = ResolveData(node, nParam);
            int sender = host.Actor.Slot;

            host.RecordAction(PlanOpcodes.ActionIds.SendMessage, node, nParam, "target=" + target + " message=" + PlanAiWorld.DescribeMessage(message) + " data=" + data);

            if (target == PlanWord.TokenBroadcast)
            {
                host.World.BroadcastEvent(message, data, sender);

                return;
            }

            host.World.PostSceneMessage(target, message, data, sender, 0);
        }

        /// <summary>PlanAction::ReallySendMsg (HYPERX 0x0045e5d0): dispatch the scene message immediately.</summary>
        private static void ReallySendMsg(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            int target = ResolveSlot(host, node, 1, nArg, "ReallySendMsg");
            int message = node.Args[2].IntValue;
            int data = ResolveData(node, nArg);
            int sender = host.Actor.Slot;

            host.RecordAction(PlanOpcodes.ActionIds.ReallySendMsg, node, nArg, "target=" + target + " message=" + PlanAiWorld.DescribeMessage(message) + " data=" + data);

            if (target == PlanWord.TokenBroadcast)
            {
                host.World.BroadcastEvent(message, data, sender);

                return;
            }

            host.World.DispatchSceneMessage(target, message, data, DecisionLog.Fresh, sender, 0);
        }

        /// <summary>
        /// PlanAction::SetKnockedDownState (HYPERX 0x0045ee20): a two-leaf node sets the actor's
        /// state, a three-leaf node sets the player selected by argument two (or by the interpreter's
        /// message data under _MESSAGE_DATA_).
        /// </summary>
        private static void SetKnockedDownState(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            int value = node.Args[1].IntValue;

            if (node.ArgCount == 3)
            {
                int slot = nArg;

                if (node.Args[2].Word.Value != PlanWord.TokenMessageData)
                {
                    slot = node.Args[2].IntValue;
                }

                PlanAiObject target = host.World.GetPlayer(slot);

                if (target != null)
                {
                    target.KnockedDownState = value;
                    host.RecordAction(PlanOpcodes.ActionIds.SetKnockedDownState, node, nArg, "player=" + slot + " state=" + value);

                    return;
                }

                host.World.Log("action.SetKnockedDownState.no-player slot=" + slot);
            }
            else if (node.ArgCount == 2)
            {
                host.Actor.KnockedDownState = value;
                host.RecordAction(PlanOpcodes.ActionIds.SetKnockedDownState, node, nArg, "player=" + host.Actor.Slot + " state=" + value);
            }
        }
    }
}
