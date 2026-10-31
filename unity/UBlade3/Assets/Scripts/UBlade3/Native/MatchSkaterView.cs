using System;
using System.Collections.Generic;
using UnityEngine;
using UBlade3.Assets;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // One original skater in the match scene: a MeshFilter/MeshRenderer built from the character's
    // SkaterMeshData, the original BMP textures and the N2/N3 DromePreview MODULATE surface, plus the
    // playback clock and the interpolated placement. The mesh itself is posed on the CPU by
    // SkaterMeshData.Pose exactly as the N3 preview does, so the geometry/materials are not rewritten
    // here; this class only places and clock-drives one character.
    public sealed class MatchSkaterView
    {
        public const string ShaderName = "UBlade3/DromePreview";

        // ObjectStats::UpdateMomentumAndEnergy replaces the model's per-frame loop with the shipped
        // clip timing: int(time * standardFps), 30 fps for both assemblies. One animation frame per
        // 30 Hz tick is therefore the exact original clip rate for this port (docs/SKATER-PREVIEW.md
        // section 4).
        public readonly MatchCharacterData Data;

        public readonly SkaterCharacter Character;

        public GameObject Root { get; private set; }

        // Playback clock, in seconds, advanced one fixed tick at a time by the scene.
        public double AnimationSeconds;

        public int Frame { get; private set; }

        public float Yaw { get; private set; }

        public Vector3 UnityPosition { get; private set; }

        public Vector3 PreviousUnityPosition { get; private set; }

        public float PreviousYaw { get; private set; }

        public bool IsBuilt { get; private set; }

        private MeshFilter meshFilter;

        private MeshRenderer meshRenderer;

        private readonly Dictionary<string, Material> materialCache =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Texture2D> textureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private Material untexturedMaterial;

        private Texture2D whiteTexture;

        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public MatchSkaterView(MatchCharacterData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            Data = data;
            Character = data.Character;
            Frame = Character.FrameAtSeconds(0.0);
            Yaw = 0.0f;
            IsBuilt = false;
        }

        public int SimSlot
        {
            get { return Data.Slot.SimSlot; }
        }

        public string Role
        {
            get { return Data.Slot.Role; }
        }

        // Creates the Unity objects for this character. The materials use the same unlit
        // texture*vertex-colour shader the N2 drome and N3 skater previews use (D3DRend::DrawFace's
        // MODULATE); nothing is procedural.
        public void Build(Transform parent)
        {
            if (IsBuilt)
            {
                return;
            }

            Shader shader = Shader.Find(ShaderName);

            if (shader == null)
            {
                throw new OriginalAssetException(
                    "Shader \"" + ShaderName + "\" was not found; the match scene uses the original N2 "
                    + "drome surface at Assets/Scripts/UBlade3/Native/DromePreview.shader.");
            }

            Root = new GameObject("MatchSkater " + SimSlot + " [" + Character.ModelName + "]");
            Root.transform.SetParent(parent, false);

            meshFilter = Root.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = Character.UnityMesh;

            meshRenderer = Root.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Material[] materials = new Material[Character.SubmeshTextureNames.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = GetMaterial(shader, Character.SubmeshTextureNames[i]);
            }

            meshRenderer.sharedMaterials = materials;

            IsBuilt = true;
            PoseAtCurrentState();
        }

        private Material GetMaterial(Shader shader, string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                if (untexturedMaterial == null)
                {
                    untexturedMaterial = CreateMaterial(shader, "UBlade3 match untextured");
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

            material = CreateMaterial(shader, "UBlade3 original texture " + textureName);
            material.mainTexture = texture;
            materialCache[textureName] = material;

            return material;
        }

        private Material CreateMaterial(Shader shader, string name)
        {
            Material material = new Material(shader);
            material.name = name;
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            owned.Add(material);

            return material;
        }

        private Texture2D GetTexture(string textureName)
        {
            Texture2D texture;

            if (textureCache.TryGetValue(textureName, out texture))
            {
                return texture;
            }

            BmpTexture bitmap;

            if (!Character.Textures.TryGetValue(textureName, out bitmap))
            {
                throw new OriginalAssetException(
                    "Texture \"" + textureName + "\" did not load for " + Character.ModelName + ".");
            }

            texture = new Texture2D(bitmap.Width, bitmap.Height, TextureFormat.RGBA32, false);
            texture.name = textureName;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.LoadRawTextureData(bitmap.RgbaBottomUp);
            texture.Apply(false, true);

            textureCache[textureName] = texture;
            owned.Add(texture);

            return texture;
        }

        private Texture2D GetWhiteTexture()
        {
            if (whiteTexture == null)
            {
                whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTexture.name = "UBlade3 match untextured white";
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply(false, false);
                owned.Add(whiteTexture);
            }

            return whiteTexture;
        }

        // Advances the original clip clock by one fixed 30 Hz tick and places the character from the
        // simulation body. The previous render state is kept for interpolation.
        public void Tick(double deltaSeconds, SimVec3 position, float yawRadians)
        {
            AnimationSeconds += deltaSeconds;
            Frame = Character.FrameAtSeconds(AnimationSeconds);

            PreviousUnityPosition = UnityPosition;
            PreviousYaw = Yaw;

            UnityPosition = ToUnity(position);
            Yaw = yawRadians;

            if (Root != null)
            {
                Root.transform.position = UnityPosition;
            }

            PoseAtCurrentState();
        }

        // Poses the mesh at the current frame and facing. Called once per tick and again when the
        // render interpolation changes the facing between ticks.
        public void PoseAtCurrentState()
        {
            Character.YawRadians = Yaw;
            Character.PoseFrame(Frame);

            if (Character.UnityMesh != null)
            {
                Character.Mesh.ApplyTo(Character.UnityMesh);
            }
        }

        // Render-time interpolation between the two most recent simulation states: the placement is
        // lerped and the facing is interpolated on the shortest arc, then the mesh is re-posed with
        // the interpolated facing (the clip frame stays the original 30 Hz frame).
        public void ApplyInterpolated(float alpha)
        {
            if (Root == null)
            {
                return;
            }

            alpha = Mathf.Clamp01(alpha);

            Root.transform.position = Vector3.Lerp(PreviousUnityPosition, UnityPosition, alpha);

            float yaw = LerpAngle(PreviousYaw, Yaw, alpha);

            if (Mathf.Abs(Mathf.DeltaAngle(PreviousYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg)) > 0.001f
                || alpha > 0.0f)
            {
                Character.YawRadians = yaw;
                Character.PoseFrame(Frame);

                if (Character.UnityMesh != null)
                {
                    Character.Mesh.ApplyTo(Character.UnityMesh);
                }
            }
        }

        // At build time and for capture, place the character exactly at the current simulation state.
        public void ApplyCurrent()
        {
            if (Root == null)
            {
                return;
            }

            Root.transform.position = UnityPosition;
            PreviousUnityPosition = UnityPosition;
            PreviousYaw = Yaw;
            PoseAtCurrentState();
        }

        public static Vector3 ToUnity(SimVec3 value)
        {
            return new Vector3(value.x, value.z, -value.y);
        }

        public static Vector3 ToUnityDirection(SimVec3 value)
        {
            return new Vector3(value.x, value.z, -value.y);
        }

        public static float LerpAngle(float from, float to, float t)
        {
            float delta = Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            return from + (delta * t);
        }

        // The model's forward axis after EngineObject::ApplyHeaderRotation: the rig's header angle
        // (90, 0, 180) degrees maps the model +Y (the rig's up) to world +Z and the model +Z to world
        // +Y, so the facing yaw about the original up axis makes forward (-sin(yaw), cos(yaw), 0).
        // This is the exact render-slot facing rule the animation port cannot supply yet; the
        // velocity direction stands in for the plan-driven object rotation.
        public static float YawForDirection(SimVec3 direction)
        {
            float x = direction.x;
            float y = direction.y;

            if (Math.Abs(x) < 1e-5f && Math.Abs(y) < 1e-5f)
            {
                return 0.0f;
            }

            return (float)Math.Atan2(-x, y);
        }

        public void Destroy()
        {
            if (Root != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(Root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }

                Root = null;
            }

            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(owned[i]);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(owned[i]);
                    }
                }
            }

            owned.Clear();
            materialCache.Clear();
            textureCache.Clear();
            untexturedMaterial = null;
            whiteTexture = null;

            if (Character != null && Character.UnityMesh != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(Character.UnityMesh);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(Character.UnityMesh);
                }

                Character.UnityMesh = null;
            }

            IsBuilt = false;
        }

        public string Describe()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_VIEW slot=").Append(SimSlot);
            text.Append(" role=").Append(Role);
            text.Append(" model=").Append(Character.ModelName);
            text.Append(" frame=").Append(Frame);
            text.Append(" seconds=").Append(AnimationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            text.Append(" unityPos=(").Append(MatchSimulation.F(UnityPosition.x)).Append(',')
                .Append(MatchSimulation.F(UnityPosition.y)).Append(',').Append(MatchSimulation.F(UnityPosition.z)).Append(')');
            text.Append(" yawRadians=").Append(MatchSimulation.F(Yaw));
            text.Append(" clip=").Append(Character.Animation.WbaName);
            text.Append(" frames=").Append(Character.Animation.DisplayedFrameCount);
            text.Append(" triangles=").Append(Character.Mesh.TriangleCount);

            return text.ToString();
        }
    }
}
