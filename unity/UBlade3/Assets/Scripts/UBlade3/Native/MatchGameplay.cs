using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Sim;
using Debug = UnityEngine.Debug;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // One captured gameplay evidence frame: the three match frames plus the two goal frames and the
    // final scoreboard, with the score/HUD/ROK state that produced each image.
    public sealed class MatchGameplayCapture
    {
        public int Index;
        public string Label;
        public string Path;
        public int Tick;
        public double Seconds;
        public int Width;
        public int Height;
        public int Bytes;
        public string HudText;
        public int HomeScore;
        public int AwayScore;
        public int Period;
        public string ClockText;
        public int RokRenderBone;
        public SimVec3 RokPosition;
    }

    // N5 gameplay layer for the native C# match: the original drome ROK model driven by Sim.Rok,
    // goal detection/scoring through the existing UBlade3.Sim rules, and the original-style HUD
    // (original assets8 frames/logos plus the clearly labelled interim text font). It is an additive
    // component for the existing MatchScene: no existing Match/Sim/Data/Assets file is modified.
    [DisallowMultipleComponent]
    public sealed class MatchGameplay : MonoBehaviour
    {
        public const string CapturePrefix = "native-gameplay";

        public const int CaptureWidth = MatchScene.CaptureWidth;

        public const int CaptureHeight = MatchScene.CaptureHeight;

        // The deterministic evidence run: the normal five seconds of match play, then a home goal,
        // an away goal and the final scoreboard.
        public const int NormalMatchTicks = MatchScene.CaptureTickCount;

        public MatchScene Scene { get; private set; }

        public MatchRokData RokData { get; private set; }

        public MatchRokView RokView { get; private set; }

        public MatchHud HUD { get; private set; }

        public MatchHudData HudData { get; private set; }

        public MatchHudModel HudModel { get; private set; }

        public readonly MatchGoalDetector GoalDetector = new MatchGoalDetector();

        public readonly List<MatchGameplayCapture> Captures = new List<MatchGameplayCapture>();

        public bool IsBuilt { get; private set; }

        public bool RuntimeInput { get; set; }

        public HeldBinding LastHumanInput { get; private set; }

        public int GoalsScored
        {
            get { return GoalDetector.GoalCount; }
        }

        public int FinalHomeScore
        {
            get { return Scene == null ? 0 : Math.Max(0, Scene.Sim.Sim.Rules.ScoreHome); }
        }

        public int FinalAwayScore
        {
            get { return Scene == null ? 0 : Math.Max(0, Scene.Sim.Sim.Rules.ScoreAway); }
        }

        private double tickAccumulator;

        private bool captureCommand;
        private bool autoStartCommand;
        private string captureBasePath;

        private int debounceExtraGoals;
        private bool carrierFollowOk;
        private float carrierFollowDistance;
        private bool renderBoneMappingOk;
        private string renderBoneDetail = string.Empty;
        private int no20xxProcesses;
        private MatchGoalEvent homeGoal;
        private MatchGoalEvent awayGoal;

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

                if (scene == null)
                {
                    throw new InvalidOperationException("No MatchScene component in the gameplay player scene.");
                }

                Build(scene);

                if (captureCommand)
                {
                    RunCommandCapture();
                }
                else
                {
                    RuntimeInput = true;
                    Debug.Log("UBLADE3_NATIVE_GAMEPLAY_PLAY_READY controls=Up/Down/Left/Right,D/S/Space/C,Escape");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED " + exception.GetType().Name + ": " + exception.Message);
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
                if (argument.StartsWith("--gameplay-capture=", StringComparison.Ordinal))
                {
                    captureCommand = true;
                    autoStartCommand = true;
                    captureBasePath = argument.Substring("--gameplay-capture=".Length);
                }
                else if (argument == "--gameplay-autostart")
                {
                    autoStartCommand = true;
                }
            }
        }

        // --- assembly ---------------------------------------------------------------------

        public void Build(MatchScene scene)
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

            // This layer owns the HUD; suppress the existing scene's interim OnGUI line.
            scene.HudPending = false;

            // GameEngine::Reset leaves the scores at -1 and InfoBar::IncrementScore(5)/(4) moves them
            // to 0 at match start (docs/CORE-NOTES.md section 7). The scene does not run the InfoBar,
            // so the same displayed start state is written here.
            scene.Sim.Sim.Rules.SetHomeScore(0);
            scene.Sim.Sim.Rules.SetAwayScore(0);

            RokData = MatchRokData.Load(scene.World);
            Debug.Log(RokData.Describe());

            RokView = new MatchRokView(RokData);
            RokView.Build(transform);

            HudData = MatchHudData.Load(scene.World);
            Debug.Log(HudData.Describe());

            HUD = scene.MatchCamera.gameObject.GetComponent<MatchHud>();

            if (HUD == null)
            {
                HUD = scene.MatchCamera.gameObject.AddComponent<MatchHud>();
            }

            HUD.Build(scene, this, HudData);

            MatchScoreFixture.PlaceRokAtSpawn(scene.Sim.Sim);
            MatchScoreFixture.CheckRenderBoneMapping(scene.Sim.Sim, out renderBoneDetail);

            Sync();

            IsBuilt = true;

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_SCENE_OK model=" + RokData.Object.Definition.Model
                      + " rokTriangles=" + RokData.TriangleCount
                      + " rokBones=" + RokData.BoneCount
                      + " rokTextures=" + RokData.CountTextures()
                      + " hudArt=" + (HudData.Missing.Count == 0 ? "ok" : "missing=" + HudData.Missing.Count)
                      + " renderBones=" + renderBoneDetail);
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_RULES note=\"goal mouth: collisioncheck.cpp 0x004521d0 GoalFrame.Mouth;"
                      + " port: CollisionCheck::BouncedInGoal 0x00469ef0; value: PlanCond::RokScoreValue 0x00465150;"
                      + " points: goal plan arm counts; north goal -> away, south goal -> home"
                      + " (NGoal.PBN/SGoal.PBN + INSCRTBL.PBN + InfoBar::IncrementScore 0x0047ae70)\"");
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_AI_FALLBACK note=\"" + MatchSimulation.AiFallbackNote + "\"");
        }

        // --- per-tick presentation sync ----------------------------------------------------

        public void Sync()
        {
            if (!IsBuilt)
            {
                return;
            }

            RokState rok = Scene.Sim.Sim.Rok;

            RokView.Tick(rok.Body.Pos, rok.Body.VelDir, rok.Body.Speed, rok.RenderBone);
            HudModel = MatchHudModel.From(Scene.World, Scene.Sim.Sim);

            if (HUD != null)
            {
                HUD.Visible = true;
            }
        }

        // Scores the current ROK position through the shipped rules and logs the event. Returns true
        // when a goal was counted.
        public bool DetectGoal()
        {
            if (!IsBuilt)
            {
                return false;
            }

            SimVec3 position = Scene.Sim.Sim.Rok.Body.Pos;
            MatchGoalEvent gameEvent = GoalDetector.TryScore(Scene.Sim.Sim, position);

            if (!gameEvent.Valid)
            {
                return false;
            }

            LogGoal(gameEvent);

            Sync();

            return true;
        }

        // The single goal log line both the runtime detector and the deterministic fixtures print.
        private void LogGoal(MatchGoalEvent gameEvent)
        {
            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_GOAL side=" + (gameEvent.ScoringSide == 0 ? "home" : "away")
                      + " points=" + gameEvent.Points
                      + " goalSlot=" + gameEvent.GoalSlot
                      + " scoreCode=" + gameEvent.ScoreCode
                      + " rokValue=" + gameEvent.RokValue
                      + " carried=" + (gameEvent.RokCarried ? "True" : "False")
                      + " atSpawnSlot=" + (gameEvent.RokAtSpawnSlot ? "True" : "False")
                      + " renderBone=" + gameEvent.RenderBone
                      + " tickMs=" + gameEvent.TickMs
                      + " homeScore=" + gameEvent.HomeScore
                      + " awayScore=" + gameEvent.AwayScore
                      + " rokPos=(" + MatchSimulation.F(gameEvent.RokPosition.x) + ","
                      + MatchSimulation.F(gameEvent.RokPosition.y) + ","
                      + MatchSimulation.F(gameEvent.RokPosition.z) + ")");
        }

        private void StepScene()
        {
            Scene.TickScene(HeldBinding.None);
            DetectGoal();
            Sync();
        }

        // A tick for the deterministic goal fixture: the fixture's own MatchGoalDetector runs the
        // goal rules, so this path must not call DetectGoal as well (that would consume the goal and
        // leave the fixture's debounce answer empty).
        private void FixtureTick()
        {
            Scene.TickScene(HeldBinding.None);
            Sync();
        }

        // Drives the ROK's presentation state through the existing RokState.SetRenderSlotFlags
        // (rokobject.cpp 0x00473b90) for the render-bone close-ups. The value is the shipped
        // PlanAction::IncRokValue state; no score is involved.
        private void SetRokPresentationValue(int value)
        {
            RokState rok = Scene.Sim.Sim.Rok;
            rok.RokValue = (short)value;
            rok.AtSpawnSlot = false;
            rok.KillState = RokConstants.InitialKillState;
            rok.SetRenderSlotFlags();
            Sync();
        }

        // --- deterministic evidence suite ---------------------------------------------------

        // Runs the five-second deterministic match, then a home goal (ROK value 2, 2 points) and an
        // away goal (ROK value 1, 1 point), capturing frames through the match camera so the HUD is
        // part of the framebuffer. Returns the failure count.
        public int RunDeterministicSuite(string[] capturePaths)
        {
            int failures = 0;

            homeGoal = new MatchGoalEvent();
            awayGoal = new MatchGoalEvent();
            debounceExtraGoals = 0;

            // 1. The normal five-second deterministic match, preserving the existing behaviour.
            Scene.AdvanceToTick(NormalMatchTicks);
            Sync();
            Capture(capturePaths, 0, "match-5s");

            // 2. Home side scores in the south goal (ROK value 2 -> 2 points, render bone 7).
            MatchScoreFixture.PlaceRokAtSpawn(Scene.Sim.Sim);
            GoalDetector.ResetDebounce();
            homeGoal = MatchScoreFixture.RunIntoGoal(Scene.Sim.Sim, GoalDetector, true, 2, 170, 2, FixtureTick);
            Sync();
            Capture(capturePaths, 1, "goal-home-2");

            if (homeGoal.Valid)
            {
                LogGoal(homeGoal);
            }

            if (!homeGoal.Valid || homeGoal.ScoringSide != 0 || homeGoal.Points != 2)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED home goal fixture: valid=" + homeGoal.Valid
                               + " side=" + homeGoal.ScoringSide + " points=" + homeGoal.Points);
            }

            // 3. A short dwell inside the mouth proves the 400 ms debounce stops a double count.
            debounceExtraGoals = MatchScoreFixture.DwellInMouth(Scene.Sim.Sim, GoalDetector,
                homeGoal.RokPosition, 10, FixtureTick);

            if (debounceExtraGoals != 0)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED the debounce counted " + debounceExtraGoals + " extra goal(s)");
            }

            // 4. ROK close-ups at its spawn point: plain (value 0, render bone 0), double (value 2,
            // render bone 7) and quad (value 4, render bone 8), all through RokState.SetRenderSlotFlags.
            MatchScoreFixture.PlaceRokAtSpawn(Scene.Sim.Sim);
            SetRokPresentationValue(0);
            CaptureRokView(capturePaths, 2, "rok-plain");
            SetRokPresentationValue(2);
            CaptureRokView(capturePaths, 3, "rok-double");
            SetRokPresentationValue(4);
            CaptureRokView(capturePaths, 4, "rok-quad");
            SetRokPresentationValue(0);
            Scene.ApplyRender(1.0f);

            // 5. Away side scores in the north goal (ROK value 1 -> 1 point, render bone 0).
            GoalDetector.ResetDebounce();
            awayGoal = MatchScoreFixture.RunIntoGoal(Scene.Sim.Sim, GoalDetector, false, 1, 170, 2, FixtureTick);
            Sync();
            Capture(capturePaths, 5, "goal-away-1");

            if (awayGoal.Valid)
            {
                LogGoal(awayGoal);
            }

            if (!awayGoal.Valid || awayGoal.ScoringSide != 1 || awayGoal.Points != 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED away goal fixture: valid=" + awayGoal.Valid
                               + " side=" + awayGoal.ScoringSide + " points=" + awayGoal.Points);
            }

            // 6. The ROK back on its spawn point and the final 2-1 scoreboard.
            MatchScoreFixture.PlaceRokAtSpawn(Scene.Sim.Sim);

            for (int i = 0; i < 5; i++)
            {
                StepScene();
            }

            Capture(capturePaths, 6, "scoreboard-2-1");

            if (Scene.Sim.Sim.Rules.ScoreHome != 2 || Scene.Sim.Sim.Rules.ScoreAway != 1)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED final score " + Scene.Sim.Sim.Rules.ScoreHome
                               + "-" + Scene.Sim.Sim.Rules.ScoreAway + " != 2-1");
            }

            // 7. The carried-ROK path of the existing sim (no capture): attach, tick, check follow.
            carrierFollowOk = MatchScoreFixture.CheckCarrierFollow(Scene.Sim, MatchSimulation.HumanSlot,
                new SimVec3(0.0f, 0.0f, 2.0f), StepScene, out carrierFollowDistance, out bool carried);

            if (!carrierFollowOk)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED carried ROK follow: carried=" + carried
                               + " distance=" + carrierFollowDistance);
            }

            renderBoneMappingOk = MatchScoreFixture.CheckRenderBoneMapping(Scene.Sim.Sim, out renderBoneDetail);

            if (!renderBoneMappingOk)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_GAMEPLAY_FAILED render-bone mapping: " + renderBoneDetail);
            }

            no20xxProcesses = CountTwentyXxProcesses();
            Sync();

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_SUMMARY goals=" + GoalDetector.GoalCount
                      + " homeGoal=(" + (homeGoal.Valid ? homeGoal.Points.ToString(CultureInfo.InvariantCulture) : "none") + ")"
                      + " awayGoal=(" + (awayGoal.Valid ? awayGoal.Points.ToString(CultureInfo.InvariantCulture) : "none") + ")"
                      + " homeScore=" + Scene.Sim.Sim.Rules.ScoreHome
                      + " awayScore=" + Scene.Sim.Sim.Rules.ScoreAway
                      + " debounceExtra=" + debounceExtraGoals
                      + " carrierFollow=" + (carrierFollowOk ? "ok" : "failed")
                      + " carrierDistance=" + carrierFollowDistance.ToString("0.#####", CultureInfo.InvariantCulture)
                      + " renderBones=" + renderBoneDetail
                      + " hud=\"" + (HudModel == null ? string.Empty : HudModel.Describe()) + "\""
                      + " no20xxProcesses=" + no20xxProcesses
                      + " rokTriangles=" + RokData.TriangleCount
                      + " rokRenderBone=" + RokView.ActiveBone);

            return failures;
        }

        private static int CountTwentyXxProcesses()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("hyperblade_20XX");

                return processes == null ? 0 : processes.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // --- capture ----------------------------------------------------------------------

        public MatchGameplayCapture Capture(string[] capturePaths, int index, string label)
        {
            string path = capturePaths != null && index < capturePaths.Length ? capturePaths[index] : null;

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Scene.ApplyRender(1.0f);
            Sync();

            return RenderAndCapture(path, label, index);
        }

        // A dedicated ROK close-up: the chase camera follows the human skater, so the goal frames do
        // not show the ROK itself. This frames the ROK from its posed mesh bounds and captures it with
        // the HUD, proving the original Rokmix model, its WBM materials and the active render bone.
        public MatchGameplayCapture CaptureRokView(string[] capturePaths, int index, string label)
        {
            string path = capturePaths != null && index < capturePaths.Length ? capturePaths[index] : null;

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Camera camera = Scene.MatchCamera;
            Transform transform = camera.transform;
            Vector3 savedPosition = transform.position;
            Quaternion savedRotation = transform.rotation;
            float savedAspect = camera.aspect;

            Vector3 localCenter = (RokData.Mesh.BoundsMin + RokData.Mesh.BoundsMax) * 0.5f;
            Vector3 size = RokData.Mesh.BoundsMax - RokData.Mesh.BoundsMin;
            float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            Vector3 worldCenter = RokView.UnityPosition + localCenter;
            float distance = Mathf.Max(3.0f, extent * 2.2f);
            Vector3 direction = new Vector3(0.25f, 0.50f, 1f).normalized;

            transform.position = worldCenter + (direction * distance);
            transform.LookAt(worldCenter, Vector3.up);

            try
            {
                return RenderAndCapture(path, label, index);
            }
            finally
            {
                transform.position = savedPosition;
                transform.rotation = savedRotation;
                camera.aspect = savedAspect;
            }
        }

        // Renders the current camera into a RenderTexture and composites the HUD into the same
        // pixel buffer before writing the PNG. The HUD is part of the captured framebuffer, like the
        // original software renderer's InfoBar; no OnGUI or post-render overlay is involved.
        private MatchGameplayCapture RenderAndCapture(string path, string label, int index)
        {
            RenderTexture target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            target.name = "UBlade3GameplayCapture";
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
                HUD.Composite(pixels, CaptureWidth, CaptureHeight, HudModel);
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
            }

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllBytes(path, png);

            MatchGameplayCapture capture = new MatchGameplayCapture();
            capture.Index = index;
            capture.Label = label;
            capture.Path = System.IO.Path.GetFullPath(path);
            capture.Tick = Scene.TickCount;
            capture.Seconds = Scene.MatchSeconds;
            capture.Width = CaptureWidth;
            capture.Height = CaptureHeight;
            capture.Bytes = png.Length;
            capture.HudText = HudModel == null ? string.Empty : HudModel.Describe();
            capture.HomeScore = Math.Max(0, Scene.Sim.Sim.Rules.ScoreHome);
            capture.AwayScore = Math.Max(0, Scene.Sim.Sim.Rules.ScoreAway);
            capture.Period = Scene.Sim.Sim.Rules.Clock.Period;
            capture.ClockText = HudModel == null ? string.Empty : HudModel.ClockText;
            capture.RokRenderBone = RokView.ActiveBone;
            capture.RokPosition = Scene.Sim.Sim.Rok.Body.Pos;

            Captures.Add(capture);

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_CAPTURE index=" + index
                      + " label=" + label
                      + " tick=" + capture.Tick
                      + " seconds=" + capture.Seconds.ToString("0.###", CultureInfo.InvariantCulture)
                      + " path=" + capture.Path
                      + " width=" + capture.Width + " height=" + capture.Height + " bytes=" + capture.Bytes
                      + " homeScore=" + capture.HomeScore
                      + " awayScore=" + capture.AwayScore
                      + " period=" + capture.Period
                      + " clock=" + capture.ClockText
                      + " rokRenderBone=" + capture.RokRenderBone
                      + " rokPos=(" + MatchSimulation.F(capture.RokPosition.x) + ","
                      + MatchSimulation.F(capture.RokPosition.y) + ","
                      + MatchSimulation.F(capture.RokPosition.z) + ")"
                      + " hud=\"" + capture.HudText + "\"");

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

            string[] labels = { "match-5s", "goal-home", "rok-plain", "rok-double", "rok-quad", "goal-away", "scoreboard" };
            string[] paths = new string[labels.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = System.IO.Path.Combine(directory,
                    stem + "-" + i.ToString("00", CultureInfo.InvariantCulture) + "-" + labels[i] + ".png");
            }

            int failures = RunDeterministicSuite(paths);

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_GAMEPLAY_CAPTURES_OK frames=" + paths.Length
                  + " score=" + FinalHomeScore + "-" + FinalAwayScore
                  + " goals=" + GoalsScored
                  + " hud=\"" + (HudModel == null ? string.Empty : HudModel.Describe()) + "\""
                : "UBLADE3_NATIVE_GAMEPLAY_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode || !Application.isEditor)
            {
                Application.Quit(failures == 0 ? 0 : 1);
            }
        }

        // --- runtime play -----------------------------------------------------------------

        private void Update()
        {
            if (!IsBuilt || captureCommand || Scene == null)
            {
                return;
            }

            tickAccumulator += Time.deltaTime;

            int stepped = 0;

            while (tickAccumulator >= MatchWorldData.FixedTickSeconds && stepped < MatchScene.MaxTicksPerFrame)
            {
                tickAccumulator -= MatchWorldData.FixedTickSeconds;
                stepped++;

                HeldBinding input = RuntimeInput ? MatchScene.ReadKeyboard() : HeldBinding.None;
                LastHumanInput = input;
                Scene.TickScene(input);
                DetectGoal();
                Sync();
            }

            float alpha = (float)(tickAccumulator / MatchWorldData.FixedTickSeconds);
            Scene.ApplyRender(alpha);
        }

        // --- probe -------------------------------------------------------------------------

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_GAMEPLAY built=").Append(IsBuilt ? "True" : "False");
            text.Append(" ticks=").Append(Scene == null ? 0 : Scene.TickCount);
            text.Append(" seconds=").Append(Scene == null ? 0.0 : Scene.MatchSeconds);
            text.Append(" score=").Append(FinalHomeScore).Append('-').Append(FinalAwayScore);
            text.Append(" goals=").Append(GoalsScored);
            text.Append(" captures=").Append(Captures.Count);
            text.Append(" rokModel=").Append(RokData == null ? "none" : RokData.Object.Definition.Model);
            text.Append(" rokTriangles=").Append(RokData == null ? 0 : RokData.TriangleCount);
            text.Append(" rokRenderBone=").Append(RokView == null ? -1 : RokView.ActiveBone);
            text.Append(" hudBuilt=").Append(HUD == null ? "False" : (HUD.Built ? "True" : "False"));
            text.Append(" hudText=\"").Append(HudModel == null ? string.Empty : HudModel.Describe()).Append('"');
            text.Append(" hudOriginalArt=").Append(HudData == null ? "none" : HudData.Describe());
            text.Append(" renderBones=").Append(renderBoneDetail);
            text.Append(" carrierFollow=").Append(carrierFollowOk ? "ok" : "failed");
            text.Append(" debounceExtra=").Append(debounceExtraGoals);
            text.Append(" no20xxProcesses=").Append(no20xxProcesses);

            return text.ToString();
        }

        public void Destroy()
        {
            if (RokView != null)
            {
                RokView.Destroy();
                RokView = null;
            }

            if (HUD != null)
            {
                HUD.Destroy();

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(HUD);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(HUD);
                }

                HUD = null;
            }

            IsBuilt = false;
            RuntimeInput = false;
        }

        private void OnDestroy()
        {
            Destroy();
        }
    }
}