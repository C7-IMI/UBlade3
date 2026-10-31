using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The N9 extension seam: decision bodies ported for the wider AI-plan roster live here instead of
    /// in PlanAiDecisions, so the verified CARRYROK Plan AI slice and its 40 self-checks (which pin
    /// PlanAiDecisions.ImplementedIds() to the original four) stay untouched. PlanAiHost consults this
    /// table only after the built-in table misses; every id still comes from the shipped plan bytes, and
    /// an id with no body here is still explicit unsupported telemetry.
    /// </summary>
    public static class PlanAiDecisionExtensions
    {
        private static readonly Dictionary<int, PlanAiDecisionHandler> Handlers =
            new Dictionary<int, PlanAiDecisionHandler>();

        public static bool TryGet(int decisionId, out PlanAiDecisionHandler handler)
        {
            return Handlers.TryGetValue(decisionId, out handler);
        }

        public static void Register(int decisionId, PlanAiDecisionHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            Handlers[decisionId] = handler;
        }

        public static int[] ImplementedIds()
        {
            List<int> ids = new List<int>(Handlers.Keys);
            ids.Sort();

            return ids.ToArray();
        }
    }
}
