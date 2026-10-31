using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Assets;
using UBlade3.Data;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The drome's own ROK model: DromeFile.Rok (CHIDRO.ENV's type=Rok name=AndRoll modfile=Rokmix)
    // decoded through the verified WBM reader, the same material palette conversion and face-fan
    // builder the skater preview uses, with the original BMP textures. The model's mesh blocks are
    // the render-slot variants (rokobject.cpp SetRenderSlotFlags 0x00473b90): the simulation's
    // RokState.RenderBone selects which block's triangles are drawn.
    public sealed class MatchRokData
    {
        public MatchWorldData World;
        public DromeObjectData Object;
        public WbmModel Model;
        public SkaterRig Rig;
        public SkaterMeshData Mesh;
        public string[] TextureNames;
        public string[] TexturePaths;
        public readonly List<string> MissingTextures = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        // Per sub-mesh triangle -> model bone slot, so a render bone can be selected at draw time.
        public int[][] SubmeshTriangleBones;
        public int[] BoneTriangleCounts;

        public int TriangleCount
        {
            get { return Mesh == null ? 0 : Mesh.TriangleCount; }
        }

        public int BoneCount
        {
            get { return Rig == null ? 0 : Rig.BoneCount; }
        }

        public static MatchRokData Load(MatchWorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }

            MatchRokData data = new MatchRokData();
            data.World = world;

            DromeObjectData found = null;

            for (int i = 0; i < world.Drome.Objects.Count; i++)
            {
                DromeObjectData item = world.Drome.Objects[i];

                if (item.Definition.Kind == ObjectKind.Rok)
                {
                    found = item;
                    break;
                }
            }

            if (found == null)
            {
                throw new OriginalAssetException(
                    "The loaded drome has no type=Rok object; " + world.Drome.DromeFileName + " should define one.");
            }

            data.Object = found;
            data.Model = found.Model;
            data.Rig = SkaterRig.FromModel(found.Model, found.Definition.Model, found.ModelSet, found.ModelPath);
            data.Mesh = SkaterMeshBuilder.Build(
                world.Root,
                found.Model,
                data.Rig,
                world.Drome.Palette,
                0,
                found.ModelSet,
                (found.ModelId & 1) != 0 || found.Definition.ForceRemap);

            data.TextureNames = data.Mesh.SubmeshTextureNames;
            data.TexturePaths = new string[data.TextureNames.Length];

            for (int i = 0; i < data.TextureNames.Length; i++)
            {
                string name = data.TextureNames[i];

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string path = world.Root.FindTextureFile(name);

                if (path == null)
                {
                    data.MissingTextures.Add(name);
                }
                else
                {
                    data.TexturePaths[i] = path;
                }
            }

            data.Warnings.AddRange(data.Mesh.Warnings);

            // Per sub-mesh triangle bone: the corner fan keeps every triangle's three corners on the
            // same MeshBlock bone (SkaterMeshBuilder.AppendFace stores the block slot), so the first
            // corner names the triangle's slot.
            data.SubmeshTriangleBones = new int[data.Mesh.SubmeshCount][];
            data.BoneTriangleCounts = new int[data.Rig.BoneCount];

            for (int submesh = 0; submesh < data.Mesh.SubmeshCount; submesh++)
            {
                int first = data.Mesh.SubmeshFirstCorner[submesh];
                int count = data.Mesh.SubmeshCornerCount[submesh];
                int triangles = count / 3;
                int[] bones = new int[triangles];

                for (int t = 0; t < triangles; t++)
                {
                    int corner = first + (t * 3);
                    int bone = data.Mesh.BoneIndices[corner];
                    bones[t] = bone;

                    if (bone >= 0 && bone < data.BoneTriangleCounts.Length)
                    {
                        data.BoneTriangleCounts[bone]++;
                    }
                }

                data.SubmeshTriangleBones[submesh] = bones;
            }

            return data;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_GAMEPLAY_ROK model=").Append(Object.Definition.Model);
            text.Append(" name=\"").Append(Object.Definition.Name).Append('"');
            text.Append(" set=").Append(Object.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
            text.Append(" lod=0 bones=").Append(BoneCount);
            text.Append(" meshSlots=").Append(Rig.MeshSlotCount);
            text.Append(" materials=").Append(Mesh.Materials.Length);
            text.Append(" submeshes=").Append(Mesh.SubmeshCount);
            text.Append(" triangles=").Append(TriangleCount);
            text.Append(" verts=").Append(Mesh.UnityVertexCount);
            text.Append(" textures=").Append(CountTextures());
            text.Append(" missingTextures=").Append(MissingTextures.Count);
            text.Append(" warnings=").Append(Warnings.Count);
            text.Append(" boneTriangles=");

            for (int i = 0; i < BoneTriangleCounts.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append(i).Append(':').Append(BoneTriangleCounts[i]);
            }

            return text.ToString();
        }

        public int CountTextures()
        {
            int count = 0;

            for (int i = 0; i < TexturePaths.Length; i++)
            {
                if (!string.IsNullOrEmpty(TexturePaths[i]))
                {
                    count++;
                }
            }

            return count;
        }

        // The triangles of one render bone across every texture sub-mesh. Empty when the model has no
        // geometry on that slot (the engine hides the bone in that case).
        public int[][] TrianglesForBone(int bone)
        {
            int[][] result = new int[Mesh.SubmeshCount][];

            for (int submesh = 0; submesh < Mesh.SubmeshCount; submesh++)
            {
                int[] bones = SubmeshTriangleBones[submesh];
                List<int> indices = new List<int>();
                int first = Mesh.SubmeshFirstCorner[submesh];
                int count = Mesh.SubmeshCornerCount[submesh];
                int triangles = count / 3;

                for (int t = 0; t < triangles; t++)
                {
                    if (bones[t] != bone)
                    {
                        continue;
                    }

                    int corner = first + (t * 3);
                    indices.Add(corner);
                    indices.Add(corner + 1);
                    indices.Add(corner + 2);
                }

                result[submesh] = indices.ToArray();
            }

            return result;
        }
    }

    // One ROK in the match scene: the original WBM mesh, original BMP textures and the N2/N3
    // DromePreview MODULATE surface. Position and facing come from Sim.Rok's body state and the
    // render bone from RokState.RenderBone. Nothing is procedural.
    public sealed class MatchRokView
    {
        public const string ShaderName = "UBlade3/DromePreview";

        public readonly MatchRokData Data;

        public GameObject Root { get; private set; }

        public Mesh UnityMesh { get; private set; }

        public int ActiveBone { get; private set; }

        public int TrianglesDrawn { get; private set; }

        public Vector3 UnityPosition { get; private set; }

        public float Yaw { get; private set; }

        public bool IsBuilt { get; private set; }

        private MeshFilter meshFilter;

        private MeshRenderer meshRenderer;

        private readonly Dictionary<int, int[][]> boneTriangles = new Dictionary<int, int[][]>();

        private readonly Dictionary<string, Material> materialCache =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Texture2D> textureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private Material untexturedMaterial;

        private Texture2D whiteTexture;

        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public MatchRokView(MatchRokData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            Data = data;
            ActiveBone = -2;
        }

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
                    "Shader \"" + ShaderName + "\" was not found; the match ROK view uses the original "
                    + "N2 drome surface at Assets/Scripts/UBlade3/Native/DromePreview.shader.");
            }

            Root = new GameObject("MatchRok [" + Data.Object.Definition.Model + "]");
            Root.transform.SetParent(parent, false);

            meshFilter = Root.AddComponent<MeshFilter>();
            meshRenderer = Root.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            UnityMesh = Data.Mesh.CreateMesh(Data.Object.Definition.Model + " (rok)");
            UnityMesh.name = Data.Object.Definition.Model + " (rok)";
            meshFilter.sharedMesh = UnityMesh;

            Material[] materials = new Material[Data.Mesh.SubmeshCount];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = GetMaterial(shader, i);
            }

            meshRenderer.sharedMaterials = materials;

            IsBuilt = true;
            SetRenderBone(RokConstants.RenderBonePlain);
        }

        private Material GetMaterial(Shader shader, int submesh)
        {
            string textureName = Data.TextureNames[submesh];

            if (string.IsNullOrEmpty(textureName))
            {
                if (untexturedMaterial == null)
                {
                    untexturedMaterial = CreateMaterial(shader, "UBlade3 ROK untextured");
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

            string path = null;

            for (int i = 0; i < Data.TextureNames.Length; i++)
            {
                if (string.Equals(Data.TextureNames[i], textureName, StringComparison.OrdinalIgnoreCase))
                {
                    path = Data.TexturePaths[i];
                    break;
                }
            }

            if (path == null)
            {
                throw new OriginalAssetException("Texture \"" + textureName + "\" did not resolve for the ROK model.");
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
            owned.Add(texture);

            return texture;
        }

        private Texture2D GetWhiteTexture()
        {
            if (whiteTexture == null)
            {
                whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTexture.name = "UBlade3 ROK untextured white";
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply(false, false);
                owned.Add(whiteTexture);
            }

            return whiteTexture;
        }

        // rokobject.cpp SetRenderSlotFlags: only the selected bone's triangles are drawn; -1 hides
        // every bone (the original's "otherwise every bone hidden" answer).
        public void SetRenderBone(int bone)
        {
            if (!IsBuilt)
            {
                return;
            }

            ActiveBone = bone;

            int[][] perSubmesh;

            if (!boneTriangles.TryGetValue(bone, out perSubmesh))
            {
                perSubmesh = bone >= 0 && bone < Data.BoneCount
                    ? Data.TrianglesForBone(bone)
                    : new int[Data.Mesh.SubmeshCount][];

                if (perSubmesh.Length != Data.Mesh.SubmeshCount)
                {
                    perSubmesh = new int[Data.Mesh.SubmeshCount][];
                }

                for (int i = 0; i < perSubmesh.Length; i++)
                {
                    if (perSubmesh[i] == null)
                    {
                        perSubmesh[i] = new int[0];
                    }
                }

                boneTriangles[bone] = perSubmesh;
            }

            int drawn = 0;

            for (int submesh = 0; submesh < UnityMesh.subMeshCount; submesh++)
            {
                int[] indices = submesh < perSubmesh.Length ? perSubmesh[submesh] : new int[0];
                UnityMesh.SetTriangles(indices, submesh, false);
                drawn += indices.Length / 3;
            }

            TrianglesDrawn = drawn;
        }

        // Places and poses the ROK from the simulation's body state. The facing follows the body's
        // velocity direction while it has speed, then holds the last facing (RokState has no rotation
        // field; the engine's render-slot transform for a plain ROK is the static model header, so
        // this is the presentation stand-in documented in docs/NATIVE-GAMEPLAY.md).
        public void Tick(SimVec3 position, SimVec3 velocityDirection, float speed, int renderBone)
        {
            if (!IsBuilt)
            {
                return;
            }

            if (speed > 0.01f)
            {
                Yaw = MatchSkaterView.YawForDirection(velocityDirection);
            }

            HbMat4 objectTransform = Data.Rig.ObjectTransform(Yaw);
            HbMat4[] world = Data.Rig.BindPose(objectTransform);
            Data.Mesh.Pose(world);
            Data.Mesh.ApplyTo(UnityMesh);

            UnityPosition = MatchSkaterView.ToUnity(position);
            Root.transform.position = UnityPosition;

            SetRenderBone(renderBone);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_GAMEPLAY_ROK_VIEW position=(")
                .Append(MatchSimulation.F(UnityPosition.x)).Append(',')
                .Append(MatchSimulation.F(UnityPosition.y)).Append(',')
                .Append(MatchSimulation.F(UnityPosition.z)).Append(')');
            text.Append(" renderBone=").Append(ActiveBone);
            text.Append(" trianglesDrawn=").Append(TrianglesDrawn);
            text.Append(" yawRadians=").Append(MatchSimulation.F(Yaw));
            text.Append(" model=").Append(Data.Object.Definition.Model);

            return text.ToString();
        }

        public void Destroy()
        {
            if (Root != null)
            {
                SafeDestroy(Root);
                Root = null;
            }

            for (int i = 0; i < owned.Count; i++)
            {
                SafeDestroy(owned[i]);
            }

            owned.Clear();
            materialCache.Clear();
            textureCache.Clear();
            untexturedMaterial = null;
            whiteTexture = null;

            if (UnityMesh != null)
            {
                SafeDestroy(UnityMesh);
                UnityMesh = null;
            }

            IsBuilt = false;
        }

        private static void SafeDestroy(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
