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
    // Builds, runs and captures the N2 original-drome preview. Every entry point is callable from
    // the UBlade3 menu and from Unity batch mode with -executeMethod:
    //
    //   Unity.exe -batchmode -projectPath unity/UBlade3 \
    //       -executeMethod UBlade3.EditorTools.UBlade3DromePreview.CaptureAll \
    //       -logFile <log>
    //
    // The capture path needs a graphics device (run -batchmode WITHOUT -nographics). The Windows
    // preview build is the normal player path; it contains no 20XX process, DLL or launcher.
    public static class UBlade3DromePreview
    {
        public const string ScenePath = "Assets/Scenes/DromePreview.unity";

        public const string MaterialPath = "Assets/Scripts/UBlade3/Native/DromePreviewMaterial.mat";

        public const string CaptureDirectory = "artifacts";

        public const string ShadedCapturePath = "artifacts/drome-preview-chidro-shaded.png";

        public const string TexturedCapturePath = "artifacts/drome-preview-chidro-textured.png";

        public const string PlayerDirectory = "build-native/drome-preview";

        public const string PlayerExecutable = "build-native/drome-preview/UBlade3DromePreview.exe";

        // The repository root is three folders above Assets (Assets -> UBlade3 -> unity -> repo).
        private static string RepositoryRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        private static string UnityProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        // Capture paths are recorded relative to the repository root, not the process directory.
        private static string ResolveArtifactPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RepositoryRoot, path));
        }

        private static string ResolvePlayerPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(UnityProjectRoot, path));
        }

        [MenuItem("UBlade3/Drome Preview/Create Preview Scene")]
        public static void CreatePreviewScene()
        {
            CreateScene();
        }

        [MenuItem("UBlade3/Drome Preview/Capture Shaded Screenshot (1280x720)")]
        public static void CaptureShadedMenu()
        {
            CaptureMode(DromeShadingMode.Shaded, ShadedCapturePath, false);
        }

        [MenuItem("UBlade3/Drome Preview/Capture Textured Screenshot (1280x720)")]
        public static void CaptureTexturedMenu()
        {
            CaptureMode(DromeShadingMode.Textured, TexturedCapturePath, false);
        }

        [MenuItem("UBlade3/Drome Preview/Run Validation Probe")]
        public static void CheckMenu()
        {
            Check(false);
        }

        [MenuItem("UBlade3/Drome Preview/Build Windows Preview Player")]
        public static void BuildWindowsMenu()
        {
            BuildWindowsPlayer(false);
        }

        [MenuItem("UBlade3/Drome Preview/Build And Capture Windows Preview")]
        public static void BuildAndCaptureMenu()
        {
            BuildAndRunWindows(false);
        }

        // -executeMethod entry points.
        public static void CreateScene()
        {
            CreateSceneInternal();

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static void CaptureAll()
        {
            int failures = 0;

            try
            {
                OriginalAssetRoot root = DromePreviewScene.ResolveRoot();
                failures += DromePreviewProbe.Run(root, OriginalAssetRoot.DefaultDromeFile);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_FAILED probe: " + exception);
                failures++;
            }

            try
            {
                CaptureMode(DromeShadingMode.Shaded, ShadedCapturePath, true);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_CAPTURE_FAILED mode=shaded " + exception);
                failures++;
            }

            try
            {
                CaptureMode(DromeShadingMode.Textured, TexturedCapturePath, true);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_CAPTURE_FAILED mode=textured " + exception);
                failures++;
            }

            Debug.Log(failures == 0
                ? "UBLADE3_DROME_PREVIEW_CAPTURES_OK"
                : "UBLADE3_DROME_PREVIEW_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        public static void CaptureShaded()
        {
            CaptureAndExit(DromeShadingMode.Shaded, ShadedCapturePath);
        }

        public static void CaptureTextured()
        {
            CaptureAndExit(DromeShadingMode.Textured, TexturedCapturePath);
        }

        public static void Check()
        {
            Check(true);
        }

        public static void BuildWindows()
        {
            BuildWindowsPlayer(true);
        }

        public static void BuildAndRunWindows()
        {
            BuildAndRunWindows(true);
        }

        // Debug-only camera sweep used while settling the preview framing. Not part of the N2
        // evidence; the shipped capture entry points are CaptureShaded/CaptureTextured/CaptureAll.
        public static void DebugViews()
        {
            string directory = Path.Combine(RepositoryRoot, "artifacts", "drome-debug");
            Directory.CreateDirectory(directory);

            if (!File.Exists(ScenePath))
            {
                CreateSceneInternal();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            DromePreviewScene preview = UnityEngine.Object.FindAnyObjectByType<DromePreviewScene>();

            if (preview == null)
            {
                throw new InvalidOperationException("The drome preview scene has no DromePreviewScene component.");
            }

            if (preview.PreviewMaterial == null)
            {
                preview.PreviewMaterial = EnsureMaterialAsset();
            }

            // Unity space: x is the original lateral axis, y the original up, z is -y (long axis).
            // The .ENV's props sit between y = +/-192.5 (the goals) and x = +/-150; the arena shell
            // spans +/-375 x, +/-371 y and z -105..232, so the eye stays inside that shell.
            string[] names =
            {
                "field-north",
                "field-south",
                "high-field",
                "center-up",
                "goal-end",
                "mid-field",
                "side-high"
            };

            Vector3[] eyes =
            {
                new Vector3(0f, 30f, 120f),
                new Vector3(0f, 30f, -120f),
                new Vector3(0f, 90f, -180f),
                new Vector3(0f, 20f, 0f),
                new Vector3(0f, 25f, -150f),
                new Vector3(0f, 25f, -100f),
                new Vector3(120f, 55f, -60f)
            };

            Vector3[] targets =
            {
                new Vector3(0f, 10f, -120f),
                new Vector3(0f, 10f, 120f),
                new Vector3(0f, 10f, -100f),
                new Vector3(0f, 40f, 190f),
                new Vector3(0f, 0f, 192f),
                new Vector3(0f, 0f, 100f),
                new Vector3(0f, 10f, 60f)
            };

            DromeShadingMode[] modes = { DromeShadingMode.Shaded, DromeShadingMode.Textured };

            for (int m = 0; m < modes.Length; m++)
            {
                preview.ClearBuilt();
                preview.ShadingMode = modes[m];
                preview.Build();
                Camera camera = preview.EnsureCamera();

                for (int i = 0; i < names.Length; i++)
                {
                    camera.transform.position = eyes[i];
                    camera.transform.LookAt(targets[i], Vector3.up);

                    DromePreviewCapture.Capture(
                        camera,
                        Path.Combine(directory, ModeName(modes[m]) + "-" + names[i] + ".png"),
                        640,
                        360);
                }
            }

            preview.ClearBuilt();

            Debug.Log("UBLADE3_DROME_PREVIEW_DEBUG_VIEWS_OK dir=" + directory);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void CaptureAndExit(DromeShadingMode mode, string path)
        {
            int exitCode = 0;

            try
            {
                CaptureMode(mode, path, true);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_CAPTURE_FAILED mode=" + ModeName(mode) + " " + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // Creates the preview scene asset, the material asset that carries the preview shader into
        // player builds, and frames the preview camera. Generated meshes and textures are runtime
        // objects; the scene stores the loader component, the camera and the material reference.
        public static void CreateSceneInternal()
        {
            Directory.CreateDirectory("Assets/Scenes");

            Material material = EnsureMaterialAsset();
            Camera camera = null;
            Vector3 eye = Vector3.zero;
            Quaternion rotation = Quaternion.identity;

            if (File.Exists(ScenePath))
            {
                // Reuse the framed camera when the scene already exists.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                DromePreviewScene existing = UnityEngine.Object.FindAnyObjectByType<DromePreviewScene>();

                if (existing != null && existing.PreviewCamera != null)
                {
                    camera = existing.PreviewCamera;
                    eye = camera.transform.position;
                    rotation = camera.transform.rotation;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new GameObject("DromePreviewCamera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();

            GameObject root = new GameObject("UBlade3 Drome Preview");

            DromePreviewScene preview = root.AddComponent<DromePreviewScene>();
            preview.PreviewMaterial = material;
            preview.PreviewCamera = camera;
            preview.ShadingMode = DromeShadingMode.Shaded;

            // Build once so the saved camera is framed by the real drome bounds; the transient
            // meshes and textures are torn down before the scene is written.
            try
            {
                preview.Build();
                camera = preview.EnsureCamera();
                eye = camera.transform.position;
                rotation = camera.transform.rotation;
                preview.ClearBuilt();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("UBLADE3_DROME_PREVIEW_SCENE_NO_FRAME " + exception.Message);
                preview.ClearBuilt();
            }

            camera.transform.position = eye;
            camera.transform.rotation = rotation;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DromePreviewScene.ClearColour;
            camera.fieldOfView = DromePreviewScene.FieldOfView;
            camera.nearClipPlane = DromePreviewScene.NearClip;
            camera.farClipPlane = DromePreviewScene.FarClip;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("UBLADE3_DROME_PREVIEW_SCENE_OK path=" + ScenePath + " material=" + MaterialPath
                      + " camera=" + Format(eye) + " rotation=" + rotation.eulerAngles.ToString("0.###"));
        }

        public static Material EnsureMaterialAsset()
        {
            Shader shader = Shader.Find(DromePreviewScene.ShaderName);

            if (shader == null)
            {
                AssetDatabase.ImportAsset(
                    "Assets/Scripts/UBlade3/Native/DromePreview.shader",
                    ImportAssetOptions.ForceSynchronousImport);

                shader = Shader.Find(DromePreviewScene.ShaderName);
            }

            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The drome preview shader \"" + DromePreviewScene.ShaderName + "\" did not compile or import.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            if (material == null)
            {
                material = new Material(shader);
                material.name = "DromePreviewMaterial";
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();

            return material;
        }

        private static string ModeName(DromeShadingMode mode)
        {
            return mode == DromeShadingMode.Shaded ? "shaded" : "textured";
        }

        private static string Format(Vector3 value)
        {
            return value.x.ToString("0.###") + "," + value.y.ToString("0.###") + "," + value.z.ToString("0.###");
        }

        // Opens (or creates) the scene, assembles the original meshes and renders 1280x720 to a PNG.
        public static void CaptureMode(DromeShadingMode mode, string path, bool verbose)
        {
            if (!File.Exists(ScenePath))
            {
                CreateSceneInternal();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            DromePreviewScene preview = UnityEngine.Object.FindAnyObjectByType<DromePreviewScene>();

            if (preview == null)
            {
                throw new InvalidOperationException("The drome preview scene has no DromePreviewScene component: " + ScenePath);
            }

            preview.ClearBuilt();
            preview.ShadingMode = mode;

            if (preview.PreviewMaterial == null)
            {
                preview.PreviewMaterial = EnsureMaterialAsset();
            }

            DromeSceneData data = preview.Build();
            Camera camera = preview.EnsureCamera();

            string absolutePath = ResolveArtifactPath(path);

            DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                camera, absolutePath, DromePreviewCapture.DefaultWidth, DromePreviewCapture.DefaultHeight);

            Debug.Log("UBLADE3_DROME_PREVIEW_CAPTURE_OK mode=" + ModeName(mode)
                      + " path=" + result.Path
                      + " width=" + result.Width
                      + " height=" + result.Height
                      + " bytes=" + result.Bytes
                      + " objects=" + data.Drome.Objects.Count
                      + " meshes=" + data.MeshCount
                      + " builtVerts=" + data.BuiltVertices
                      + " triangles=" + data.BuiltTriangles
                      + " textures=" + data.TextureFiles.Count
                      + " missing=" + data.MissingTextures.Count
                      + " digest=" + data.Digest);

            if (verbose)
            {
                Debug.Log(data.Describe());
            }

            preview.ClearBuilt();
        }

        private static void Check(bool exitWhenBatch)
        {
            int mismatches;

            try
            {
                OriginalAssetRoot root = DromePreviewScene.ResolveRoot();
                mismatches = DromePreviewProbe.Run(root, OriginalAssetRoot.DefaultDromeFile);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_FAILED probe: " + exception);
                mismatches = 1;
            }

            Debug.Log(mismatches == 0
                ? "UBLADE3_DROME_PREVIEW_PROBE_OK"
                : "UBLADE3_DROME_PREVIEW_PROBE_FAILED mismatches=" + mismatches);

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(mismatches == 0 ? 0 : 1);
            }
        }

        private static void BuildWindowsPlayer(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                if (!File.Exists(ScenePath))
                {
                    CreateSceneInternal();
                }

                string playerPath = ResolvePlayerPath(PlayerExecutable);

                Directory.CreateDirectory(Path.GetDirectoryName(playerPath));

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("UBLADE3_DROME_PREVIEW_BUILD_FAILED " + report.summary.result);
                    exitCode = 1;
                }
                else
                {
                    Debug.Log("UBLADE3_DROME_PREVIEW_BUILD_OK bytes=" + report.summary.totalSize
                              + " output=" + report.summary.outputPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // Builds the Windows preview player, then runs it once per mode with --drome-capture= so the
        // screenshot comes from the same normal player path a release would use.
        private static void BuildAndRunWindows(bool exitWhenBatch)
        {
            int exitCode = 0;

            try
            {
                if (!File.Exists(ScenePath))
                {
                    CreateSceneInternal();
                }

                string playerPath = ResolvePlayerPath(PlayerExecutable);

                Directory.CreateDirectory(Path.GetDirectoryName(playerPath));

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Windows preview build failed: " + report.summary.result);
                }

                Debug.Log("UBLADE3_DROME_PREVIEW_BUILD_OK bytes=" + report.summary.totalSize
                          + " output=" + report.summary.outputPath);

                RunPlayer(DromeShadingMode.Shaded, ShadedCapturePath);
                RunPlayer(DromeShadingMode.Textured, TexturedCapturePath);

                Debug.Log("UBLADE3_DROME_PREVIEW_PLAYER_CAPTURES_OK");
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_BUILD_FAILED " + exception);
                exitCode = 1;
            }

            if (exitWhenBatch && Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void RunPlayer(DromeShadingMode mode, string capturePath)
        {
            string executable = ResolvePlayerPath(PlayerExecutable);

            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("The Windows preview player is missing.", executable);
            }

            string absoluteCapture = ResolveArtifactPath(capturePath);
            string directory = Path.GetDirectoryName(absoluteCapture);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(absoluteCapture))
            {
                File.Delete(absoluteCapture);
            }

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = executable;
            start.Arguments = "-batchmode -screen-width 1280 -screen-height 720 -screen-fullscreen 0"
                              + " --drome-mode=" + ModeName(mode)
                              + " --drome-capture=\"" + absoluteCapture + "\"";
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;

            UnityEngine.Debug.Log("UBLADE3_DROME_PREVIEW_PLAYER_START mode=" + ModeName(mode) + " " + start.Arguments);

            using (Process process = Process.Start(start))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("The Windows preview player could not be started.");
                }

                if (!process.WaitForExit(180000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                    }

                    throw new TimeoutException("The Windows preview player did not exit within 180 seconds.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("The Windows preview player exited with code " + process.ExitCode + ".");
                }
            }

            if (!File.Exists(absoluteCapture))
            {
                throw new FileNotFoundException("The Windows preview player did not write its capture.", absoluteCapture);
            }

            FileInfo info = new FileInfo(absoluteCapture);

            Debug.Log("UBLADE3_DROME_PREVIEW_PLAYER_CAPTURE_OK mode=" + ModeName(mode)
                      + " path=" + absoluteCapture + " bytes=" + info.Length);
        }
    }
}
