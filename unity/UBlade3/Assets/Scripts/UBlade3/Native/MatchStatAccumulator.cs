using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.PlanVM;
using UBlade3.Sim;

namespace UBlade3.Native
{
    // N16: the match-side MatchStats accumulator. PlanAction::IncrementStat (plancond.cpp
    // 0x00461d20) only acts on the pronoun form: it calls target.m_matchStats.AccumulateStat(statId,
    // sourceSlot). The PlanVM mock records the target slot and stat id in PlanAiActionState
    // (LastStatSlot/LastStatId/StatEvents); this class applies the exact MatchStats::AccumulateStat
    // switch (engine/matchstats.cpp 0x004918d0) to a per-player 49-float table.
    //
    // Values are not fabricated: only action-9 executions observed in the live plan state are
    // accumulated. The current PlanVM telemetry carries one target/stat pair per tick, so several
    // different stat ids in the same tick cannot be separated; that limitation is logged and
    // documented rather than guessed. The plan VM has no ported match-stat read path yet, so this
    // API is exposed to the match/plan context by MatchStatAccumulator.GetStat for future callers.
    public sealed class MatchStatAccumulator
    {
        public const string Marker = "UBLADE3_NATIVE_MATCH_STAT";

        public const int StatCount = 0x31;

        public const int PlayerSlots = ObjectConstants.PlayerObjectCount;

        // engine/statids.h: MATCH_STAT_* ids used by the switch.
        private const int StatGoals = 0x00;
        private const int StatShots = 0x01;
        private const int StatAssists = 0x02;
        private const int StatShooting = 0x03;
        private const int StatPassing = 0x04;
        private const int StatPassesAttempted = 0x05;
        private const int StatPassesCompleted = 0x06;
        private const int StatTimeOfPossession = 0x07;
        private const int StatTopStampStart = 0x08;
        private const int StatTopStampEnd = 0x09;
        private const int Stat2PointScores = 0x0a;
        private const int Stat2PointCompleted = 0x0c;
        private const int StatShortHandedAttempts = 0x0e;
        private const int StatShortHandedCompleted = 0x0f;
        private const int StatPowerPlayAttempts = 0x11;
        private const int StatPowerPlayCompleted = 0x12;
        private const int StatTimeOnRail = 0x17;
        private const int StatTorStampStart = 0x18;
        private const int StatTorStampEnd = 0x19;
        private const int StatHangTime = 0x1b;
        private const int StatHtStampStart = 0x1c;
        private const int StatHtStampEnd = 0x1d;
        private const int StatOneTimers = 0x1e;
        private const int StatPowerPlayTime = 0x20;
        private const int StatPptStampStart = 0x21;
        private const int StatPptStampEnd = 0x22;
        private const int Stat1XGoal = 0x23;
        private const int StatTotalGoals = 0x24;
        private const int StatPossStart = 0x25;
        private const int StatPossStop = 0x26;
        private const int StatTotalPoints = 0x27;
        private const int StatShotsOnGoal = 0x28;
        private const int StatShootingPct = 0x29;
        private const int Stat2XGoal = 0x2a;
        private const int Stat4XGoal = 0x2b;
        private const int StatTimeOfPoss = 0x2c;
        private const int StatRandomizers = 0x2d;
        private const int StatKillballs = 0x2e;
        private const int StatInjuries = 0x2f;
        private const int StatFatalities = 0x30;

        private static bool statIntervalOpen;

        private readonly float[,] stats = new float[PlayerSlots, StatCount];

        private readonly int[] lastEvents = new int[PlayerSlots];

        private readonly bool[] observed = new bool[PlayerSlots];

        public int AppliedEvents;

        public int RejectedEvents;

        public int MultiplePerTick;

        public int LastSlot = -1;

        public int LastStatId = -1;

        public float LastValue;

        public readonly List<string> Trace = new List<string>();

        public float GetStat(int slot, int statId)
        {
            if (slot < 0 || slot >= PlayerSlots || statId < 0 || statId >= StatCount)
            {
                return float.NaN;
            }

            return stats[slot, statId];
        }

        public float[] GetStats(int slot, float[] destination = null)
        {
            if (slot < 0 || slot >= PlayerSlots)
            {
                return null;
            }

            float[] result = destination ?? new float[StatCount];

            for (int i = 0; i < StatCount; i++)
            {
                result[i] = stats[slot, i];
            }

            return result;
        }

        public void Tick(MatchPlanAiRoster roster, Simulation sim)
        {
            if (roster == null)
            {
                return;
            }

            for (int index = 0; index < roster.PlanSlots.Count; index++)
            {
                int actorSlot = roster.PlanSlots[index];
                MatchPlanAi plan = roster.Adapter(actorSlot);

                if (plan == null)
                {
                    continue;
                }

                PlanAiActionState effects = plan.AnimationActionState;

                if (effects == null || actorSlot < 0 || actorSlot >= PlayerSlots)
                {
                    continue;
                }

                if (!observed[actorSlot])
                {
                    observed[actorSlot] = true;
                    lastEvents[actorSlot] = effects.StatEvents;
                    continue;
                }

                int delta = effects.StatEvents - lastEvents[actorSlot];

                if (delta <= 0)
                {
                    continue;
                }

                lastEvents[actorSlot] = effects.StatEvents;

                if (effects.LastStatSlot < 0 || effects.LastStatSlot >= PlayerSlots
                    || effects.LastStatId < 0 || effects.LastStatId >= StatCount)
                {
                    RejectedEvents += delta;
                    Trace.Add("reject actor=" + actorSlot + " target=" + effects.LastStatSlot
                              + " stat=" + effects.LastStatId + " count=" + delta);
                    continue;
                }

                if (delta > 1)
                {
                    MultiplePerTick += delta - 1;
                    Trace.Add("multiple actor=" + actorSlot + " target=" + effects.LastStatSlot
                              + " stat=" + effects.LastStatId + " count=" + delta
                              + " (last pair only; PlanVM telemetry limitation)");
                }

                for (int i = 0; i < delta; i++)
                {
                    Accumulate(effects.LastStatSlot, effects.LastStatId, sim == null
                        ? 0 : sim.Rules.Clock.TickMs);
                }

                LastSlot = effects.LastStatSlot;
                LastStatId = effects.LastStatId;
                LastValue = GetStat(effects.LastStatSlot, effects.LastStatId);
                Trace.Add("stat actor=" + actorSlot + " target=" + LastSlot + " id=" + LastStatId
                          + " value=" + LastValue.ToString("0.###", CultureInfo.InvariantCulture)
                          + " count=" + delta);
            }
        }

        // Exact MatchStats::AccumulateStat switch. The nSlot parameter is unused in the original
        // switch body; the plan's source slot is not part of the match stats table.
        public float Accumulate(int slot, int statId, double nowMs)
        {
            if (slot < 0 || slot >= PlayerSlots || statId < 0 || statId >= StatCount)
            {
                RejectedEvents++;
                return float.NaN;
            }

            AppliedEvents++;

            switch (statId)
            {
                case Stat1XGoal:
                    stats[slot, Stat1XGoal]++;
                    stats[slot, StatTotalGoals]++;
                    stats[slot, StatTotalPoints]++;
                    stats[slot, StatShootingPct] =
                        (stats[slot, StatTotalGoals] / stats[slot, StatShotsOnGoal]) * 100.0f;
                    break;

                case Stat2XGoal:
                    stats[slot, Stat2XGoal]++;
                    stats[slot, StatTotalGoals]++;
                    stats[slot, StatTotalPoints] += 2.0f;
                    stats[slot, StatShootingPct] =
                        (stats[slot, StatTotalGoals] / stats[slot, StatShotsOnGoal]) * 100.0f;
                    break;

                case Stat4XGoal:
                    stats[slot, Stat4XGoal]++;
                    stats[slot, StatTotalGoals]++;
                    stats[slot, StatTotalPoints] += 4.0f;
                    stats[slot, StatShootingPct] =
                        (stats[slot, StatTotalGoals] / stats[slot, StatShotsOnGoal]) * 100.0f;
                    break;

                case StatShotsOnGoal:
                    stats[slot, StatShotsOnGoal]++;
                    stats[slot, StatShootingPct] =
                        (stats[slot, StatTotalGoals] / stats[slot, StatShotsOnGoal]) * 100.0f;
                    break;

                case StatPossStart:
                    stats[slot, StatPossStart] = (float)nowMs;
                    statIntervalOpen = true;
                    break;

                case StatPossStop:
                    if (statIntervalOpen)
                    {
                        float now = (float)nowMs;
                        stats[slot, StatPossStop] = now;
                        stats[slot, StatTimeOfPoss] =
                            (now - stats[slot, StatPossStart]) + stats[slot, StatTimeOfPoss];
                        statIntervalOpen = false;
                    }
                    break;

                case StatRandomizers:
                    stats[slot, StatRandomizers]++;
                    break;

                case StatKillballs:
                    stats[slot, StatKillballs]++;
                    break;

                case StatInjuries:
                    stats[slot, StatInjuries]++;
                    break;

                case StatFatalities:
                    stats[slot, StatFatalities]++;
                    break;

                case StatAssists:
                    if ((float)nowMs - 15000.0f < stats[slot, StatTopStampEnd])
                    {
                        stats[slot, StatAssists]++;
                    }

                    if ((float)nowMs - 1000.0f < stats[slot, StatTopStampEnd])
                    {
                        stats[slot, StatOneTimers]++;
                    }
                    break;

                case StatGoals:
                    stats[slot, StatGoals]++;
                    stats[slot, StatShooting] =
                        Percent(slot, StatGoals, StatShots);
                    break;

                case StatPassesCompleted:
                    stats[slot, StatPassesCompleted]++;
                    stats[slot, StatPassing] =
                        Percent(slot, StatPassesCompleted, StatPassesAttempted);
                    break;

                case Stat2PointCompleted:
                    stats[slot, Stat2PointCompleted]++;
                    stats[slot, Stat2PointScores] =
                        Percent(slot, StatPassesCompleted, StatPassesAttempted);
                    break;

                case StatShortHandedCompleted:
                    stats[slot, StatShortHandedCompleted]++;
                    stats[slot, Stat2PointScores] =
                        Percent(slot, StatShortHandedCompleted, StatShortHandedAttempts);
                    break;

                case StatPowerPlayCompleted:
                    stats[slot, StatPowerPlayCompleted]++;
                    stats[slot, Stat2PointScores] =
                        Percent(slot, StatPowerPlayCompleted, StatPowerPlayAttempts);
                    break;

                default:
                    stats[slot, statId]++;
                    break;
            }

            return stats[slot, statId];
        }

        private float Percent(int slot, int part, int whole)
        {
            return (stats[slot, part] / stats[slot, whole]) * 100.0f;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append("_STATE applied=").Append(AppliedEvents)
                .Append(" rejected=").Append(RejectedEvents)
                .Append(" multiplePerTick=").Append(MultiplePerTick)
                .Append(" lastSlot=").Append(LastSlot)
                .Append(" lastStat=").Append(LastStatId)
                .Append(" lastValue=").Append(LastValue.ToString("0.###",
                    CultureInfo.InvariantCulture));

            for (int slot = 0; slot < PlayerSlots; slot++)
            {
                int nonzero = 0;

                for (int stat = 0; stat < StatCount; stat++)
                {
                    if (stats[slot, stat] != 0.0f)
                    {
                        nonzero++;
                    }
                }

                if (nonzero > 0)
                {
                    text.Append(" [slot=").Append(slot).Append(" nonzero=").Append(nonzero);

                    for (int stat = 0; stat < StatCount; stat++)
                    {
                        if (stats[slot, stat] != 0.0f)
                        {
                            text.Append(' ').Append(stat).Append('=')
                                .Append(stats[slot, stat].ToString("0.###",
                                    CultureInfo.InvariantCulture));
                        }
                    }

                    text.Append(']');
                }
            }

            return text.ToString();
        }

        public string TraceText(int max)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < Trace.Count && i < max; i++)
            {
                text.Append(Marker).Append("_TRACE ").AppendLine(Trace[i]);
            }

            return text.ToString();
        }
    }
}
