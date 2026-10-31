using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // The N3 skater preview scene. It resolves the player's own HyperBlade install, loads one
    // original skater (default CHIOMX, the Chicago home side's human controlled Dromer) and that
    // team's goalie (CHIGMX), their original WBM rigs and WBA animation sets, poses the meshes on
    // the CPU from the reconstructed bone matrices and draws them with the original BMP textures.
    // Nothing is procedural: if the install, a model, a texture or the animation assembly script is
    // missing, the preview reports the exact file and fails.
    //
    // The surface is the N2 drome preview's shader (texture MODULATEd by the vertex colour the
    // renderer baked from the original material record), reused as-is; the skater needs the same
    // unlit MODULATE contract and inventing a second shader would not add exactness.
    //
    // The clip plays with the shipped timing: AnimPlayer::SetClip starts at the assembly segment's
    // start frame and advances int(time * standardFps) frames (SkaterAnimationClip.FrameAtSeconds
    // documents the mapping), wrapping at the segment end. AdvancePlayback is the clock the Update
    // loop and the Editor capture both step, so a captured frame is the frame the shipped player
    // would show at that elapsed time.
    [DisallowMultipleComponent]
    public sealed class SkaterPreviewScene : MonoBehaviour
    {
        public const string ShaderName = "UBlade3/DromePreview";

        // DDraw::ClearBackBuffer(CLEAR_COLOUR) with CLEAR_COLOUR 0x505050, the same validation
        // background the drome preview uses (renderer.h).
        public static readonly Color ClearColour = new Color32(0x50, 0x50, 0x50, 255);

        // The preview is a validation view, not a claimed match camera: a 45 degree vertical field
        // of view frames a standing skater without the 85 degree engine FOV's edge distortion.
        public const float FieldOfView = 45f;

        public const float NearClip = 0.05f;

        public const float FarClip = 200f;

        // Gap between the two characters' original-space lateral centres, in model units.
        public const float LineupGap = 3.6f;

        [SerializeField]
        private string skaterModel = SkaterPreviewData.ExpectedSkaterModel;

        [SerializeField]
        private string goalieModel = SkaterPreviewData.ExpectedGoalieModel;

        [SerializeField]
        private string skaterAssembly = SkaterPreviewData.ExpectedSkaterAssembly;

        [SerializeField]
        private string goalieAssembly = SkaterPreviewData.ExpectedGoalieAssembly;

        [SerializeField]
        private OriginalModelSet modelSet = OriginalModelSet.Textured;

        [SerializeField]
        private int lodIndex;

        [SerializeField]
        private bool remapPalette;

        [SerializeField]
        private bool includeGoalie = true;

        // Facing of the model in the original's world (yaw about its up axis, degrees). The shipped
        // skater models face down the drome's long axis; a still preview picks a three-quarter view.
        [SerializeField]
        private float skaterYawDegrees = 35f;

        [SerializeField]
        private float goalieYawDegrees = 35f;

        [SerializeField]
        private bool buildOnStart = true;

        [SerializeField]
        private bool playOnStart = true;

        [SerializeField]
        private Material previewMaterial;

        [SerializeField]
        private Camera previewCamera;

        private Transform builtRoot;

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        private readonly Dictionary<string, Material> materialCache =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Texture2D> textureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private Material untexturedMaterial;

        private Texture2D whiteTexture;

        private GameObject skaterObject;

        private GameObject goalieObject;

        private bool built;

        private double playbackSeconds;

        public SkaterPreviewData Data { get; private set; }

        public bool IsBuilt
        {
            get { return built; }
        }

        // The playback clock the animation frames are derived from.
        public double PlaybackSeconds
        {
            get { return playbackSeconds; }
        }

        public string SkaterModel
        {
            get { return skaterModel; }
            set { skaterModel = value; }
        }

        public string GoalieModel
        {
            get { return goalieModel; }
            set { goalieModel = value; }
        }

        public string SkaterAssembly
        {
            get { return skaterAssembly; }
            set { skaterAssembly = value; }
        }

        public string GoalieAssembly
        {
            get { return goalieAssembly; }
            set { goalieAssembly = value; }
        }

        public OriginalModelSet ModelSet
        {
            get { return modelSet; }
            set { modelSet = value; }
        }

        public bool IncludeGoalie
        {
            get { return includeGoalie; }
            set { includeGoalie = value; }
        }

        public Material PreviewMaterial
        {
            get { return previewMaterial; }
            set { previewMaterial = value; }
        }

        public Camera PreviewCamera
        {
            get { return previewCamera; }
            set { previewCamera = value; }
        }

        public static OriginalAssetRoot ResolveRoot()
        {
            return OriginalAssetRoot.Load(Application.streamingAssetsPath, OriginalAssetRoot.DefaultInstallPath);
        }

        private void Awake()
        {
            if (!buildOnStart)
            {
                return;
            }

            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_FAILED " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void Update()
        {
            if (!built || !playOnStart || Data == null)
            {
                return;
            }

            playbackSeconds += Time.deltaTime;

            try
            {
                SampleTime(playbackSeconds);
            }
            catch (Exception exception)
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_FAILED playback: " + exception.Message);
                playOnStart = false;
            }
        }

        // Loads both original characters and assembles the preview scene objects. Throws (with the
        // exact missing file) when the player's install cannot supply the original data.
        public SkaterPreviewData Build()
        {
            ClearBuilt();

            OriginalAssetRoot root = ResolveRoot();

            Debug.Log("UBLADE3_SKATER_PREVIEW_START install=" + root.InstallPath + " data=" + root.DataPath
                      + " set=" + (modelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd"));

            SkaterPreviewOptions options = new SkaterPreviewOptions();
            options.SkaterModel = skaterModel;
            options.GoalieModel = goalieModel;
            options.SkaterAssembly = skaterAssembly;
            options.GoalieAssembly = goalieAssembly;
            options.ModelSet = modelSet;
            options.LodIndex = lodIndex;
            options.RemapPalette = remapPalette;
            options.IncludeGoalie = includeGoalie;

            Data = SkaterPreviewData.Load(root, options);

            builtRoot = new GameObject("OriginalSkaters").transform;
            builtRoot.SetParent(transform, false);

            skaterObject = CreateCharacterObject(Data.Skater);
            goalieObject = Data.Goalie == null ? null : CreateCharacterObject(Data.Goalie);

            EnsureCamera();
            LayoutCharacters();
            SampleTime(0.0);
            ApplyFraming(true, includeGoalie);

            built = true;
            playbackSeconds = 0.0;

            Debug.Log(Data.Describe());
            Debug.Log("UBLADE3_SKATER_PREVIEW_SCENE_OK skater=" + Data.Skater.ModelName
                      + " goalie=" + (Data.Goalie == null ? "none" : Data.Goalie.ModelName)
                      + " objects=" + (Data.Goalie == null ? 1 : 2)
                      + " triangles=" + Data.TotalTriangles
                      + " digest=" + Data.Digest);

            return Data;
        }

        private GameObject CreateCharacterObject(SkaterCharacter character)
        {
            GameObject go = new GameObject(character.Role + " [" + character.ModelName + "]");
            go.transform.SetParent(builtRoot, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = character.UnityMesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Material[] materials = new Material[character.Mesh.SubmeshTextureNames.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = GetMaterial(character, character.Mesh.SubmeshTextureNames[i]);
            }

            renderer.sharedMaterials = materials;

            return go;
        }

        private Material GetMaterial(SkaterCharacter character, string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                if (untexturedMaterial == null)
                {
                    untexturedMaterial = CreateMaterial("UBlade3 original skater untextured");
                    untexturedMaterial.mainTexture = GetWhiteTexture();
                }

                return untexturedMaterial;
            }

            Material material;

            if (materialCache.TryGetValue(textureName, out material))
            {
                return material;
            }

            Texture2D texture;

            if (!textureCache.TryGetValue(textureName, out texture))
            {
                BmpTexture bitmap;

                if (!character.Textures.TryGetValue(textureName, out bitmap))
                {
                    throw new OriginalAssetException(
                        "Texture \"" + textureName + "\" did not load for " + character.ModelName + ".");
                }

                texture = new Texture2D(bitmap.Width, bitmap.Height, TextureFormat.RGBA32, false);
                texture.name = textureName;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 0;
                texture.LoadRawTextureData(bitmap.RgbaBottomUp);
                texture.Apply(false, true);

                textureCache[textureName] = texture;
                createdObjects.Add(texture);
            }

            material = CreateMaterial("UBlade3 original texture " + textureName);
            material.mainTexture = texture;
            materialCache[textureName] = material;

            return material;
        }

        private Material CreateMaterial(string name)
        {
            Shader shader = previewMaterial != null ? previewMaterial.shader : Shader.Find(ShaderName);

            if (shader == null)
            {
                throw new OriginalAssetException(
                    "Shader \"" + ShaderName + "\" was not found. The skater preview reuses the N2 drome "
                    + "preview surface at Assets/Scripts/UBlade3/Native/DromePreview.shader.");
            }

            Material material = new Material(shader);
            material.name = name;
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            createdObjects.Add(material);

            return material;
        }

        private Texture2D GetWhiteTexture()
        {
            if (whiteTexture == null)
            {
                whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTexture.name = "UBlade3 skater untextured white";
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply(false, false);
                createdObjects.Add(whiteTexture);
            }

            return whiteTexture;
        }

        // The two characters stand side by side on the original drome's up axis (Z), which the mesh
        // conversion turns into Unity's Y. Their feet are at Unity Y zero in the shipped animation
        // frames, so the root stays at the origin and only lateral placement and facing are set.
        public void LayoutCharacters()
        {
            if (Data == null)
            {
                return;
            }

            float yaw = skaterYawDegrees * 0.017453292f;

            Data.Skater.YawRadians = yaw;

            if (Data.Goalie != null)
            {
                Data.Goalie.YawRadians = goalieYawDegrees * 0.017453292f;
                Data.Goalie.OffsetX = includeGoalie ? -LineupGap : 0f;
                Data.Skater.OffsetX = includeGoalie ? LineupGap : 0f;
            }
            else
            {
                Data.Skater.OffsetX = 0f;
            }

            ApplyTransformToGameObjects();
        }

        private void ApplyTransformToGameObjects()
        {
            if (Data == null)
            {
                return;
            }

            if (skaterObject != null)
            {
                skaterObject.transform.position = new Vector3(Data.Skater.OffsetX, 0f, 0f);
            }

            if (goalieObject != null)
            {
                goalieObject.transform.position = new Vector3(Data.Goalie.OffsetX, 0f, 0f);
            }
        }

        // Poses both characters at the frame the given playback time produces, and pushes the
        // vertices to their meshes.
        public void SampleTime(double seconds)
        {
            if (Data == null)
            {
                return;
            }

            playbackSeconds = seconds;

            SampleFrame(Data.Skater, Data.Skater.FrameAtSeconds(seconds));

            if (Data.Goalie != null)
            {
                SampleFrame(Data.Goalie, Data.Goalie.FrameAtSeconds(seconds));
            }
        }

        // Poses both characters at the same clip-relative frame index where possible; the Editor's
        // per-character captures use SampleTime for the shipped timing and SampleFrame when it wants
        // a specific pose.
        public void SampleFrame(SkaterCharacter character, int frame)
        {
            if (Data == null || character == null)
            {
                return;
            }

            character.PoseFrame(frame);

            if (character.UnityMesh != null)
            {
                character.Mesh.ApplyTo(character.UnityMesh);
            }
        }

        public void SetVisible(bool skater, bool goalie)
        {
            if (skaterObject != null)
            {
                skaterObject.SetActive(skater);
            }

            if (goalieObject != null)
            {
                goalieObject.SetActive(goalie);
            }
        }

        public Camera EnsureCamera()
        {
            if (previewCamera == null)
            {
                foreach (Camera candidate in GetComponentsInChildren<Camera>(true))
                {
                    previewCamera = candidate;
                    break;
                }
            }

            if (previewCamera == null)
            {
                GameObject cameraObject = new GameObject("SkaterPreviewCamera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "MainCamera";
                previewCamera = cameraObject.AddComponent<Camera>();
            }

            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = ClearColour;
            previewCamera.fieldOfView = FieldOfView;
            previewCamera.nearClipPlane = NearClip;
            previewCamera.farClipPlane = FarClip;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
            previewCamera.orthographic = false;

            return previewCamera;
        }

        // Frames whichever characters are visible from the loaded posed bounds: a three quarter
        // front view, high enough that the ground line sits below the feet.
        public void ApplyFraming()
        {
            ApplyFraming(true, Data != null && Data.Goalie != null);
        }

        public void ApplyFraming(bool frameSkater, bool frameGoalie)
        {
            if (Data == null)
            {
                return;
            }

            Camera camera = EnsureCamera();

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            Accumulate(Data.Skater, frameSkater, ref min, ref max, ref any);

            if (Data.Goalie != null && frameGoalie)
            {
                Accumulate(Data.Goalie, true, ref min, ref max, ref any);
            }

            if (!any)
            {
                return;
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;

            float aspect = 16f / 9f;
            float halfHeight = Mathf.Max(size.y, size.x / aspect) * 0.5f;
            float distance = (halfHeight / Mathf.Tan(FieldOfView * 0.5f * 0.017453292f)) * 1.25f;

            Vector3 direction = new Vector3(0.32f, 0.10f, 1f).normalized;
            Vector3 eye = center + (direction * distance);
            eye.y = Mathf.Max(eye.y, max.y + (size.y * 0.05f));

            camera.transform.position = eye;
            camera.transform.LookAt(center, Vector3.up);
        }

        private static void Accumulate(SkaterCharacter character, bool visible, ref Vector3 min, ref Vector3 max, ref bool any)
        {
            if (!visible || character == null || character.Mesh == null)
            {
                return;
            }

            Vector3 offset = new Vector3(character.OffsetX, 0f, 0f);
            Vector3 localMin = character.Mesh.BoundsMin + offset;
            Vector3 localMax = character.Mesh.BoundsMax + offset;

            min = Vector3.Min(min, localMin);
            max = Vector3.Max(max, localMax);
            any = true;
        }

        public void ClearBuilt()
        {
            if (builtRoot != null)
            {
                SafeDestroy(builtRoot.gameObject);
                builtRoot = null;
            }

            for (int i = 0; i < createdObjects.Count; i++)
            {
                SafeDestroy(createdObjects[i]);
            }

            createdObjects.Clear();
            materialCache.Clear();
            textureCache.Clear();
            untexturedMaterial = null;
            whiteTexture = null;
            skaterObject = null;
            goalieObject = null;

            if (Data != null)
            {
                if (Data.Skater != null && Data.Skater.UnityMesh != null)
                {
                    SafeDestroy(Data.Skater.UnityMesh);
                }

                if (Data.Goalie != null && Data.Goalie.UnityMesh != null)
                {
                    SafeDestroy(Data.Goalie.UnityMesh);
                }
            }

            Data = null;
            built = false;
            playbackSeconds = 0.0;
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
}
