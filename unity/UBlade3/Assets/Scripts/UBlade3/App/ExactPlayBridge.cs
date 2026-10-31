using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UBlade3.App
{
    // Plays the exact HyperBlade engine through the bundled 20XX runtime. The Unity host stages the
    // runtime beside the user's game data, launches it, and reparents the SDL window into the Unity
    // window when Windows allows it. This is the fully playable exact path while the C# renderer port
    // is in progress; the native runtime is LGPL-3.0 and contains no original game data.
    public sealed class ExactPlayBridge : MonoBehaviour
    {
        public enum State { Idle, Staging, Launching, Embedded, External, Failed }

        private const string RuntimeFolder = "20xx";
        private const string StageFolderName = "UBlade3ExactPlay";

        public State CurrentState { get; private set; } = State.Idle;
        public string LastMessage { get; private set; } = "exact runtime ready";
        public bool IsRunning { get { return process != null && !process.HasExited; } }

        private Process process;
        private IntPtr unityWindow;
        private IntPtr childWindow;
        private string gameRoot;
        private float pollTimer;

        private void Awake()
        {
            unityWindow = GetActiveWindow();
            if (unityWindow == IntPtr.Zero) unityWindow = FindWindow(null, Application.productName);
            gameRoot = ResolveGameRoot();
        }

        public static string ResolveGameRoot()
        {
            var env = Environment.GetEnvironmentVariable("HYPERBLADE_GAME_DATA");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(Path.Combine(env, "data"))) return env;
            var streamed = Path.Combine(Application.streamingAssetsPath, "game-data-path.txt");
            if (File.Exists(streamed))
            {
                foreach (var raw in File.ReadAllLines(streamed))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (Directory.Exists(Path.Combine(line, "data"))) return line;
                }
            }
            return string.Empty;
        }

        public static string ResolveGameExecutable(string root)
        {
            foreach (var name in new[] { "HYPERX.EXE", "HyperX-XP.exe", "hyperx.exe" })
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path)) return path;
            }
            return string.Empty;
        }

        public void Launch()
        {
            if (IsRunning)
            {
                FocusChild();
                return;
            }
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            {
                CurrentState = State.Failed;
                LastMessage = "GAME DATA NOT FOUND: set HYPERBLADE_GAME_DATA to the HyperBlade install (the folder containing data).";
                return;
            }
            CurrentState = State.Staging;
            try
            {
                var stage = PrepareStage(gameRoot);
                CurrentState = State.Launching;
                LogExact("stage=" + stage);
                var info = new ProcessStartInfo(Path.Combine(stage, "hyperblade_20XX.exe"))
                {
                    WorkingDirectory = stage,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                process = Process.Start(info);
                if (process == null) throw new InvalidOperationException("20XX process did not start");
                LogExact("started pid=" + process.Id);
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) => { CurrentState = State.Idle; LastMessage = "exact game exited"; };
                StartCoroutine(EmbedWhenReady());
            }
            catch (Exception exception)
            {
                CurrentState = State.Failed;
                LastMessage = "EXACT RUNTIME FAILED: " + exception.Message;
                UnityEngine.Debug.LogError(LastMessage);
            }
        }

        private System.Collections.IEnumerator EmbedWhenReady()
        {
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForSeconds(0.25f);
                if (process == null || process.HasExited) yield break;
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) handle = FindWindowForProcess(process.Id);
                if (handle == IntPtr.Zero) continue;
                // The exact runtime is a complete SDL application with its own menus and input.
                // Give it the foreground window and hide the Unity host while it runs. Reparenting
                // an SDL window is fragile, so the stable playable path is a dedicated game window.
                childWindow = handle;
                if (unityWindow != IntPtr.Zero) ShowWindow(unityWindow, 0);
                ShowWindow(handle, 3);
                SetForegroundWindow(handle);
                CurrentState = State.External;
                LastMessage = "exact HyperBlade running; quit the game to return to UBlade3";
                LogExact("external hwnd=" + handle);
                while (process != null && !process.HasExited) yield return new WaitForSeconds(0.25f);
                if (unityWindow != IntPtr.Zero) ShowWindow(unityWindow, 5);
                CurrentState = State.Idle;
                LastMessage = "exact game exited";
                yield break;
            }
            CurrentState = State.Failed;
            LastMessage = "exact runtime did not create a window";
        }

        private static string PrepareStage(string root)
        {
            var runtimeSource = Path.Combine(Application.streamingAssetsPath, RuntimeFolder);
            if (!Directory.Exists(runtimeSource)) throw new DirectoryNotFoundException("bundled 20xx runtime missing: " + runtimeSource);
            var stage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UBlade3", StageFolderName);
            Directory.CreateDirectory(stage);
            foreach (var name in new[] { "hyperblade_20XX.exe", "20XX_data.pak", "SDL3.dll" })
            {
                File.Copy(Path.Combine(runtimeSource, name), Path.Combine(stage, name), true);
            }
            var exe = ResolveGameExecutable(root);
            if (string.IsNullOrEmpty(exe)) throw new FileNotFoundException("No HYPERX.EXE or HyperX-XP.exe in " + root);
            File.Copy(exe, Path.Combine(stage, "HYPERX.EXE"), true);
            var stageData = Path.Combine(stage, "data");
            if (!Directory.Exists(stageData))
            {
                var target = Path.Combine(root, "data");
                var link = StartHidden("cmd.exe", "/c mklink /J \"" + stageData + "\" \"" + target + "\"");
                link.WaitForExit(15000);
                if (!Directory.Exists(stageData))
                {
                    // Fallback: run beside the game install itself (adds only the platform files).
                    foreach (var name in new[] { "hyperblade_20XX.exe", "20XX_data.pak", "SDL3.dll" })
                        File.Copy(Path.Combine(runtimeSource, name), Path.Combine(root, name), true);
                    File.Copy(exe, Path.Combine(root, "HYPERX.EXE"), true);
                    return root;
                }
            }
            return stage;
        }

        public static void LogExact(string message)
        {
            UnityEngine.Debug.Log("UBLADE3_EXACT " + message);
        }

        private static IntPtr FindWindowForProcess(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner != (uint)processId || !IsWindowVisible(hWnd)) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        private static Process StartHidden(string file, string arguments)
        {
            var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            return Process.Start(info);
        }

        public void FocusChild()
        {
            if (childWindow != IntPtr.Zero) SetForegroundWindow(childWindow);
        }

        private void Update()
        {
            if (process == null || process.HasExited) return;
            pollTimer += Time.unscaledDeltaTime;
            if (pollTimer < 1f) return;
            pollTimer = 0f;
            if (CurrentState == State.Embedded && childWindow != IntPtr.Zero)
            {
                MoveWindow(childWindow, 0, 0, Screen.width, Screen.height, false);
            }
        }

        private void OnDestroy()
        {
            if (process != null && !process.HasExited)
            {
                try { process.Kill(); } catch { }
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    }
}
