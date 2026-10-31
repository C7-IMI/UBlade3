using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // The N2 drome preview scene. It resolves the player's own HyperBlade install, asks
    // DromeSceneLoader for the original CHIDRO.ENV objects and their WBM geometry, and assembles
    // them as Unity meshes with the original textures and palette colours. Nothing is procedural:
    // if the install or a model is missing the preview reports the exact file and fails.
    //
    // The camera may be captured from an Editor batch-mode run (UBlade3.EditorTools.
    // UBlade3DromePreview) or from a built Windows player with --drome-capture=<png>.
    [DisallowMultipleComponent]
    public sealed class DromePreviewScene : MonoBehaviour
    {
        public const string ShaderName = "UBlade3/DromePreview";

        // DDraw::ClearBackBuffer(CLEAR_COLOUR) with CLEAR_COLOUR 0x505050 (graphics/renderer.h).
        public static readonly Color ClearColour = new Color32(0x50, 0x50, 0x50, 255);

        // CameraProjectionState (src/engine/cameraprojectionstate.cpp): the horizontal field of view
        // is fixed at 85 degrees in both axes for the main camera path.
        public const float FieldOfView = 85f;

        // The engine's projection near/far for the main camera: far 1000 world units
        // (CameraProjectionState::CreateProjectionState's 1000.0f) and the near plane the 72 dpi
        // projection maths produces (halfWidthCm * cos / sin; about 12.3 units at 4:3).
        public const float NearClip = 12.3f;

        public const float FarClip = 1000f;

        [SerializeField]
        private DromeShadingMode mode = DromeShadingMode.Shaded;

        [SerializeField]
        private string dromeFile = OriginalAssetRoot.DefaultDromeFile;

        [SerializeField]
        private Material previewMaterial;

        [SerializeField]
        private Camera previewCamera;

        [SerializeField]
        private bool buildOnStart = true;

        [SerializeField]
        private bool captureOnStart;

        [SerializeField]
        private string capturePath;

        [SerializeField]
        private int captureWidth = DromePreviewCapture.DefaultWidth;

        [SerializeField]
        private int captureHeight = DromePreviewCapture.DefaultHeight;

        // When eye and target differ the preview camera uses them instead of the framing computed
        // from the drome's bounding box. Both are Unity-space (Y up).
        [SerializeField]
        private Vector3 cameraOverrideEye;

        [SerializeField]
        private Vector3 cameraOverrideTarget;

        private Transform builtRoot;

        private readonly List<Object> createdObjects = new List<Object>();

        private readonly Dictionary<string, Material> materialCache =
            new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Texture2D> textureCache =
            new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        private Material untexturedMaterial;

        private Texture2D whiteTexture;

        private bool built;

        public DromeSceneData Data { get; private set; }

        public DromeShadingMode ShadingMode
        {
            get { return mode; }
            set { mode = value; }
        }

        public string DromeFileName
        {
            get { return dromeFile; }
            set { dromeFile = value; }
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

        public bool CaptureOnStart
        {
            get { return captureOnStart; }
            set { captureOnStart = value; }
        }

        public string CapturePath
        {
            get { return capturePath; }
            set { capturePath = value; }
        }

        public Vector3 CameraOverrideEye
        {
            get { return cameraOverrideEye; }
            set { cameraOverrideEye = value; }
        }

        public Vector3 CameraOverrideTarget
        {
            get { return cameraOverrideTarget; }
            set { cameraOverrideTarget = value; }
        }

        public bool IsBuilt
        {
            get { return built; }
        }

        public static OriginalAssetRoot ResolveRoot()
        {
            return OriginalAssetRoot.Load(Application.streamingAssetsPath, OriginalAssetRoot.DefaultInstallPath);
        }

        private void Awake()
        {
            ApplyCommandLine();

            if (buildOnStart)
            {
                try
                {
                    Build();
                }
                catch (System.Exception exception)
                {
                    Debug.LogError("UBLADE3_DROME_PREVIEW_FAILED " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            if (captureOnStart && !string.IsNullOrEmpty(capturePath))
            {
                StartCoroutine(CaptureAfterRender());
            }
        }

        private IEnumerator CaptureAfterRender()
        {
            // Two frames make sure the scene is fully applied and the camera has a target size.
            yield return null;
            yield return new WaitForEndOfFrame();

            try
            {
                DromePreviewCapture.Result result = DromePreviewCapture.Capture(
                    EnsureCamera(), capturePath, captureWidth, captureHeight);

                Debug.Log("UBLADE3_DROME_PREVIEW_CAPTURE_OK mode=" + ModeName() + " path=" + result.Path
                          + " width=" + result.Width + " height=" + result.Height + " bytes=" + result.Bytes + " digest="
                          + (Data != null ? Data.Digest : "none"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("UBLADE3_DROME_PREVIEW_CAPTURE_FAILED " + exception.GetType().Name + ": " + exception.Message);
            }

            if (Application.isBatchMode || !Application.isEditor)
            {
                Application.Quit();
            }
        }

        private void ApplyCommandLine()
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith("--drome-mode=", System.StringComparison.Ordinal))
                {
                    string value = argument.Substring("--drome-mode=".Length).Trim();

                    if (string.Equals(value, "textured", System.StringComparison.OrdinalIgnoreCase))
                    {
                        mode = DromeShadingMode.Textured;
                    }
                    else if (string.Equals(value, "shaded", System.StringComparison.OrdinalIgnoreCase))
                    {
                        mode = DromeShadingMode.Shaded;
                    }
                }
                else if (argument.StartsWith("--drome-capture=", System.StringComparison.Ordinal))
                {
                    capturePath = argument.Substring("--drome-capture=".Length);
                    captureOnStart = true;
                    buildOnStart = true;
                }
                else if (argument.StartsWith("--drome-drome=", System.StringComparison.Ordinal))
                {
                    dromeFile = argument.Substring("--drome-drome=".Length);
                }
                else if (argument.StartsWith("--drome-eye=", System.StringComparison.Ordinal))
                {
                    Vector3 eye;

                    if (TryParseVector(argument.Substring("--drome-eye=".Length), out eye))
                    {
                        cameraOverrideEye = eye;
                    }
                }
                else if (argument.StartsWith("--drome-target=", System.StringComparison.Ordinal))
                {
                    Vector3 target;

                    if (TryParseVector(argument.Substring("--drome-target=".Length), out target))
                    {
                        cameraOverrideTarget = target;
                    }
                }
            }
        }

        private static bool TryParseVector(string text, out Vector3 value)
        {
            value = Vector3.zero;
            string[] fields = text.Split(',');

            if (fields.Length != 3)
            {
                return false;
            }

            float x;
            float y;
            float z;

            if (!float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return false;
            }

            value = new Vector3(x, y, z);

            return true;
        }

        // Loads and assembles the preview. Throws (with the exact missing file) when the player's
        // install cannot supply the original data, and logs the probe's report.
        public DromeSceneData Build()
        {
            ClearBuilt();

            OriginalAssetRoot root = ResolveRoot();
            Debug.Log("UBLADE3_DROME_PREVIEW_START mode=" + ModeName() + " drome=" + dromeFile
                      + " install=" + root.InstallPath + " data=" + root.DataPath);

            Data = DromeSceneLoader.Load(root, mode, dromeFile);

            builtRoot = new GameObject("OriginalDrome").transform;
            builtRoot.SetParent(transform, false);

            foreach (DromeObjectData item in Data.Objects)
            {
                CreateObject(item);
            }

            EnsureCamera();
            ApplyFraming();

            built = true;

            Debug.Log(Data.Describe());
            Debug.Log("UBLADE3_DROME_PREVIEW_SCENE_OK mode=" + ModeName()
                      + " objects=" + Data.Drome.Objects.Count
                      + " meshes=" + Data.MeshCount
                      + " builtVerts=" + Data.BuiltVertices
                      + " triangles=" + Data.BuiltTriangles
                      + " textures=" + Data.TextureFiles.Count
                      + " missingTextures=" + Data.MissingTextures.Count
                      + " digest=" + Data.Digest);

            return Data;
        }

        private void CreateObject(DromeObjectData item)
        {
            GameObject go = new GameObject(item.Definition.Name + " [" + item.Definition.TypeName + "]");
            go.transform.SetParent(builtRoot, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = item.Built.Mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Material[] materials = new Material[item.Built.SubmeshTextureNames.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = GetMaterial(item.Built.SubmeshTextureNames[i]);
            }

            renderer.sharedMaterials = materials;
        }

        private Material GetMaterial(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                if (untexturedMaterial == null)
                {
                    untexturedMaterial = CreateMaterial("UBlade3 original drome untextured");
                    untexturedMaterial.mainTexture = GetWhiteTexture();
                }

                return untexturedMaterial;
            }

            Material material;

            if (materialCache.TryGetValue(textureName, out material))
            {
                return material;
            }

            Texture2D texture = GetTexture(textureName);

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
                    "Shader \"" + ShaderName + "\" was not found. The drome preview ships it at "
                    + "Assets/Scripts/UBlade3/Native/DromePreview.shader; an Editor scene builder also "
                    + "assigns it through the DromePreviewMaterial asset.");
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
                whiteTexture.name = "UBlade3 drome untextured white";
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply(false, false);
                createdObjects.Add(whiteTexture);
            }

            return whiteTexture;
        }

        private Texture2D GetTexture(string textureName)
        {
            Texture2D texture;

            if (textureCache.TryGetValue(textureName, out texture))
            {
                return texture;
            }

            string path;

            if (Data == null || !Data.TextureFiles.TryGetValue(textureName, out path))
            {
                throw new OriginalAssetException(
                    "Texture \"" + textureName + "\" has no resolved file in Data\\Textures.");
            }

            BmpTexture bitmap = BmpReader.LoadFile(path);

            texture = new Texture2D(bitmap.Width, bitmap.Height, TextureFormat.RGBA32, false);
            texture.name = textureName;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.LoadRawTextureData(bitmap.RgbaBottomUp);
            texture.Apply(false, true);

            textureCache[textureName] = texture;
            createdObjects.Add(texture);

            return texture;
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
                GameObject cameraObject = new GameObject("DromePreviewCamera");
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

        // Frames the drome from inside the bowl, looking down its long axis (Unity Z, the original
        // y). The .ENV has no MainCamera object - the engine's camera lives in Data\COMMON.CFG as a
        // plan driven follower (pos 60 170 10, azim 90, elev -20) - so a still preview chooses a
        // spectator view of the loaded geometry instead of guessing a moving camera's final pose.
        public void ApplyFraming()
        {
            Camera camera = EnsureCamera();

            Vector3 min;
            Vector3 max;

            if (Data != null && Data.HasDromeBounds)
            {
                min = Data.DromeBoundsMin;
                max = Data.DromeBoundsMax;
            }
            else if (Data != null)
            {
                min = Data.BoundsMin;
                max = Data.BoundsMax;
            }
            else
            {
                return;
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;

            if (cameraOverrideEye != cameraOverrideTarget)
            {
                camera.transform.position = cameraOverrideEye;
                camera.transform.LookAt(cameraOverrideTarget, Vector3.up);

                return;
            }

            // Unity space here: x is the original lateral axis, y is the original z (up) and z is
            // -y (the original long axis). The eye sits inside the arena shell, a third of the way
            // up the loaded height, near one end; the target crosses the props toward the far end.
            // The drome shell spans x +/-375, y -105..232 (up) and z +/-371 on the shipped CHIDRO
            // model, which is what these coefficients were settled against (verified captures in
            // docs/DROME-PREVIEW.md); they are derived from the loaded bounds, not hard-coded to
            // the camera position.
            float floorY = min.y;

            Vector3 eye = new Vector3(
                center.x,
                floorY + (size.y * 0.40f),
                center.z + (size.z * 0.16f));

            Vector3 target = new Vector3(
                center.x,
                floorY + (size.y * 0.34f),
                center.z - (size.z * 0.16f));

            camera.transform.position = eye;
            camera.transform.LookAt(target, Vector3.up);
        }

        public void ClearBuilt()
        {
            StopAllCoroutines();

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
            Data = null;
            built = false;

            if (previewMaterial != null)
            {
                // The assigned asset is not ours to destroy.
            }
        }

        private static void SafeDestroy(Object value)
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

        public string ModeName()
        {
            return mode == DromeShadingMode.Shaded ? "shaded" : "textured";
        }

        // Debug aid for the N2 winding check: the preview shader's _Cull property is set on every
        // material this scene created. Shipped captures leave it at Back.
        public void SetCullMode(UnityEngine.Rendering.CullMode cullMode)
        {
            if (untexturedMaterial != null)
            {
                untexturedMaterial.SetFloat("_Cull", (float)cullMode);
            }

            foreach (Material material in materialCache.Values)
            {
                material.SetFloat("_Cull", (float)cullMode);
            }
        }

        private void OnDestroy()
        {
            ClearBuilt();
        }
    }
}
