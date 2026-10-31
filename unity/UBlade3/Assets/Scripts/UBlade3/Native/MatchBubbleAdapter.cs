using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // N16: the action-71 ScaleBubToRating radius consumer.
    //
    // The decompiled action (plancond.cpp 0x00461ea0) writes m_flRadius on the BoundBubble returned
    // by g_pGameEngine->GetColObjPtr(colObject). The native Sim slice has no per-player/plan bound
    // bubble object: Simulation.cs explicitly says the collision bubble and plan-interactor ports are
    // not in this slice, and PhysicsBodyState only carries the arena CollisionEllipsoid shell. The
    // action state records the computed radius (LastBubbleRadius/BubbleScaleEvents) but not the
    // colObject id, so this adapter cannot write into a real Sim collision shape without inventing a
    // mapping.
    //
    // This new-file adapter therefore exposes the exact radius values per plan actor slot:
    //   MatchBubbleAdapter.GetRadius(slot)
    //   MatchBubbleAdapter.CountAwarenessNeighbours(sim, slot)
    // It is an explicit documented adapter, not a simulated collision change. The simulation's
    // collision path is unchanged.
    public sealed class MatchBubbleAdapter
    {
        public const string Marker = "UBLADE3_NATIVE_MATCH_BUBBLE";

        public const int PlayerSlots = ObjectConstants.PlayerObjectCount;

        private readonly float[] radius = new float[PlayerSlots];

        private readonly int[] lastEvents = new int[PlayerSlots];

        private readonly bool[] observed = new bool[PlayerSlots];

        public int AppliedEvents;

        public int RejectedEvents;

        public int LastSlot = -1;

        public float LastRadius = -1.0f;

        public int AwarenessPairs;

        public int SimCollisionPairs;

        public readonly List<string> Trace = new List<string>();

        public MatchBubbleAdapter()
        {
            for (int i = 0; i < radius.Length; i++)
            {
                radius[i] = -1.0f;
            }
        }

        public float GetRadius(int slot)
        {
            return slot >= 0 && slot < PlayerSlots ? radius[slot] : float.NaN;
        }

        public void Tick(MatchPlanAiRoster roster, Simulation sim)
        {
            if (roster == null)
            {
                return;
            }

            for (int index = 0; index < roster.PlanSlots.Count; index++)
            {
                int actorSlot = roster.PlanSlots[index];
                MatchPlanAi plan = roster.Adapter(actorSlot);

                if (plan == null || actorSlot < 0 || actorSlot >= PlayerSlots)
                {
                    continue;
                }

                PlanAiActionState effects = plan.AnimationActionState;

                if (effects == null)
                {
                    continue;
                }

                if (!observed[actorSlot])
                {
                    observed[actorSlot] = true;
                    lastEvents[actorSlot] = effects.BubbleScaleEvents;
                    continue;
                }

                if (effects.BubbleScaleEvents <= lastEvents[actorSlot])
                {
                    continue;
                }

                lastEvents[actorSlot] = effects.BubbleScaleEvents;

                if (float.IsNaN(effects.LastBubbleRadius) || float.IsInfinity(effects.LastBubbleRadius))
                {
                    RejectedEvents++;
                    Trace.Add("reject actor=" + actorSlot + " radius=" + effects.LastBubbleRadius);
                    continue;
                }

                radius[actorSlot] = effects.LastBubbleRadius;
                AppliedEvents++;
                LastSlot = actorSlot;
                LastRadius = effects.LastBubbleRadius;

                // N17: write the action-71 radius into the Sim awareness-bubble API and count the
                // overlaps from the Sim collision query, not from an adapter-local loop.
                AwarenessBubbles.SetRadius(sim, actorSlot, effects.LastBubbleRadius);
                System.Collections.Generic.List<AwarenessCollision> collisions =
                    new System.Collections.Generic.List<AwarenessCollision>();
                int total = AwarenessBubbles.CollectCollisions(sim, collisions);
                AwarenessPairs = 0;

                for (int collisionIndex = 0; collisionIndex < collisions.Count; collisionIndex++)
                {
                    if (collisions[collisionIndex].OwnerSlot == actorSlot)
                    {
                        AwarenessPairs++;
                    }
                }

                SimCollisionPairs = total;
                Trace.Add("bubble actor=" + actorSlot + " radius=" + radius[actorSlot].ToString(
                    "0.####", CultureInfo.InvariantCulture) + " neighbours=" + AwarenessPairs);
            }
        }

        // Adapter-only awareness query: count the other player bodies inside the plan radius. This
        // does not feed the Sim collision response; it is the observable value the Sim can expose
        // until a real BoundBubble/colObject path exists.
        public int CountAwarenessNeighbours(Simulation sim, int slot)
        {
            if (sim == null || sim.Players == null || slot < 0 || slot >= sim.Players.Length)
            {
                return 0;
            }

            float r = radius[slot];

            if (r <= 0.0f)
            {
                return 0;
            }

            SimVec3 centre = sim.Players[slot].Body.Pos;
            int count = 0;

            for (int other = 0; other < sim.Players.Length; other++)
            {
                if (other == slot)
                {
                    continue;
                }

                SimVec3 position = sim.Players[other].Body.Pos;
                float dx = position.x - centre.x;
                float dy = position.y - centre.y;
                float dz = position.z - centre.z;

                if ((dx * dx) + (dy * dy) + (dz * dz) <= r * r)
                {
                    count++;
                }
            }

            return count;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append("_STATE applied=").Append(AppliedEvents)
                .Append(" rejected=").Append(RejectedEvents)
                .Append(" lastSlot=").Append(LastSlot)
                .Append(" lastRadius=").Append(LastRadius.ToString("0.####",
                    CultureInfo.InvariantCulture))
                .Append(" awarenessPairs=").Append(AwarenessPairs)
                .Append(" simCollisionPairs=").Append(SimCollisionPairs)
                .Append(" simCollisionPath=SimAwarenessBubbles");

            for (int slot = 0; slot < PlayerSlots; slot++)
            {
                if (radius[slot] >= 0.0f)
                {
                    text.Append(" [slot=").Append(slot).Append(" radius=")
                        .Append(radius[slot].ToString("0.####", CultureInfo.InvariantCulture))
                        .Append(']');
                }
            }

            return text.ToString();
        }

        public string TraceText(int max)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < Trace.Count && i < max; i++)
            {
                text.Append(Marker).Append("_TRACE ").AppendLine(Trace[i]);
            }

            return text.ToString();
        }
    }
}
