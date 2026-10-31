using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UBlade3.Assets;
using UBlade3.Audio;
using UBlade3.Native;
using UBlade3.PlanVM;
using UBlade3.Sim;
using Debug = UnityEngine.Debug;

namespace UBlade3.EditorTools
{
    // Editor gates for N6, the first truly playable native C# match loop: real pickup/carry, shot
    // power meter, ported PlanAction::Launch shot, exact PlanAction::Throw pass, free-ROK flight,
    // shipped goal rules and match-driven audio.
    //
    //   Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.Check -logFile <temp>\ublade3-play-check.log
    //
    //   Unity.exe -batchmode -quit -projectPath unity/UBlade3 --ublade3-audio \
    //       -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CaptureAll -logFile <temp>\ublade3-play-capture.log
    //
    // The capture runs need a graphics device (no -nographics). The built player is launched with
    // --play-capture and --ublade3-audio.
    public static class UBlade3MatchPlay
    {
        public const string CapturePrefix = "native-play";

        public const string PlayerExecutable = "build-match/UBlade3NativeMatchPlay.exe";

        public const int CaptureWidth = MatchPlay.CaptureWidth;

        public const int CaptureHeight = MatchPlay.CaptureHeight;

        private const string TempScenePath = "Assets/Scenes/__UBlade3NativePlayTemp.unity";

        private const string PreviewMaterialPath = "Assets/Scripts/UBlade3/Native/DromePreviewMaterial.mat";

        public static readonly string[] CaptureFiles =
        {
            "artifacts/native-play-00-start.png",
            "artifacts/native-play-01-pickup.png",
            "artifacts/native-play-02-charge.png",
            "artifacts/native-play-03-shot.png",
            "artifacts/native-play-04-goal.png",
            "artifacts/native-play-05-score.png",
            "artifacts/native-play-06-pass.png",
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

        [MenuItem("UBlade3/Native Play/Run Validation Check")]
        public static void CheckMenu()
        {
            Check(false);
        }

        [MenuItem("UBlade3/Native Play/Capture Playable Match Frames")]
        public static void CaptureAllMenu()
        {
            CaptureAll(false);
        }

        [MenuItem("UBlade3/Native Play/Build Windows Preview Player")]
        public static void BuildWindowsMenu()
        {
            BuildWindowsPlayer(false);
        }

        [MenuItem("UBlade3/Native Play/Build And Capture Windows Preview")]
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

        private static void Check(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += CheckPlay();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PLAY_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_PLAY_CHECK_OK"
                : "UBLADE3_NATIVE_PLAY_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int CheckPlay()
        {
            int mismatches = 0;
            OriginalAssetRoot root = MatchScene.ResolveRoot();

            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK root=" + root.DataPath
                      + " unity=" + Application.unityVersion + " no20xx=True");

            MatchWorldData world = MatchWorldData.Load(root);

            MatchRokData rokData = MatchRokData.Load(world);
            Debug.Log(rokData.Describe());

            if (rokData.Object.Definition.Model != "Rokmix" || rokData.MissingTextures.Count != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_PLAY_MISMATCH ROK model/textures");
            }

            MatchHudData hudData = MatchHudData.Load(world);
            Debug.Log(hudData.Describe());

            if (hudData.Missing.Count != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_PLAY_MISMATCH HUD art missing=" + hudData.Missing.Count);
            }

            int audioProbeMismatches = RunAudioProbe();
            mismatches += audioProbeMismatches;

            MatchPlayScriptResult first = RunScript(world, out MatchPlayControl firstControl, out MatchAudioDirector firstAudio);
            mismatches += first.Failures;

            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_RESULT approachTicks=" + first.ApproachTicks
                      + " pickups=" + first.Pickups
                      + " shots=" + first.Shots
                      + " passes=" + first.Passes
                      + " goals=" + first.Goals
                      + " bounces=" + first.Bounces
                      + " score=" + first.ScoreAfterHome + "-" + first.ScoreAfterAway
                      + " realFlightGoal=" + (first.GoalWasRealFlight ? "True" : "False")
                      + " failures=" + first.Failures
                      + " reason=" + first.FailureText);

            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_CONTROL " + firstControl.Describe());
            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_AUDIO " + firstAudio.Describe());

            if (first.Pickups < 2 || first.Shots != 1 || first.Passes != 1
                || first.Goals != 1 || first.ScoreAfterHome < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_PLAY_MISMATCH counts pickups=" + first.Pickups
                          + " shots=" + first.Shots + " passes=" + first.Passes
                          + " goals=" + first.Goals + " home=" + first.ScoreAfterHome);
            }

            if (firstAudio.GoalEvents != 1 || firstAudio.CrowdEvents != 1
                || firstAudio.ImpactEvents < 2 || firstAudio.RequestedEvents < 4)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_PLAY_MISMATCH audio routing " + firstAudio.Describe());
            }

            // Determinism: a second fresh world/sim must produce the same counts and score.
            MatchPlayScriptResult second = RunScript(world, out MatchPlayControl secondControl, out MatchAudioDirector secondAudio);

            if (second.Failures != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_PLAY_MISMATCH second run failures=" + second.Failures + " " + second.FailureText);
            }

            bool deterministic = first.Pickups == second.Pickups
                                 && first.Shots == second.Shots
                                 && first.Passes == second.Passes
                                 && first.Goals == second.Goals
                                 && first.ScoreAfterHome == second.ScoreAfterHome
                                 && first.ScoreAfterAway == second.ScoreAfterAway
                                 && firstAudio.GoalEvents == secondAudio.GoalEvents;

            Debug.Log("UBLADE3_NATIVE_PLAY_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " run1=" + first.Steps[0] + " run2=" + second.Steps[0]);

            if (!deterministic)
            {
                mismatches++;
            }

            bool bones;
            string boneDetail;
            bones = MatchScoreFixture.CheckRenderBoneMapping(firstControl.Sim, out boneDetail);
            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_RENDER_BONES ok=" + bones + " mapping=" + boneDetail);

            if (!bones)
            {
                mismatches++;
            }

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_PLAY_CHECK_COUNTS rokTriangles=" + rokData.TriangleCount
                      + " hudSprites=5"
                      + " audioProbeMismatches=" + audioProbeMismatches
                      + " pickups=" + first.Pickups
                      + " shots=" + first.Shots
                      + " passes=" + first.Passes
                      + " goals=" + first.Goals
                      + " bounces=" + first.Bounces
                      + " score=" + first.ScoreAfterHome + "-" + first.ScoreAfterAway
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static MatchPlayScriptResult RunScript(MatchWorldData world, out MatchPlayControl control,
            out MatchAudioDirector audio)
        {
            MatchSimulation sim = MatchSimulation.Create(world);

            for (int slot = 0; slot < sim.Sim.Players.Length; slot++)
            {
                sim.Sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            sim.Sim.Rules.SetHomeScore(0);
            sim.Sim.Rules.SetAwayScore(0);

            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            audio = new MatchAudioDirector(null);
            control = new MatchPlayControl(sim.Sim, config, audio);
            control.TraceFlight = true;

            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            control.ResetRokToSpawn();

            // The headless tick must go through the MatchSimulation wrapper: it maps the held mask to
            // the human slot and runs the AI fallback for the other slots, exactly like the scene.
            return MatchPlayScript.Run(control, null, held =>
            {
                sim.HumanInput = held;
                sim.Step();
            });
        }

        private static int RunAudioProbe()
        {
            string previous = Environment.GetEnvironmentVariable("HYPERBLADE_GAME_DATA");

            try
            {
                // AudioProbe resolves the install through GameDataRoot itself; point it at the same
                // install the match layer resolves so the run is not dependent on an ambient env var.
                Environment.SetEnvironmentVariable("HYPERBLADE_GAME_DATA", MatchScene.ResolveRoot().InstallPath);
                AudioProbe.Check();
                Debug.Log("UBLADE3_NATIVE_PLAY_AUDIO_PROBE ok=True");
                return 0;
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PLAY_AUDIO_PROBE ok=False " + exception.Message);
                return 1;
            }
            finally
            {
                Environment.SetEnvironmentVariable("HYPERBLADE_GAME_DATA", previous);
            }
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
                Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_PLAY_CAPTURES_OK frames=" + CaptureFiles.Length
                  + " width=" + CaptureWidth + " height=" + CaptureHeight
                : "UBLADE3_NATIVE_PLAY_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureScene()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native Playable Match");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();
            MatchPlay play = rootObject.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            string[] paths = new string[CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayScriptResult result;
            failures += play.RunDeterministicCapture(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(play.Describe());

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }

                if (capture.Width != CaptureWidth || capture.Height != CaptureHeight)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED " + capture.Path + " size "
                                   + capture.Width + "x" + capture.Height);
                }
            }

            if (play.Captures.Count != CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + CaptureFiles.Length);
            }

            // The playable loop's acceptance: real pickup/carry, one shot, one real flight goal, one
            // pass, score increased, audio events routed (played only when the run opted in).
            if (result.Pickups < 2 || result.Shots != 1 || result.Passes != 1
                || result.Goals != 1 || !result.GoalWasRealFlight || result.ScoreAfterHome < 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED script counts " + result.Steps[0]
                               + " reason=" + result.FailureText);
            }

            if (play.Audio.RequestedEvents < 4 || play.Audio.GoalEvents != 1
                || play.Audio.CrowdEvents != 1 || play.Audio.ImpactEvents < 2)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED audio " + play.Audio.Describe());
            }

            Debug.Log("UBLADE3_NATIVE_PLAY_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " pickups=" + result.Pickups
                      + " shots=" + result.Shots
                      + " passes=" + result.Passes
                      + " goals=" + result.Goals
                      + " bounces=" + result.Bounces
                      + " score=" + play.HomeScore + "-" + play.AwayScore
                      + " audio=\"" + play.Audio.Describe() + "\"");

            return failures;
        }

        // --- player build -------------------------------------------------------------------

        private static void BuildWindowsPlayer(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                Debug.Log("UBLADE3_NATIVE_PLAY_BUILD_OK output=" + BuildPlayer());
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PLAY_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static string BuildPlayer(string requestedPath = null)
        {
            string playerPath = string.IsNullOrEmpty(requestedPath) ? ResolvePlayerPath() : requestedPath;
            Directory.CreateDirectory(Path.GetDirectoryName(playerPath));

            try
            {
                UnityEngine.SceneManagement.Scene scene =
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                GameObject root = new GameObject("UBlade3 Native Playable Match");
                root.AddComponent<MatchScene>();
                root.AddComponent<MatchGameplay>();
                root.AddComponent<MatchPlay>();

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

                Debug.Log("UBLADE3_NATIVE_PLAY_PLAYER_BUILD_OK bytes=" + report.summary.totalSize
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
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-play-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-play-player.log"));
                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] expected = new string[MatchPlay.CaptureLabels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-" + MatchPlay.CaptureLabels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + CaptureWidth + " -screen-height " + CaptureHeight
                                  + " -screen-fullscreen 0 --play-capture=\"" + captureBase + "\""
                                  + " --ublade3-audio -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_PLAY_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows play player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows play player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows play player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_PLAY_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_PLAY_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PLAY_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
        // --- N7: playability follow-up ------------------------------------------------------
        // Post-goal celebration + face-off restart, the head-camera aim/shot path and fallback AI
        // possession. The N6 entry points above stay intact.

        public static readonly string[] N7CaptureFiles =
        {
            "artifacts/native-match-n7-00-start.png",
            "artifacts/native-match-n7-01-pickup.png",
            "artifacts/native-match-n7-02-charge.png",
            "artifacts/native-match-n7-03-headcam.png",
            "artifacts/native-match-n7-04-shot.png",
            "artifacts/native-match-n7-05-goal.png",
            "artifacts/native-match-n7-06-celebration.png",
            "artifacts/native-match-n7-07-faceoff.png",
            "artifacts/native-match-n7-08-restart.png",
            "artifacts/native-match-n7-09-ai-pickup.png",
            "artifacts/native-match-n7-10-ai-shot.png",
            "artifacts/native-match-n7-11-score.png",
        };

        // --- N8: real C# PlanVM-driven AI skater ------------------------------------------------

        public static readonly string[] N8CaptureFiles =
        {
            "artifacts/native-match-n8-00-start.png",
            "artifacts/native-match-n8-01-ai-pickup.png",
            "artifacts/native-match-n8-02-plan-bound.png",
            "artifacts/native-match-n8-03-passing-play.png",
            "artifacts/native-match-n8-04-plan-pass.png",
            "artifacts/native-match-n8-05-result.png",
        };

        // --- N9: whole-match PlanVM AI roster --------------------------------------------------

        public static readonly string[] N9CaptureFiles =
        {
            "artifacts/native-match-n9-00-start.png",
            "artifacts/native-match-n9-01-plans-bound.png",
            "artifacts/native-match-n9-02-ai-pickup.png",
            "artifacts/native-match-n9-03-plan-switch.png",
            "artifacts/native-match-n9-04-plan-pass.png",
            "artifacts/native-match-n9-05-result.png",
        };

        // --- N10: default whole-match PlanVM AI in the normal build -------------------------------

        public static readonly string[] N10CaptureFiles =
        {
            "artifacts/native-match-n10-00-start.png",
            "artifacts/native-match-n10-01-plans-active.png",
            "artifacts/native-match-n10-02-ai-pickup.png",
            "artifacts/native-match-n10-03-plan-switch.png",
            "artifacts/native-match-n10-04-plan-pass.png",
            "artifacts/native-match-n10-05-plan-shot.png",
            "artifacts/native-match-n10-06-result.png",
        };

        // --- N11: exact plan-target steering physics ------------------------------------------

        public static readonly string[] N11CaptureFiles =
        {
            "artifacts/native-match-n11-00-start.png",
            "artifacts/native-match-n11-01-plan-physics.png",
            "artifacts/native-match-n11-02-target-close.png",
            "artifacts/native-match-n11-03-ai-pickup.png",
            "artifacts/native-match-n11-04-target-change.png",
            "artifacts/native-match-n11-05-result.png",
        };

        public static void CheckN11()
        {
            CheckN11(true);
        }

        public static void CaptureAllN11()
        {
            CaptureAllN11(true);
        }

        public static void CheckN10()
        {
            CheckN10(true);
        }

        public static void CaptureAllN10()
        {
            CaptureAllN10(true);
        }

        public static void BuildAndRunWindowsN10()
        {
            BuildAndRunWindowsN10(true);
        }

        public static void CheckN9()
        {
            CheckN9(true);
        }

        public static void CaptureAllN9()
        {
            CaptureAllN9(true);
        }

        public static void BuildAndRunWindowsN9()
        {
            BuildAndRunWindowsN9(true);
        }

        public static void CheckN8()
        {
            CheckN8(true);
        }

        public static void CaptureAllN8()
        {
            CaptureAllN8(true);
        }

        public static void BuildAndRunWindowsN8()
        {
            BuildAndRunWindowsN8(true);
        }

        public static void CheckN7()
        {
            CheckN7(true);
        }

        public static void CaptureAllN7()
        {
            CaptureAllN7(true);
        }

        public static void BuildAndRunWindowsN7()
        {
            BuildAndRunWindowsN7(true);
        }

        private static void CheckN7(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN7Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N7_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N7_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN7Check()
        {
            int mismatches = 0;
            MatchWorldData world = MatchWorldData.Load(MatchScene.ResolveRoot());

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_CHECK root=" + MatchScene.ResolveRoot().DataPath
                      + " unity=" + Application.unityVersion + " no20xx=True");

            MatchRokData rokData = MatchRokData.Load(world);
            MatchHudData hudData = MatchHudData.Load(world);

            if (rokData.Object.Definition.Model != "Rokmix" || rokData.MissingTextures.Count != 0
                || hudData.Missing.Count != 0)
            {
                mismatches++;
            }

            int audioProbe = RunAudioProbe();
            mismatches += audioProbe;

            MatchPlayN7ScriptResult first;
            MatchPlayControl firstControl;
            MatchSimulation firstSim;
            RunN7Script(world, out first, out firstControl, out firstSim);

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_CHECK_RESULT " + first.Steps[0]
                      + " failures=" + first.Failures + " reason=" + first.FailureText);
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_FACE_OFF " + MatchFaceOff.Describe());
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_CONTROL " + firstControl.Describe());
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_AUDIO " + firstControl.Audio.Describe());

            if (first.Failures != 0 || first.HumanPickups < 1 || first.HumanShots < 1
                || first.HumanGoals < 1 || first.AiPickups < 1 || first.AiShots < 1
                || first.FaceOffSetupTick == 0 || first.FaceOffResumeTick <= first.FaceOffSetupTick)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N7_MISMATCH played result "
                          + first.FailureText);
            }

            MatchPlayN7ScriptResult second;
            MatchPlayControl secondControl;
            MatchSimulation secondSim;
            RunN7Script(world, out second, out secondControl, out secondSim);

            if (second.Failures != 0)
            {
                mismatches++;
            }

            bool deterministic = first.HumanPickups == second.HumanPickups
                                 && first.HumanShots == second.HumanShots
                                 && first.HumanGoals == second.HumanGoals
                                 && first.AiPickups == second.AiPickups
                                 && first.AiShots == second.AiShots
                                 && first.AiPasses == second.AiPasses
                                 && first.ScoreHome == second.ScoreHome
                                 && first.ScoreAway == second.ScoreAway
                                 && Math.Abs(first.FaceOffResumeTick - second.FaceOffResumeTick) == 0;

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " run1=" + first.Steps[0] + " run2=" + second.Steps[0]);

            if (!deterministic)
            {
                mismatches++;
            }

            string aimDetail;
            bool aimOk = MatchPlayN7Script.HeadCamAimCheck(world, out aimDetail);
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_HEAD_CAM_AIM ok=" + (aimOk ? "True" : "False")
                      + " " + aimDetail);

            if (!aimOk)
            {
                mismatches++;
            }

            string passDetail;
            bool passOk = MatchPlayN7Script.AiPassCheck(world, out passDetail);
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_AI_PASS ok=" + (passOk ? "True" : "False")
                      + " " + passDetail);

            if (!passOk)
            {
                mismatches++;
            }

            MusicCheck(firstControl);

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_COUNTS humanPickups=" + first.HumanPickups
                      + " humanShots=" + first.HumanShots + " humanGoals=" + first.HumanGoals
                      + " aiPickups=" + first.AiPickups + " aiShots=" + first.AiShots
                      + " aiPasses=" + first.AiPasses + " goals=" + first.Goals
                      + " score=" + first.ScoreHome + "-" + first.ScoreAway
                      + " headCamFovDegrees=" + first.HeadCamFovDegrees
                      + " faceOffSetupTick=" + first.FaceOffSetupTick
                      + " faceOffResumeTick=" + first.FaceOffResumeTick
                      + " audioProbeMismatches=" + audioProbe
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static void MusicCheck(MatchPlayControl control)
        {
            // Silence-aware evidence that the goal/crowd/impact routing still ran through the real
            // event path; the Editor and player runs opt into actual playback separately.
            if (control.Audio.GoalEvents < 1 || control.Audio.CrowdEvents < 1
                || control.Audio.ImpactEvents < 3)
            {
                Debug.LogWarning("UBLADE3_NATIVE_MATCH_N7_AUDIO_MISMATCH " + control.Audio.Describe());
            }
        }

        private static void RunN7Script(MatchWorldData world, out MatchPlayN7ScriptResult result,
            out MatchPlayControl control, out MatchSimulation sim)
        {
            sim = MatchSimulation.Create(world);

            for (int slot = 0; slot < sim.Sim.Players.Length; slot++)
            {
                sim.Sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            sim.Sim.Rules.SetHomeScore(0);
            sim.Sim.Rules.SetAwayScore(0);

            MatchPlayConfig config = MatchPlayConfig.FromWorld(world, MatchSimulation.HumanSlot);
            MatchAudioDirector audio = new MatchAudioDirector(null);
            MatchPlayControl localControl = new MatchPlayControl(sim.Sim, config, audio);
            localControl.AttachAi(ObjectConstants.SlotDromer1, ObjectConstants.SlotDromer2);
            sim.Sim.InputForSlot = slot => localControl.InputForSlot(slot);

            MatchScoreFixture.PlaceRokAtSpawn(sim.Sim);
            localControl.ResetRokToSpawn();
            control = localControl;

            MatchSimulation localSim = sim;
            result = MatchPlayN7Script.Run(localControl, null, held => localSim.Step());
            sim = localSim;
        }

        private static void CaptureAllN7(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureSceneN7();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N7_CAPTURES_OK frames=" + N7CaptureFiles.Length
                  + " width=" + MatchPlay.CaptureWidth + " height=" + MatchPlay.CaptureHeight
                : "UBLADE3_NATIVE_MATCH_N7_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureSceneN7()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native N7 Match");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();
            MatchPlay play = rootObject.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            string[] paths = new string[N7CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(N7CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayN7ScriptResult result;
            failures += play.RunDeterministicCaptureN7(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(play.Describe());

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }
            }

            if (play.Captures.Count != N7CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + N7CaptureFiles.Length);
            }

            if (result.Failures != 0 || result.HumanShots < 1 || result.HumanGoals < 1
                || result.AiPickups < 1 || result.AiShots < 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED script " + result.Steps[0]
                               + " reason=" + result.FailureText);
            }

            if (play.Audio.GoalEvents < 1 || play.Audio.CrowdEvents < 1 || play.Audio.ImpactEvents < 3)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED audio " + play.Audio.Describe());
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " humanPickups=" + result.HumanPickups + " humanShots=" + result.HumanShots
                      + " humanGoals=" + result.HumanGoals + " aiPickups=" + result.AiPickups
                      + " aiShots=" + result.AiShots + " aiPasses=" + result.AiPasses
                      + " score=" + play.HomeScore + "-" + play.AwayScore
                      + " headCamFovDegrees=" + result.HeadCamFovDegrees
                      + " audio=\"" + play.Audio.Describe() + "\"");

            return failures;
        }

        private static void BuildAndRunWindowsN7(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-match-n7-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-match-n7-player.log"));
                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] expected = new string[MatchPlay.N7CaptureLabels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-" + MatchPlay.N7CaptureLabels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + MatchPlay.CaptureWidth
                                  + " -screen-height " + MatchPlay.CaptureHeight
                                  + " -screen-fullscreen 0 --n7-capture=\"" + captureBase + "\""
                                  + " --ublade3-audio -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_MATCH_N7_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows N7 player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows N7 player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows N7 player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_MATCH_N7_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_N7_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // --- N8: real C# PlanVM-driven AI skater ------------------------------------------------
        // The headless gate loads the shipped CARRYROK plan through UBlade3.PlanVM, instantiates it
        // for one AI skater, runs the deterministic pickup -> TurnToMate -> PassingPlay -> plan pass
        // scenario twice, compares the plan trace digests, and contrasts it with the N7 heuristic
        // controller. The label stays honest: this is one bounded plan-driven behaviour path, not
        // full plan-VM AI parity.

        private static void CheckN8(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN8Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N8_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N8_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN8Check()
        {
            int mismatches = 0;
            OriginalAssetRoot root = MatchScene.ResolveRoot();
            MatchWorldData world = MatchWorldData.Load(root);
            string dataPath = root.DataPath;

            Debug.Log("UBLADE3_NATIVE_MATCH_N8_CHECK root=" + dataPath
                      + " unity=" + Application.unityVersion + " no20xx=True");

            int audioProbe = RunAudioProbe();
            mismatches += audioProbe;

            MatchPlayN8ScriptResult first = RunN8Scenario(world, dataPath);

            Debug.Log(first.PlanImpl);
            Debug.Log(first.PlanOpcodeLine);
            Debug.Log(first.PlanDescribe);

            for (int index = 0; index < first.Trace.Count && index < 120; index++)
            {
                Debug.Log("UBLADE3_NATIVE_MATCH_N8_TRACE " + first.Trace[index]);
            }
            Debug.Log("UBLADE3_NATIVE_MATCH_N8_CHECK_RESULT aiPickups=" + first.AiPickups
                      + " aiPasses=" + first.AiPasses + " aiShots=" + first.AiShots
                      + " planBoundTick=" + first.PlanBoundTick
                      + " passingPlayTick=" + first.PassingPlayTick
                      + " planPassTick=" + first.PlanPassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " planBehaviorChanges=" + first.PlanBehaviorChanges
                      + " planTargetChanges=" + first.PlanTargetChanges
                      + " planTicks=" + first.PlanTicks
                      + " fallbackMotionTicks=" + first.PlanFallbackMotionTicks
                      + " unsupported=" + first.UnsupportedCount
                      + " planDigest=" + first.PlanDigest
                      + " failures=" + first.Failures + " reason=" + first.FailureText);

            if (first.Failures != 0 || first.AiPickups != 1 || first.AiPasses != 1
                || first.PlanPassRequests < 1 || first.PlanBehaviorChanges < 2
                || first.PlanTargetChanges < 1 || first.UnsupportedCount < 1
                || first.PlanTicks < 1
                || !first.BehaviorPath.Contains("TurnToMate")
                || !first.BehaviorPath.Contains("PassingPlay"))
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N8_MISMATCH plan result path="
                          + string.Join(">", first.BehaviorPath.ToArray())
                          + " reason=" + first.FailureText);
            }

            if (first.AiShots != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N8_MISMATCH plan slot took heuristic shots=" + first.AiShots);
            }

            MatchPlayN8ScriptResult second = RunN8Scenario(world, dataPath);
            bool deterministic = string.Equals(first.PlanDigest, second.PlanDigest, StringComparison.Ordinal)
                                 && first.AiPasses == second.AiPasses
                                 && first.AiPickups == second.AiPickups
                                 && first.AiShots == second.AiShots
                                 && first.PlanPassRequests == second.PlanPassRequests
                                 && first.PlanBehaviorChanges == second.PlanBehaviorChanges
                                 && Math.Abs(first.PlanPassTick - second.PlanPassTick) == 0;

            Debug.Log("UBLADE3_NATIVE_MATCH_N8_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " digest1=" + first.PlanDigest + " digest2=" + second.PlanDigest);

            if (!deterministic)
            {
                mismatches++;
            }

            int heuristicPickups;
            int heuristicShots;
            int heuristicPasses;
            string comparison = MatchPlayN8Script.CompareHeuristic(world, dataPath,
                out heuristicPickups, out heuristicShots, out heuristicPasses);

            Debug.Log("UBLADE3_NATIVE_MATCH_N8_COMPARISON " + comparison
                      + " | plan: aiPasses=" + first.AiPasses
                      + " planBehaviorChanges=" + first.PlanBehaviorChanges
                      + " planPassRequests=" + first.PlanPassRequests
                      + " planDigest=" + first.PlanDigest);

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_MATCH_N8_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N8_COUNTS aiPickups=" + first.AiPickups
                      + " aiPasses=" + first.AiPasses + " aiShots=" + first.AiShots
                      + " planBoundTick=" + first.PlanBoundTick
                      + " passingPlayTick=" + first.PassingPlayTick
                      + " planPassTick=" + first.PlanPassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " planBehaviorChanges=" + first.PlanBehaviorChanges
                      + " planTargetChanges=" + first.PlanTargetChanges
                      + " planTicks=" + first.PlanTicks
                      + " unsupported=" + first.UnsupportedCount
                      + " unsupportedIds=[" + first.UnsupportedIds + "]"
                      + " planDigest=" + first.PlanDigest
                      + " heuristicPickups=" + heuristicPickups
                      + " heuristicShots=" + heuristicShots
                      + " heuristicPasses=" + heuristicPasses
                      + " audioProbeMismatches=" + audioProbe
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static MatchPlayN8ScriptResult RunN8Scenario(MatchWorldData world, string dataPath)
        {
            MatchSimulation sim;
            MatchPlayControl control;
            MatchAudioDirector audio;
            MatchPlayN8Script.CreateScenario(world, dataPath, out sim, out control, out audio);

            return MatchPlayN8Script.Run(control, dataPath, null, held => sim.Sim.Tick());
        }

        private static void CaptureAllN8(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureSceneN8();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N8_CAPTURES_OK frames=" + N8CaptureFiles.Length
                  + " width=" + MatchPlay.CaptureWidth + " height=" + MatchPlay.CaptureHeight
                : "UBLADE3_NATIVE_MATCH_N8_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureSceneN8()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native N8 Plan AI Match");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();
            MatchPlay play = rootObject.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            string[] paths = new string[N8CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(N8CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayN8ScriptResult result;
            failures += play.RunDeterministicCaptureN8(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(play.Describe());
            Debug.Log(result.PlanImpl);
            Debug.Log(result.PlanDescribe);

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }
            }

            if (play.Captures.Count != N8CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + N8CaptureFiles.Length);
            }

            if (result.Failures != 0 || result.AiPickups != 1 || result.AiPasses != 1
                || result.PlanPassRequests < 1 || result.PlanBehaviorChanges < 2)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N8_CAPTURE_FAILED script path="
                               + string.Join(">", result.BehaviorPath.ToArray())
                               + " reason=" + result.FailureText);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N8_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " aiPickups=" + result.AiPickups + " aiPasses=" + result.AiPasses
                      + " aiShots=" + result.AiShots
                      + " planBoundTick=" + result.PlanBoundTick
                      + " passingPlayTick=" + result.PassingPlayTick
                      + " planPassTick=" + result.PlanPassTick
                      + " planPassRequests=" + result.PlanPassRequests
                      + " planBehaviorChanges=" + result.PlanBehaviorChanges
                      + " planTargetChanges=" + result.PlanTargetChanges
                      + " unsupported=" + result.UnsupportedCount
                      + " planDigest=" + result.PlanDigest);

            return failures;
        }

        private static void BuildAndRunWindowsN8(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-match-n8-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-match-n8-player.log"));
                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] expected = new string[MatchPlay.N8CaptureLabels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-" + MatchPlay.N8CaptureLabels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + MatchPlay.CaptureWidth
                                  + " -screen-height " + MatchPlay.CaptureHeight
                                  + " -screen-fullscreen 0 --n8-capture=\"" + captureBase + "\""
                                  + " -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_MATCH_N8_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows N8 player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows N8 player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows N8 player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_MATCH_N8_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_N8_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N8_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // --- N9: whole-match PlanVM AI roster --------------------------------------------------
        // The headless gate runs the deterministic roster scenario twice: all five non-human slots
        // (three AI skaters + two goalies) run their shipped plan tables, the carrier plan changes from
        // Defense to CARRYROK and passes, LostIt changes it back, and every slot reports its own plan id,
        // behaviour path, ticks, actions and decisions. Determinism, active-slot count, relay/rebind
        // bounds and unsupported-handler telemetry are all checked; this remains a bounded slice, not
        // full plan-VM AI parity.

        private static string JoinIds(int[] ids)
        {
            string[] parts = new string[ids.Length];

            for (int index = 0; index < ids.Length; index++)
            {
                parts[index] = ids[index].ToString();
            }

            return string.Join(",", parts);
        }

        private static void CheckN9(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN9Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N9_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N9_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN9Check()
        {
            int mismatches = 0;
            OriginalAssetRoot root = MatchScene.ResolveRoot();
            MatchWorldData world = MatchWorldData.Load(root);
            string dataPath = root.DataPath;

            Debug.Log("UBLADE3_NATIVE_MATCH_N9_CHECK root=" + dataPath
                      + " unity=" + Application.unityVersion + " no20xx=True");
            mismatches += RunAudioProbe();

            int[] planDecisions = MatchPlanAiDecisions.ImplementedIds();
            int[] planActions = PlanAiActions.ImplementedIds();

            Debug.Log("UBLADE3_PLAN_AI_N9_IMPL planDecisions=" + JoinIds(planDecisions)
                      + " planActions=" + JoinIds(planActions)
                      + " planTables=ObjectDefinition.Plans"
                      + " initial=Defense:0,Goalie:0 carrier=CARRYROK mapped=1->5");

            MatchPlayN9ScriptResult first = RunN9Scenario(world, dataPath);

            Debug.Log(first.RosterDescribe);
            Debug.Log(first.RosterSlots);

            for (int index = 0; index < first.Trace.Count && index < 60; index++)
            {
                Debug.Log("UBLADE3_NATIVE_MATCH_N9_TRACE " + first.Trace[index]);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N9_CHECK_RESULT aiPickups=" + first.AiPickups
                      + " aiPasses=" + first.AiPasses + " aiShots=" + first.AiShots
                      + " pickupTick=" + first.PickupTick + " passTick=" + first.PassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " planShotRequests=" + first.PlanShotRequests
                      + " maxActiveSlots=" + first.MaxActiveSlots
                      + " planIds=[" + first.PlanIdList + "]"
                      + " rebinds=" + first.Rebinds
                      + " maxRebindsPerSlot=" + first.MaxRebindsPerSlot
                      + " relays=" + first.Relays
                      + " relayBudgetHits=" + first.RelayBudgetHits
                      + " possessionEvents=" + first.PossessionEvents
                      + " unsupportedDecisions=" + first.UnsupportedDecisions
                      + " ticks=" + first.TicksRun
                      + " planDigest=" + first.Digest
                      + " failures=" + first.Failures + " reason=" + first.FailureText);

            if (first.Failures != 0 || first.AiPickups < 1 || first.AiPasses < 1
                || first.PlanPassRequests < 1 || first.MaxActiveSlots != MatchPlayN9Script.PlanSlots.Length
                || first.PlanIds < 2 || first.Rebinds < 2
                || first.MaxRebindsPerSlot > MatchPlayN9Script.MaxRebindsPerSlot
                || first.RelayBudgetHits != 0 || first.Relays < 1
                || first.TicksRun > MatchPlayN9Script.MaxTicks
                || string.IsNullOrEmpty(first.Digest))
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N9_MISMATCH roster=" + first.RosterDescribe
                          + " reason=" + first.FailureText);
            }

            string ids = "," + first.PlanIdList + ",";

            if (ids.IndexOf(",Defense,", StringComparison.OrdinalIgnoreCase) < 0 ||
                ids.IndexOf(",CarryRok,", StringComparison.OrdinalIgnoreCase) < 0 ||
                ids.IndexOf(",Goalie,", StringComparison.OrdinalIgnoreCase) < 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N9_MISMATCH plan id coverage=[" + first.PlanIdList + "]");
            }

            MatchPlayN9ScriptResult second = RunN9Scenario(world, dataPath);
            bool deterministic = string.Equals(first.Digest, second.Digest, StringComparison.Ordinal)
                                 && first.AiPasses == second.AiPasses
                                 && first.AiPickups == second.AiPickups
                                 && first.PlanPassRequests == second.PlanPassRequests
                                 && first.MaxActiveSlots == second.MaxActiveSlots
                                 && first.Rebinds == second.Rebinds
                                 && first.Relays == second.Relays;

            Debug.Log("UBLADE3_NATIVE_MATCH_N9_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " digest1=" + first.Digest + " digest2=" + second.Digest);

            if (!deterministic)
            {
                mismatches++;
            }

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_MATCH_N9_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N9_COUNTS aiPickups=" + first.AiPickups
                      + " aiPasses=" + first.AiPasses + " aiShots=" + first.AiShots
                      + " pickupTick=" + first.PickupTick + " passTick=" + first.PassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " maxActiveSlots=" + first.MaxActiveSlots
                      + " planIds=[" + first.PlanIdList + "]"
                      + " rebinds=" + first.Rebinds
                      + " maxRebindsPerSlot=" + first.MaxRebindsPerSlot
                      + " relays=" + first.Relays
                      + " relayBudgetHits=" + first.RelayBudgetHits
                      + " possessionEvents=" + first.PossessionEvents
                      + " unsupportedDecisions=" + first.UnsupportedDecisions
                      + " ticks=" + first.TicksRun
                      + " planDigest=" + first.Digest
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static MatchPlayN9ScriptResult RunN9Scenario(MatchWorldData world, string dataPath)
        {
            MatchSimulation sim;
            MatchPlayControl control;
            MatchAudioDirector audio;
            MatchPlayN9Script.CreateScenario(world, dataPath, out sim, out control, out audio);

            return MatchPlayN9Script.Run(control, world, dataPath, null, held => sim.Sim.Tick());
        }

        private static void CaptureAllN9(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureSceneN9();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N9_CAPTURES_OK frames=" + N9CaptureFiles.Length
                  + " width=" + MatchPlay.CaptureWidth + " height=" + MatchPlay.CaptureHeight
                : "UBLADE3_NATIVE_MATCH_N9_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureSceneN9()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native N9 Plan AI Roster");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();
            MatchPlay play = rootObject.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            string[] paths = new string[N9CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(N9CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayN9ScriptResult result;
            failures += play.RunDeterministicCaptureN9(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(play.Describe());
            Debug.Log(result.RosterDescribe);
            Debug.Log(result.RosterSlots);

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }
            }

            if (play.Captures.Count != N9CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + N9CaptureFiles.Length);
            }

            if (result.Failures != 0 || result.AiPickups < 1 || result.AiPasses < 1
                || result.MaxActiveSlots != MatchPlayN9Script.PlanSlots.Length
                || result.PlanPassRequests < 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N9_CAPTURE_FAILED roster=" + result.RosterDescribe
                               + " reason=" + result.FailureText);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N9_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " aiPickups=" + result.AiPickups + " aiPasses=" + result.AiPasses
                      + " aiShots=" + result.AiShots
                      + " planPassRequests=" + result.PlanPassRequests
                      + " maxActiveSlots=" + result.MaxActiveSlots
                      + " planIds=[" + result.PlanIdList + "]"
                      + " rebinds=" + result.Rebinds
                      + " relays=" + result.Relays
                      + " unsupportedDecisions=" + result.UnsupportedDecisions
                      + " planDigest=" + result.Digest);

            return failures;
        }

        private static void BuildAndRunWindowsN9(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-match-n9-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-match-n9-player.log"));
                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] expected = new string[MatchPlay.N9CaptureLabels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-" + MatchPlay.N9CaptureLabels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + MatchPlay.CaptureWidth
                                  + " -screen-height " + MatchPlay.CaptureHeight
                                  + " -screen-fullscreen 0 --n9-capture=\"" + captureBase + "\""
                                  + " -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_MATCH_N9_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows N9 player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows N9 player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows N9 player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_MATCH_N9_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_N9_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N9_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // --- N10: default whole-match PlanVM AI in the normal build -------------------------------

        private static void CheckN10(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN10Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N10_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N10_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN10Check()
        {
            int mismatches = 0;
            OriginalAssetRoot root = MatchScene.ResolveRoot();
            MatchWorldData world = MatchWorldData.Load(root);

            Debug.Log("UBLADE3_NATIVE_MATCH_N10_CHECK root=" + root.DataPath
                      + " unity=" + Application.unityVersion + " captureFlag=False no20xx=True");
            mismatches += RunAudioProbe();

            MatchPlay firstPlay;
            int firstFailures = RunN10DefaultScenario(out firstPlay, out MatchPlayN10ScriptResult first);
            mismatches += firstFailures;

            Debug.Log(first.RosterDescribe);
            Debug.Log(first.RosterSlots);
            Debug.Log("UBLADE3_NATIVE_MATCH_N10_CHECK_RESULT defaultPlanAi="
                      + (firstPlay.Control != null && firstPlay.Control.DefaultPlanAi ? "True" : "False")
                      + " aiPickups=" + first.AiPickups + " aiPasses=" + first.AiPasses
                      + " aiShots=" + first.AiShots
                      + " pickupTick=" + first.PickupTick + " passTick=" + first.PassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " planShotRequests=" + first.PlanShotRequests
                      + " maxActiveSlots=" + first.MaxActiveSlots
                      + " activeAtEnd=" + first.ActiveAtEnd
                      + " planIds=[" + first.PlanIdList + "]"
                      + " rebinds=" + first.Rebinds
                      + " relays=" + first.Relays
                      + " relayBudgetHits=" + first.RelayBudgetHits
                      + " unsupportedDecisions=" + first.UnsupportedDecisions
                      + " heuristicFallbackTicks=" + first.HeuristicFallbackTicks
                      + " suppressedUpdateTicks=" + first.SuppressedUpdateTicks
                      + " ticks=" + first.TicksRun
                      + " planDigest=" + first.Digest
                      + " failures=" + first.Failures + " reason=" + first.FailureText);

            bool defaultWired = firstPlay.Control != null && firstPlay.Control.DefaultPlanAi
                                && firstPlay.Control.PlanRoster != null;

            if (first.Failures != 0 || !defaultWired || first.AiPickups < 1 || first.AiPasses < 1
                || first.PlanPassRequests < 1 || first.MaxActiveSlots != MatchPlayN10Script.PlanSlots.Length
                || first.PlanIds < 2 || first.Rebinds < 1
                || first.HeuristicFallbackTicks > MatchPlayN10Script.PlanSlots.Length * 2
                || first.SuppressedUpdateTicks < first.TicksRun
                || first.RelayBudgetHits != 0 || first.TicksRun > MatchPlayN10Script.MaxTicks
                || string.IsNullOrEmpty(first.Digest))
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N10_MISMATCH defaultWired=" + defaultWired
                          + " roster=" + first.RosterDescribe + " reason=" + first.FailureText);
            }

            string ids = "," + first.PlanIdList + ",";

            if (ids.IndexOf(",Defense,", StringComparison.OrdinalIgnoreCase) < 0 ||
                ids.IndexOf(",CarryRok,", StringComparison.OrdinalIgnoreCase) < 0 ||
                ids.IndexOf(",Goalie,", StringComparison.OrdinalIgnoreCase) < 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N10_MISMATCH plan id coverage=[" + first.PlanIdList + "]");
            }

            MatchPlay secondPlay;
            int secondFailures = RunN10DefaultScenario(out secondPlay, out MatchPlayN10ScriptResult second);
            mismatches += secondFailures;

            bool deterministic = string.Equals(first.Digest, second.Digest, StringComparison.Ordinal)
                                 && first.AiPasses == second.AiPasses
                                 && first.AiPickups == second.AiPickups
                                 && first.PlanPassRequests == second.PlanPassRequests
                                 && first.MaxActiveSlots == second.MaxActiveSlots
                                 && first.Rebinds == second.Rebinds
                                 && first.HeuristicFallbackTicks == second.HeuristicFallbackTicks;

            Debug.Log("UBLADE3_NATIVE_MATCH_N10_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " digest1=" + first.Digest + " digest2=" + second.Digest);

            if (!deterministic)
            {
                mismatches++;
            }

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_MATCH_N10_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N10_COUNTS defaultPlanAi=" + (defaultWired ? "True" : "False")
                      + " aiPickups=" + first.AiPickups + " aiPasses=" + first.AiPasses
                      + " aiShots=" + first.AiShots
                      + " pickupTick=" + first.PickupTick + " passTick=" + first.PassTick
                      + " planPassRequests=" + first.PlanPassRequests
                      + " maxActiveSlots=" + first.MaxActiveSlots
                      + " activeAtEnd=" + first.ActiveAtEnd
                      + " planIds=[" + first.PlanIdList + "]"
                      + " rebinds=" + first.Rebinds
                      + " relays=" + first.Relays
                      + " relayBudgetHits=" + first.RelayBudgetHits
                      + " unsupportedDecisions=" + first.UnsupportedDecisions
                      + " heuristicFallbackTicks=" + first.HeuristicFallbackTicks
                      + " suppressedUpdateTicks=" + first.SuppressedUpdateTicks
                      + " ticks=" + first.TicksRun
                      + " planDigest=" + first.Digest
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static int RunN10DefaultScenario(out MatchPlay play, out MatchPlayN10ScriptResult result)
        {
            int failures = 0;
            play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);

            MatchPlanAiRoster roster = play.Control == null ? null : play.Control.PlanRoster;

            if (play.Control == null || roster == null)
            {
                failures++;
                result = new MatchPlayN10ScriptResult();
                result.Failures = 1;
                result.FailureText = "normal build did not attach the default PlanVM AI roster; ";
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CHECK_FAILED " + result.FailureText);

                return failures;
            }

            failures += play.RunDeterministicCaptureN10(null, out result);

            return failures;
        }

        private static void DisableLegacyN13Handlers(MatchPlay play)
        {
            if (play != null && play.Control != null && play.Control.PlanRoster != null)
            {
                // N13 handlers change plan semantics; the pinned N4-N12 deterministic evidence keeps
                // the N12 handler set and the legacy bridge exactly as recorded.
                play.Control.PlanRoster.EnableN13Handlers(false);
                PlanAiMatchDecisionsN14.Enabled = false;
            }
        }

        private static MatchPlay CreateDefaultPlayScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native N10 Default Plan AI Match");
            MatchScene scene = rootObject.AddComponent<MatchScene>();
            MatchGameplay gameplay = rootObject.AddComponent<MatchGameplay>();
            MatchPlay play = rootObject.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            return play;
        }

        private static void CaptureAllN10(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureSceneN10();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N10_CAPTURES_OK frames=" + N10CaptureFiles.Length
                  + " width=" + MatchPlay.CaptureWidth + " height=" + MatchPlay.CaptureHeight
                : "UBLADE3_NATIVE_MATCH_N10_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureSceneN10()
        {
            int failures = 0;
            MatchPlay play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED the normal build did not attach the default PlanVM AI");
            }

            string[] paths = new string[N10CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(N10CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayN10ScriptResult result;
            failures += play.RunDeterministicCaptureN10(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(play.Scene));
            Debug.Log(play.Describe());
            Debug.Log(result.RosterDescribe);
            Debug.Log(result.RosterSlots);

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }
            }

            if (play.Captures.Count != N10CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + N10CaptureFiles.Length);
            }

            if (result.Failures != 0 || result.AiPickups < 1 || result.AiPasses < 1
                || result.MaxActiveSlots != MatchPlayN10Script.PlanSlots.Length
                || result.PlanPassRequests < 1)

            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_CAPTURE_FAILED roster=" + result.RosterDescribe
                               + " reason=" + result.FailureText);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N10_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " defaultPlanAi=" + (play.Control.DefaultPlanAi ? "True" : "False")
                      + " aiPickups=" + result.AiPickups + " aiPasses=" + result.AiPasses
                      + " aiShots=" + result.AiShots
                      + " planPassRequests=" + result.PlanPassRequests
                      + " maxActiveSlots=" + result.MaxActiveSlots
                      + " activeAtEnd=" + result.ActiveAtEnd
                      + " planIds=[" + result.PlanIdList + "]"
                      + " rebinds=" + result.Rebinds
                      + " relays=" + result.Relays
                      + " unsupportedDecisions=" + result.UnsupportedDecisions
                      + " heuristicFallbackTicks=" + result.HeuristicFallbackTicks
                      + " suppressedUpdateTicks=" + result.SuppressedUpdateTicks
                      + " planDigest=" + result.Digest);

            return failures;
        }

        private static void BuildAndRunWindowsN10(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();
                string captureBase = ResolveArtifactPath(Path.Combine("artifacts", "native-match-n10-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-match-n10-player.log"));
                string directory = Path.GetDirectoryName(captureBase);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string stem = Path.GetFileNameWithoutExtension(captureBase);
                string[] expected = new string[MatchPlay.N10CaptureLabels.Length];

                for (int i = 0; i < expected.Length; i++)
                {
                    expected[i] = Path.Combine(directory, stem + "-" + MatchPlay.N10CaptureLabels[i] + ".png");

                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + MatchPlay.CaptureWidth
                                  + " -screen-height " + MatchPlay.CaptureHeight
                                  + " -screen-fullscreen 0 --n10-capture=\"" + captureBase + "\""
                                  + " -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_MATCH_N10_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows N10 player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows N10 player exited "
                                                            + process.ExitCode + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows N10 player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_MATCH_N10_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_N10_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N10_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // --- N11: display/resolution options ------------------------------------------------

        public const string DisplayPlayerExecutable = "build-match/UBlade3NativeDisplay.exe";

        public static void CheckDisplay()
        {
            int mismatches = 0;

            try
            {
                mismatches += RunDisplayCheck();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DISPLAY_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_DISPLAY_CHECK_OK"
                : "UBLADE3_DISPLAY_CHECK_FAILED mismatches=" + mismatches);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunDisplayCheck()
        {
            int failures = 0;

            // 1. default + parser + persistence round-trip
            MatchDisplaySettings defaults = MatchDisplaySettings.Default();

            if (defaults.Width != MatchDisplaySettings.DefaultWidth
                || defaults.Height != MatchDisplaySettings.DefaultHeight
                || defaults.Mode != "windowed"
                || defaults.Preset != "1280x720")
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH defaults " + defaults.Describe());
            }

            if (!MatchDisplaySettings.TryParseResolution("1920x1080", out int width, out int height)
                || width != 1920 || height != 1080)
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH parse 1920x1080");
            }

            MatchDisplaySettings persisted = new MatchDisplaySettings();
            persisted.ApplyPreset("2560x1440");
            persisted.Mode = "borderless";
            string tempPath = Path.Combine(Application.temporaryCachePath, "ublade3-display-check.json");
            string saveDetail;

            if (!MatchDisplaySettings.Save(tempPath, persisted, out saveDetail))
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH save " + saveDetail);
            }

            string loadDetail;
            MatchDisplaySettings loaded = MatchDisplaySettings.Load(tempPath, out loadDetail);

            if (loaded.Width != 2560 || loaded.Height != 1440 || loaded.Mode != "borderless"
                || loaded.Preset != "2560x1440")
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH round-trip " + loaded.Describe() + " " + loadDetail);
            }

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            // 2. validation/fallback on a synthetic 1080p host
            Resolution[] supported =
            {
                new Resolution { width = 1280, height = 720 },
                new Resolution { width = 1920, height = 1080 },
            };
            Resolution desktop = new Resolution { width = 1920, height = 1080 };

            MatchDisplaySettings hd = new MatchDisplaySettings();
            hd.Width = 1920;
            hd.Height = 1080;
            hd.Mode = "windowed";
            hd.Preset = "1920x1080";
            MatchDisplayPlan.Result hdPlan = MatchDisplayPlan.Validate(hd, supported, desktop, null);

            if (hdPlan.Width != 1920 || hdPlan.Height != 1080 || hdPlan.Fallback)
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH 1080p plan " + hdPlan.Width + "x" + hdPlan.Height
                          + " fallback=" + hdPlan.Fallback);
            }

            MatchDisplaySettings qhd = new MatchDisplaySettings();
            qhd.ApplyPreset("2560x1440");
            qhd.Mode = "windowed";
            MatchDisplayPlan.Result qhdPlan = MatchDisplayPlan.Validate(qhd, supported, desktop, null);

            if (qhdPlan.Width != 1920 || qhdPlan.Height != 1080 || !qhdPlan.Fallback
                || qhdPlan.Reason != "windowed-larger-than-desktop")
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH qhd windowed fallback " + qhdPlan.Width + "x"
                          + qhdPlan.Height + " fallback=" + qhdPlan.Fallback + " reason=" + qhdPlan.Reason);
            }

            MatchDisplaySettings uhd = new MatchDisplaySettings();
            uhd.ApplyPreset("3840x2160");
            uhd.Mode = "borderless";
            MatchDisplayPlan.Result uhdPlan = MatchDisplayPlan.Validate(uhd, supported, desktop, null);

            if (uhdPlan.Width != 1920 || uhdPlan.Height != 1080 || !uhdPlan.Fallback
                || uhdPlan.Reason != "unsupported-mode-resolution")
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH 4K borderless fallback " + uhdPlan.Width + "x"
                          + uhdPlan.Height + " fallback=" + uhdPlan.Fallback + " reason=" + uhdPlan.Reason);
            }

            MatchDisplaySettings native = new MatchDisplaySettings();
            native.ApplyPreset("native");
            native.Mode = "windowed";
            MatchDisplayPlan.Result nativePlan = MatchDisplayPlan.Validate(native, supported, desktop, null);

            if (nativePlan.Width != 1920 || nativePlan.Height != 1080 || nativePlan.Fallback)
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH native plan " + nativePlan.Width + "x" + nativePlan.Height);
            }

            // 3. corrupt file falls back to defaults
            string corrupt = Path.Combine(Application.temporaryCachePath, "ublade3-display-corrupt.json");
            File.WriteAllText(corrupt, "{not json", new System.Text.UTF8Encoding(false));
            MatchDisplaySettings invalid = MatchDisplaySettings.Load(corrupt, out string corruptDetail);

            if (invalid.Width != MatchDisplaySettings.DefaultWidth || !corruptDetail.StartsWith("invalid"))
            {
                failures++;
                Debug.Log("UBLADE3_DISPLAY_MISMATCH corrupt fallback " + invalid.Describe() + " " + corruptDetail);
            }

            if (File.Exists(corrupt))
            {
                File.Delete(corrupt);
            }

            Debug.Log("UBLADE3_DISPLAY_CHECK_COUNTS presets=" + MatchDisplaySettings.Presets.Length
                      + " modes=" + string.Join(",", MatchDisplaySettings.Modes)
                      + " hd=" + hdPlan.Width + "x" + hdPlan.Height
                      + " qhdFallback=" + qhdPlan.Width + "x" + qhdPlan.Height
                      + " uhdFallback=" + uhdPlan.Width + "x" + uhdPlan.Height
                      + " native=" + nativePlan.Width + "x" + nativePlan.Height
                      + " failures=" + failures);

            return failures;
        }

        public static void BuildWindowsDisplay()
        {
            int exitCode = 0;

            try
            {
                string playerPath = Path.GetFullPath(Path.Combine(ProjectRoot, DisplayPlayerExecutable));
                Debug.Log("UBLADE3_DISPLAY_BUILD_OK output=" + BuildPlayer(playerPath));
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DISPLAY_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }


        // --- N11: exact plan-target steering physics ------------------------------------------

        private static void CheckN11(bool exitWhenBatch)
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN11Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N11_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N11_CHECK_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN11Check()
        {
            int mismatches = 0;

            MatchPlay firstPlay;
            int firstFailures = RunN11Scenario(out firstPlay, out MatchPlayN11ScriptResult first);
            mismatches += firstFailures;

            Debug.Log(first.RosterDescribe);
            Debug.Log(first.SlotLines);
            Debug.Log(first.StartMotion);
            Debug.Log(first.PhysicsMotion);
            Debug.Log(first.BeforeChangeMotion);
            Debug.Log(first.AfterChangeMotion);

            for (int index = 0; index < first.TargetTrace.Count && index < 12; index++)
            {
                Debug.Log("UBLADE3_NATIVE_MATCH_N11_TARGET_CHANGE " + first.TargetTrace[index]);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N11_CHECK_RESULT defaultPlanAi="
                      + (firstPlay.Control != null && firstPlay.Control.DefaultPlanAi ? "True" : "False")
                      + " targetPhysics="
                      + (firstPlay.Control != null && firstPlay.Control.PlanRoster != null
                         && firstPlay.Control.PlanRoster.TargetPhysicsEnabled ? "True" : "False")
                      + " aiPickups=" + first.AiPickups
                      + " planPhysicsTicks=" + first.PlanPhysicsTicks
                      + " slot1PhysicsTicks=" + first.Slot1PhysicsTicks
                      + " slot1BankedTicks=" + first.Slot1BankedTicks
                      + " slot1TargetChanges=" + first.Slot1TargetChanges
                      + " slot1Displacement=" + first.Slot1Displacement.ToString("0.###")
                      + " slot1HeadingTravelDeg=" + first.Slot1HeadingTravelDeg.ToString("0.###")
                      + " slot1StartDistance=" + first.Slot1StartDistance.ToString("0.###")
                      + " slot1MinDistance=" + first.Slot1MinDistance.ToString("0.###")
                      + " slot1LastDistance=" + first.Slot1LastDistance.ToString("0.###")
                      + " bridgeFallbackTicks=" + first.RosterBridgeFallbackTicks
                      + " ticks=" + first.TicksRun
                      + " digest=" + first.Digest
                      + " failures=" + first.Failures + " reason=" + first.FailureText);

            bool defaultWired = firstPlay.Control != null && firstPlay.Control.DefaultPlanAi
                                && firstPlay.Control.PlanRoster != null
                                && firstPlay.Control.PlanRoster.TargetPhysicsEnabled;

            if (first.Failures != 0 || !defaultWired
                || first.Slot1PhysicsTicks < 10
                || (first.Slot1BankedTicks + first.RosterBankedTicks) < 1
                || first.Slot1Displacement < 5.0f || first.Slot1HeadingTravelDeg < 5.0f
                || first.Slot1StartDistance < 5.0f
                || first.Slot1MinDistance >= first.Slot1StartDistance - 2.0f
                || first.Slot1TargetChanges < 1 || first.TargetChanges < 1
                || first.AiPickups < 1 || first.RosterBridgeFallbackTicks < 1
                || first.TicksRun > MatchPlayN11Script.MaxTicks
                || string.IsNullOrEmpty(first.Digest))
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N11_MISMATCH defaultWired=" + defaultWired
                          + " roster=" + first.RosterDescribe + " reason=" + first.FailureText);
            }

            MatchPlay secondPlay;
            int secondFailures = RunN11Scenario(out secondPlay, out MatchPlayN11ScriptResult second);
            mismatches += secondFailures;

            bool deterministic = string.Equals(first.Digest, second.Digest, StringComparison.Ordinal)
                                 && first.AiPickups == second.AiPickups
                                 && first.Slot1PhysicsTicks == second.Slot1PhysicsTicks
                                 && first.Slot1BankedTicks == second.Slot1BankedTicks
                                 && first.Slot1TargetChanges == second.Slot1TargetChanges
                                 && Math.Abs(first.Slot1Displacement - second.Slot1Displacement) < 1e-4f
                                 && Math.Abs(first.Slot1StartDistance - second.Slot1StartDistance) < 1e-4f
                                 && Math.Abs(first.Slot1MinDistance - second.Slot1MinDistance) < 1e-4f;

            Debug.Log("UBLADE3_NATIVE_MATCH_N11_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " digest1=" + first.Digest + " digest2=" + second.Digest);

            if (!deterministic)
            {
                mismatches++;
            }

            int processes = CountTwentyXxProcesses();
            Debug.Log("UBLADE3_NATIVE_MATCH_N11_NO_20XX running20xxProcesses=" + processes + " no20xx=True");

            if (processes != 0)
            {
                mismatches++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N11_COUNTS aiPickups=" + first.AiPickups
                      + " planPhysicsTicks=" + first.PlanPhysicsTicks
                      + " slot1PhysicsTicks=" + first.Slot1PhysicsTicks
                      + " slot1BankedTicks=" + first.Slot1BankedTicks
                      + " slot1TargetChanges=" + first.Slot1TargetChanges
                      + " slot1Displacement=" + first.Slot1Displacement.ToString("0.###")
                      + " slot1HeadingTravelDeg=" + first.Slot1HeadingTravelDeg.ToString("0.###")
                      + " slot1StartDistance=" + first.Slot1StartDistance.ToString("0.###")
                      + " slot1MinDistance=" + first.Slot1MinDistance.ToString("0.###")
                      + " bridgeFallbackTicks=" + first.RosterBridgeFallbackTicks
                      + " ticks=" + first.TicksRun
                      + " digest=" + first.Digest
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static int RunN11Scenario(out MatchPlay play, out MatchPlayN11ScriptResult result)
        {
            int failures = 0;
            play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi)
            {
                failures++;
                result = new MatchPlayN11ScriptResult();
                result.Failures = 1;
                result.FailureText = "normal build did not attach the default PlanVM AI roster; ";
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CHECK_FAILED " + result.FailureText);

                return failures;
            }

            failures += play.RunDeterministicCaptureN11(null, out result);

            return failures;
        }

        private static void CaptureAllN11(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureSceneN11();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N11_CAPTURES_OK frames=" + N11CaptureFiles.Length
                  + " width=" + MatchPlay.CaptureWidth + " height=" + MatchPlay.CaptureHeight
                : "UBLADE3_NATIVE_MATCH_N11_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureSceneN11()
        {
            int failures = 0;
            MatchPlay play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi
                || !play.Control.PlanRoster.TargetPhysicsEnabled)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED the normal build is not running target physics");
            }

            string[] paths = new string[N11CaptureFiles.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(N11CaptureFiles[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            MatchPlayN11ScriptResult result;
            failures += play.RunDeterministicCaptureN11(paths, out result);

            Debug.Log(MatchProbe.DescribeScene(play.Scene));
            Debug.Log(play.Describe());
            Debug.Log(result.RosterDescribe);
            Debug.Log(result.SlotLines);

            for (int i = 0; i < play.Captures.Count; i++)
            {
                MatchPlayCapture capture = play.Captures[i];

                if (!File.Exists(capture.Path))
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED missing " + capture.Path);
                    continue;
                }

                FileInfo info = new FileInfo(capture.Path);

                if (info.Length != capture.Bytes || info.Length < 1024)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED " + capture.Path + " bytes "
                                   + info.Length + " != " + capture.Bytes);
                }
            }

            if (play.Captures.Count != N11CaptureFiles.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED captured " + play.Captures.Count
                               + " of " + N11CaptureFiles.Length);
            }

            if (result.Failures != 0 || result.Slot1PhysicsTicks < 10 || result.Slot1TargetChanges < 1
                || result.AiPickups < 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N11_CAPTURE_FAILED roster=" + result.RosterDescribe
                               + " reason=" + result.FailureText);
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N11_CAPTURE_EVIDENCE frames=" + play.Captures.Count
                      + " defaultPlanAi=True targetPhysics=True"
                      + " aiPickups=" + result.AiPickups
                      + " planPhysicsTicks=" + result.PlanPhysicsTicks
                      + " slot1PhysicsTicks=" + result.Slot1PhysicsTicks
                      + " slot1BankedTicks=" + result.Slot1BankedTicks
                      + " slot1TargetChanges=" + result.Slot1TargetChanges
                      + " slot1Displacement=" + result.Slot1Displacement.ToString("0.###")
                      + " slot1HeadingTravelDeg=" + result.Slot1HeadingTravelDeg.ToString("0.###")
                      + " slot1StartDistance=" + result.Slot1StartDistance.ToString("0.###")
                      + " slot1MinDistance=" + result.Slot1MinDistance.ToString("0.###")
                      + " bridgeFallbackTicks=" + result.RosterBridgeFallbackTicks
                      + " planDigest=" + result.Digest);

            return failures;
        }

        // --- N12: original InfoBar layout / GDI text HUD --------------------------------

        public static readonly string[] HudCaptureFiles =
        {
            "artifacts/native-hud-720p.png",
            "artifacts/native-hud-1080p.png",
            "artifacts/native-hud-4k.png",
            "artifacts/native-hud-design-640x480.png",
        };

        public static void CheckHud()
        {
            int mismatches = 0;

            try
            {
                MatchWorldData world = MatchWorldData.Load(MatchScene.ResolveRoot());
                MatchHudData baseData = MatchHudData.Load(world);
                Debug.Log("UBLADE3_NATIVE_HUD_BASE " + baseData.Describe());

                string detail;
                mismatches += MatchHudProbe.Check(world, out detail);
                Debug.Log(detail);

                if (!MatchHudFont.Available)
                {
                    mismatches++;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_HUD_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_HUD_CHECK_OK"
                : "UBLADE3_NATIVE_HUD_CHECK_FAILED mismatches=" + mismatches);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        public static void CaptureHud()
        {
            int failures = 0;

            try
            {
                failures += CaptureHudFrames();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_HUD_CAPTURE_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_HUD_CAPTURES_OK frames=" + HudCaptureFiles.Length
                : "UBLADE3_NATIVE_HUD_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int CaptureHudFrames()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject root = new GameObject("UBlade3 Native HUD Capture");
            MatchScene scene = root.AddComponent<MatchScene>();
            MatchGameplay gameplay = root.AddComponent<MatchGameplay>();

            scene.Build();
            gameplay.Build(scene);

            MatchHud hud = gameplay.HUD;

            if (hud == null || !hud.Built)
            {
                throw new InvalidOperationException("The HUD did not build.");
            }

            hud.SetOriginalLayout(true);

            if (hud.OriginalHud == null || !hud.OriginalHud.IsBuilt)
            {
                throw new InvalidOperationException("The original HUD renderer did not build.");
            }

            // A deterministic HUD state that exercises the shot-power/head-camera art without needing
            // a live MatchPlay shot in this capture. The HUD probe logs this as a pinned probe state.
            hud.SetOriginalProbeState(72, true, true, 0.62f, 0.5f, 0.65f, "MATCH LIVE");

            for (int tick = 0; tick < 60; tick++)
            {
                scene.TickScene(HeldBinding.None);
            }

            gameplay.Sync();
            Debug.Log("UBLADE3_NATIVE_HUD_CAPTURE_STATE " + hud.OriginalHud.Describe());
            Debug.Log("UBLADE3_NATIVE_HUD_CAPTURE_PROBE_STATE shotPower=72 headCam=True reticle=True"
                      + " reticleFrac=(0.62,0.5) speedRatio=0.65 status=\"MATCH LIVE\""
                      + " simTick=" + scene.TickCount);

            int[,] sizes = { { 1280, 720 }, { 1920, 1080 }, { 3840, 2160 }, { 640, 480 } };

            for (int i = 0; i < sizes.GetLength(0); i++)
            {
                int width = sizes[i, 0];
                int height = sizes[i, 1];
                string path = ResolveArtifactPath(HudCaptureFiles[i]);

                if (CaptureHudFrame(scene, gameplay, hud, path, width, height))
                {
                    FileInfo info = new FileInfo(path);
                    Debug.Log("UBLADE3_NATIVE_HUD_CAPTURE path=" + path + " size=" + width + "x"
                              + height + " bytes=" + info.Length);
                }
                else
                {
                    failures++;
                }
            }

            return failures;
        }

        private static bool CaptureHudFrame(MatchScene scene, MatchGameplay gameplay, MatchHud hud,
            string path, int width, int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.name = "UBlade3 HUD Capture";
            target.Create();

            RenderTexture savedActive = RenderTexture.active;
            RenderTexture savedTarget = scene.MatchCamera.targetTexture;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                scene.ApplyRender(1.0f);
                scene.MatchCamera.targetTexture = target;
                scene.MatchCamera.aspect = (float)width / height;
                scene.MatchCamera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                Color32[] pixels = image.GetPixels32();
                hud.Composite(pixels, width, height, gameplay.HudModel);
                image.SetPixels32(pixels);
                image.Apply(false, false);

                byte[] png = image.EncodeToPNG();
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(path, png);
                return png.Length > 1024;
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_HUD_CAPTURE_FRAME_FAILED " + path + " "
                               + exception.GetType().Name + ": " + exception.Message);
                return false;
            }
            finally
            {
                scene.MatchCamera.targetTexture = savedTarget;
                RenderTexture.active = savedActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        // --- N13: native C# preview 006 (default audio + original HUD) -------------------

        public static string Preview006PlayerPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, "build", "preview-006",
                "UBlade3NativePreview006.exe"));
        }

        public static void BuildPreview006()
        {
            int exitCode = 0;

            try
            {
                string path = BuildPlayer(Preview006PlayerPath());
                Debug.Log("UBLADE3_NATIVE_PREVIEW_006_BUILD_OK output=" + path
                          + " defaultAudio=interactive defaultHud=original");
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PREVIEW_006_BUILD_FAILED " + exception);
                Debug.LogException(exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }


        // --- N12: observed plan-handler coverage ------------------------------------------------

        public static readonly int[] N12ActionIds = { 6, 7, 9, 18, 29, 30, 71 };

        public static readonly int[] N12DecisionIds = { 6, 23, 68 };

        public static void CheckN12()
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN12Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N12_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N12_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N12_CHECK_FAILED mismatches=" + mismatches);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN12Check()
        {
            int mismatches = 0;
            MatchPlay play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N12_CHECK_FAILED no default PlanVM AI");
                return 1;
            }

            MatchPlayN11Script.Stage(play.Control);

            int[] actionTotals = new int[N12ActionIds.Length];
            int[] decisionTotals = new int[N12DecisionIds.Length];
            int[] effectTotals = new int[8];
            int ticks = 0;

            for (int guard = 0; guard < MatchPlayN11Script.MaxTicks && ticks < 140; guard++)
            {
                play.Control.Advance(HeldBinding.None, held =>
                {
                    play.Scene.TickScene(held);
                    MatchPlayN11Script.HoldStaged(play.Control.Sim);
                });
                ticks = play.Control.Ticks;
            }

            MatchPlanAiRoster roster = play.Control.PlanRoster;

            for (int slotIndex = 0; slotIndex < roster.PlanSlots.Count; slotIndex++)
            {
                MatchPlanAi adapter = roster.Adapter(roster.PlanSlots[slotIndex]);

                if (adapter == null)
                {
                    continue;
                }

                for (int i = 0; i < N12ActionIds.Length; i++)
                {
                    int count;

                    if (adapter.PlanActionCounts.TryGetValue(N12ActionIds[i], out count))
                    {
                        actionTotals[i] += count;
                    }
                }

                for (int i = 0; i < N12DecisionIds.Length; i++)
                {
                    int count;

                    if (adapter.PlanDecisionCounts.TryGetValue(N12DecisionIds[i], out count))
                    {
                        decisionTotals[i] += count;
                    }
                }

                effectTotals[0] += adapter.PlanActionFrictionApplications;
                effectTotals[1] += adapter.PlanActionLocationApplications;
                effectTotals[2] += adapter.PlanActionAnimationEvents;
                effectTotals[3] += adapter.PlanActionSoundEvents;
                effectTotals[4] += adapter.PlanActionStatEvents;
                effectTotals[5] += adapter.PlanActionBubbleEvents;
                effectTotals[6] += adapter.PlanActionSpeedBurstEvents;
                effectTotals[7] += adapter.UnsupportedDecisionErrors;
            }

            string actionText = JoinIdsWithCounts(N12ActionIds, actionTotals);
            string decisionText = JoinIdsWithCounts(N12DecisionIds, decisionTotals);

            Debug.Log("UBLADE3_NATIVE_MATCH_N12_RESULT defaultPlanAi=True targetPhysics="
                      + (roster.TargetPhysicsEnabled ? "True" : "False")
                      + " ticks=" + ticks
                      + " aiPickups=" + play.Control.AiPickups
                      + " aiPasses=" + play.Control.AiPasses
                      + " actions=[" + actionText + "]"
                      + " decisions=[" + decisionText + "]"
                      + " effects=[friction=" + effectTotals[0] + ",location=" + effectTotals[1]
                      + ",animation=" + effectTotals[2] + ",sound=" + effectTotals[3]
                      + ",stat=" + effectTotals[4] + ",bubble=" + effectTotals[5]
                      + ",speedBurst=" + effectTotals[6] + ",unsupportedDecisionErrors=" + effectTotals[7] + "]"
                      + " planIds=[" + roster.PlanIds() + "]"
                      + " digest=" + roster.DigestHex());
            Debug.Log(roster.Describe());
            Debug.Log(roster.DescribeSlots());

            for (int i = 0; i < N12ActionIds.Length; i++)
            {
                if (actionTotals[i] < 1)
                {
                    mismatches++;
                    Debug.Log("UBLADE3_NATIVE_MATCH_N12_MISMATCH action id=" + N12ActionIds[i] + " never executed");
                }
            }

            for (int i = 0; i < N12DecisionIds.Length; i++)
            {
                if (decisionTotals[i] < 1)
                {
                    mismatches++;
                    Debug.Log("UBLADE3_NATIVE_MATCH_N12_MISMATCH decision id=" + N12DecisionIds[i] + " never executed");
                }
            }

            for (int i = 0; i < N12ActionIds.Length; i++)
            {
                if (actionTotals[i] < 1 && effectTotals[0] + effectTotals[1] + effectTotals[2] + effectTotals[3]
                    + effectTotals[4] + effectTotals[5] + effectTotals[6] < 1)
                {
                    mismatches++;
                }
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N12_COUNTS ticks=" + ticks
                      + " actions=" + actionText
                      + " decisions=" + decisionText
                      + " frictionApplied=" + effectTotals[0]
                      + " locationApplied=" + effectTotals[1]
                      + " animationEvents=" + effectTotals[2]
                      + " soundEvents=" + effectTotals[3]
                      + " statEvents=" + effectTotals[4]
                      + " bubbleEvents=" + effectTotals[5]
                      + " speedBurstEvents=" + effectTotals[6]
                      + " unsupportedDecisionErrors=" + effectTotals[7]
                      + " mismatches=" + mismatches);

            return mismatches;
        }

        private static string JoinIdsWithCounts(int[] ids, int[] counts)
        {
            string[] parts = new string[ids.Length];

            for (int index = 0; index < ids.Length; index++)
            {
                parts[index] = ids[index] + "x" + counts[index];
            }

            return string.Join(",", parts);
        }


        // --- N13: remaining observed plan-handler coverage ----------------------------------------

        public static readonly int[] N13ActionIds = { 84, 139 };

        public static readonly int[] N13DecisionIds = { 51, 71, 120 };

        public static void CheckN13()
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN13Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N13_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N13_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N13_CHECK_FAILED mismatches=" + mismatches);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN13Check()
        {
            int mismatches = 0;
            MatchPlay play = CreateDefaultPlayScene();
            PlanAiMatchDecisionsN14.Enabled = false;

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N13_CHECK_FAILED no default PlanVM AI");
                return 1;
            }

            MatchPlayN10Script.Stage(play.Control);

            int[] actionTotals = new int[N13ActionIds.Length];
            int[] decisionTotals = new int[N13DecisionIds.Length];
            int[] effectTotals = new int[17];
            int ticks = 0;

            for (int guard = 0; guard < 300 && ticks < 240; guard++)
            {
                play.Control.Advance(HeldBinding.None, held =>
                {
                    play.Scene.TickScene(held);
                    MatchPlayN10Script.HoldStaged(play.Control.Sim);
                });
                ticks = play.Control.Ticks;
            }

            MatchPlanAiRoster roster = play.Control.PlanRoster;

            for (int slotIndex = 0; slotIndex < roster.PlanSlots.Count; slotIndex++)
            {
                MatchPlanAi adapter = roster.Adapter(roster.PlanSlots[slotIndex]);

                if (adapter == null)
                {
                    continue;
                }

                for (int i = 0; i < N13ActionIds.Length; i++)
                {
                    int count;

                    if (adapter.PlanActionCounts.TryGetValue(N13ActionIds[i], out count))
                    {
                        actionTotals[i] += count;
                    }
                }

                for (int i = 0; i < N13DecisionIds.Length; i++)
                {
                    int count;

                    if (adapter.PlanDecisionCounts.TryGetValue(N13DecisionIds[i], out count))
                    {
                        decisionTotals[i] += count;
                    }
                }

                effectTotals[0] += adapter.PlanActionSoundApplied;
                effectTotals[1] += adapter.PlanActionSoundUnavailable;
                effectTotals[2] += adapter.PlanActionAnimationApplied;
                effectTotals[3] += adapter.PlanActionStatApplied;
                effectTotals[4] += adapter.PlanActionBubbleApplied;
                effectTotals[5] += adapter.PlanActionSoundEvents;
                effectTotals[6] += adapter.PlanActionAnimationEvents;
                effectTotals[7] += adapter.PlanActionStatEvents;
                effectTotals[8] += adapter.BehaviorParamApplications;
                effectTotals[9] += adapter.TargetGoalApplications;
                effectTotals[10] += adapter.AgendaAddsAvoidObjects;
                effectTotals[11] += adapter.AgendaAddsRelaxToPosition;
                effectTotals[12] += adapter.AgendaPositionApplications;
                effectTotals[13] += adapter.AgendaSmoothVelApplications;
                effectTotals[14] += adapter.AgendaBlockedEvents;
                effectTotals[15] += adapter.AgendaBreakawayEvents;
                effectTotals[16] += adapter.AgendaRelaxAdvanceEvents;
            }

            string actionText = JoinIdsWithCounts(N13ActionIds, actionTotals);
            string decisionText = JoinIdsWithCounts(N13DecisionIds, decisionTotals);

            Debug.Log("UBLADE3_NATIVE_MATCH_N13_RESULT defaultPlanAi=True targetPhysics="
                      + (roster.TargetPhysicsEnabled ? "True" : "False")
                      + " ticks=" + ticks
                      + " aiPickups=" + play.Control.AiPickups
                      + " aiPasses=" + play.Control.AiPasses
                      + " aiShots=" + play.Control.AiShots
                      + " actions=[" + actionText + "]"
                      + " decisions=[" + decisionText + "]"
                      + " effects=[soundApplied=" + effectTotals[0]
                      + ",soundUnavailable=" + effectTotals[1]
                      + ",animationApplied=" + effectTotals[2]
                      + ",statApplied=" + effectTotals[3]
                      + ",bubbleApplied=" + effectTotals[4]
                      + ",soundEvents=" + effectTotals[5]
                      + ",animationEvents=" + effectTotals[6]
                      + ",statEvents=" + effectTotals[7]
                      + ",behParams=" + effectTotals[8]
                      + ",targetGoal=" + effectTotals[9]
                      + ",agendaAdds8=" + effectTotals[10]
                      + ",agendaAdds15=" + effectTotals[11]
                      + ",agendaPos=" + effectTotals[12]
                      + ",agendaSmoothVel=" + effectTotals[13]
                      + ",agendaBlocked=" + effectTotals[14]
                      + ",agendaBreakaway=" + effectTotals[15]
                      + ",agendaRelax=" + effectTotals[16] + "]"
                      + " planIds=[" + roster.PlanIds() + "]"
                      + " digest=" + roster.DigestHex());
            Debug.Log(roster.Describe());
            Debug.Log(roster.DescribeSlots());

            // N13's default handler set changes the plan path, so the shot body (139) is proven on
            // the pinned N4-N12 shot staging in a second, explicitly legacy-handler scene. The same
            // N13 action registration is active there; only the new behaviour/agenda bodies are off.
            MatchPlay legacy = CreateDefaultPlayScene();
            int legacyShotAction = 0;
            int legacyShots = 0;

            if (legacy.Control != null && legacy.Control.PlanRoster != null)
            {
                DisableLegacyN13Handlers(legacy);
                MatchPlayN10Script.Stage(legacy.Control);

                for (int guard = 0; guard < 300 && legacy.Control.Ticks < 240; guard++)
                {
                    legacy.Control.Advance(HeldBinding.None, held =>
                    {
                        legacy.Scene.TickScene(held);
                        MatchPlayN10Script.HoldStaged(legacy.Control.Sim);
                    });
                }

                legacyShots = legacy.Control.AiShots;

                for (int slotIndex = 0; slotIndex < legacy.Control.PlanRoster.PlanSlots.Count; slotIndex++)
                {
                    MatchPlanAi adapter = legacy.Control.PlanRoster.Adapter(
                        legacy.Control.PlanRoster.PlanSlots[slotIndex]);

                    if (adapter != null)
                    {
                        int count;

                        if (adapter.PlanActionCounts.TryGetValue(139, out count))
                        {
                            legacyShotAction += count;
                        }
                    }
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_N13_LEGACY_SHOT actions=[139x" + legacyShotAction
                          + "] aiShots=" + legacyShots + " ticks=" + legacy.Control.Ticks);
            }

            if (actionTotals[0] < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N13_MISMATCH action id=" + N13ActionIds[0] + " never executed");
            }

            if (actionTotals[1] < 1 && legacyShotAction < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N13_MISMATCH action id=139 never executed in either scenario");
            }

            if (effectTotals[8] < 1 || effectTotals[10] < 1 || effectTotals[11] < 1
                || effectTotals[12] < 1 || effectTotals[13] < 1 || effectTotals[16] < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N13_MISMATCH behaviour/agenda handlers did not execute:"
                          + " behParams=" + effectTotals[8] + " agendaAdds8=" + effectTotals[10]
                          + " agendaAdds15=" + effectTotals[11] + " agendaPos=" + effectTotals[12]
                          + " agendaSmoothVel=" + effectTotals[13] + " agendaRelax=" + effectTotals[16]);
            }

            for (int i = 0; i < N13DecisionIds.Length; i++)
            {
                if (decisionTotals[i] < 1)
                {
                    mismatches++;
                    Debug.Log("UBLADE3_NATIVE_MATCH_N13_MISMATCH decision id=" + N13DecisionIds[i] + " never executed");
                }
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N13_COUNTS ticks=" + ticks
                      + " actions=" + actionText
                      + " decisions=" + decisionText
                      + " soundApplied=" + effectTotals[0]
                      + " soundUnavailable=" + effectTotals[1]
                      + " animationApplied=" + effectTotals[2]
                      + " statApplied=" + effectTotals[3]
                      + " bubbleApplied=" + effectTotals[4]
                      + " soundEvents=" + effectTotals[5]
                      + " animationEvents=" + effectTotals[6]
                      + " statEvents=" + effectTotals[7]
                      + " behParams=" + effectTotals[8]
                      + " targetGoal=" + effectTotals[9]
                      + " agendaAdds8=" + effectTotals[10]
                      + " agendaAdds15=" + effectTotals[11]
                      + " agendaPos=" + effectTotals[12]
                      + " agendaSmoothVel=" + effectTotals[13]
                      + " agendaBlocked=" + effectTotals[14]
                      + " agendaBreakaway=" + effectTotals[15]
                      + " agendaRelax=" + effectTotals[16]
                      + " mismatches=" + mismatches);

            return mismatches;
        }


        // --- N14: last observed default-match handler coverage --------------------------------

        public static readonly int[] N14DecisionIds = { 31, 57, 123 };

        public static void CheckN14()
        {
            int mismatches = 0;

            try
            {
                mismatches += RunN14Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N14_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                mismatches++;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_N14_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N14_CHECK_FAILED mismatches=" + mismatches);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static int RunN14Check()
        {
            int[] first = RunN14Scenario(out string firstDigest, out int firstTicks, out int firstPickups,
                out int firstPasses, out int firstShots);
            int[] second = RunN14Scenario(out string secondDigest, out int secondTicks, out int secondPickups,
                out int secondPasses, out int secondShots);
            int mismatches = 0;

            string decisionText = JoinIdsWithCounts(N14DecisionIds, new int[] { first[0], first[1], first[2] });
            Debug.Log("UBLADE3_NATIVE_MATCH_N14_RESULT defaultPlanAi=True targetPhysics=True ticks=" + firstTicks
                      + " aiPickups=" + firstPickups + " aiPasses=" + firstPasses + " aiShots=" + firstShots
                      + " decisions=[" + decisionText + "]"
                      + " effects=[behParams=" + first[3] + ",targetGoal=" + first[4]
                      + ",agendaAdds8=" + first[5] + ",agendaAdds15=" + first[6]
                      + ",agendaSmoothVel=" + first[7] + ",agendaPos=" + first[8]
                      + ",agendaRelax=" + first[9] + ",release=" + first[10]
                      + ",netTarget=" + first[11] + ",cameraBias=" + first[12]
                      + ",avoidDetach=" + first[13] + ",relaxDetach=" + first[14]
                      + ",soundEvents=" + first[15] + ",soundApplied=" + first[16]
                      + ",soundUnavailable=" + first[17]
                      + ",animationEvents=" + first[18] + ",animationApplied=" + first[19]
                      + ",statEvents=" + first[20] + ",statApplied=" + first[21]
                      + ",bubbleEvents=" + first[22] + ",bubbleApplied=" + first[23]
                      + ",unsupportedHandlers=" + first[24] + "]"
                      + " digest=" + firstDigest);
            Debug.Log("UBLADE3_NATIVE_MATCH_N14_COUNTS decisions=[" + decisionText + "]"
                      + " unsupportedHandlers=" + first[24]
                      + " release=" + first[10] + " netTarget=" + first[11]
                      + " cameraBias=" + first[12] + " avoidDetach=" + first[13]
                      + " relaxDetach=" + first[14] + " mismatches=" + mismatches);

            if (first[0] < 1 || first[1] < 1 || first[2] < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N14_MISMATCH a decision body did not execute");
            }

            if (first[24] != 0)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N14_MISMATCH default slots still have unsupported handlers=" + first[24]);
            }

            if (first[10] < 1 || first[11] < 1 || first[12] < 1 || first[13] < 1)
            {
                mismatches++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N14_MISMATCH agenda release/purge/camera effects missing"
                          + " (relaxDetach=" + first[14] + " is reported, not required: the goalie stays in"
                          + " one behaviour in this window)");
            }

            bool deterministic = string.Equals(firstDigest, secondDigest, StringComparison.Ordinal)
                                 && firstTicks == secondTicks && firstPickups == secondPickups
                                 && firstPasses == secondPasses && firstShots == secondShots;

            for (int index = 0; index < first.Length && deterministic; index++)
            {
                if (first[index] != second[index])
                {
                    deterministic = false;
                }
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N14_DETERMINISM ok=" + (deterministic ? "True" : "False")
                      + " digest1=" + firstDigest + " digest2=" + secondDigest);

            if (!deterministic)
            {
                mismatches++;
            }

            return mismatches;
        }

        private static int[] RunN14Scenario(out string digest, out int ticks, out int pickups,
            out int passes, out int shots)
        {
            MatchPlay play = CreateDefaultPlayScene();

            if (play.Control == null || play.Control.PlanRoster == null || !play.Control.DefaultPlanAi
                || !PlanAiMatchDecisionsN14.Enabled)
            {
                throw new InvalidOperationException("N14 scenario did not get the default N14-enabled PlanVM roster");
            }

            MatchPlayN10Script.Stage(play.Control);
            int[] counts = new int[25];

            for (int guard = 0; guard < 300 && play.Control.Ticks < 240; guard++)
            {
                play.Control.Advance(HeldBinding.None, held =>
                {
                    play.Scene.TickScene(held);
                    MatchPlayN10Script.HoldStaged(play.Control.Sim);
                });
            }

            MatchPlanAiRoster roster = play.Control.PlanRoster;

            for (int slotIndex = 0; slotIndex < roster.PlanSlots.Count; slotIndex++)
            {
                MatchPlanAi adapter = roster.Adapter(roster.PlanSlots[slotIndex]);

                if (adapter == null)
                {
                    continue;
                }

                for (int index = 0; index < N14DecisionIds.Length; index++)
                {
                    int count;

                    if (adapter.PlanDecisionCounts.TryGetValue(N14DecisionIds[index], out count))
                    {
                        counts[index] += count;
                    }
                }

                counts[3] += adapter.BehaviorParamApplications;
                counts[4] += adapter.TargetGoalApplications;
                counts[5] += adapter.AgendaAddsAvoidObjects;
                counts[6] += adapter.AgendaAddsRelaxToPosition;
                counts[7] += adapter.AgendaSmoothVelApplications;
                counts[8] += adapter.AgendaPositionApplications;
                counts[9] += adapter.AgendaRelaxAdvanceEvents;
                counts[10] += adapter.AgendaReleaseEvents;
                counts[11] += adapter.AgendaNetTargetApplications;
                counts[12] += adapter.AgendaCameraBiasUses;
                counts[13] += adapter.AgendaAvoidDetaches;
                counts[14] += adapter.AgendaRelaxDetaches;
                counts[15] += adapter.PlanActionSoundEvents;
                counts[16] += adapter.PlanActionSoundApplied;
                counts[17] += adapter.PlanActionSoundUnavailable;
                counts[18] += adapter.PlanActionAnimationEvents;
                counts[19] += adapter.PlanActionAnimationApplied;
                counts[20] += adapter.PlanActionStatEvents;
                counts[21] += adapter.PlanActionStatApplied;
                counts[22] += adapter.PlanActionBubbleEvents;
                counts[23] += adapter.PlanActionBubbleApplied;
                counts[24] += adapter.UnsupportedHandlerCount;
            }

            ticks = play.Control.Ticks;
            pickups = play.Control.AiPickups;
            passes = play.Control.AiPasses;
            shots = play.Control.AiShots;
            digest = roster.DigestHex();
            Debug.Log(roster.Describe());
            Debug.Log(roster.DescribeSlots());

            return counts;
        }

        // --- N15: plan-animation consumer trace and visible pose captures -----------------

        public static readonly string[] AnimationCaptureFiles =
        {
            "artifacts/native-animation-frame-00.png",
            "artifacts/native-animation-frame-01.png",
            "artifacts/native-animation-frame-02.png",
            "artifacts/native-animation-frame-03.png",
            "artifacts/native-animation-frame-04.png",
            "artifacts/native-animation-frame-05.png",
        };

        public static void CheckAnimation()
        {
            int failures = 0;

            try
            {
                failures += RunAnimationCheck();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_ANIMATION_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_ANIMATION_CHECK_OK"
                : "UBLADE3_NATIVE_ANIMATION_CHECK_FAILED failures=" + failures);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        // Stages the captured character at a known point in the drome and renders it against the
        // real drome. The long plan trace can leave a live view transform outside the arena (and the
        // chase camera is not a portrait camera), so this evidence frame is explicitly a staged pose
        // capture: other skater views are hidden, the captured view is placed at the centre ice point
        // and the match camera is put on a fixed three-quarter angle. It is labelled as such in the
        // log; it does not claim to be a gameplay camera frame.
        private static void PlaceAnimationCaptureCamera(MatchScene scene, int slot)
        {
            if (scene == null || scene.MatchCamera == null || scene.Views == null
                || slot < 0 || slot >= scene.Views.Length || scene.Views[slot] == null)
            {
                return;
            }

            Vector3 stage = new Vector3(0.0f, 0.0f, 0.0f);

            for (int i = 0; i < scene.Views.Length; i++)
            {
                MatchSkaterView view = scene.Views[i];

                if (view == null || view.Root == null)
                {
                    continue;
                }

                view.Root.SetActive(i == slot);
            }

            scene.Views[slot].Root.transform.position = stage;
            Vector3 target = stage + (Vector3.up * 10.0f);
            scene.MatchCamera.transform.position = stage + new Vector3(0.0f, 11.0f, -32.0f);
            scene.MatchCamera.transform.LookAt(target);
            scene.MatchCamera.fieldOfView = 48.0f;
            scene.MatchCamera.aspect = (float)MatchScene.CaptureWidth / MatchScene.CaptureHeight;
        }

        private static int RunAnimationCheck()
        {
            int failures = 0;
            MatchAnimationConsumer.ForceEnabled = true;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject root = new GameObject("UBlade3 Native Animation Trace");
            MatchScene scene = root.AddComponent<MatchScene>();
            MatchGameplay gameplay = root.AddComponent<MatchGameplay>();
            MatchPlay play = root.AddComponent<MatchPlay>();

            scene.Build();
            gameplay.Build(scene);
            play.Build(scene, gameplay);

            MatchAnimationConsumer consumer = scene.Animation;

            if (consumer == null)
            {
                consumer = scene.EnableAnimationConsumer();
            }

            if (consumer == null)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_ANIMATION_MISMATCH consumer=missing");
                return failures;
            }

            Debug.Log("UBLADE3_NATIVE_ANIMATION_REGISTRY " + consumer.Registry.Describe());

            if (consumer.Registry.Count < 240 || consumer.Registry.Count != 249)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH registry=" + consumer.Registry.Count
                          + " expected=249");
            }

            int captures = 0;
            int lastClipChanges = -1;
            int lastEvents = -1;
            int lastRequests = -1;

            for (int tick = 0; tick < 360; tick++)
            {
                HeldBinding input = MatchScene.DeterministicInput(scene.TickCount);
                play.TickPlay(input);

                consumer.RefreshCounters();

                if (consumer.ClipChanges != lastClipChanges)
                {
                    lastClipChanges = consumer.ClipChanges;
                    Debug.Log("UBLADE3_NATIVE_ANIMATION_CHANGE tick=" + scene.TickCount
                              + " clipChanges=" + consumer.ClipChanges
                              + " snapshot=\"" + consumer.Snapshot() + "\"");
                }

                if (consumer.EventCount != lastEvents)
                {
                    lastEvents = consumer.EventCount;
                    Debug.Log("UBLADE3_NATIVE_ANIMATION_EVENT tick=" + scene.TickCount
                              + " count=" + consumer.EventCount + " events=\""
                              + string.Join(" | ", consumer.EventLines.ToArray()) + "\"");
                }

                if (consumer.PlanAnimationRequests != lastRequests)
                {
                    lastRequests = consumer.PlanAnimationRequests;
                    Debug.Log("UBLADE3_NATIVE_ANIMATION_PLAN_REQUEST tick=" + scene.TickCount
                              + " requests=" + lastRequests + " applied="
                              + consumer.PlanAnimationApplied);
                }
            }

            // The natural plan trace above resolves action 30 and behaviour 9. These shipped
            // animation ids exercise the other categories through the same consumer path:
            // celebration id 0 AA_CelbAjdn, throw id 49 AA_OvhndThrw, launch id 225 AA_TorsoLaunch,
            // fall/knockdown id 11 AA_Fall, idle id 151 AA_idle1, goalie idle id 183 AA_Goalidle.
            int[] fixtureSlots = { 1, 2, 3, 4, 0, 5 };
            int[] fixtureIds = { 0, 49, 225, 11, 151, 183 };
            string[] fixtureSources =
            {
                "behparam11-celebration", "behparam11-throw", "action30-launch",
                "action30-fall", "behparam11-idle", "behparam9-goalie-idle",
            };

            for (int i = 0; i < fixtureIds.Length; i++)
            {
                int before = consumer.ClipChanges;
                bool changed = consumer.PlayFixture(fixtureSlots[i], fixtureIds[i], 1.0f, 0,
                    fixtureSources[i]);

                for (int tick = 0; tick < 12; tick++)
                {
                    play.TickPlay(HeldBinding.None);
                }

                consumer.RefreshCounters();
                Debug.Log("UBLADE3_NATIVE_ANIMATION_FIXTURE index=" + i
                          + " slot=" + fixtureSlots[i] + " id=" + fixtureIds[i]
                          + " source=" + fixtureSources[i] + " changed=" + (changed ? "True" : "False")
                          + " clipChangesBefore=" + before
                          + " clipChangesAfter=" + consumer.ClipChanges
                          + " events=" + consumer.EventCount
                          + " snapshot=\"" + consumer.Snapshot() + "\"");

                if (captures < AnimationCaptureFiles.Length && consumer.ClipChanges > before)
                {
                    scene.ApplyRender(1.0f);
                    PlaceAnimationCaptureCamera(scene, fixtureSlots[i]);
                    string path = ResolveArtifactPath(AnimationCaptureFiles[captures]);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

                    DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                        scene.MatchCamera, path, MatchScene.CaptureWidth, MatchScene.CaptureHeight);

                    Debug.Log("UBLADE3_NATIVE_ANIMATION_CAPTURE index=" + captures
                              + " tick=" + scene.TickCount + " path=" + result.Path
                              + " frame=" + result.Width + "x" + result.Height
                              + " bytes=" + result.Bytes
                              + " clipChanges=" + consumer.ClipChanges
                              + " snapshot=\"" + consumer.Snapshot() + "\"");
                    captures++;
                }
            }

            consumer.RefreshCounters();
            Debug.Log("UBLADE3_NATIVE_ANIMATION_STATE " + consumer.Describe());
            Debug.Log(consumer.TraceText(160));

            if (consumer.PlanSlots <= 0 || consumer.Roster == null)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH plan-roster");
            }

            if (consumer.PlanAnimationRequests <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH plan-animation-requests");
            }

            if (consumer.PlanAnimationApplied <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH plan-animation-applied");
            }

            if (consumer.ClipChanges < 2)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH clip-changes=" + consumer.ClipChanges);
            }

            if (consumer.ResolvedPlanIds.Count <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH resolved-ids");
            }

            if (consumer.EventCount <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH animation-events=0");
            }

            if (captures < 2)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_ANIMATION_MISMATCH captures=" + captures);
            }

            Debug.Log("UBLADE3_NATIVE_ANIMATION_CHECK_COUNTS planSlots=" + consumer.PlanSlots
                      + " planRequests=" + consumer.PlanAnimationRequests
                      + " planApplied=" + consumer.PlanAnimationApplied
                      + " behaviorChanges=" + consumer.BehaviorChanges
                      + " persistentChanges=" + consumer.PersistentChanges
                      + " clipChanges=" + consumer.ClipChanges
                      + " events=" + consumer.EventCount
                      + " resolvedIds=" + consumer.ResolvedPlanIds.Count
                      + " unsupported=" + consumer.UnsupportedCount
                      + " captures=" + captures
                      + " unresolvedPlanUnsupported=" + consumer.Unsupported.Count);

            return failures;
        }

        // --- N15: native C# preview 009 (default plan-animation consumer) ---------------

        public static string Preview009PlayerPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, "build", "preview-009",
                "UBlade3NativePreview009.exe"));
        }

        public static void BuildPreview009()
        {
            int exitCode = 0;

            try
            {
                string path = BuildPlayer(Preview009PlayerPath());
                Debug.Log("UBLADE3_NATIVE_PREVIEW_009_BUILD_OK output=" + path
                          + " defaultAnimation=interactive defaultAudio=interactive defaultHud=original");
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PREVIEW_009_BUILD_FAILED " + exception);
                Debug.LogException(exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // --- N16: stat accumulator, bubble adapter, animation message bridge --------

        public static void CheckEffects()
        {
            int failures = 0;

            try
            {
                failures += RunEffectsCheck();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_EFFECTS_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_EFFECTS_CHECK_OK"
                : "UBLADE3_NATIVE_EFFECTS_CHECK_FAILED failures=" + failures);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int RunEffectsCheck()
        {
            int failures = 0;
            MatchAnimationConsumer.ForceEnabled = true;
            MatchEffectsDriver.ForceEnabled = false;

            // The N12 default-PlanVM scenario is the shipped-plan window that actually executes
            // action 9 IncrementStat and action 71 ScaleBubToRating. Reusing its stage/ticks keeps the
            // stat/bubble evidence deterministic rather than synthesizing values.
            MatchPlay play = CreateDefaultPlayScene();
            DisableLegacyN13Handlers(play);
            MatchPlayN11Script.Stage(play.Control);

            MatchScene scene = play.Scene;
            MatchAnimationConsumer animation = scene.Animation;
            MatchPlanAiRoster roster = play.Control == null ? null : play.Control.PlanRoster;

            if (animation == null || roster == null)
            {
                Debug.LogError("UBLADE3_NATIVE_EFFECTS_MISMATCH animation=" + (animation != null)
                               + " roster=" + (roster != null));
                return 1;
            }

            MatchStatAccumulator stats = new MatchStatAccumulator();
            MatchBubbleAdapter bubbles = new MatchBubbleAdapter();
            MatchAnimationMessageBridge messages = new MatchAnimationMessageBridge();
            messages.Attach(animation, roster);
            int lastStats = 0;
            int lastBubbles = 0;
            int lastInjected = 0;
            int lastSync = 0;
            int captures = 0;

            // Baseline: the first tick records the already-present counters; later deltas are applied.
            stats.Tick(roster, scene.Sim.Sim);
            bubbles.Tick(roster, scene.Sim.Sim);

            for (int tick = 0; tick < 240; tick++)
            {
                play.Control.Advance(HeldBinding.None, held =>
                {
                    play.Scene.TickScene(held);
                    MatchPlayN11Script.HoldStaged(play.Control.Sim);
                });
                animation.RefreshCounters();
                stats.Tick(roster, scene.Sim.Sim);
                bubbles.Tick(roster, scene.Sim.Sim);

                if (stats.AppliedEvents != lastStats)
                {
                    lastStats = stats.AppliedEvents;
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_STAT_CHANGE tick=" + scene.TickCount
                              + " applied=" + stats.AppliedEvents
                              + " lastStat=" + stats.LastStatId + " lastSlot=" + stats.LastSlot
                              + " value=" + stats.LastValue.ToString("0.###")
                              + " state=\"" + stats.Describe() + "\"");
                }

                if (bubbles.AppliedEvents != lastBubbles)
                {
                    lastBubbles = bubbles.AppliedEvents;
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_BUBBLE_CHANGE tick=" + scene.TickCount
                              + " applied=" + bubbles.AppliedEvents
                              + " lastSlot=" + bubbles.LastSlot
                              + " radius=" + bubbles.LastRadius.ToString("0.####")
                              + " neighbours=" + bubbles.AwarenessPairs);
                }

                if (messages.InjectedMessages != lastInjected)
                {
                    lastInjected = messages.InjectedMessages;
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_MESSAGE_CHANGE tick=" + scene.TickCount
                              + " injected=" + lastInjected);
                }

                if (messages.SyncTriggers != lastSync)
                {
                    lastSync = messages.SyncTriggers;
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_SYNC_CHANGE tick=" + scene.TickCount
                              + " syncTriggers=" + lastSync);
                }
            }

            Debug.Log("UBLADE3_NATIVE_EFFECTS_STAT_STATE " + stats.Describe());
            Debug.Log(stats.TraceText(120));
            Debug.Log("UBLADE3_NATIVE_EFFECTS_BUBBLE_STATE " + bubbles.Describe());
            Debug.Log(bubbles.TraceText(80));
            Debug.Log("UBLADE3_NATIVE_EFFECTS_MESSAGE_STATE " + messages.Describe());
            Debug.Log(messages.TraceText(120));
            Debug.Log("UBLADE3_NATIVE_EFFECTS_ANIMATION_STATE " + animation.Describe());

            if (stats.AppliedEvents <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-stat-events (action 9 not observed)");
            }

            int nonzero = 0;

            for (int slot = 0; slot < MatchStatAccumulator.PlayerSlots; slot++)
            {
                float[] values = stats.GetStats(slot);

                for (int stat = 0; stat < MatchStatAccumulator.StatCount; stat++)
                {
                    if (values[stat] != 0.0f && !float.IsNaN(values[stat]))
                    {
                        nonzero++;
                    }
                }
            }

            if (stats.AppliedEvents > 0 && nonzero <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-nonzero-stat");
            }

            if (bubbles.AppliedEvents <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-bubble-events (action 71 not observed)");
            }

            if (messages.AttachedPlayers <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-animation-message-attach");
            }

            if (animation.SyncTransitions <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-sync-transitions");
            }

            // Real non-sync AnimAssm trigger through the live consumer, to exercise re-injection
            // without synthesizing a message.
            string triggerClipName;
            int triggerClipId;
            int triggerSlot = FindNonSyncTrigger(animation, out triggerClipName, out triggerClipId);

            if (triggerSlot >= 0)
            {
                int before = messages.InjectedMessages;
                animation.PlayFixture(1, triggerClipId, 1.0f, 0, "effects-trigger-fixture");
                Debug.Log("UBLADE3_NATIVE_EFFECTS_TRIGGER_FIXTURE id=" + triggerClipId
                          + " name=" + triggerClipName + " triggerSlot=" + triggerSlot);

                for (int i = 0; i < 80; i++)
                {
                    play.TickPlay(HeldBinding.None);
                }

                animation.RefreshCounters();
                Debug.Log("UBLADE3_NATIVE_EFFECTS_MESSAGE_AFTER " + messages.Describe());

                if (messages.InjectedMessages <= before)
                {
                    failures++;
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_MISMATCH no-trigger-reinjection");
                }
            }
            else
            {
                Debug.Log("UBLADE3_NATIVE_EFFECTS_GAP no-non-sync-trigger-in-registry");
            }

            string multiName;
            int multiId = FindMultiSegmentClip(animation, out multiName);

            if (multiId >= 0)
            {
                animation.PlayFixture(1, multiId, 1.0f, 0, "effects-multisegment-fixture");

                for (int i = 0; i < 220; i++)
                {
                    play.TickPlay(HeldBinding.None);
                }

                animation.RefreshCounters();
                Debug.Log("UBLADE3_NATIVE_EFFECTS_SEGMENT_FIXTURE id=" + multiId
                          + " name=" + multiName
                          + " segmentTransitions=" + animation.SegmentTransitions
                          + " syncTransitions=" + animation.SyncTransitions
                          + " state=\"" + animation.Describe() + "\"");

                if (animation.SegmentTransitions <= 0)
                {
                    Debug.Log("UBLADE3_NATIVE_EFFECTS_GAP multi-segment-clip-did-not-transition id="
                              + multiId + " name=" + multiName);
                }
            }
            else
            {
                Debug.Log("UBLADE3_NATIVE_EFFECTS_GAP no-multi-segment-clip");
            }

            scene.ApplyRender(1.0f);
            PlaceAnimationCaptureCamera(scene, 1);
            string capturePath = ResolveArtifactPath("artifacts/native-effects-00.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capturePath)));
            DromePreviewCapture.Result capture = DromePreviewCapture.Capture(
                scene.MatchCamera, capturePath, MatchScene.CaptureWidth, MatchScene.CaptureHeight);
            Debug.Log("UBLADE3_NATIVE_EFFECTS_CAPTURE path=" + capture.Path + " bytes=" + capture.Bytes
                      + " statApplied=" + stats.AppliedEvents
                      + " bubbleApplied=" + bubbles.AppliedEvents
                      + " injected=" + messages.InjectedMessages
                      + " sync=" + messages.SyncTriggers);
            captures++;

            if (captures <= 0)
            {
                failures++;
            }

            Debug.Log("UBLADE3_NATIVE_EFFECTS_CHECK_COUNTS statApplied=" + stats.AppliedEvents
                      + " statNonzero=" + nonzero
                      + " bubbleApplied=" + bubbles.AppliedEvents
                      + " bubbleRadius=" + bubbles.LastRadius.ToString("0.####")
                      + " messagesInjected=" + messages.InjectedMessages
                      + " syncTriggers=" + messages.SyncTriggers
                      + " unmappedTriggers=" + messages.UnmappedTriggers
                      + " attachedPlayers=" + messages.AttachedPlayers
                      + " syncTransitions=" + animation.SyncTransitions
                      + " segmentTransitions=" + animation.SegmentTransitions
                      + " unsupported=" + animation.UnsupportedCount
                      + " captures=" + captures);

            return failures;
        }

        private static int FindMultiSegmentClip(MatchAnimationConsumer animation, out string clipName)
        {
            clipName = string.Empty;

            for (int id = 0; id < animation.Registry.Count; id++)
            {
                string name;
                AnimationAssembly assembly;

                if (!animation.Registry.TryResolve(id, out name, out assembly) || assembly == null
                    || assembly.Segments == null || assembly.Segments.Length < 2)
                {
                    continue;
                }

                if (assembly.Segments[0].LoopCount == 0)
                {
                    continue;
                }

                bool hasSync = false;

                for (int i = 0; i < assembly.Triggers.Length; i++)
                {
                    if (assembly.Triggers[i].HasSync)
                    {
                        hasSync = true;
                        break;
                    }
                }

                if (!hasSync)
                {
                    clipName = name;
                    return id;
                }
            }

            return -1;
        }

        private static int FindNonSyncTrigger(MatchAnimationConsumer animation, out string clipName,
            out int clipId)
        {
            clipName = string.Empty;
            clipId = -1;

            for (int id = 0; id < animation.Registry.Count; id++)
            {
                string name;
                AnimationAssembly assembly;

                if (!animation.Registry.TryResolve(id, out name, out assembly) || assembly == null)
                {
                    continue;
                }

                for (int i = 0; i < assembly.Triggers.Length; i++)
                {
                    AnimationAssemblyTrigger trigger = assembly.Triggers[i];

                    if (!trigger.HasSync && trigger.Segment == 0
                        && !string.IsNullOrEmpty(trigger.ActionName))
                    {
                        string action = trigger.ActionName;

                        if (action.StartsWith("id_MSG_", StringComparison.OrdinalIgnoreCase))
                        {
                            string messageName = action.Substring("id_MSG_".Length);

                            if (PlanOpcodes.MessageNames.ContainsValue(messageName))
                            {
                                clipName = name;
                                clipId = id;
                                return trigger.Segment;
                            }
                        }
                    }
                }
            }

            return -1;
        }

        // --- N16: native C# preview 010 (remaining PlanVM effects) ---------------------

        public static string Preview010PlayerPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, "build", "preview-010",
                "UBlade3NativePreview010.exe"));
        }

        public static void BuildPreview010()
        {
            int exitCode = 0;

            try
            {
                string path = BuildPlayer(Preview010PlayerPath());
                Debug.Log("UBLADE3_NATIVE_PREVIEW_010_BUILD_OK output=" + path
                          + " defaultAnimation=interactive defaultAudio=interactive"
                          + " defaultHud=original defaultEffects=interactive");
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PREVIEW_010_BUILD_FAILED " + exception);
                Debug.LogException(exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }


        // --- N17: MatchStats read seam, Sim awareness bubbles, animation trigger map ----------

        public static void CheckN17()
        {
            int failures = 0;

            try
            {
                failures += RunN17Check();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N17_CHECK_FAILED " + exception);
                Debug.LogException(exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N17_CHECK_OK"
                : "UBLADE3_NATIVE_MATCH_N17_CHECK_FAILED failures=" + failures);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int RunN17Check()
        {
            int failures = 0;
            MatchAnimationConsumer.ForceEnabled = true;
            MatchEffectsDriver.ForceEnabled = false;
            MatchPlay play = CreateDefaultPlayScene();

            if (play.Control == null || play.Control.PlanRoster == null || play.Control.PlanRoster.MatchStats == null)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_N17_MISMATCH no default PlanVM/MatchStats roster");
                return 1;
            }

            MatchScene scene = play.Scene;
            MatchPlanAiRoster roster = play.Control.PlanRoster;
            MatchPlayN11Script.Stage(play.Control);

            for (int guard = 0; guard < 300 && play.Control.Ticks < 240; guard++)
            {
                play.Control.Advance(HeldBinding.None, held =>
                {
                    play.Scene.TickScene(held);
                    MatchPlayN11Script.HoldStaged(play.Control.Sim);
                });
            }

            MatchStatAccumulator stats = roster.MatchStats;
            Debug.Log("UBLADE3_NATIVE_MATCH_N17_STATS " + stats.Describe());

            int statSlot = -1;
            int statId = -1;
            float statValue = 0.0f;

            for (int slot = 0; slot < MatchStatAccumulator.PlayerSlots && statSlot < 0; slot++)
            {
                float[] values = stats.GetStats(slot);

                for (int stat = 0; stat < MatchStatAccumulator.StatCount; stat++)
                {
                    if (values[stat] != 0.0f && !float.IsNaN(values[stat]))
                    {
                        statSlot = slot;
                        statId = stat;
                        statValue = values[stat];
                        break;
                    }
                }
            }

            int statReadBranch = 0;
            MatchPlanAi statReader = null;

            for (int probeSlot = 0; probeSlot < roster.PlanSlots.Count; probeSlot++)
            {
                statReader = roster.Adapter(roster.PlanSlots[probeSlot]);

                if (statReader != null)
                {
                    break;
                }
            }

            if (statSlot >= 0 && statReader != null)
            {
                statReadBranch = statReader.ProbeMatchStatDecision(statSlot, statId);
                Debug.Log("UBLADE3_NATIVE_MATCH_N17_STAT_DECISION slot=" + statSlot + " stat=" + statId
                          + " value=" + statValue.ToString("0.###") + " branch=" + statReadBranch
                          + " reads=" + PlanAiMatchStats.Reads
                          + " last=" + PlanAiMatchStats.LastSlot + "/" + PlanAiMatchStats.LastStatId
                          + "/" + PlanAiMatchStats.LastValue.ToString("0.###"));
            }

            if (statSlot < 0 || stats.AppliedEvents <= 0 || statReadBranch != 1 || PlanAiMatchStats.Reads <= 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N17_MISMATCH stat-read applied=" + stats.AppliedEvents
                          + " slot=" + statSlot + " branch=" + statReadBranch
                          + " reads=" + PlanAiMatchStats.Reads);
            }

            int bubbleOwner = -1;
            float bubbleRadius = -1.0f;

            for (int slot = 0; slot < ObjectConstants.PlayerObjectCount; slot++)
            {
                float radius = AwarenessBubbles.GetRadius(scene.Sim.Sim, slot);

                if (radius > 0.0f)
                {
                    bubbleOwner = slot;
                    bubbleRadius = radius;
                    break;
                }
            }

            if (bubbleOwner < 0)
            {
                failures++;
                Debug.Log("UBLADE3_NATIVE_MATCH_N17_MISMATCH no-Sim-awareness-radius (action 71 not applied)");
            }

            int bubbleOther = bubbleOwner >= 0 ? ((bubbleOwner + 1) % ObjectConstants.PlayerObjectCount) : -1;
            int bubblePairBefore = 0;

            if (bubbleOwner >= 0)
            {
                MatchPlayN10Script.SetPlayerAt(play.Control.Sim, bubbleOther,
                    scene.Sim.Sim.Players[bubbleOwner].Body.Pos.x + (bubbleRadius * 0.25f),
                    scene.Sim.Sim.Players[bubbleOwner].Body.Pos.y, 1.0f, 0.0f);
                System.Collections.Generic.List<AwarenessCollision> pairs =
                    new System.Collections.Generic.List<AwarenessCollision>();
                AwarenessBubbles.CollectCollisions(scene.Sim.Sim, pairs);
                bubblePairBefore = pairs.Count;
                int nearest = AwarenessBubbles.NearestOpponent(scene.Sim.Sim, bubbleOwner,
                    out int nearestSlot, out float nearestDistance);
                Debug.Log("UBLADE3_NATIVE_MATCH_N17_BUBBLE owner=" + bubbleOwner
                          + " radius=" + bubbleRadius.ToString("0.###")
                          + " pairs=" + bubblePairBefore
                          + " nearest=" + nearest + "/" + nearestSlot
                          + "/" + nearestDistance.ToString("0.###")
                          + " " + AwarenessBubbles.Describe(scene.Sim.Sim));
                float[] savedRadii = new float[ObjectConstants.PlayerObjectCount];

                for (int slot = 0; slot < savedRadii.Length; slot++)
                {
                    savedRadii[slot] = AwarenessBubbles.GetRadius(scene.Sim.Sim, slot);
                    AwarenessBubbles.SetRadius(scene.Sim.Sim, slot, 0.0f);
                }

                pairs.Clear();
                AwarenessBubbles.CollectCollisions(scene.Sim.Sim, pairs);
                int pairsZero = pairs.Count;

                for (int slot = 0; slot < savedRadii.Length; slot++)
                {
                    AwarenessBubbles.SetRadius(scene.Sim.Sim, slot, savedRadii[slot]);
                }

                if (bubblePairBefore <= 0 || pairsZero != 0)
                {
                    failures++;
                    Debug.Log("UBLADE3_NATIVE_MATCH_N17_MISMATCH bubble radius did not affect collision query:"
                              + " before=" + bubblePairBefore + " zero=" + pairsZero);
                }
            }

            // Animation trigger map: a YerDown trigger must inject, and the three non-message
            // action names must be counted exactly and never injected.
            MatchAnimationMessageBridge messages = new MatchAnimationMessageBridge();
            messages.Attach(scene.Animation, roster);
            int yerDownBefore = messages.YerDownInjected;
            int nonMessageBefore = messages.NonMessageTriggers;
            int injectedBefore = messages.InjectedMessages;
            int fixtureCount = 0;
            int yerDownClips = 0;
            int nonMessageClips = 0;

            string[] fixtureNames = { "id_MSG_YerDown", "Contact", "Catch", "release" };

            for (int index = 0; index < fixtureNames.Length; index++)
            {
                string clipName;
                int clipId;
                int segment;

                if (!FindTriggerClip(scene.Animation, fixtureNames[index], out clipName, out clipId, out segment))
                {
                    Debug.Log("UBLADE3_NATIVE_MATCH_N17_TRIGGER_GAP action=" + fixtureNames[index]
                              + " (no clip trigger in the registry)");
                    continue;
                }

                fixtureCount++;
                int beforeNonMessage = messages.NonMessageTriggers;
                int beforeInjected = messages.InjectedMessages;
                scene.Animation.PlayFixture(1, clipId, 1.0f, 0, "n17-fixture-" + fixtureNames[index]);

                for (int tick = 0; tick < 120; tick++)
                {
                    play.TickPlay(HeldBinding.None);
                }

                scene.Animation.RefreshCounters();
                Debug.Log("UBLADE3_NATIVE_MATCH_N17_TRIGGER_FIXTURE action=" + fixtureNames[index]
                          + " clip=" + clipId + " name=" + clipName + " segment=" + segment
                          + " injected+" + (messages.InjectedMessages - beforeInjected)
                          + " nonMessage+" + (messages.NonMessageTriggers - beforeNonMessage));

                if (string.Equals(fixtureNames[index], "id_MSG_YerDown", StringComparison.OrdinalIgnoreCase))
                {
                    yerDownClips++;

                    if (messages.YerDownInjected <= yerDownBefore)
                    {
                        failures++;
                        Debug.Log("UBLADE3_NATIVE_MATCH_N17_MISMATCH YerDown trigger did not inject");
                    }
                }
                else
                {
                    nonMessageClips++;

                    if (messages.NonMessageTriggers <= beforeNonMessage
                        || messages.InjectedMessages != beforeInjected)
                    {
                        failures++;
                        Debug.Log("UBLADE3_NATIVE_MATCH_N17_MISMATCH non-message trigger was not classified: "
                                  + fixtureNames[index]);
                    }
                }
            }

            if (messages.YerDownInjected <= yerDownBefore)
            {
                failures++;
            }

            if (yerDownClips < 1 || nonMessageClips < 1 || fixtureCount < 1)
            {
                failures++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_N17_RESULT defaultPlanAi=True ticks=" + play.Control.Ticks
                      + " statApplied=" + stats.AppliedEvents
                      + " statSlot=" + statSlot + " statId=" + statId
                      + " statBranch=" + statReadBranch + " statReads=" + PlanAiMatchStats.Reads
                      + " bubbleOwner=" + bubbleOwner + " bubbleRadius=" + bubbleRadius.ToString("0.###")
                      + " bubblePairs=" + bubblePairBefore
                      + " bubbleCollisions=" + AwarenessBubbles.CollisionsObserved
                      + " animationFiles=" + fixtureCount + " yerDownClips=" + yerDownClips
                      + " nonMessageClips=" + nonMessageClips
                      + " messages=" + messages.Describe());
            Debug.Log(messages.Describe());
            Debug.Log(messages.TraceText(80));
            Debug.Log("UBLADE3_NATIVE_MATCH_N17_COUNTS statApplied=" + stats.AppliedEvents
                      + " statReads=" + PlanAiMatchStats.Reads + " statBranch=" + statReadBranch
                      + " bubbleOwner=" + bubbleOwner + " bubblePairs=" + bubblePairBefore
                      + " bubbleCollisions=" + AwarenessBubbles.CollisionsObserved
                      + " messagesInjected=" + messages.InjectedMessages
                      + " yerDownInjected=" + (messages.YerDownInjected - yerDownBefore)
                      + " nonMessageTriggers=" + (messages.NonMessageTriggers - nonMessageBefore)
                      + " failures=" + failures);

            return failures;
        }

        private static bool FindTriggerClip(MatchAnimationConsumer animation, string action,
            out string clipName, out int clipId, out int segment)
        {
            clipName = string.Empty;
            clipId = -1;
            segment = -1;

            if (animation == null || animation.Registry == null)
            {
                return false;
            }

            for (int id = 0; id < animation.Registry.Count; id++)
            {
                string name;
                UBlade3.Assets.AnimationAssembly assembly;

                if (!animation.Registry.TryResolve(id, out name, out assembly) || assembly == null
                    || assembly.Triggers == null)
                {
                    continue;
                }

                for (int index = 0; index < assembly.Triggers.Length; index++)
                {
                    UBlade3.Assets.AnimationAssemblyTrigger trigger = assembly.Triggers[index];

                    if (string.Equals(trigger.ActionName, action, StringComparison.OrdinalIgnoreCase))
                    {
                        clipName = name;
                        clipId = id;
                        segment = trigger.Segment;
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
