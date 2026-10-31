using System.Collections.Generic;
using System;

namespace UBlade3.Sim
{
    /// <summary>
    /// N17 additive Sim awareness-bubble binding. The original PlanAction::ScaleBubToRating
    /// (plancond.cpp HYPERX 0x00461ea0) writes BoundBubble::m_flRadius on the collision object
    /// named by the plan; the six player awareness objects are COB_DR0_AWARENESS (0x29) through
    /// COB_DR5_AWARENESS (0x2e), as numbered in engine/collisionobjectids.h and DATA/ENUMIDS.H.
    /// The shipped Sim slice had no per-player BoundBubble/colObject, so this file adds the
    /// documented radius state and a nearest-opponent/collision query over the live player bodies.
    /// The original collision shape is a bound bubble/capsule; this bounded port treats the radius
    /// as a sphere around the player body centre and says so in the docs.
    /// </summary>
    public struct AwarenessCollision
    {
        public int OwnerSlot;

        public int OtherSlot;

        public float Distance;

        public float Radius;

        public bool Opponent;
    }

    public static class AwarenessBubbles
    {
        public const string Marker = "UBLADE3_SIM_AWARENESS_BUBBLE";

        public const int FirstAwarenessCollisionObject = 0x29;

        public const int LastAwarenessCollisionObject = 0x2e;

        public const int PlayerSlots = ObjectConstants.PlayerObjectCount;

        private static readonly Dictionary<PlayerState, float> Radii =
            new Dictionary<PlayerState, float>();

        public static int SetRadiusCalls;

        public static int CollisionQueries;

        public static int CollisionsObserved;

        public static int NearestQueries;

        public static bool TryGetSlotForCollisionObject(int collisionObject, out int slot)
        {
            if (collisionObject >= FirstAwarenessCollisionObject
                && collisionObject <= LastAwarenessCollisionObject)
            {
                slot = collisionObject - FirstAwarenessCollisionObject;
                return true;
            }

            slot = -1;
            return false;
        }

        public static void SetRadius(Simulation sim, int slot, float radius)
        {
            if (sim == null || sim.Players == null || slot < 0 || slot >= sim.Players.Length)
            {
                return;
            }

            float value = float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0.0f
                ? 0.0f : radius;
            Radii[sim.Players[slot]] = value;
            SetRadiusCalls++;
        }

        public static float GetRadius(Simulation sim, int slot)
        {
            float value;

            if (sim == null || sim.Players == null || slot < 0 || slot >= sim.Players.Length)
            {
                return 0.0f;
            }

            return Radii.TryGetValue(sim.Players[slot], out value) ? value : 0.0f;
        }

        public static void Clear(Simulation sim)
        {
            if (sim == null || sim.Players == null)
            {
                return;
            }

            for (int slot = 0; slot < sim.Players.Length; slot++)
            {
                Radii.Remove(sim.Players[slot]);
            }
        }

        public static bool HasAnyRadius(Simulation sim)
        {
            if (sim == null || sim.Players == null)
            {
                return false;
            }

            for (int slot = 0; slot < sim.Players.Length; slot++)
            {
                if (GetRadius(sim, slot) > 0.0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Nearest opponent inside the actor's awareness bubble; zero when none.</summary>
        public static int NearestOpponent(Simulation sim, int slot, out int otherSlot, out float distance)
        {
            otherSlot = -1;
            distance = float.MaxValue;
            NearestQueries++;

            if (sim == null || sim.Players == null || slot < 0 || slot >= sim.Players.Length)
            {
                return 0;
            }

            float radius = GetRadius(sim, slot);

            if (radius <= 0.0f)
            {
                return 0;
            }

            Vec3 centre = sim.Players[slot].Body.Pos;

            for (int other = 0; other < sim.Players.Length; other++)
            {
                if (other == slot || ((other ^ slot) & 1) == 0)
                {
                    continue;
                }

                float d = FlatDistance(centre, sim.Players[other].Body.Pos);

                if (d <= radius && d < distance)
                {
                    distance = d;
                    otherSlot = other;
                }
            }

            return otherSlot >= 0 ? 1 : 0;
        }

        /// <summary>All player pairs whose awareness bubble contains the other body.</summary>
        public static int CollectCollisions(Simulation sim, List<AwarenessCollision> output)
        {
            CollisionQueries++;

            if (output != null)
            {
                output.Clear();
            }

            if (sim == null || sim.Players == null)
            {
                return 0;
            }

            int collisions = 0;

            for (int owner = 0; owner < sim.Players.Length; owner++)
            {
                float radius = GetRadius(sim, owner);

                if (radius <= 0.0f)
                {
                    continue;
                }

                Vec3 centre = sim.Players[owner].Body.Pos;

                for (int other = 0; other < sim.Players.Length; other++)
                {
                    if (other == owner)
                    {
                        continue;
                    }

                    float d = FlatDistance(centre, sim.Players[other].Body.Pos);

                    if (d > radius)
                    {
                        continue;
                    }

                    collisions++;
                    CollisionsObserved++;

                    if (output != null)
                    {
                        AwarenessCollision collision = new AwarenessCollision();
                        collision.OwnerSlot = owner;
                        collision.OtherSlot = other;
                        collision.Distance = d;
                        collision.Radius = radius;
                        collision.Opponent = ((owner ^ other) & 1) != 0;
                        output.Add(collision);
                    }
                }
            }

            return collisions;
        }

        private static float FlatDistance(Vec3 a, Vec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static string Describe(Simulation sim)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append(Marker).Append("_STATE setCalls=").Append(SetRadiusCalls)
                .Append(" queries=").Append(CollisionQueries)
                .Append(" nearestQueries=").Append(NearestQueries)
                .Append(" collisions=").Append(CollisionsObserved);

            for (int slot = 0; slot < PlayerSlots; slot++)
            {
                float radius = GetRadius(sim, slot);

                if (radius > 0.0f)
                {
                    text.Append(" [slot=").Append(slot).Append(" radius=")
                        .Append(radius.ToString("0.####")).Append(']');
                }
            }

            return text.ToString();
        }
    }
}
