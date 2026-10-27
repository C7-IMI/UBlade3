// UBlade3 - exact HyperBlade (1996) simulation core.
// Match rules: the period/pause clock and run-state machine from src/engine/gameengine.cpp
// (TickClock 0x00481130, SetRunState 0x00486de0, BeginMatchPlay 0x00481a20, RestartMatch
// 0x00486ff0, Reset 0x00487550, ResetPeriodToFirst 0x00487580). Goal and multiplier rules from
// src/planvm/plancond.cpp (RokScoreValue 0x00465150, CheckScoreOnOwnGoal 0x00468b90,
// IsDoubleScore 0x00461c00, IncRokValue 0x00463300), src/planvm/plancond.cpp 0x004521d0 and
// src/ui/infobar.cpp IncrementScore (0x0047ae70).
// Original data (BOOIE.INI period length, team ratings, plans) is supplied by the data port.

using System;
using System.Collections.Generic;

namespace UBlade3.Sim
{
    // src/engine/messageids.h, the subset the rule machines raise.
    public static class MessageIds
    {
        public const int Shoot = 0x00;
        public const int LostIt = 0x02;
        public const int GotIt = 0x03;
        public const int GoalScored = 0x07;
        public const int Jump = 0x08;
        public const int Fly = 0x09;
        public const int Landed = 0x0a;
        public const int Attach = 0x0b;
        public const int Hit = 0x0c;
        public const int SpunDown = 0x17;
        public const int NoInputForSec = 0x40;
        public const int NewInput = 0x43;
        public const int DragOver = 0x48;
        public const int DoubleScore = 0x49;
        public const int ShotOnGoal = 0x50;
        public const int StartDrag = 0x5b;
        public const int GlideToStop = 0x5e;
        public const int EndOfPeriod = 0x87;
        public const int EndOfGame = 0x88;
        public const int NoMessage = 0x8e;
    }

    // One raised scene event, in the order the original broadcasts it.
    public struct GameEvent
    {
        public int Message;
        public int Param;
        public int SenderSlot;
    }

    // The rules' view of the world; the simulation supplies the bodies behind these answers.
    // Defaults are the "nothing is injured, nobody is dead" answers the original reads from the
    // team blocks before a match is loaded.
    public sealed class MatchContext
    {
        public int PeriodLengthMs = ClockConstants.DefaultPeriodLengthMs;
        public bool DragOff;              // g_bDragOff
        public int PlayerMode;            // 0 exhibition, 1 tournament (globals.h PLAYER_MODE_*)
        public bool SimulatedSession;     // g_bSessionActive

        public Func<bool> AreAllPlayersInjured = () => false;
        public Func<bool> IsHomeTeamInjured = () => false;
        public Func<bool> IsAwayTeamInjured = () => false;
        public Func<bool> IsHomeTeamDead = () => false;
        public Func<bool> IsAwayTeamDead = () => false;

        // The original's broadcasts go through GameEngine::BroadcastEvent; the port collects them so
        // a headless run (or a later Unity presentation layer) can react or assert on them.
        public Action<GameEvent> Broadcast;

        // g_gameApp.m_nEngineState = ESTATE_JUMBOTRON; the port records the request instead of
        // owning an app state machine.
        public Action OnJumbotronRequested;

        // Tournament mode's AdvanceTournamentBracket and the pause it requests.
        public Action AdvanceTournamentBracket;
        public Action TogglePause;

        // BeginMatchPlay's per-object hooks (Rok spawn, randomizer idle, plan activation, stats).
        public Action BeginMatchPlayObjects;
        public Action RestartObjects;
        public Action ResetScoreDisplay;
    }

    // gameengine.cpp TickClock (0x00481130), the period clock half. The clock's frame average is
    // EngineClock; this class owns the run state, the period flags, scores and awards.
    public sealed class MatchRules
    {
        public const int PlayerModeExhibition = 0;
        public const int PlayerModeTournament = 1;
        public const int AwardCount = 6;
        public const int TickIntervalPauseStreakNeeded = 3; // src/planvm/plancond.cpp PAUSE_STREAK_NEEDED

        public readonly EngineClock Clock = new EngineClock();
        public readonly MatchContext Context;

        public int RunState = ClockConstants.RunStopped; // m_wRunState
        public int ScoreHome = -1;
        public int ScoreAway = -1;
        public bool MatchOver;              // m_bMatchOver
        public bool EndPeriodRequested;     // m_bEndPeriodRequested
        public bool PeriodEnded;            // m_bPeriodEnded
        public bool FollowingOther;         // m_bFollowingOther
        public bool PeriodCountdownActive;  // g_bPeriodCountdownActive
        public int CountdownLastSecs;       // g_nCountdownLastSecs
        public short ActiveScoreLine = -1;  // g_wActiveScoreLine; -1 = none
        public bool JumbotronPending;
        public bool TournamentBracketAdvancePending;
        public readonly byte[] Awards = new byte[AwardCount];
        public readonly List<GameEvent> Events = new List<GameEvent>();

        // The original's s_dwPauseBase static inside SetRunState.
        private uint s_dwPauseBase;
        // The original's s_dwEventBase static inside BroadcastEvent (0x00486a10).
        private uint s_dwEventBase;

        public MatchRules(MatchContext context)
        {
            Context = context;
            Clock.InitMatchTimers(0);
            // SetRunState's static starts at timeGetTime(); a fresh match starts unpaused.
            s_dwPauseBase = 0;
        }

        public int GetHomeScore() { return ScoreHome; }
        public int GetAwayScore() { return ScoreAway; }
        public bool IsTournamentMode() { return Context.PlayerMode == PlayerModeTournament; }
        public bool IsExhibitionMode() { return Context.PlayerMode == PlayerModeExhibition; }
        public int GetDifficultyLevel() { return 0; }

        // gameengine.cpp TickClock (0x00481130). Called once per simulation tick before the object
        // pass. Returns true when the period clock ran (the original early-returns most of the time).
        public bool TickClock(uint nowMs, bool hostFrame = false, bool fixedTick = false)
        {
            if (fixedTick)
            {
                Clock.AdvanceFixedTick(nowMs);
            }
            else if (hostFrame)
            {
                Clock.AdvanceHostFrame(nowMs);
            }

            int overtimeMs;
            uint elapsedMs;

            if (Clock.Period >= ClockConstants.FinalPeriodIndex)
            {
                overtimeMs = ClockConstants.OvertimeFinalMs;
            }
            else
            {
                overtimeMs = ClockConstants.OvertimeEarlyMs;
            }

            if (Clock.PauseMark != 1)
            {
                if ((uint)(Clock.TickMs - Clock.PeriodStartMs) < (uint)Context.PeriodLengthMs
                    && !EndPeriodRequested
                    && !Context.AreAllPlayersInjured()
                    && !Context.IsHomeTeamInjured()
                    && !Context.IsAwayTeamInjured())
                {
                    return false;
                }
            }

            if (Clock.PauseMark == 1)
            {
                Clock.PauseStartMs = Clock.TickMs;
                elapsedMs = Clock.TickMs - Clock.PeriodStartMs;
            }
            else
            {
                elapsedMs = Clock.PauseStartMs - Clock.PeriodStartMs;
            }

            if (!PeriodCountdownActive)
            {
                if ((uint)Context.PeriodLengthMs - elapsedMs <= ClockConstants.CountdownStartMs)
                {
                    PeriodCountdownActive = true;
                    CountdownLastSecs = ClockConstants.CountdownSecs;
                }
            }

            if (PeriodCountdownActive)
            {
                int secs = (int)((uint)Context.PeriodLengthMs - elapsedMs) / 1000;
                if (secs < CountdownLastSecs)
                {
                    CountdownLastSecs = secs;
                }
            }

            if (elapsedMs < (uint)Context.PeriodLengthMs || Context.DragOff)
            {
                if (Clock.PauseMark == 0 && (Context.IsHomeTeamInjured() || Context.IsAwayTeamInjured()))
                {
                    EndPeriodRequested = true;
                }

                if (Context.AreAllPlayersInjured() || EndPeriodRequested)
                {
                    Clock.PeriodStartMs = Clock.PauseStartMs - (uint)Context.PeriodLengthMs;

                    if (Context.IsHomeTeamDead() || Context.IsAwayTeamDead())
                    {
                        Clock.Period = ClockConstants.FinalPeriodIndex;
                    }

                    Clock.PauseMark = 1;
                }
            }
            else if (!PeriodEnded)
            {
                if (Clock.Period == ClockConstants.FinalPeriodIndex)
                {
                    Broadcast(MessageIds.EndOfGame, 0, ObjectConstants.SlotGod);
                }
                else
                {
                    Broadcast(MessageIds.EndOfPeriod, 0, ObjectConstants.SlotGod);
                }

                if (Context.IsHomeTeamDead())
                {
                    ActiveScoreLine = 1;
                }
                else if (Context.IsAwayTeamDead())
                {
                    ActiveScoreLine = 0;
                }
                else
                {
                    ActiveScoreLine = (short)(GetHomeScore() <= GetAwayScore() ? 1 : 0);
                }

                Clock.PauseMark = 0;
                PeriodEnded = true;
            }

            if (IsExhibitionMode()
                && (uint)(Clock.TickMs - Clock.PeriodStartMs) >= (uint)(Context.PeriodLengthMs + overtimeMs)
                && elapsedMs >= (uint)Context.PeriodLengthMs
                && !Context.DragOff)
            {
                Clock.Period++;

                if (Clock.Period == ClockConstants.PeriodCount)
                {
                    MatchOver = true;
                }

                JumbotronPending = true;
                if (Context.OnJumbotronRequested != null)
                {
                    Context.OnJumbotronRequested();
                }
            }

            if (IsTournamentMode()
                && (uint)(Clock.TickMs - Clock.PeriodStartMs) >= (uint)(Context.PeriodLengthMs + overtimeMs)
                && elapsedMs >= (uint)Context.PeriodLengthMs
                && !Context.DragOff)
            {
                Clock.Period++;

                if (Clock.Period == ClockConstants.PeriodCount)
                {
                    TournamentBracketAdvancePending = true;
                    if (Context.AdvanceTournamentBracket != null)
                    {
                        Context.AdvanceTournamentBracket();
                    }
                }

                JumbotronPending = true;
                if (Context.OnJumbotronRequested != null)
                {
                    Context.OnJumbotronRequested();
                }
                if (Context.TogglePause != null)
                {
                    Context.TogglePause();
                }
            }

            return true;
        }

        // gameengine.cpp SetRunState (0x00486de0). bRunning 1 resumes, 0 pauses. The original also
        // raises/lowers the process priority and screensaver; those side effects are not simulation.
        public void SetRunState(int bRunning, uint nowMs)
        {
            switch (bRunning)
            {
                case 1:
                    if (Clock.PauseMark == ClockConstants.PauseMarkNone)
                    {
                        Clock.PauseMark = 1;
                        Clock.PauseStartMs = nowMs;
                        Clock.PeriodStartMs += Clock.PauseStartMs - s_dwPauseBase;
                    }

                    RunState = ClockConstants.RunPlaying;
                    break;

                case 0:
                    s_dwPauseBase = nowMs;
                    RunState = ClockConstants.RunPaused;

                    if (Clock.PauseMark == 1)
                    {
                        Clock.PauseMark = ClockConstants.PauseMarkNone;
                    }
                    break;
            }
        }

        // gameengine.cpp GameEngine::Reset (0x00487550): both scores to -1 (no score yet).
        public void Reset()
        {
            ScoreHome = -1;
            ScoreAway = -1;
            if (Context.ResetScoreDisplay != null)
            {
                Context.ResetScoreDisplay();
            }
        }

        // gameengine.cpp ResetPeriodToFirst (0x00487580).
        public void ResetPeriodToFirst()
        {
            Clock.Period = ClockConstants.FirstPeriod;
        }

        // gameengine.cpp BeginMatchPlay (0x00481a20): reset the period flags, clamp the clock's
        // start and restore damage to full weight. The object half is delegated to the context.
        public void BeginMatchPlay()
        {
            EndPeriodRequested = false;
            PeriodEnded = false;
            FollowingOther = false;
            PeriodCountdownActive = false;

            if (Context.BeginMatchPlayObjects != null)
            {
                Context.BeginMatchPlayObjects();
            }

            Clock.PeriodStartMs = Clock.TickMs;
        }

        // gameengine.cpp RestartMatch (0x00486ff0).
        public void RestartMatch()
        {
            Events.Clear();

            if (Context.RestartObjects != null)
            {
                Context.RestartObjects();
            }

            if (Clock.Period == ClockConstants.FirstPeriod)
            {
                Reset();
                ActiveScoreLine = -1;
            }

            BeginMatchPlay();
            SceneEvent(MessageIds.StartDrag, 0, ObjectConstants.SlotGod, Clock.TickMs);
        }

        // gameengine.cpp GameEngine::IncrementHomeScore / IncrementAwayScore / InfoBar::IncrementScore.
        public int IncrementHomeScore()
        {
            ScoreHome++;
            ActiveScoreLine = 0;
            return ScoreHome;
        }

        public int IncrementAwayScore()
        {
            ScoreAway++;
            ActiveScoreLine = 1;
            return ScoreAway;
        }

        public void SetHomeScore(int score) { ScoreHome = score; }
        public void SetAwayScore(int score) { ScoreAway = score; }

        public bool HasAward(int index) { return index >= 0 && index < Awards.Length && Awards[index] != 0; }
        public void SetAward(int index, bool value)
        {
            if (index >= 0 && index < Awards.Length)
            {
                Awards[index] = value ? (byte)1 : (byte)0;
            }
        }

        public void ClearAwards()
        {
            Array.Clear(Awards, 0, Awards.Length);
        }

        // The engine's own broadcasts, collected for the caller and test harness. This is the
        // delivery half only; the clock effects live in SceneEvent below.
        public void Broadcast(int message, int param, int senderSlot)
        {
            GameEvent gameEvent = new GameEvent { Message = message, Param = param, SenderSlot = senderSlot };
            Events.Add(gameEvent);
            if (Context.Broadcast != null)
            {
                Context.Broadcast(gameEvent);
            }
        }

        // gameengine.cpp GameEngine::BroadcastEvent (0x00486a10): a goal stops the clock, a shot
        // restarts it and a start drag zeroes it. The original also skips the sender and the drag
        // suppression; object-level delivery is a presentation concern in this slice.
        public void SceneEvent(int message, int param, int senderSlot, uint nowMs)
        {
            Broadcast(message, param, senderSlot);

            if (message == MessageIds.GoalScored)
            {
                s_dwEventBase = nowMs;
                Clock.PauseMark = 0;
            }
            else if (message == MessageIds.StartDrag)
            {
                s_dwEventBase = nowMs;
                Clock.PeriodStartMs = nowMs;
                Clock.PauseStartMs = nowMs;
                Clock.PauseMark = 0;
            }
            else if (message == MessageIds.Shoot)
            {
                if (Clock.PauseMark == 0)
                {
                    Clock.PauseStartMs = nowMs;
                    Clock.PeriodStartMs += Clock.PauseStartMs - s_dwEventBase;
                }

                Clock.PauseMark = 1;
            }
        }
    }

    // src/planvm/plancond.cpp RokScoreValue (0x00465150): the goal clamps the Rok value to one,
    // the north goal negates it; the result code selects the goal plan's score branch.
    public static class GoalRules
    {
        // The result codes RokScoreValue writes into pResult.
        public static int RokScoreValue(int goalSlot, int rokValue, bool rokAttached, bool rokAtSpawnSlot)
        {
            int clamped;

            int value = rokValue;

            if (goalSlot == ObjectConstants.SlotSGoal)
            {
                if (value < 1)
                {
                    value = 1;
                }
            }
            else if (goalSlot == ObjectConstants.SlotNGoal)
            {
                clamped = Math.Min(-1, value);
                value = -clamped;
            }
            else
            {
                value = 0;
            }

            int result = RokConstants.ScoreValueResult1x;

            // The original writes the carrier flag first and then overwrites it with the value code,
            // so only the spawn-slot flag survives; the statement order is kept.
            if (rokAttached)
            {
                result = RokConstants.ScoreValueResultCarried;
            }

            if (value == 1)
            {
                result = RokConstants.ScoreValueResult1x;
            }
            else if (value == 2)
            {
                result = RokConstants.ScoreValueResult2x;
            }
            else if (value == 4)
            {
                result = RokConstants.ScoreValueResult4x;
            }
            else
            {
                result = RokConstants.ScoreValueResult1x;
            }

            if (rokAtSpawnSlot)
            {
                result = RokConstants.ScoreValueResultSpawn;
            }

            return result;
        }

        // src/planvm/plancond.cpp IsDoubleScore (0x00461c00): a carried Rok answers two; a skater
        // whose target goal slot is zero answers one. (Used by the AI, kept for plan parity.)
        public static int IsDoubleScore(bool rokAttached, int targetGoalSlotOfActor)
        {
            if (rokAttached)
            {
                return 2;
            }

            return targetGoalSlotOfActor == 0 ? 1 : 0;
        }

        // src/planvm/plancond.cpp CheckScoreOnOwnGoal (0x00468b90): true means "score on your own
        // goal". Only carrier slots 0..3 are inspected; anything else answers yes.
        public static int CheckScoreOnOwnGoal(int actorSlot, int carrierSlot, int carrierTargetGoalSlot)
        {
            if (carrierSlot >= 0 && carrierSlot <= 3)
            {
                return carrierTargetGoalSlot != actorSlot ? 1 : 0;
            }

            return 1;
        }

        // src/physics/collisioncheck.cpp BouncedInGoal (0x00469ef0): 1 north port, 2 south port, 0 neither.
        public static int BouncedInGoal(bool intersectsNorthPort, bool intersectsSouthPort)
        {
            if (intersectsNorthPort)
            {
                return 1;
            }

            if (intersectsSouthPort)
            {
                return 2;
            }

            return 0;
        }

        // The score code the goal frame test (collisioncheck.cpp 0x004521d0) writes: 1 post hit,
        // 2 through the mouth, 3 goal inside the 400 ms debounce. The plan decides what a goal is
        // worth; this helper only reproduces the frame test's answers.
        public const int FrameTestPostHit = 1;
        public const int FrameTestMouth = 2;
        public const int FrameTestDebounced = 3;

        // The point values the shipped goal plans apply for RokScoreValue's codes, as measured by
        // 20XX docs/sim_results.md (1x/2x/4x goals). The C++ only exposes one point per
        // PlanAction::IncrementScore call; the plan VM port must confirm the call counts.
        // TODO(U3-M3): replace with the plan VM's own score actions once src/planvm is ported.
        public static int PointsForScoreValue(int scoreValue)
        {
            switch (scoreValue)
            {
                case RokConstants.ScoreValueResult2x: return 2;
                case RokConstants.ScoreValueResult4x: return 4;
                default: return 1;
            }
        }
    }

    // src/planvm/plancond.cpp PlanAction::IncRokValue (0x00463300) and GameEngine::ShowRokValue
    // (0x00463220). The Rok value is team relative: positive locks the multiplier for one side.
    public static class MultiplierRules
    {
        public const int ColourPositive = 144; // g_nMultiplierColourPositive
        public const int ColourNegative = 160; // g_nMultiplierColourNegative

        public struct StepResult
        {
            public int Value;
            public bool RaisedEvent;
            public int EventParam;
            public int Material1Colour;
            public int Material2Colour;
        }

        // request: 0 clears, 7 raises by two, 8 lowers by two (ARENA_TRACK_UP/DOWN); any other
        // value leaves the Rok value alone.
        public static StepResult IncRokValue(int current, int request, bool restColoursSaved,
            int restColour1, int restColour2)
        {
            StepResult result = new StepResult();
            int mode = current;

            if (mode == 1)
            {
                mode = 0;
            }

            if (request == RokConstants.ArenaTrackUp)
            {
                mode += RokConstants.RokValueStep;

                if (mode <= RokConstants.RokValueMax)
                {
                    result.RaisedEvent = true;
                    result.EventParam = 0;
                }

                if (mode >= RokConstants.RokValueMax)
                {
                    mode = RokConstants.RokValueMax;
                }
            }
            else if (request == RokConstants.ArenaTrackDown)
            {
                mode -= RokConstants.RokValueStep;

                if (mode >= RokConstants.RokValueMin)
                {
                    result.RaisedEvent = true;
                    result.EventParam = 1;
                }

                if (mode <= RokConstants.RokValueMin)
                {
                    mode = RokConstants.RokValueMin;
                }
            }
            else if (request == 0)
            {
                mode = 0;
            }

            result.Value = mode;

            // GameEngine::ShowRokValue's material colouring, for the presentation layer.
            int material1 = restColour1;
            int material2 = restColour2;

            switch (mode)
            {
                case -4:
                    material1 = ColourNegative;
                    material2 = ColourNegative;
                    break;
                case -2:
                    material1 = restColour1;
                    material2 = ColourNegative;
                    break;
                case 2:
                    material1 = restColour1;
                    material2 = ColourPositive;
                    break;
                case 4:
                    material1 = ColourPositive;
                    material2 = ColourPositive;
                    break;
                default:
                    material1 = restColour1;
                    material2 = restColour2;
                    break;
            }

            result.Material1Colour = material1;
            result.Material2Colour = material2;
            return result;
        }

        // rokobject.cpp SetRenderSlotFlags (0x00473b90): the bone shown for the Rok's current state.
        public static int RenderSlotBone(int rokValue, bool atSpawnSlot, int killState)
        {
            if (atSpawnSlot)
            {
                return RokConstants.RenderBonePlain;
            }

            if (killState == RokConstants.InitialKillState)
            {
                switch (Math.Abs(rokValue))
                {
                    case 0:
                    case 1:
                        return RokConstants.RenderBonePlain;
                    case 2:
                        return RokConstants.RenderBoneDouble;
                    case 4:
                        return RokConstants.RenderBoneQuad;
                    default:
                        return -1; // every bone stays hidden
                }
            }

            return RokConstants.RenderBoneCarried;
        }
    }
}
