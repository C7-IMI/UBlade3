using System;
using System.Globalization;
using System.Text;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The three states the play layer adds to the N6 loop: live play, the goal/celebration hold, and
    // the face-off drag that resets the shipped positions before the next possession.
    public enum MatchFlowPhase
    {
        Live = 0,
        GoalCelebration = 1,
        FaceOffDrag = 2,
    }

    // The post-goal restart. The shipped plans do this through the goal plans and the ROK plan:
    //   NGOAL.PBN PostScoreWait4Drag: HeadCamOff, NoInputForSec, DropRok, SET_TIMEOUTMSG 1400 ->
    //     Stop -> Extra0 -> DropRok;
    //   HUMAN.PBN GettingThere (the SkateToFaceOff target): SetLocation(28,28)/(28,-28)/(-28,28)/
    //     (-28,-28) by MyID, RelaxYawToFace(26), SetAnimation(42);
    //   ROK.PBN RokDrag: SetLocation(0,0,-95), render flag 0, the four InDrag timeouts (700 ms).
    // The plan VM's goal agenda chain is not ported, so this class applies those exact positions and
    // timings directly on the sim bodies and documents the places where the retail flow differs
    // (the ROK plan's auto-launch after the drag, and the goalies' separate plans).
    public static class MatchFaceOff
    {
        // NGOAL.PBN PostScoreWait4Drag: SET_TIMEOUTMSG 1400, id_MSG_Stop.
        public const int GoalCelebrationTicks = 42;

        // ROK.PBN RokDrag: SET_TIMEOUTMSG 700, id_MSG_InDrag for each skater.
        public const int DragTicks = 21;

        public const int PostGoalRestartTicks = GoalCelebrationTicks + DragTicks;

        // HUMAN.PBN GettingThere SetLocation arguments.
        public const float PlayerCorner = 28.0f;

        // ROK.PBN RokDrag SetLocation(0,0,-95); the plan hides the ROK (render flag 0).
        public const float RokDragHeight = -95.0f;

        public static void StopPlay(Simulation sim)
        {
            sim.Rules.SetRunState(0, sim.Rules.Clock.TickMs);
        }

        public static void ResumePlay(Simulation sim)
        {
            sim.Rules.SetRunState(1, sim.Rules.Clock.TickMs);
        }

        public static SimVec3 PlayerCornerForSlot(int slot)
        {
            // The four HUMAN GettingThere SetLocation pairs, indexed by id_DCF_MyID.
            float x = (slot == 0 || slot == 1) ? PlayerCorner : -PlayerCorner;
            float y = (slot == 0 || slot == 2) ? PlayerCorner : -PlayerCorner;
            return new SimVec3(x, y, 0.0f);
        }

        // Sets every dromer 0..3 to its shipped face-off corner on the arena shell, heading to the
        // centre. Goalies 4/5 are deliberately left where their goal plans put them; the shipped
        // goalie plans are a separate flow and are not part of this bounded restart.
        public static void ApplyPlayerFaceOff(Simulation sim)
        {
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            for (int slot = 0; slot < 4 && slot < sim.Players.Length; slot++)
            {
                PlayerState player = sim.Players[slot];
                SimVec3 corner = PlayerCornerForSlot(slot);
                float z = arena.GetZ(corner.x, corner.y);
                SimVec3 position = new SimVec3(corner.x, corner.y, z);
                SimVec3 normal;

                if (!arena.NormalAtPoint(position, out normal))
                {
                    normal = new SimVec3(0.0f, 0.0f, 1.0f);
                }

                player.Body.Pos = position;
                player.Body.PrevPos = position;
                player.Body.Vel = SimVec3.Zero;
                player.Body.SmoothVel = SimVec3.Zero;
                player.Body.Axis = SimVec3.Zero;
                player.Body.Speed = 0.0f;
                player.Body.SurfNormal = normal;
                player.Body.VelDir = new SimVec3(0.0f, -1.0f, 0.0f);
                player.Body.VelDirLatched = player.Body.VelDir;
                player.AimPosition = new SimVec3(corner.x, corner.y, z + MatchWorldData.HeadHeight);
            }
        }

        // ROK.PBN RokDrag: hide the ROK at (0,0,-95).
        public static void ApplyRokFaceOff(Simulation sim)
        {
            RokState rok = sim.Rok;
            SimVec3 position = new SimVec3(0.0f, 0.0f, RokDragHeight);
            rok.Body.Pos = position;
            rok.Body.PrevPos = position;
            rok.Body.Vel = SimVec3.Zero;
            rok.Body.SmoothVel = SimVec3.Zero;
            rok.Body.Speed = 0.0f;
            rok.Body.VelDir = new SimVec3(0.0f, 1.0f, 0.0f);
            rok.Body.VelDirLatched = rok.Body.VelDir;
            rok.Body.SurfNormal = new SimVec3(0.0f, 0.0f, 1.0f);
            rok.AtSpawnSlot = true;
            rok.KillState = RokConstants.InitialKillState;
            rok.RenderBone = -1; // render flag 0: every bone hidden while the drag hides the ROK
        }

        // The drag's end fallback: the retail ROK plan auto-launches the hidden ROK at the end of the
        // 700 ms drag; the plan VM is not ported, so the ROK is dropped visibly on the arena floor at
        // the drome's spawn point for the next possession instead. Documented divergence.
        public static void DropRokForNextPossession(Simulation sim)
        {
            MatchScoreFixture.PlaceRokAtSpawn(sim);
        }

        public static string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("celebrationTicks=").Append(GoalCelebrationTicks.ToString(CultureInfo.InvariantCulture));
            text.Append(" dragTicks=").Append(DragTicks.ToString(CultureInfo.InvariantCulture));
            text.Append(" restartTicks=").Append(PostGoalRestartTicks.ToString(CultureInfo.InvariantCulture));
            text.Append(" playerCorner=").Append(PlayerCorner.ToString("0.#", CultureInfo.InvariantCulture));
            text.Append(" rokDragHeight=").Append(RokDragHeight.ToString("0.#", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
