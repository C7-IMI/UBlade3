using System;
using System.Collections.Generic;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The bounded fallback AI possession controller. It is not the plan VM: it gives one or two AI
    // skaters the input mask the N4/N5 fallback already understands, steers them to the free ROK,
    // lets the same MatchPlayControl pickup/attach path take possession, and asks the same
    // MatchPossession rules to shoot (the shipped unaimed PlanAction::Launch branch, because an AI
    // has no head camera) or pass (the exact PlanAction::Throw math). The retail AI plans (OffWing,
    // Defense, Goalie, Sideln) remain unported and are listed as gaps in docs/NATIVE-MATCH-N7.md.
    public sealed class MatchAiController
    {
        public readonly List<int> Slots = new List<int>();

        public bool Enabled;

        public bool ForceNextPass;

        // N8: a plan-driven slot keeps the pickup/possession path but must not receive heuristic
        // shot/pass requests; the plan's own decisions drive those.
        public Func<int, bool> SuppressSlot;

        // N10: how many heuristic update passes were skipped because a slot is PlanVM-driven.
        public int SuppressedUpdateTicks;

        public int LastActiveSlot = -1;

        public HeldBinding GetInput(Simulation sim, int slot)
        {
            if (!Enabled || !Slots.Contains(slot))
            {
                return HeldBinding.None;
            }

            PlayerState actor = sim.Players[slot];
            RokState rok = sim.Rok;
            bool carrying = rok.IsCarried && rok.AttachedSlot == slot;

            if (carrying)
            {
                SimVec3 target = GoalPosition(sim, actor.TargetGoalSlot);
                return SteerToward(actor, target);
            }

            if (!rok.IsCarried)
            {
                return SteerToward(actor, rok.Body.Pos);
            }

            return HeldBinding.Forward;
        }

        // Called at the start of every live tick. It only records what the AI wants: the control
        // executes the action in its own BeforeTick so the same shot/pass code path is used.
        public void Update(Simulation sim, MatchPlayControl control)
        {
            if (!Enabled || control == null || control.Phase != MatchFlowPhase.Live)
            {
                return;
            }

            RokState rok = sim.Rok;
            int carrier = rok.IsCarried ? rok.AttachedSlot : -1;

            for (int i = 0; i < Slots.Count; i++)
            {
                int slot = Slots[i];

                if (slot < 0 || slot >= sim.Players.Length)
                {
                    continue;
                }

                if (SuppressSlot != null && SuppressSlot(slot))
                {
                    SuppressedUpdateTicks++;
                    continue;
                }

                PlayerState actor = sim.Players[slot];
                SimVec3 goal = GoalPosition(sim, actor.TargetGoalSlot);
                float distance = FlatDistance(actor.Body.Pos, goal);

                if (carrier == slot)
                {
                    LastActiveSlot = slot;

                    if (ForceNextPass)
                    {
                        ForceNextPass = false;
                        control.RequestAiPass(slot);
                        return;
                    }

                    if (distance < 140.0f && FacingDot(actor, goal) > 0.55f)
                    {
                        control.RequestAiShot(slot);
                        return;
                    }

                    return;
                }

                if (carrier < 0)
                {
                    float rokDistance = FlatDistance(actor.Body.Pos, rok.Body.Pos);

                    if (rokDistance < 3.0f)
                    {
                        LastActiveSlot = slot;
                    }
                }
            }
        }

        public static SimVec3 GoalPosition(Simulation sim, int targetGoalSlot)
        {
            GoalFrame frame = sim.Arena.GoalForSlot(targetGoalSlot);
            return frame.MouthCentre;
        }

        private static float FlatDistance(SimVec3 a, SimVec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static float FacingDot(PlayerState actor, SimVec3 target)
        {
            float dx = target.x - actor.Body.Pos.x;
            float dy = target.y - actor.Body.Pos.y;
            float length = (float)Math.Sqrt((dx * dx) + (dy * dy));

            if (length < 1e-6f)
            {
                return 1.0f;
            }

            return ((actor.Body.VelDir.x * dx) + (actor.Body.VelDir.y * dy)) / length;
        }

        // Steers the actor's heading at the target: Forward when there is distance (or when close,
        // to keep speed), and rotate left/right by the sign of the cross product of the current
        // heading and the desired flat direction. Sign convention is the sim's: a positive cross
        // (desired to the left of the heading) uses RotateLeft.
        private static HeldBinding SteerToward(PlayerState actor, SimVec3 target)
        {
            float dx = target.x - actor.Body.Pos.x;
            float dy = target.y - actor.Body.Pos.y;
            float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < 1e-6f)
            {
                return HeldBinding.None;
            }

            float desiredX = dx / distance;
            float desiredY = dy / distance;

            float vx = actor.Body.VelDir.x;
            float vy = actor.Body.VelDir.y;
            float vlength = (float)Math.Sqrt((vx * vx) + (vy * vy));

            if (vlength < 1e-6f)
            {
                return HeldBinding.Forward;
            }

            vx /= vlength;
            vy /= vlength;

            float cross = (vx * desiredY) - (vy * desiredX);
            float dot = (vx * desiredX) + (vy * desiredY);

            HeldBinding input = distance > 2.0f ? HeldBinding.Forward : HeldBinding.None;

            if (cross > 0.02f)
            {
                input |= HeldBinding.RotateLeft;
            }
            else if (cross < -0.02f)
            {
                input |= HeldBinding.RotateRight;
            }
            else if (dot < 0.98f && input == HeldBinding.None)
            {
                input = HeldBinding.Forward;
            }

            return input;
        }
    }
}
