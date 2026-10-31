using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace UBlade3.App
{
    // Bootstrap for the exact-port lab. It reports the data path, the imported original data and the
    // simulation-core self-check without pretending the full game is already ported.
    public sealed class PortLab : MonoBehaviour
    {
        [SerializeField] private string gameDataPath = string.Empty;
        private string status = "booting";
        private string detail = string.Empty;
        private int frames;
        private string capturePath;
        private ExactPlayBridge exactPlay;
        private bool autoPlayExact;

        private void Awake()
        {
            capturePath = Arg("--port-lab-capture=");
            autoPlayExact = HasArg("--play-exact");
            gameDataPath = ResolveDataPath();
            TryLoadPort();
            exactPlay = FindObjectOfType<ExactPlayBridge>();
            if (exactPlay == null) exactPlay = gameObject.AddComponent<ExactPlayBridge>();
            if (autoPlayExact) exactPlay.Launch();
            Debug.Log("UBLADE3_START unity=" + Application.unityVersion + " data=" + gameDataPath + " capture=" + (capturePath ?? "none"));
        }

        public static void LogExact(string message)
        {
            Debug.Log("UBLADE3_EXACT " + message);
        }

        private static bool HasArg(string value) { return Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0; }

        private static string Arg(string prefix)
        {
            foreach (var value in Environment.GetCommandLineArgs())
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
            }
            return null;
        }

        private static string ResolveDataPath()
        {
            var env = Environment.GetEnvironmentVariable("HYPERBLADE_GAME_DATA");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(Path.Combine(env, "data"))) return env;
            var streamed = Path.Combine(Application.streamingAssetsPath, "game-data-path.txt");
            if (File.Exists(streamed))
            {
                foreach (var line in File.ReadAllLines(streamed))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (Directory.Exists(Path.Combine(trimmed, "data"))) return trimmed;
                }
            }
            return string.Empty;
        }

        private void TryLoadPort()
        {
            if (string.IsNullOrEmpty(gameDataPath))
            {
                status = "GAME DATA NOT FOUND";
                detail = "Set HYPERBLADE_GAME_DATA to the HyperBlade install root (the folder containing data\\) or write Assets/StreamingAssets/game-data-path.txt. Original assets stay outside this repository.";
                return;
            }
            status = "GAME DATA FOUND";
            detail = gameDataPath + "\n" + DescribeDataAssembly() + "\n" + DescribeSimulationAssembly();
        }

        private static string DescribeDataAssembly()
        {
            var type = Type.GetType("UBlade3.Data.DataProbe, Assembly-CSharp") ?? Type.GetType("UBlade3.Data.DataProbe");
            if (type == null) return "data port: not loaded yet";
            try
            {
                var method = type.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
                if (method != null) return "data port: " + method.Invoke(null, null);
                return "data port: " + type.FullName;
            }
            catch (Exception exception)
            {
                return "data port error: " + exception.Message;
            }
        }

        private static string DescribeSimulationAssembly()
        {
            var type = Type.GetType("UBlade3.Sim.SimSelfCheck, Assembly-CSharp") ?? Type.GetType("UBlade3.Sim.SimSelfCheck");
            if (type == null) return "sim port: not loaded yet";
            try
            {
                var method = type.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
                if (method != null) return "sim port: " + method.Invoke(null, null);
                return "sim port: " + type.FullName;
            }
            catch (Exception exception)
            {
                return "sim port error: " + exception.Message;
            }
        }

        private void Update()
        {
            frames++;
            if (!string.IsNullOrEmpty(capturePath) && frames == 30) StartCoroutine(CaptureRoutine());
        }

        private System.Collections.IEnumerator CaptureRoutine()
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(capturePath);
            yield return new WaitForSeconds(0.5f);
            Debug.Log("UBLADE3_CAPTURE_OK path=" + capturePath);
            Application.Quit(0);
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            GUILayout.BeginArea(new Rect(24, 24, Screen.width - 48, Screen.height - 48));
            GUILayout.Label("<b>ULTRABLADE 3 / HYPERBLADE EXACT PORT LAB</b>", style);
            GUILayout.Space(10);
            GUILayout.Label(status, style);
            GUILayout.Space(8);
            GUILayout.Label(detail, style);
            GUILayout.Space(16);
            GUILayout.Label("This build is the port foundation: Unity project + original data/probe + simulation self-check.", style);
            GUILayout.Label("Original HyperBlade code is being ported from third_party/hyperblade-decomp; no original assets are committed.", style);
            GUILayout.Space(18);
            if (exactPlay == null) exactPlay = FindObjectOfType<ExactPlayBridge>();
            if (exactPlay == null) exactPlay = gameObject.AddComponent<ExactPlayBridge>();
            GUILayout.Label("EXACT RUNTIME: " + exactPlay.CurrentState + " - " + exactPlay.LastMessage, style);
            if (!exactPlay.IsRunning)
            {
                if (GUILayout.Button("PLAY EXACT HYPERBLADE (native 20XX runtime)", GUILayout.Height(52f)))
                {
                    exactPlay.Launch();
                }
            }
            else
            {
                if (GUILayout.Button("FOCUS EXACT GAME WINDOW", GUILayout.Height(44f)))
                {
                    exactPlay.FocusChild();
                }
            }
            GUILayout.Label("Exact play uses the bundled LGPL-3.0 20XX runtime and your local original game data. The C# Unity port remains the editable code path.", style);
            GUILayout.EndArea();
        }
    }
}
