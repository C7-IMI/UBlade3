using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N12 action bodies for the ids the N9/N10 default roster actually reached and that had explicit
    /// unsupported telemetry: 6 Set_SpeedBurst, 7 AnimEvent::PlaySnd, 9 IncrementStat, 18 SetLocation,
    /// 29 SetFriction, 30 SetAnimation and 71 ScaleBubToRating. The bodies are ports of the decompiled
    /// positions named below; the engine side effects that the mock object seam does not have (animation
    /// playback, sound bank, match-stat accumulator, collision bubble) are recorded as plan-side state
    /// and adapter telemetry rather than invented. Friction/location/speed-burst velocity and axis are
    /// consumed by MatchPlanAi and applied to the real Simulation body.
    /// </summary>
    public static class PlanAiMatchActions
    {
        private const float PlanStatScale = 0.1f;

        private const float SpeedRatioScale = 0.3f;

        private const float SpeedBurstFriction = 0.1f;

        private const float SpeedBurstBoost = 1.05f;

        private const uint FrictionSteerScaleThreshold = 25;

        private static float ResolveFloat(PlanAiHost host, PlanNode node, int index)
        {
            PlanWord word = node.Args[index].Word;

            if (word.IsLocalReal)
            {
                int local = word.Low;
                PlanState state = host.State;

                if (state != null && state.LocalReals != null && local >= 0 && local < state.LocalRealCount)
                {
                    return state.LocalReals[local];
                }

                return 0.0f;
            }

            return (float)word.Value;
        }

        private static int ResolveData(PlanNode node, int nArg)
        {
            if (node.ArgCount > 1 && node.Args[1].Word.Value == PlanWord.TokenMessageData)
            {
                return nArg;
            }

            return node.ChildCount >= 0 && node.ArgCount > 1 ? node.Args[1].IntValue : nArg;
        }

        private static int OpposingFirst(int slot)
        {
            return (~slot) & 1;
        }

        private static int OpposingSecond(int slot)
        {
            return OpposingFirst(slot) | 2;
        }

        /// <summary>PlanAction::Set_SpeedBurst (plancond.cpp HYPERX 0x0045eb30).</summary>
        public static void SetSpeedBurst(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            int period = node.Args[1].IntValue;
            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);

            if (period != 0)
            {
                float scale = PlanAiMatchContext.GetSpeedRatio(actor.Slot) > SpeedRatioScale ? 1.0f : 0.0f;
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
                host.RecordAction(PlanOpcodes.ActionIds.SetSpeedBurst, node, nArg,
                    "period=" + period + " target=" + effects.SpeedBurstTarget.ToString("0.###")
                    + " friction=" + SpeedBurstFriction.ToString("0.###") + " axisX+=0.25");
            }
            else
            {
                effects.SpeedBurstActive = false;
                effects.SpeedBurstRemove = true;
                effects.SpeedBurstEvents++;
                host.RecordAction(PlanOpcodes.ActionIds.SetSpeedBurst, node, nArg, "remove");
            }
        }

        /// <summary>AnimEvent::PlaySnd (animevent.cpp HYPERX 0x00406ea0). The mock has no sound bank; the id is recorded.</summary>
        public static void PlaySound(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;
            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            int index = ResolveData(node, nArg);

            effects.LastSoundIndex = index;
            effects.SoundEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.PlaySound, node, nArg,
                "sound=" + index + " (no sound bank in the match mock; request recorded)");
        }

        /// <summary>PlanAction::IncrementStat (plancond.cpp HYPERX 0x00461d20); only the pronoun form acts in the source.</summary>
        public static void IncrementStat(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;
            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            PlanWord targetWord = node.Args[1].Word;
            int slot = targetWord.IsPronoun ? nArg : targetWord.Value;
            int stat = node.Args[2].IntValue;

            effects.LastStatSlot = slot;
            effects.LastStatId = stat;
            effects.StatEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.IncrementStat, node, nArg,
                "slot=" + slot + " stat=" + stat + " pronoun=" + (targetWord.IsPronoun ? "True" : "False")
                + " (match stat accumulator not ported; request recorded)");
        }

        /// <summary>PlanAction::SetLocation (plancond.cpp HYPERX 0x0045efc0).</summary>
        public static void SetLocation(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            float x;
            float y;
            float z;

            if (node.ArgCount >= 3)
            {
                x = ResolveFloat(host, node, 1);
                y = ResolveFloat(host, node, 2);

                if (node.ArgCount >= 4)
                {
                    z = ResolveFloat(host, node, 3);
                }
                else if (!PlanAiMatchContext.TryTerrainZ(x, y, out z))
                {
                    z = 0.0f;
                }
            }
            else
            {
                int marker = node.Args[1].Word.Value == PlanWord.TokenMessageData ? nArg : node.Args[1].IntValue;

                if (PlanAiMatchContext.MarkerPosition == null || !PlanAiMatchContext.MarkerResolver(marker))
                {
                    host.RecordAction(PlanOpcodes.ActionIds.SetLocation, node, nArg,
                        "marker=" + marker + " unresolved (position markers are not in the match mock)");

                    return;
                }

                PlanAiVec3 markerPosition = PlanAiMatchContext.MarkerPosition(marker);
                x = markerPosition.X;
                y = markerPosition.Y;
                z = markerPosition.Z;
            }

            PlanAiVec3 location = new PlanAiVec3(x, y, z);
            actor.Body.Position = location;
            actor.Body.Velocity = new PlanAiVec3(0.0f, 0.0f, 0.0f);
            effects.LocationDirty = true;
            effects.Location = location;
            effects.LocationEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.SetLocation, node, nArg,
                "location=(" + x.ToString("0.####") + "," + y.ToString("0.####") + "," + z.ToString("0.####") + ")");
        }

        /// <summary>PlanAction::SetFriction (plancond.cpp HYPERX 0x00463790).</summary>
        public static void SetFriction(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            uint raw = unchecked((uint)node.Args[1].IntValue);
            float friction = (float)raw * 0.01f;
            actor.Body.Friction = friction;

            if (raw > FrictionSteerScaleThreshold)
            {
                actor.Body.SteerScale = 1.0f;
            }

            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            effects.FrictionDirty = true;
            effects.Friction = friction;
            effects.FrictionEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.SetFriction, node, nArg,
                "raw=" + raw + " friction=" + friction.ToString("0.###")
                + " steerScale=" + actor.Body.SteerScale.ToString("0.###"));
        }

        /// <summary>PlanAction::SetAnimation (planactionanim.cpp HYPERX 0x00401000); the animation registry/clip port is absent.</summary>
        public static void SetAnimation(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;
            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            int index = node.Args[1].Word.Value == PlanWord.TokenMessageData ? nArg : node.Args[1].IntValue;
            float rate = -1.0f;
            int flags = 0;

            if (node.ArgCount >= 4)
            {
                rate = node.Args[2].Word.AsFloat;
                flags = node.Args[3].IntValue;
            }

            effects.AnimationIndex = index;
            effects.AnimationRate = rate;
            effects.AnimationFlags = flags;
            effects.AnimationEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.SetAnimation, node, nArg,
                "anim=" + index + " rate=" + rate.ToString("0.###") + " flags=" + flags
                + " (animation registry/clip port absent; state recorded)");
        }

        /// <summary>PlanAction::ScaleBubToRating (plancond.cpp HYPERX 0x00461ea0).</summary>
        public static void ScaleBubToRating(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            PlanAiObject actor = host.Actor;

            if (actor == null)
            {
                return;
            }

            int colObject = node.Args[1].IntValue;
            int statId = node.Args[2].IntValue;
            float stat = actor.Stats.GetScalar(statId);
            float low = ResolveFloat(host, node, 3);
            float high = ResolveFloat(host, node, 4);
            float radius = (high - low) * (stat * PlanStatScale) + low;
            PlanAiActionState effects = PlanAiActionStateStore.Get(actor);
            effects.LastBubbleRadius = radius;
            effects.BubbleScaleEvents++;
            host.RecordAction(PlanOpcodes.ActionIds.ScaleBubToRating, node, nArg,
                "colObject=" + colObject + " stat=" + statId + " radius=" + radius.ToString("0.####")
                + " (collision bubble port absent; radius recorded)");
        }
    }
}
