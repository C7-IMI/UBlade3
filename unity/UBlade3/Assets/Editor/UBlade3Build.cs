using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UBlade3.EditorTools
{
    public static class UBlade3Build
    {
        private const string ScenePath = "Assets/Scenes/PortLab.unity";
        private const string NativeScenePath = "Assets/Scenes/NativeEngine.unity";

        [MenuItem("UBlade3/Create Port Lab Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.035f);
            cameraObject.transform.position = new Vector3(0f, 4f, -10f);
            cameraObject.transform.LookAt(Vector3.zero);

            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var app = new GameObject("UBlade3 Port Lab");
            app.AddComponent<UBlade3.App.PortLab>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("UBLADE3_SCENE_OK " + ScenePath);
        }

        public static void CreateNativeScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("NativeCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.cullingMask = 0;
            new GameObject("NativeEngineHost").AddComponent<UBlade3.Native.NativeEngineHost>();
            new GameObject("NativeAudioListener").AddComponent<AudioListener>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, NativeScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("UBLADE3_NATIVE_SCENE_OK " + NativeScenePath);
        }

        public static void BuildNativeWindows()
        {
            if (!File.Exists(NativeScenePath)) { CreateNativeScene(); }
            if (!File.Exists(NativeScenePath)) { Debug.LogError("UBLADE3_NATIVE_SCENE_MISSING " + NativeScenePath); EditorApplication.Exit(1); return; }
            Directory.CreateDirectory("build-native");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { NativeScenePath },
                locationPathName = Path.GetFullPath(Path.Combine("build-native", "UBlade3Native.exe")),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            }).summary;
            if (report.result != BuildResult.Succeeded)
            {
                Debug.LogError("UBLADE3_NATIVE_BUILD_FAILED " + report.result);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("UBLADE3_NATIVE_BUILD_OK bytes=" + report.totalSize + " output=" + report.outputPath);
        }

        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            Directory.CreateDirectory("build");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.GetFullPath(Path.Combine("build", "UBlade3.exe")),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            }).summary;
            if (report.result != BuildResult.Succeeded)
            {
                Debug.LogError("UBLADE3_BUILD_FAILED " + report.result);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("UBLADE3_BUILD_OK bytes=" + report.totalSize + " output=" + report.outputPath);
        }

        public static void Inspect()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            Debug.Log("UBLADE3_INSPECT_OK project=UBlade3 unity=" + Application.unityVersion);
        }
    }
}
