using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UBlade3.Native;
using UBlade3.Sim;
using Debug = UnityEngine.Debug;

namespace UBlade3.EditorTools
{
    // Editor gates for N5 gameplay: ROK rendering, goal detection/scoring and the original-style HUD.
    // All entry points are callable from the UBlade3 menu and from Unity batch mode:
    //
    //   Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.Check -logFile <temp>\ublade3-gameplay-check.log
    //
    //   Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3MatchGameplay.CaptureAll -logFile <temp>\ublade3-gameplay-capture.log
    //
    // The capture path needs a graphics device: run -batchmode WITHOUT -nographics. The scene is
    // built in memory from the player's own HyperBlade install; no scene asset, prefab or copy of the
    // original data is written to the repository.
    public static class UBlade3MatchGameplay
    {
        public const string CapturePrefix = "native-gameplay";

        public const string PlayerExecutable = "build-match/UBlade3NativeMatchGameplay.exe";

        public const int CaptureWidth = MatchGameplay.CaptureWidth;

        public const int CaptureHeight = MatchGameplay.CaptureHeight;

        private const string TempScenePath = "Assets/Scenes/__UBlade3NativeGameplayTemp.unity";

        private const string PreviewMaterialPath = "Assets/Scripts/UBlade3/Native/DromePreviewMaterial.mat";

        public static readonly string[] CaptureFiles =
        {
            "artifacts/native-gameplay-00-match-5s.png",
            "artifacts/native-gameplay-01-goal-home-2.png",
            "artifacts/native-gameplay-02-rok-plain.png",
            "artifacts/native-gameplay-03-rok-double.png",
            "artifacts/native-gameplay-04-rok-quad.png",
            "artifacts/native-gameplay-05-goal-away-1.png",
            "artifacts/native-gameplay-06-scoreboard-2-1.png",
        };

        private static string RepositoryRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        private static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        private static string ResolveArtifactPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RepositoryRoot, path));
        }

        private static string ResolvePlayerPath()
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, PlayerExecutable));
        }

        // --- menu -------------------------------------------------------------------------

        [MenuItem("UBlade3/Native Gameplay/Run Validation Check")]
        public static void CheckMenu()
        {
            Check(false);
        }

        [MenuItem("UBlade3/Native Gameplay/Capture Match, Goal And HUD Frames")]
        public static void CaptureAllMenu()
        {
            CaptureAll(false);
        }

        [MenuItem("UBlade3/Native Gameplay/Build Windows Preview Player")]
        public static void BuildWindowsMenu()
        {
            BuildWindowsPlayer(false);
        }

        [MenuItem("UBlade3/Native Gameplay/Build And Capture Windows Preview")]
        public static void BuildAndRunMenu()
        {
            BuildAndRunWindows(false);
        }

        // --- -executeMethod entry points ---------------------------------------------------

        public static void Check()
        {
            Check(true);
        }

        public static void CaptureAll()
        {
            CaptureAll(true);
        }

        public static void BuildWindows()
        {
            BuildWindowsPlayer(true);
        }

        public static void BuildAndRunWindows()
        {
            BuildAndRunWindows(true);
        }

        // --- headless check ----------------------------------------------------------------

        // Loads the same original data the scene uses (no graphics objects), runs the deterministic
        // goal fixtures and checks the scoring/HUD/ROK rules without a graphics device.
        private static void Check(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += CheckGameplay();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_GAMEPLAY_CHECK_OK"
                : "UBLADE3_NATIVE_GAMEPLAY_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int CheckGameplay()
        {
            int mismatches = 0;
            OriginalAssetRoot root = MatchScene.ResolveRoot();

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK root=" + root.DataPath
                      + " unity=" + Application.unityVersion + " no20xx=True");

            MatchWorldData world = MatchWorldData.Load(root);
            Debug.Log(world.Describe());

            MatchRokData rokData = MatchRokData.Load(world);
            Debug.Log(rokData.Describe());

            if (rokData.Object.Definition.Model != "Rokmix")
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH rok model=" + rokData.Object.Definition.Model + " != Rokmix");
            }

            if (rokData.MissingTextures.Count != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH missing ROK textures=" + rokData.MissingTextures.Count);
            }

            if (rokData.BoneCount < 9)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH ROK bones=" + rokData.BoneCount + " < 9 render slots");
            }

            for (int bone = 0; bone < rokData.BoneTriangleCounts.Length; bone++)
            {
                if (rokData.BoneTriangleCounts[bone] == 0 && (bone == 0 || bone == 2 || bone == 7 || bone == 8))
                {
                    mismatches++;
                    Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH render bone " + bone + " has no triangles");
                }
            }

            MatchHudData hud = MatchHudData.Load(world);
            Debug.Log(hud.Describe());

            if (hud.Missing.Count != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH HUD art missing=" + hud.Missing.Count);
            }

            if (hud.PeriodFrame == null || hud.TeamAFrame == null || hud.TeamBFrame == null
                || hud.HomeLogo == null || hud.AwayLogo == null)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH HUD art null");
            }
            else
            {
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_HUD_ART"
                          + " period=" + hud.PeriodFrame.Width + "x" + hud.PeriodFrame.Height
                          + " teamA=" + hud.TeamAFrame.Width + "x" + hud.TeamAFrame.Height
                          + " teamB=" + hud.TeamBFrame.Width + "x" + hud.TeamBFrame.Height
                          + " homeLogo=" + hud.HomeLogo.Width + "x" + hud.HomeLogo.Height
                          + " awayLogo=" + hud.AwayLogo.Width + "x" + hud.AwayLogo.Height
                          + " textFont=interim3x5 originalTextFont=MS Sans Serif");
            }

            MatchSimulation sim = MatchSimulation.Create(world);
            sim.Sim.Rules.SetHomeScore(0);
            sim.Sim.Rules.SetAwayScore(0);
            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);

            bool bonesOk = MatchScoreFixture.CheckRenderBoneMapping(sim.Sim, out string boneDetail);
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_RENDER_BONES ok=" + bonesOk + " mapping=" + boneDetail);

            if (!bonesOk)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH render-bone mapping " + boneDetail);
            }

            MatchGoalDetector detector = new MatchGoalDetector();

            // Home goal: ROK value 2, south goal -> 2 points for the home side.
            MatchGoalEvent home = MatchScoreFixture.RunIntoGoal(sim.Sim, detector, true, 2, 170, 2,
                () => sim.Step());

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_GOAL side=" + home.ScoringSide + " valid=" + home.Valid
                      + " points=" + home.Points + " goalSlot=" + home.GoalSlot
                      + " scoreCode=" + home.ScoreCode + " home=" + home.HomeScore + " away=" + home.AwayScore
                      + " renderBone=" + home.RenderBone + " tickMs=" + home.TickMs);

            if (!home.Valid || home.ScoringSide != 0 || home.Points != 2 || home.HomeScore != 2 || home.AwayScore != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH home goal fixture");
            }

            int extra = MatchScoreFixture.DwellInMouth(sim.Sim, detector, home.RokPosition, 10, () => sim.Step());

            if (extra != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH debounce extra goals=" + extra);
            }

            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            sim.Step();
            detector.ResetDebounce();

            // Away goal: ROK value 1, north goal -> 1 point for the away side.
            MatchGoalEvent away = MatchScoreFixture.RunIntoGoal(sim.Sim, detector, false, 1, 170, 2,
                () => sim.Step());

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_GOAL side=" + away.ScoringSide + " valid=" + away.Valid
                      + " points=" + away.Points + " goalSlot=" + away.GoalSlot
                      + " scoreCode=" + away.ScoreCode + " home=" + away.HomeScore + " away=" + away.AwayScore
                      + " renderBone=" + away.RenderBone + " tickMs=" + away.TickMs);

            if (!away.Valid || away.ScoringSide != 1 || away.Points != 1 || away.HomeScore != 2 || away.AwayScore != 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH away goal fixture");
            }

            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            MatchHudModel hudModel = MatchHudModel.From(world, sim.Sim);
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_HUD " + hudModel.Describe());

            if (hudModel.HomeScoreText != "2" || hudModel.AwayScoreText != "1"
                || hudModel.Period != 1 || !hudModel.ClockText.Contains(":")
                || hudModel.RokText != "x0")
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH HUD model " + hudModel.Describe());
            }

            bool carrierOk = MatchScoreFixture.CheckCarrierFollow(sim, MatchSimulation.HumanSlot,
                new Vec3(0.0f, 0.0f, 2.0f), () => sim.Step(),
                out float carrierDistance, out bool carrierCarried);

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_CARRIER ok=" + carrierOk
                      + " carried=" + carrierCarried
                      + " distance=" + carrierDistance.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture));

            if (!carrierOk)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH carrier follow");
            }

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_MISMATCH 20XX process running");
            }

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CHECK_COUNTS rokTriangles=" + rokData.TriangleCount
                      + " rokBones=" + rokData.BoneCount
                      + " hudSprites=5"
                      + " homeScore=" + sim.Sim.Rules.ScoreHome
                      + " awayScore=" + sim.Sim.Rules.ScoreAway
                      + " goals=" + detector.GoalCount
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static int CountTwentyXxProcesses()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("hyperblade_20XX");

                return processes == null ? 0 : processes.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // --- editor capture -----------------------------------------------------------------

        private static void CaptureAll(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureScene();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_GAMEPLAY_CAPTURES_OK frames=" + CaptureFiles.Length
                  + " width=" + CaptureWidth + " height=" + CaptureHeight + " score=2-1"
                : "UBLADE3_NATIVE_GAMEPLAY_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureScene()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native Match Gameplay");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();

            scene.Build();
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_DROME " + scene.World.Drome.Describe());
            gameplay.Build(scene);

            string[] paths = new string[CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            failures += gameplay.RunDeterministicSuite(paths);

            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(gameplay.Describe());
            Debug.Log(MatchHudModel.From(scene.World, scene.Sim.Sim).Describe());

            for (int i = 0; i < gameplay.Captures.Count; i++)
            {
                MatchGameplayCapture capture = gameplay.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }

                if (capture.Width != CaptureWidth || capture.Height != CaptureHeight)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED " + capture.Path + " size "
                                   + capture.Width + "x" + capture.Height);
                }
            }

            if (gameplay.Captures.Count != CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED captured " + gameplay.Captures.Count
                               + " of " + CaptureFiles.Length + " frames");
            }

            if (gameplay.FinalHomeScore != 2 || gameplay.FinalAwayScore != 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_FAILED final score "
                               + gameplay.FinalHomeScore + "-" + gameplay.FinalAwayScore);
            }

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CAPTURE_EVIDENCE frames=" + gameplay.Captures.Count
                      + " score=" + gameplay.FinalHomeScore + "-" + gameplay.FinalAwayScore
                      + " goals=" + gameplay.GoalsScored
                      + " hud=\"" + (gameplay.HudModel == null ? string.Empty : gameplay.HudModel.Describe()) + "\""
                      + " no20xx=True");

            return failures;
        }

        // --- player build -------------------------------------------------------------------

        private static void BuildWindowsPlayer(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_BUILD_OK output=" + BuildPlayer());
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static string BuildPlayer()
        {
            string playerPath = ResolvePlayerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(playerPath));

            try
            {
                UnityEngine.SceneManagement.Scene scene =
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                GameObject root = new GameObject("UBlade3 Native Match Gameplay");
                root.AddComponent<MatchScene>();
                root.AddComponent<MatchGameplay>();

                Material previewMaterial = AssetDatabase.LoadAssetAtPath<Material>(PreviewMaterialPath);

                if (previewMaterial != null)
                {
                    GameObject anchor = new GameObject("OriginalSurfaceShaderAnchor");
                    MeshRenderer renderer = anchor.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = previewMaterial;
                    anchor.SetActive(false);
                }

                EditorSceneManager.SaveScene(scene, TempScenePath);
                AssetDatabase.Refresh();

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { TempScenePath },
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Windows player build result " + report.summary.result);
                }

                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_PLAYER_BUILD_OK bytes=" + report.summary.totalSize
                          + " output=" + report.summary.outputPath);

                return playerPath;
            }
            finally
            {
                if (File.Exists(Path.Combine(ProjectRoot, TempScenePath)))
                {
                    AssetDatabase.DeleteAsset(TempScenePath);
                    AssetDatabase.Refresh();
                }
            }
        }

        private static void BuildAndRunWindows(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-gameplay-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-gameplay-player.log"));

                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] labels = { "match-5s", "goal-home", "rok-plain", "rok-double", "rok-quad", "goal-away", "scoreboard" };
                string[] expected = new string[labels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-"
                                               + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
                                               + "-" + labels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + CaptureWidth + " -screen-height " + CaptureHeight
                                  + " -screen-fullscreen 0 --gameplay-capture=\"" + captureBase + "\""
                                  + " -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows gameplay player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows gameplay player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows gameplay player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_GAMEPLAY_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_GAMEPLAY_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " score=2-1 frameset=" + string.Join(",", labels)
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
