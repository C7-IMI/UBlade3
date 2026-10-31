using System;
using System.Collections.Generic;
using UBlade3.Assets;
using UnityEngine;

namespace UBlade3.Native
{
    // The skater mesh: one Unity mesh per original WBM model, posed on the CPU from the rig's bone
    // world matrices each sampled frame. Nothing is procedural. Every position, UV, face, material
    // and texture name comes from the player's own .wbm/.bmp files.
    //
    // Why CPU posing instead of a Unity SkinnedMeshRenderer: the shipped rig is not a skinning rig
    // in Unity's sense. MeshBone::PropagateTransforms (HYPERX 0x0046ef10) gives each component slot
    // its own solid transform and MeshBone::DrawFaces (HYPERX 0x0046f060) transforms that slot's
    // vertices through it; there are no weights and no shared vertices between slots. Building the
    // same thing here means one base position per corner in its bone's local space, and a matrix
    // multiply per corner per frame - exactly what the software renderer did, with the exact same
    // matrices SkaterRig reconstructs. A future release can upload those 16 matrices as bones once
    // the material path is settled; the geometry and UVs will not change.
    //
    // Shading follows the Direct3D path (the Mdltxt set this preview draws), D3DRend::DrawFace
    // (HYPERX 0x004328c0):
    //   * textured face: vertex colour is the light's grey level; the texture is MODULATEd by it;
    //   * untextured face: the material's converted 0xAARRGGBB scaled by the light level.
    // The light is g_lightDirs[0], the one MeshBone::DrawFaces uses for ordinary gameplay frames
    // (DromeMeshBuilder.LightDirectionWorld, the 303 degree spherical light).
    public sealed class SkaterMeshData
    {
        public SkaterRig Rig;

        public WbmModel Model;

        public DromeMaterialState[] Materials;

        public int LodIndex;

        // Sub-mesh texture names, "" for an untextured group. The Unity mesh's sub-mesh i draws
        // SubmeshTextureNames[i] with the material table above.
        public string[] SubmeshTextureNames;

        public int[] SubmeshFirstCorner;

        public int[] SubmeshCornerCount;

        public int SourceVertices;

        public int SourceFaces;

        public int TwoSidedFaces;

        public int SkippedFaces;

        public int TriangleCount;

        public int BuiltVertices
        {
            get { return TriangleCount * 3; }
        }

        // The Unity mesh's vertex count: one flat-shaded corner per triangle vertex.
        public int UnityVertexCount
        {
            get { return Positions == null ? 0 : Positions.Length; }
        }

        public int SubmeshCount
        {
            get { return SubmeshTextureNames.Length; }
        }

        public Vector3 RestBoundsMin;

        public Vector3 RestBoundsMax;

        public Vector3 BoundsMin;

        public Vector3 BoundsMax;

        public readonly List<string> Warnings = new List<string>();

        // One corner (a triangle vertex). Faces are fanned and flat shaded, so corners are never
        // shared and a corner names its own bone.
        internal Vector3[] RestPositions;

        internal Vector2[] Uvs;

        internal Vector3[] LocalNormals;

        internal int[] BoneIndices;

        internal int[] MaterialIndices;

        internal bool[] Reversed;

        // True where the face carries UVs and its material names a texture: the shipped Direct3D
        // path MODULATEs those faces and draws the others from the material colour.
        internal bool[] Textured;

        internal Vector3[] Positions;

        internal Vector3[] Normals;

        internal Color32[] Colours;

        // The original's model units use Z up; the mesh is converted once through the same proper
        // rotation (x, y, z) -> (x, z, -y) the N2 drome preview uses, so winding is preserved.
        public static Vector3 ToUnity(float x, float y, float z)
        {
            return DromeMeshBuilder.ToUnity(x, y, z);
        }

        public Mesh CreateMesh(string name)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.MarkDynamic();

            if (BuiltVertices > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(new List<Vector3>(Positions));
            mesh.SetNormals(new List<Vector3>(Normals));
            mesh.SetUVs(0, new List<Vector2>(Uvs));
            mesh.SetColors(new List<Color32>(Colours));

            mesh.subMeshCount = SubmeshCount;

            for (int i = 0; i < SubmeshCount; i++)
            {
                int first = SubmeshFirstCorner[i];
                int count = SubmeshCornerCount[i];
                int[] indices = new int[count];

                for (int k = 0; k < count; k++)
                {
                    indices[k] = first + k;
                }

                mesh.SetTriangles(indices, i, false);
            }

            mesh.RecalculateBounds();

            return mesh;
        }

        // AnimPlayer::UpdateFrame followed by MeshBone::DrawFaces: every corner through its bone's
        // world matrix, every face's normal through the same rotation, then the shipped light.
        public void Pose(HbMat4[] boneWorld)
        {
            if (boneWorld == null)
            {
                throw new ArgumentNullException("boneWorld");
            }

            if (boneWorld.Length != Rig.BoneCount)
            {
                throw new OriginalAssetException(
                    "Pose got " + boneWorld.Length + " bone matrices for " + Rig.BoneCount + " bones.");
            }

            Vector3 light = DromeMeshBuilder.LightDirectionWorld;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;

            int corners = TriangleCount * 3;

            for (int triangle = 0; triangle < TriangleCount; triangle++)
            {
                int corner = triangle * 3;
                int bone = BoneIndices[corner];
                HbMat4 matrix = boneWorld[bone];

                // ModelDatabase::ComputeNormals (HYPERX 0x004734a0) stores the face normal in the
                // bone's local space; the world normal is the bone's rotation applied to it. A two
                // sided face's back copy negates it (the 20XX HwMesh::Build reference).
                AssetVector3 local = new AssetVector3(
                    LocalNormals[corner].x, LocalNormals[corner].y, LocalNormals[corner].z);

                float nx;
                float ny;
                float nz;
                SkaterRig.TransformDirection(matrix, local, out nx, out ny, out nz);

                if (Reversed[corner])
                {
                    nx = -nx;
                    ny = -ny;
                    nz = -nz;
                }

                DromeMaterialState material = Materials[MaterialIndices[corner]];
                Color32 colour = Shade(material, Textured[corner], nx, ny, nz, light);

                for (int k = 0; k < 3; k++)
                {
                    int at = corner + k;
                    AssetVector3 rest = new AssetVector3(
                        RestPositions[at].x, RestPositions[at].y, RestPositions[at].z);

                    float x;
                    float y;
                    float z;
                    SkaterRig.TransformPoint(matrix, rest, out x, out y, out z);

                    Vector3 unity = ToUnity(x, y, z);

                    Positions[at] = unity;
                    Normals[at] = ToUnity(nx, ny, nz);
                    Colours[at] = colour;

                    if (unity.x < minX) { minX = unity.x; }
                    if (unity.y < minY) { minY = unity.y; }
                    if (unity.z < minZ) { minZ = unity.z; }
                    if (unity.x > maxX) { maxX = unity.x; }
                    if (unity.y > maxY) { maxY = unity.y; }
                    if (unity.z > maxZ) { maxZ = unity.z; }
                }
            }

            BoundsMin = new Vector3(minX, minY, minZ);
            BoundsMax = new Vector3(maxX, maxY, maxZ);
        }

        public void ApplyTo(Mesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException("mesh");
            }

            mesh.SetVertices(new List<Vector3>(Positions));
            mesh.SetNormals(new List<Vector3>(Normals));
            mesh.SetColors(new List<Color32>(Colours));
            mesh.RecalculateBounds();
        }

        // MeshMaterial::CalcLightIntensity (HYPERX 0x00433820) and D3DRend::DrawFace's two modes.
        private static Color32 Shade(DromeMaterialState material, bool textured, float nx, float ny, float nz, Vector3 light)
        {
            float dot = (light.x * nx) + (light.y * ny) + (light.z * nz);
            float level = (dot * material.Diffuse) + material.Ambient + material.Emissive;

            if (textured)
            {
                // D3DRend::DrawFace textured branch: white at or above one, the level in 255ths
                // (truncated) between, black at or below zero. The texture is MODULATEd by it.
                int grey = 255;

                if (level <= 0f)
                {
                    grey = 0;
                }
                else if (level < 1f)
                {
                    grey = (int)(255.0f * level);
                }

                byte lit = (byte)Mathf.Clamp(grey, 0, 255);

                return new Color32(lit, lit, lit, 255);
            }

            // D3DRend::DrawFace's untextured branch: the material's converted 0xAARRGGBB scaled by
            // the light level per channel (integer truncation), black when unlit.
            OriginalRgb bright = material.BrightRgb;

            if (level <= 0f)
            {
                return new Color32(0, 0, 0, 255);
            }

            if (level < 1f)
            {
                return new Color32(
                    (byte)Mathf.Clamp((int)(bright.Red * level), 0, 255),
                    (byte)Mathf.Clamp((int)(bright.Green * level), 0, 255),
                    (byte)Mathf.Clamp((int)(bright.Blue * level), 0, 255),
                    255);
            }

            return new Color32(bright.Red, bright.Green, bright.Blue, 255);
        }
    }

    // Builds SkaterMeshData from one loaded WBM. The face fan and two sided rules are the same ones
    // the N2 drome builder implements from HwMesh::Build (third_party/hyperblade-20XX/src/graphics/
    // hwmesh.cpp) and D3DRend::DrawFace: front (0, n, n-1), (0, n-1, n-2), ...; a material with a
    // non negative MeshMaterial.BackIndex adds the reversed fan with the back material.
    public static class SkaterMeshBuilder
    {
        // EngineObject::InitModel (HYPERX 0x00470790) converts a model's material list once, with
        // bRemap = (m_nSlot & 1) | m_bForceRemap. A team's first Dromer on the home side has object
        // id 0 (LoadTeamFromFile starts nRunningId at the team index and steps by two per Dromer),
        // and its goalie id 4, so the home skater and goalie this preview draws are the unremapped
        // palette rows. A preview of an away slot would pass true.
        public static SkaterMeshData Build(
            OriginalAssetRoot root,
            WbmModel model,
            SkaterRig rig,
            OriginalPalette palette,
            int lodIndex,
            OriginalModelSet modelSet,
            bool remap)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            if (rig == null)
            {
                throw new ArgumentNullException("rig");
            }

            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }

            if (model.Lods == null || model.Lods.Length == 0)
            {
                throw new OriginalAssetException(model.SourceName + " has no LOD group.");
            }

            int level = Mathf.Clamp(lodIndex, 0, model.Lods.Length - 1);
            ModelLod lod = model.Lods[level];

            SkaterMeshData data = new SkaterMeshData();
            data.Rig = rig;
            data.Model = model;
            data.LodIndex = level;

            DromeMaterialState[] materials = BuildMaterials(model, palette, remap, modelSet, data.Warnings);
            data.Materials = materials;

            List<Vector3> restPositions = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<int> bones = new List<int>();
            List<int> materialIndices = new List<int>();
            List<bool> reversed = new List<bool>();
            List<bool> textured = new List<bool>();

            Dictionary<string, List<int>> submeshes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();

            int slotCount = model.Component.SubComponentCount;

            for (int slot = 0; slot < slotCount; slot++)
            {
                MeshBlock block = lod.BySlot[slot];

                if (block == null)
                {
                    continue;
                }

                data.SourceVertices += block.VertexCount;
                data.SourceFaces += block.FaceCount;

                for (int faceIndex = 0; faceIndex < block.Faces.Length; faceIndex++)
                {
                    MeshFace face = block.Faces[faceIndex];

                    if (face == null || face.CornerCount < 3)
                    {
                        data.SkippedFaces++;
                        continue;
                    }

                    int frontMaterialIndex = face.MaterialIndex;

                    if (frontMaterialIndex < 0 || frontMaterialIndex >= materials.Length)
                    {
                        data.SkippedFaces++;
                        data.Warnings.Add(
                            model.SourceName + " slot " + slot + " face " + faceIndex
                            + ": material index " + frontMaterialIndex + " is outside the model's material list.");
                        continue;
                    }

                    DromeMaterialState front = materials[frontMaterialIndex];
                    AssetVector3 faceNormal = DromeMeshBuilder.ComputeFaceNormal(block, face);

                    AppendFace(
                        data, block, face, faceNormal, slot, frontMaterialIndex, false,
                        uvs, restPositions, normals, bones, materialIndices, reversed, textured, submeshes, order);

                    if (front.BackIndex >= 0 && front.BackIndex < materials.Length)
                    {
                        AppendFace(
                            data, block, face, faceNormal, slot, front.BackIndex, true,
                            uvs, restPositions, normals, bones, materialIndices, reversed, textured, submeshes, order);

                        data.TwoSidedFaces++;
                    }
                }
            }

            if (restPositions.Count == 0)
            {
                throw new OriginalAssetException(model.SourceName + " produced no triangles.");
            }

            // Flatten the per-sub-mesh corner lists so each sub-mesh owns a contiguous triangle run.
            int cornerCount = restPositions.Count;
            data.RestPositions = new Vector3[cornerCount];
            data.Uvs = new Vector2[cornerCount];
            data.LocalNormals = new Vector3[cornerCount];
            data.BoneIndices = new int[cornerCount];
            data.MaterialIndices = new int[cornerCount];
            data.Reversed = new bool[cornerCount];
            data.Textured = new bool[cornerCount];
            data.Positions = new Vector3[cornerCount];
            data.Normals = new Vector3[cornerCount];
            data.Colours = new Color32[cornerCount];

            data.SubmeshTextureNames = new string[order.Count];
            data.SubmeshFirstCorner = new int[order.Count];
            data.SubmeshCornerCount = new int[order.Count];

            int at = 0;

            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                List<int> corners = submeshes[key];

                data.SubmeshTextureNames[i] = key;
                data.SubmeshFirstCorner[i] = at;
                data.SubmeshCornerCount[i] = corners.Count;

                for (int k = 0; k < corners.Count; k++)
                {
                    int source = corners[k];
                    data.RestPositions[at] = restPositions[source];
                    data.Uvs[at] = uvs[source];
                    data.LocalNormals[at] = normals[source];
                    data.BoneIndices[at] = bones[source];
                    data.MaterialIndices[at] = materialIndices[source];
                    data.Reversed[at] = reversed[source];
                    data.Textured[at] = textured[source];
                    at++;
                }
            }

            data.TriangleCount = cornerCount / 3;

            // The unanimated bind pose (MeshBone::BindToLODMesh: every local matrix is the rest
            // translation) for the probe and the preview's first framing.
            data.Pose(rig.BindPose());
            data.RestBoundsMin = data.BoundsMin;
            data.RestBoundsMax = data.BoundsMax;

            return data;
        }

        // EngineObject::InitModel's material conversion for the Direct3D path: the low palette byte
        // through the team remap range becomes the bright RGB the D3D material used, and a one
        // based MeshMaterial.TextureIndex resolves to the model's texture name table entry.
        public static DromeMaterialState[] BuildMaterials(
            WbmModel model,
            OriginalPalette palette,
            bool remap,
            OriginalModelSet modelSet,
            List<string> warnings)
        {
            MeshMaterial[] source = model.Materials;
            DromeMaterialState[] states = new DromeMaterialState[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                MeshMaterial material = source[i];
                DromeMaterialState state = new DromeMaterialState();
                state.ColourRaw = material.Colour;
                state.PaletteIndex = palette.ApplyMaterialRemap(material.Colour, remap);
                state.BrightRgb = palette.GetRemappedEntry(state.PaletteIndex);
                state.Ambient = material.Ambient;
                state.Diffuse = material.Diffuse;
                state.Emissive = material.Emissive;
                state.BackIndex = material.BackIndex;
                state.TextureName = model.GetTextureName(material);
                states[i] = state;

                if (state.TextureName != null && state.TextureName.Length == 0)
                {
                    warnings.Add(
                        model.SourceName + " material " + i
                        + " references an empty texture name; ModelDatabase::CreateMaterialList would fail on it.");
                }

                if (material.TextureIndex < 0 || material.TextureIndex > model.TextureNames.Length)
                {
                    warnings.Add(
                        model.SourceName + " material " + i + " texture index " + material.TextureIndex
                        + " is outside the model's " + model.TextureNames.Length + " texture names.");
                }
            }

            return states;
        }

        private static void AppendFace(
            SkaterMeshData data,
            MeshBlock block,
            MeshFace face,
            AssetVector3 faceNormal,
            int slot,
            int materialIndex,
            bool reverse,
            List<Vector2> uvs,
            List<Vector3> restPositions,
            List<Vector3> normals,
            List<int> bones,
            List<int> materialIndices,
            List<bool> reversed,
            List<bool> textured,
            Dictionary<string, List<int>> submeshes,
            List<string> order)
        {
            int cornerCount = face.Corners.Length;

            // D3DRend::DrawFace only textures a face that carries UVs and a texture.
            string key = face.HasUV && materialIndex >= 0 && materialIndex < data.Materials.Length
                         && data.Materials[materialIndex].TextureName != null
                ? data.Materials[materialIndex].TextureName
                : string.Empty;

            if (key.Length == 0)
            {
                key = string.Empty;
            }

            bool isTextured = key.Length != 0;

            List<int> indices;

            if (!submeshes.TryGetValue(key, out indices))
            {
                indices = new List<int>();
                submeshes[key] = indices;
                order.Add(key);
            }

            int triangleCount = cornerCount - 2;
            int first;
            int second;

            if (reverse)
            {
                first = 1;
                second = 2;
            }
            else
            {
                first = cornerCount - 1;
                second = cornerCount - 2;
            }

            for (int t = 0; t < triangleCount; t++)
            {
                int corner = restPositions.Count;

                AddCorner(0);
                AddCorner(first);
                AddCorner(second);

                indices.Add(corner);
                indices.Add(corner + 1);
                indices.Add(corner + 2);

                if (reverse)
                {
                    first++;
                    second++;
                }
                else
                {
                    first--;
                    second--;
                }
            }

            void AddCorner(int cornerIndex)
            {
                short vertexIndex = face.Corners[cornerIndex];
                AssetVector3 position = block.Vertices[vertexIndex];

                // Both are kept in the original model space (Z up) because SkaterMeshData.Pose
                // multiplies them by the model space bone matrices; only the posed result is
                // converted to Unity space.
                restPositions.Add(new Vector3(position.X, position.Y, position.Z));
                normals.Add(new Vector3(faceNormal.X, faceNormal.Y, faceNormal.Z));
                bones.Add(slot);
                materialIndices.Add(materialIndex);
                reversed.Add(reverse);
                textured.Add(isTextured);

                if (face.HasUV && cornerIndex < face.UVIndices.Length)
                {
                    int uvIndex = face.UVIndices[cornerIndex];

                    if (uvIndex >= 0 && uvIndex < block.Coords.Length)
                    {
                        AssetVector2 uv = block.Coords[uvIndex];
                        uvs.Add(new Vector2(uv.Tu, uv.Tv));

                        return;
                    }
                }

                uvs.Add(Vector2.zero);
            }
        }
    }
}
