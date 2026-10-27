// UBlade3 - exact HyperBlade (1996) simulation core.
// EngineClock: src/engine/engineclock.h (0x4c-byte record) and the clock half of
// src/engine/gameengine.cpp GameEngine::TickClock (0x00481130) / InitMatchTimers (0x00481d60).
// The fixed 30 Hz step is third_party/hyperblade-20XX src/engine/framepacing.h kTickMs: the port
// feeds TickClock a constant 1000/30 ms frame delta, so the original five-slot ring averages to
// exactly 1/30 s. The decomp itself smooths the real host frame time; both paths are kept.

using System;

namespace UBlade3.Sim
{
    // src/engine/engineclock.h. Field order and meanings follow the original record; the nTickMs
    // stamps are 32-bit milliseconds and wrap after 49 days, exactly as timeGetTime did.
    public sealed class EngineClock
    {
        public float InvDelta;                  // flInvDelta, 0x00
        public float PrevInvDelta;              // flPrevInvDelta, 0x04
        public float InvDeltaSquared;           // flInvDeltaSquared, 0x08
        public float PrevInvDeltaSquared;       // flPrevInvDeltaSquared, 0x0c
        public float Delta;                     // flDelta, 0x10
        public float PrevDelta;                 // flPrevDelta, 0x14
        public float DeltaSquared;              // flDeltaSquared, 0x18
        public float PrevDeltaSquared;          // flPrevDeltaSquared, 0x1c
        public readonly float[] DeltaRing = new float[ClockConstants.RingLength]; // aflDeltaRing, 0x20
        public uint TickMs;                     // nTickMs, 0x34
        public uint PrevTickMs;                 // nPrevTickMs, 0x38
        public short RingIndex;                 // nRingIndex, 0x3c
        public short Period;                    // nPeriod, 0x3e
        public uint PeriodStartMs;              // nPeriodStartMs, 0x40
        public uint PauseStartMs;               // nPauseStartMs, 0x44
        public int PauseMark;                   // nPauseMark, 0x48

        // The raw host delta before the fifth-of-a-frame weight, kept for tools and tests.
        public float LastHostFrameDeltaMs;
        // Set 1 when TickClock rejected a host delta (over 200 ms or negative) and left the average alone.
        public bool LastHostFrameRejected;

        public EngineClock()
        {
            Clear();
        }

        public void Clear()
        {
            InvDelta = 0.0f;
            PrevInvDelta = 0.0f;
            InvDeltaSquared = 0.0f;
            PrevInvDeltaSquared = 0.0f;
            Delta = 0.0f;
            PrevDelta = 0.0f;
            DeltaSquared = 0.0f;
            PrevDeltaSquared = 0.0f;
            Array.Clear(DeltaRing, 0, DeltaRing.Length);
            TickMs = 0;
            PrevTickMs = 0;
            RingIndex = 0;
            Period = 0;
            PeriodStartMs = 0;
            PauseStartMs = 0;
            PauseMark = 0;
            LastHostFrameDeltaMs = 0.0f;
            LastHostFrameRejected = false;
        }

        public float GetInvDelta() { return InvDelta; }
        public float GetDelta() { return Delta; }
        public float GetDeltaSquared() { return DeltaSquared; }

        // gameengine.cpp GameEngine::InitMatchTimers (0x00481d60). The ring is seeded with a fifth of
        // MATCH_STEP; the period starts at one and the pause mark at minus one.
        public void InitMatchTimers(uint nowMs)
        {
            PrevInvDelta = ClockConstants.MatchFrameRate;
            InvDelta = ClockConstants.MatchFrameRate;

            TickMs = 0;
            PrevTickMs = 0;

            PrevInvDeltaSquared = ClockConstants.MatchRateSquared;
            InvDeltaSquared = ClockConstants.MatchRateSquared;

            float step = 1.0f * ClockConstants.MatchStep;

            PrevDelta = step;
            Delta = step;
            PrevDeltaSquared = step * step;
            DeltaSquared = step * step;

            for (int slot = 0; slot < ClockConstants.RingLength; slot++)
            {
                DeltaRing[slot] = ClockConstants.Fifth * Delta;
            }

            RingIndex = 0;
            Period = ClockConstants.FirstPeriod;

            PeriodStartMs = nowMs;
            PauseStartMs = nowMs;
            PauseMark = ClockConstants.PauseMarkNone;
        }

        // gameengine.cpp TickClock, the host-frame path: the delta is the real time since the last
        // tick, clamped to [0, 200] ms, then weighted by a fifth of a second.
        public void AdvanceHostFrame(uint nowMs)
        {
            PrevDelta = Delta;
            PrevDeltaSquared = DeltaSquared;
            PrevInvDelta = InvDelta;
            PrevInvDeltaSquared = InvDeltaSquared;
            PrevTickMs = TickMs;

            TickMs = nowMs;
            float frameDeltaMs = (float)((double)nowMs - (double)PrevTickMs);
            LastHostFrameDeltaMs = frameDeltaMs;
            LastHostFrameRejected = false;

            if (frameDeltaMs > ClockConstants.MaxFrameTimeMs || frameDeltaMs < 0.0f)
            {
                LastHostFrameRejected = true;
                return;
            }

            ApplyWeightedDelta(ClockConstants.Fifth * frameDeltaMs * ClockConstants.MsToSec);
        }

        // 20XX TickClock, the fixed-tick path: every simulation tick assumes a host frame of exactly
        // kTickMs, so the five-ring average is 0.2 * (1000/30) * 0.001 * 5 = 1/30 s. nowMs is the
        // 20XX simulated clock stamp (fractional kTickMs accumulated and truncated).
        public void AdvanceFixedTick(uint nowMs)
        {
            PrevDelta = Delta;
            PrevDeltaSquared = DeltaSquared;
            PrevInvDelta = InvDelta;
            PrevInvDeltaSquared = InvDeltaSquared;
            PrevTickMs = TickMs;

            TickMs = nowMs;
            LastHostFrameDeltaMs = ClockConstants.FixedTickMs;
            LastHostFrameRejected = false;

            ApplyWeightedDelta(ClockConstants.Fifth * ClockConstants.FixedTickMs * ClockConstants.MsToSec);
        }

        // TickClock's ring average and reciprocal update.
        private void ApplyWeightedDelta(float weightedDelta)
        {
            short slot = RingIndex;
            Delta = Delta - DeltaRing[slot] + weightedDelta;
            DeltaSquared = Delta * Delta;

            InvDelta = 1.0f / Delta;
            InvDeltaSquared = InvDelta * InvDelta;

            DeltaRing[slot] = weightedDelta;
            RingIndex = (short)((short)(RingIndex + 1) % ClockConstants.RingLength);
        }

        // --- Pause/period helpers, from gameengine.cpp TickClock and SetRunState (0x00486de0). ---

        // TickClock: the pause mark is -1 while the period clock is parked, 1 while a pause is being
        // folded into the period start, and 0 otherwise.
        public void MarkPaused(uint nowMs)
        {
            if (PauseMark == ClockConstants.PauseMarkNone)
            {
                PauseMark = 1;
                PauseStartMs = nowMs;
            }
        }

        public bool IsMatchClockRunning()
        {
            return PauseMark != ClockConstants.PauseMarkNone;
        }

        // { tick - periodStart } as the unsigned 32-bit difference the original uses.
        public uint ElapsedMs()
        {
            return TickMs - PeriodStartMs;
        }

        // { pauseStart - periodStart } as the unsigned 32-bit difference the original uses.
        public uint PausedElapsedMs()
        {
            return PauseStartMs - PeriodStartMs;
        }
    }

    // 20XX src/app/simulator.cpp PlayMatch: the batch clock accumulates a fractional kTickMs and
    // truncates it into the simulated millisecond stamp the engine reads.
    public sealed class FixedStepClock
    {
        public double ElapsedMs;
        public readonly EngineClock Clock = new EngineClock();

        public void InitMatchTimers()
        {
            ElapsedMs = 0.0;
            Clock.InitMatchTimers(0);
        }

        // One 30 Hz simulation tick. Returns the millisecond stamp used for the tick.
        public uint Advance()
        {
            ElapsedMs += ClockConstants.FixedTickMs;
            uint nowMs = (uint)ElapsedMs;
            Clock.AdvanceFixedTick(nowMs);
            return nowMs;
        }
    }
}
