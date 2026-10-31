using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>Plan-side state written by the N12 action bodies and consumed by the match adapter.</summary>
    public sealed class PlanAiActionState
    {
        public bool FrictionDirty;

        public float Friction;

        public bool LocationDirty;

        public PlanAiVec3 Location;

        public bool VelocityDirty;

        public PlanAiVec3 Velocity;

        public float AxisXDelta;

        public bool SpeedBurstActive;

        public float SpeedBurstTarget;

        public float SpeedBurstBoost;

        public bool SpeedBurstRemove;

        public int AnimationIndex = -1;

        public float AnimationRate = -1.0f;

        public int AnimationFlags;

        public int AnimationEvents;

        public int LastSoundIndex = -1;

        public int LastSoundGroup = -1;

        public int SoundEvents;

        public readonly List<int> PendingSounds = new List<int>();

        public int LastStatSlot = -1;

        public int LastStatId = -1;

        public int StatEvents;

        public int BubbleScaleEvents;

        public float LastBubbleRadius = -1.0f;

        public int LastBubbleColObject = -1;

        public int LastStatTimerSlot = -1;

        public int LastStatTimerId = -1;

        public int StatTimerStartEvents;

        public int StatTimerStopEvents;

        public int FrictionEvents;

        public int LocationEvents;

        public int SpeedBurstEvents;
    }

    /// <summary>Per-PlanAiObject storage for the N12 action bodies (new files only; no mock-object edit).</summary>
    public static class PlanAiActionStateStore
    {
        private static readonly Dictionary<PlanAiObject, PlanAiActionState> States =
            new Dictionary<PlanAiObject, PlanAiActionState>();

        public static PlanAiActionState Get(PlanAiObject actor)
        {
            PlanAiActionState state;

            if (!States.TryGetValue(actor, out state))
            {
                state = new PlanAiActionState();
                States[actor] = state;
            }

            return state;
        }

        public static void Clear(PlanAiObject actor)
        {
            States.Remove(actor);
        }
    }
}
