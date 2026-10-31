using System.Collections.Generic;

namespace UBlade3.Assets
{
    // The version a model or animation file carries. src/graphics/modelfile.h struct FileVersion:
    // four 16 bit words and an int, 0x0c bytes. AnimFile::VersionCompare (HYPERX 0x00493c40) only
    // answers equal when every field is equal, which is what the shipped files carry.
    public readonly struct FileVersion
    {
        public FileVersion(short major, short minor, short revision, short unused, int build)
        {
            Major = major;
            Minor = minor;
            Revision = revision;
            Unused = unused;
            Build = build;
        }

        public short Major { get; }

        public short Minor { get; }

        public short Revision { get; }

        public short Unused { get; }

        public int Build { get; }

        // src/globals.cpp: FileVersion g_modelVersion = { 1, 7, 0, 0, 0 };
        public static FileVersion ExpectedModelVersion
        {
            get { return new FileVersion(1, 7, 0, 0, 0); }
        }

        // src/anim/animfile.cpp: FileVersion g_animVersion = { 1, 1, 0, 0, 0 };
        public static FileVersion ExpectedAnimationVersion
        {
            get { return new FileVersion(1, 1, 0, 0, 0); }
        }

        public override string ToString()
        {
            return Major + "." + Minor + "." + Revision + "." + Unused + "." + Build;
        }
    }

    // The 0x30 byte header every model and animation file opens with.
    // src/graphics/modelfile.h struct FileHeader, read by ModelFile::ReadHeader (HYPERX 0x00493be0).
    public sealed class FileHeader
    {
        // sizeof(FileHeader) in src/graphics/modelfile.h (STATIC_ASSERT).
        public const int Size = 0x30;

        // g_apFileTypeNames in src/globals.cpp: the tag is an index into that table.
        public const int DirectoryTag = 1;

        public const int ModelFileTag = 2;

        public const int AnimationFileTag = 4;

        internal FileHeader(int tag, string name, FileVersion version)
        {
            Tag = tag;
            Name = name;
            Version = version;
        }

        public int Tag { get; }

        // 32 bytes, NUL padded; the model/animation name the loader compares against file names.
        public string Name { get; }

        public FileVersion Version { get; }

        internal static FileHeader Read(AssetBytes bytes, long at)
        {
            int tag = bytes.I32(at);
            string name = bytes.FixedAscii(at + 0x04, 0x20);
            FileVersion version = new FileVersion(
                bytes.I16(at + 0x24),
                bytes.I16(at + 0x26),
                bytes.I16(at + 0x28),
                bytes.I16(at + 0x2a),
                bytes.I32(at + 0x2c));

            return new FileHeader(tag, name, version);
        }

        public override string ToString()
        {
            return Name + " tag=" + Tag + " version=" + Version;
        }
    }

    // A fixed 16 byte name used by the material and texture tables.
    // src/graphics/materialname.h: #define MATERIAL_NAME_BYTES 0x10, typedef char MaterialName[0x10].
    public readonly struct MaterialName
    {
        public const int Size = 0x10;

        internal MaterialName(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(Value); }
        }

        internal static MaterialName Read(AssetBytes bytes, long at)
        {
            return new MaterialName(bytes.FixedAscii(at, Size));
        }

        public override string ToString()
        {
            return Value;
        }
    }

    // One material record. src/graphics/meshmaterial.h class MeshMaterial, #pragma pack(push, 2)
    // with the loader's own 22 byte stride (STATIC_ASSERT(sizeof(MeshMaterial) == 0x16)).
    public sealed class MeshMaterial
    {
        // The record's stride in the file: 22, where natural packing would give 24.
        public const int Size = 0x16;

        // The runtime union in the original: nColour / acChannel[4].
        // 0xAARRGGBB; the low channel is a palette index to the software paths.
        public const int OffsetColour = 0x00;
        public const int OffsetAmbient = 0x04;
        public const int OffsetDiffuse = 0x08;
        public const int OffsetEmissive = 0x0c;
        public const int OffsetTextureIndex = 0x10;
        public const int OffsetBackIndex = 0x14;

        internal MeshMaterial(int colour, float ambient, float diffuse, float emissive, int textureIndex, short backIndex)
        {
            Colour = colour;
            Ambient = ambient;
            Diffuse = diffuse;
            Emissive = emissive;
            TextureIndex = textureIndex;
            BackIndex = backIndex;
        }

        public int Colour { get; }

        public float Ambient { get; }

        public float Diffuse { get; }

        public float Emissive { get; }

        // A one based index into the model's texture name table, 0 for none. At run time
        // ModelDatabase::CreateMaterialList replaces this with the loaded TexCacheEntry pointer.
        public int TextureIndex { get; }

        // A negative value means no back material; a back material makes the face two sided.
        public short BackIndex { get; }

        public byte Red
        {
            get { return (byte)((Colour >> 16) & 0xff); }
        }

        public byte Green
        {
            get { return (byte)((Colour >> 8) & 0xff); }
        }

        public byte Blue
        {
            get { return (byte)(Colour & 0xff); }
        }

        internal static MeshMaterial Read(AssetBytes bytes, long at)
        {
            return new MeshMaterial(
                bytes.I32(at + OffsetColour),
                bytes.F32(at + OffsetAmbient),
                bytes.F32(at + OffsetDiffuse),
                bytes.F32(at + OffsetEmissive),
                bytes.I32(at + OffsetTextureIndex),
                bytes.I16(at + OffsetBackIndex));
        }
    }

    // One bone (component slot) descriptor. src/graphics/meshbone.h struct MeshBoneDesc.
    //
    // The link id is the slot's own, not its parent's; m_vScale is a per model scale applied to the
    // translation of a posed animation matrix (MeshBone::PropagateTransforms, HYPERX 0x0046ef10).
    // Child ids index the model's slot descriptors, and in every shipped model the link id equals
    // the slot's own index.
    public sealed class MeshBoneDesc
    {
        // The fixed part up to the variable child id array: STATIC_ASSERT sizeof is not given for
        // the desc, but the child ids start at 0x20 (src/graphics/meshbone.h).
        public const int FixedBytes = 0x20;

        internal MeshBoneDesc(short linkId, short childCount, AssetVector3 rest, AssetVector3 scale, int unused, short[] childIds)
        {
            LinkId = linkId;
            ChildCount = childCount;
            Rest = rest;
            Scale = scale;
            Unused = unused;
            ChildIds = childIds;
        }

        // The slot's own id; MeshBone::PropagateTransforms compares it with the slot index it is
        // called for, and it equals the slot index in every shipped model.
        public short LinkId { get; }

        public short ChildCount { get; }

        // The rest translation, in model units. src/graphics/meshbone.cpp BindToLODMesh uses it as
        // the local translation for a bone with no animation.
        public AssetVector3 Rest { get; }

        // Per model scale, applied to the animation translation in PropagateTransforms. It maps
        // animation units to model units (0.0035 on the skater rigs, per axis on the props).
        public AssetVector3 Scale { get; }

        // Raw 0x1c int. Nothing in either reference reads it; on the skater rigs it decodes as a
        // very small negative float and no shipped name documents it (unresolved field).
        public int Unused { get; }

        // Child link ids, ChildCount long, beginning at 0x20 of the descriptor.
        public short[] ChildIds { get; }
    }

    // The shadow mesh a model carries, a flat copy of the model's silhouette.
    // src/graphics/shadowblock.h struct ShadowBlock: two shorts, two self relative offsets and
    // nFaceCount ShadowFace records beginning at 0x08. The vertex and index arrays live inside the
    // same component record, at the offsets the header records.
    public sealed class ShadowBlock
    {
        public const int FixedBytes = 0x08;

        internal ShadowBlock(
            short faceCount,
            short vertexCount,
            short vertexOffsetBytes,
            short indexOffsetBytes,
            ShadowFace[] faces,
            AssetVector3[] vertices,
            short[] indices)
        {
            FaceCount = faceCount;
            VertexCount = vertexCount;
            VertexOffsetBytes = vertexOffsetBytes;
            IndexOffsetBytes = indexOffsetBytes;
            Faces = faces;
            Vertices = vertices;
            Indices = indices;
        }

        public short FaceCount { get; }

        public short VertexCount { get; }

        // RecordOffset<Vec3>: the byte distance from the start of the ShadowBlock record to the
        // first vertex. Self relative, not a file offset.
        public short VertexOffsetBytes { get; }

        // RecordOffset<short>: the distance from the start of the ShadowBlock record to the index
        // array, one component link id per vertex.
        public short IndexOffsetBytes { get; }

        public ShadowFace[] Faces { get; }

        public AssetVector3[] Vertices { get; }

        public short[] Indices { get; }
    }

    // A shadow face: a MeshFace whose corner array runs on to four.
    // src/graphics/meshface.h struct ShadowFace, STATIC_ASSERT(sizeof(ShadowFace) == 0x18).
    public sealed class ShadowFace
    {
        public const int Size = 0x18;

        internal ShadowFace(int cornerCount, byte flags, byte materialIndex, AssetVector3 normal, short[] corners)
        {
            CornerCount = cornerCount;
            Flags = flags;
            MaterialIndex = materialIndex;
            Normal = normal;
            Corners = corners;
        }

        public int CornerCount { get; }

        public byte Flags { get; }

        public byte MaterialIndex { get; }

        public AssetVector3 Normal { get; }

        // Always four shorts in the record; only the first CornerCount are used.
        public short[] Corners { get; }
    }

    // The model component: the variable length header blob that names the model and holds the bone
    // descriptors and the shadow block. src/graphics/modelcomponent.h struct ModelComponent, read
    // by ModelComponent::ReadFirst/Read (HYPERX 0x004361e0 / 0x00436d30).
    //
    // Record layout: 0x00 int size (the record's own byte count, filled in after the read),
    // 0x04 short id, 0x06 char name[0x20], 0x26 short subcomponent count, 0x28 char unused[0x2c],
    // 0x54 Vec3 angle, 0x60 RecordOffset<ShadowBlock> shadow,
    // 0x62 RecordOffset<MeshBoneDesc> slot offsets, one per subcomponent.
    public sealed class ModelComponent
    {
        public const int SizeOffset = 0x00;
        public const int IdOffset = 0x04;
        public const int NameOffset = 0x06;
        public const int NameBytes = 0x20;
        public const int SubComponentOffset = 0x26;
        public const int UnusedOffset = 0x28;
        public const int UnusedBytes = 0x2c;
        public const int AngleOffset = 0x54;
        public const int ShadowOffsetField = 0x60;
        public const int SlotOffsetsOffset = 0x62;

        // g_flDegreesToRadians, src/globals.cpp: ModelDatabase::LoadLOD multiplies the three stored
        // angles by it in place before the record is used.
        public const float DegreesToRadians = 0.017453292f;

        internal ModelComponent(
            int size,
            short id,
            string name,
            short subComponentCount,
            byte[] unused,
            AssetVector3 angle,
            short shadowOffsetBytes,
            ShadowBlock shadow,
            short[] slotOffsetBytes,
            MeshBoneDesc[] slotDescriptors)
        {
            Size = size;
            Id = id;
            Name = name;
            SubComponentCount = subComponentCount;
            Unused = unused;
            AngleDegrees = angle;
            ShadowOffsetBytes = shadowOffsetBytes;
            Shadow = shadow;
            SlotOffsetBytes = slotOffsetBytes;
            SlotDescriptors = slotDescriptors;
        }

        public int Size { get; }

        // LoadLOD overwrites the stored id with the database's next index; the file's own value is
        // exposed here.
        public short Id { get; }

        public string Name { get; }

        public short SubComponentCount { get; }

        // 0x2c bytes the loader never reads; every shipped MDLTXT file holds zeros here.
        public byte[] Unused { get; }

        public bool UnusedAllZero
        {
            get
            {
                for (int i = 0; i < Unused.Length; i++)
                {
                    if (Unused[i] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        // The file stores degrees; LoadLOD converts the record in place to radians.
        public AssetVector3 AngleDegrees { get; }

        public AssetVector3 AngleRadians
        {
            get
            {
                return new AssetVector3(
                    AngleDegrees.X * DegreesToRadians,
                    AngleDegrees.Y * DegreesToRadians,
                    AngleDegrees.Z * DegreesToRadians);
            }
        }

        // RecordOffset<ShadowBlock>: 0 when the model carries no shadow block. Self relative to the
        // start of this component record.
        public short ShadowOffsetBytes { get; }

        public ShadowBlock Shadow { get; }

        // RecordOffset<MeshBoneDesc>, one per slot, self relative to the component record.
        public short[] SlotOffsetBytes { get; }

        // The bone/slot descriptors resolved from SlotOffsetBytes, in slot order.
        public MeshBoneDesc[] SlotDescriptors { get; }
    }

    // A face of a mesh block. src/graphics/meshface.h struct MeshFace (#pragma pack(push, 1)):
    //   0x00 short corner count       0x02 unsigned char flags
    //   0x03 char material index      0x04 Vec3 normal
    //   0x10 short corners[corner count], then a second corner block when FACE_FLAG_UV_SKIP is
    //   set, then one short UV index per corner when FACE_FLAG_HAS_UV is set, then a short width.
    public sealed class MeshFace
    {
        // src/graphics/face.h: FACE_FLAG_HAS_UV 0x20, FACE_FLAG_UV_SKIP 0x10. The clip bits share
        // the byte in the runtime but are not stored.
        public const byte FlagHasUV = 0x20;

        public const byte FlagUVSkip = 0x10;

        public const int HeaderBytes = 0x10;

        internal MeshFace(
            int cornerCount,
            byte flags,
            byte materialIndex,
            AssetVector3 normal,
            short[] corners,
            short[] secondCorners,
            short[] uvIndices,
            short scaledWidth)
        {
            CornerCount = cornerCount;
            Flags = flags;
            MaterialIndex = materialIndex;
            Normal = normal;
            Corners = corners;
            SecondCorners = secondCorners;
            UVIndices = uvIndices;
            ScaledWidth = scaledWidth;
        }

        public int CornerCount { get; }

        public byte Flags { get; }

        public byte MaterialIndex { get; }

        // The face normal. The shipped 24 bit path lights with it; D3DRend::DrawFace (HYPERX
        // 0x0044ce10) and SWRend::DrawFace (HYPERX 0x00496e00) both read it directly.
        public AssetVector3 Normal { get; }

        // Indices into the block's vertex array, CornerCount long.
        public short[] Corners { get; }

        // The second corner block stored when FlagUVSkip is set. No shipped renderer reads it; the
        // field is exposed so the data is not lost (unresolved use - see ASSET-FORMATS.md).
        public short[] SecondCorners { get; }

        // Indices into the block's TexCoord array, CornerCount long; empty when FlagHasUV is clear.
        public short[] UVIndices { get; }

        // The texture width less one the UVs were last scaled to (MeshFace::GetScaledWidth), or -1
        // with no UVs. The software renderer rescales the UVs in place against it.
        public short ScaledWidth { get; }

        public bool HasUV
        {
            get { return (Flags & FlagHasUV) != 0; }
        }

        public bool HasUVSkip
        {
            get { return (Flags & FlagUVSkip) != 0; }
        }
    }

    // One mesh block: the geometry of one component slot at one level of detail.
    // src/graphics/meshblock.h struct MeshBlock (#pragma pack(push, 1), sizeof 0x24):
    //   0x00 int size          0x04 short slot            0x06 short face count
    //   0x08 short vertex count 0x0a short normal count    0x0c short coord count
    //   0x0e Vec3 origin        0x1a float depth bias      0x1e ushort flags
    //   0x20 RecordOffset<Vec3> vertex offset, 0x22 face offsets, one per face.
    //
    // The arrays follow the face offset table, at VertexOffsetBytes from the start of the block:
    // vertex count Vec3 positions, then normal count Vec3 values, then coord count TexCoord pairs
    // (src/graphics/meshblock.h MeshBlock::Vertices/Coords). The field the decomp calls
    // nCoordBytes holds the TexCoord COUNT: MeshBlock::Coords returns TexCoord* and
    // CD3DTextureManager::RescaleSoftTexCoords indexes it with the face's UV index bounded by
    // nCoordBytes. All shipped blocks end exactly at the last TexCoord.
    //
    // The face records may sit anywhere in the block, before or after the vertex data; the face
    // offset table is the only way to find them.
    public sealed class MeshBlock
    {
        public const int HeaderBytes = 0x24;

        public const int FaceOffsetTableOffset = 0x22;

        // Bit five of wFlags asks the renderer for the depth sort (MeshBlock::SortsFaces).
        public const ushort FlagSortsFaces = 0x20;

        internal MeshBlock(
            int size,
            short slot,
            short faceCount,
            short vertexCount,
            short normalCount,
            short coordCount,
            AssetVector3 origin,
            float depthBias,
            ushort flags,
            short vertexOffsetBytes,
            AssetVector3[] vertices,
            AssetVector3[] normals,
            AssetVector2[] coords,
            MeshFace[] faces)
        {
            Size = size;
            Slot = slot;
            FaceCount = faceCount;
            VertexCount = vertexCount;
            NormalCount = normalCount;
            CoordCount = coordCount;
            Origin = origin;
            DepthBias = depthBias;
            Flags = flags;
            VertexOffsetBytes = vertexOffsetBytes;
            Vertices = vertices;
            Normals = normals;
            Coords = coords;
            Faces = faces;
        }

        // The record's byte count, including this field.
        public int Size { get; }

        // The destination slot in the LOD's slot array. ModelDatabase::LinkComponents
        // (HYPERX 0x00473240) stores the block at pSlots[pBlob->nSlot].
        public short Slot { get; }

        public short FaceCount { get; }

        public short VertexCount { get; }

        public short NormalCount { get; }

        // The TexCoord count. The original's field name (nCoordBytes) does not describe its use.
        public short CoordCount { get; }

        public AssetVector3 Origin { get; }

        public float DepthBias { get; }

        public ushort Flags { get; }

        public bool SortsFaces
        {
            get { return ((Flags >> 5) & 1) != 0; }
        }

        // RecordOffset<Vec3>: the byte distance from the start of this block to the vertex array.
        public short VertexOffsetBytes { get; }

        public AssetVector3[] Vertices { get; }

        // The normal pool stored after the vertices. Neither shipped renderer reads it: the
        // software path lights each face with MeshFace::Normal, and the 20XX hardware mesh builder
        // recomputes smooth normals. It is exposed raw, exactly as loaded.
        public AssetVector3[] Normals { get; }

        public AssetVector2[] Coords { get; }

        public MeshFace[] Faces { get; }
    }

    // One level of detail: a ComponentFileHeader then that many MeshBlock records.
    // src/graphics/modelcomponent.cpp struct ComponentFileHeader { unsigned int cRecords;
    // int cBytes; }, read by ModelDatabase::LinkComponents (HYPERX 0x00473240).
    public sealed class ModelLod
    {
        internal ModelLod(int level, uint recordCount, int byteCount, MeshBlock[] blocks, MeshBlock[] bySlot)
        {
            Level = level;
            RecordCount = recordCount;
            ByteCount = byteCount;
            Blocks = blocks;
            BySlot = bySlot;
        }

        public int Level { get; }

        public uint RecordCount { get; }

        // The total MeshBlock bytes the file header states; ModelComponent::ReadAll compares it
        // with the sum of the record sizes.
        public int ByteCount { get; }

        // The records in file order.
        public MeshBlock[] Blocks { get; }

        // The same blocks indexed by their slot, null where the level has no block for a slot.
        public MeshBlock[] BySlot { get; }
    }

    // One complete .wbm model: header, component, material block and every LOD group.
    // The read path is ModelDatabase::LoadLOD (HYPERX 0x004728e0), which calls
    // ModelComponent::ReadFirst, ModelDatabase::ReadMaterials (HYPERX 0x00472c00) and
    // ModelDatabase::LinkComponents.
    public sealed class WbmModel
    {
        internal WbmModel(
            string sourceName,
            FileHeader header,
            short lodCount,
            float[] lodThresholds,
            ModelComponent component,
            string materialFileName,
            MaterialName[] materialNames,
            MeshMaterial[] materials,
            MaterialName[] textureNames,
            ModelLod[] lods)
        {
            SourceName = sourceName;
            Header = header;
            LodCount = lodCount;
            LodThresholds = lodThresholds;
            Component = component;
            MaterialFileName = materialFileName;
            MaterialNames = materialNames;
            Materials = materials;
            TextureNames = textureNames;
            Lods = lods;
        }

        public string SourceName { get; }

        public FileHeader Header { get; }

        // The count of LOD groups that follow the material block; LODNode::Init sizes its arrays
        // from it (LOD_LEVEL_COUNT is five).
        public short LodCount { get; }

        // The five thresholds the file always carries; LoadLOD squares the used ones for the
        // runtime near/far bands and only reads entries one to lodCount - 1.
        public float[] LodThresholds { get; }

        public ModelComponent Component { get; }

        // The material list file name: 16 fixed bytes in the model, loaded as a sibling .mat file
        // by ModelDatabase::FindOrLoadMaterialList unless the model already supplies its materials.
        public string MaterialFileName { get; }

        // The nMatCount * 16 byte name block ReadMaterials seeks past. The shipped models carry
        // names here but the runtime never reads them; exposed for completeness.
        public MaterialName[] MaterialNames { get; }

        public MeshMaterial[] Materials { get; }

        // Texture name table; MeshMaterial.TextureIndex is one based into it.
        public MaterialName[] TextureNames { get; }

        public ModelLod[] Lods { get; }

        public string GetTextureName(MeshMaterial material)
        {
            if (material == null || material.TextureIndex <= 0 || material.TextureIndex > TextureNames.Length)
            {
                return null;
            }

            return TextureNames[material.TextureIndex - 1].Value;
        }

        public override string ToString()
        {
            return "WbmModel(" + Component.Name + ", lods=" + LodCount + ", materials=" + Materials.Length + ")";
        }
    }

    // Reads a .wbm file into the immutable model above. The layout and every validation here come
    // from src/graphics/modeldatabase.cpp, src/graphics/modelcomponent.cpp,
    // src/graphics/modelfile.cpp and src/graphics/meshblock.h.
    public static class WbmReader
    {
        // FileHeader + short lod count + five floats.
        public const int FixedHeaderBytes = FileHeader.Size + 2 + (5 * 4);

        public static WbmModel LoadFile(string path)
        {
            return Load(System.IO.File.ReadAllBytes(path), path);
        }

        public static WbmModel Load(byte[] data, string sourceName)
        {
            AssetBytes bytes = new AssetBytes(data, sourceName);
            FileHeader header = FileHeader.Read(bytes, 0);

            // ModelFile::Open (HYPERX 0x00435fe0) refuses a database directory record, then
            // requires AnimFile::VersionCompare(&g_modelVersion, &version) == 0, an exact match.
            if (header.Tag == FileHeader.DirectoryTag)
            {
                throw AssetFormatException.At(sourceName, 0, "this is a model database directory record (tag 1), not a model file");
            }

            if (!SameVersion(header.Version, FileVersion.ExpectedModelVersion))
            {
                throw AssetFormatException.At(
                    sourceName,
                    0x24,
                    "model file version is " + header.Version + ", the loader expects " + FileVersion.ExpectedModelVersion);
            }

            // ModelDatabase::LoadLOD reads the LOD count and the five thresholds, then the header
            // blob through ModelComponent::ReadFirst (HYPERX 0x004361e0).
            short lodCount = bytes.I16(FileHeader.Size);

            if (lodCount < 1 || lodCount > 5)
            {
                // LODNode::Init sizes its arrays with LOD_LEVEL_COUNT five.
                throw AssetFormatException.At(sourceName, FileHeader.Size, "LOD count " + lodCount + " is outside 1..5");
            }

            float[] thresholds = new float[5];

            for (int i = 0; i < 5; i++)
            {
                thresholds[i] = bytes.F32(FileHeader.Size + 2 + (i * 4));
            }

            long at = FixedHeaderBytes;
            int componentSize = bytes.I32(at);

            if (componentSize < 4 || at + componentSize > data.Length)
            {
                throw AssetFormatException.At(sourceName, at, "model component record size " + componentSize + " does not fit the file");
            }

            ModelComponent component = ReadComponent(bytes, at, componentSize);
            at += componentSize;

            // ModelDatabase::ReadMaterials (HYPERX 0x00472c00).
            int materialCount = bytes.U8(at);
            at += 1;

            string materialFileName = bytes.FixedAscii(at, MaterialName.Size);
            at += MaterialName.Size;

            MaterialName[] materialNames = new MaterialName[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                materialNames[i] = MaterialName.Read(bytes, at);
                at += MaterialName.Size;
            }

            MeshMaterial[] materials = new MeshMaterial[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                materials[i] = MeshMaterial.Read(bytes, at);
                at += MeshMaterial.Size;
            }

            short textureCount = bytes.I16(at);
            at += 2;

            if (textureCount < 0)
            {
                throw AssetFormatException.At(sourceName, at - 2, "negative texture name count " + textureCount);
            }

            MaterialName[] textureNames = new MaterialName[textureCount];

            for (int i = 0; i < textureCount; i++)
            {
                textureNames[i] = MaterialName.Read(bytes, at);
                at += MaterialName.Size;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                MeshMaterial material = materials[i];

                if (material.TextureIndex < 0 || material.TextureIndex > textureCount)
                {
                    throw AssetFormatException.At(
                        sourceName,
                        at,
                        "material " + i + " names texture " + material.TextureIndex + " of a " + textureCount + " entry table");
                }

                if (material.TextureIndex > 0 && textureNames[material.TextureIndex - 1].IsEmpty)
                {
                    // ModelDatabase::CreateMaterialList would call FindOrLoadTexture("") and then
                    // Fatal with "Error reading activating texture".
                    throw AssetFormatException.At(
                        sourceName,
                        at,
                        "material " + i + " references the empty texture name " + material.TextureIndex);
                }

                if (material.BackIndex < -1 || material.BackIndex >= materialCount)
                {
                    throw AssetFormatException.At(
                        sourceName,
                        at,
                        "material " + i + " back index " + material.BackIndex + " is outside -1.." + (materialCount - 1));
                }
            }

            // ModelDatabase::LinkComponents reads one component file header per LOD group.
            ModelLod[] lods = new ModelLod[lodCount];
            int slotCount = component.SubComponentCount;

            for (int level = 0; level < lodCount; level++)
            {
                uint recordCount = bytes.U32(at);
                int byteCount = bytes.I32(at + 4);
                at += 8;

                if (recordCount > 0x10000)
                {
                    throw AssetFormatException.At(sourceName, at - 8, "LOD " + level + " record count " + recordCount + " is implausible");
                }

                if (byteCount < 0)
                {
                    throw AssetFormatException.At(sourceName, at - 4, "LOD " + level + " byte count " + byteCount + " is negative");
                }

                MeshBlock[] blocks = new MeshBlock[recordCount];
                int total = 0;

                for (int record = 0; record < recordCount; record++)
                {
                    int blockSize = bytes.I32(at);

                    if (blockSize < MeshBlock.HeaderBytes || at + blockSize > data.Length)
                    {
                        throw AssetFormatException.At(
                            sourceName,
                            at,
                            "LOD " + level + " record " + record + " size " + blockSize + " does not fit the file");
                    }

                    blocks[record] = ReadMeshBlock(bytes, at, blockSize, materialCount, slotCount, level, record);
                    total += blockSize;
                    at += blockSize;
                }

                if (total != byteCount)
                {
                    // ModelComponent::ReadAll: "Component file totals error."
                    throw AssetFormatException.At(
                        sourceName,
                        at,
                        "LOD " + level + " records total " + total + " byte(s), the header states " + byteCount);
                }

                MeshBlock[] bySlot = new MeshBlock[slotCount];

                for (int record = 0; record < blocks.Length; record++)
                {
                    MeshBlock block = blocks[record];

                    if (block.Slot < 0 || block.Slot >= slotCount)
                    {
                        // LinkComponents would write pSlots[pBlob->nSlot] out of bounds here.
                        throw AssetFormatException.At(
                            sourceName,
                            at,
                            "LOD " + level + " record " + record + " slot " + block.Slot + " is outside the " + slotCount + " component slots");
                    }

                    if (bySlot[block.Slot] != null)
                    {
                        throw AssetFormatException.At(
                            sourceName,
                            at,
                            "LOD " + level + " has two blocks for slot " + block.Slot);
                    }

                    bySlot[block.Slot] = block;
                }

                lods[level] = new ModelLod(level, recordCount, byteCount, blocks, bySlot);
            }

            if (at != data.Length)
            {
                throw AssetFormatException.At(sourceName, at, (data.Length - at) + " byte(s) follow the last LOD record");
            }

            return new WbmModel(
                sourceName,
                header,
                lodCount,
                thresholds,
                component,
                materialFileName,
                materialNames,
                materials,
                textureNames,
                lods);
        }

        private static ModelComponent ReadComponent(AssetBytes bytes, long recordStart, int recordSize)
        {
            long end = recordStart + recordSize;

            // The fixed part ends at the slot offset table: 0x62 bytes.
            if (recordSize < ModelComponent.SlotOffsetsOffset)
            {
                throw AssetFormatException.At(bytes.SourceName, recordStart, "model component record is too short for its fixed fields");
            }

            short id = bytes.I16(recordStart + ModelComponent.IdOffset);
            string name = bytes.FixedAscii(recordStart + ModelComponent.NameOffset, ModelComponent.NameBytes);
            short subComponents = bytes.I16(recordStart + ModelComponent.SubComponentOffset);

            if (subComponents < 0 || subComponents > 0x1000)
            {
                throw AssetFormatException.At(bytes.SourceName, recordStart + ModelComponent.SubComponentOffset, "subcomponent count " + subComponents + " is implausible");
            }

            byte[] unused = bytes.Slice(recordStart + ModelComponent.UnusedOffset, ModelComponent.UnusedBytes);
            AssetVector3 angle = new AssetVector3(
                bytes.F32(recordStart + ModelComponent.AngleOffset),
                bytes.F32(recordStart + ModelComponent.AngleOffset + 4),
                bytes.F32(recordStart + ModelComponent.AngleOffset + 8));
            short shadowOffset = bytes.I16(recordStart + ModelComponent.ShadowOffsetField);

            if (ModelComponent.SlotOffsetsOffset + (2 * subComponents) > recordSize)
            {
                throw AssetFormatException.At(
                    bytes.SourceName,
                    recordStart + ModelComponent.SlotOffsetsOffset,
                    subComponents + " slot offsets do not fit the " + recordSize + " byte component record");
            }

            short[] slotOffsets = new short[subComponents];
            MeshBoneDesc[] bones = new MeshBoneDesc[subComponents];

            for (int slot = 0; slot < subComponents; slot++)
            {
                short offset = bytes.I16(recordStart + ModelComponent.SlotOffsetsOffset + (2 * slot));

                slotOffsets[slot] = offset;

                if (offset < ModelComponent.SlotOffsetsOffset + (2 * subComponents) || offset + MeshBoneDesc.FixedBytes > recordSize)
                {
                    throw AssetFormatException.At(
                        bytes.SourceName,
                        recordStart + ModelComponent.SlotOffsetsOffset + (2 * slot),
                        "slot " + slot + " bone descriptor offset " + offset + " is outside the component record");
                }

                long boneAt = recordStart + offset;
                short linkId = bytes.I16(boneAt);
                short childCount = bytes.I16(boneAt + 2);

                if (linkId < 0 || linkId >= subComponents)
                {
                    throw AssetFormatException.At(bytes.SourceName, boneAt, "bone link id " + linkId + " is outside 0.." + (subComponents - 1));
                }

                if (childCount < 0 || childCount > subComponents)
                {
                    throw AssetFormatException.At(bytes.SourceName, boneAt + 2, "bone child count " + childCount + " is outside 0.." + subComponents);
                }

                if (offset + MeshBoneDesc.FixedBytes + (2 * childCount) > recordSize)
                {
                    throw AssetFormatException.At(bytes.SourceName, boneAt, "bone child id array runs past the component record");
                }

                short[] childIds = new short[childCount];

                for (int child = 0; child < childCount; child++)
                {
                    short childId = bytes.I16(boneAt + MeshBoneDesc.FixedBytes + (2 * child));

                    if (childId < 0 || childId >= subComponents)
                    {
                        throw AssetFormatException.At(bytes.SourceName, boneAt + MeshBoneDesc.FixedBytes + (2 * child), "bone child id " + childId + " is outside 0.." + (subComponents - 1));
                    }

                    childIds[child] = childId;
                }

                bones[slot] = new MeshBoneDesc(
                    linkId,
                    childCount,
                    new AssetVector3(bytes.F32(boneAt + 4), bytes.F32(boneAt + 8), bytes.F32(boneAt + 12)),
                    new AssetVector3(bytes.F32(boneAt + 16), bytes.F32(boneAt + 20), bytes.F32(boneAt + 24)),
                    bytes.I32(boneAt + 28),
                    childIds);
            }

            ShadowBlock shadow = null;

            if (shadowOffset != 0)
            {
                if (shadowOffset < 0 || shadowOffset + ShadowBlock.FixedBytes > recordSize)
                {
                    throw AssetFormatException.At(bytes.SourceName, recordStart + ModelComponent.ShadowOffsetField, "shadow block offset " + shadowOffset + " is outside the component record");
                }

                shadow = ReadShadowBlock(bytes, recordStart + shadowOffset, recordStart, recordSize);
            }

            return new ModelComponent(recordSize, id, name, subComponents, unused, angle, shadowOffset, shadow, slotOffsets, bones);
        }

        private static ShadowBlock ReadShadowBlock(AssetBytes bytes, long blockStart, long recordStart, int recordSize)
        {
            long recordEnd = recordStart + recordSize;
            short faceCount = bytes.I16(blockStart);
            short vertexCount = bytes.I16(blockStart + 2);
            short vertexOffset = bytes.I16(blockStart + 4);
            short indexOffset = bytes.I16(blockStart + 6);

            if (faceCount < 0 || vertexCount < 0)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart, "negative shadow face or vertex count");
            }

            long facesAt = blockStart + ShadowBlock.FixedBytes;

            if (facesAt + (faceCount * ShadowFace.Size) > recordEnd)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart, "shadow faces run past the component record");
            }

            ShadowFace[] faces = new ShadowFace[faceCount];

            for (int face = 0; face < faceCount; face++)
            {
                long faceAt = facesAt + (face * ShadowFace.Size);
                int cornerCount = bytes.I16(faceAt);
                short[] corners = new short[4];

                for (int corner = 0; corner < 4; corner++)
                {
                    corners[corner] = bytes.I16(faceAt + MeshFace.HeaderBytes + (2 * corner));
                }

                if (cornerCount < 3 || cornerCount > 4)
                {
                    throw AssetFormatException.At(bytes.SourceName, faceAt, "shadow face corner count " + cornerCount + " is outside 3..4");
                }

                faces[face] = new ShadowFace(
                    cornerCount,
                    bytes.U8(faceAt + 2),
                    bytes.U8(faceAt + 3),
                    new AssetVector3(bytes.F32(faceAt + 4), bytes.F32(faceAt + 8), bytes.F32(faceAt + 12)),
                    corners);
            }

            AssetVector3[] vertices = new AssetVector3[vertexCount];

            if (vertexOffset != 0)
            {
                long verticesAt = blockStart + vertexOffset;

                if (verticesAt + (vertexCount * 12) > recordEnd)
                {
                    throw AssetFormatException.At(bytes.SourceName, blockStart + 4, "shadow vertex array runs past the component record");
                }

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    long vertexAt = verticesAt + (vertex * 12);

                    vertices[vertex] = new AssetVector3(bytes.F32(vertexAt), bytes.F32(vertexAt + 4), bytes.F32(vertexAt + 8));
                }
            }
            else if (vertexCount != 0)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart + 4, "shadow block has vertices but a zero vertex offset");
            }

            short[] indices = new short[vertexCount];

            if (indexOffset != 0)
            {
                long indicesAt = blockStart + indexOffset;

                if (indicesAt + (vertexCount * 2) > recordEnd)
                {
                    throw AssetFormatException.At(bytes.SourceName, blockStart + 6, "shadow index array runs past the component record");
                }

                for (int index = 0; index < vertexCount; index++)
                {
                    indices[index] = bytes.I16(indicesAt + (index * 2));
                }
            }
            else if (vertexCount != 0)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart + 6, "shadow block has vertices but a zero index offset");
            }

            return new ShadowBlock(faceCount, vertexCount, vertexOffset, indexOffset, faces, vertices, indices);
        }

        private static MeshBlock ReadMeshBlock(AssetBytes bytes, long blockStart, int blockSize, int materialCount, int slotCount, int level, int record)
        {
            long blockEnd = blockStart + blockSize;
            short slot = bytes.I16(blockStart + 0x04);
            short faceCount = bytes.I16(blockStart + 0x06);
            short vertexCount = bytes.I16(blockStart + 0x08);
            short normalCount = bytes.I16(blockStart + 0x0a);
            short coordCount = bytes.I16(blockStart + 0x0c);

            if (faceCount < 0 || vertexCount < 0 || normalCount < 0 || coordCount < 0)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart + 0x06, "negative mesh block count");
            }

            long faceTableEnd = blockStart + MeshBlock.FaceOffsetTableOffset + (2 * (long)faceCount);

            if (faceTableEnd > blockEnd)
            {
                throw AssetFormatException.At(bytes.SourceName, blockStart + 0x06, "face offset table runs past the mesh block");
            }

            AssetVector3 origin = new AssetVector3(
                bytes.F32(blockStart + 0x0e),
                bytes.F32(blockStart + 0x12),
                bytes.F32(blockStart + 0x16));
            float depthBias = bytes.F32(blockStart + 0x1a);
            ushort flags = bytes.U16(blockStart + 0x1e);
            short vertexOffset = bytes.I16(blockStart + 0x20);

            // MeshBlock::Vertices: VertexOffset.In(this), a self relative byte offset.
            AssetVector3[] vertices = new AssetVector3[vertexCount];
            AssetVector3[] normals = new AssetVector3[normalCount];
            AssetVector2[] coords = new AssetVector2[coordCount];

            long dataBytes = ((long)vertexCount + normalCount) * 12 + ((long)coordCount * 8);

            if (dataBytes == 0)
            {
                if (vertexOffset != 0)
                {
                    throw AssetFormatException.At(bytes.SourceName, blockStart + 0x20, "mesh block has no geometry data but a nonzero vertex offset");
                }
            }
            else
            {
                long dataAt = blockStart + vertexOffset;

                if (vertexOffset < 0 || dataAt + dataBytes > blockEnd)
                {
                    throw AssetFormatException.At(bytes.SourceName, blockStart + 0x20, "mesh block geometry data runs past the record");
                }

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    long at = dataAt + (vertex * 12);

                    vertices[vertex] = new AssetVector3(bytes.F32(at), bytes.F32(at + 4), bytes.F32(at + 8));
                }

                long normalsAt = dataAt + ((long)vertexCount * 12);

                for (int normal = 0; normal < normalCount; normal++)
                {
                    long at = normalsAt + (normal * 12);

                    normals[normal] = new AssetVector3(bytes.F32(at), bytes.F32(at + 4), bytes.F32(at + 8));
                }

                long coordsAt = normalsAt + ((long)normalCount * 12);

                for (int coord = 0; coord < coordCount; coord++)
                {
                    long at = coordsAt + (coord * 8);

                    coords[coord] = new AssetVector2(bytes.F32(at), bytes.F32(at + 4));
                }
            }

            MeshFace[] faces = new MeshFace[faceCount];

            for (int face = 0; face < faceCount; face++)
            {
                short faceOffset = bytes.I16(blockStart + MeshBlock.FaceOffsetTableOffset + (2 * face));
                long faceAt = blockStart + faceOffset;

                if (faceOffset < 0 || faceAt + MeshFace.HeaderBytes > blockEnd)
                {
                    throw AssetFormatException.At(
                        bytes.SourceName,
                        blockStart + MeshBlock.FaceOffsetTableOffset + (2 * face),
                        "LOD " + level + " record " + record + " face " + face + " offset " + faceOffset + " is outside the block");
                }

                int cornerCount = bytes.I16(faceAt);
                byte faceFlags = bytes.U8(faceAt + 2);
                byte materialIndex = bytes.U8(faceAt + 3);

                if (cornerCount < 3 || cornerCount > 0x100)
                {
                    throw AssetFormatException.At(bytes.SourceName, faceAt, "face corner count " + cornerCount + " is outside 3..256");
                }

                if (materialIndex >= materialCount)
                {
                    throw AssetFormatException.At(bytes.SourceName, faceAt + 3, "face material " + materialIndex + " is outside 0.." + (materialCount - 1));
                }

                AssetVector3 normal = new AssetVector3(
                    bytes.F32(faceAt + 0x04),
                    bytes.F32(faceAt + 0x08),
                    bytes.F32(faceAt + 0x0c));

                long cursor = faceAt + MeshFace.HeaderBytes;
                short[] corners = new short[cornerCount];

                for (int corner = 0; corner < cornerCount; corner++)
                {
                    short vertex = bytes.I16(cursor);
                    cursor += 2;

                    if (vertex < 0 || vertex >= vertexCount)
                    {
                        throw AssetFormatException.At(bytes.SourceName, faceAt, "face corner " + vertex + " is outside the " + vertexCount + " block vertices");
                    }

                    corners[corner] = vertex;
                }

                short[] secondCorners = new short[0];

                if ((faceFlags & MeshFace.FlagUVSkip) != 0)
                {
                    secondCorners = new short[cornerCount];

                    for (int corner = 0; corner < cornerCount; corner++)
                    {
                        secondCorners[corner] = bytes.I16(cursor);
                        cursor += 2;
                    }
                }

                short[] uvIndices = new short[0];
                short scaledWidth = -1;

                if ((faceFlags & MeshFace.FlagHasUV) != 0)
                {
                    uvIndices = new short[cornerCount];

                    for (int corner = 0; corner < cornerCount; corner++)
                    {
                        short uv = bytes.I16(cursor);
                        cursor += 2;

                        if (uv < 0 || uv >= coordCount)
                        {
                            throw AssetFormatException.At(bytes.SourceName, faceAt, "face UV index " + uv + " is outside the " + coordCount + " block texture coordinates");
                        }

                        uvIndices[corner] = uv;
                    }

                    scaledWidth = bytes.I16(cursor);
                    cursor += 2;
                }

                if (cursor > blockEnd)
                {
                    throw AssetFormatException.At(bytes.SourceName, faceAt, "face record runs past the mesh block");
                }

                faces[face] = new MeshFace(cornerCount, faceFlags, materialIndex, normal, corners, secondCorners, uvIndices, scaledWidth);
            }

            return new MeshBlock(
                blockSize,
                slot,
                faceCount,
                vertexCount,
                normalCount,
                coordCount,
                origin,
                depthBias,
                flags,
                vertexOffset,
                vertices,
                normals,
                coords,
                faces);
        }

        private static bool SameVersion(FileVersion left, FileVersion right)
        {
            return left.Major == right.Major
                   && left.Minor == right.Minor
                   && left.Revision == right.Revision
                   && left.Unused == right.Unused
                   && left.Build == right.Build;
        }
    }
}
