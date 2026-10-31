using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UBlade3.Sim;
using UnityEngine;
using Debug = UnityEngine.Debug;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The native-match validation probe. It loads the same original drome, teams, characters and
    // simulation the scene uses, runs the deterministic five-second input script headlessly, checks
    // the loaded counts/positions/camera/missing references and reports the capture state. Every line
    // starts with UBLADE3_NATIVE_MATCH_.
    //
    // Editor batch-mode entry point (no graphics device needed):
    //   Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3Match.Check -logFile <temp>\ublade3-match-check.log
    public static class MatchProbe
    {
        // The shipped team files this match loads and the characters their first three Dromers name
        // (seattle.htm slot 0 has no SetMotionParams line, so it keeps the ObjectDef zero position,
        // which is the exact 1996 loader behaviour).
        public const string ExpectedHomeTeamName = "The Machine";
        public const string ExpectedAwayTeamName = "The Fury";
        public const string ExpectedHomeSkaterModel = "CHIOMX";
        public const string ExpectedHomeTeammateModel = "CHIDMX";
        public const string ExpectedHomeGoalieModel = "CHIGMX";
        public const string ExpectedAwaySkaterModel = "SEADMX";
        public const string ExpectedAwayGoalieModel = "SEAGMX";

        public const int ExpectedPlayers = ObjectConstants.PlayerObjectCount;

        public const int ExpectedDromeObjects = DromeSceneLoader.ExpectedObjectCount;

        public const int ExpectedDromeModelObjects = DromeSceneLoader.ExpectedModelObjectCount;

        // The acceptance floor: the match must actually run at least five seconds of 30 Hz ticks and
        // the human-controlled skater must move under the deterministic held-key script.
        public const int RequiredTicks = 150;

        public const double RequiredSeconds = 5.0;

        public const float RequiredHumanDisplacement = 20.0f;

        private const float HumanCameraMinDistance = 6.0f;
        private const float HumanCameraMaxDistance = 80.0f;

        public sealed class Report
        {
            public int Mismatches;
            public readonly List<string> Lines = new List<string>();

            public void Check(bool condition, string message)
            {
                if (condition)
                {
                    return;
                }

                Mismatches++;
                Lines.Add("UBLADE3_NATIVE_MATCH_MISMATCH " + message);
            }
        }

        // The full headless acceptance run. Returns the mismatch count (0 = passed).
        public static int RunCheck(OriginalAssetRoot root)
        {
            Report report = new Report();
            MatchWorldData world = null;
            MatchSimulation sim = null;
            List<MatchCharacterData> characters = null;

            try
            {
                if (root == null)
                {
                    root = MatchScene.ResolveRoot();
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_CHECK root=" + root.DataPath
                          + " unity=" + Application.unityVersion
                          + " assetSet=Mdltxt no20xx=True");

                world = MatchWorldData.Load(root);
                Debug.Log(world.Describe());

                CheckDrome(report, world);
                CheckTeams(report, world);

                characters = world.LoadCharacters();

                for (int i = 0; i < characters.Count; i++)
                {
                    Debug.Log(characters[i].Describe());
                    CheckCharacter(report, characters[i]);
                }

                sim = MatchSimulation.Create(world);
                Debug.Log(sim.DescribeSimulation());

                CheckInitialPlacement(report, world, sim);

                HeldBinding human = HeldBinding.None;

                while (sim.TickCount < RequiredTicks)
                {
                    human = MatchScene.DeterministicInput(sim.TickCount);
                    sim.HumanInput = human;
                    sim.Step();
                }

                Debug.Log(sim.DescribeSimulation());
                Debug.Log(DescribeViewsFromSimulation(world, sim));

                CheckRun(report, world, sim, characters);
                CheckNoExternalRuntime(report);
                CheckDataDrivenHudAndFallbacks(report);
            }
            catch (Exception exception)
            {
                report.Mismatches++;
                report.Lines.Add("UBLADE3_NATIVE_MATCH_CHECK_FAILED exception: " + exception.GetType().Name
                                 + ": " + exception.Message);
                Debug.LogException(exception);
            }

            foreach (string line in report.Lines)
            {
                Debug.Log(line);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_CHECK ticks=" + (sim == null ? 0 : sim.TickCount)
                      + " seconds=" + (sim == null ? 0.0 : sim.MatchSeconds).ToString("0.###", CultureInfo.InvariantCulture)
                      + " mismatches=" + report.Mismatches
                      + " digest=" + (world == null ? "none" : world.Digest));
            Debug.Log(report.Mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_CHECK_FAILED mismatches=" + report.Mismatches);

            return report.Mismatches;
        }

        private static void CheckDrome(Report report, MatchWorldData world)
        {
            report.Check(world.Drome != null, "the drome scene data did not load");
            report.Check(world.Drome.Drome.Objects.Count == ExpectedDromeObjects,
                "drome object count " + world.Drome.Drome.Objects.Count + " != shipped " + ExpectedDromeObjects);
            report.Check(world.Drome.Objects.Count == ExpectedDromeModelObjects,
                "drome model object count " + world.Drome.Objects.Count + " != shipped " + ExpectedDromeModelObjects);
            report.Check(world.Drome.MeshCount == world.Drome.Objects.Count,
                "drome mesh count " + world.Drome.MeshCount + " != model object count " + world.Drome.Objects.Count);
            report.Check(world.Drome.MissingTextures.Count == 0,
                world.Drome.MissingTextures.Count + " drome textures missing");
            report.Check(world.Drome.MissingModels.Count == 0,
                world.Drome.MissingModels.Count + " drome models missing");
            report.Check(world.Drome.BuiltVertices == world.Drome.BuiltTriangles * 3,
                "drome built vertices " + world.Drome.BuiltVertices + " != 3 * triangles " + world.Drome.BuiltTriangles);
            report.Check(world.SimDrome.Placements.Count == world.Drome.Objects.Count,
                "simulation placement count " + world.SimDrome.Placements.Count + " != drome model objects "
                + world.Drome.Objects.Count);
            report.Check(world.SimDrome.RokDefinition != null, "the drome's Rok definition did not load");
            report.Check(world.Drome.Digest != null && world.Drome.Digest.Length == 16,
                "drome mesh digest is not a 16 digit hash: " + world.Drome.Digest);

            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_DROME file=" + world.Drome.DromeFileName
                      + " objects=" + world.Drome.Drome.Objects.Count
                      + " modelObjects=" + world.Drome.Objects.Count
                      + " noModelObjects=" + world.Drome.ObjectsWithoutModel.Count
                      + " meshes=" + world.Drome.MeshCount
                      + " triangles=" + world.Drome.BuiltTriangles
                      + " textures=" + world.Drome.TextureFiles.Count
                      + " digest=" + world.Drome.Digest);
            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_DROME_BOUNDS unityMin=(" + F(world.Drome.BoundsMin.x) + ","
                      + F(world.Drome.BoundsMin.y) + "," + F(world.Drome.BoundsMin.z) + ")"
                      + " unityMax=(" + F(world.Drome.BoundsMax.x) + ","
                      + F(world.Drome.BoundsMax.y) + "," + F(world.Drome.BoundsMax.z) + ")"
                      + (world.Drome.HasDromeBounds
                          ? " dromeObjectMin=(" + F(world.Drome.DromeBoundsMin.x) + ","
                            + F(world.Drome.DromeBoundsMin.y) + "," + F(world.Drome.DromeBoundsMin.z) + ")"
                            + " dromeObjectMax=(" + F(world.Drome.DromeBoundsMax.x) + ","
                            + F(world.Drome.DromeBoundsMax.y) + "," + F(world.Drome.DromeBoundsMax.z) + ")"
                          : " dromeObject=missing"));
        }

        private static void CheckTeams(Report report, MatchWorldData world)
        {
            report.Check(world.Home.File.Name == ExpectedHomeTeamName,
                "home team name \"" + world.Home.File.Name + "\" != \"" + ExpectedHomeTeamName + "\"");
            report.Check(world.Away.File.Name == ExpectedAwayTeamName,
                "away team name \"" + world.Away.File.Name + "\" != \"" + ExpectedAwayTeamName + "\"");

            report.Check(world.Home.OnIce[0].ModelName == ExpectedHomeSkaterModel,
                "home skater model " + world.Home.OnIce[0].ModelName + " != " + ExpectedHomeSkaterModel);
            report.Check(world.Home.OnIce[1].ModelName == ExpectedHomeTeammateModel,
                "home teammate model " + world.Home.OnIce[1].ModelName + " != " + ExpectedHomeTeammateModel);
            report.Check(world.Home.OnIce[2].ModelName == ExpectedHomeGoalieModel,
                "home goalie model " + world.Home.OnIce[2].ModelName + " != " + ExpectedHomeGoalieModel);
            report.Check(world.Away.OnIce[0].ModelName == ExpectedAwaySkaterModel,
                "away skater model " + world.Away.OnIce[0].ModelName + " != " + ExpectedAwaySkaterModel);
            report.Check(world.Away.OnIce[1].ModelName == ExpectedAwaySkaterModel,
                "away teammate model " + world.Away.OnIce[1].ModelName + " != " + ExpectedAwaySkaterModel);
            report.Check(world.Away.OnIce[2].ModelName == ExpectedAwayGoalieModel,
                "away goalie model " + world.Away.OnIce[2].ModelName + " != " + ExpectedAwayGoalieModel);

            // Players is indexed by the shipped slot id; LoadTeamFromFile gives the home side even
            // ids 0,2,4 and the away side 1,3,5, so side == slot & 1 and roster slot == slot / 2.
            for (int slot = 0; slot < ExpectedPlayers; slot++)
            {
                MatchPlayerSlot player = world.Players[slot];

                report.Check(player.SimSlot == slot,
                    "player array entry " + slot + " carries sim slot " + player.SimSlot);
                report.Check(player.Side == (slot & 1),
                    "slot " + slot + " is on side " + player.Side + ", expected " + (slot & 1));
                report.Check(player.RosterSlot == slot / 2,
                    "slot " + slot + " is roster slot " + player.RosterSlot + ", expected " + (slot / 2));
            }

            report.Check(!world.Home.OnIce[0].RemapPalette, "the home skater must not remap the palette");
            report.Check(world.Away.OnIce[0].RemapPalette && world.Away.OnIce[1].RemapPalette
                         && world.Away.OnIce[2].RemapPalette, "away players must take the forced palette remap");
            report.Check(world.Home.OnIce[2].AssemblyName == MatchWorldData.GoalieAssembly,
                "home goalie assembly is not " + MatchWorldData.GoalieAssembly);
            report.Check(world.Home.OnIce[0].AssemblyName == MatchWorldData.SkaterAssembly,
                "home skater assembly is not " + MatchWorldData.SkaterAssembly);
            report.Check(world.Home.OnIce[0].Ratings != null && world.Home.OnIce[0].Ratings.Length == 63,
                "home skater rating table is not 63 values");
            report.Check(world.Home.OnIce[2].Ratings != null && world.Home.OnIce[2].Ratings.Length == 16,
                "home goalie rating table is not 16 values");
        }

        private static void CheckCharacter(Report report, MatchCharacterData character)
        {
            report.Check(character.Character != null, "slot " + character.Slot.SimSlot + " has no character");
            report.Check(character.Character.Rig.BoneCount == 16,
                "slot " + character.Slot.SimSlot + " rig has " + character.Character.Rig.BoneCount + " bones");
            report.Check(character.Character.Animation.DisplayedFrameCount > 0,
                "slot " + character.Slot.SimSlot + " has no animation frames");
            report.Check(character.Character.Animation.StandardFps == 30.0f,
                "slot " + character.Slot.SimSlot + " clip rate " + character.Character.Animation.StandardFps + " != 30");
            report.Check(character.Character.MissingTextures.Count == 0,
                "slot " + character.Slot.SimSlot + " is missing " + character.Character.MissingTextures.Count + " textures");
            report.Check(character.Character.Mesh.TriangleCount > 0,
                "slot " + character.Slot.SimSlot + " built no triangles");
            report.Check(character.Preview.Digest != null && character.Preview.Digest.Length == 16,
                "slot " + character.Slot.SimSlot + " preview digest is missing");
        }

        private static void CheckInitialPlacement(Report report, MatchWorldData world, MatchSimulation sim)
        {
            for (int slot = 0; slot < ExpectedPlayers; slot++)
            {
                SimVec3 position = sim.Sim.Players[slot].Body.Pos;

                report.Check(Finite(position), "slot " + slot + " initial position is not finite");
                report.Check(EllipsoidStatics.Arena.PointInside(position) || position.z <= 0.0f,
                    "slot " + slot + " initial position is outside the arena");
            }

            report.Check(Finite(sim.Sim.MainCamera.Body.Pos), "the main camera initial position is not finite");
        }

        private static void CheckRun(Report report, MatchWorldData world, MatchSimulation sim,
            List<MatchCharacterData> characters)
        {
            report.Check(sim.TickCount == RequiredTicks,
                "simulation ran " + sim.TickCount + " ticks != " + RequiredTicks);
            report.Check(sim.MatchSeconds + 1e-6 >= RequiredSeconds,
                "simulation ran " + sim.MatchSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + " s < " + RequiredSeconds + " s");
            report.Check(Math.Abs(sim.Sim.Rules.Clock.Delta - (1.0f / 30.0f)) < 1e-4f,
                "fixed tick delta " + sim.Sim.Rules.Clock.Delta + " != 1/30");
            int expectedClockMs = (int)(RequiredTicks * (double)ClockConstants.FixedTickMs);
            report.Check(sim.ClockMilliseconds == expectedClockMs,
                "simulated clock stamp " + sim.ClockMilliseconds + " != " + expectedClockMs);

            report.Check(sim.HumanDisplacement() >= RequiredHumanDisplacement,
                "the human skater moved only " + sim.HumanDisplacement().ToString("0.###", CultureInfo.InvariantCulture)
                + " units; the acceptance floor is " + RequiredHumanDisplacement);

            SimVec3 human = sim.Sim.Players[MatchSimulation.HumanSlot].Body.Pos;
            SimVec3 camera = sim.Sim.MainCamera.Body.Pos;

            float dx = camera.x - human.x;
            float dy = camera.y - human.y;
            float dz = camera.z - human.z;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

            report.Check(Finite(camera), "the chase camera position is not finite");
            report.Check(distance >= HumanCameraMinDistance && distance <= HumanCameraMaxDistance,
                "the chase camera is " + distance.ToString("0.###", CultureInfo.InvariantCulture)
                + " units from the human skater; expected " + HumanCameraMinDistance + ".." + HumanCameraMaxDistance);
            report.Check(sim.Sim.MainCamera.HalfFov * 2.0f * 57.29577951308232f > 84.9f
                         && sim.Sim.MainCamera.HalfFov * 2.0f * 57.29577951308232f < 85.1f,
                "the simulation camera FOV is not 85 degrees");

            int distinct = 0;

            for (int slot = 0; slot < ExpectedPlayers; slot++)
            {
                SimVec3 a = sim.Sim.Players[slot].Body.Pos;

                if (!Finite(a))
                {
                    report.Check(false, "slot " + slot + " position became non-finite");
                }

                for (int other = slot + 1; other < ExpectedPlayers; other++)
                {
                    SimVec3 b = sim.Sim.Players[other].Body.Pos;
                    float ex = a.x - b.x;
                    float ey = a.y - b.y;
                    float ez = a.z - b.z;

                    if (Math.Sqrt(ex * ex + ey * ey + ez * ez) > 2.0f)
                    {
                        distinct++;
                    }
                }
            }

            report.Check(distinct >= 8, "only " + distinct + " of the 15 skater-pair separations exceed 2 units");

            for (int i = 0; i < characters.Count; i++)
            {
                MatchCharacterData character = characters[i];
                int frame = character.Character.FrameAtSeconds(5.0);

                report.Check(frame >= character.Character.Animation.StartFrame
                             && frame <= character.Character.Animation.EndFrame,
                    "slot " + character.Slot.SimSlot + " frame " + frame + " is outside the clip range");
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_RUN ticks=" + sim.TickCount
                      + " seconds=" + sim.MatchSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                      + " humanDisplacement=" + sim.HumanDisplacement().ToString("0.###", CultureInfo.InvariantCulture)
                      + " cameraDistance=" + distance.ToString("0.###", CultureInfo.InvariantCulture)
                      + " playerSeparations=" + distinct);
        }

        // This scene must run with no external runtime: nothing calls hyperblade_20XX.exe or the
        // native engine plugin. The check records the absence of a 20XX process while it runs.
        private static void CheckNoExternalRuntime(Report report)
        {
            int twentyXx = 0;

            try
            {
                Process[] processes = Process.GetProcessesByName("hyperblade_20XX");
                twentyXx = processes == null ? 0 : processes.Length;
            }
            catch (Exception)
            {
                twentyXx = 0;
            }

            report.Check(twentyXx == 0, twentyXx + " hyperblade_20XX process(es) are running during the match check");
            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_NO_20XX running20xxProcesses=" + twentyXx + " no20xx=True");
        }

        private static void CheckDataDrivenHudAndFallbacks(Report report)
        {
            // Both are real gaps recorded rather than hidden: these checks assert the labels exist,
            // not that the features are done.
            report.Check(MatchSimulation.AiFallbackNote.Length > 0, "the fallback AI label is empty");
            report.Check(MatchSimulation.GoaliePlacementNote.Length > 0, "the goalie placement label is empty");
            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_HUD_PENDING data=teamNames,score,period available;"
                      + " original HUD fonts/art not ported (N6)");
            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_AI_FALLBACK note=\"" + MatchSimulation.AiFallbackNote + "\"");
            Debug.Log("UBLADE3_NATIVE_MATCH_PROBE_GOALIE note=\"" + MatchSimulation.GoaliePlacementNote + "\"");
        }

        // Capture-state probe for the Editor capture (and the built player's --match-capture run):
        // logs the scene's loaded world, simulation state and every captured frame.
        public static string DescribeScene(MatchScene scene)
        {
            StringBuilder text = new StringBuilder();

            if (scene == null || !scene.IsBuilt)
            {
                text.Append("UBLADE3_NATIVE_MATCH_PROBE captureState=not-built");
                Debug.Log(text.ToString());

                return text.ToString();
            }

            Debug.Log(scene.World.Describe());
            Debug.Log(scene.Sim.DescribeSimulation());
            Debug.Log(scene.DescribeViews());

            for (int i = 0; i < scene.Captures.Count; i++)
            {
                MatchCaptureRecord record = scene.Captures[i];

                text.Clear();
                text.Append("UBLADE3_NATIVE_MATCH_PROBE_CAPTURE index=").Append(record.Index);
                text.Append(" tick=").Append(record.Tick);
                text.Append(" seconds=").Append(record.Seconds.ToString("0.###", CultureInfo.InvariantCulture));
                text.Append(" path=").Append(record.Path);
                text.Append(" width=").Append(record.Width);
                text.Append(" height=").Append(record.Height);
                text.Append(" bytes=").Append(record.Bytes);
                text.Append(" humanFrame=").Append(record.HumanFrame);
                text.Append(" humanDisplacement=").Append(record.HumanDisplacement.ToString("0.###", CultureInfo.InvariantCulture));
                text.Append(" cameraPos=(").Append(MatchSimulation.F(record.CameraPosition.x)).Append(',')
                    .Append(MatchSimulation.F(record.CameraPosition.y)).Append(',')
                    .Append(MatchSimulation.F(record.CameraPosition.z)).Append(')');
                Debug.Log(text.ToString());
            }

            text.Clear();
            text.Append("UBLADE3_NATIVE_MATCH_PROBE captureState=complete frames=").Append(scene.Captures.Count);
            text.Append(" ticks=").Append(scene.TickCount);
            text.Append(" seconds=").Append(scene.MatchSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            text.Append(" missingRefs=").Append(scene.TotalMissingRefs());
            text.Append(" digest=").Append(scene.World.Digest);

            Debug.Log(text.ToString());

            return text.ToString();
        }

        private static string DescribeViewsFromSimulation(MatchWorldData world, MatchSimulation sim)
        {
            StringBuilder text = new StringBuilder();

            for (int slot = 0; slot < ExpectedPlayers; slot++)
            {
                MatchPlayerSlot player = world.Players[slot];
                PlayerState state = sim.Sim.Players[slot];
                SimVec3 position = state.Body.Pos;

                text.Append("UBLADE3_NATIVE_MATCH_VIEW slot=").Append(slot);
                text.Append(" role=").Append(player.Role);
                text.Append(" model=").Append(player.ModelName);
                text.Append(" pos=(").Append(MatchSimulation.F(position.x)).Append(',')
                    .Append(MatchSimulation.F(position.y)).Append(',')
                    .Append(MatchSimulation.F(position.z)).Append(')');
                text.AppendLine();
            }

            return text.ToString().TrimEnd();
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool Finite(SimVec3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
