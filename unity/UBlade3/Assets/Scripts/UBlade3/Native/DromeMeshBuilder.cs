using System;
using System.Collections.Generic;
using UBlade3.Assets;
using UBlade3.Data;
using UnityEngine;

namespace UBlade3.Native
{
    // Which of the two shipped render paths the frame reproduces.
    //   Shaded   - the software renderer the retail game actually ran (Data\Models\Mdlshd): a
    //              flat-shaded face walks the original palette ramps. This is the 1996 look.
    //   Textured - the Direct3D path's model set (Data\Models\Mdltxt) with its 24 bit BMP
    //              textures, lit as D3DRend::DrawFace lit it: MODULATE the texture by the light's
    //              grey level. Never a procedural substitute.
    public enum DromeShadingMode
    {
        Shaded = 0,
        Textured = 1
    }

    // MeshMaterial after EngineObject::InitModel converted its low byte: the software path keeps
    // the (remapped) palette index, the Direct3D path keeps 0xAARRGGBB. Both are needed because the
    // preview can draw either set.
    public sealed class DromeMaterialState
    {
        // MeshMaterial::nColour as the file stores it.
        public int ColourRaw;

        // The low channel after EngineObject::InitModel's optional team remap.
        public int PaletteIndex;

        // ColorPalette::GetRemappedEntry of PaletteIndex: the bright RGB the Direct3D material used.
        public OriginalRgb BrightRgb;

        public float Ambient;

        public float Diffuse;

        public float Emissive;

        // Negative means single sided (MeshMaterial::nBackIndex).
        public int BackIndex;

        // One based texture name resolved through WbmModel.GetTextureName; null when the material
        // names no texture (every Mdlshd material).
        public string TextureName;
    }

    // One model's materials as loaded by one object. EngineObject::InitModel converts a material
    // list only while its reference count is below MATERIAL_SHARED (2), so the first object that
    // loads a model's list fixes the remap for every later object using the same model.
    public sealed class DromeMaterialTable
    {
        public string ModelName;

        public OriginalModelSet ModelSet;

        public DromeMaterialState[] Materials;

        public bool RemapApplied;

        public int RemapSlotId;

        public bool OwnPaletteLoaded;

        public int OwnPaletteEntries;

        public int TextureReferences;
    }

    // A row-major 4x4 transform in HyperBlade model/world space. The engine keeps
    // TransformObject::m_a[4][4] row major with the translation in column three
    // (src/math/transformobject.h, multiply order in TransformObject::Multiply HYPERX 0x00427400:
    // result = this * rhs), so the same layout and product are used here.
    public sealed class HbMat4
    {
        public readonly float[] M = new float[16];

        public static HbMat4 Identity()
        {
            HbMat4 matrix = new HbMat4();
            matrix.M[0] = 1f;
            matrix.M[5] = 1f;
            matrix.M[10] = 1f;
            matrix.M[15] = 1f;

            return matrix;
        }

        public static HbMat4 Translation(float x, float y, float z)
        {
            HbMat4 matrix = Identity();
            matrix.M[3] = x;
            matrix.M[7] = y;
            matrix.M[11] = z;

            return matrix;
        }

        // TransformObject::ApplyRotationZ (HYPERX 0x00427a10), identity arm: row major, the matrix
        // that takes a column vector (x, y, z) to (x cos - y sin, x sin + y cos, z).
        public static HbMat4 RotationZ(float radians)
        {
            HbMat4 matrix = Identity();
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            matrix.M[0] = cos;
            matrix.M[1] = -sin;
            matrix.M[4] = sin;
            matrix.M[5] = cos;

            return matrix;
        }

        // TransformObject::RotateXStaged (HYPERX 0x00427940), identity arm.
        public static HbMat4 RotationX(float radians)
        {
            HbMat4 matrix = Identity();
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            matrix.M[5] = cos;
            matrix.M[6] = -sin;
            matrix.M[9] = sin;
            matrix.M[10] = cos;

            return matrix;
        }

        // TransformObject::RotateAroundY (HYPERX 0x00426e40), identity arm.
        public static HbMat4 RotationY(float radians)
        {
            HbMat4 matrix = Identity();
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            matrix.M[0] = cos;
            matrix.M[2] = sin;
            matrix.M[8] = -sin;
            matrix.M[10] = cos;

            return matrix;
        }

        public HbMat4 Multiply(HbMat4 rhs)
        {
            HbMat4 result = new HbMat4();

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float sum = 0f;

                    for (int k = 0; k < 4; k++)
                    {
                        sum += M[(row * 4) + k] * rhs.M[(k * 4) + column];
                    }

                    result.M[(row * 4) + column] = sum;
                }
            }

            return result;
        }

        public float Element(int row, int column)
        {
            return M[(row * 4) + column];
        }

        // EngineObject::UpdateRenderSlotTransform (HYPERX 0x00471630) writes m_transformC as
        // m_transformB * m_transformA and then adds PhysicsBody::pos to its translation column.
        // The static objects are never banked, so m_transformB is the identity here.
        public static HbMat4 PlaceObject(ObjectDefinition definition, WbmModel model)
        {
            HbMat4 rotation = HbMat4.Identity();
            AssetVector3 radians = model.Component.AngleRadians;

            // EngineObject::ApplyHeaderRotation (HYPERX 0x00472620) applies Z, then X, then Y, and
            // skips a zero angle (the identity product has the same result, so skipping is safe).
            if (radians.Z != 0f)
            {
                rotation = rotation.Multiply(RotationZ(radians.Z));
            }

            if (radians.X != 0f)
            {
                rotation = rotation.Multiply(RotationX(radians.X));
            }

            if (radians.Y != 0f)
            {
                rotation = rotation.Multiply(RotationY(radians.Y));
            }

            return Translation(definition.Position.x, definition.Position.y, definition.Position.z)
                .Multiply(rotation);
        }
    }

    // Builds one Unity mesh per original model object from the shipped WBM data. Nothing here is
    // procedural: every position, UV, material record and triangle comes from the file, and every
    // triangle is wound exactly as the 20XX hardware reference builder
    // (third_party/hyperblade-20XX/src/graphics/hwmesh.cpp HwMesh::Build, which mirrors
    // D3DRend::DrawFace HYPERX 0x004328c0) fans it:
    //   front: (0, n, n-1), (0, n-1, n-2), ... down to the third corner;
    //   back (only when the material names one): (0, i, i+1) for i = 1 .. n-2, with the normal
    //   negated and the back material's colour/lighting.
    public static class DromeMeshBuilder
    {
        // g_lightDirs[0].dir after Renderer::InitializeLightDirections (HYPERX 0x0047aa20):
        // Math::SphericalToCartesian({ halfPi, 1, 303 degrees }) = (0, -0.8386706, 0.5446390).
        // Normal gameplay frames use table entry zero (Renderer::DrawWholeScene ->
        // g_pScene->DrawShadowAndTransparentPass; MeshBone::DrawFaces HYPERX 0x0046f060).
        public static readonly Vector3 LightDirectionWorld = ComputeLightDirection();

        // Renderer::InitializeLightDirections (HYPERX 0x0047aa20) also builds the two other lights:
        // g_lightDirs[2] at azimuth 54 degrees and g_vLightDirExtra at azimuth 180. Renderer::
        // DrawAllMeshes (HYPERX 0x00442d20) relights the drome's first three bones with the extra,
        // the 303 and the 54 degree light and caches those per-face colours; the normal match frame
        // then draws the drome from that cache (MeshBone::DrawFaceList -> SWRend::DrawFace). The
        // drome's own bone light directions are therefore fixed, one per slot.
        public static readonly Vector3 LightDirectionExtra = ComputeLightDirection(180f);

        public static readonly Vector3 LightDirectionSecond = ComputeLightDirection(54f);

        // The per-slot lights DrawAllMeshes bakes into the drome, in bone order: bone 0 with the
        // 180 degree extra light, bone 1 with the 303 degree light, bone 2 with the 54 degree
        // light; any further bone keeps the first light (the restored g_lightDirs[0]).
        public static Vector3[] DromeSlotLightDirections(int slotCount)
        {
            Vector3[] lights = new Vector3[slotCount > 0 ? slotCount : 0];

            for (int i = 0; i < lights.Length; i++)
            {
                lights[i] = LightDirectionWorld;
            }

            if (slotCount > 0)
            {
                lights[0] = LightDirectionExtra;
            }

            if (slotCount > 1)
            {
                lights[1] = LightDirectionWorld;
            }

            if (slotCount > 2)
            {
                lights[2] = LightDirectionSecond;
            }

            return lights;
        }

        public sealed class Result
        {
            public Mesh Mesh;

            // "" means the sub-mesh is drawn untextured (the original shaded materials).
            public string[] SubmeshTextureNames;

            public int SourceVertices;

            public int SourceFaces;

            public int BuiltVertices;

            public int BuiltTriangles;

            public int TwoSidedFaces;

            public int SkippedFaces;

            public int SubmeshCount;

            public Vector3 BoundsMin;

            public Vector3 BoundsMax;

            public readonly List<string> Warnings = new List<string>();
        }

        // HyperBlade world space uses Z up (gravity integrates pos.z, physicsbody.cpp lines 738-746;
        // goals sit at y = +/-192.5 in HOUDRO.ENV). Unity is left handed with Y up. Mapping
        // (x, y, z) -> (x, z, -y) is a proper rotation (determinant +1), so triangle winding is
        // preserved and Unity's front-face convention still matches the original's front.
        public static Vector3 ToUnity(float x, float y, float z)
        {
            return new Vector3(x, z, -y);
        }

        public static Vector3 ToUnityDirection(float x, float y, float z)
        {
            return new Vector3(x, z, -y);
        }

        private static Vector3 ComputeLightDirection()
        {
            return ComputeLightDirection(303f);
        }

        private static Vector3 ComputeLightDirection(float azimuthDegrees)
        {
            // Renderer::InitializeLightDirections sets the spherical input from
            // g_gameApp.m_vLightSpherical = { g_flHalfPi, 1, g_flLightAzimuth * g_flDegToRad },
            // then Math::SphericalToCartesian (HYPERX 0x0047ab30) normalises.
            double halfPi = 1.5707964f;
            float azimuthRadians = azimuthDegrees * 0.017453292f;
            float x = (float)((Math.Cos(halfPi) * Math.Sin(azimuthRadians)) * 1.0f);
            float y = (float)((Math.Sin(halfPi) * Math.Sin(azimuthRadians)) * 1.0f);
            float z = (float)(Math.Cos(azimuthRadians) * 1.0f);

            double length = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));

            return new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
        }

        public static Result Build(
            WbmModel model,
            int lodIndex,
            HbMat4 objectTransform,
            DromeMaterialTable materials,
            OriginalPalette palette,
            DromeShadingMode mode,
            Vector3[] slotLightDirections)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            if (objectTransform == null)
            {
                throw new ArgumentNullException("objectTransform");
            }

            if (materials == null || materials.Materials == null)
            {
                throw new ArgumentNullException("materials");
            }

            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }

            Result result = new Result();

            if (model.Lods == null || model.Lods.Length == 0)
            {
                throw new OriginalAssetException(model.SourceName + " has no LOD group.");
            }

            int level = Mathf.Clamp(lodIndex, 0, model.Lods.Length - 1);
            ModelLod lod = model.Lods[level];

            int slotCount = model.Component.SubComponentCount;
            int[] parents = BuildParentMap(model, slotCount);
            HbMat4[] boneWorld = BuildBoneWorld(model, objectTransform, parents);

            // One vertex list for the whole object; each sub-mesh indexes into it. Corners are not
            // shared because shading is flat and each triangle's three corners share the face's
            // palette colour.
            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color32> colours = new List<Color32>();

            Dictionary<string, List<int>> submeshes =
                new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();

            DromeMaterialState[] materialStates = materials.Materials;

            for (int slot = 0; slot < slotCount; slot++)
            {
                MeshBlock block = lod.BySlot[slot];

                if (block == null)
                {
                    continue;
                }

                result.SourceVertices += block.VertexCount;
                result.SourceFaces += block.FaceCount;

                HbMat4 bone = boneWorld[slot];
                Vector3 lightWorld = slotLightDirections != null && slot < slotLightDirections.Length
                    ? slotLightDirections[slot]
                    : LightDirectionWorld;
                Vector3 lightLocal = LightInBoneSpace(bone, lightWorld);

                for (int faceIndex = 0; faceIndex < block.Faces.Length; faceIndex++)
                {
                    MeshFace face = block.Faces[faceIndex];

                    if (face == null || face.CornerCount < 3)
                    {
                        result.SkippedFaces++;
                        continue;
                    }

                    int frontMaterialIndex = face.MaterialIndex;

                    if (frontMaterialIndex >= materialStates.Length)
                    {
                        result.SkippedFaces++;
                        result.Warnings.Add(
                            model.SourceName + " slot " + slot + " face " + faceIndex
                            + ": material index " + frontMaterialIndex + " is outside the model's material list.");
                        continue;
                    }

                    DromeMaterialState front = materialStates[frontMaterialIndex];

                    // ModelDatabase::ComputeNormals (HYPERX 0x004734a0) overwrites every stored face
                    // normal when the model loads; the shipped files store zero here.
                    AssetVector3 faceNormal = ComputeFaceNormal(block, face);

                    AppendFace(
                        result,
                        block,
                        face,
                        faceNormal,
                        bone,
                        lightLocal,
                        front,
                        false,
                        palette,
                        mode,
                        positions,
                        normals,
                        uvs,
                        colours,
                        submeshes,
                        order);

                    if (front.BackIndex >= 0 && front.BackIndex < materialStates.Length)
                    {
                        DromeMaterialState back = materialStates[front.BackIndex];

                        AppendFace(
                            result,
                            block,
                            face,
                            faceNormal,
                            bone,
                            lightLocal,
                            back,
                            true,
                            palette,
                            mode,
                            positions,
                            normals,
                            uvs,
                            colours,
                            submeshes,
                            order);

                        result.TwoSidedFaces++;
                    }
                }
            }

            if (positions.Count == 0)
            {
                throw new OriginalAssetException(model.SourceName + " produced no triangles.");
            }

            Mesh mesh = new Mesh();
            mesh.name = model.Component.Name;

            if (positions.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);

            string[] submeshNames = new string[order.Count];
            mesh.subMeshCount = order.Count;

            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                List<int> indices = submeshes[key];
                mesh.SetTriangles(indices, i, true);
                submeshNames[i] = key;
                result.BuiltTriangles += indices.Count / 3;
            }

            mesh.RecalculateBounds();

            result.Mesh = mesh;
            result.SubmeshTextureNames = submeshNames;
            result.SubmeshCount = order.Count;
            result.BuiltVertices = positions.Count;
            result.BoundsMin = mesh.bounds.min;
            result.BoundsMax = mesh.bounds.max;

            return result;
        }

        public static Result Build(
            WbmModel model,
            int lodIndex,
            HbMat4 objectTransform,
            DromeMaterialTable materials,
            OriginalPalette palette,
            DromeShadingMode mode)
        {
            return Build(model, lodIndex, objectTransform, materials, palette, mode, null);
        }

        private static void AppendFace(
            Result result,
            MeshBlock block,
            MeshFace face,
            AssetVector3 faceNormal,
            HbMat4 bone,
            Vector3 lightLocal,
            DromeMaterialState material,
            bool reversed,
            OriginalPalette palette,
            DromeShadingMode mode,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color32> colours,
            Dictionary<string, List<int>> submeshes,
            List<string> order)
        {
            int cornerCount = face.Corners.Length;

            // D3DRend::DrawFace only textures a face that carries UVs and a texture.
            bool textured = mode == DromeShadingMode.Textured
                            && face.HasUV
                            && material.TextureName != null;

            string key = textured ? material.TextureName : string.Empty;

            List<int> indices;

            if (!submeshes.TryGetValue(key, out indices))
            {
                indices = new List<int>();
                submeshes[key] = indices;
                order.Add(key);
            }

            Color32 colour = ComputeColour(material, faceNormal, lightLocal, palette, mode, textured, reversed);

            int corner;
            int first;
            int second;
            int triangleCount = cornerCount - 2;

            if (reversed)
            {
                // The back copy is the front fan reversed (HwMesh::Build appends (0, i, i+1)),
                // lit with a negated normal and the back material.
                first = 1;
                second = 2;

                for (int t = 0; t < triangleCount; t++)
                {
                    corner = positions.Count;
                    AddCorner(0);
                    AddCorner(first);
                    AddCorner(second);
                    indices.Add(corner);
                    indices.Add(corner + 1);
                    indices.Add(corner + 2);
                    first++;
                    second++;
                }
            }
            else
            {
                first = cornerCount - 1;
                second = cornerCount - 2;

                for (int t = 0; t < triangleCount; t++)
                {
                    corner = positions.Count;
                    AddCorner(0);
                    AddCorner(first);
                    AddCorner(second);
                    indices.Add(corner);
                    indices.Add(corner + 1);
                    indices.Add(corner + 2);
                    first--;
                    second--;
                }
            }

            void AddCorner(int cornerIndex)
            {
                short vertexIndex = face.Corners[cornerIndex];
                AssetVector3 position = block.Vertices[vertexIndex];

                float x = (bone.M[0] * position.X) + (bone.M[1] * position.Y) + (bone.M[2] * position.Z) + bone.M[3];
                float y = (bone.M[4] * position.X) + (bone.M[5] * position.Y) + (bone.M[6] * position.Z) + bone.M[7];
                float z = (bone.M[8] * position.X) + (bone.M[9] * position.Y) + (bone.M[10] * position.Z) + bone.M[11];

                positions.Add(ToUnity(x, y, z));

                float nx = faceNormal.X;
                float ny = faceNormal.Y;
                float nz = faceNormal.Z;

                if (reversed)
                {
                    nx = -nx;
                    ny = -ny;
                    nz = -nz;
                }

                normals.Add(ToUnityDirection(nx, ny, nz));

                if (face.HasUV && cornerIndex < face.UVIndices.Length)
                {
                    int uvIndex = face.UVIndices[cornerIndex];

                    if (uvIndex >= 0 && uvIndex < block.Coords.Length)
                    {
                        AssetVector2 uv = block.Coords[uvIndex];
                        uvs.Add(new Vector2(uv.Tu, uv.Tv));
                        colours.Add(colour);

                        return;
                    }
                }

                uvs.Add(Vector2.zero);
                colours.Add(colour);
            }
        }

        // MeshMaterial::CalcLightIntensity (HYPERX 0x00433820) plus the two renderers' use of it.
        private static Color32 ComputeColour(
            DromeMaterialState material,
            AssetVector3 normal,
            Vector3 lightLocal,
            OriginalPalette palette,
            DromeShadingMode mode,
            bool texturedFace,
            bool reversed)
        {
            float nx = normal.X;
            float ny = normal.Y;
            float nz = normal.Z;

            if (reversed)
            {
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }

            // MeshBone::DrawFaces (HYPERX 0x0046f060) sets g_vLightDirModel with
            // TransformObject::TransformDirectionByRows (HYPERX 0x00406e40): the transpose of the
            // bone's world rotation multiplies the world light direction.
            float dot = (lightLocal.x * nx) + (lightLocal.y * ny) + (lightLocal.z * nz);
            float level = (dot * material.Diffuse) + material.Ambient + material.Emissive;

            if (mode == DromeShadingMode.Shaded)
            {
                // SWRend::DrawFace's software path: a colour index inside m_rampBounds is shaded
                // through the palette ramps; outside them the palette entry is the material's own
                // index. The displayed RGB is the palette entry the lookup answers.
                OriginalShade shade = level > 0f
                    ? (level < 1f ? OriginalShade.Scaled : OriginalShade.White)
                    : OriginalShade.Black;

                int colourIndex = material.PaletteIndex;
                int index = palette.IsInRamp(colourIndex)
                    ? palette.LookupColorIntensity(shade, level, colourIndex)
                    : colourIndex;

                OriginalRgb rgb = palette.Entry(index);

                return new Color32(rgb.Red, rgb.Green, rgb.Blue, 255);
            }

            if (texturedFace)
            {
                // D3DRend::DrawFace's textured branch: white at or above one, the level in 255ths
                // (truncated) between, black at or below zero; the texture is MODULATEd by it.
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

        // ModelDatabase::ComputeNormals (HYPERX 0x004734a0): the cross product of the second edge
        // with the first (e2 x e1, where e1 = corner1 - corner0 and e2 = corner2 - corner0), then
        // Math::Vec3Normalize (HYPERX 0x00430110). Every shipped WBM stores a zero face normal;
        // this load time computation is the only normal either shipped renderer lights with.
        public static AssetVector3 ComputeFaceNormal(MeshBlock block, MeshFace face)
        {
            AssetVector3 v0 = block.Vertices[face.Corners[0]];
            AssetVector3 v1 = block.Vertices[face.Corners[1]];
            AssetVector3 v2 = block.Vertices[face.Corners[2]];

            float e1x = v1.X - v0.X;
            float e1y = v1.Y - v0.Y;
            float e1z = v1.Z - v0.Z;

            float e2x = v2.X - v0.X;
            float e2y = v2.Y - v0.Y;
            float e2z = v2.Z - v0.Z;

            float x = (e2z * e1y) - (e2y * e1z);
            float y = (e2x * e1z) - (e2z * e1x);
            float z = (e2y * e1x) - (e2x * e1y);

            // g_dNormalizeEpsilon = 1e-07 (src/globals.cpp); at or below it the original writes zero.
            double length = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));

            if (length > 1e-07)
            {
                return new AssetVector3((float)(x / length), (float)(y / length), (float)(z / length));
            }

            return new AssetVector3(0f, 0f, 0f);
        }

        private static int[] BuildParentMap(WbmModel model, int slotCount)
        {
            int[] parents = new int[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                parents[i] = -1;
            }

            MeshBoneDesc[] descriptors = model.Component.SlotDescriptors;

            for (int i = 0; i < descriptors.Length; i++)
            {
                MeshBoneDesc descriptor = descriptors[i];

                for (int c = 0; c < descriptor.ChildIds.Length; c++)
                {
                    int child = descriptor.ChildIds[c];

                    if (child < 0 || child >= slotCount)
                    {
                        throw new OriginalAssetException(
                            model.SourceName + " slot " + i + " names child slot " + child
                            + " outside the model's " + slotCount + " slots.");
                    }

                    if (child != i)
                    {
                        parents[child] = i;
                    }
                }
            }

            return parents;
        }

        // MeshBone::PropagateTransforms (HYPERX 0x0046ef10): world = parentWorld * local, and the
        // local transform of an unanimated bone is its rest translation
        // (MeshBone::BindToLODMesh HYPERX 0x0046fba0 stores m_vRest with SetTranslation). The
        // engine starts the walk at slot zero, so a root elsewhere is data this renderer does not
        // silently invent a pose for.
        private static HbMat4[] BuildBoneWorld(WbmModel model, HbMat4 objectTransform, int[] parents)
        {
            int slotCount = parents.Length;
            HbMat4[] world = new HbMat4[slotCount];
            bool[] solved = new bool[slotCount];

            MeshBoneDesc[] descriptors = model.Component.SlotDescriptors;

            int rootCount = 0;
            int rootSlot = -1;

            for (int i = 0; i < slotCount; i++)
            {
                if (parents[i] < 0)
                {
                    rootCount++;
                    rootSlot = i;
                }
            }

            if (rootCount != 1)
            {
                throw new OriginalAssetException(
                    model.SourceName + " has " + rootCount + " bone roots; the shipped rigs have exactly one.");
            }

            if (rootSlot != 0)
            {
                throw new OriginalAssetException(
                    model.SourceName + " has its bone root at slot " + rootSlot
                    + "; the engine poses from slot zero (EngineObject::UpdateRenderSlotTransform).");
            }

            int remaining = slotCount;

            for (int pass = 0; pass < slotCount; pass++)
            {
                bool progressed = false;

                for (int slot = 0; slot < slotCount; slot++)
                {
                    if (solved[slot])
                    {
                        continue;
                    }

                    int parent = parents[slot];

                    if (parent >= 0 && !solved[parent])
                    {
                        continue;
                    }

                    MeshBoneDesc descriptor = descriptors[slot];
                    HbMat4 local = HbMat4.Translation(descriptor.Rest.X, descriptor.Rest.Y, descriptor.Rest.Z);

                    world[slot] = parent < 0 ? objectTransform.Multiply(local) : world[parent].Multiply(local);
                    solved[slot] = true;
                    progressed = true;
                    remaining--;
                }

                if (remaining == 0)
                {
                    return world;
                }

                if (!progressed)
                {
                    throw new OriginalAssetException(
                        model.SourceName + " bone hierarchy has a cycle; the engine would recurse forever.");
                }
            }

            return world;
        }

        private static Vector3 LightInBoneSpace(HbMat4 bone, Vector3 light)
        {
            // TransformObject::TransformDirectionByRows (HYPERX 0x00406e40): the transpose of the
            // rotation multiplies the world direction, i.e. output.x = M[2][0]*z + M[1][0]*y + M[0][0]*x.
            float x = (bone.M[8] * light.z) + (bone.M[4] * light.y) + (bone.M[0] * light.x);
            float y = (bone.M[9] * light.z) + (bone.M[5] * light.y) + (bone.M[1] * light.x);
            float z = (bone.M[10] * light.z) + (bone.M[6] * light.y) + (bone.M[2] * light.x);

            return new Vector3(x, y, z);
        }
    }
}
