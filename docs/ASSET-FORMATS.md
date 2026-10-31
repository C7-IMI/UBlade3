# ASSET-FORMATS - HyperBlade 1996 model, animation, palette and texture formats

This is the exact format reference established by reading the byte-matching decompilation
(`third_party/hyperblade-decomp`, UBlade3's source of truth), the modern C++ reference build
(`third_party/hyperblade-20XX`, used as the readable oracle for hand-written assembly and for
damaged-file behaviour), and the original retail data (`D:\Games\HyperBlade\HYB\data`, never
committed). It documents the formats implemented by the dependency-free C# namespace
`UBlade3.Assets` in `unity/UBlade3/Assets/Scripts/UBlade3/Assets`, the coverage achieved against
the shipped disc, and the fields that remain unresolved.

UBlade3 never copies or commits original assets. Every loader reads the player's own install; the
probe fails loudly rather than substituting placeholder geometry or textures.

## 1. Sources of truth

| Source | Role |
| --- | --- |
| `third_party/hyperblade-decomp` | Byte-matching decompilation of `HYPERX.EXE` v1.0.0.1. Field offsets, structure layouts, version rules and every loader's control flow. Citations below name the source file plus the function's HYPERX address. |
| `third_party/hyperblade-20XX` | Modern C++ rebuild. Used where the decompilation is hand-written assembly (MDM1), and for the documented behaviour on damaged shipped files (`docs/hardware_renderer.md`, `tests/mdm1_check.cpp`, `tools/check_model_files.py`). |
| `D:\Games\HyperBlade\HYB\data` | The shipped data this document measures. 1996 disc, `HYPERX.EXE` v1.0.0.1. Read-only; not part of the repository. |

The binary tags come from `g_apFileTypeNames` in
`third_party/hyperblade-decomp/src/globals.cpp` (tag 1 = database directory, 2 = model file,
4 = animation file), the extensions from the same file (`g_pszModelExtension = "wbm"`,
`g_pszAnimExtension = "wba"`, `g_pszPaletteExtension = "wbp"`, `g_pszBitmapExtension = "bmp"`).

## 2. Where the assets live and how they are found

The original executable resolves every name against its own directory (`g_gameApp.m_strExeDir`,
which is `<install>\Data\`) and appends its own folder names:

| Directory | Constant in `src/globals.cpp` / `src/app/gameapp.cpp` | Used for |
| --- | --- | --- |
| `Data\Models\Mdltxt` | `GameApp::SetModelPath` (`gameapp.cpp` line 158) | textured model set (173 `.wbm`, 28 `.wbp`) |
| `Data\Models\Mdlshd` | `GameApp::SetModelPath` (`gameapp.cpp` line 163) | shaded model set (173 `.wbm`, 28 `.wbp`) |
| `Data\Textures` | `m_strTexturesPath` (`gameapp.cpp` line 331) | 24-bit `.bmp` textures (333) |
| `Data\AnimData` | `m_strAnimDataPath` (`gameapp.cpp` line 320) | `.wba` animation sets (188) and `AnimAssm.txt` |
| `Data\assets8` | `m_strAssetsPath` (`gameapp.cpp` line 317) | MDM1-packed 8-bit bitmaps (1767) for the never-shipped `mdmtex.dll` software path |

UBlade3 locates the install through `UBlade3.Data.GameDataRoot` (unchanged from the U3-M1 data
layer): the `HYPERBLADE_GAME_DATA` environment variable first, then
`Assets/StreamingAssets/game-data-path.txt`, then a caller-supplied default. All child folders are
resolved case-insensitively because the disc mixes case. Only `AssetProbe` references the data
layer; every format reader in `UBlade3.Assets` takes bytes or text and depends on nothing outside
the BCL.

## 3. Common file header and version rules

Every model and animation file opens with the 0x30 byte `FileHeader`
(`src/graphics/modelfile.h`, read by `ModelFile::ReadHeader` HYPERX 0x00493be0):

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | int tag: 2 for a model file, 4 for an animation file, 1 for a database directory |
| 0x04 | 0x20 | char name[32], NUL padded |
| 0x24 | 2+2+2+2 | short version major, minor, revision, unused |
| 0x2c | 4 | int build |

Version acceptance is exact:

* Models: `ModelFile::Open` (HYPERX 0x00435fe0) refuses a directory record and then requires
  `AnimFile::VersionCompare(&g_modelVersion, &version) == 0`. `g_modelVersion = {1, 7, 0, 0, 0}`
  (`src/globals.cpp`).
* Animations: `AnimFile::Open` (HYPERX 0x00493cb0) requires tag 4 and then
  `g_animVersion = {1, 1, 0, 0, 0}` (`src/anim/animfile.cpp`).

`AnimFile::VersionCompare` (HYPERX 0x00493c40) answers zero only when every field matches; the
C# readers therefore reject any other tag and version with an `AssetFormatException` carrying the
file name and byte offset.

## 4. MDM1 LZSS

Every asset file may be packed, and on this disc exactly the 1767 bitmaps under `Data\assets8`
are (`Mdm1.HasMagic` on the first dword).

Container:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | dword tag `MDM1` = 0x314d444d little endian |
| 0x04 | 4 | int decompressed byte size (the payload that follows is the LZSS stream) |

Sources: `src/graphics/mdm1.h` (`MDM1_MAGIC 0x314d444d`, `WINDOW_BYTES 0x1000`,
`WINDOW_MARGIN 0x80`), `src/graphics/mdm1.cpp`: `MDM1::Decompress` HYPERX 0x00440140,
`MDM1::ReadCallback` HYPERX 0x004401e0, `MDM1::LZSSDecompress` HYPERX 0x00493f80 (naked `__asm`),
`MDM1::RefillInputWindow` HYPERX 0x0049403a (naked `__asm`). The readable C++ port is
`third_party/hyperblade-20XX/src/graphics/mdm1.cpp`.

Algorithm, transcribed exactly:

* A 0x1000 byte sliding window. Reading starts with the window "full" (position 0x1000), so the
  first refill fills it. A refill moves the unread tail to the window front and reads
  `0x1000 - tail` bytes after it; a short read at end of file is success with the tail (the
  original's `call RefillInputWindow / jge loadFlags` continues on a zero read, and only a
  negative read fails). A refill happens before a flag word when the read position reaches
  `0x1000 - 0x80`; the 0x80 margin covers one flag word and its sixteen items.
* A refill first reads a 16 bit flag word; its low bit first, one shift per item. Clear = literal
  byte from the stream. Set = a 16 bit reference word: bits 0-3 are the length nibble (length =
  nibble + 3), bits 4-15 are the distance (source = output position - distance). References may
  overlap what they are writing.
* A length of 0x12 (nibble 0xf) is long form: the next byte is added to the length. If that add
  carries past 0xff the stream ends with the number of bytes written, except when the wrapped
  length is 0x10, which copies nothing and continues with the next item.
* `MDM1::Decompress` verifies the written count against the declared size and answers null on a
  mismatch; the C# port throws `AssetFormatException` instead (fail loudly).
* The original's allocation of the declared size is not checked against the data; the C# port
  bounds the output and throws on an overrun. Both agree on every shipped file.

Verified: all 1767 MDM1 files decode, and every decompressed stream is exactly the declared
number of bytes (the 20XX `mdm1_check` gate asserts the same). The decompressed payload of an
`assets8` file is a raw DIB (section 9.4), not a `BM` file.

## 5. WBM model database

Read path: `ModelDatabase::LoadLOD` (HYPERX 0x004728e0), which calls
`ModelComponent::ReadFirst` (HYPERX 0x004361e0), `ModelDatabase::ReadMaterials`
(HYPERX 0x00472c00) and `ModelDatabase::LinkComponents` (HYPERX 0x00473240).
Structures: `src/graphics/modelcomponent.h`, `meshblock.h`, `meshbone.h`, `meshface.h`,
`shadowblock.h`, `meshmaterial.h`, `materialname.h`, `recordoffset.h`.

### 5.1 File layout

```
0x00   FileHeader                                     (0x30 bytes)
0x30   short lodCount
0x32   float lodThresholds[5]                         (20 bytes, always five)
       ModelComponent record                         int size then size-4 bytes
       material block                                uchar count, char file[16],
                                                      count * MaterialName (0x10),
                                                      count * MeshMaterial (0x16),
                                                      short textureCount,
                                                      textureCount * MaterialName (0x10)
       per LOD level (lodCount of them):
         unsigned int recordCount; int totalBytes     (ComponentFileHeader, 8 bytes)
         recordCount * MeshBlock                      each opens with its own int size
```

Every offset inside a record (component slot offsets, shadow vertex/index offsets, mesh
vertex/face offsets) is a `RecordOffset<T>`: a 16 bit byte distance from the START OF THE RECORD
THAT CONTAINS IT, not a file offset (`src/graphics/recordoffset.h`). The C# readers resolve them
against the record base and also expose the raw 16 bit values for exactness. Bone child ids are
direct slot indices, not offsets.

### 5.2 LOD header

`short lodCount` then five floats. `LODNode::Init` sizes its arrays with `LOD_LEVEL_COUNT` = 5
(`src/graphics/lodnode.h`), so the C# reader accepts 1..5. `LoadLOD` squares the used threshold
entries into the runtime near/far bands (index 1..lodCount-1); the file stores the unsquared
thresholds. Shipped models use lodCount 1 (47 per set) or 2 (126 per set), and the second LOD of
the two-level skater models carries threshold 40.0.

### 5.3 ModelComponent record

The record's first dword is its own byte size, inclusive (`ModelComponent::Read` reads the size
and then `size-4` further bytes). `ModelComponent::ReadFirst` stores that size back into the
record.

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | int size (the record's byte count) |
| 0x04 | 2 | short id; at run time `LoadLOD` overwrites it with the database's next index |
| 0x06 | 0x20 | char name[32]; the model name, duplicated from the file name in every shipped file |
| 0x26 | 2 | short subcomponent count (one per bone/slot) |
| 0x28 | 0x2c | 44 unused bytes; every shipped MDLTXT file holds zeros |
| 0x54 | 12 | Vec3 angle in DEGREES; `LoadLOD` multiplies each component by `g_flDegreesToRadians` (0.017453292f) in place |
| 0x60 | 2 | RecordOffset<ShadowBlock>; 0 = no shadow block |
| 0x62 | 2 * subcomponentCount | RecordOffset<MeshBoneDesc>, one per slot |

The C# `ModelComponent` exposes `AngleDegrees`, `AngleRadians`, `Unused`/`UnusedAllZero`,
`ShadowOffsetBytes`, `SlotOffsetBytes` and the resolved `SlotDescriptors`.

### 5.4 MeshBoneDesc

`src/graphics/meshbone.h`. One variable-length record per slot, addressed from the component's
slot offset table:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 2 | short link id |
| 0x02 | 2 | short child count |
| 0x04 | 12 | Vec3 rest translation |
| 0x10 | 12 | Vec3 scale (per axis) |
| 0x1c | 4 | int unused / unresolved (see section 14) |
| 0x20 | 2 * child count | short child link ids |

`MeshBone::PropagateTransforms` (HYPERX 0x0046ef10) multiplies the posed translation by the
scale after the animation has been applied (`m_local.SetTranslation(m_local.m_a[n][3] *
pDesc->m_vScale.n, ...)`), and applies scale to the world matrix after the children, so it is
never inherited. When an object has no animation, `MeshBone`'s bind path sets the local
translation straight from `m_vRest`.

Invariants verified over every shipped model in both sets: the link id equals the slot's own
index; there is exactly one root; the child counts sum to subcomponentCount - 1; every child id is
in range. The 16-slot skater rigs share one hierarchy; the drome/prop rigs have one, three, five
or nine slots. The C# reader validates all of this and throws otherwise.

### 5.5 ShadowBlock

`src/graphics/shadowblock.h`. Begins at the component's shadow offset:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 2 | short face count |
| 0x02 | 2 | short vertex count |
| 0x04 | 2 | RecordOffset<Vec3> vertex array (relative to the ShadowBlock start) |
| 0x06 | 2 | RecordOffset<short> index array, one component link id per vertex |
| 0x08 | 0x18 * face count | ShadowFace records |

A `ShadowFace` is a `MeshFace` whose corner array runs on to four: 0x10 byte MeshFace header
(corner count, flags, material, normal) then four shorts of corners, 0x18 bytes total
(`STATIC_ASSERT(sizeof(ShadowFace) == 0x18)`). The shipped shadow faces are triangles stored in
the four-corner form. 86 MDLTXT and 85 MDLSHD models carry a shadow block; its vertices and
indices live inside the component record after the faces.

### 5.6 Material block

`ModelDatabase::ReadMaterials` reads, in order:

* `unsigned char materialCount`
* `char materialFileName[16]` - the `.mat` name the model would load as a sibling; no `.mat`
  file ships on this disc and the models embed their materials instead
* `materialCount * MaterialName` (16 bytes each) - a name table the loader seeks past; the
  shipped files carry material names there but the runtime never reads them
* `materialCount * MeshMaterial` (22 bytes each)
* `short textureCount`
* `textureCount * MaterialName` (16 bytes each) - the texture name table

`MeshMaterial` (`src/graphics/meshmaterial.h`, `#pragma pack(push, 2)`,
`STATIC_ASSERT(sizeof(MeshMaterial) == 0x16)` - the stride is 22 where natural packing would give
24):

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | int colour, 0xAARRGGBB; the low byte is a palette index on the software paths |
| 0x04 | 4 | float ambient |
| 0x08 | 4 | float diffuse |
| 0x0c | 4 | float emissive |
| 0x10 | 4 | int texture index, ONE BASED into the texture name table, 0 = none; at run time `CreateMaterialList` replaces it with the loaded cache entry |
| 0x14 | 2 | short back index; negative = no back material, non-negative makes the face two sided |

`MeshMaterial::ParseFile` (HYPERX 0x004364e0) documents the `.mat` text form (`newmtl`,
`tx`, `bf`, `Cr`, `Cp`, `Ca`, `Cd`, `Ce`) but no `.mat` file ships, so the asset layer does
not parse that text format; the embedded records above are the shipped surface.

### 5.7 LOD groups and MeshBlock

`LinkComponents` reads one `ComponentFileHeader { unsigned int recordCount; int totalBytes; }`
per LOD level, then that many MeshBlock records. Each record opens with its own int size
(inclusive). The loader requires the record sizes to sum exactly to `totalBytes` and stores each
block at `pSlots[block.slot]`; the C# reader enforces the same and additionally rejects a slot
outside the component's subcomponent count (the original would write out of bounds) and a
duplicate slot.

`MeshBlock` (`src/graphics/meshblock.h`, `#pragma pack(push, 1)`,
`STATIC_ASSERT(sizeof(MeshBlock) == 0x24)`):

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | int size |
| 0x04 | 2 | short slot: the destination index in the LOD's slot array |
| 0x06 | 2 | short face count |
| 0x08 | 2 | short vertex count |
| 0x0a | 2 | short normal count |
| 0x0c | 2 | short coord count (called `nCoordBytes`, but it counts TexCoord pairs) |
| 0x0e | 12 | Vec3 origin (unaligned) |
| 0x1a | 4 | float depth bias |
| 0x1e | 2 | unsigned short flags; bit 5 asks the renderer for the depth sort (`SortsFaces`) |
| 0x20 | 2 | RecordOffset<Vec3> vertex array |
| 0x22 | 2 * face count | RecordOffset<MeshFace>, one per face |

The geometry arrays live at `vertexOffset` from the block start, in this order:
`vertexCount` Vec3 positions, `normalCount` Vec3 normals, `coordCount` TexCoord pairs
(`struct TexCoord { float tu; float tv; }`, 8 bytes - `src/graphics/texcoord.h`). The face
records may sit before or after that data; only the face offset table locates them. All shipped
blocks end exactly at the last TexCoord; the C# reader requires the data to fit the record and
does not require it to end it, so a future release with padding still loads.

Important naming trap: the decompilation calls the field `nCoordBytes` and the model database
does `new char[nCoordBytes]` as a per-index scratch array, but `MeshBlock::Coords()` returns
`TexCoord*` at `Vertices() + vertexCount + normalCount` and `RescaleSoftTexCoords` indexes it
with the face's UV index. The field is therefore the TexCoord COUNT. Every shipped block
confirms this: `vertexOffset + (vertexCount + normalCount) * 12 + coordCount * 8 == block size`.

The normal pool is stored but never read by either shipped renderer: the software path lights
each face with `MeshFace::Normal`, and the 20XX hardware mesh builder recomputes smooth corner
normals. It is exposed raw.

### 5.8 MeshFace

`src/graphics/meshface.h` (`#pragma pack(push, 1)`):

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 2 | short corner count |
| 0x02 | 1 | unsigned char flags: 0x20 = has UVs, 0x10 = UV skip (a second corner block is present) |
| 0x03 | 1 | char material index into the model's material array |
| 0x04 | 12 | Vec3 face normal |
| 0x10 | 2 * corner count | short corner indices into the block's vertex array |
| ... | 2 * corner count | only when flag 0x10 is set: a second corner block, purpose unresolved (section 14) |
| ... | 2 * corner count | only when flag 0x20 is set: short indices into the block's TexCoord array |
| ... | 2 | only when flag 0x20 is set: short "scaled width" (the texture width less one the UVs were last scaled to) |

Shipped faces have 3 to 9 corners in both sets; flags are exactly 0x00, 0x10 or 0x30 (0x10 and
0x30 both carry the second corner block; no shipped face carries 0x20 alone). The corner order is
the original's polygon order; the 20XX hardware mesh builder (`src/graphics/hwmesh.cpp`,
`HwMesh::Build`) fans a face as triangles `(0, n, n-1), (0, n-1, n-2), ...` for the front and the
reverse for a two-sided back material, which is the reference for Unity triangle winding in the
next milestone.

### 5.9 The two model sets: coverage

Both `Models\Mdltxt` (textured) and `Models\Mdlshd` (shaded) ship 173 `.wbm` files each.

| Measure | Mdltxt | Mdlshd | Total |
| --- | --- | --- | --- |
| `.wbm` files | 173 | 173 | 346 |
| Loaded | 173 | 171 | 344 |
| Known corrupt (skipped, see below) | 0 | 2 | 2 |
| lodCount 1 / 2 | 47 / 126 | 47 / 124 | 94 / 250 |
| Material records | 2319 | 1209 | 3528 |
| Bone/slot descriptors | 2107 | 2075 | 4182 |
| Mesh blocks (all LODs) | 3871 | 3811 | 7682 |
| Vertices | 57752 | 61166 | 118918 |
| Normal-pool entries | 83682 | 76299 | 159981 |
| TexCoords | 60185 | 41163 | 101348 |
| Faces | 40652 | 42565 | 83217 |
| Texture-name table entries | 2130 | 0 | 2130 |
| Models with a shadow block | 86 | 85 | 171 |
| `.wbp` palettes / entries | 28 / 1344 | 28 / 1344 | 56 / 2688 |

The two damaged shipped files are `Mdlshd\chisom.wbm` and `Mdlshd\nemptm.wbm`. The
decompilation's own checker lists exactly these
(`third_party/hyperblade-decomp/tools/check_model_files.py`, `KNOWN`) and 20XX's renderer notes say
"Two `Mdlshd` files are corrupt, CHISOM and NEMPTM"
(`third_party/hyperblade-20XX/docs/hardware_renderer.md`). `chisom.wbm` has an LOD 0 record 1
whose size word is garbage (negative when read as int); `nemptm.wbm` has an LOD 0 record 8 with
size 853185. The Mdltxt copies of both names load normally. The C# probe records them as
shipped-data warnings and skips the files; any other unreadable model is fatal.

Texture references: 2132 shipped material records name a texture (texture index > 0); the 148
empty entries in the texture name tables are never referenced. All 311 distinct referenced names
resolve to a `Data\Textures\<name>.bmp` file. `ModelDatabase::CreateMaterialList` would raise
"Error reading activating texture" for a miss, so the reader rejects a referenced empty name and
the probe rejects an unresolved file.

## 6. WBP palette definition (not animation)

`.wbp` is a PALETTE definition, not animation data. `src/globals.cpp` defines
`g_pszPaletteExtension = "wbp"`; `ColorPalette::LoadPaletteEntries` (HYPERX 0x004264a0) opens the
file with `fopen(path, "r")` and reads text:

1. `fscanf("%d %d", &version, &count)` on the first line, then skip to the end of the line.
2. `count` rows of `fscanf("%u %u %u %u", &index, &red, &green, &blue)`, each followed by a
   skip to the end of its line. A row replaces palette entry `index` (0..255).

Runtime version rules: `ColorPalette::ColorPalette` sets `m_wVersion = 0x303`;
`LoadPaletteEntries` accepts either that or `PALETTE_VERSION_ANY = 0x302` (and logs an error but
continues otherwise). Every shipped `.wbp` begins with `770 064` or `770 032`, i.e. version 770
decimal = 0x302, the "always" version. The files carry 64 entries (the fourteen drome models,
`*dro.wbp`) or 32 entries (the fourteen goalie/drome-extra models, `*gmx.wbp`), 2688 rows
altogether.

`EngineObject::InitModel` (HYPERX 0x00470790) calls `LoadPaletteEntries(pDef->GetModel(), bRemap)`
with the active model path, which is why the palettes live beside the models and are named after
them. The remap (runtime palette swap between team slots) is a palette-table operation, not a file
field; the reader exposes the raw entries and their indices.

## 7. WBA animation data

Read path: `AnimFile::Open` (HYPERX 0x00493cb0) validates tag 4 and version 1.1.0.0.0, then
`AnimSet::LoadIntoRegistry` (HYPERX 0x00417220) builds an `AnimClipSet` whose `Load`
(HYPERX 0x004172d0) reads the set header and one `AnimClip` per bone through `AnimClip::Load`
(HYPERX 0x00416510). The runtime only ever resolves a set by name and indexes clips by bone index
(`AnimPlayer::UpdateFrame` line 299: `pClip = &m_pBoneSet->m_pClips[nBone]`).

### 7.1 Layout

```
0x00   FileHeader                                   (0x30 bytes)
0x30   AnimHeader                                   (0x0e bytes)
       clipCount * AnimClip:                        (0x12 byte header, then nDataSize bytes)
         unsigned short segment
         unsigned short frame
         short dataSize
         char axisOrder[3]
         char unused[5]
         int format
         nDataSize bytes of float data
       trailing bytes (unread by the loader; see 7.5)
```

### 7.2 AnimHeader

`src/anim/animclip.h` `class AnimHeader`, `STATIC_ASSERT(sizeof(AnimHeader) == 0x0e)`:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 2 | short clip count |
| 0x02 | 2 | short frame count |
| 0x04 | 2 | short frame rate (copied, never read by the shipped code) |
| 0x06 | 4 | float duration = frameCount / 120 on every shipped set, whatever the frame rate |
| 0x0a | 2 | short unused |
| 0x0c | 2 | short end frame = frameCount - 1 on every shipped set |

### 7.3 AnimClip and the eight formats

Formats as `AnimClip::Load` / `GetBoneTransform` (`src/anim/animclip.cpp`) decode them:

| Format | Data layout inside nDataSize | Shipped clips |
| --- | --- | --- |
| 1 | frameCount Vec3 translations, nothing else | 186 |
| 2 | one Vec3 translation, then frameCount triples of Euler angles | 2808 (no format 4 ships; the probe counts formats 2 and 4 together as animationsFmt2) |
| 3 | frameCount Vec3 translations, then frameCount Euler triples | 1 |
| 4 | same as 2 (the loader groups them) | 0 |
| 5 | one Vec3 translation; loader allocates the quaternion array and never fills it | 0 |
| 6 | frameCount Vec3 translations; quaternion array allocated and never filled | 0 |
| 7 | one translation and a per frame 3x3 matrix (PackedBoneMatrix); loader raises "ANIM SETUP ERROR: ONE_TRAN_3x3 format not yet support!" | 0 |
| 8 | per frame translation and matrix; "ALL_TRAN_3x3 format not yet support!" | 0 |

The Euler triples are in DEGREES and are applied last to first according to `acAxisOrder`
(`Anim::EulerToMatrix` HYPERX 0x00416ba0 multiplies by 0.017453292f). Shipped axis orders by
format: format 1 clips carry "zyx" (translation only, the letters are unused); format 2 clips
carry "yzx" (1309), "zyx" (377) or "xyz" (1122); the single format 3 clip carries "zyx". Sizes the
loader reads, and the C# reader enforces, for the implemented formats:
`format 1 = frameCount * 12`, `formats 2/4 = 12 + frameCount * 12`,
`format 3 = frameCount * 24`.

`AnimClip::GetBoneTransform` builds a bone local transform: translation from the clip (the single
translation for formats 2/4/5/7, per frame for 1/3/6/8), rotation from the packed Euler matrix.
`MeshBone` then multiplies the translation by `MeshBoneDesc.Scale`. The C# reader deliberately
exposes keyframes only (translations and Euler degrees); it does not interpolate, which matches
the milestone ("do not invent interpolation yet"). The next milestone can build matrices with the
same `EulerToMatrix` order.

`segment` and `frame` in each clip header are loaded but never read by the shipped player. In
every shipped set the segment equals the clip index (0..15 for the 16-bone skater sets, 0..2 for
`gunanim.wba`), and `frame` is a small offset in 0..5. They are exposed raw for whoever needs the
original metadata; the assembly script below is the real source of ranges.

### 7.4 Shipped coverage

188 `.wba` sets, 2995 clips, 9086 frames. 187 sets carry 16 clips (one per bone);
`gunanim.wba` carries 3. Frame counts range from 3 to 165. Frame rates are 30 (184 sets), 36 (1:
`bpphandc.wba`), 60 (2: `bpptrn5c.wba`, `bpptwrlc.wba`) and 2 (1: `gunanim.wba`); duration is
always frameCount/120 and end frame always frameCount-1. All 188 files carry tag 4 and version
1.1.0.0.0, and every clip uses a format the shipped loader implements (1, 2 or 3).

### 7.5 Trailing bytes

`AnimClipSet::Load` reads exactly clipCount clips and never checks for end of file. 128 of the 188
shipped sets leave 85 unread bytes after the last clip: sixteen "DONE " tokens followed by "DONE"
and a NUL. They are a compiler leftover and are ignored at run time. The C# `AnimationClipSet`
exposes `TrailingBytes`/`TrailingAscii`; the probe counts them but does not warn.

## 8. AnimAssm.txt: assembly script, ranges and events

`Data\AnimData\AnimAssm.txt` is the animation assembly script the game loads at start-up:
`src/engine/game.cpp` line 162 calls `g_pAnimRegistry->LoadFromPath("animdata\\AnimAssm.txt")`,
and `AnimScriptTable::ParseScript` (HYPERX 0x00418210) walks it with `ParseScriptLine`
(HYPERX 0x004183e0), `ParseAndAddAnimDB` (HYPERX 0x00418540), `ParseSegment` (HYPERX 0x00418670)
and `ParseTrigger` (HYPERX 0x004189c0). The only token delimiter is a tab (`src/globals.cpp`
`g_pszAnimTokenDelimiters = "\\t"`), and the number of leading tabs selects the line shape. The
file's own header documents the same three shapes:

```
Assemblies:  <tab>Name<tab>Description
Segments:    <tab><tab><tab>Filename<tab>Description<tab>Start<tab>End<tab>Slow<tab>Standard<tab>Trans. Fact.<tab>Loop
Triggers:    <tab><tab><tab><tab>SegmentIndex<tab>FrameNumber<tab>ActionName<tab>[Sync assembly<tab>Sync SegmentIndex<tab>Sync Frame Number]
```

`ParseSegment` reads file name, description, then `ScanShort` start, `ScanShort` end,
`ScanFloat` slow, `ScanFloat` standard, `ScanFloat` fast, `ScanShort` loop. The third float is
what the file header calls "Trans. Fact." and what the loader stores in `AnimNode::m_flFastFps`;
shipped values are 0.10. The loader clamps slow to standard when slow > standard.
`ParseTrigger` reads segment index and frame (shorts), the action name, then optionally a sync
assembly name plus sync segment and frame; a sync entry becomes an `AnimNamedRef` on the
`AnimClipEntry` (`AnimDB::AddClipEntry` HYPERX 0x004176d0).

Measured: 249 assembly lines (248 distinct names; `AA_GldtoStnd` is written twice), 388 segment
lines, 103 trigger lines of which 10 carry a sync entry. The 388 segments name 188 distinct
`.wba` files - exactly the 188 files in `Data\AnimData` - so every segment resolves. A trigger's
segment index is an index into its own assembly's segment list.

C# reader: `AnimationAssemblyReader.LoadFile` (text) -> `AnimationAssemblyFile` ->
`AnimationAssembly` with `Segments` (ranges and rates) and `Triggers` (the animation events).
It validates short/float fields and every line shape, and throws with the line number on a
malformed line.

## 9. BMP textures

Read path: `GDI::ReadBitmapFile` (HYPERX 0x0043fff0) and `GDI::ReadDIB` (HYPERX 0x00440200) for
the general bitmap; `CD3DTexture::LoadBitmapFile` (HYPERX 0x00435310) requires 24 bits per pixel,
and `SoftTex::Load` (HYPERX 0x00492920) requires 8 bits per pixel. The `mdmtex.dll` that would
have let the software renderer draw textures never shipped (20XX `docs/hardware_renderer.md`), so
the Direct3D/20XX path is the one that matters for models.

### 9.1 Accepted shapes

`GDI::ReadBitmapFile` accepts three shapes, and the C# `BmpReader` implements all three:

1. An MDM1 packed file: tag dword then declared size; the decompressed payload is a raw DIB
   (starts directly with `BITMAPINFOHEADER`, no `BM` file header). This is every `assets8` file.
2. A `BM` file: 14 byte `BITMAPFILEHEADER` (bfType, bfSize, reserved, reserved, bfOffBits), then
   the DIB. `GDI::ReadDIB` treats the file header as optional: if the first word is not "BM" it
   rewinds and reads a DIB from offset 0.
3. A raw DIB.

### 9.2 DIB headers and palette

The DIB starts with a size word: 12 = OS/2 `BITMAPCOREHEADER` (`bcWidth`, `bcHeight`,
`bcPlanes`, `bcBitCount` as 16 bit words, RGBTRIPLE palette), anything >= 40 =
`BITMAPINFOHEADER` (`biWidth`, `biHeight`, `biPlanes`, `biBitCount`, `biCompression`,
`biSizeImage`, ..., `biClrUsed`). `ReadDIB` widens an OS/2 header to the Windows layout.
Palette size is `biClrUsed` when nonzero, else `1 << biBitCount` for <= 8 bits, else 0. Windows
rows are bottom up when `biHeight` is positive, top down when negative. The pixel pointer is
`ColorTable + entries * sizeofentry`; the C# reader also honours a nonzero `bfOffBits` exactly as
`ReadDIB`'s final seek does. For `BI_RGB` the row stride is
`((width * bitCount + 31) / 32) * 4`; for `BI_RLE8`/`BI_RLE4` the header's `biSizeImage` bounds
the stream.

The C# reader supports `BI_RGB` at 1, 4, 8, 24 and 32 bits per pixel, `BI_RLE8` and `BI_RLE4`
(the RLE commands 0 end-of-line, 1 end-of-bitmap, 2 delta, 3+ absolute runs with word padding),
paletted and direct colour, bottom-up and top-down. `BI_BITFIELDS` is not shipped and is rejected
with a clear error (section 14). Output is RGBA with alpha always 255 (the original never stores
or uses an alpha channel for model textures); `Indices` keeps the palette indices for paletted
images; `RgbaBottomUp` gives the row order a Unity `Texture2D` expects (Unity's texture origin is
the bottom left, matching the original's bottom-up DIB rows).

### 9.3 Data\Textures: the 24-bit set

333 files, all 24 bits per pixel, `BI_RGB`, 40 byte `BITMAPINFOHEADER`, `bfOffBits = 54`,
`biSizeImage = 0`, positive height (bottom up), square powers of two: 16x16 (22), 32x32 (180),
64x64 (80), 128x128 (49), 256x256 (2); 1,451,520 pixels total.

A shipped quirk: every one of the 333 files is exactly two bytes longer than
`54 + rowStride * height`, and those two bytes are zero. `GDI::ReadBitmapFile` computes
`biSizeImage` from the header and reads only that many bytes, so the two bytes are never read.
The C# `BmpTexture.TrailingBytes` reports 2 for every one of them (verified).

### 9.4 Data\assets8: MDM1 8-bit bitmaps

1767 files, every one MDM1 packed; each decompresses to a raw DIB. Composition:
1759 are 8 bits per pixel `BI_RGB` with a 256 entry colour table; 1 is 8 bits per pixel `BI_RGB`
with a declared 257 entry table (`jumscrn.bmp`); 3 are 8-bit `BI_RLE8` (`bpowrbar.bmp`,
`bshotpwr.bmp`, `network.bmp`); 2 are 4-bit `BI_RLE4` (`blogoscr.bmp`, `brlogscr.bmp`); 2 are
24-bit `BI_RGB` (`objlas.bmp`, `torhud.bmp`). Four decompressed DIBs carry two unread trailing
zero bytes (`final512.bmp`, `jumscrn.bmp`, `objlas.bmp`, `torhud.bmp`); the rest end exactly at
the pixel data.

These bitmaps are read through the same `BmpReader`; the probe loads all 1767 and cross-checks the
decoded palette indices and RGBA against an independent decoder. `SoftTex` treats palette index
255 as transparent for its RLE sprite path, which is why the shipped sprite bitmaps use 255 as the
transparent colour; the asset reader exposes the indices so a future sprite path can apply that
rule. The model texture path never uses these files.

## 10. Disk catalogues

### 10.1 model.lst

26 names, one per line: `chio, JAMdrome, vegdrome, NYdrome, houso, jamo, lao, lono, moso, muno,
nemo, nyo, seao, toko, toro, waro, offense, offense2, offense3, offense4, offense5, vegmno,
ladrome, ramp0, goalStub, rok`.

This file is a development-time catalogue, not a runtime list: its name does not occur as a string
in `HYPERX.EXE`, `HB3DFxV2.EXE`, `HyperX-XP.exe` or `hyperblade_20XX.exe`, and the
decompilation has no consumer for it. It is reported for completeness and the probe records how
its names relate to the shipped WBM names: 1 matches a file name exactly (`goalStub` ->
`goalstub.wbm`), 11 are only a prefix of a name (`chio`, `houso`, `jamo`, `moso`, `muno`,
`nemo`, `seao`, `toko`, `toro`, `waro`, `rok`), and 14 match nothing (`JAMdrome`, `vegdrome`,
`NYdrome`, `lao`, `lono`, `nyo`, `offense` through `offense5`, `vegmno`, `ladrome`,
`ramp0`). The set membership cannot be reconstructed from the shipped files; it is recorded as a
shipped catalogue gap rather than guessed.

### 10.2 animate.lst

97 non-empty lines, 96 distinct names (`apstmth2` appears twice): a name list of animation sets.
Like `model.lst` it is not read by any shipped executable (no string in the executables, no
decompilation consumer). 3 names have no `.wba` on the disc (`apkskat2`, `apgglid2`,
`apgchip2`). Recorded as a shipped catalogue gap.

### 10.3 Reaction lists

`Data\HitAnims.lst`, `ChkAnims.lst`, `JmpAnims.lst`, `ThrAnims.lst`: five lines of seven tab
separated animation assembly names each (140 names). `PlanVM::InitReactionTables`
(`src/planvm/planvm.cpp` lines 389-456) reads exactly five lines and resolves each name through
the animation registry; a line that does not yield seven names is fatal. Four shipped names have
no assembly (typos in the data): `AA_HtHMdFrnt` (hitanims), `AA_Kicklef` (chkanims),
`AAA_Jmpmthd` (jmpanims), `AA_HtDnFrnt` (thranims); `FindAnimByNameIdx` would answer -1 for them.
Recorded as a shipped data defect.

### 10.4 Model references

The 14 `*.env` and 14 `*.htm` files contain 980 `modfile=` references to 114 distinct model names
(excluding `null`). Every distinct name exists as a `.wbm` file in BOTH model sets. The asset
probe re-verifies this independently of the data layer's object parser and fails if a reference
has no file.

## 11. C# API

All types are in namespace `UBlade3.Assets`, under
`unity/UBlade3/Assets/Scripts/UBlade3/Assets`. The readers take bytes or text and do not use
UnityEngine; only `AssetProbe` references `UBlade3.Data.GameDataRoot`, to locate the install.

| File | Public surface |
| --- | --- |
| `AssetFormatException.cs` | `AssetFormatException` with source name and byte offset |
| `AssetMath.cs` | `AssetVector3`, `AssetVector2` |
| `Mdm1.cs` | `Mdm1.HasMagic/Decompress/TryDecompress` |
| `ModelFile.cs` | `FileVersion`, `FileHeader`, `MaterialName`, `MeshMaterial`, `MeshBoneDesc`, `ShadowBlock`, `ShadowFace`, `ModelComponent`, `MeshFace`, `MeshBlock`, `ModelLod`, `WbmModel`, `WbmReader.Load/LoadFile` |
| `PaletteFile.cs` | `WbpPaletteEntry`, `WbpPalette`, `WbpReader.Load/LoadFile` |
| `AnimationFile.cs` | `AnimationHeader`, `AnimationClip`, `AnimationClipSet`, `WbaReader.Load/LoadFile` |
| `AnimationAssembly.cs` | `AnimationAssemblySegment`, `AnimationAssemblyTrigger`, `AnimationAssembly`, `AnimationAssemblyFile`, `AnimationAssemblyReader.Load/LoadFile` |
| `BmpTexture.cs` | `BmpPaletteEntry`, `BmpTexture`, `BmpReader.Load/LoadFile/UnwrapMdm1` |
| `AssetProbe.cs` | `AssetProbe.Describe/Check/Run/Report/Reset/Digest/LastError` plus expected-count constants |
| `AssetBytes.cs`, `AssetText.cs` | internal bounds-checked byte reader and Latin-1 text helpers |

Typical use:

```csharp
WbmModel model = WbmReader.LoadFile(path);
ModelLod lod = model.Lods[0];
MeshBlock block = lod.BySlot[slot];
// positions: block.Vertices[face.Corners[i]]
// uvs:       block.Coords[face.UVIndices[i]]      (when face.HasUV)
// normal:    face.Normal (flat) or recompute smooth
// bones:     model.Component.SlotDescriptors[slot] -> Rest, Scale, ChildIds, LinkId

BmpTexture texture = BmpReader.LoadFile(texturePath);   // RGBA, or RgbaBottomUp for Unity
AnimationClipSet set = WbaReader.LoadFile(wbaPath);
float degrees = set.Clips[bone].EulerAt(frame, axis);   // formats 2/3/4
WbpPalette palette = WbpReader.LoadFile(wbpPath);
AnimationAssemblyFile script = AnimationAssemblyReader.LoadFile(animAssmPath);
```

Malformed input is always an `AssetFormatException` naming the file and offset; the readers never
fall back to placeholder geometry or textures.

## 12. AssetProbe output contract and verified run

`AssetProbe` is scene free. `Check()` prints the report and throws on a failed load or a count
mismatch; `Run()` is the same as a process exit code; `Describe()` prints one line; `Report()`
returns the full text. It loads every model in both sets, every material, bone, mesh block,
texture, MDM1 stream, animation set, palette, assembly script and name list, then prints:

```
UBLADE3_ASSETS_OK models=344 materials=3528 bones=4182 textures=333 animations=188 digest=d860fe66423aa141
UBLADE3_ASSETS_DETAIL data=<Data path>
UBLADE3_ASSETS_COUNTS <key=value ...>
UBLADE3_ASSETS_DIGEST models=d03b61aeb34f4e0e materials=bbabbc8c0d83550f textures=8834e43bd839cbd5 animations=d1d2faa58a018153 catalogs=420f37b22e0b66d3 total=d860fe66423aa141
UBLADE3_ASSETS_WARNING <text>        (one per shipped-data defect)
UBLADE3_ASSETS_WARNINGS 5
UBLADE3_ASSETS_CHECK mismatches=0
```

The digest is FNV-1a 64 over a canonical stream of every parsed primitive (floats through their
bit patterns, so it is exact and platform stable); the total hashes the five category digests. The
probe also compares every count with the constants measured on this disc and reports
`UBLADE3_ASSETS_EXPECTED_MISMATCH` lines (which make `Check()` fail) when the data or the parser
changes.

Verified run against `D:\Games\HyperBlade\HYB` (2026-10-30):

* `UBLADE3_ASSETS_OK ... digest=d860fe66423aa141`, `UBLADE3_ASSETS_WARNINGS 5`,
  `UBLADE3_ASSETS_CHECK mismatches=0`, 2.6 s wall clock.
* The five warnings are exactly the shipped defects documented above: the two Mdlshd short
  records, `model.lst`'s catalogue names, `animate.lst`'s three missing `.wba` names, and the
  four reaction-list names with no assembly.
* A 20-case self test also passed: truncated model/animation/bitmap streams, a directory-tag
  model, a wrong version, an MDM1 size mismatch and a 16-bit bitmap all throw; all 173 + 171
  models, 188 animations, 333 textures and 1767 assets8 bitmaps load; two fresh reports are byte
  identical.

Counts printed by the run:

```
models=344 (Mdltxt 173, Mdlshd 171, skipped 2)   meshBlocks=7682   vertices=118918
faces=83217   materials=3528   bones=4182   palettes=56/2688   textures=333/1451520 px
textureRefs=311   mdm1=1767 (paletted 1765, direct 2)   animations=188/2995 clips/9086 frames
assemblies=249 segments=388 triggers=103   modelList=26 (1 exact, 11 prefix, 14 none)
animateList=97/96 unique/3 missing   reactionNames=140/4 missing   modelRefs=980/114
```

## 13. Notes for the Unity mesh milestone

* Positions and normals are in model units; the file stores no global unit conversion. The bone
  `Scale` field is the animation-to-model translation conversion, not a mesh scale: apply it to
  the animation translation exactly as `MeshBone::PropagateTransforms` does.
* A face is a polygon with 3 to 9 corners. Fan it as `(0, n, n-1), (0, n-1, n-2), ...` for the
  front orientation the 20XX hardware builder uses; a material with a non-negative
  `MeshMaterial.BackIndex` is two sided and its back copy is the same fan reversed, lit with the
  back material and a negated normal. See `third_party/hyperblade-20XX/src/graphics/hwmesh.cpp`
  `HwMesh::Build` and `tests/hwmesh_check.cpp`.
* UVs are normalised texcoords indexed per corner; the stored `ScaledWidth` is a software-renderer
  rescale value, not a normalised scale. Unity should use the TexCoord values directly.
* Flat shading uses `MeshFace.Normal`; smooth shading recomputes corner normals across co-sited
  faces with the original crease threshold (the 20XX builder is the reference), because the
  stored normal pool is not what the shipped renderers used.
* Textures: `BmpTexture.RgbaBottomUp` matches Unity's bottom-left texture origin; textures are
  opaque and square powers of two. The 20XX hardware path uses stb_image for the same 24-bit
  files.
* LOD selection: `ModelLod` index is the level; the file's two thresholds are unsquared model
  units (the runtime squares them with a 5.0 hysteresis). Each level's blocks are already indexed
  by slot in `ModelLod.BySlot`.
* Two-sided/back materials, palette remapping (`MeshMaterial.Colour` low byte through the
  `.wbp`/team palette ranges) and the shadow block are all exposed but not yet consumed by a
  renderer.

## 14. Unresolved fields and known gaps

1. `MeshBoneDesc` 0x1c int (labelled "unused" by both references). On the skater rigs its bits
   decode as tiny negative floats; nothing in either source reads it. Exposed as `Unused`.
2. `MeshBlock` normal pool: stored and skipped over to find the TexCoords, but neither shipped
   renderer reads it. Its purpose (possibly a smooth-normal pool for the never-shipped
   `mdmtex.dll` path) is unknown. Exposed raw as `Normals`.
3. `MeshFace` second corner block (flag 0x10): present on 183 mdltxt faces (flag 0x10 without UV)
   and 39968 (flag 0x30 with UV); no shipped renderer reads it. Exposed raw as
   `SecondCorners`; its meaning is unresolved.
4. Animation formats 5-8: never shipped. The loader allocates the quaternion array and never fills
   it (5, 6) or raises (7, 8), so their byte layouts cannot be established from working data. The
   C# reader exposes the raw stream and translations for them and marks
   `HasUsableTransforms = false`.
5. `BI_BITFIELDS` bitmaps: not shipped (the shipped disc uses `BI_RGB`, `BI_RLE8` and
   `BI_RLE4`); the reader rejects them with a clear error. 32-bit `BI_RGB` is decoded as opaque
   RGB (the fourth byte is ignored, as the original does).
6. `model.lst` and `animate.lst` set semantics: both files ship but no shipped executable reads
   them and no decompiled code consumes them, so their intended sets cannot be reconstructed.
   Parsed and reported, with resolution counts, but not invented.
7. `.mat` material-list text format: `MeshMaterial::ParseFile` documents it, but no `.mat` file
   ships (all shipped materials are embedded in the models), so it is not implemented.
8. The 148 empty entries in the shipped texture-name tables are unreferenced by materials. The
   reader tolerates them; a *referenced* empty name is a fatal format error.
9. Material ramp codes (`r8`/`r16`) and palette remap ranges are runtime palette behaviour, not
   model fields; only the raw material records and palette files are exposed here.

## 15. Verification performed

* C# 9 compile (LangVersion 9.0, `dotnet build`): succeeds with 0 warnings and 0 errors against
  both `net9.0` and `netstandard2.1` (the Unity .NET Standard 2.1 profile), together with the
  existing `UBlade3.Data` sources.
* `AssetProbe` run against the retail install: section 12 output, `mismatches=0`, digest stable
  across two fresh runs.
* A 20-case parser self test: all shipped files load, all known corrupt inputs reject, counts
  match, and two reports are byte identical.
* Independent cross-checks against a separate Node implementation of the same formats: the 24-bit
  BMP RGBA hash, the 8-bit palette indices/RGBA hash, the RLE8 and RLE4 hashes, WBM geometry
  (vertices, normals, UVs, face corners, bone fields, shadow vertices), WBA format-3 translations
  and Euler angles, and the MDM1 payload hash all match the C# reader byte for byte.
* I did not run a Unity batch-mode build (another process may own the project, and opening the
  editor would write `.meta` files outside this task's write scope). However, a concurrent Unity
  6000.6.7f1 editor import in this checkout (another teammate's process) generated the script
  `.meta` files and rebuilt `Library/ScriptAssemblies/Assembly-CSharp.dll` at 04:55:31Z, after
  the newest asset source at 04:55:04Z; that assembly contains the `AssetProbe` markers and the
  expected-digest constant, and `Logs/*.log` has no compile-error lines. The explicit
  verification below is the `dotnet` C# 9 build against both `net9.0` and `netstandard2.1`.

## 16. Legal

The original game data is read from the user's own HyperBlade install and is never copied into
this repository. `hyperblade-decomp` and `hyperblade-20XX` are read-only references for format
research; the C# code here is written from that research.
