using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UBlade3.Sim;

namespace UBlade3.Native
{
    // N13 preview-006 probe. A normal interactive player build launches with:
    //   --n13-probe=<png>
    // It waits for the default interactive match build, verifies the default AudioService is ready
    // and actually plays the original event routes (goal/crowd/impact/menu/pickup/shot), drives the
    // human forward through the public TickPlay path (so the dynamic speed meter is live), captures
    // 1080p and 4K HUD frames, logs the HUD model, and exits 0. Audio silence is a logged fallback,
    // not a crash: a host without an audio device still produces the HUD frame and a clean exit.
    public sealed class MatchAudioHudProbe : MonoBehaviour
    {
        public const string ProbeSwitch = "--n13-probe=";
        public const string Marker = "UBLADE3_NATIVE_PREVIEW";

        private string probePath;
        private int delayFrames = 90;
        private int readyFrame = -1;
        private bool done;
        private int audioFrame = -1;
        private int audioEventFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(ProbeSwitch, StringComparison.Ordinal))
                {
                    GameObject host = new GameObject("UBlade3 Preview Probe");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    host.AddComponent<MatchAudioHudProbe>();
                    break;
                }
            }
        }

        private void Awake()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(ProbeSwitch, StringComparison.Ordinal))
                {
                    probePath = arguments[i].Substring(ProbeSwitch.Length);
                }
                else if (arguments[i].StartsWith("--n13-probe-delay=", StringComparison.Ordinal))
                {
                    int.TryParse(arguments[i].Substring("--n13-probe-delay=".Length),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out delayFrames);
                    delayFrames = Math.Max(10, delayFrames);
                }
            }
        }

        private void Update()
        {
            if (done)
            {
                return;
            }

            MatchScene scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();
            MatchPlay play = UnityEngine.Object.FindAnyObjectByType<MatchPlay>();

            if (scene == null || play == null || !play.IsBuilt || scene.Sim == null
                || scene.Sim.Sim == null)
            {
                return;
            }

            if (readyFrame < 0)
            {
                readyFrame = Time.frameCount;
                Debug.Log(Marker + "_READY tick=" + scene.TickCount
                          + " hudOriginal=" + (play.Gameplay != null && play.Gameplay.HUD != null
                              && play.Gameplay.HUD.OriginalLayout ? "True" : "False")
                          + " audioReady=" + (play.Audio != null && play.Audio.Ready ? "True" : "False"));
            }

            if (Time.frameCount < readyFrame + delayFrames)
            {
                return;
            }

            if (audioFrame < 0)
            {
                RunAudioProbe(play, scene);
                audioFrame = Time.frameCount;
                return;
            }

            if (audioEventFrame < 0 && Time.frameCount >= audioFrame + 20)
            {
                // Drive the real public input path for a short burst so the dynamic speed meter and
                // the HUD state are live rather than a pinned screenshot state.
                for (int i = 0; i < 45; i++)
                {
                    play.TickPlay(HeldBinding.Forward);
                }

                audioEventFrame = Time.frameCount;
                LogAudioResult(play);
                LogHudState(play, scene);
                return;
            }

            if (audioEventFrame >= 0 && Time.frameCount >= audioEventFrame + 10)
            {
                done = true;
                CaptureHud(play, scene, 1920, 1080, probePath);
                CaptureHud(play, scene, 3840, 2160, AppendSuffix(probePath, "-4k"));

                Debug.Log(Marker + "_PROBE_OK path=" + Path.GetFullPath(probePath)
                          + " audio=\"" + play.Audio.Describe() + "\""
                          + " hudOriginal=" + (play.Gameplay != null && play.Gameplay.HUD != null
                              && play.Gameplay.HUD.OriginalLayout ? "True" : "False"));

                if (!Application.isEditor)
                {
                    Application.Quit(0);
                }
            }
        }

        private void RunAudioProbe(MatchPlay play, MatchScene scene)
        {
            uint now = scene.Sim.Sim.Rules.Clock.TickMs;
            Debug.Log(Marker + "_AUDIO_BEFORE ready=" + (play.Audio != null && play.Audio.Ready ? "True" : "False")
                      + " device=\"" + MatchAudioDefaults.DescribeAudioDevice() + "\""
                      + " defaults=\"" + MatchAudioDefaults.Describe() + "\"");

            try
            {
                // The routes the interactive layer uses: pickup/shot/pass via the director's original
                // group mapping, plus the goal/crowd/menu/impact/ambient calls.
                play.Audio.Impact("rok-pickup", now);
                play.Audio.Impact("shot-release", now);
                play.Audio.Impact("pass-release", now);
                play.Audio.Impact("rok-bounce", now);
                play.Audio.Goal(1, true, 0, 0, now);
                play.Audio.Menu("n13-probe", now);
                play.Audio.AmbientStart();
            }
            catch (Exception exception)
            {
                Debug.LogError(Marker + "_AUDIO_EXCEPTION " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void LogAudioResult(MatchPlay play)
        {
            bool ready = play.Audio != null && play.Audio.Ready;
            Debug.Log(Marker + "_AUDIO_RESULT ready=" + (ready ? "True" : "False")
                      + " device=\"" + MatchAudioDefaults.DescribeAudioDevice() + "\""
                      + " defaults=\"" + MatchAudioDefaults.Describe() + "\""
                      + " counts=\"" + (play.Audio == null ? "none" : play.Audio.Describe()) + "\""
                      + " playedOk=" + (ready && play.Audio.PlayedCalls > 0 ? "True" : "False"));

            if (!ready)
            {
                Debug.LogWarning(Marker + "_AUDIO_SILENT_FALLBACK reason=\""
                                 + (play.Audio == null || play.Audio.Service == null
                                     ? "no-service" : play.Audio.Service.LastError)
                                 + "\" hudWillStillRender=True");
            }
        }

        private void LogHudState(MatchPlay play, MatchScene scene)
        {
            if (play.Gameplay == null || play.Gameplay.HUD == null)
            {
                return;
            }

            MatchHudModel model = play.Gameplay.HudModel != null
                ? play.Gameplay.HudModel
                : MatchHudModel.From(scene.World, scene.Sim.Sim);
            Simulation sim = scene.Sim.Sim;
            string clock = MatchHudModel.FormatClock(
                (long)sim.Rules.Clock.TickMs - (long)sim.Rules.Clock.PeriodStartMs < 0
                    ? 0
                    : (long)sim.Rules.Context.PeriodLengthMs
                      - ((long)sim.Rules.Clock.TickMs - (long)sim.Rules.Clock.PeriodStartMs));

            bool scoreOk = model.HomeScoreText == Math.Max(0, sim.Rules.ScoreHome).ToString(
                               CultureInfo.InvariantCulture)
                           && model.AwayScoreText == Math.Max(0, sim.Rules.ScoreAway).ToString(
                               CultureInfo.InvariantCulture);
            bool periodOk = model.Period == sim.Rules.Clock.Period;
            bool clockOk = model.ClockText == clock;
            float speed = 0f;

            if (sim.ViewedSlot >= 0 && sim.ViewedSlot < sim.Players.Length)
            {
                speed = sim.Players[sim.ViewedSlot].Stats.SpeedRatio;
            }

            Debug.Log(Marker + "_HUD_STATE original="
                      + (play.Gameplay.HUD.OriginalLayout ? "True" : "False")
                      + " period=" + model.Period + " clock=\"" + model.ClockText
                      + "\" score=\"" + model.HomeTeam + " " + model.HomeScoreText + "-"
                      + model.AwayScoreText + " " + model.AwayTeam + "\""
                      + " speedRatio=" + speed.ToString("0.###", CultureInfo.InvariantCulture)
                      + " shotPower=" + (play.Control == null ? 0 : play.Control.ShotPowerLevel)
                      + " carrying=" + (sim.Rok.IsCarried ? "True" : "False")
                      + " scoreOk=" + (scoreOk ? "True" : "False")
                      + " periodOk=" + (periodOk ? "True" : "False")
                      + " clockOk=" + (clockOk ? "True" : "False"));
        }

        private void CaptureHud(MatchPlay play, MatchScene scene, int width, int height, string path)
        {
            if (play.Gameplay == null || play.Gameplay.HUD == null || scene.MatchCamera == null)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                scene.ApplyRender(1.0f);

                RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
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
                    play.Gameplay.HUD.Composite(pixels, width, height, play.Gameplay.HudModel);
                    image.SetPixels32(pixels);
                    image.Apply(false, false);
                    byte[] png = image.EncodeToPNG();
                    File.WriteAllBytes(path, png);

                    Debug.Log(Marker + "_HUD_CAPTURE path=" + Path.GetFullPath(path)
                              + " frame=" + width + "x" + height + " bytes=" + png.Length
                              + " hudOriginal=" + (play.Gameplay.HUD.OriginalLayout ? "True" : "False"));
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
            catch (Exception exception)
            {
                Debug.LogError(Marker + "_HUD_CAPTURE_FAILED " + exception.GetType().Name + ": "
                               + exception.Message);
            }
        }

        private static string AppendSuffix(string path, string suffix)
        {
            if (string.IsNullOrEmpty(path))
            {
                return suffix;
            }

            string directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            if (string.IsNullOrEmpty(extension))
            {
                extension = ".png";
            }

            string name = stem + suffix + extension;
            return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
        }
    }
}
