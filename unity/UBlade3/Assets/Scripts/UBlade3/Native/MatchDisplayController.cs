using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UBlade3.Native
{
    // N11 display/UI slice: persisted settings, startup application before the match scene builds,
    // validation/fallback, HUD scaling and a small runtime display control. The default settings file
    // lives in Application.persistentDataPath; command-line overrides are:
    //   --ublade3-display=1920x1080|native
    //   --ublade3-display-mode=windowed|borderless|fullscreen
    //   --ublade3-display-preset=1920x1080|2560x1440|3840x2160|native
    //   --ublade3-display-settings=<json path>
    //   --display-probe=<png path>  --display-probe-delay=<frames>
    // Runtime controls (non-probe): F10 cycles presets, F11 toggles windowed/borderless, F12 resets
    // the default. Every change is applied immediately and persisted.
    [DefaultExecutionOrder(-20000)]
    public sealed class MatchDisplayController : MonoBehaviour
    {
        public const string SettingsFileName = "ublade3-display.json";

        public const string DefaultSettingsPath = "";

        public MatchDisplaySettings Settings;

        public MatchDisplayPlan.Result Applied;

        public string SettingsPath = string.Empty;

        public bool ProbeMode;

        public string ProbePath = string.Empty;

        public int ProbeDelayFrames = 90;

        public float HudScale = 1f;

        private int startFrame;

        private int actualLoggedFrame = -1;

        private bool probeCaptured;

        private bool probeSceneBuilt;

        private int probeCaptureFrame = -1;

        private MatchHud hud;

        private int hudSearchFrame;

        private static MatchDisplayController instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("UBlade3 Display");
            UnityEngine.Object.DontDestroyOnLoad(host);
            instance = host.AddComponent<MatchDisplayController>();
        }

        private void Awake()
        {
            startFrame = Time.frameCount;
            ParseCommandLine();

            string detail;
            Settings = MatchDisplaySettings.Load(SettingsPath, out detail);
            Debug.Log("UBLADE3_DISPLAY_SETTINGS path=" + SettingsPath + " status=" + detail
                      + " settings=" + Settings.Describe());

            if (!string.IsNullOrEmpty(presetArgument))
            {
                Settings.ApplyPreset(presetArgument);
            }

            if (!string.IsNullOrEmpty(resolutionArgument))
            {
                if (string.Equals(resolutionArgument, "native", StringComparison.OrdinalIgnoreCase))
                {
                    Settings.ApplyPreset("native");
                }
                else if (MatchDisplaySettings.TryParseResolution(resolutionArgument, out int width, out int height))
                {
                    Settings.Width = width;
                    Settings.Height = height;
                    Settings.Preset = "custom";
                }
            }

            if (!string.IsNullOrEmpty(modeArgument))
            {
                Settings.Mode = MatchDisplaySettings.NormalizeMode(modeArgument);
            }

            Resolution desktop = Screen.currentResolution;
            Resolution[] supported = Screen.resolutions;

            Debug.Log("UBLADE3_DISPLAY_REQUESTED requested=" + Settings.Width + "x" + Settings.Height
                      + " preset=" + Settings.Preset
                      + " mode=" + Settings.Mode
                      + " desktop=" + desktop.width + "x" + desktop.height
                      + " supported=" + (supported == null ? 0 : supported.Length));

            Applied = MatchDisplayPlan.Validate(Settings, supported, desktop, null);

            Screen.SetResolution(Applied.Width, Applied.Height, ModeToUnity(Applied.Mode));

            Debug.Log("UBLADE3_DISPLAY_APPLIED requested=" + Settings.Width + "x" + Settings.Height
                      + " applied=" + Applied.Width + "x" + Applied.Height
                      + " mode=" + Applied.Mode
                      + " fallback=" + (Applied.Fallback ? "True" : "False")
                      + " reason=" + Applied.Reason
                      + " preset=" + Applied.Preset);

            if (Applied.Fallback)
            {
                Debug.Log("UBLADE3_DISPLAY_FALLBACK requested=" + Settings.Width + "x" + Settings.Height
                          + " applied=" + Applied.Width + "x" + Applied.Height
                          + " reason=" + Applied.Reason);
            }

            string saveDetail;
            bool saved = MatchDisplaySettings.Save(SettingsPath, Settings, out saveDetail);
            Debug.Log("UBLADE3_DISPLAY_SAVED path=" + SettingsPath + " status=" + saveDetail
                      + " saved=" + (saved ? "True" : "False"));

            if (ProbeMode)
            {
                Debug.Log("UBLADE3_DISPLAY_PROBE_READY screenshot=" + ProbePath
                          + " delayFrames=" + ProbeDelayFrames.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void Update()
        {
            if (Time.frameCount >= startFrame + 2 && actualLoggedFrame < 0)
            {
                actualLoggedFrame = Time.frameCount;
                LogActual();
            }

            if (hud == null && Time.frameCount >= hudSearchFrame)
            {
                hudSearchFrame = Time.frameCount + 30;
                hud = UnityEngine.Object.FindAnyObjectByType<MatchHud>();

                if (hud != null)
                {
                    Debug.Log("UBLADE3_DISPLAY_HUD hudScale=" + HudScale.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            if (hud != null)
            {
                hud.UiScale = HudScale;
            }

            if (ProbeMode)
            {
                TickProbe();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                CyclePreset();
            }
            else if (Input.GetKeyDown(KeyCode.F11))
            {
                ToggleWindowMode();
            }
            else if (Input.GetKeyDown(KeyCode.F12))
            {
                ResetToDefault();
            }
        }

        private void LogActual()
        {
            Resolution current = Screen.currentResolution;
            HudScale = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            Debug.Log("UBLADE3_DISPLAY_ACTUAL screen=" + Screen.width + "x" + Screen.height
                      + " currentResolution=" + current.width + "x" + current.height
                      + " fullScreenMode=" + Screen.fullScreenMode
                      + " hudScale=" + HudScale.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // The player scene has the match components, but MatchPlay.Start only auto-builds for the
        // N6/N7 capture entry points. The display probe asks for a live frame, so build the match once
        // here and then render its camera deterministically.
        private bool EnsureProbeScene()
        {
            if (probeSceneBuilt)
            {
                return true;
            }

            MatchScene scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();

            if (scene == null)
            {
                return false;
            }

            MatchGameplay gameplay = scene.GetComponent<MatchGameplay>();

            if (gameplay == null)
            {
                gameplay = scene.gameObject.AddComponent<MatchGameplay>();
            }

            MatchPlay play = scene.GetComponent<MatchPlay>();

            if (play == null)
            {
                play = scene.gameObject.AddComponent<MatchPlay>();
            }

            if (!scene.IsBuilt)
            {
                scene.Build();
            }

            if (!gameplay.IsBuilt)
            {
                gameplay.Build(scene);
            }

            if (!play.IsBuilt)
            {
                play.Build(scene, gameplay);
            }

            probeSceneBuilt = true;
            Debug.Log("UBLADE3_DISPLAY_PROBE_SCENE scene=" + scene.TickCount
                      + " hudBuilt=" + (gameplay.HUD != null && gameplay.HUD.Built ? "True" : "False")
                      + " playBuilt=" + (play.IsBuilt ? "True" : "False"));
            return true;
        }

        private void TickProbe()
        {
            if (!EnsureProbeScene())
            {
                return;
            }

            int elapsed = Time.frameCount - startFrame;

            if (!probeCaptured && elapsed >= ProbeDelayFrames)
            {
                if (WriteProbeFrame(ProbePath))
                {
                    probeCaptured = true;
                    probeCaptureFrame = Time.frameCount;
                    Debug.Log("UBLADE3_DISPLAY_PROBE_SCREENSHOT path=" + Path.GetFullPath(ProbePath)
                              + " screen=" + Screen.width + "x" + Screen.height
                              + " mode=" + Screen.fullScreenMode);
                }
            }

            if (probeCaptured && Time.frameCount > probeCaptureFrame + 3)
            {
                if (File.Exists(ProbePath))
                {
                    FileInfo info = new FileInfo(ProbePath);
                    Debug.Log("UBLADE3_DISPLAY_PROBE_OK screenshot=" + Path.GetFullPath(ProbePath)
                              + " bytes=" + info.Length
                              + " screen=" + Screen.width + "x" + Screen.height
                              + " fullScreenMode=" + Screen.fullScreenMode
                              + " hudScale=" + HudScale.ToString("0.###", CultureInfo.InvariantCulture)
                              + " settings=" + Settings.Describe()
                              + " fallback=" + (Applied.Fallback ? "True" : "False"));
                }
                else
                {
                    Debug.LogError("UBLADE3_DISPLAY_PROBE_FAILED screenshot-missing path="
                                   + Path.GetFullPath(ProbePath));
                }

                if (Application.isBatchMode || Application.isEditor)
                {
                    return;
                }

                Application.Quit(0);
            }
        }

        // Deterministic probe frame: render the live match camera at the actual screen size, then
        // composite the HUD at the display's UiScale. ScreenCapture.CaptureScreenshot was tried first
        // on this host but produced a black frame from a windowed player launched by a script, so the
        // probe uses the same RenderTexture+readback path as the N6/N7 capture suites.
        private bool WriteProbeFrame(string path)
        {
            MatchScene scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();
            MatchGameplay gameplay = UnityEngine.Object.FindAnyObjectByType<MatchGameplay>();

            if (scene == null || !scene.IsBuilt || scene.MatchCamera == null || gameplay == null
                || gameplay.HUD == null || !gameplay.HUD.Built)
            {
                return false;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            scene.ApplyRender(1.0f);

            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.name = "UBlade3 Display Probe";
            target.Create();

            RenderTexture savedActive = RenderTexture.active;
            RenderTexture savedTarget = scene.MatchCamera.targetTexture;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                scene.MatchCamera.targetTexture = target;
                scene.MatchCamera.aspect = (float)width / height;
                scene.MatchCamera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                Color32[] pixels = image.GetPixels32();
                gameplay.HUD.UiScale = HudScale;
                gameplay.HUD.Composite(pixels, width, height, gameplay.HudModel);
                image.SetPixels32(pixels);
                image.Apply(false, false);

                byte[] png = image.EncodeToPNG();
                File.WriteAllBytes(path, png);

                Debug.Log("UBLADE3_DISPLAY_PROBE_FRAME path=" + Path.GetFullPath(path)
                          + " frame=" + width + "x" + height
                          + " bytes=" + png.Length
                          + " hudScale=" + HudScale.ToString("0.###", CultureInfo.InvariantCulture));

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_DISPLAY_PROBE_FRAME_FAILED " + exception.GetType().Name + ": "
                               + exception.Message);
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

        private void CyclePreset()
        {
            int current = 0;

            for (int i = 0; i < MatchDisplaySettings.Presets.Length; i++)
            {
                if (string.Equals(MatchDisplaySettings.Presets[i].Id, Settings.Preset,
                        StringComparison.OrdinalIgnoreCase))
                {
                    current = i;
                }
            }

            int next = (current + 1) % MatchDisplaySettings.Presets.Length;
            Settings.ApplyPreset(MatchDisplaySettings.Presets[next].Id);
            ApplyAndPersist("hotkey-F10");
        }

        private void ToggleWindowMode()
        {
            Settings.Mode = Settings.Mode == "windowed" ? "borderless" : "windowed";
            ApplyAndPersist("hotkey-F11");
        }

        private void ResetToDefault()
        {
            Settings = MatchDisplaySettings.Default();
            ApplyAndPersist("hotkey-F12");
        }

        private void ApplyAndPersist(string source)
        {
            Resolution desktop = Screen.currentResolution;
            Resolution[] supported = Screen.resolutions;
            Applied = MatchDisplayPlan.Validate(Settings, supported, desktop, null);
            Screen.SetResolution(Applied.Width, Applied.Height, ModeToUnity(Applied.Mode));
            HudScale = Mathf.Clamp(Applied.Height / 720f, 1f, 3f);

            if (hud != null)
            {
                hud.UiScale = HudScale;
            }

            string saveDetail;
            MatchDisplaySettings.Save(SettingsPath, Settings, out saveDetail);

            Debug.Log("UBLADE3_DISPLAY_CHANGED source=" + source
                      + " requested=" + Settings.Width + "x" + Settings.Height
                      + " preset=" + Settings.Preset
                      + " mode=" + Settings.Mode
                      + " applied=" + Applied.Width + "x" + Applied.Height
                      + " fallback=" + (Applied.Fallback ? "True" : "False")
                      + " reason=" + Applied.Reason
                      + " save=" + saveDetail);

            actualLoggedFrame = -1;
            startFrame = Time.frameCount;
        }

        private static FullScreenMode ModeToUnity(string mode)
        {
            if (mode == "borderless")
            {
                return FullScreenMode.FullScreenWindow;
            }

            if (mode == "fullscreen")
            {
                return FullScreenMode.ExclusiveFullScreen;
            }

            return FullScreenMode.Windowed;
        }

        private string presetArgument;
        private string resolutionArgument;
        private string modeArgument;

        private void ParseCommandLine()
        {
            SettingsPath = Path.Combine(Application.persistentDataPath, SettingsFileName);
            ProbePath = string.Empty;
            ProbeMode = false;
            ProbeDelayFrames = 90;

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];

                if (argument.StartsWith("--ublade3-display=", StringComparison.Ordinal))
                {
                    resolutionArgument = argument.Substring("--ublade3-display=".Length);
                }
                else if (argument.StartsWith("--ublade3-display-preset=", StringComparison.Ordinal))
                {
                    presetArgument = argument.Substring("--ublade3-display-preset=".Length);
                }
                else if (argument.StartsWith("--ublade3-display-mode=", StringComparison.Ordinal))
                {
                    modeArgument = argument.Substring("--ublade3-display-mode=".Length);
                }
                else if (argument.StartsWith("--ublade3-display-settings=", StringComparison.Ordinal))
                {
                    SettingsPath = argument.Substring("--ublade3-display-settings=".Length);
                }
                else if (argument.StartsWith("--display-probe=", StringComparison.Ordinal))
                {
                    ProbePath = argument.Substring("--display-probe=".Length);
                    ProbeMode = !string.IsNullOrEmpty(ProbePath);
                }
                else if (argument.StartsWith("--display-probe-delay=", StringComparison.Ordinal))
                {
                    int.TryParse(argument.Substring("--display-probe-delay=".Length),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out ProbeDelayFrames);
                    ProbeDelayFrames = Math.Max(10, ProbeDelayFrames);
                }
            }

            if (string.IsNullOrEmpty(SettingsPath))
            {
                SettingsPath = Path.Combine(Application.persistentDataPath, SettingsFileName);
            }
        }

        public string Describe()
        {
            return "settings=" + (Settings == null ? "null" : Settings.Describe())
                   + " applied=" + (Settings == null ? "null"
                       : Applied.Width + "x" + Applied.Height + " mode=" + Applied.Mode)
                   + " fallback=" + (Applied.Fallback ? "True" : "False")
                   + " reason=" + Applied.Reason
                   + " screen=" + Screen.width + "x" + Screen.height
                   + " hudScale=" + HudScale.ToString("0.###", CultureInfo.InvariantCulture)
                   + " probe=" + (ProbeMode ? ProbePath : "off");
        }
    }
}
