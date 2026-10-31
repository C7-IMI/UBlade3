using System;
using System.Globalization;
using System.IO;
using UBlade3.Sim;
using UnityEngine;

namespace UBlade3.Native
{
    // N15 preview-009 probe. A normal interactive build launched with --n15-probe=<png> waits for
    // the default match/plan-animation/audio/HUD setup, ticks the deterministic plan trace, reports
    // the consumer's clip/event counters and the audio/HUD state, optionally captures one staged
    // animation frame, and quits 0. It is a probe, not a gameplay camera claim.
    public sealed class MatchAnimationPreviewProbe : MonoBehaviour
    {
        public const string Prefix = "--n15-probe";
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_PROBE";

        private string capturePath = string.Empty;
        private int frames;
        private int readyFrame = -1;
        private bool done;
        private MatchPlay play;
        private MatchScene scene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(Prefix, StringComparison.Ordinal))
                {
                    GameObject host = new GameObject("UBlade3 Animation Probe");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    host.AddComponent<MatchAnimationPreviewProbe>();
                    return;
                }
            }
        }

        private void Awake()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(Prefix + "=", StringComparison.Ordinal))
                {
                    capturePath = arguments[i].Substring((Prefix + "=").Length);
                }
            }
        }

        private void Update()
        {
            if (done)
            {
                return;
            }

            frames++;

            if (readyFrame < 0)
            {
                scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();
                play = UnityEngine.Object.FindAnyObjectByType<MatchPlay>();

                if (scene == null || play == null || !play.IsBuilt || scene.Animation == null)
                {
                    if (frames > 600)
                    {
                        Debug.LogWarning(Marker + "_FAILED scene=" + (scene != null)
                                         + " play=" + (play != null)
                                         + " animation=" + (scene != null && scene.Animation != null));
                        Quit(1);
                    }

                    return;
                }

                readyFrame = Time.frameCount;
                Debug.Log(Marker + "_READY hudOriginal=" + HudOriginal()
                          + " audioReady=" + AudioReady()
                          + " registry=" + scene.Animation.Registry.Count
                          + " planSlots=" + scene.Animation.PlanSlots
                          + " camera=" + scene.MatchCamera.transform.position.ToString("0.##"));
                return;
            }

            if (Time.frameCount < readyFrame + 30)
            {
                return;
            }

            // Fixed-step plan trace; the same deterministic input the match capture uses.
            for (int tick = 0; tick < 2; tick++)
            {
                play.TickPlay(MatchScene.DeterministicInput(scene.TickCount));
            }

            scene.Animation.RefreshCounters();

            if (scene.TickCount >= 120)
            {
                Debug.Log(Marker + "_PLAN_STATE " + scene.Animation.Describe());
                Debug.Log(Marker + "_AUDIO hudOriginal=" + HudOriginal()
                          + " audioReady=" + AudioReady()
                          + " counts=\"" + (play.Audio == null ? "none" : play.Audio.Describe()) + "\"");
                Debug.Log("UBLADE3_NATIVE_ANIMATION_PROBE_HUD period="
                          + play.Gameplay.HudModel.Period + " clock=\""
                          + play.Gameplay.HudModel.ClockText + "\" score=\""
                          + play.Gameplay.HudModel.HomeTeam + " "
                          + play.Gameplay.HudModel.HomeScoreText + "-"
                          + play.Gameplay.HudModel.AwayScoreText + " "
                          + play.Gameplay.HudModel.AwayTeam + "\""
                          + " originalHud=" + HudOriginal());

                // One shipped non-persistent clip through the consumer, so the extracted player
                // proves clip changes/events are live outside the Editor trace.
                scene.Animation.PlayFixture(1, 0, 1.0f, 0, "preview-fixture-celebration");

                for (int tick = 0; tick < 6; tick++)
                {
                    play.TickPlay(HeldBinding.None);
                }

                scene.Animation.RefreshCounters();
                Debug.Log(Marker + "_FIXTURE " + scene.Animation.Describe());

                if (!string.IsNullOrEmpty(capturePath))
                {
                    CaptureStaged(0);
                }

                Debug.Log(Marker + "_OK clips=" + scene.Animation.ClipChanges
                          + " events=" + scene.Animation.EventCount
                          + " resolvedIds=" + scene.Animation.ResolvedPlanIds.Count
                          + " audioReady=" + AudioReady()
                          + " hudOriginal=" + HudOriginal());
                Quit(0);
                done = true;
            }
        }

        private string HudOriginal()
        {
            return play != null && play.Gameplay != null && play.Gameplay.HUD != null
                   && play.Gameplay.HUD.OriginalLayout ? "True" : "False";
        }

        private string AudioReady()
        {
            return play != null && play.Audio != null && play.Audio.Ready ? "True" : "False";
        }

        private void CaptureStaged(int slot)
        {
            try
            {
                // The same staged character portrait as the Editor trace: hide the other views,
                // place the subject at centre ice and put the camera on a fixed three-quarter angle.
                if (scene.Views != null)
                {
                    for (int i = 0; i < scene.Views.Length; i++)
                    {
                        if (scene.Views[i] != null && scene.Views[i].Root != null)
                        {
                            scene.Views[i].Root.SetActive(i == slot);
                        }
                    }
                }

                Vector3 stage = Vector3.zero;
                scene.Views[slot].Root.transform.position = stage;
                scene.MatchCamera.transform.position = stage + new Vector3(0.0f, 11.0f, -32.0f);
                scene.MatchCamera.transform.LookAt(stage + (Vector3.up * 10.0f));
                scene.MatchCamera.fieldOfView = 48.0f;
                scene.MatchCamera.aspect = 16.0f / 9.0f;
                scene.ApplyRender(1.0f);

                string directory = Path.GetDirectoryName(Path.GetFullPath(capturePath));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                    scene.MatchCamera, capturePath, 1280, 720);
                Debug.Log(Marker + "_CAPTURE path=" + result.Path + " bytes=" + result.Bytes);
            }
            catch (Exception exception)
            {
                Debug.LogError(Marker + "_CAPTURE_FAILED " + exception.GetType().Name + ": "
                               + exception.Message);
            }
        }

        private static void Quit(int code)
        {
            if (!Application.isEditor)
            {
                Application.Quit(code);
            }
        }
    }
}
