using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Assets;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The first native C# match scene: the original CHIDRO drome and six original skaters
    // (Chicago home, Seattle away) driven by the exact UBlade3.Sim core at 30 Hz, a chase camera
    // built from the simulation's own CameraState/CTrack, keyboard control of the home skater and a
    // deterministic capture path for batch-mode evidence. No 20XX process, DLL or runtime is used:
    // every mesh, texture and animation is read from the player's own HyperBlade install by the
    // verified UBlade3.Data/UBlade3.Assets/UBlade3.Native readers.
    //
    // The scene is assembled in memory (no scene asset) by the Editor capture entry point or the
    // built player's --match-capture command line. The plan VM is not ported: the non-human skaters
    // and goalies use the labelled fallback AI in MatchSimulation, and the HUD is logged pending.
    [DisallowMultipleComponent]
    public sealed class MatchScene : MonoBehaviour
    {
        public const string ShaderName = "UBlade3/DromePreview";

        // DDraw::ClearBackBuffer(CLEAR_COLOUR) with CLEAR_COLOUR 0x505050, the same background the
        // verified drome/skater previews use (graphics/renderer.h).
        public static readonly Color ClearColour = new Color32(0x50, 0x50, 0x50, 255);

        // CameraProjectionState: m_flHalfFov = 85/2 degrees (cameraobject.cpp, 0.7417649 rad);
        // CameraProjectionState::CreateProjectionState's far clip is 1000.0f and the Direct3D path
        // (m_bHasRow3 set) uses the 1.0 near clip (docs/SIM-SLICE.md section 4 and SimSelfCheck's
        // "projection near 1 far 1000" check).
        public const float FieldOfView = 85f;
        public const float NearClip = 1f;
        public const float FarClip = 1000f;

        public const int CaptureWidth = 1280;
        public const int CaptureHeight = 720;

        // Five seconds of the fixed 30 Hz simulation: the acceptance floor for this milestone.
        public const int CaptureTickCount = 150;
        public const int CaptureTicksPerSecond = 30;

        // The three evidence frames, at the tick counts the deterministic input script below is
        // authored for (1.5 s, 3.5 s and 5.0 s of simulation; the first is after the CTrack settle
        // countdown of about 45 ticks).
        public static readonly int[] CaptureTickList = { 45, 105, 150 };

        public static readonly string[] CaptureFileList =
        {
            "artifacts/native-match-frame-045.png",
            "artifacts/native-match-frame-105.png",
            "artifacts/native-match-frame-150.png",
        };

        // Chase camera settle guard: the original CTrack countdown is InvDelta * 1.5 frames, about
        // 45 ticks at 30 Hz, so the first capture is deliberately after it.
        public const int MaxTicksPerFrame = 5;

        public MatchWorldData World { get; private set; }

        public List<MatchCharacterData> Characters { get; private set; }

        // Simulation slot -> view; slot 0 is the human-controlled home skater.
        public MatchSkaterView[] Views { get; private set; }

        // N15: plan-animation consumer (interactive default / N15 trace only). Null in every
        // deterministic N4-N14 capture path so those frames keep their own clip clock.
        public MatchAnimationConsumer Animation { get; private set; }

        public MatchSimulation Sim { get; private set; }

        public DromePreviewScene DromeScene { get; private set; }

        private Camera matchCamera;

        public Camera MatchCamera
        {
            get { return matchCamera; }
        }

        public bool IsBuilt { get; private set; }

        // True once Build() has run and the Update loop should step the match from the keyboard.
        public bool RuntimeInput { get; set; }

        public HeldBinding LastHumanInput { get; private set; }

        public readonly List<MatchCaptureRecord> Captures = new List<MatchCaptureRecord>();

        public bool HudPending = true;

        public bool NoTwentyXx = true;

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        private double tickAccumulator;

        private Vector3 cameraCurrentPosition;
        private Vector3 cameraPreviousPosition;
        private Vector3 cameraCurrentDirection = Vector3.forward;
        private Vector3 cameraPreviousDirection = Vector3.forward;

        private bool playOnStart;
        private bool commandCapture;
        private string commandCapturePath;
        private int commandCaptureSeconds = 5;
        private bool commandAutoStart;
        private bool paused;

        public int TickCount
        {
            get { return Sim == null ? 0 : Sim.TickCount; }
        }

        public double MatchSeconds
        {
            get { return Sim == null ? 0.0 : Sim.MatchSeconds; }
        }

        public static OriginalAssetRoot ResolveRoot()
        {
            return OriginalAssetRoot.Load(Application.streamingAssetsPath, OriginalAssetRoot.DefaultInstallPath);
        }

        private void Awake()
        {
            ParseCommandLine();
        }

        private void Start()
        {
            if (!commandAutoStart || IsBuilt)
            {
                return;
            }

            try
            {
                Build();

                if (commandCapture)
                {
                    RunCommandCapture();
                }
                else
                {
                    RuntimeInput = true;
                    playOnStart = true;
                    Debug.Log("UBLADE3_NATIVE_MATCH_PLAY_READY controls=Up/Down/Left/Right,D/S/Space/C,Escape");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_NATIVE_MATCH_FAILED " + exception.GetType().Name + ": " + exception.Message);
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
                if (argument.StartsWith("--match-capture=", StringComparison.Ordinal))
                {
                    commandCapture = true;
                    commandAutoStart = true;
                    commandCapturePath = argument.Substring("--match-capture=".Length);
                }
                else if (argument.StartsWith("--match-seconds=", StringComparison.Ordinal))
                {
                    int seconds;

                    if (int.TryParse(argument.Substring("--match-seconds=".Length), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out seconds))
                    {
                        commandCaptureSeconds = Mathf.Clamp(seconds, 1, 600);
                    }
                }
                else if (argument == "--match-autostart")
                {
                    commandAutoStart = true;
                }
            }
        }

        // --- assembly ---------------------------------------------------------------------

        // Loads the original drome, both teams' six characters and the exact simulation, and builds
        // the in-memory scene. Throws with the missing file/asset when the player's install cannot
        // supply the original data; nothing is substituted.
        public MatchWorldData Build()
        {
            if (IsBuilt)
            {
                return World;
            }

            ClearBuilt();

            OriginalAssetRoot root = ResolveRoot();
            Debug.Log("UBLADE3_NATIVE_MATCH_START unity=" + Application.unityVersion
                      + " install=" + root.InstallPath + " data=" + root.DataPath
                      + " assetSet=Mdltxt no20xx=True");

            World = MatchWorldData.Load(root);
            Debug.Log(World.Describe());

            DromeScene = BuildDrome();
            Characters = World.LoadCharacters();

            for (int i = 0; i < Characters.Count; i++)
            {
                Debug.Log(Characters[i].Describe());
            }

            Views = new MatchSkaterView[ObjectConstants.PlayerObjectCount];

            for (int i = 0; i < Characters.Count; i++)
            {
                MatchSkaterView view = new MatchSkaterView(Characters[i]);
                view.Build(transform);
                Views[Characters[i].Slot.SimSlot] = view;
            }

            Sim = MatchSimulation.Create(World);

            matchCamera = DromeScene.EnsureCamera();
            matchCamera.transform.SetParent(transform, true);
            ConfigureCamera(matchCamera);

            SyncViewsFromSimulation();

            if (MatchAnimationConsumer.ShouldEnableByDefault())
            {
                try
                {
                    Animation = new MatchAnimationConsumer(World, Sim.Sim, Views);
                    Animation.Tick(0.0);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UBLADE3_NATIVE_ANIMATION_CONSUMER_FAILED "
                                     + exception.GetType().Name + ": " + exception.Message);
                }
            }

            cameraCurrentPosition = MatchSkaterView.ToUnity(Sim.Sim.MainCamera.Body.Pos);
            cameraPreviousPosition = cameraCurrentPosition;
            cameraCurrentDirection = MatchSkaterView.ToUnityDirection(Sim.Sim.MainCamera.Body.VelDir);
            cameraPreviousDirection = cameraCurrentDirection;

            ApplyRender(1.0f);

            IsBuilt = true;

            Debug.Log("UBLADE3_NATIVE_MATCH_SCENE_OK slots=" + PlayersBound()
                      + " drome=\"" + World.Drome.DromeFileName + "\" objects=" + World.Drome.Drome.Objects.Count
                      + " dromeTriangles=" + World.Drome.BuiltTriangles
                      + " skaters=" + Characters.Count
                      + " skaterTriangles=" + TotalSkaterTriangles()
                      + " missingRefs=" + TotalMissingRefs()
                      + " digest=" + World.Digest);
            Debug.Log("UBLADE3_NATIVE_MATCH_AI_FALLBACK active=True note=\"" + MatchSimulation.AiFallbackNote + "\"");
            Debug.Log("UBLADE3_NATIVE_MATCH_GOALIE_PLACEMENT note=\"" + MatchSimulation.GoaliePlacementNote + "\"");
            Debug.Log("UBLADE3_NATIVE_MATCH_HUD_PENDING data=teamNames,score,period available;"
                      + " original HUD bitmap fonts/art not ported (N6)");
            Debug.Log("UBLADE3_NATIVE_MATCH_NO_ENGINE_PROCESS no20xx=True engines=csharp");

            return World;
        }

        private DromePreviewScene BuildDrome()
        {
            GameObject dromeObject = new GameObject("OriginalDrome");
            dromeObject.transform.SetParent(transform, false);
            dromeObject.SetActive(false);

            DromePreviewScene drome = dromeObject.AddComponent<DromePreviewScene>();
            drome.ShadingMode = DromeShadingMode.Textured;
            drome.DromeFileName = MatchWorldData.DromeFileName;
            drome.CaptureOnStart = false;

            // In play mode activating the object runs the preview component's Awake, which builds the
            // drome with the settings above. In an Editor batch-mode capture Awake does not run for a
            // plain MonoBehaviour in edit mode, so Build() is called explicitly there. The preview
            // assembles the original 25 CHIDRO.ENV model objects (29 definitions; four have modfile=null)
            // and their BMP textures.
            dromeObject.SetActive(true);

            if (!drome.IsBuilt)
            {
                drome.Build();
            }

            if (!drome.IsBuilt)
            {
                throw new OriginalAssetException(
                    "The original drome scene did not build; see the UBLADE3_DROME_PREVIEW_FAILED log line.");
            }

            createdObjects.Add(dromeObject);

            return drome;
        }

        public void ConfigureCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ClearColour;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = NearClip;
            camera.farClipPlane = FarClip;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.orthographic = false;
            camera.cullingMask = ~0;
            camera.depth = 0;
            camera.targetTexture = null;
        }

        private int PlayersBound()
        {
            int bound = 0;

            for (int slot = 0; slot < ObjectConstants.PlayerObjectCount; slot++)
            {
                if (Views != null && Views[slot] != null)
                {
                    bound++;
                }
            }

            return bound;
        }

        private int TotalSkaterTriangles()
        {
            int triangles = 0;

            for (int i = 0; i < Characters.Count; i++)
            {
                triangles += Characters[i].Character.Mesh.TriangleCount;
            }

            return triangles;
        }

        public int TotalMissingRefs()
        {
            int missing = World == null ? 0 : World.Missing.Count;

            if (World != null)
            {
                missing += World.Drome.MissingTextures.Count + World.Drome.MissingModels.Count;
            }

            return missing;
        }

        // --- deterministic capture --------------------------------------------------------

        // The deterministic keyboard script the batch capture runs, so the evidence is reproducible
        // without a live input device: hold Forward for the whole five seconds, with a short held
        // RotateLeft at 2.0 s and a short held RotateRight at 4.0 s, which gives the run a visible
        // steering change while keeping the skater on the drome's long axis. It is a scripted
        // held-key sequence, not the fallback AI and not random input.
        public static HeldBinding DeterministicInput(int tick)
        {
            HeldBinding input = HeldBinding.Forward;

            if (tick >= 60 && tick < 66)
            {
                input |= HeldBinding.RotateLeft;
            }
            else if (tick >= 120 && tick < 126)
            {
                input |= HeldBinding.RotateRight;
            }

            return input;
        }

        // Steps the deterministic script forward to the requested tick and applies the render state.
        public void AdvanceToTick(int targetTick)
        {
            if (!IsBuilt)
            {
                throw new InvalidOperationException("MatchScene.Build must run before AdvanceToTick.");
            }

            if (targetTick < Sim.TickCount)
            {
                throw new ArgumentOutOfRangeException("targetTick", "The simulation cannot step backwards.");
            }

            while (Sim.TickCount < targetTick)
            {
                HeldBinding input = DeterministicInput(Sim.TickCount);
                TickScene(input);
            }

            ApplyRender(1.0f);
        }

        public void AdvanceToSeconds(double seconds)
        {
            AdvanceToTick((int)Math.Round(seconds * CaptureTicksPerSecond));
        }

        // The deterministic acceptance run: 150 ticks (5.0 s), one PNG at each tick in CaptureTickList.
        // Returns the number of failures (non-zero when a capture failed or the run is short).
        public int RunDeterministicCapture(string[] paths, int[] ticks)
        {
            int failures = 0;
            int index = 0;

            for (int i = 0; i < ticks.Length; i++)
            {
                try
                {
                    AdvanceToTick(ticks[i]);
                    string path = paths != null && i < paths.Length ? paths[i] : null;

                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    CaptureFrame(path, index);
                    index++;
                }
                catch (Exception exception)
                {
                    failures++;
                    Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED tick=" + ticks[i] + " " + exception);
                }
            }

            if (Sim == null || Sim.MatchSeconds + 1e-6 < 5.0)
            {
                failures++;
                Debug.LogError("UBLADE3_NATIVE_MATCH_CAPTURE_FAILED the match ran only "
                               + (Sim == null ? 0.0 : Sim.MatchSeconds) + " s, less than the 5 s floor");
            }

            return failures;
        }

        private void RunCommandCapture()
        {
            string[] paths = new string[CaptureTickList.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                string directory = string.IsNullOrEmpty(commandCapturePath)
                    ? null
                    : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(commandCapturePath));
                string file = System.IO.Path.GetFileNameWithoutExtension(commandCapturePath)
                              + "-" + CaptureTickList[i].ToString("000", CultureInfo.InvariantCulture) + ".png";
                paths[i] = directory == null ? file : System.IO.Path.Combine(directory, file);
            }

            int failures = RunDeterministicCapture(paths, CaptureTickList);

            Debug.Log(failures == 0
                ? "UBLADE3_NATIVE_MATCH_CAPTURES_OK frames=" + paths.Length + " ticks=" + string.Join(",", Array.ConvertAll(CaptureTickList, v => v.ToString(CultureInfo.InvariantCulture)))
                : "UBLADE3_NATIVE_MATCH_CAPTURES_FAILED failures=" + failures);

            if (Application.isBatchMode || !Application.isEditor)
            {
                Application.Quit(failures == 0 ? 0 : 1);
            }
        }

        public MatchCaptureRecord CaptureFrame(string absolutePath, int index)
        {
            if (!IsBuilt || matchCamera == null)
            {
                throw new InvalidOperationException("MatchScene.Build must run before CaptureFrame.");
            }

            ApplyRender(1.0f);

            DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                matchCamera, absolutePath, CaptureWidth, CaptureHeight);

            MatchCaptureRecord record = new MatchCaptureRecord();
            record.Index = index;
            record.Tick = Sim.TickCount;
            record.Seconds = Sim.MatchSeconds;
            record.Path = result.Path;
            record.Width = result.Width;
            record.Height = result.Height;
            record.Bytes = result.Bytes;
            record.HumanPosition = Sim.HumanPosition;
            record.HumanDisplacement = Sim.HumanDisplacement();
            record.HumanFrame = Views[MatchSimulation.HumanSlot] == null ? -1 : Views[MatchSimulation.HumanSlot].Frame;
            record.CameraPosition = Sim.Sim.MainCamera.Body.Pos;
            Captures.Add(record);

            Debug.Log("UBLADE3_NATIVE_MATCH_CAPTURE index=" + index
                      + " tick=" + record.Tick
                      + " seconds=" + record.Seconds.ToString("0.###", CultureInfo.InvariantCulture)
                      + " path=" + record.Path
                      + " width=" + record.Width + " height=" + record.Height + " bytes=" + record.Bytes
                      + " humanFrame=" + record.HumanFrame
                      + " humanPos=(" + MatchSimulation.F(record.HumanPosition.x) + ","
                      + MatchSimulation.F(record.HumanPosition.y) + ","
                      + MatchSimulation.F(record.HumanPosition.z) + ")"
                      + " humanDisplacement=" + MatchSimulation.F(record.HumanDisplacement)
                      + " cameraPos=(" + MatchSimulation.F(record.CameraPosition.x) + ","
                      + MatchSimulation.F(record.CameraPosition.y) + ","
                      + MatchSimulation.F(record.CameraPosition.z) + ")");

            Debug.Log(Sim.DescribeStates());
            Debug.Log(DescribeViews());

            return record;
        }

        // --- per-tick simulation/render ---------------------------------------------------

        public void TickScene(HeldBinding humanInput)
        {
            if (!IsBuilt)
            {
                return;
            }

            LastHumanInput = humanInput;
            Sim.HumanInput = humanInput;

            // The camera's previous render state is the state the last tick produced.
            cameraPreviousPosition = cameraCurrentPosition;
            cameraPreviousDirection = cameraCurrentDirection;

            Sim.Step();

            for (int slot = 0; slot < ObjectConstants.PlayerObjectCount; slot++)
            {
                MatchSkaterView view = Views[slot];

                if (view == null)
                {
                    continue;
                }

                PlayerState state = Sim.Sim.Players[slot];
                SimVec3 position = state.Body.Pos;
                float yaw = MatchSkaterView.YawForDirection(state.Body.VelDir);

                view.Tick(MatchWorldData.FixedTickSeconds, position, yaw);
            }

            if (Animation != null)
            {
                Animation.Tick(MatchWorldData.FixedTickSeconds);
            }

            cameraCurrentPosition = MatchSkaterView.ToUnity(Sim.Sim.MainCamera.Body.Pos);
            cameraCurrentDirection = MatchSkaterView.ToUnityDirection(Sim.Sim.MainCamera.Body.VelDir);
        }

        public void SyncViewsFromSimulation()
        {
            if (Views == null)
            {
                return;
            }

            for (int slot = 0; slot < ObjectConstants.PlayerObjectCount; slot++)
            {
                MatchSkaterView view = Views[slot];

                if (view == null)
                {
                    continue;
                }

                PlayerState state = Sim.Sim.Players[slot];
                view.Tick(0.0, state.Body.Pos, MatchSkaterView.YawForDirection(state.Body.VelDir));
                view.ApplyCurrent();
            }
        }

        // Render-time application: interpolate each character's placement/facing and the chase
        // camera between the last two simulation ticks. alpha 0 = previous tick, 1 = current tick.
        public void ApplyRender(float alpha)
        {
            if (Views != null)
            {
                for (int slot = 0; slot < Views.Length; slot++)
                {
                    if (Views[slot] != null)
                    {
                        Views[slot].ApplyInterpolated(alpha);
                    }
                }
            }

            if (Animation != null)
            {
                Animation.ApplyRender();
            }

            if (matchCamera != null)
            {
                Vector3 position = Vector3.Lerp(cameraPreviousPosition, cameraCurrentPosition, Mathf.Clamp01(alpha));
                Vector3 direction = Vector3.Lerp(cameraPreviousDirection, cameraCurrentDirection, Mathf.Clamp01(alpha));

                if (direction.sqrMagnitude < 1e-8f)
                {
                    direction = cameraCurrentDirection;
                }

                if (direction.sqrMagnitude > 1e-8f)
                {
                    matchCamera.transform.position = position;
                    matchCamera.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }
        }

        public MatchAnimationConsumer EnableAnimationConsumer()
        {
            if (Animation != null)
            {
                return Animation;
            }

            if (World == null || Sim == null || Views == null)
            {
                return null;
            }

            try
            {
                Animation = new MatchAnimationConsumer(World, Sim.Sim, Views);
                Animation.Tick(0.0);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("UBLADE3_NATIVE_ANIMATION_CONSUMER_FAILED "
                                 + exception.GetType().Name + ": " + exception.Message);
                Animation = null;
            }

            return Animation;
        }

        public void AttachPlanAnimation(MatchPlanAiRoster roster)
        {
            if (Animation == null)
            {
                return;
            }

            Animation.AttachRoster(roster);
        }

        public string DescribeViews()
        {
            StringBuilder text = new StringBuilder();

            for (int slot = 0; slot < ObjectConstants.PlayerObjectCount; slot++)
            {
                if (Views != null && Views[slot] != null)
                {
                    text.AppendLine(Views[slot].Describe());
                }
            }

            return text.ToString().TrimEnd();
        }

        public string DescribeScene()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_SCENE built=").Append(IsBuilt ? "True" : "False");
            text.Append(" ticks=").Append(TickCount.ToString(CultureInfo.InvariantCulture));
            text.Append(" seconds=").Append(MatchSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            text.Append(" skaters=").Append(Characters == null ? 0 : Characters.Count);
            text.Append(" captures=").Append(Captures.Count);
            text.Append(" missingRefs=").Append(TotalMissingRefs());
            text.Append(" camera=(").Append(MatchSimulation.F(cameraCurrentPosition.x)).Append(',')
                .Append(MatchSimulation.F(cameraCurrentPosition.y)).Append(',')
                .Append(MatchSimulation.F(cameraCurrentPosition.z)).Append(')');
            text.Append(" fov=").Append(MatchSimulation.F(FieldOfView));
            text.Append(" hudPending=").Append(HudPending ? "True" : "False");

            return text.ToString();
        }

        // --- runtime keyboard play ---------------------------------------------------------

        private void Update()
        {
            if (!IsBuilt || !playOnStart || commandCapture)
            {
                return;
            }

            if (paused)
            {
                ApplyRender(1.0f);
                return;
            }

            tickAccumulator += Time.deltaTime;

            int stepped = 0;

            while (tickAccumulator >= MatchWorldData.FixedTickSeconds && stepped < MaxTicksPerFrame)
            {
                tickAccumulator -= MatchWorldData.FixedTickSeconds;
                stepped++;

                HeldBinding input = RuntimeInput ? ReadKeyboard() : DeterministicInput(Sim.TickCount);
                TickScene(input);
            }

            float alpha = (float)(tickAccumulator / MatchWorldData.FixedTickSeconds);
            ApplyRender(alpha);
        }

        // BOOIE.INI [Keys]: Forward=38 (Up), Stop=40 (Down), RotateLeft=37 (Left),
        // RotateRight=39 (Right), Attack=83 (S), SpeedBurst=68 (D), Shoot=32 (Space), Pass=67 (C).
        // The physical bindings are the shipped ones; g_anKeyBindingOrder's Attack/SpeedBurst swap is
        // a keyboard-device payload detail, not a held-binding mapping.
        public static HeldBinding ReadKeyboard()
        {
            HeldBinding input = HeldBinding.None;

            if (Input.GetKey(KeyCode.UpArrow))
            {
                input |= HeldBinding.Forward;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                input |= HeldBinding.Stop;
            }

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                input |= HeldBinding.RotateLeft;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                input |= HeldBinding.RotateRight;
            }

            if (Input.GetKey(KeyCode.D))
            {
                input |= HeldBinding.SpeedBurst;
            }

            if (Input.GetKey(KeyCode.S))
            {
                input |= HeldBinding.Attack;
            }

            if (Input.GetKey(KeyCode.Space))
            {
                input |= HeldBinding.Shoot;
            }

            if (Input.GetKey(KeyCode.C))
            {
                input |= HeldBinding.Pass;
            }

            return input;
        }

        // The exact pause entry point (GameEngine::TogglePause 0x0047acf0); Escape toggles it while
        // the runtime scene plays.
        private void OnGUI()
        {
            if (!RuntimeInput)
            {
                return;
            }

            Event current = Event.current;

            if (current != null && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                paused = !paused;
                Sim.Sim.TogglePause();
                Debug.Log("UBLADE3_NATIVE_MATCH_PAUSE paused=" + (paused ? "True" : "False"));
            }

            if (HudPending)
            {
                // A clearly labelled placeholder, not a fake HUD: the original HUD is a set of
                // DATA bitmap fonts/art (InfoBar/UiBitmapText) that the N6 audio/UI milestone owns.
                GUI.color = Color.yellow;
                GUI.Label(new Rect(12, 12, 720, 44),
                    "HUD PENDING - original HyperBlade HUD fonts/art not ported (N6). "
                    + "Team/score/period data is loaded and logged by MatchProbe.");
                GUI.color = Color.white;
            }
        }

        public void StopPlaying()
        {
            playOnStart = false;
            RuntimeInput = false;
        }

        // --- teardown ---------------------------------------------------------------------

        public void ClearBuilt()
        {
            playOnStart = false;
            RuntimeInput = false;
            paused = false;
            tickAccumulator = 0.0;

            if (Views != null)
            {
                for (int i = 0; i < Views.Length; i++)
                {
                    if (Views[i] != null)
                    {
                        Views[i].Destroy();
                    }
                }

                Views = null;
            }

            Characters = null;

            if (matchCamera != null && matchCamera.gameObject != gameObject)
            {
                SafeDestroy(matchCamera.gameObject);
            }

            matchCamera = null;

            if (DromeScene != null)
            {
                DromeScene.ClearBuilt();
                DromeScene = null;
            }

            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                {
                    SafeDestroy(createdObjects[i]);
                }
            }

            createdObjects.Clear();

            Sim = null;
            World = null;
            Captures.Clear();
            IsBuilt = false;
            cameraCurrentPosition = Vector3.zero;
            cameraPreviousPosition = Vector3.zero;
            cameraCurrentDirection = Vector3.forward;
            cameraPreviousDirection = Vector3.forward;
        }

        private static void SafeDestroy(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private void OnDestroy()
        {
            ClearBuilt();
        }
    }

    // One captured 1280x720 match frame and the simulation state it shows.
    public sealed class MatchCaptureRecord
    {
        public int Index;
        public int Tick;
        public double Seconds;
        public string Path;
        public int Width;
        public int Height;
        public int Bytes;
        public SimVec3 HumanPosition;
        public float HumanDisplacement;
        public int HumanFrame;
        public SimVec3 CameraPosition;
    }
}