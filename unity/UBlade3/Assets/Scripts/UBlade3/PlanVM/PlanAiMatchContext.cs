using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N12 match context for plan handler bodies that read engine state the mock PlanAiWorld does not
    /// carry: the match score (GameEngine::IsTeamWinning), the sender slot of the scene message being
    /// dispatched (MsgInteractor::GetTargetSlot for IsSenderTeammate), the per-slot speed ratio the
    /// SpeedBurst body reads, behaviour subclass metadata the adapter extracts from the compiled plan
    /// block (ModsTweenNFacing aim object for ClosestUncoveredOppn), and the arena terrain/position
    /// marker callbacks SetLocation needs. MatchPlanAi populates it from the live Simulation/roster;
    /// the pure PlanVM self-checks never set it and therefore see the same zeros as before.
    /// </summary>
    public static class PlanAiMatchContext
    {
        public const int PlayerObjectCount = 6;

        private static readonly int[] behaviorKind = new int[PlayerObjectCount];

        private static readonly int[] behaviorAimSlot = new int[PlayerObjectCount];

        private static readonly float[] speedRatio = new float[PlayerObjectCount];

        private static int homeScore;

        private static int awayScore;

        /// <summary>MsgInteractor::GetTargetSlot for the message whose handler is executing.</summary>
        public static int CurrentMessageSender = -1;

        public static Func<float, float, float> TerrainZ;

        public static Func<int, bool> MarkerResolver = index => false;

        public const float DefaultInvDelta = 30.0f;

        public const float DefaultDeltaSquared = 1.0f / 900.0f;

        public static float EngineInvDelta = DefaultInvDelta;

        public static float EngineDeltaSquared = DefaultDeltaSquared;

        private static readonly float[] forwardSpeedDt = new float[PlayerObjectCount];

        private static readonly int[] targetGoalSlot = new int[PlayerObjectCount];

        public static void SetClock(float invDelta, float deltaSquared)
        {
            EngineInvDelta = invDelta;
            EngineDeltaSquared = deltaSquared;
        }

        public static void SetForwardSpeedDt(int slot, float value)
        {
            if (slot >= 0 && slot < PlayerObjectCount)
            {
                forwardSpeedDt[slot] = value;
            }
        }

        public static float GetForwardSpeedDt(int slot)
        {
            return slot >= 0 && slot < PlayerObjectCount ? forwardSpeedDt[slot] : 0.0f;
        }

        public static void SetTargetGoal(int slot, int value)
        {
            if (slot >= 0 && slot < PlayerObjectCount)
            {
                targetGoalSlot[slot] = value;
            }
        }

        public static int GetTargetGoal(int slot)
        {
            return slot >= 0 && slot < PlayerObjectCount ? targetGoalSlot[slot] : PlanOpcodes.SlotNgoal;
        }

        public static Func<int, PlanAiVec3> MarkerPosition;


        public static void SetScores(int home, int away)
        {
            homeScore = home;
            awayScore = away;
        }

        /// <summary>GameEngine::IsTeamWinning: slots 0/2/4 are the home side, 1/3/5 the away side.</summary>
        public static bool IsTeamWinning(int slot)
        {
            return (slot & 1) == 0 ? homeScore > awayScore : awayScore > homeScore;
        }

        public static void SetSpeedRatio(int slot, float value)
        {
            if (slot >= 0 && slot < PlayerObjectCount)
            {
                speedRatio[slot] = value;
            }
        }

        public static float GetSpeedRatio(int slot)
        {
            return slot >= 0 && slot < PlayerObjectCount ? speedRatio[slot] : 0.0f;
        }

        public static void PublishBehavior(int slot, int kind, int aimSlot)
        {
            if (slot >= 0 && slot < PlayerObjectCount)
            {
                behaviorKind[slot] = kind;
                behaviorAimSlot[slot] = aimSlot;
            }
        }

        public static void GetBehavior(int slot, out int kind, out int aimSlot)
        {
            if (slot >= 0 && slot < PlayerObjectCount)
            {
                kind = behaviorKind[slot];
                aimSlot = behaviorAimSlot[slot];

                return;
            }

            kind = -1;
            aimSlot = -1;
        }

        public static bool TryTerrainZ(float x, float y, out float z)
        {
            if (TerrainZ == null)
            {
                z = 0.0f;

                return false;
            }

            z = TerrainZ(x, y);

            return true;
        }

        /// <summary>Resets the per-run state before a deterministic match probe (not called in normal play).</summary>
        public static void Reset()
        {
            CurrentMessageSender = -1;
            homeScore = 0;
            awayScore = 0;

            for (int index = 0; index < PlayerObjectCount; index++)
            {
                behaviorKind[index] = 0;
                behaviorAimSlot[index] = -1;
                speedRatio[index] = 0.0f;
            }
        }
    }
}
