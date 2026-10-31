using System;
using System.Globalization;
using System.Text;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // One goal-scoring result. The goal slot, score code and points are the shipped rules' answers;
    // the attribution follows the shipped goal plan data (see ScoringSideForGoal).
    public struct MatchGoalEvent
    {
        public bool Valid;
        public int GoalSlot;
        public int ScoreCode;
        public int Points;
        public int ScoringSide;    // 0 home, 1 away
        public uint TickMs;
        public int HomeScore;
        public int AwayScore;
        public int RokValue;
        public bool RokCarried;
        public bool RokAtSpawnSlot;
        public int RenderBone;
        public SimVec3 RokPosition;
    }

    // Goal detection and scoring built only from the existing simulation rules:
    //   collisioncheck.cpp 0x004521d0 builds GoalFrame.Mouth (hud mouth box) - used as the entry test;
    //   CollisionCheck::BouncedInGoal 0x00469ef0 -> GoalRules.BouncedInGoal (1 north port, 2 south);
    //   PlanCond::RokScoreValue 0x00465150 -> GoalRules.RokScoreValue (1x/2x/4x/spawn code);
    //   plancond.cpp IncRokValue / goal plan arm counts -> GoalRules.PointsForScoreValue;
    //   plan data: NGoal.PBN/SGoal.PBN send MSG_NGoalScored/MSG_SGoalScored and
    //   INSCRTBL.PBN turns them into PlanAction::IncrementScore(0)/(1); PlanAction::IncrementScore
    //   (0x00464720) sends 0 to SCORE_LINE_TEAM2 and non-zero to SCORE_LINE_TEAM1, and
    //   InfoBar::IncrementScore (0x0047ae70) maps SCORE_LINE_TEAM1 to the home score and
    //   SCORE_LINE_TEAM2 to the away score. Therefore a north goal scores for the away side and a
    //   south goal for the home side, which also credits own goals correctly (the own goal's plan
    //   arm still sends that goal's score messages, see docs/NATIVE-GAMEPLAY.md).
    public sealed class MatchGoalDetector
    {
        // collisioncheck.cpp g_nGoalScoreDebounceMs = 400 (GoalConstants.ScoreDebounceMs).
        public readonly uint DebounceMs;

        public uint LastGoalTickMs = uint.MaxValue;

        public int GoalCount;

        public MatchGoalEvent LastEvent;

        public MatchGoalDetector()
        {
            DebounceMs = GoalConstants.ScoreDebounceMs;
        }

        // The world-space mouth test. GoalFrame.Mouth is in the goal's local frame (origin at the
        // mouth centre: x across the mouth, y up, z through the plane, collisioncheck.cpp), and for
        // both goals the local x is world x, local y is world z minus the mouth-centre height, and
        // the depth is the absolute distance from the goal line (the box is symmetric +-0.5).
        public static bool InsideGoalMouth(GoalFrame frame, SimVec3 point)
        {
            if (frame == null || !frame.Mouth.Valid)
            {
                return false;
            }

            float localX = point.x;
            float localY = point.z - frame.MouthCentre.z;
            float localZ = Math.Abs(point.y - frame.MouthCentre.y);

            return localX >= frame.Mouth.Min.x && localX <= frame.Mouth.Max.x
                   && localY >= frame.Mouth.Min.y && localY <= frame.Mouth.Max.y
                   && localZ >= frame.Mouth.Min.z && localZ <= frame.Mouth.Max.z;
        }

        // 1 = north port, 2 = south port, 0 = neither (the existing BouncedInGoal answer shape).
        public static int GoalPort(Simulation sim, SimVec3 rokPosition)
        {
            bool north = InsideGoalMouth(sim.Arena.NorthGoal, rokPosition);
            bool south = InsideGoalMouth(sim.Arena.SouthGoal, rokPosition);

            return GoalRules.BouncedInGoal(north, south);
        }

        // A north goal scores for the away side, a south goal for the home side. This is the score
        // line mapping of the shipped goal plans and InfoBar (see the class comment).
        public static int ScoringSideForGoal(int goalSlot)
        {
            return goalSlot == ObjectConstants.SlotSGoal ? 0 : 1;
        }

        // Runs the shipped goal rules against one ROK position. Returns Valid=false when the ROK is
        // outside both mouths or inside the 400 ms score debounce window.
        public MatchGoalEvent TryScore(Simulation sim, SimVec3 rokPosition)
        {
            MatchGoalEvent none = new MatchGoalEvent();

            int port = GoalPort(sim, rokPosition);

            if (port == 0)
            {
                return none;
            }

            int goalSlot = port == 1 ? ObjectConstants.SlotNGoal : ObjectConstants.SlotSGoal;
            uint now = sim.Rules.Clock.TickMs;

            if (LastGoalTickMs != uint.MaxValue && (uint)(now - LastGoalTickMs) < DebounceMs)
            {
                return none;
            }

            RokState rok = sim.Rok;

            int scoreCode = GoalRules.RokScoreValue(goalSlot, rok.RokValue, rok.IsCarried, rok.AtSpawnSlot);
            int points = GoalRules.PointsForScoreValue(scoreCode);
            int side = ScoringSideForGoal(goalSlot);

            // The goal plan arms send one IncrementScore message per point (1x arm once, 2x arm twice,
            // 4x arm four times - docs/CORE-NOTES.md section 7). The plan VM is not ported, so the
            // same number of exact MatchRules increments is applied here.
            for (int i = 0; i < points; i++)
            {
                if (side == 0)
                {
                    sim.Rules.IncrementHomeScore();
                }
                else
                {
                    sim.Rules.IncrementAwayScore();
                }
            }

            sim.BroadcastSceneEvent(MessageIds.GoalScored, points, goalSlot);

            MatchGoalEvent gameEvent = new MatchGoalEvent();
            gameEvent.Valid = true;
            gameEvent.GoalSlot = goalSlot;
            gameEvent.ScoreCode = scoreCode;
            gameEvent.Points = points;
            gameEvent.ScoringSide = side;
            gameEvent.TickMs = now;
            gameEvent.HomeScore = sim.Rules.ScoreHome;
            gameEvent.AwayScore = sim.Rules.ScoreAway;
            gameEvent.RokValue = rok.RokValue;
            gameEvent.RokCarried = rok.IsCarried;
            gameEvent.RokAtSpawnSlot = rok.AtSpawnSlot;
            gameEvent.RenderBone = rok.RenderBone;
            gameEvent.RokPosition = rokPosition;

            LastGoalTickMs = now;
            GoalCount++;
            LastEvent = gameEvent;

            return gameEvent;
        }

        public void ResetDebounce()
        {
            LastGoalTickMs = uint.MaxValue;
        }
    }

    // The HUD values, all from the loaded data and the exact rules: team names come from the HTM
    // headers, the scores from GameEngine::IncrementHome/AwayScore state, the clock from the
    // TickClock period state (period length from BOOIE.INI's default options, 300000 ms), and the
    // ROK value from PlanAction::IncRokValue's state. Pure data; the HUD renderer draws it.
    public sealed class MatchHudModel
    {
        public string HomeTeam = string.Empty;
        public string AwayTeam = string.Empty;
        public string HomeScoreText = "0";
        public string AwayScoreText = "0";
        public int Period;
        public string ClockText = "0:00";
        public long RemainingMs;
        public string RokText = "x0";
        public string StateLine = string.Empty;

        public static MatchHudModel From(MatchWorldData world, Simulation sim)
        {
            MatchHudModel model = new MatchHudModel();

            model.HomeTeam = (world.Home.File.Name ?? "HOME").ToUpperInvariant();
            model.AwayTeam = (world.Away.File.Name ?? "AWAY").ToUpperInvariant();
            model.HomeScoreText = Math.Max(0, sim.Rules.ScoreHome).ToString(CultureInfo.InvariantCulture);
            model.AwayScoreText = Math.Max(0, sim.Rules.ScoreAway).ToString(CultureInfo.InvariantCulture);
            model.Period = sim.Rules.Clock.Period;

            long elapsedMs = (long)sim.Rules.Clock.TickMs - (long)sim.Rules.Clock.PeriodStartMs;
            long remainingMs = (long)sim.Rules.Context.PeriodLengthMs - elapsedMs;

            if (remainingMs < 0)
            {
                remainingMs = 0;
            }

            model.RemainingMs = remainingMs;
            model.ClockText = FormatClock(remainingMs);

            int rokValue = sim.Rok.RokValue;
            model.RokText = "x" + Math.Abs(rokValue).ToString(CultureInfo.InvariantCulture);
            model.StateLine = "AI FALLBACK / INTERIM HUD FONT";

            return model;
        }

        public static string FormatClock(long remainingMs)
        {
            long totalSeconds = remainingMs / 1000;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;

            return minutes.ToString(CultureInfo.InvariantCulture) + ":"
                   + seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("home=\"").Append(HomeTeam).Append("\" away=\"").Append(AwayTeam).Append('"');
            text.Append(" scoreHome=").Append(HomeScoreText).Append(" scoreAway=").Append(AwayScoreText);
            text.Append(" period=").Append(Period.ToString(CultureInfo.InvariantCulture));
            text.Append(" clock=").Append(ClockText);
            text.Append(" rok=").Append(RokText);

            return text.ToString();
        }
    }

    // The deterministic score fixture. The plan VM and CProjectAndBounce are not ported, so the test
    // places the ROK body on a straight line into a goal mouth instead of firing a plan shot; the
    // goal detection, score value and score increment are the real shipped rules. Every moved tick
    // calls the caller's tick action (the scene's 30 Hz tick plus presentation sync, or a plain
    // MatchSimulation.Step in the headless check).
    public static class MatchScoreFixture
    {
        public const int GoalSteps = 40;

        // Places the ROK at the drome's own spawn point (the .ENV Rok definition's 0 0 0 position)
        // snapped to the arena shell, which is what the engine's first flight frame would do.
        public static void PlaceRokAtSpawn(Simulation sim)
        {
            RokState rok = sim.Rok;
            rok.ResetToSpawnState();
            rok.ActivateAtSpawnSlot(RokConstants.SpawnSlotAny);

            float floor = EllipsoidStatics.Arena.GetZ(0.0f, 0.0f);
            SimVec3 position = new SimVec3(0.0f, 0.0f, floor);

            rok.Body.Pos = position;
            rok.Body.PrevPos = position;
            rok.Body.Vel = SimVec3.Zero;
            rok.Body.SmoothVel = SimVec3.Zero;
            rok.Body.Speed = 0.0f;
            rok.Body.VelDir = new SimVec3(1.0f, 0.0f, 0.0f);
            rok.Body.VelDirLatched = rok.Body.VelDir;
            rok.Body.SurfNormal = new SimVec3(0.0f, 0.0f, 1.0f);
            rok.SetRenderSlotFlags();
        }

        public static void SetRokPosition(RokState rok, SimVec3 position)
        {
            rok.Body.PrevPos = rok.Body.Pos;
            rok.Body.Pos = position;
            rok.Body.Vel = SimVec3.Zero;
            rok.Body.SmoothVel = SimVec3.Zero;
            rok.Body.Speed = 0.0f;
        }

        // Moves the ROK from startDistance units in front of the goal through the mouth, along the
        // mouth's mid height (GoalFrame.MouthCentre.z plus the middle of the mouth box's y range),
        // because a goal is a plane at the goal line and not the arena floor at that y. The caller's
        // tick action runs before each goal test, so a scene can advance its 30 Hz tick and sync its
        // ROK view/HUD between positions. Returns the first goal event (Valid=false if none).
        public static MatchGoalEvent RunIntoGoal(Simulation sim, MatchGoalDetector detector, bool southGoal,
            int rokValue, int startDistance, int stepUnits, Action tick)
        {
            RokState rok = sim.Rok;
            rok.RokValue = (short)rokValue;
            rok.AtSpawnSlot = false;
            rok.KillState = RokConstants.InitialKillState;
            rok.SetRenderSlotFlags();

            GoalFrame frame = southGoal ? sim.Arena.SouthGoal : sim.Arena.NorthGoal;
            float mouthZ = frame.MouthCentre.z + (frame.Mouth.Min.y + frame.Mouth.Max.y) * 0.5f;

            float y = southGoal ? -startDistance : startDistance;
            SimVec3 position = new SimVec3(0.0f, y, mouthZ);
            SetRokPosition(rok, position);

            MatchGoalEvent found = new MatchGoalEvent();

            for (int step = 0; step < GoalSteps; step++)
            {
                y += southGoal ? -stepUnits : stepUnits;
                position = new SimVec3(0.0f, y, mouthZ);
                SetRokPosition(rok, position);

                if (tick != null)
                {
                    tick();
                }

                MatchGoalEvent gameEvent = detector.TryScore(sim, position);

                if (gameEvent.Valid)
                {
                    found = gameEvent;
                    break;
                }
            }

            return found;
        }

        // Keeps the ROK in the mouth for a short dwell (fewer than the 400 ms debounce) to prove a
        // single entry scores once. Returns the number of additional goals that fired (must be 0).
        public static int DwellInMouth(Simulation sim, MatchGoalDetector detector, SimVec3 position,
            int ticks, Action tick)
        {
            int extra = 0;

            for (int i = 0; i < ticks; i++)
            {
                if (tick != null)
                {
                    tick();
                }

                if (detector.TryScore(sim, position).Valid)
                {
                    extra++;
                }
            }

            return extra;
        }

        // The carry path of the existing sim (CBeAttached): attach the ROK to a carrier with an
        // offset and check the carried follow position after one tick. Returns the measured distance
        // and the attached/carried flags.
        public static bool CheckCarrierFollow(MatchSimulation sim, int carrierSlot, SimVec3 offset,
            Action tick, out float distance, out bool carried)
        {
            PlaceRokAtSpawn(sim.Sim);

            AttachPak pak = new AttachPak();
            pak.Slot = (short)carrierSlot;
            pak.Offset = offset;
            pak.LinkId = 0;

            sim.Sim.Rok.Attach(pak, null);

            // CBeAttached::Process reads the carrier's aim position at the start of the tick that
            // follows; MatchSimulation.Step updates AimPosition after the tick, so the pre-tick aim
            // is the expected position.
            SimVec3 expected = sim.Sim.Players[carrierSlot].AimPosition;

            if (tick != null)
            {
                tick();
            }

            SimVec3 actual = sim.Sim.Rok.Body.Pos;

            float dx = actual.x - (expected.x + offset.x);
            float dy = actual.y - (expected.y + offset.y);
            float dz = actual.z - (expected.z + offset.z);

            distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            carried = sim.Sim.Rok.IsCarried;

            sim.Sim.Rok.Detach();
            PlaceRokAtSpawn(sim.Sim);

            return carried && distance < 0.01f;
        }

        // The ROK render-bone mapping the presentation reads, checked against the existing
        // MultiplierRules.RenderSlotBone helper (rokobject.cpp SetRenderSlotFlags 0x00473b90).
        public static bool CheckRenderBoneMapping(Simulation sim, out string detail)
        {
            RokState rok = sim.Rok;
            bool ok = true;
            StringBuilder text = new StringBuilder();

            text.Append("plain=").Append(rok.RenderBone);

            rok.RokValue = 0;
            rok.KillState = RokConstants.InitialKillState;
            rok.AtSpawnSlot = false;
            rok.SetRenderSlotFlags();
            ok &= rok.RenderBone == RokConstants.RenderBonePlain;

            rok.RokValue = 2;
            rok.SetRenderSlotFlags();
            ok &= rok.RenderBone == RokConstants.RenderBoneDouble;
            text.Append(" double=").Append(rok.RenderBone);

            rok.RokValue = 4;
            rok.SetRenderSlotFlags();
            ok &= rok.RenderBone == RokConstants.RenderBoneQuad;
            text.Append(" quad=").Append(rok.RenderBone);

            rok.AtSpawnSlot = true;
            rok.SetRenderSlotFlags();
            ok &= rok.RenderBone == RokConstants.RenderBonePlain;

            rok.AtSpawnSlot = false;
            rok.KillState = 0;
            rok.SetRenderSlotFlags();
            ok &= rok.RenderBone == RokConstants.RenderBoneCarried;
            text.Append(" kill=").Append(rok.RenderBone);

            rok.RokValue = 0;
            rok.KillState = RokConstants.InitialKillState;
            rok.SetRenderSlotFlags();

            detail = text.ToString();

            return ok;
        }
    }
}
