using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N13 action bodies for the remaining observed default-roster ids:
    ///   * 84 SpeedBurstSituation (plancond.cpp HYPERX 0x00467760);
    ///   * 139 SendLaunchMessage (planactionlaunch.cpp HYPERX 0x004031e0).
    /// Action 84 reuses the N12 plan-side speed-burst state and applies the same real-body
    /// friction/axis/velocity subset as action 6. Action 139 posts the original MSG_SHOOT to the
    /// actor slot; an AI actor uses the SHOT_UNAIMED word exactly as the source's non-Human branch.
    /// </summary>
    public static class PlanAiMatchActionsN13
    {
        private const int RatingSpeedBurst0A = 0x11;

        private const int ShotUnaimed = unchecked((int)0xffff0000);

        private const int MessageShoot = 0;

        private const float SpeedRatioGate = 0.25f;

        private const float SpeedRatioOnePlan = 0.3f;

        private const float SpeedBurstFriction = 0.1f;

        private const float SpeedBurstBoost = 1.05f;

        /// <summary>PlanAction::SpeedBurstSituation (plancond.cpp HYPERX 0x00467760).</summary>
        public static void SpeedBurstSituation(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            float ratio = PlanAiMatchContext.GetSpeedRatio(actor.Slot);

            if (ratio < SpeedRatioGate)
            {
                host.RecordAction(PlanOpcodes.ActionIds.SpeedBurstSituation, node, nArg,
                    "speedRatio=" + ratio.ToString("0.###") + " below gate, no burst (source returns)");

                return;
            }

            int rating = node.Args[1].IntValue;

            if (PlanAiMatchContext.IsTeamWinning(actor.Slot))
            {
                rating *= 2;
            }
            else
            {
                rating = rating * 2 + 1;
            }

            if ((int)actor.Stats.GetScalar(rating + RatingSpeedBurst0A) == 0)
            {
                host.RecordAction(PlanOpcodes.ActionIds.SpeedBurstSituation, node, nArg,
                    "rating=" + rating + " speedBurstStat=0, no burst (source returns)");

                return;
            }

            int period = node.Args[2].IntValue;

            if (period == 0)
            {
                // The original divides by this word; a zero-period plan word would fault there. Keep
                // the action observable without inventing a division result.
                host.RecordAction(PlanOpcodes.ActionIds.SpeedBurstSituation, node, nArg,
                    "rating=" + rating + " period=0 (source would divide by zero; no burst)");

                return;
            }

            float scale = ratio > SpeedRatioOnePlan ? 1.0f : 0.0f;
            effects.SpeedBurstActive = true;
            effects.SpeedBurstRemove = false;
            effects.SpeedBurstTarget = 80.0f / (float)period * scale + 1.0f;
            effects.SpeedBurstBoost = SpeedBurstBoost;
            effects.AxisXDelta += 0.25f;
            effects.FrictionDirty = true;
            effects.Friction = SpeedBurstFriction;
            actor.Body.Friction = SpeedBurstFriction;

            float length = (float)Math.Sqrt(
                actor.Body.Velocity.X * actor.Body.Velocity.X
                + actor.Body.Velocity.Y * actor.Body.Velocity.Y
                + actor.Body.Velocity.Z * actor.Body.Velocity.Z);

            if (length < 0.1f)
            {
                effects.VelocityDirty = true;
                effects.Velocity = actor.Body.VelocityDirection;
            }

            effects.SpeedBurstEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.SpeedBurstSituation, node, nArg,
                "rating=" + rating + " period=" + period + " target=" + effects.SpeedBurstTarget.ToString("0.###")
                + " speedRatio=" + ratio.ToString("0.###") + " friction=0.1 axisX+=0.25");
        }

        /// <summary>PlanAction::SendLaunchMessage (planactionlaunch.cpp HYPERX 0x004031e0).</summary>
        public static void SendLaunchMessage(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            // The source only packs a head-camera shot for a Local Human named "Human". Every AI
            // actor takes SHOT_UNAIMED. This slice has no head-camera byte pack, so the AI branch is
            // the exact path and the Human branch is recorded as unavailable rather than faked.
            bool human = string.Equals(actor.PlanName, "Human", StringComparison.Ordinal);
            int shot = human ? ShotUnaimed : ShotUnaimed;
            host.World.PostSceneMessage(actor.Slot, MessageShoot, shot, actor.Slot, 0);
            host.RecordAction(PlanOpcodes.ActionIds.SendLaunchMessage, node, nArg,
                "posted MSG_SHOOT unaimed=0x" + shot.ToString("x8") + " human=" + (human ? "True" : "False")
                + (human ? " (head-camera pack not ported; unaimed)" : string.Empty));
        }
    }
}
