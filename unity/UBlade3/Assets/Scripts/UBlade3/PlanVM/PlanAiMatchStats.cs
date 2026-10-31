using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N17 plan-context MatchStats read/write seam. The original MatchStats table is engine-side
    /// (engine/matchstats.h) and the plan actions that touch it are PlanAction::IncrementStat,
    /// StartStatTimer and EndStatTimer; the decomp has no PlanCond/PlanAction body that calls
    /// MatchStats::GetAt (the only GetAt caller is RokObject::Postpass). This seam exposes the
    /// (in-progress, documented) match-stat values to a plan handler, and records the read so the
    /// deterministic probe can prove a decision-path read of a real accumulated value.
    /// </summary>
    public static class PlanAiMatchStats
    {
        public static Func<int, int, float> Reader;

        public static int Reads;

        public static int LastSlot = -1;

        public static int LastStatId = -1;

        public static float LastValue;

        public static bool Available
        {
            get { return Reader != null; }
        }

        public static void Bind(Func<int, int, float> reader)
        {
            Reader = reader;
        }

        public static float Read(int slot, int statId)
        {
            if (Reader == null || slot < 0 || statId < 0)
            {
                return float.NaN;
            }

            float value = Reader(slot, statId);
            Reads++;
            LastSlot = slot;
            LastStatId = statId;
            LastValue = value;

            return value;
        }

        /// <summary>Diagnostic decision body: reads a live MatchStats value and answers 1 when nonzero.</summary>
        public static short ProbeDecision(PlanAiHost host, PlanNode node, int statId)
        {
            return ProbeDecisionAt(host, host == null || host.Actor == null ? -1 : host.Actor.Slot, statId);
        }

        public static short ProbeDecisionAt(PlanAiHost host, int slot, int statId)
        {
            if (host == null || slot < 0)
            {
                return 0;
            }

            float value = Read(slot, statId);
            host.World.Log("decide.MatchStatProbe slot=" + slot + " stat=" + statId
                           + " value=" + value.ToString("0.###") + " reads=" + Reads);

            return (short)(value != 0.0f ? 1 : 0);
        }
    }

    /// <summary>Documented PlanAction stat handlers (plancond.cpp): 8 NoteStats4Possessors (empty),
    /// 10 StartStatTimer 0x00461dc0 and 11 EndStatTimer 0x00461e30. Both timer bodies only act on a
    /// pronoun target and forward the stat id; MatchStatAccumulator consumes the requests.</summary>
    public static class PlanAiMatchStatsActions
    {
        public static void NoteStats4Possessors(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            host.RecordAction(PlanOpcodes.ActionIds.NoteStats4Possessors, node, nArg,
                "empty source body (NoteStats4Possessors has no statements)");
        }

        public static void StartStatTimer(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            Timer(host, node, nArg, true);
        }

        public static void EndStatTimer(PlanAiHost host, PlanNode node, ref short state, int nArg)
        {
            Timer(host, node, nArg, false);
        }

        private static void Timer(PlanAiHost host, PlanNode node, int nArg, bool start)
        {
            PlanWord target = node.Args[1].Word;

            if (!target.IsPronoun)
            {
                host.RecordAction(start ? PlanOpcodes.ActionIds.StartStatTimer
                                        : PlanOpcodes.ActionIds.EndStatTimer,
                    node, nArg, "non-pronoun target; source body does nothing");
                return;
            }

            int slot = host.ArgResolver.ResolvePronoun(target.Low, target);
            int statId = node.Args[2].IntValue;

            if (slot < 0 || slot >= PlanAiConstants.PlayerObjectCount || statId < 0 || statId >= 0x31)
            {
                host.RecordAction(start ? PlanOpcodes.ActionIds.StartStatTimer
                                        : PlanOpcodes.ActionIds.EndStatTimer,
                    node, nArg, "invalid target/stat " + slot + "/" + statId + "; source does nothing");
                return;
            }

            PlanAiActionState effects = PlanAiActionStateStore.Get(host.Actor);
            effects.LastStatTimerSlot = slot;
            effects.LastStatTimerId = statId;

            if (start)
            {
                effects.StatTimerStartEvents++;
            }
            else
            {
                effects.StatTimerStopEvents++;
            }

            host.RecordAction(start ? PlanOpcodes.ActionIds.StartStatTimer
                                    : PlanOpcodes.ActionIds.EndStatTimer,
                node, nArg, "slot=" + slot + " stat=" + statId + " start=" + (start ? "True" : "False"));
        }
    }
}
