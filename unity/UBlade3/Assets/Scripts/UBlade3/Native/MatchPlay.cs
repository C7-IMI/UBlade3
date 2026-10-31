using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Audio;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // One captured frame of the played match.
    public sealed class MatchPlayCapture
    {
        public int Index;
        public string Beat;
        public string Path;
        public int Tick;
        public double Seconds;
        public int Width;
        public int Height;
        public int Bytes;
        public int HomeScore;
        public int AwayScore;
        public int ShotPower;
        public bool Carrying;
        public bool Flight;
        public int RokRenderBone;
        public string Status;
    }

    // The N6 playable match layer. It drives the existing MatchScene/MatchGameplay with the
    // possession/control state machine in MatchPossession.cs: the human home skater picks the ROK up
    // through the shipped RokInRange/Attach path, carries it, charges a shot with the shipped
    // shot-power meter, shoots with the ported PlanAction::Launch, passes with the exact
    // PlanAction::Throw, and the ROK flies with a bounded CProjectAndBounce port until a real goal
    // entry scores through the shipped goal rules. Audio events route to UBlade3.Audio.AudioService
    // when the run opted in with --ublade3-audio / UBLADE3_AUDIO=1 (record-only otherwise).
    [DisallowMultipleComponent]
    public sealed class MatchPlay : MonoBehaviour
    {
        // The audio bootstrap auto-installs after scene load when --ublade3-audio is present, but the
        // audio layer resolves the data root through GameDataRoot, which needs the install path the
        // native match layer already resolved. Because that happens earlier than scene load, point it
        // at the resolved install before the bootstrap runs; the build fallback below also covers the
        // Editor capture path, where RuntimeInitializeOnLoadMethod does not run in edit mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PrepareAudioDataPath()
        {
            if (!AudioBootstrap.IsRequested())
            {
                return;
            }

            try
            {
                var assetRoot = MatchScene.ResolveRoot();
                Environment.SetEnvironmentVariable("HYPERBLADE_GAME_DATA", assetRoot.InstallPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("UBLADE3_MATCH_AUDIO_PATH " + exception.Message);
            }
        }

        public const string CapturePrefix = "native-play";

        public const int CaptureWidth = MatchScene.CaptureWidth;

        public const int CaptureHeight = MatchScene.CaptureHeight;

        public static readonly string[] CaptureBeats =
        {
            "start", "pickup", "charge", "shot", "goal", "score", "pass"
        };

        public static readonly string[] CaptureLabels =
        {
            "00-start", "01-pickup", "02-charge", "03-shot", "04-goal", "05-score", "06-pass"
        };

        public const string N7CapturePrefix = "native-match-n7";

        public static readonly string[] N7CaptureBeats =
        {
            "start", "pickup", "charge", "headcam", "shot", "goal", "celebration",
            "faceoff", "restart", "ai-pickup", "ai-shot", "score"
        };

        public static readonly string[] N7CaptureLabels =
        {
            "00-start", "01-pickup", "02-charge", "03-headcam", "04-shot", "05-goal",
            "06-celebration", "07-faceoff", "08-restart", "09-ai-pickup", "10-ai-shot", "11-score"
        };

        public MatchScene Scene { get; private set; }

        public MatchGameplay Gameplay { get; private set; }

        public MatchPlayConfig Config { get; private set; }

        public MatchPlayControl Control { get; private set; }

        public MatchAudioDirector Audio { get; private set; }

        public MatchPlayScriptResult LastScript { get; private set; }

        public bool IsBuilt { get; private set; }

        public bool RuntimeInput { get; set; }

        public HeldBinding LastHeld { get; private set; }

        public readonly List<MatchPlayCapture> Captures = new List<MatchPlayCapture>();

        public string LastStatus
        {
            get { return Control == null ? string.Empty : Control.Status; }
        }

        public int HomeScore
        {
            get { return Scene == null ? 0 : Math.Max(0, Scene.Sim.Sim.Rules.ScoreHome); }
        }

        public int AwayScore
        {
            get { return Scene == null ? 0 : Math.Max(0, Scene.Sim.Sim.Rules.ScoreAway); }
        }

        private double tickAccumulator;

        private bool captureCommand;
        private bool n7CaptureCommand;
        private bool autoStartCommand;
        private string captureBasePath;
        private string n7CaptureBasePath;

        private static readonly Dictionary<string, int> BeatIndex = BuildBeatIndex();
        private static readonly Dictionary<string, int> N7BeatIndex = BuildN7BeatIndex();

        private static Dictionary<string, int> BuildBeatIndex()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < CaptureBeats.Length; i++)
            {
                map[CaptureBeats[i]] = i;
            }

            return map;
        }

        private static Dictionary<string, int> BuildN7BeatIndex()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < N7CaptureBeats.Length; i++)
            {
                map[N7CaptureBeats[i]] = i;
            }

            return map;
        }

        private void Awake()
        {
            ParseCommandLine();
        }

        private void Start()
        {
            if (!autoStartCommand || IsBuilt)
            {
                return;
            }

            try
            {
                MatchScene scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();
                MatchGameplay gameplay = UnityEngine.Object.FindAnyObjectByType<MatchGameplay>();

                if (scene == null)
                {
                    throw new InvalidOperationException("No MatchScene component in the play player scene.");
                }

                if (gameplay == null)
                {
                    gameplay = scene.gameObject.AddComponent<MatchGameplay>();
                }

                Build(scene, gameplay);

                if (n7CaptureCommand)
                {
                    RunCommandCaptureN7();
                }
                else if (captureCommand)
                {
                    RunCommandCapture();
                }
                else
                {
                    RuntimeInput = true;
                    Debug.Log("UBLADE3_NATIVE_PLAY_READY controls=Up/Down/Left/Right,D/S/Space,C,Escape"
                              + " audio=" + (Audio != null && Audio.Ready ? "on" : "off"));
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_PLAY_FAILED " + exception.GetType().Name + ": " + exception.Message);
                Debug.LogException(exception);

                if (Application.isBatchMode || !Application.isEditor)
                {
                    Application.Quit(1);
                }
            }
        }

        private void ParseCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            foreach (string argument in arguments)
            {
                if (argument.StartsWith("--n7-capture=", StringComparison.Ordinal))
                {
                    n7CaptureCommand = true;
                    autoStartCommand = true;
                    n7CaptureBasePath = argument.Substring("--n7-capture=".Length);
                }
                else if (argument.StartsWith("--play-capture=", StringComparison.Ordinal))
                {
                    captureCommand = true;
                    autoStartCommand = true;
                    captureBasePath = argument.Substring("--play-capture=".Length);
                }
                else if (argument == "--play-autostart")
                {
                    autoStartCommand = true;
                }
            }
        }

        // --- assembly -------------------------------------------------------------------------

        public void Build(MatchScene scene, MatchGameplay gameplay)
        {
            if (IsBuilt)
            {
                return;
            }

            if (scene == null)
            {
                throw new ArgumentNullException("scene");
            }

            Scene = scene;

            if (!scene.IsBuilt)
            {
                scene.Build();
            }

            if (gameplay == null)
            {
                gameplay = scene.gameObject.GetComponent<MatchGameplay>();
            }

            if (gameplay == null)
            {
                throw new OriginalAssetException("The play layer needs the MatchGameplay ROK/HUD layer.");
            }

            if (!gameplay.IsBuilt)
            {
                gameplay.Build(scene);
            }

            Gameplay = gameplay;

            // This layer drives the fixed ticks (the N5 gameplay component's own Update would double
            // tick otherwise).
            gameplay.ExternalTickDriver = true;

            // PlayerObject sets m_wTargetGoalSlot from the side: the keeper three below a goal slot
            // says NGoal's keeper is slot 4 (home) and SGoal's is slot 5 (away), so the home skaters
            // attack SGoal and the away skaters NGoal. The sim slice's loader default is the reverse
            // stand-in; the play layer corrects it to the shipped side mapping for the shot aim.
            MatchRules rules = scene.Sim.Sim.Rules;
            rules.SetHomeScore(0);
            rules.SetAwayScore(0);

            for (int slot = 0; slot < scene.Sim.Sim.Players.Length; slot++)
            {
                scene.Sim.Sim.Players[slot].TargetGoalSlot = (short)((slot & 1) == 0
                    ? ObjectConstants.SlotSGoal
                    : ObjectConstants.SlotNGoal);
            }

            Config = MatchPlayConfig.FromWorld(scene.World, MatchSimulation.HumanSlot);

            AudioService service = null;

            if (AudioBootstrap.IsRequested())
            {
                try
                {
                    // The audio layer resolves the install through GameDataRoot, which needs the
                    // path the match layer already resolved; point it at the same install.
                    try
                    {
                        var assetRoot = MatchScene.ResolveRoot();
                        Environment.SetEnvironmentVariable("HYPERBLADE_GAME_DATA", assetRoot.InstallPath);
                    }
                    catch (Exception rootException)
                    {
                        Debug.LogWarning("UBLADE3_MATCH_AUDIO_PATH " + rootException.Message);
                    }

                    service = AudioBootstrap.InstallAudio();

                    if (service != null && !service.IsReady)
                    {
                        service.Initialize();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UBLADE3_MATCH_AUDIO_UNAVAILABLE setup=" + exception.Message);
                    service = null;
                }
            }

            Audio = new MatchAudioDirector(service);
            Control = new MatchPlayControl(scene.Sim.Sim, Config, Audio);

            // N7 fallback AI possession: the two non-goalie AI skaters pursue the free ROK, and the
            // sim's input map now resolves every slot through the control (the human's held mask or
            // the AI controller's steering).
            Control.AttachAi(ObjectConstants.SlotDromer1, ObjectConstants.SlotDromer2);
            scene.Sim.Sim.InputForSlot = slot => Control.InputForSlot(slot);

            MatchScoreFixture.PlaceRokAtSpawn(scene.Sim.Sim);
            Control.ResetRokToSpawn();

            if (Audio.Ready)
            {
                Audio.AmbientStart();
            }

            SyncPresentation();

            IsBuilt = true;

            Debug.Log("UBLADE3_NATIVE_PLAY_SCENE_OK humanSlot=" + Config.HumanSlot
                      + " rokModel=" + gameplay.RokData.Object.Definition.Model
                      + " goalNorth=(" + MatchSimulation.F(Config.NorthGoalPosition.x) + ","
                      + MatchSimulation.F(Config.NorthGoalPosition.y) + ","
                      + MatchSimulation.F(Config.NorthGoalPosition.z) + ")"
                      + " goalSouth=(" + MatchSimulation.F(Config.SouthGoalPosition.x) + ","
                      + MatchSimulation.F(Config.SouthGoalPosition.y) + ","
                      + MatchSimulation.F(Config.SouthGoalPosition.z) + ")"
                      + " audio=" + (Audio.Ready ? "on" : "record-only")
                      + " postGoalResetTicks=" + Config.PostGoalResetTicks);
            Debug.Log("UBLADE3_NATIVE_PLAY_RULES note=\"RokInRange 0x00468c40 + RokState.Attach;"
                      + " PlanAction::Launch 0x00403290; PlanAction::Throw 0x00403a30; CProjectAndBounce subset 0x0040d510;"
                      + " goal rules MatchGoalDetector (collisioncheck 0x004521d0 / RokScoreValue 0x00465150)\"");
        }

        // --- interactive play -----------------------------------------------------------------

        private void Update()
        {
            if (!IsBuilt || captureCommand || n7CaptureCommand || Scene == null)
            {
                return;
            }

            bool paused = Scene.Sim.Sim.Rules.RunState == ClockConstants.RunPaused;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Scene.Sim.Sim.TogglePause();
                Audio.Menu(paused ? "resume" : "pause", Scene.Sim.Sim.Rules.Clock.TickMs);
                Debug.Log("UBLADE3_NATIVE_PLAY_PAUSE paused=" + (paused ? "False" : "True"));
            }

            if (Scene.Sim.Sim.Rules.RunState == ClockConstants.RunPaused)
            {
                Scene.ApplyRender(1.0f);
                return;
            }

            tickAccumulator += Time.deltaTime;

            int stepped = 0;

            while (tickAccumulator >= MatchWorldData.FixedTickSeconds && stepped < MatchScene.MaxTicksPerFrame)
            {
                tickAccumulator -= MatchWorldData.FixedTickSeconds;
                stepped++;
                TickPlay(RuntimeInput ? MatchScene.ReadKeyboard() : HeldBinding.None);
            }

            float alpha = (float)(tickAccumulator / MatchWorldData.FixedTickSeconds);
            Scene.ApplyRender(alpha);
        }

        public void TickPlay(HeldBinding held)
        {
            LastHeld = held;
            Control.Advance(held, () => Scene.TickScene(held));
            SyncPresentation();
        }

        public void SyncPresentation()
        {
            if (Gameplay != null)
            {
                Gameplay.Sync();

                if (Gameplay.HUD != null)
                {
                    Gameplay.HUD.StatusLine = Control.Status;
                }
            }
        }

        // --- deterministic played test -------------------------------------------------------

        // Runs the deterministic pickup/carry/charge/shoot/goal/score/pass script and captures the
        // seven beats through the match camera with the HUD composited into the framebuffer.
        public int RunDeterministicCapture(string[] paths, out MatchPlayScriptResult result)
        {
            int failures = 0;

            // The N6 suite is the human-only loop; the N7 suite below exercises the fallback AI.
            Control.AiEnabled = false;
            Control.HumanPickupEnabled = true;

            result = MatchPlayScript.Run(Control, beat => CaptureBeat(beat, paths), held => Scene.TickScene(held));

            LastScript = result;

            if (result.Failures != 0)
            {
                failures += result.Failures;
                Debug.LogError("UBLADE3_NATIVE_PLAY_SCRIPT_FAILED failures=" + result.Failures
                               + " reason=" + result.FailureText);
            }

            Debug.Log("UBLADE3_NATIVE_PLAY_RESULT pickups=" + result.Pickups
                      + " shots=" + result.Shots
                      + " passes=" + result.Passes
                      + " goals=" + result.Goals
                      + " bounces=" + result.Bounces
                      + " scoreBefore=" + result.ScoreBeforeHome + "-" + result.ScoreBeforeAway
                      + " scoreAfter=" + result.ScoreAfterHome + "-" + result.ScoreAfterAway
                      + " realFlightGoal=" + (result.GoalWasRealFlight ? "True" : "False")
                      + " steps=\"" + string.Join(";", result.Steps.ToArray()) + "\"");
            Debug.Log("UBLADE3_NATIVE_PLAY_AUDIO " + Audio.Describe());
            Debug.Log("UBLADE3_NATIVE_PLAY_CONTROL " + Control.Describe());

            if (Captures.Count != CaptureBeats.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_PLAY_CAPTURE_FAILED captured " + Captures.Count
                               + " of " + CaptureBeats.Length + " beats");
            }

            SyncPresentation();
            LastScript = result;

            return failures;
        }

        // --- N7: head camera, celebration/face-off restart and fallback AI possession ---------------

        public int RunDeterministicCaptureN7(string[] paths, out MatchPlayN7ScriptResult result)
        {
            int failures = 0;

            if (Control.Ai == null)
            {
                Control.AttachAi(ObjectConstants.SlotDromer1, ObjectConstants.SlotDromer2);
            }

            Control.AiEnabled = true;
            Control.HumanPickupEnabled = true;
            Control.Ai.Enabled = true;
            Scene.Sim.Sim.InputForSlot = slot => Control.InputForSlot(slot);

            result = MatchPlayN7Script.Run(Control, beat => CaptureBeatN7(beat, paths),
                held => Scene.TickScene(held));

            Debug.Log("UBLADE3_NATIVE_MATCH_N7_RESULT approachTicks=" + result.ApproachTicks
                      + " humanPickups=" + result.HumanPickups
                      + " humanShots=" + result.HumanShots
                      + " humanGoals=" + result.HumanGoals
                      + " aiPickups=" + result.AiPickups
                      + " aiShots=" + result.AiShots
                      + " aiPasses=" + result.AiPasses
                      + " aiGoals=" + result.AiGoals
                      + " goals=" + result.Goals
                      + " bounces=" + result.Bounces
                      + " score=" + result.ScoreHome + "-" + result.ScoreAway
                      + " headCamViewFrac=(" + result.HeadCamViewFracX.ToString("0.###", CultureInfo.InvariantCulture)
                      + "," + result.HeadCamViewFracY.ToString("0.###", CultureInfo.InvariantCulture) + ")"
                      + " headCamFovDegrees=" + result.HeadCamFovDegrees.ToString("0.###", CultureInfo.InvariantCulture)
                      + " headCamShotWord=0x" + result.HeadCamShotWord.ToString("x8", CultureInfo.InvariantCulture)
                      + " centreShotVel=(" + MatchSimulation.F(result.CentreShotVelocity.x) + ","
                      + MatchSimulation.F(result.CentreShotVelocity.y) + ","
                      + MatchSimulation.F(result.CentreShotVelocity.z) + ")"
                      + " faceOffSetupTick=" + result.FaceOffSetupTick
                      + " faceOffResumeTick=" + result.FaceOffResumeTick
                      + " aiPickupTick=" + result.AiPickupTick
                      + " aiShotTick=" + result.AiShotTick
                      + " finalPhase=" + result.FinalPhase
                      + " failures=" + result.Failures
                      + " reason=" + result.FailureText);
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_AUDIO " + Audio.Describe());
            Debug.Log("UBLADE3_NATIVE_MATCH_N7_CONTROL " + Control.Describe());

            if (result.Failures != 0)
            {
                failures += result.Failures;
            }

            if (Captures.Count != N7CaptureBeats.Length)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_N7_CAPTURE_FAILED captured " + Captures.Count
                               + " of " + N7CaptureBeats.Length + " beats");
            }

            SyncPresentation();
            return failures;
        }

        private void RunCommandCaptureN7()
        {
            string baseName = string.IsNullOrEmpty(n7CaptureBasePath)
                ? System.IO.Path.Combine("artifacts", N7CapturePrefix + "-player.png")
                : n7CaptureBasePath;

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseName));
            string stem = System.IO.Path.GetFileNameWithoutExtension(baseName);

            if (string.IsNullOrEmpty(directory))
            {
                directory = System.IO.Directory.GetCurrentDirectory();
            }

            string[] paths = new string[N7CaptureLabels.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = System.IO.Path.Combine(directory, stem + "-" + N7CaptureLabels[i] + ".png");
            }

            MatchPlayN7ScriptResult result;
            int failures = RunDeterministicCaptureN7(paths, out result);

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_N7_CAPTURES_OK frames=" + paths.Length
                  + " humanPickups=" + result.HumanPickups + " humanShots=" + result.HumanShots
                  + " humanGoals=" + result.HumanGoals
                  + " aiPickups=" + result.AiPickups + " aiShots=" + result.AiShots
                  + " aiPasses=" + result.AiPasses + " score=" + result.ScoreHome + "-" + result.ScoreAway
                  + " headCamFovDegrees=" + result.HeadCamFovDegrees
                  + " audio=\"" + Audio.Describe() + "\""
                : "UBLADE3_NATIVE_MATCH_N7_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode || !Application.isEditor)
            {
                Application.Quit(failures == 0 ? 0 : 1);
            }
        }

        private void CaptureBeat(string beat, string[] paths)
        {
            int index;

            if (!BeatIndex.TryGetValue(beat, out index))
            {
                Debug.LogWarning("UBLADE3_NATIVE_PLAY_CAPTURE_UNKNOWN beat=" + beat);
                return;
            }

            if (paths == null || index >= paths.Length || string.IsNullOrEmpty(paths[index]))
            {
                return;
            }

            SyncPresentation();
            Capture(paths[index], index, beat, false);
        }

        private void CaptureBeatN7(string beat, string[] paths)
        {
            int index;

            if (!N7BeatIndex.TryGetValue(beat, out index))
            {
                Debug.LogWarning("UBLADE3_NATIVE_MATCH_N7_CAPTURE_UNKNOWN beat=" + beat);
                return;
            }

            if (paths == null || index >= paths.Length || string.IsNullOrEmpty(paths[index]))
            {
                return;
            }

            SyncPresentation();
            Capture(paths[index], index, beat, string.Equals(beat, "headcam", StringComparison.OrdinalIgnoreCase));
        }

        public MatchPlayCapture Capture(string path, int index, string beat, bool headCamera = false)
        {
            Scene.ApplyRender(1.0f);
            SyncPresentation();

            Camera matchCamera = Scene.MatchCamera;

            if (headCamera)
            {
                Control.HeadCam.Update(Scene.Sim.Sim, Config.HumanSlot);
                Vector3 eye = MatchSkaterView.ToUnity(Control.HeadCam.Eye);
                Vector3 forward = MatchSkaterView.ToUnityDirection(Control.HeadCam.Forward);
                matchCamera.transform.position = eye;
                matchCamera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                matchCamera.fieldOfView = MatchHeadCamera.FieldOfViewDegrees;
                Gameplay.HUD.ShowReticle = true;
                Gameplay.HUD.ReticleFracX = Control.HeadCam.ViewFracX;
                Gameplay.HUD.ReticleFracY = Control.HeadCam.ViewFracY;
                Debug.Log("UBLADE3_NATIVE_MATCH_N7_HEAD_CAM frame=" + beat
                          + " eye=(" + MatchSimulation.F(Control.HeadCam.Eye.x) + ","
                          + MatchSimulation.F(Control.HeadCam.Eye.y) + ","
                          + MatchSimulation.F(Control.HeadCam.Eye.z) + ")"
                          + " forward=(" + MatchSimulation.F(Control.HeadCam.Forward.x) + ","
                          + MatchSimulation.F(Control.HeadCam.Forward.y) + ","
                          + MatchSimulation.F(Control.HeadCam.Forward.z) + ")"
                          + " fovDegrees=" + MatchHeadCamera.FieldOfViewDegrees
                          + " viewFrac=(" + MatchSimulation.F(Control.HeadCam.ViewFracX) + ","
                          + MatchSimulation.F(Control.HeadCam.ViewFracY) + ")");
            }

            RenderTexture target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            target.name = "UBlade3PlayCapture";
            target.Create();

            RenderTexture savedActive = RenderTexture.active;
            RenderTexture savedTarget = Scene.MatchCamera.targetTexture;
            Texture2D image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            byte[] png;

            try
            {
                Scene.MatchCamera.targetTexture = target;
                Scene.MatchCamera.aspect = (float)CaptureWidth / CaptureHeight;
                Scene.MatchCamera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                image.Apply(false, false);

                Color32[] pixels = image.GetPixels32();
                Gameplay.HUD.Composite(pixels, CaptureWidth, CaptureHeight, Gameplay.HudModel);
                image.SetPixels32(pixels);
                image.Apply(false, false);
                png = image.EncodeToPNG();
            }
            finally
            {
                Scene.MatchCamera.targetTexture = savedTarget;
                RenderTexture.active = savedActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);

                if (headCamera)
                {
                    Gameplay.HUD.ShowReticle = false;
                    Scene.ApplyRender(1.0f);
                }
            }

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllBytes(path, png);

            MatchPlayCapture capture = new MatchPlayCapture();
            capture.Index = index;
            capture.Beat = beat;
            capture.Path = System.IO.Path.GetFullPath(path);
            capture.Tick = Scene.TickCount;
            capture.Seconds = Scene.MatchSeconds;
            capture.Width = CaptureWidth;
            capture.Height = CaptureHeight;
            capture.Bytes = png.Length;
            capture.HomeScore = Math.Max(0, Scene.Sim.Sim.Rules.ScoreHome);
            capture.AwayScore = Math.Max(0, Scene.Sim.Sim.Rules.ScoreAway);
            capture.ShotPower = Control.ShotPowerLevel;
            capture.Carrying = Scene.Sim.Sim.Rok.IsCarried;
            capture.Flight = Control.Flight.Active;
            capture.RokRenderBone = Gameplay.RokView.ActiveBone;
            capture.Status = Control.Status;
            Captures.Add(capture);

            Debug.Log("UBLADE3_NATIVE_PLAY_CAPTURE index=" + index + " beat=" + beat
                      + " tick=" + capture.Tick
                      + " seconds=" + capture.Seconds.ToString("0.###", CultureInfo.InvariantCulture)
                      + " path=" + capture.Path
                      + " width=" + capture.Width + " height=" + capture.Height + " bytes=" + capture.Bytes
                      + " score=" + capture.HomeScore + "-" + capture.AwayScore
                      + " carrying=" + (capture.Carrying ? "True" : "False")
                      + " flight=" + (capture.Flight ? "True" : "False")
                      + " shotPower=" + capture.ShotPower
                      + " rokRenderBone=" + capture.RokRenderBone
                      + " status=\"" + capture.Status + "\"");

            return capture;
        }

        private void RunCommandCapture()
        {
            string baseName = string.IsNullOrEmpty(captureBasePath)
                ? System.IO.Path.Combine("artifacts", CapturePrefix + "-player.png")
                : captureBasePath;

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseName));
            string stem = System.IO.Path.GetFileNameWithoutExtension(baseName);

            if (string.IsNullOrEmpty(directory))
            {
                directory = System.IO.Directory.GetCurrentDirectory();
            }

            string[] paths = new string[CaptureLabels.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = System.IO.Path.Combine(directory, stem + "-" + CaptureLabels[i] + ".png");
            }

            MatchPlayScriptResult result;
            int failures = RunDeterministicCapture(paths, out result);

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_PLAY_CAPTURES_OK frames=" + paths.Length
                  + " pickups=" + result.Pickups + " shots=" + result.Shots + " passes=" + result.Passes
                  + " goals=" + result.Goals + " score=" + result.ScoreAfterHome + "-" + result.ScoreAfterAway
                  + " audio=\"" + Audio.Describe() + "\""
                : "UBLADE3_NATIVE_PLAY_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode || !Application.isEditor)
            {
                Application.Quit(failures == 0 ? 0 : 1);
            }
        }

        // --- teardown -------------------------------------------------------------------------

        public void Destroy()
        {
            if (Audio != null)
            {
                Audio.AmbientStop();
            }

            if (Gameplay != null)
            {
                Gameplay.ExternalTickDriver = false;
            }

            IsBuilt = false;
            RuntimeInput = false;
        }

        private void OnDestroy()
        {
            Destroy();
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_PLAY built=").Append(IsBuilt ? "True" : "False");
            text.Append(" home=").Append(HomeScore).Append(" away=").Append(AwayScore);
            text.Append(" control=\"").Append(Control == null ? string.Empty : Control.Describe()).Append('"');
            text.Append(" audio=\"").Append(Audio == null ? string.Empty : Audio.Describe()).Append('"');
            text.Append(" captures=").Append(Captures.Count);
            text.Append(" status=\"").Append(LastStatus).Append('"');

            return text.ToString();
        }
    }
}
