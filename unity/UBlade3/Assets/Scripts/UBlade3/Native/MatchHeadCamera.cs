using System;
using System.Globalization;
using System.Text;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The original head camera is CameraObject slot 25. Its constructor sets m_flHalfFov to
    // 85/2 degrees (cameraobject.cpp 0x00421980); CHeadTrack (planvm/cheadtrack.cpp 0x0040a100)
    // puts the camera at the tracked skater's HEAD_BONE=9 world position, points it along the
    // skater's velDir, and moves the reticle's view fractions from the rotate/throttle held bits
    // (CHeadTrack::Process: flStep = ((shotPower - 6)*0.1 + g_flCamFracFull) * flDelta, clamped to
    // [0,1]; negative rotate moves X, positive forward throttle moves Y down).
    //
    // This is the bounded port: the animation bone transform is not exposed by the slice, so the
    // eye is the existing sim AimPosition (MatchWorldData.HeadHeight = 5.9822, the same head height
    // the chase camera uses), and the view direction is the skater's body velocity direction. The
    // view fractions are the real CHeadTrack state and are packed into the shot word exactly like
    // PlanAction::SendLaunchMessage packs the head camera's (viewFracX, shotPower, viewFracY).
    public sealed class MatchHeadCamera
    {
        public const float FieldOfViewDegrees = 85.0f;

        public const float HalfFieldOfViewRadians = 0.7417649f;

        public const float ViewFracMid = 0.5f;

        public const float CamFracFull = 1.0f;

        public float ViewFracX = ViewFracMid;

        public float ViewFracY = ViewFracMid;

        public SimVec3 Eye;

        public SimVec3 Forward;

        public SimVec3 Up;

        public SimVec3 Right;

        public int HumanSlot = MatchSimulation.HumanSlot;

        public void ResetAim()
        {
            ViewFracX = ViewFracMid;
            ViewFracY = ViewFracMid;
        }

        // Camera pose: head position and the skater's facing. Called before a render/capture and
        // before the shot so a capture can render this exact pose.
        public void Update(Simulation sim, int humanSlot)
        {
            HumanSlot = humanSlot;
            PlayerState human = sim.Players[humanSlot];
            Eye = human.AimPosition;

            SimVec3 forward = human.Body.VelDir;
            float length = (float)Math.Sqrt(
                (forward.x * forward.x) + (forward.y * forward.y) + (forward.z * forward.z));

            if (length < 1e-6f)
            {
                forward = new SimVec3(0.0f, 1.0f, 0.0f);
            }
            else
            {
                forward = new SimVec3(forward.x / length, forward.y / length, forward.z / length);
            }

            SimVec3 up = human.Body.SurfNormal;
            float upLength = (float)Math.Sqrt((up.x * up.x) + (up.y * up.y) + (up.z * up.z));

            if (upLength < 1e-6f)
            {
                up = new SimVec3(0.0f, 0.0f, 1.0f);
            }
            else
            {
                up = new SimVec3(up.x / upLength, up.y / upLength, up.z / upLength);
            }

            // right = forward x up; up = right x forward (cameraobject.cpp ViewIt's basis order).
            SimVec3 right = new SimVec3(
                (forward.y * up.z) - (forward.z * up.y),
                (forward.z * up.x) - (forward.x * up.z),
                (forward.x * up.y) - (forward.y * up.x));
            float rightLength = (float)Math.Sqrt(
                (right.x * right.x) + (right.y * right.y) + (right.z * right.z));

            if (rightLength < 1e-6f)
            {
                right = new SimVec3(1.0f, 0.0f, 0.0f);
            }
            else
            {
                right = new SimVec3(right.x / rightLength, right.y / rightLength, right.z / rightLength);
            }

            Forward = forward;
            Up = new SimVec3(
                (right.y * forward.z) - (right.z * forward.y),
                (right.z * forward.x) - (right.x * forward.z),
                (right.x * forward.y) - (right.y * forward.x));
            Right = right;
        }

        // CHeadTrack::Process reticle movement: the rotate bits move X by the shot-power-scaled
        // step, the throttle bits move Y (forward decreases Y), both clamped to [0,1].
        public void TickAim(Simulation sim, HeldBinding held, int humanSlot)
        {
            Update(sim, humanSlot);

            EngineClock clock = sim.Rules.Clock;
            float step = (((sim.Players[humanSlot].Stats.GetScalar(ObjectConstants.RatingShotPower) - 6.0f) * 0.1f)
                          + CamFracFull) * clock.GetDelta();

            if ((held & HeldBinding.Rotate) != 0)
            {
                if ((held & HeldBinding.RotateRight) != 0)
                {
                    ViewFracX += step;
                }
                else
                {
                    ViewFracX -= step;
                }
            }
            else if ((held & HeldBinding.Throttle) != 0)
            {
                if ((held & HeldBinding.Forward) != 0)
                {
                    ViewFracY -= step;
                }
                else
                {
                    ViewFracY += step;
                }
            }

            ViewFracX = Math.Max(0.0f, Math.Min(1.0f, ViewFracX));
            ViewFracY = Math.Max(0.0f, Math.Min(1.0f, ViewFracY));
        }

        public uint PackShotWord(int shotPowerLevel, out int resetShotPowerLevel)
        {
            return RokState.PackShotWord(ViewFracX, shotPowerLevel, ViewFracY, out resetShotPowerLevel);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("eye=(").Append(MatchSimulation.F(Eye.x)).Append(',')
                .Append(MatchSimulation.F(Eye.y)).Append(',')
                .Append(MatchSimulation.F(Eye.z)).Append(')');
            text.Append(" forward=(").Append(MatchSimulation.F(Forward.x)).Append(',')
                .Append(MatchSimulation.F(Forward.y)).Append(',')
                .Append(MatchSimulation.F(Forward.z)).Append(')');
            text.Append(" fovDegrees=").Append(FieldOfViewDegrees.ToString("0.###", CultureInfo.InvariantCulture));
            text.Append(" halfFovRad=").Append(HalfFieldOfViewRadians.ToString("0.#######", CultureInfo.InvariantCulture));
            text.Append(" viewFrac=(").Append(ViewFracX.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(',').Append(ViewFracY.ToString("0.####", CultureInfo.InvariantCulture)).Append(')');
            return text.ToString();
        }
    }
}
