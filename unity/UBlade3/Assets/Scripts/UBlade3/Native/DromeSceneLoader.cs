using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UBlade3.Data;
using UnityEngine;

namespace UBlade3.Native
{
    // One ObjectDefinition with its loaded original model, the material table EngineObject::InitModel
    // would have converted for it, its placement matrix and its Unity mesh.
    public sealed class DromeObjectData
    {
        public ObjectDefinition Definition;

        // The engine slot id Cmd_DefineObject assigned (m_abTypeHighWater[nType]++), which decides
        // the team palette remap bit (slot & 1).
        public int ModelId;

        public WbmModel Model;

        public OriginalModelSet ModelSet;

        public string ModelPath;

        public DromeMaterialTable Materials;

        public HbMat4 Placement;

        public DromeMeshBuilder.Result Built;

        public Vector3 BoundsMin;

        public Vector3 BoundsMax;
    }

    // Everything the N2 preview needs from one original drome: the .ENV objects, the WBM models,
    // the material palette state and the triangle meshes. No scene objects are created here, so the
    // same data drives the Editor capture, the probe and (later) the playable renderer.
    public sealed class DromeSceneData
    {
        public OriginalAssetRoot Root;

        public DromeShadingMode Mode;

        public string DromeFileName;

        public string DromeSourcePath;

        public DromeFile Drome;

        public OriginalPalette Palette;

        // Objects whose modfile is "null": the engine creates them with ENGINE_OBJECT_NO_MODEL and
        // no MeshBone slots, so they have no mesh. The shipped CHIDRO.ENV has four
        // (inscrut, Rob, Coach0, Coach1).
        public readonly List<ObjectDefinition> ObjectsWithoutModel = new List<ObjectDefinition>();

        public readonly List<DromeObjectData> Objects = new List<DromeObjectData>();

        public readonly List<string> Warnings = new List<string>();

        public readonly List<string> MissingModels = new List<string>();

        public readonly List<string> MissingTextures = new List<string>();

        // distinct texture name -> absolute .bmp path (textured mode only)
        public readonly Dictionary<string, string> TextureFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> TextureNames = new List<string>();

        public int TextureReferences;

        public int MeshCount;

        public int SourceVertices;

        public int SourceFaces;

        public int BuiltVertices;

        public int BuiltTriangles;

        public int SubmeshCount;

        public int TwoSidedFaces;

        // Bounds in Unity space (Y up), for the preview camera.
        public Vector3 BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

        public Vector3 BoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        public bool HasDromeBounds;

        public Vector3 DromeBoundsMin;

        public Vector3 DromeBoundsMax;

        public string Digest;

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_DROME_PREVIEW_DATA root=").Append(Root.DataPath);
            text.Append(" env=").Append(DromeFileName).Append('(').Append(DromeSourcePath).Append(')');
            text.Append(" mode=").Append(Mode == DromeShadingMode.Shaded ? "shaded" : "textured");
            text.AppendLine();
            text.Append("UBLADE3_DROME_PREVIEW_PALETTE source=").Append(Palette.CustomPalettePath);
            text.Append(" gif=").Append(Palette.GifPath);
            text.Append(" entries=").Append(Palette.LoadedEntryCount);
            text.Append(" wbpFiles=").Append(Palette.WbpFilesApplied);
            text.Append(" wbpEntries=").Append(Palette.WbpEntriesApplied);
            text.Append(" ramp=").Append(Palette.RampFirst).Append("..").Append(Palette.RampLast);
            text.Append(" smallRamp=").Append(Palette.SmallRampFirst).Append("..").Append(Palette.SmallRampLast);
            text.Append(" largeRamp=").Append(Palette.LargeRampFirst).Append("..").Append(Palette.LargeRampLast);
            text.Append(" remap=").Append(Palette.RemapFirst).Append("..").Append(Palette.RemapLast);
            text.AppendLine();
            text.Append("UBLADE3_DROME_PREVIEW_SCENE objects=").Append(Drome.Objects.Count);
            text.Append(" meshes=").Append(MeshCount);
            text.Append(" sourceVerts=").Append(SourceVertices);
            text.Append(" sourceFaces=").Append(SourceFaces);
            text.Append(" builtVerts=").Append(BuiltVertices);
            text.Append(" triangles=").Append(BuiltTriangles);
            text.Append(" submeshes=").Append(SubmeshCount);
            text.Append(" twoSidedFaces=").Append(TwoSidedFaces);
            text.Append(" textures=").Append(TextureFiles.Count).Append('/').Append(TextureReferences);
            text.AppendLine();
            text.Append("UBLADE3_DROME_PREVIEW_DIGEST ").Append(Digest);
            text.AppendLine();

            foreach (DromeObjectData item in Objects)
            {
                text.Append("UBLADE3_DROME_PREVIEW_OBJECT name=\"").Append(item.Definition.Name).Append('"');
                text.Append(" type=").Append(item.Definition.TypeName);
                text.Append(" model=").Append(item.Definition.Model);
                text.Append(" set=").Append(item.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
                text.Append(" slot=").Append(item.ModelId.ToString(CultureInfo.InvariantCulture));
                text.Append(" pos=").Append(F(item.Definition.Position.x)).Append(',')
                    .Append(F(item.Definition.Position.y)).Append(',').Append(F(item.Definition.Position.z));
                text.Append(" lods=").Append(item.Model.LodCount);
                text.Append(" lod0Blocks=").Append(item.Model.Lods[0].Blocks.Length);
                text.Append(" sourceVerts=").Append(item.Built.SourceVertices);
                text.Append(" sourceFaces=").Append(item.Built.SourceFaces);
                text.Append(" verts=").Append(item.Built.BuiltVertices);
                text.Append(" tris=").Append(item.Built.BuiltTriangles);
                text.Append(" submeshes=").Append(item.Built.SubmeshCount);
                text.AppendLine();
            }

            foreach (string warning in Warnings)
            {
                text.Append("UBLADE3_DROME_PREVIEW_WARNING ").AppendLine(warning);
            }

            foreach (string missing in MissingModels)
            {
                text.Append("UBLADE3_DROME_PREVIEW_MISSING_MODEL ").AppendLine(missing);
            }

            foreach (string missing in MissingTextures)
            {
                text.Append("UBLADE3_DROME_PREVIEW_MISSING_TEXTURE ").AppendLine(missing);
            }

            return text.ToString().TrimEnd();
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // Loads one shipped .ENV drome exactly as GameEngine::LoadArena (HYPERX 0x00484e80) does and
    // builds its original meshes. Everything comes from the player's install:
    //   - object definitions and placement from Data\<drome>.ENV (verified UBlade3.Data parser);
    //   - models from Data\Models\Mdlshd (fallback MdlTxt), by the object's modfile name;
    //   - palettes from Data\CUSTOM.PAL + the GIF it names, and the drome's own .wbp;
    //   - textures from Data\Textures by the model's one based texture name table (textured mode).
    public static class DromeSceneLoader
    {
        // Cmd_DefineObject (HYPERX 0x00482390) starts every type's model id at
        // g_abObjectTypeBaseline[nType] (engine/gameengine.cpp lines 5590-5591) and increments the
        // per-type high-water mark, so an object's slot id is file order within its type.
        private static readonly int[] TypeBaseline =
        {
            // id_GOB order: Null, Dromer, Rok, Drome, Ramp, NGoal, SGoal, MainCamera, HeadCam,
            // StartGun, Turnstile, Multiplier, LaserHurdle, GreenRandomizer, RedRandomizer, God,
            // SoundMan, LimboPole, Hurdle, FastLane, Explosion, Armory, Keyboard, Gamepad,
            // Joystick, Mouse, SlowLane, Mine, Swarf, Coach, BodyPart, Generic.
            102, 0, 6, 10, 12, 7, 8, 9, 25, 26, 33, 27, 46, 72, 80, 23, 24, 40, 66, 56, 60, 52, 86, 87, 89, 91, 58, 54, 55, 92, 94, 102
        };

        // Expected shipped CHIDRO.ENV inventory (docs/DATA-FORMATS.md section 5.2: 29 objects,
        // 2 ramps, 4 turnstiles, 4 green randomizers; 4 objects with modfile=null).
        public const string ExpectedDromeFile = "CHIDRO.ENV";

        public const int ExpectedObjectCount = 29;

        public const int ExpectedModelObjectCount = 25;

        public static DromeSceneData Load(
            OriginalAssetRoot root,
            DromeShadingMode mode,
            string dromeFileName)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (string.IsNullOrEmpty(dromeFileName))
            {
                dromeFileName = OriginalAssetRoot.DefaultDromeFile;
            }

            DromeSceneData data = new DromeSceneData();
            data.Root = root;
            data.Mode = mode;
            data.DromeFileName = dromeFileName;
            data.Drome = DromeFile.Load(root.Data, dromeFileName, DataParseOptions.Default);
            data.DromeSourcePath = data.Drome.SourcePath;

            if (data.Drome.Objects.Count != 0 && data.Drome.Drome != null)
            {
                data.Warnings.AddRange(data.Drome.Warnings);
            }

            data.Palette = OriginalPalette.Load(root);

            int[] highWater = (int[])TypeBaseline.Clone();

            Dictionary<string, WbmModel> modelCache =
                new Dictionary<string, WbmModel>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, DromeMaterialTable> materialCache =
                new Dictionary<string, DromeMaterialTable>(StringComparer.OrdinalIgnoreCase);

            ulong digest = FnvOffset;
            Fnv(ref digest, dromeFileName);

            foreach (ObjectDefinition definition in data.Drome.Objects)
            {
                int modelId = AssignModelId(highWater, definition, data.Warnings);

                if (IsNullModel(definition.Model))
                {
                    data.ObjectsWithoutModel.Add(definition);
                    continue;
                }

                string modelName = definition.Model.Trim();

                OriginalModelSet foundSet;
                string modelPath = root.FindModelFile(modelName, mode == DromeShadingMode.Textured ? OriginalModelSet.Textured : OriginalModelSet.Shaded, out foundSet);

                if (modelPath == null)
                {
                    string message = "Object \"" + definition.Name + "\" (type " + definition.TypeName
                                     + ") names model \"" + modelName + "\", which is in neither original model set.";

                    data.MissingModels.Add(message);
                    throw new OriginalAssetException(message + " Searched " + root.ShadedModelsPath + " and " + root.TexturedModelsPath + ".");
                }

                WbmModel model;

                if (!modelCache.TryGetValue(modelPath, out model))
                {
                    model = WbmReader.LoadFile(modelPath);
                    modelCache[modelPath] = model;
                }

                DromeMaterialTable table;

                if (!materialCache.TryGetValue(modelPath, out table))
                {
                    table = BuildMaterialTable(root, definition, model, modelPath, foundSet, modelId, data);
                    materialCache[modelPath] = table;
                }
                else if (definition.OwnPalette)
                {
                    // EngineObject::InitModel loads the object's own palette on every instance, not
                    // only the first (the material list conversion is what is once per list).
                    string wbpPath = root.FindPaletteFile(modelName, foundSet);

                    if (wbpPath != null)
                    {
                        data.Palette.ApplyWbpFile(wbpPath, (modelId & 1) != 0 || definition.ForceRemap);
                    }
                }

                DromeObjectData item = new DromeObjectData();
                item.Definition = definition;
                item.ModelId = modelId;
                item.Model = model;
                item.ModelSet = foundSet;
                item.ModelPath = modelPath;
                item.Materials = table;
                item.Placement = HbMat4.PlaceObject(definition, model);

                // LOD zero: the file's fullest level. The engine picks a level per frame by camera
                // distance (EngineObject::UpdateLODAndPushComponents HYPERX 0x004716f0); a still
                // preview shows the level a close camera would use.
                //
                // The drome object alone is relit with the three fixed lights by Renderer::
                // DrawAllMeshes (HYPERX 0x00442d20) and drawn from that cached per-face colour
                // table; every other object lights from g_lightDirs[0] only.
                Vector3[] slotLights = definition.Kind == ObjectKind.Drome
                    ? DromeMeshBuilder.DromeSlotLightDirections(model.Component.SubComponentCount)
                    : null;

                item.Built = DromeMeshBuilder.Build(
                    model, 0, item.Placement, table, data.Palette, mode, slotLights);

                item.BoundsMin = item.Built.BoundsMin;
                item.BoundsMax = item.Built.BoundsMax;

                data.Objects.Add(item);
                data.MeshCount++;
                data.SourceVertices += item.Built.SourceVertices;
                data.SourceFaces += item.Built.SourceFaces;
                data.BuiltVertices += item.Built.BuiltVertices;
                data.BuiltTriangles += item.Built.BuiltTriangles;
                data.SubmeshCount += item.Built.SubmeshCount;
                data.TwoSidedFaces += item.Built.TwoSidedFaces;

                data.Warnings.AddRange(item.Built.Warnings);

                AccumulateBounds(data, item, definition);

                Fnv(ref digest, definition.Name);
                Fnv(ref digest, definition.TypeName);
                Fnv(ref digest, modelPath);
                Fnv(ref digest, item.Built.Mesh);

                foreach (string textureName in item.Built.SubmeshTextureNames)
                {
                    if (string.IsNullOrEmpty(textureName))
                    {
                        continue;
                    }

                    if (!data.TextureFiles.ContainsKey(textureName) && !data.MissingTextures.Contains(textureName))
                    {
                        data.TextureNames.Add(textureName);
                        data.TextureReferences++;

                        string texturePath = root.FindTextureFile(textureName);

                        if (texturePath == null)
                        {
                            data.MissingTextures.Add(textureName);
                        }
                        else
                        {
                            data.TextureFiles[textureName] = texturePath;
                        }
                    }
                    else
                    {
                        data.TextureReferences++;
                    }
                }
            }

            data.Digest = digest.ToString("x16", CultureInfo.InvariantCulture);

            return data;
        }

        public static DromeSceneData Load(OriginalAssetRoot root, DromeShadingMode mode)
        {
            return Load(root, mode, OriginalAssetRoot.DefaultDromeFile);
        }

        private static void AccumulateBounds(DromeSceneData data, DromeObjectData item, ObjectDefinition definition)
        {
            Vector3 min = item.BoundsMin;
            Vector3 max = item.BoundsMax;

            data.BoundsMin = Vector3.Min(data.BoundsMin, min);
            data.BoundsMax = Vector3.Max(data.BoundsMax, max);

            if (definition.Kind == ObjectKind.Drome)
            {
                if (!data.HasDromeBounds)
                {
                    data.DromeBoundsMin = min;
                    data.DromeBoundsMax = max;
                    data.HasDromeBounds = true;
                }
                else
                {
                    data.DromeBoundsMin = Vector3.Min(data.DromeBoundsMin, min);
                    data.DromeBoundsMax = Vector3.Max(data.DromeBoundsMax, max);
                }
            }
        }

        private static DromeMaterialTable BuildMaterialTable(
            OriginalAssetRoot root,
            ObjectDefinition definition,
            WbmModel model,
            string modelPath,
            OriginalModelSet modelSet,
            int modelId,
            DromeSceneData data)
        {
            DromeMaterialTable table = new DromeMaterialTable();
            table.ModelName = definition.Model;
            table.ModelSet = modelSet;
            table.RemapSlotId = modelId;

            // EngineObject::InitModel: bRemap = m_nSlot & 1, forced by m_bForceRemap. The palette load
            // happens before the material conversion, so a drome's own .wbp is in the palette table
            // by the time the Direct3D conversion reads it.
            bool remap = (modelId & 1) != 0 || definition.ForceRemap;
            table.RemapApplied = remap;

            if (definition.OwnPalette)
            {
                string wbpPath = root.FindPaletteFile(definition.Model, modelSet);

                if (wbpPath == null)
                {
                    data.Warnings.Add(
                        "Object \"" + definition.Name + "\" asks for its own palette, but " + definition.Model
                        + ".wbp is in neither original model set; the base palette stands (the engine logs "
                        + "\"CGPalette::LoadPaletteEntries could not open\" and continues).");
                }
                else
                {
                    int before = data.Palette.WbpEntriesApplied;
                    table.OwnPaletteLoaded = data.Palette.ApplyWbpFile(wbpPath, remap);
                    table.OwnPaletteEntries = data.Palette.WbpEntriesApplied - before;
                }
            }

            MeshMaterial[] materials = model.Materials;
            DromeMaterialState[] states = new DromeMaterialState[materials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                MeshMaterial material = materials[i];
                DromeMaterialState state = new DromeMaterialState();
                state.ColourRaw = material.Colour;
                state.PaletteIndex = data.Palette.ApplyMaterialRemap(material.Colour, remap);
                state.BrightRgb = data.Palette.GetRemappedEntry(state.PaletteIndex);
                state.Ambient = material.Ambient;
                state.Diffuse = material.Diffuse;
                state.Emissive = material.Emissive;
                state.BackIndex = material.BackIndex;
                state.TextureName = model.GetTextureName(material);
                states[i] = state;

                if (state.TextureName != null)
                {
                    table.TextureReferences++;
                }
            }

            table.Materials = states;

            return table;
        }

        private static int AssignModelId(int[] highWater, ObjectDefinition definition, List<string> warnings)
        {
            int type = definition.TypeIndex;

            if (type < 0 || type >= highWater.Length)
            {
                warnings.Add("Object \"" + definition.Name + "\" has type index " + type + ", outside the 32 original types.");
                return -1;
            }

            // Cmd_DefineObject gives a Dromer its id from the team loader rather than the high-water
            // mark; an .ENV never defines one. Keep the engine's slot numbering for the type and warn.
            if (type == 1)
            {
                warnings.Add("Environment object \"" + definition.Name + "\" is a Dromer; the engine takes its model id from the team file, not the environment.");
            }

            int id = highWater[type];
            highWater[type] = id + 1;

            return id;
        }

        private static bool IsNullModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return true;
            }

            return string.Equals(model.Trim(), "null", StringComparison.OrdinalIgnoreCase);
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static void Fnv(ref ulong hash, string text)
        {
            if (text == null)
            {
                Fnv(ref hash, "<null>");
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= (byte)text[i];
                hash *= FnvPrime;
            }

            hash ^= 0xFF;
            hash *= FnvPrime;
        }

        private static void Fnv(ref ulong hash, Mesh mesh)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            Color32[] colours = mesh.colors32;

            Fnv(ref hash, positions.Length);
            Fnv(ref hash, mesh.subMeshCount);

            for (int i = 0; i < positions.Length; i++)
            {
                Fnv(ref hash, positions[i].x);
                Fnv(ref hash, positions[i].y);
                Fnv(ref hash, positions[i].z);
                Fnv(ref hash, normals[i].x);
                Fnv(ref hash, normals[i].y);
                Fnv(ref hash, normals[i].z);
                Fnv(ref hash, uvs[i].x);
                Fnv(ref hash, uvs[i].y);
                hash ^= (uint)((colours[i].r << 16) | (colours[i].g << 8) | colours[i].b);
                hash *= FnvPrime;
            }

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);

                Fnv(ref hash, indices.Length);

                for (int i = 0; i < indices.Length; i++)
                {
                    Fnv(ref hash, (uint)indices[i]);
                }
            }
        }

        private static void Fnv(ref ulong hash, int value)
        {
            Fnv(ref hash, (uint)value);
        }

        private static void Fnv(ref ulong hash, uint value)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FnvPrime;
            }
        }

        private static void Fnv(ref ulong hash, float value)
        {
            Fnv(ref hash, (uint)BitConverter.SingleToInt32Bits(value));
        }
    }
}
