using System;
using System.Collections.Generic;
using System.Globalization;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // N7 result: the N6 human loop, the head-camera aim beat, the goal celebration/face-off restart
    // and the fallback AI possession phase.
    public sealed class MatchPlayN7ScriptResult
    {
        public int Failures;
        public int HumanPickups;
        public int HumanShots;
        public int Goals;
        public int HumanGoals;
        public int AiPickups;
        public int AiShots;
        public int AiPasses;
        public int AiGoals;
        public int Bounces;
        public int ScoreHome;
        public int ScoreAway;
        public int ApproachTicks;
        public int FaceOffSetupTick;
        public int FaceOffResumeTick;
        public int AiPickupTick;
        public int AiShotTick;
        public MatchFlowPhase FinalPhase;
        public float HeadCamViewFracX;
        public float HeadCamViewFracY;
        public float HeadCamFovDegrees;
        public uint HeadCamShotWord;
        public SimVec3 HeadCamShotVelocity;
        public SimVec3 CentreShotVelocity;
        public bool HeadCamShotVectorDiffers;
        public string FailureText = string.Empty;
        public readonly List<string> Steps = new List<string>();
    }

    // Deterministic N7 played run. It is the same state machine the interactive player uses and it
    // captures these beats for the Editor and Windows player runs:
    //   start, pickup, charge, headcam, shot, goal, celebration, faceoff, restart, ai-pickup,
    //   ai-shot, score
    public static class MatchPlayN7Script
    {
        public const int ChargeTicks = 15;
        public const int HeadCamAimTicks = 6;
        public const int RestartWaitTicks = 100;

        public static MatchPlayN7ScriptResult Run(MatchPlayControl control, Action<string> capture,
            Action<HeldBinding> sceneTick = null)
        {
            MatchPlayN7ScriptResult result = new MatchPlayN7ScriptResult();
            Simulation sim = control.Sim;
            Action<HeldBinding> tick = sceneTick ?? (held => sim.Tick());

            result.HeadCamFovDegrees = MatchHeadCamera.FieldOfViewDegrees;

            // --- the N6 human loop -----------------------------------------------------------------
            MatchPossessionRules.StageHumanForTest(sim, control.Config.HumanSlot, 0.0f, -100.0f);

            for (int i = 0; i < 60; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            capture?.Invoke("start");

            MatchPossessionRules.PlaceRokForApproachTest(sim, control.Config.HumanSlot, 30.0f);

            int approachTicks = 0;

            for (; approachTicks < 180 && !control.Carrying; approachTicks++)
            {
                control.Advance(HeldBinding.Forward, tick);
            }

            result.ApproachTicks = approachTicks;

            if (!control.Carrying)
            {
                result.Failures++;
                result.FailureText += "human approach pickup did not happen; ";
            }

            capture?.Invoke("pickup");

            for (int i = 0; i < ChargeTicks; i++)
            {
                control.Advance(HeldBinding.Shoot, tick);
            }

            if (!control.Carrying || control.ShotPowerLevel <= 0)
            {
                result.Failures++;
                result.FailureText += "human charge did not build; ";
            }

            capture?.Invoke("charge");

            // CHeadTrack aim beat: the real rotate held bit moves the reticle fractions while the
            // carrier is charging. The capture renders this head-camera pose.
            for (int i = 0; i < HeadCamAimTicks; i++)
            {
                control.Advance(HeldBinding.Shoot | HeldBinding.RotateRight, tick);
            }

            control.HeadCam.Update(sim, control.Config.HumanSlot);
            result.HeadCamViewFracX = control.HeadCam.ViewFracX;
            result.HeadCamViewFracY = control.HeadCam.ViewFracY;

            if (control.HeadCam.ViewFracX <= MatchHeadCamera.ViewFracMid + 1e-4f)
            {
                result.Failures++;
                result.FailureText += "head-camera reticle did not move; ";
            }

            capture?.Invoke("headcam");

            // Aim back to the centre before the goal shot so the scored human shot is the N6 shot;
            // the aimed-shot vector check runs as its own deterministic scenario in the gate.
            control.HeadCam.ResetAim();
            control.Advance(HeldBinding.None, tick);

            for (int i = 0; i < 4; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            result.HumanShots = control.HumanShots;
            result.CentreShotVelocity = control.LastHumanShotVelocity;
            result.HeadCamShotWord = control.LastHumanShotWord;

            if (control.HumanShots != 1)
            {
                result.Failures++;
                result.FailureText += "human shot count " + control.HumanShots + "; ";
            }

            capture?.Invoke("shot");

            for (int i = 0; i < 180 && control.Goals == 0; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            result.Goals = control.Goals;
            result.HumanGoals = control.HumanGoals;

            if (control.Goals != 1 || control.HumanGoals != 1)
            {
                result.Failures++;
                result.FailureText += "human goal did not score (goals=" + control.Goals
                                      + " human=" + control.HumanGoals + "); ";
            }

            capture?.Invoke("goal");

            // Goal celebration beat, then the shipped face-off positions.
            for (int i = 0; i < 8; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            capture?.Invoke("celebration");

            int guard = 0;

            while (control.Phase != MatchFlowPhase.FaceOffDrag && guard < 120)
            {
                control.Advance(HeldBinding.None, tick);
                guard++;
            }

            result.FaceOffSetupTick = control.Ticks;

            if (control.Phase != MatchFlowPhase.FaceOffDrag)
            {
                result.Failures++;
                result.FailureText += "face-off setup phase did not start; ";
            }

            capture?.Invoke("faceoff");

            guard = 0;

            while (control.Phase != MatchFlowPhase.Live && guard < 60)
            {
                control.Advance(HeldBinding.None, tick);
                guard++;
            }

            result.FaceOffResumeTick = control.Ticks;

            if (control.Phase != MatchFlowPhase.Live)
            {
                result.Failures++;
                result.FailureText += "face-off did not resume play; ";
            }

            capture?.Invoke("restart");

            // --- fallback AI possession -----------------------------------------------------------
            control.HumanPickupEnabled = false; // the human stays at the face-off corner in this beat
            int aiGuard = 0;

            while (control.AiPickups == 0 && aiGuard < 240)
            {
                control.Advance(HeldBinding.None, tick);
                aiGuard++;
            }

            result.AiPickupTick = control.Ticks;

            if (control.AiPickups == 0)
            {
                result.Failures++;
                result.FailureText += "AI did not pick up the free ROK; ";
            }

            capture?.Invoke("ai-pickup");

            aiGuard = 0;

            while (control.AiShots == 0 && aiGuard < 240)
            {
                control.Advance(HeldBinding.None, tick);
                aiGuard++;
            }

            result.AiShotTick = control.Ticks;

            if (control.AiShots == 0)
            {
                result.Failures++;
                result.FailureText += "AI did not take a shot; ";
            }

            capture?.Invoke("ai-shot");

            // Let the AI shot resolve enough for the final HUD/score frame.
            for (int i = 0; i < 60; i++)
            {
                control.Advance(HeldBinding.None, tick);
            }

            capture?.Invoke("score");

            result.FinalPhase = control.Phase;
            result.HumanPickups = control.HumanPickups;
            result.HumanShots = control.HumanShots;
            result.Goals = control.Goals;
            result.HumanGoals = control.HumanGoals;
            result.AiPickups = control.AiPickups;
            result.AiShots = control.AiShots;
            result.AiPasses = control.AiPasses;
            result.AiGoals = control.AiGoals;
            result.Bounces = control.Bounces;
            result.ScoreHome = sim.Rules.ScoreHome;
            result.ScoreAway = sim.Rules.ScoreAway;

            if (result.HumanPickups < 1 || result.HumanShots < 1 || result.HumanGoals < 1)
            {
                result.Failures++;
                result.FailureText += "human loop counts; ";
            }

            if (result.AiPickups < 1 || result.AiShots < 1)
            {
                result.Failures++;
                result.FailureText += "AI possession counts; ";
            }

            result.Steps.Add("approachTicks=" + result.ApproachTicks
                             + " humanPickups=" + result.HumanPickups
                             + " humanShots=" + result.HumanShots
                             + " humanGoals=" + result.HumanGoals
                             + " aiPickups=" + result.AiPickups
                             + " aiShots=" + result.AiShots
                             + " aiGoals=" + result.AiGoals
                             + " score=" + result.ScoreHome + "-" + result.ScoreAway
                             + " headCam=(" + result.HeadCamViewFracX.ToString("0.###", CultureInfo.InvariantCulture)
                             + "," + result.HeadCamViewFracY.ToString("0.###", CultureInfo.InvariantCulture) + ")");

            return result;
        }
        // --- deterministic head-camera shot-vector check ---------------------------------------
        // Two fresh matches: the first centre-aim human shot and the second one aimed right by
        // holding the real rotate held bit for HeadCamAimTicks during the charge. The shot words and
        // initial ROK velocities must differ, and the head-camera fractions must be the CHeadTrack
        // values. This is the shot-vector evidence for the head-camera path.
        public static bool HeadCamAimCheck(MatchWorldData world, out string detail)
        {
            detail = string.Empty;

            MatchSimulation centreSim;
            MatchPlayControl centre;
            MatchAudioDirector unusedAudio;
            Simulation centreRaw;
            CreateScenario(world, false, out centreSim, out centre, out unusedAudio);
            centreRaw = centreSim.Sim;

            centre.HeadCam.ResetAim();
            PrepareHumanCarry(centre, centreSim);
            ChargeAndShoot(centre, centreSim, 0, false);
            uint centreWord = centre.LastHumanShotWord;
            SimVec3 centreVelocity = centre.LastHumanShotVelocity;
            int centreAimX = RokState.ShotWordAimX(centreWord);

            MatchSimulation aimSim;
            MatchPlayControl aim;
            MatchAudioDirector aimAudio;
            CreateScenario(world, false, out aimSim, out aim, out aimAudio);

            aim.HeadCam.ResetAim();
            PrepareHumanCarry(aim, aimSim);
            ChargeAndShoot(aim, aimSim, HeadCamAimTicks, true);
            uint aimWord = aim.LastHumanShotWord;
            SimVec3 aimVelocity = aim.LastHumanShotVelocity;
            int aimX = RokState.ShotWordAimX(aimWord);

            float dx = aimVelocity.x - centreVelocity.x;
            float dy = aimVelocity.y - centreVelocity.y;
            float dz = aimVelocity.z - centreVelocity.z;
            float difference = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            bool ok = aim.HeadCam.ViewFracX > MatchHeadCamera.ViewFracMid + 1e-4f
                      && aimX > centreAimX
                      && difference > 1e-3f;

            detail = "fovDegrees=" + MatchHeadCamera.FieldOfViewDegrees.ToString("0.###", CultureInfo.InvariantCulture)
                     + " centreWord=0x" + centreWord.ToString("x8", CultureInfo.InvariantCulture)
                     + " aimWord=0x" + aimWord.ToString("x8", CultureInfo.InvariantCulture)
                     + " centreAimX=" + centreAimX
                     + " aimX=" + aimX
                     + " aimedViewFracX=" + aim.HeadCam.ViewFracX.ToString("0.###", CultureInfo.InvariantCulture)
                     + " centreVel=(" + MatchSimulation.F(centreVelocity.x) + ","
                     + MatchSimulation.F(centreVelocity.y) + ","
                     + MatchSimulation.F(centreVelocity.z) + ")"
                     + " aimVel=(" + MatchSimulation.F(aimVelocity.x) + ","
                     + MatchSimulation.F(aimVelocity.y) + ","
                     + MatchSimulation.F(aimVelocity.z) + ")"
                     + " velocityDelta=" + difference.ToString("0.####", CultureInfo.InvariantCulture);

            centreRaw = null;
            return ok;
        }

        // One fresh match with the human carrying the ROK in front of the south goal.
        private static void PrepareHumanCarry(MatchPlayControl control, MatchSimulation sim)
        {
            MatchPossessionRules.StageHumanForTest(sim.Sim, control.Config.HumanSlot, 0.0f, -100.0f);
            Ticks(control, sim, HeldBinding.None, 20);
            MatchPossessionRules.PlaceRokForApproachTest(sim.Sim, control.Config.HumanSlot, 30.0f);
            int guard = 0;

            while (!control.Carrying && guard < 180)
            {
                Ticks(control, sim, HeldBinding.Forward, 1);
                guard++;
            }
        }

        private static void ChargeAndShoot(MatchPlayControl control, MatchSimulation sim, int aimTicks, bool aim)
        {
            Ticks(control, sim, HeldBinding.Shoot, ChargeTicks);

            if (aim && aimTicks > 0)
            {
                Ticks(control, sim, HeldBinding.Shoot | HeldBinding.RotateRight, aimTicks);
            }

            Ticks(control, sim, HeldBinding.None, 1);
        }

        private static void Ticks(MatchPlayControl control, MatchSimulation sim, HeldBinding held, int count)
        {
            for (int i = 0; i < count; i++)
            {
                control.Advance(held, () => sim.Step());
            }
        }

        // --- deterministic AI pass check --------------------------------------------------------
        // The same fallback AI as the played run, forced through its pass request once it has the
        // ROK. It proves AiPickups and AiPasses through the same MatchPlayControl rules.
        public static bool AiPassCheck(MatchWorldData world, out string detail)
        {
            detail = string.Empty;

            MatchSimulation sim;
            MatchPlayControl control;
            MatchAudioDirector audio;
            CreateScenario(world, true, out sim, out control, out audio);

            control.HumanPickupEnabled = false;
            control.Ai.ForceNextPass = true;

            int guard = 0;

            while ((control.AiPickups == 0 || control.AiPasses == 0) && guard < 360)
            {
                control.Advance(HeldBinding.None, () => sim.Step());
                guard++;
            }

            bool ok = control.AiPickups >= 1 && control.AiPasses >= 1;
            detail = "aiPickups=" + control.AiPickups
                     + " aiPasses=" + control.AiPasses
                     + " aiShots=" + control.AiShots
                     + " ticks=" + control.Ticks
                     + " aiSlot=" + (control.Ai == null || control.Ai.LastActiveSlot < 0 ? "none" : control.Ai.LastActiveSlot.ToString(CultureInfo.InvariantCulture));

            return ok;
        }

        private static void CreateScenario(MatchWorldData world, bool ai, out MatchSimulation sim,
            out MatchPlayControl control, out MatchAudioDirector audio)
        {
            sim = MatchSimulation.Create(world);

            for (int slot = 0; slot < sim.Sim.Players.Length; slot++)
            {
                sim.Sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            sim.Sim.Rules.SetHomeScore(0);
            sim.Sim.Rules.SetAwayScore(0);

            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            audio = new MatchAudioDirector(null);
            MatchPlayControl localControl = new MatchPlayControl(sim.Sim, config, audio);

            if (ai)
            {
                localControl.AttachAi(ObjectConstants.SlotDromer1, ObjectConstants.SlotDromer2);
                localControl.HumanPickupEnabled = false;
            }
            else
            {
                localControl.AiEnabled = false;
            }

            sim.Sim.InputForSlot = slot => localControl.InputForSlot(slot);
            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            localControl.ResetRokToSpawn();
            control = localControl;
        }

    }
}
