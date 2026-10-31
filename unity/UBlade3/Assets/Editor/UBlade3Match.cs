using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UBlade3.Native;
using Debug = UnityEngine.Debug;

namespace UBlade3.EditorTools
{
    // Builds, runs and captures the first native C# match scene. Every entry point is callable from
    // the UBlade3 menu and from Unity batch mode with -executeMethod:
    //
    //   Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3Match.Check -logFile <temp>\ublade3-match-check.log
    //
    //   Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3Match.CaptureAll -logFile <temp>\ublade3-match-capture.log
    //
    // The capture path needs a graphics device: run -batchmode WITHOUT -nographics. The scene is
    // built in memory from the player's own HyperBlade install; no scene asset, prefab or copy of the
    // original data is written to the repository. The deterministic input script is
    // MatchScene.DeterministicInput, so the five-second run is reproducible without a live device.
    public static class UBlade3Match
    {
        public const string CaptureDirectory = "artifacts";

        public const string PlayerExecutable = "build-match/UBlade3NativeMatch.exe";

        public const int CaptureWidth = MatchScene.CaptureWidth;

        public const int CaptureHeight = MatchScene.CaptureHeight;

        private const string TempScenePath = "Assets/Scenes/__UBlade3NativeMatchTemp.unity";

        private const string PreviewMaterialPath = "Assets/Scripts/UBlade3/Native/DromePreviewMaterial.mat";

        // The repository root is three folders above Assets (Assets -> UBlade3 -> unity -> repo).
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

        [MenuItem("UBlade3/Native Match/Run Validation Check")]
        public static void CheckMenu()
        {
            Check(false);
        }

        [MenuItem("UBlade3/Native Match/Capture 1280x720 Match Frames")]
        public static void CaptureAllMenu()
        {
            CaptureAll(false);
        }

        [MenuItem("UBlade3/Native Match/Build Windows Preview Player")]
        public static void BuildWindowsMenu()
        {
            BuildWindowsPlayer(false);
        }

        [MenuItem("UBlade3/Native Match/Build And Capture Windows Preview")]
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

        // --- check ------------------------------------------------------------------------

        private static void Check(bool exitWhenBatch)
        {
            int mismatches;

            try
            {
                mismatches = MatchProbe.RunCheck(MatchScene.ResolveRoot());
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CHECK_FAILED probe: " + exception);
                mismatches = 1;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_NATIVE_MATCH_PROBE_PASSED"
                : "UBLADE3_NATIVE_MATCH_PROBE_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        // --- capture ----------------------------------------------------------------------

        private static void CaptureAll(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += CaptureScene();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED " + exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_CAPTURES_OK frames=" + MatchScene.CaptureTickList.Length
                  + " ticks=" + string.Join(",", Array.ConvertAll(MatchScene.CaptureTickList, v => v.ToString()))
                  + " seconds=5 width=" + CaptureWidth + " height=" + CaptureHeight
                : "UBLADE3_NATIVE_MATCH_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        // Builds the match in the current (empty) scene, runs the deterministic five-second script and
        // captures each requested 1280x720 frame. Returns the failure count.
        private static int CaptureScene()
        {
            int failures = 0;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rootObject = new GameObject("UBlade3 Native Match");
            MatchScene scene = rootObject.AddComponent<MatchScene>();

            MatchWorldData world = scene.Build();

            Debug.Log("UBLADE3_NATIVE_MATCH_CAPTURE_DROME " + world.Drome.Describe());

            // One extra wide capture (not an acceptance chase frame): the N2 drome preview's own
            // framing, so the drome provenance/alignment can be read alongside the match frames. It
            // calls EnsureCamera(), which resets the near clip; the match camera is configured again
            // before the acceptance run.
            string wideDrome = ResolveArtifactPath(Path.Combine(CaptureDirectory, "native-match-drome.png"));

            if (File.Exists(wideDrome))
            {
                File.Delete(wideDrome);
            }

            scene.DromeScene.ApplyFraming();
            DromePreviewCapture.Result wide = DromePreviewCapture.Capture(scene.MatchCamera, wideDrome, CaptureWidth, CaptureHeight);
            scene.ConfigureCamera(scene.MatchCamera);
            Debug.Log("UBLADE3_NATIVE_MATCH_DROME_CAPTURE path=" + wide.Path + " bytes=" + wide.Bytes
                      + " width=" + wide.Width + " height=" + wide.Height);

            string[] paths = new string[MatchScene.CaptureFileList.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolveArtifactPath(MatchScene.CaptureFileList[i]);

                if (File.Exists(paths[i]))
                {
                    File.Delete(paths[i]);
                }
            }

            failures += scene.RunDeterministicCapture(paths, MatchScene.CaptureTickList);
            Debug.Log(MatchProbe.DescribeScene(scene));
            Debug.Log(scene.DescribeScene());

            for (int i = 0; i < scene.Captures.Count; i++)
            {
                MatchCaptureRecord record = scene.Captures[i];

                if (!File.Exists(record.Path))
                {
                    Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED missing file " + record.Path);
                    failures++;
                    continue;
                }

                FileInfo info = new FileInfo(record.Path);

                if (info.Length != record.Bytes || info.Length < 1024)
                {
                    Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED " + record.Path + " has "
                                   + info.Length + " bytes (record " + record.Bytes + ")");
                    failures++;
                }

                if (record.Width != CaptureWidth || record.Height != CaptureHeight)
                {
                    Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED " + record.Path + " is "
                                   + record.Width + "x" + record.Height);
                    failures++;
                }

                if (record.Tick < MatchScene.CaptureTickList[i])
                {
                    Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED " + record.Path + " shows tick "
                                   + record.Tick + " before the requested " + MatchScene.CaptureTickList[i]);
                    failures++;
                }
            }

            if (scene.Captures.Count != MatchScene.CaptureTickList.Length)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED captured " + scene.Captures.Count
                               + " of " + MatchScene.CaptureTickList.Length + " frames");
                failures++;
            }

            MatchCaptureRecord last = scene.Captures.Count == 0 ? null : scene.Captures[scene.Captures.Count - 1];

            if (last == null || last.Tick < MatchScene.CaptureTickCount)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED the last capture is before the 5 s floor");
                failures++;
            }
            else if (last.HumanDisplacement < MatchProbe.RequiredHumanDisplacement)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED the human skater moved only "
                               + last.HumanDisplacement + " units");
                failures++;
            }

            int builtViews = 0;

            if (scene.Views != null)
            {
                for (int slot = 0; slot < scene.Views.Length; slot++)
                {
                    if (scene.Views[slot] != null && scene.Views[slot].IsBuilt)
                    {
                        builtViews++;
                    }
                }
            }

            if (builtViews != world.Players.Length)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED built " + builtViews + " of "
                               + world.Players.Length + " skater views");
                failures++;
            }

            Debug.Log("UBLADE3_NATIVE_MATCH_CAPTURE_EVIDENCE frames=" + scene.Captures.Count
                      + " views=" + builtViews
                      + " ticks=" + scene.TickCount
                      + " seconds=" + scene.MatchSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                      + " missingRefs=" + scene.TotalMissingRefs()
                      + " digest=" + world.Digest);

            return failures;
        }

        // --- player build ------------------------------------------------------------------

        private static void BuildWindowsPlayer(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                string playerPath = BuildPlayer();

                Debug.Log("UBLADE3_NATIVE_MATCH_BUILD_OK output=" + playerPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // Builds the standalone Windows player from an in-memory scene. The scene asset is transient:
        // it exists only for the duration of the build and is removed afterwards, so no new project
        // asset is left behind. The original DromePreview material is referenced by a disabled anchor
        // object so the unlit original surface is not stripped from the player.
        private static string BuildPlayer()
        {
            string playerPath = ResolvePlayerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(playerPath));

            try
            {
                UnityEngine.SceneManagement.Scene scene =
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                GameObject root = new GameObject("UBlade3 Native Match");
                root.AddComponent<MatchScene>();

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

                Debug.Log("UBLADE3_NATIVE_MATCH_PLAYER_BUILD_OK bytes=" + report.summary.totalSize
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
                string capturePath = ResolveArtifactPath(Path.Combine(CaptureDirectory, "native-match-player.png"));
                string logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ublade3-native-match-player.log"));

                string directory = Path.GetDirectoryName(capturePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string[] expected = ExpectedPlayerCapturesFrom(capturePath);

                for (int i = 0; i < expected.Length; i++)
                {
                    if (File.Exists(expected[i]))
                    {
                        File.Delete(expected[i]);
                    }
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = playerPath;
                start.Arguments = "-batchmode -screen-width " + CaptureWidth + " -screen-height " + CaptureHeight
                                  + " -screen-fullscreen 0 --match-capture=\"" + capturePath + "\""
                                  + " --match-seconds=5 -logFile \"" + logPath + "\"";
                start.WorkingDirectory = Path.GetDirectoryName(playerPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;

                Debug.Log("UBLADE3_NATIVE_MATCH_PLAYER_START " + start.Arguments);

                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException("The Windows match player did not finish in 300 s.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("The Windows match player exited " + process.ExitCode
                                                            + "; log " + logPath);
                    }
                }

                for (int i = 0; i < expected.Length; i++)
                {
                    if (!File.Exists(expected[i]))
                    {
                        throw new FileNotFoundException("The Windows match player did not write " + expected[i], expected[i]);
                    }

                    FileInfo info = new FileInfo(expected[i]);
                    Debug.Log("UBLADE3_NATIVE_MATCH_PLAYER_CAPTURE path=" + expected[i] + " bytes=" + info.Length);
                }

                Debug.Log("UBLADE3_NATIVE_MATCH_PLAYER_CAPTURES_OK frames=" + expected.Length
                          + " output=" + playerPath + " log=" + logPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_PLAYER_CAPTURE_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static string[] ExpectedPlayerCapturesFrom(string capturePath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(capturePath));
            string baseName = Path.GetFileNameWithoutExtension(capturePath);
            string[] paths = new string[MatchScene.CaptureTickList.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = Path.Combine(directory, baseName + "-"
                                        + MatchScene.CaptureTickList[i].ToString("000")
                                        + ".png");
            }

            return paths;
        }
    }
}
