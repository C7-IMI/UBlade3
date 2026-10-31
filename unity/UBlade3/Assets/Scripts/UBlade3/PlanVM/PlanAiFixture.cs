using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The mock match fixture the Plan AI probe drives. It is deliberately small and explicit: the
    /// six player slots, the Rok (attached to the carrier), the two goals, the position markers and
    /// the rating scalars the ported decision bodies read. Every value is an input to the ported
    /// bodies, not a substitute for one: the handlers still compute their outcomes from these fields.
    /// </summary>
    public static class PlanAiFixture
    {
        public const short CarrierSlot = 3;

        public const short HumanTeammateSlot = 0;

        public const string CarrierPlanName = "CarryRok";

        /// <summary>The owner plan table EngineObject::FindPlanByName walks; the CARRYROK plan references these names.</summary>
        public static readonly string[] CarryRokPlanTable = new string[]
        {
            "CarryRok",
            "Defense",
            "Human",
            "OffWing",
            "Sideln",
        };

        public static PlanAiWorld CreateCarryRokWorld()
        {
            PlanAiWorld world = new PlanAiWorld();
            world.TeammateMode = 1;
            world.Difficulty = PlanAiDifficulty.Medium;
            world.PlanTable.AddRange(CarryRokPlanTable);

            // Slots 0 and 1 are the two possible pass receivers the probe exercises: slot 1 starts
            // far away (the RecvrInRangeNView arm that re-arms the documented data-0 timer) and
            // slot 0 starts close and in front (the arm the buggy re-arm actually tests).
            PlanAiObject human = AddPlayer(world, 0, "HumanMate", "Human", new PlanAiVec3(0.0f, 100.0f, 0.0f));
            PlanAiObject mate = AddPlayer(world, 1, "AiMate", "Human", new PlanAiVec3(0.0f, 400.0f, 0.0f));
            AddPlayer(world, 2, "OpponentA", "Defense", new PlanAiVec3(0.0f, -100.0f, 0.0f));
            PlanAiObject carrier = AddPlayer(world, CarrierSlot, "AiCarrier", CarrierPlanName, new PlanAiVec3(0.0f, 0.0f, 0.0f));
            AddPlayer(world, 4, "GoalieA", "Goalie", new PlanAiVec3(0.0f, 250.0f, 0.0f));
            AddPlayer(world, 5, "GoalieB", "Goalie", new PlanAiVec3(0.0f, -250.0f, 0.0f));

            carrier.Body.VelocityDirection = new PlanAiVec3(0.0f, 1.0f, 0.0f);
            carrier.Stats.Set(PlanAiConstants.RatingDamage, 20.0f);
            carrier.Stats.Set(PlanAiConstants.RatingCarryRokThrowDist, 150.0f);
            human.Stats.Set(PlanAiConstants.RatingDamage, 20.0f);
            human.Stats.Set(PlanAiConstants.RatingLongPassEnable, 0.0f);
            mate.Stats.Set(PlanAiConstants.RatingDamage, 20.0f);

            PlanAiObject rok = world.AddObject(PlanAiConstants.SlotRok, "Rok", null);
            rok.Body.AttachTargetSlot = CarrierSlot;
            rok.Body.Position = carrier.Body.Position;

            AddMarker(world, PlanAiConstants.SlotNgoal, "NGoal", new PlanAiVec3(0.0f, 250.0f, 0.0f));
            AddMarker(world, PlanAiConstants.SlotSgoal, "SGoal", new PlanAiVec3(0.0f, -250.0f, 0.0f));

            return world;
        }

        private static PlanAiObject AddPlayer(PlanAiWorld world, int slot, string name, string planName, PlanAiVec3 position)
        {
            PlanAiObject player = world.AddObject(slot, name, planName);
            player.Body.Position = position;
            player.Stats.Set(PlanAiConstants.RatingDamage, 20.0f);

            return player;
        }

        private static PlanAiObject AddMarker(PlanAiWorld world, int slot, string name, PlanAiVec3 position)
        {
            PlanAiObject marker = world.AddObject(slot, name, null);
            marker.Body.Position = position;

            return marker;
        }
    }
}
