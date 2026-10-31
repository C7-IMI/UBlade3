using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UBlade3.Native;
using Debug = UnityEngine.Debug;

namespace UBlade3.EditorTools
{
    // Builds, plays and captures the N3 original-skater preview. Every entry point is callable from
    // the UBlade3 menu and from Unity batch mode with -executeMethod:
    //
    //   Unity.exe -batchmode -quit -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3SkaterPreview.CaptureAll \
    //       -logFile <temp>\ublade3-skater-preview.log
    //
    // The capture path needs a graphics device: run -batchmode WITHOUT -nographics. The scene is
    // built in memory from the player's own HyperBlade install; no scene asset, prefab or copy of
    // the original data is written to the repository. The capture poses come from the same
    // playback clock the scene's Update loop advances (SkaterPreviewScene.SampleTime): each
    // capture steps the clock by whole frames at the assembly's standard rate, so the frame shown
    // is the frame AnimPlayer would show at that elapsed time.
    public static class UBlade3SkaterPreview
    {
        public const string CaptureDirectory = "artifacts";

        public const int CaptureWidth = 1280;

        public const int CaptureHeight = 720;

        // The skater: AA_Skate_Std / azkskat2 "normal skate", 27 displayed frames at 30 fps.
        public const string SkaterFrame0Path = "artifacts/skater-preview-chiomx-frame000.png";

        public const string SkaterFrame9Path = "artifacts/skater-preview-chiomx-frame009.png";

        public const string SkaterFrame18Path = "artifacts/skater-preview-chiomx-frame018.png";

        // The goalie: AA_Goalidle / aplread2 "stand ready", 81 displayed frames at 30 fps.
        public const string GoalieFrame0Path = "artifacts/skater-preview-chigmx-frame000.png";

        public const string GoalieFrame40Path = "artifacts/skater-preview-chigmx-frame040.png";

        public const string LineupPath = "artifacts/skater-preview-lineup-frame009.png";

        // The repository root is three folders above Assets (Assets -> UBlade3 -> unity -> repo).
        private static string RepositoryRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        private static string ResolveArtifactPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RepositoryRoot, path));
        }

        [MenuItem("UBlade3/Skater Preview/Run Validation Probe")]
        public static void CheckMenu()
        {
            Check(false);
        }

        [MenuItem("UBlade3/Skater Preview/Capture 1280x720 Frames")]
        public static void CaptureAllMenu()
        {
            CaptureAllInternal(false);
        }

        [MenuItem("UBlade3/Skater Preview/Capture Skater Poses")]
        public static void CaptureSkaterMenu()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                CaptureInternal(true, false);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_CAPTURE_FAILED " + exception);
            }
        }

        [MenuItem("UBlade3/Skater Preview/Capture Goalie Pose")]
        public static void CaptureGoalieMenu()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                CaptureInternal(false, true);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_CAPTURE_FAILED " + exception);
            }
        }

        // -executeMethod entry points.
        public static void CaptureAll()
        {
            CaptureAllInternal(true);
        }

        public static void Check()
        {
            Check(true);
        }

        private static void Check(bool exitWhenBatch)
        {
            int mismatches;

            try
            {
                mismatches = SkaterPreviewProbe.Run(SkaterPreviewScene.ResolveRoot());
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_FAILED probe: " + exception);
                mismatches = 1;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_SKATER_PREVIEW_PROBE_PASSED"
                : "UBLADE3_SKATER_PREVIEW_PROBE_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static void CaptureAllInternal(bool exitWhenBatch)
        {
            int failures = 0;

            try
            {
                failures += SkaterPreviewProbe.Run(SkaterPreviewScene.ResolveRoot());
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_FAILED probe: " + exception);
                failures++;
            }

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures += CaptureInternal(true, true);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_CAPTURE_FAILED " + exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_SKATER_PREVIEW_CAPTURES_OK"
                : "UBLADE3_SKATER_PREVIEW_CAPTURES_FAILED failures=" + failures);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        // Builds the preview in the current (empty) scene and captures the requested characters.
        private static int CaptureInternal(bool captureSkater, bool captureGoalie)
        {
            OriginalAssetRoot root = SkaterPreviewScene.ResolveRoot();

            GameObject cameraObject = new GameObject("SkaterPreviewCamera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();

            GameObject rootObject = new GameObject("UBlade3 Skater Preview");
            SkaterPreviewScene preview = rootObject.AddComponent<SkaterPreviewScene>();
            preview.PreviewCamera = camera;
            preview.SkaterModel = SkaterPreviewData.ExpectedSkaterModel;
            preview.GoalieModel = SkaterPreviewData.ExpectedGoalieModel;
            preview.SkaterAssembly = SkaterPreviewData.ExpectedSkaterAssembly;
            preview.GoalieAssembly = SkaterPreviewData.ExpectedGoalieAssembly;
            preview.ModelSet = OriginalAssetSet();
            preview.IncludeGoalie = captureGoalie;

            SkaterPreviewData data = preview.Build();

            int failures = 0;
            int skaterFps = 30;
            int goalieFps = 30;

            // Whole-frame times at each assembly's standard rate: 0.0 s, 0.3 s (frame 9) and 0.6 s
            // (frame 18) for the skate loop; 40/30 s for the goalie stand. The scene's playback
            // clock is stepped to each time before the capture, exactly as its Update loop would.
            if (captureSkater)
            {
                failures += CaptureOne(preview, camera, "skater", 0.0, SkaterFrame0Path, true, false, data);
                failures += CaptureOne(preview, camera, "skater", 9.0 / skaterFps, SkaterFrame9Path, true, false, data);
                failures += CaptureOne(preview, camera, "skater", 18.0 / skaterFps, SkaterFrame18Path, true, false, data);
            }

            if (captureGoalie && data.Goalie != null)
            {
                failures += CaptureOne(preview, camera, "goalie", 0.0, GoalieFrame0Path, false, true, data);
                failures += CaptureOne(preview, camera, "goalie", 40.0 / goalieFps, GoalieFrame40Path, false, true, data);
            }

            if (captureSkater && captureGoalie && data.Goalie != null)
            {
                failures += CaptureOne(preview, camera, "lineup", 9.0 / 30.0, LineupPath, true, true, data);
            }

            Camera used = preview.EnsureCamera();
            Debug.Log("UBLADE3_SKATER_PREVIEW_SCENE_BUILT root=" + root.DataPath
                      + " camera=" + used.transform.position.ToString("F3")
                      + " skater=" + data.Skater.ModelName
                      + " goalie=" + (data.Goalie == null ? "none" : data.Goalie.ModelName)
                      + " digest=" + data.Digest);

            return failures;
        }

        private static OriginalModelSet OriginalAssetSet()
        {
            // The N3 preview draws the textured (Direct3D) model set: the original BMP textures and
            // the Mdltxt UVs the goal asks for. The shaded set remains available through
            // SkaterPreviewScene.ModelSet for a later comparison pass.
            return OriginalModelSet.Textured;
        }

        private static int CaptureOne(
            SkaterPreviewScene preview,
            Camera camera,
            string role,
            double seconds,
            string path,
            bool showSkater,
            bool showGoalie,
            SkaterPreviewData data)
        {
            try
            {
                preview.SetVisible(showSkater, showGoalie);
                preview.SampleTime(seconds);
                preview.ApplyFraming(showSkater, showGoalie);

                int skaterFrame = data.Skater.FrameAtSeconds(seconds);
                int goalieFrame = data.Goalie == null ? -1 : data.Goalie.FrameAtSeconds(seconds);

                string absolutePath = ResolveArtifactPath(path);
                DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                    camera, absolutePath, CaptureWidth, CaptureHeight);

                Debug.Log("UBLADE3_SKATER_PREVIEW_CAPTURE role=" + role
                          + " time=" + seconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                          + " skaterFrame=" + skaterFrame
                          + " goalieFrame=" + goalieFrame
                          + " path=" + result.Path
                          + " width=" + result.Width
                          + " height=" + result.Height
                          + " bytes=" + result.Bytes
                          + " digest=" + data.Digest);

                return 0;
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_CAPTURE_FAILED role=" + role + " path=" + path + " " + exception);
                return 1;
            }
        }
    }
}
