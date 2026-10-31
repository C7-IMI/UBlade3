using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    public sealed class PlanAiSelfCheckResult
    {
        public int Passed;

        public int Total;

        public readonly List<string> Failures = new List<string>();

        public bool Ok
        {
            get { return Total == Passed; }
        }
    }

    /// <summary>
    /// Unit checks for the ported math, agenda/timer and decision bodies. They run without the retail
    /// install so the Plan AI slice's pieces stay verified even when the data path is absent; the
    /// Probe adds the shipped-plan and deterministic-trace checks on top.
    /// </summary>
    public static class PlanAiSelfCheck
    {
        public static PlanAiSelfCheckResult Run()
        {
            PlanAiSelfCheckResult result = new PlanAiSelfCheckResult();
            Checker checker = new Checker(result);

            CheckRandom(checker);
            CheckVec3(checker);
            CheckDamageGate(checker);
            CheckMessageQueue(checker);
            CheckTimer(checker);
            CheckDecisions(checker);

            return result;
        }

        private sealed class Checker
        {
            private readonly PlanAiSelfCheckResult result;

            public Checker(PlanAiSelfCheckResult result)
            {
                this.result = result;
            }

            public void Check(string name, bool condition)
            {
                result.Total++;

                if (condition)
                {
                    result.Passed++;
                }
                else
                {
                    result.Failures.Add(name);
                }
            }

            public void Throws<T>(string name, Action action) where T : Exception
            {
                result.Total++;

                try
                {
                    action();
                    result.Failures.Add(name + " (no exception)");
                }
                catch (T)
                {
                    result.Passed++;
                }
                catch (Exception exception)
                {
                    result.Failures.Add(name + " (threw " + exception.GetType().Name + ")");
                }
            }
        }

        private static PlanNode LeafNode(int opcode, params int[] values)
        {
            PlanNode node = new PlanNode();
            node.Opcode = opcode;
            node.ArgCount = unchecked((short)values.Length);
            node.Args = new PlanOperand[values.Length];

            for (int index = 0; index < values.Length; index++)
            {
                node.Args[index] = PlanOperand.ForLeaf(new PlanWord(values[index]), new PlanWord(values[index]));
            }

            return node;
        }

        private static void CheckRandom(Checker checker)
        {
            // Expected values computed independently from src/math/squirrel5.h's constants.
            checker.Check("random.squirrel5.0.0", PlanAiRandom.Squirrel5(0u, 0u) == 0xDA46EFD3u);
            checker.Check("random.squirrel5.1.0", PlanAiRandom.Squirrel5(1u, 0u) == 898227765u);
            checker.Check("random.squirrel5.0.seed", PlanAiRandom.Squirrel5(0u, 0x12345678u) == 2758154431u);
            checker.Check("random.squirrel5.5.seed", PlanAiRandom.Squirrel5(5u, 0xABCDEF01u) == 2083054808u);
            checker.Check("random.squirrel5.123", PlanAiRandom.Squirrel5(123u, 0x5EED1234u) == 1457539601u);

            PlanAiRandom.Seed(0x5EED1234u);
            bool sequence = PlanAiRandom.Rand() == 1800105814 && PlanAiRandom.Rand() == 1431481063 &&
                            PlanAiRandom.Rand() == 2067783103 && PlanAiRandom.Rand() == 863313664 &&
                            PlanAiRandom.Rand() == 977294423 && PlanAiRandom.Rand() == 400382195;
            checker.Check("random.sequence.seed", sequence);

            PlanAiRandom.Seed(1u);
            checker.Check("random.sequence.reseed", PlanAiRandom.Rand() == 2027696724);

            PlanAiRandom.Seed(0u);
            float signed = PlanAiRandom.RandSigned();
            checker.Check("random.signed.range", signed >= -1.0f && signed <= 1.0f);
            checker.Check("random.magnitude", PlanAiRandom.RandMagnitude() >= 0.0f);
        }

        private static void CheckVec3(Checker checker)
        {
            PlanAiVec3 zero = new PlanAiVec3(0.0f, 0.0f, 0.0f);
            checker.Check("vec3.zero.normalize", !zero.NormalizeInPlace() && zero.X == 0.0f && zero.Y == 0.0f && zero.Z == 0.0f);

            PlanAiVec3 threeFour = new PlanAiVec3(3.0f, 4.0f, 0.0f);
            checker.Check("vec3.length", Math.Abs(threeFour.Length - 5.0f) < 0.0001f);
            PlanAiVec3 unit = threeFour.NormalizedCopy();
            checker.Check("vec3.normalize", Math.Abs(unit.X - 0.6f) < 0.0001f && Math.Abs(unit.Y - 0.8f) < 0.0001f);
            checker.Check("vec3.dot", Math.Abs(PlanAiVec3.Dot(threeFour, threeFour) - 25.0f) < 0.0001f);

            PlanAiVec3 sum = new PlanAiVec3(1.0f, 2.0f, 3.0f) + new PlanAiVec3(4.0f, 5.0f, 6.0f);
            PlanAiVec3 difference = sum - new PlanAiVec3(1.0f, 2.0f, 3.0f);
            PlanAiVec3 scaled = difference * 2.0f;
            checker.Check("vec3.operators", scaled.X == 8.0f && scaled.Y == 10.0f && scaled.Z == 12.0f);
        }

        private static void CheckDamageGate(Checker checker)
        {
            PlanAiWorld world = new PlanAiWorld();
            world.Difficulty = PlanAiDifficulty.Easy;
            checker.Check("damagegate.easy", world.DamageGate == 3.0f);
            world.Difficulty = PlanAiDifficulty.Medium;
            checker.Check("damagegate.medium", world.DamageGate == 6.0f);
            world.Difficulty = PlanAiDifficulty.Hard;
            checker.Check("damagegate.hard", world.DamageGate == 9.0f);
        }

        private static void CheckMessageQueue(Checker checker)
        {
            PlanAiWorld world = PlanAiFixture.CreateCarryRokWorld();
            PlanAiHost host = new PlanAiHost(world, PlanAiFixture.CarrierSlot);
            int before = world.Pending.Count;
            world.PostSceneMessage(0, PlanAiConstants.MessageInitiate, 0, PlanAiFixture.CarrierSlot, 0);
            checker.Check("queue.no-handler.not-queued", world.Pending.Count == before);

            bool dropped = false;

            for (int index = 0; index < world.Events.Count; index++)
            {
                if (world.Events[index].Contains("reason=no-handler"))
                {
                    dropped = true;
                }
            }

            checker.Check("queue.no-handler.telemetry", dropped);
            checker.Check("queue.no-target", world.GetObject(99) == null);
            host.World.Log("selftest"); // keep the host/world pair referenced
        }

        private static void CheckTimer(Checker checker)
        {
            PlanAiWorld world = PlanAiFixture.CreateCarryRokWorld();
            PlanAiHost host = new PlanAiHost(world, PlanAiFixture.CarrierSlot);
            PlanAiObject actor = host.Actor;

            PlanNode timerNode = LeafNode(PlanOpcodes.OpcodeSetTimeoutMsg, 50, 69, 3);
            host.SetTimeoutMessage(timerNode, null, 0);
            checker.Check("timer.armed", actor.Observers is PlanAiTimerActivity && host.TimerArmCount == 1);

            PlanAiTimerActivity timer = (PlanAiTimerActivity)actor.Observers;
            checker.Check("timer.firetick", timer.FireTickMs == 500 && timer.Event == 69 && timer.TargetSlot == 3 && timer.EventParam == 0);

            world.ClockMs = 500;
            world.TickAgenda();
            checker.Check("timer.not-early", CountEvents(world, "timer-fire") == 0);

            world.ClockMs = 501;
            world.TickAgenda();
            checker.Check("timer.fires-strictly-after", CountEvents(world, "timer-fire") == 1 && actor.Observers == null);
            checker.Check("timer.posts-message", CountEvents(world, "post.dropped") >= 1);

            PlanNode dataNode = LeafNode(PlanOpcodes.OpcodeSetTimeoutMsg, 10, 60, 3, PlanWord.TokenMessageData);
            host.SetTimeoutMessage(dataNode, null, 7);
            PlanAiTimerActivity dataTimer = (PlanAiTimerActivity)actor.Observers;
            checker.Check("timer.message-data", dataTimer.FireTickMs == 601 && dataTimer.EventParam == 7);

            PlanNode explicitNode = LeafNode(PlanOpcodes.OpcodeSetTimeoutMsg, 10, 60, 3, 5);
            host.SetTimeoutMessage(explicitNode, null, 7);
            PlanAiTimerActivity explicitTimer = (PlanAiTimerActivity)actor.Observers;
            checker.Check("timer.explicit-data", explicitTimer.EventParam == 5);
        }

        private static int CountEvents(PlanAiWorld world, string needle)
        {
            int count = 0;

            for (int index = 0; index < world.Events.Count; index++)
            {
                if (world.Events[index].Contains(needle))
                {
                    count++;
                }
            }

            return count;
        }

        private static void CheckDecisions(Checker checker)
        {
            PlanAiWorld world = PlanAiFixture.CreateCarryRokWorld();
            PlanAiHost host = new PlanAiHost(world, PlanAiFixture.CarrierSlot);
            PlanAiObject carrier = host.Actor;
            PlanAiObject mate = world.GetObject(1);
            PlanAiObject human = world.GetObject(0);
            PlanAiDecisionHandler handler;

            checker.Check("decision.ids.ported", PlanAiDecisions.ImplementedIds().Length == 4);

            if (PlanAiDecisions.TryGet(PlanOpcodes.DecisionIds.DecidePosition, out handler))
            {
                checker.Check("decision.position.attached", handler(host, null, 0) == 2);

                carrier.Stats.Set(PlanAiConstants.RatingDamage, 5.0f);
                checker.Check("decision.position.gated", handler(host, null, 0) == 5);
                carrier.Stats.Set(PlanAiConstants.RatingDamage, 20.0f);

                carrier.ClockStoppedSemaphore = 1;
                checker.Check("decision.position.held", handler(host, null, 0) == 4);
                carrier.ClockStoppedSemaphore = 0;

                world.Rok.Body.AttachTargetSlot = -1;
                checker.Check("decision.position.loose", handler(host, null, 0) == 1);
                world.Rok.Body.AttachTargetSlot = PlanAiFixture.CarrierSlot;
            }

            if (PlanAiDecisions.TryGet(PlanOpcodes.DecisionIds.OkToPassToTeammate, out handler))
            {
                checker.Check("decision.pass.healthy", handler(host, null, 0) == 1);
                mate.DesiredSubstitute = 4;
                checker.Check("decision.pass.substituting", handler(host, null, 0) == 0);
                mate.DesiredSubstitute = -1;
            }

            if (PlanAiDecisions.TryGet(PlanOpcodes.DecisionIds.RecvrInRangeNView, out handler))
            {
                checker.Check("decision.receiver.far", handler(host, null, 1) == 1);
                checker.Check("decision.receiver.near", handler(host, null, PlanAiFixture.HumanTeammateSlot) == 0);
                checker.Throws<PlanAiFatalException>("decision.receiver.badslot", delegate { handler(host, null, 4); });
                longPassEnableSwitch(human);
                checker.Check("decision.receiver.longpass", handler(host, null, PlanAiFixture.HumanTeammateSlot) == 0);
            }

            if (PlanAiDecisions.TryGet(PlanOpcodes.DecisionIds.ShootSituation, out handler))
            {
                checker.Check("decision.shoot.far", handler(host, null, 0) == 1);
            }

            checker.Check("decision.alias.table", PlanOpcodes.IsSupportedDecisionId(PlanOpcodes.DecisionIds.RecvrInRangeNView));
        }

        private static void longPassEnableSwitch(PlanAiObject human)
        {
            human.Stats.Set(PlanAiConstants.RatingLongPassEnable, 1.0f);
        }
    }
}
