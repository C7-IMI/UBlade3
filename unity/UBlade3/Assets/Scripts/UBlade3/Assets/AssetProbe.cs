using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBlade3.Assets
{
    // The asset gate: loads the original HyperBlade model, animation, palette and texture files
    // from the player's own install and prints a stable, scene independent report. No UnityEngine
    // type is used. Batch mode can call Check() (for example through an -executeMethod hook);
    // Describe() is the one line form a port scene can print.
    //
    // The game root is located through UBlade3.Data.GameDataRoot, which owns the retail-install
    // contract: HYPERBLADE_GAME_DATA, then Assets/StreamingAssets/game-data-path.txt, then a
    // caller supplied default. Nothing from the original game is committed.
    //
    // Output contract:
    //   UBLADE3_ASSETS_OK models=<n> materials=<n> bones=<n> textures=<n> animations=<n> digest=<16 hex>
    //   UBLADE3_ASSETS_DETAIL data=<path>
    //   UBLADE3_ASSETS_COUNTS <key=value ...>
    //   UBLADE3_ASSETS_DIGEST models=.. materials=.. textures=.. animations=.. catalogs=.. total=..
    //   UBLADE3_ASSETS_WARNING <text>            (one per shipped-data defect found)
    //   UBLADE3_ASSETS_WARNINGS <n>
    //   UBLADE3_ASSETS_CHECK mismatches=<n>
    // and, instead of the first line, UBLADE3_ASSETS_ERROR <reason> when a file could not be read.
    public static class AssetProbe
    {
        public const string OkMarker = "UBLADE3_ASSETS_OK";
        public const string ErrorMarker = "UBLADE3_ASSETS_ERROR";
        public const string WarningsMarker = "UBLADE3_ASSETS_WARNINGS";

        // The model-sets catalogue. The retail executables never read Data\model.lst (its name
        // does not occur in HYPERX.EXE, HB3DFxV2.EXE or HyperX-XP.EXE, and the decompilation has
        // no consumer), so the probe records it as a catalogue and reports how many of its names
        // resolve against the shipped WBM names. All 26 entries are still read.
        public const int ExpectedModelListEntries = 26;
        public const int ExpectedModelListExact = 1;
        public const int ExpectedModelListPrefixOnly = 11;
        public const int ExpectedModelListUnresolved = 14;

        public const int ExpectedModelSets = 2;
        public const int ExpectedModelFiles = 346;
        public const int ExpectedModels = 344;
        public const int ExpectedModelsTextured = 173;
        public const int ExpectedModelsShaded = 171;
        public const int ExpectedSkippedModels = 2;
        public const int ExpectedMaterialCount = 3528;
        public const int ExpectedBoneCount = 4182;
        public const int ExpectedMeshBlocks = 7682;
        public const int ExpectedVertices = 118918;
        public const int ExpectedNormals = 159981;
        public const int ExpectedTextureCoords = 101348;
        public const int ExpectedFaces = 83217;
        public const int ExpectedTextureNameEntries = 2130;
        public const int ExpectedPaletteFiles = 56;
        public const int ExpectedPaletteEntries = 2688;
        public const int ExpectedLodOne = 94;
        public const int ExpectedLodTwo = 250;

        public const int ExpectedTextures = 333;
        public const int ExpectedTexturePixels = 1451520;
        public const int ExpectedMdm1Files = 1767;
        public const int ExpectedMdm1Paletted = 1765;
        public const int ExpectedMdm1Direct = 2;

        public const int ExpectedAnimationSets = 188;
        public const int ExpectedAnimationClips = 2995;
        public const int ExpectedAnimationFrames = 9086;
        public const int ExpectedAnimationFormat1 = 186;
        public const int ExpectedAnimationFormat2 = 2808;
        public const int ExpectedAnimationFormat3 = 1;
        public const int ExpectedAnimationTrailing = 128;

        public const int ExpectedAssemblies = 249;
        public const int ExpectedAssemblySegments = 388;
        public const int ExpectedAssemblyTriggers = 103;
        public const int ExpectedAssemblySegmentFiles = 188;
        public const int ExpectedAnimateListLines = 97;
        public const int ExpectedAnimateListUnique = 96;
        public const int ExpectedAnimateListMissing = 3;
        public const int ExpectedReactionNames = 140;
        public const int ExpectedReactionNamesMissing = 4;
        public const int ExpectedModelReferences = 980;
        public const int ExpectedModelReferenceNames = 114;

        // Set from the first verified run against the shipped disc; the probe fails when a change
        // moves it. See docs/ASSET-FORMATS.md.
        public const string ExpectedTotalDigest = "d860fe66423aa141";

        private static Scan cachedScan;

        private static string cachedReport;

        private static Exception cachedError;

        public static string Digest
        {
            get
            {
                return ExtractValue(EnsureReport(), OkMarker, "digest");
            }
        }

        public static string LastError
        {
            get
            {
                EnsureReport();

                return cachedError == null ? string.Empty : cachedError.Message;
            }
        }

        // Called by a port scene through reflection. Never throws and never needs a scene.
        public static string Describe()
        {
            string report = EnsureReport();

            if (cachedError != null)
            {
                return ErrorMarker + " " + Flatten(cachedError.Message);
            }

            string first = FirstLine(report);

            if (cachedScan.Warnings.Count > 0)
            {
                return first + " warnings=" + cachedScan.Warnings.Count.ToString(CultureInfo.InvariantCulture);
            }

            return first;
        }

        // Batch-mode entry point: print the report, then throw on a failed load or a count that
        // no longer matches the shipped data so the caller exits non-zero.
        public static void Check()
        {
            string report = EnsureReport();

            Console.Out.WriteLine(report);

            if (cachedError != null)
            {
                throw cachedError is AssetFormatException
                    ? cachedError
                    : new AssetFormatException("UBLADE3_ASSETS_FAILED: " + cachedError.Message, cachedError);
            }

            if (!report.StartsWith(OkMarker, StringComparison.Ordinal))
            {
                throw new AssetFormatException("UBLADE3_ASSETS_FAILED: " + FirstLine(report));
            }

            if (cachedScan.Mismatches.Count > 0)
            {
                throw new AssetFormatException("UBLADE3_ASSETS_FAILED: " + string.Join("; ", cachedScan.Mismatches.ToArray()));
            }
        }

        // Same as Check but answers a process exit code instead of throwing.
        public static int Run()
        {
            try
            {
                Check();

                return 0;
            }
            catch (Exception exception)
            {
                string message = Flatten(exception.Message);

                if (!message.StartsWith("UBLADE3_ASSETS_FAILED", StringComparison.Ordinal))
                {
                    message = "UBLADE3_ASSETS_FAILED " + message;
                }

                Console.Out.WriteLine(message);

                return 1;
            }
        }

        public static string Report()
        {
            return EnsureReport();
        }

        public static void Reset()
        {
            cachedScan = null;
            cachedReport = null;
            cachedError = null;
        }

        private static string EnsureReport()
        {
            if (cachedReport != null)
            {
                return cachedReport;
            }

            try
            {
                cachedScan = Build();

                cachedReport = BuildReport(cachedScan);
            }
            catch (Exception exception)
            {
                cachedScan = null;
                cachedError = exception;
                cachedReport = ErrorMarker + " " + Flatten(exception.Message);
            }

            return cachedReport;
        }

        private static Scan Build()
        {
            Scan scan = new Scan();
            UBlade3.Data.GameDataRoot root = UBlade3.Data.GameDataRoot.Load();

            scan.DataPath = root.DataPath;

            string modelsDirectory = FindChildDirectory(scan.DataPath, "models");

            if (modelsDirectory == null)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "the Data folder has no models directory");
            }

            scan.Sets = new[]
            {
                LoadModelSet(scan, modelsDirectory, "mdltxt", true),
                LoadModelSet(scan, modelsDirectory, "mdlshd", false)
            };

            LoadTextures(scan);
            LoadAnimations(scan);
            LoadMdm1(scan);
            LoadCatalogs(scan);

            Fnv1a64 models = new Fnv1a64();
            Fnv1a64 materials = new Fnv1a64();

            for (int set = 0; set < scan.Sets.Length; set++)
            {
                models.Add("SET|");
                models.Add(scan.Sets[set].Name);
                models.Add(scan.Sets[set].ModelDigest);
                materials.Add("SET|");
                materials.Add(scan.Sets[set].Name);
                materials.Add(scan.Sets[set].MaterialsDigest);
            }

            scan.ModelsDigest = models.Value;
            scan.MaterialsDigest = materials.Value;

            Fnv1a64 textures = new Fnv1a64();

            textures.Add("TEXTURES|");
            textures.Add(scan.TextureDigest);
            textures.Add("MDM1|");
            textures.Add(scan.Mdm1Digest);
            scan.TexturesDigest = textures.Value;

            Fnv1a64 catalogs = new Fnv1a64();

            catalogs.Add("CATALOGS|");
            catalogs.Add(scan.CatalogDigest);

            for (int set = 0; set < scan.Sets.Length; set++)
            {
                catalogs.Add("SET|");
                catalogs.Add(scan.Sets[set].Name);
                catalogs.Add(scan.Sets[set].PaletteDigest);
            }

            scan.CatalogsDigest = catalogs.Value;

            Fnv1a64 total = new Fnv1a64();

            total.Add("MODELS|");
            total.Add(scan.ModelsDigest);
            total.Add("MATERIALS|");
            total.Add(scan.MaterialsDigest);
            total.Add("TEXTURES|");
            total.Add(scan.TexturesDigest);
            total.Add("ANIMATIONS|");
            total.Add(scan.AnimationsDigest);
            total.Add("CATALOGS|");
            total.Add(scan.CatalogsDigest);
            scan.TotalDigest = total.Value;

            return scan;
        }

        // ------------------------------------------------------------------------------------
        // Model sets
        // ------------------------------------------------------------------------------------
        private static ModelSetScan LoadModelSet(Scan scan, string modelsDirectory, string setDirectoryName, bool textured)
        {
            ModelSetScan set = new ModelSetScan();

            set.Name = setDirectoryName;

            string directory = FindChildDirectory(modelsDirectory, setDirectoryName);

            if (directory == null)
            {
                throw AssetFormatException.At(modelsDirectory, -1, "the models directory has no " + setDirectoryName + " folder");
            }

            set.Directory = directory;

            Fnv1a64 digest = new Fnv1a64();
            Fnv1a64 materials = new Fnv1a64();
            Fnv1a64 palettes = new Fnv1a64();

            digest.Add("MODELSET|");
            digest.Add(setDirectoryName);
            materials.Add("MATERIALSET|");
            materials.Add(setDirectoryName);
            palettes.Add("PALETTESET|");
            palettes.Add(setDirectoryName);

            string[] files = ListFiles(directory, ".wbm");

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                set.ModelFiles++;
                set.ModelFileNames.Add(Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant());

                try
                {
                    WbmModel model = WbmReader.LoadFile(file);

                    set.Models++;
                    set.ModelNames.Add(model.Component.Name.ToLowerInvariant());

                    set.Materials += model.Materials.Length;
                    set.Bones += model.Component.SubComponentCount;
                    set.TextureNameEntries += model.TextureNames.Length;

                    if (model.LodCount == 1)
                    {
                        set.LodOne++;
                    }
                    else
                    {
                        set.LodTwo++;
                    }

                    for (int level = 0; level < model.Lods.Length; level++)
                    {
                        ModelLod lod = model.Lods[level];

                        set.MeshBlocks += lod.Blocks.Length;

                        for (int block = 0; block < lod.Blocks.Length; block++)
                        {
                            MeshBlock mesh = lod.Blocks[block];

                            set.Vertices += mesh.VertexCount;
                            set.Normals += mesh.NormalCount;
                            set.Coords += mesh.CoordCount;
                            set.Faces += mesh.FaceCount;
                        }
                    }

                    for (int material = 0; material < model.Materials.Length; material++)
                    {
                        MeshMaterial entry = model.Materials[material];
                        string textureName = model.GetTextureName(entry);

                        if (textureName != null)
                        {
                            set.ReferencedTextureNames.Add(textureName.ToLowerInvariant());
                        }

                        materials.Add("MAT|");
                        materials.Add(model.Component.Name);
                        materials.Add(material);
                        materials.Add(entry.Colour);
                        materials.Add(entry.Ambient);
                        materials.Add(entry.Diffuse);
                        materials.Add(entry.Emissive);
                        materials.Add(entry.TextureIndex);
                        materials.Add(textureName == null ? string.Empty : textureName);
                        materials.Add(entry.BackIndex);
                    }

                    AppendModel(digest, model, textured);
                }
                catch (AssetFormatException exception)
                {
                    if (!textured && (string.Equals(fileName, "chisom.wbm", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(fileName, "nemptm.wbm", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Two records on the shipped disc are short. third_party/hyperblade-decomp
                        // tools/check_model_files.py lists exactly these two and 20XX's
                        // docs/hardware_renderer.md says "Two Mdlshd files are corrupt, CHISOM and
                        // NEMPTM". They are recorded as shipped-data warnings, not silent failures.
                        set.Skipped.Add(fileName + ": " + exception.Message);
                        digest.Add("SKIP|");
                        digest.Add(fileName);
                        continue;
                    }

                    throw;
                }
            }

            set.ModelDigest = digest.Value;
            set.MaterialsDigest = materials.Value;

            // The .wbp palettes sit beside their models and are loaded by
            // ColorPalette::LoadPaletteEntries with the model name, from the active model path.
            string[] paletteFiles = ListFiles(directory, ".wbp");

            foreach (string file in paletteFiles)
            {
                WbpPalette palette = WbpReader.LoadFile(file);

                set.Palettes++;
                set.PaletteEntries += palette.Entries.Length;
                palettes.Add("PALETTE|");
                palettes.Add(Path.GetFileName(file));
                palettes.Add(palette.Version);
                palettes.Add(palette.DeclaredCount);

                for (int entry = 0; entry < palette.Entries.Length; entry++)
                {
                    WbpPaletteEntry row = palette.Entries[entry];

                    palettes.Add(row.Index);
                    palettes.Add(row.Red);
                    palettes.Add(row.Green);
                    palettes.Add(row.Blue);
                }
            }

            set.PaletteDigest = palettes.Value;

            if (set.Skipped.Count > 0)
            {
                // One warning pair per known-shipped short file, in file order.
                foreach (string skipped in set.Skipped)
                {
                    scan.Warnings.Add("mdlshd " + skipped + " (shipped short record; see third_party/hyperblade-decomp/tools/check_model_files.py)");
                }
            }

            return set;
        }

        private static void AppendModel(Fnv1a64 digest, WbmModel model, bool textured)
        {
            FileHeader header = model.Header;

            digest.Add("FILE|");
            digest.Add(Path.GetFileName(model.SourceName));
            digest.Add(header.Tag);
            digest.Add(header.Name);
            AppendVersion(digest, header.Version);
            digest.Add(model.LodCount);
            digest.Add(textured ? 1 : 0);

            for (int i = 0; i < model.LodThresholds.Length; i++)
            {
                digest.Add(model.LodThresholds[i]);
            }

            ModelComponent component = model.Component;

            digest.Add(component.Size);
            digest.Add(component.Id);
            digest.Add(component.Name);
            digest.Add(component.SubComponentCount);
            AppendBytes(digest, component.Unused);
            digest.Add(component.AngleDegrees.X);
            digest.Add(component.AngleDegrees.Y);
            digest.Add(component.AngleDegrees.Z);
            digest.Add(component.ShadowOffsetBytes);

            ShadowBlock shadow = component.Shadow;

            if (shadow != null)
            {
                digest.Add(shadow.FaceCount);
                digest.Add(shadow.VertexCount);
                digest.Add(shadow.VertexOffsetBytes);
                digest.Add(shadow.IndexOffsetBytes);

                for (int face = 0; face < shadow.Faces.Length; face++)
                {
                    ShadowFace shadowFace = shadow.Faces[face];

                    digest.Add(shadowFace.CornerCount);
                    digest.Add(shadowFace.Flags);
                    digest.Add(shadowFace.MaterialIndex);
                    AppendVector(digest, shadowFace.Normal);

                    for (int corner = 0; corner < shadowFace.Corners.Length; corner++)
                    {
                        digest.Add(shadowFace.Corners[corner]);
                    }
                }

                for (int vertex = 0; vertex < shadow.Vertices.Length; vertex++)
                {
                    AppendVector(digest, shadow.Vertices[vertex]);
                }

                for (int index = 0; index < shadow.Indices.Length; index++)
                {
                    digest.Add(shadow.Indices[index]);
                }
            }

            for (int slot = 0; slot < component.SlotDescriptors.Length; slot++)
            {
                MeshBoneDesc bone = component.SlotDescriptors[slot];

                digest.Add("BONE|");
                digest.Add(bone.LinkId);
                digest.Add(bone.ChildCount);
                AppendVector(digest, bone.Rest);
                AppendVector(digest, bone.Scale);
                digest.Add(bone.Unused);

                for (int child = 0; child < bone.ChildIds.Length; child++)
                {
                    digest.Add(bone.ChildIds[child]);
                }
            }

            digest.Add(model.MaterialFileName);

            for (int name = 0; name < model.MaterialNames.Length; name++)
            {
                digest.Add("MNAME|");
                digest.Add(model.MaterialNames[name].Value);
            }

            for (int name = 0; name < model.TextureNames.Length; name++)
            {
                digest.Add("TNAME|");
                digest.Add(model.TextureNames[name].Value);
            }

            for (int level = 0; level < model.Lods.Length; level++)
            {
                ModelLod lod = model.Lods[level];

                digest.Add("LOD|");
                digest.Add(level);
                digest.Add((int)lod.RecordCount);
                digest.Add(lod.ByteCount);

                for (int block = 0; block < lod.Blocks.Length; block++)
                {
                    AppendMeshBlock(digest, lod.Blocks[block]);
                }
            }
        }

        private static void AppendMeshBlock(Fnv1a64 digest, MeshBlock block)
        {
            digest.Add("BLOCK|");
            digest.Add(block.Size);
            digest.Add(block.Slot);
            digest.Add(block.FaceCount);
            digest.Add(block.VertexCount);
            digest.Add(block.NormalCount);
            digest.Add(block.CoordCount);
            AppendVector(digest, block.Origin);
            digest.Add(block.DepthBias);
            digest.Add(block.Flags);
            digest.Add(block.VertexOffsetBytes);

            for (int vertex = 0; vertex < block.Vertices.Length; vertex++)
            {
                AppendVector(digest, block.Vertices[vertex]);
            }

            for (int normal = 0; normal < block.Normals.Length; normal++)
            {
                AppendVector(digest, block.Normals[normal]);
            }

            for (int coord = 0; coord < block.Coords.Length; coord++)
            {
                digest.Add(block.Coords[coord].Tu);
                digest.Add(block.Coords[coord].Tv);
            }

            for (int face = 0; face < block.Faces.Length; face++)
            {
                MeshFace data = block.Faces[face];

                digest.Add("FACE|");
                digest.Add(data.CornerCount);
                digest.Add(data.Flags);
                digest.Add(data.MaterialIndex);
                AppendVector(digest, data.Normal);

                for (int corner = 0; corner < data.Corners.Length; corner++)
                {
                    digest.Add(data.Corners[corner]);
                }

                for (int corner = 0; corner < data.SecondCorners.Length; corner++)
                {
                    digest.Add(data.SecondCorners[corner]);
                }

                for (int corner = 0; corner < data.UVIndices.Length; corner++)
                {
                    digest.Add(data.UVIndices[corner]);
                }

                digest.Add(data.ScaledWidth);
            }
        }

        // ------------------------------------------------------------------------------------
        // Textures: Data\Textures, the 24 bit bitmap set the Direct3D path and the 20XX hardware
        // renderer draw (CD3DTexture::LoadBitmapFile errors on an 8 bit file and never reached its
        // 8 bit arm in the shipped build).
        // ------------------------------------------------------------------------------------
        private static void LoadTextures(Scan scan)
        {
            Fnv1a64 digest = new Fnv1a64();

            digest.Add("TEXTURESET|");

            string directory = FindChildDirectory(scan.DataPath, "textures");

            if (directory == null)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "the Data folder has no textures directory");
            }

            scan.TexturesDirectory = directory;

            string[] files = ListFiles(directory, ".bmp");

            scan.TextureFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                scan.TextureFiles[Path.GetFileNameWithoutExtension(file)] = file;
            }

            foreach (string file in files)
            {
                BmpTexture texture = BmpReader.LoadFile(file);

                scan.Textures++;
                scan.TexturePixels += texture.PixelCount;

                if (texture.BitsPerPixel == 24)
                {
                    scan.Textures24++;
                }

                digest.Add("TEX|");
                digest.Add(Path.GetFileName(file));
                digest.Add(texture.Width);
                digest.Add(texture.Height);
                digest.Add(texture.BitsPerPixel);
                digest.Add(texture.Compression);
                digest.Add(texture.PaletteEntryCount);

                for (int entry = 0; entry < texture.Palette.Length; entry++)
                {
                    digest.Add(texture.Palette[entry].Red);
                    digest.Add(texture.Palette[entry].Green);
                    digest.Add(texture.Palette[entry].Blue);
                }

                AppendBytes(digest, texture.Rgba);
            }

            // Every texture a shipped material names must exist. ModelDatabase::CreateMaterialList
            // raises "Error reading activating texture" when the file cannot be opened, so a miss
            // here is a loud failure, not a warning.
            for (int set = 0; set < scan.Sets.Length; set++)
            {
                foreach (string name in scan.Sets[set].ReferencedTextureNames)
                {
                    if (!scan.TextureFiles.ContainsKey(name))
                    {
                        throw AssetFormatException.At(scan.Sets[set].Directory, -1, "material texture \"" + name + "\" has no " + name + ".bmp in Data\\Textures");
                    }
                }

                scan.ReferencedTextureNames += scan.Sets[set].ReferencedTextureNames.Count;
            }

            scan.TextureDigest = digest.Value;
        }

        // ------------------------------------------------------------------------------------
        // Animations: Data\AnimData\*.wba through AnimFile/AnimClipSet, and the assembly script
        // Data\AnimData\AnimAssm.txt that names the sets and carries the clip ranges and events.
        // ------------------------------------------------------------------------------------
        private static void LoadAnimations(Scan scan)
        {
            Fnv1a64 digest = new Fnv1a64();

            digest.Add("ANIMSETS|");

            string directory = FindChildDirectory(scan.DataPath, "animdata");

            if (directory == null)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "the Data folder has no animdata directory");
            }

            scan.AnimationsDirectory = directory;
            scan.AnimationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] files = ListFiles(directory, ".wba");

            foreach (string file in files)
            {
                AnimationClipSet set = WbaReader.LoadFile(file);

                scan.AnimationSets++;
                scan.AnimationClips += set.Clips.Length;
                scan.AnimationFrames += set.AnimationHeader.FrameCount;
                scan.AnimationNames.Add(Path.GetFileNameWithoutExtension(file));

                if (set.TrailingBytes > 0)
                {
                    scan.AnimationTrailing++;
                }

                digest.Add("WBA|");
                digest.Add(Path.GetFileName(file));
                digest.Add(set.Header.Tag);
                digest.Add(set.Header.Name);
                AppendVersion(digest, set.Header.Version);
                digest.Add(set.AnimationHeader.ClipCount);
                digest.Add(set.AnimationHeader.FrameCount);
                digest.Add(set.AnimationHeader.FrameRate);
                digest.Add(set.AnimationHeader.Duration);
                digest.Add(set.AnimationHeader.EndFrame);
                digest.Add(set.TrailingBytes);

                for (int clip = 0; clip < set.Clips.Length; clip++)
                {
                    AnimationClip animation = set.Clips[clip];

                    digest.Add("CLIP|");
                    digest.Add(animation.Segment);
                    digest.Add(animation.Frame);
                    digest.Add(animation.DataSize);
                    digest.Add(animation.AxisOrder);
                    digest.Add(animation.Format);
                    digest.Add(animation.RawData.Length);

                    for (int value = 0; value < animation.RawData.Length; value++)
                    {
                        digest.Add(animation.RawData[value]);
                    }

                    switch (animation.Format)
                    {
                        case AnimationClip.FormatTranslations:
                            scan.AnimationFormat1++;
                            break;

                        case AnimationClip.FormatSingleTranslationEuler:
                        case AnimationClip.FormatSingleTranslationEuler4:
                            scan.AnimationFormat2++;
                            break;

                        case AnimationClip.FormatTranslationsEuler:
                            scan.AnimationFormat3++;
                            break;

                        default:
                            scan.Warnings.Add(Path.GetFileName(file) + " clip " + clip + " uses animation format " + animation.Format + "; the shipped disc only carries formats 1, 2 and 3");
                            break;
                    }
                }
            }

            // AnimAssm.txt. game.cpp calls g_pAnimRegistry->LoadFromPath("animdata\\AnimAssm.txt")
            // and AnimScriptTable::ParseScript walks it into the animation databases.
            string assemblyFile = FindChildFile(directory, "animassm.txt");

            if (assemblyFile == null)
            {
                throw AssetFormatException.At(directory, -1, "AnimData has no AnimAssm.txt animation assembly script");
            }

            AnimationAssemblyFile assembly = AnimationAssemblyReader.LoadFile(assemblyFile);

            scan.Assemblies = assembly.Assemblies.Length;

            for (int index = 0; index < assembly.Assemblies.Length; index++)
            {
                AnimationAssembly entry = assembly.Assemblies[index];

                digest.Add("ASM|");
                digest.Add(entry.Name);
                digest.Add(entry.Description);

                for (int segment = 0; segment < entry.Segments.Length; segment++)
                {
                    AnimationAssemblySegment range = entry.Segments[segment];

                    scan.AssemblySegments++;
                    scan.AssemblySegmentFiles.Add(range.FileName.ToLowerInvariant());

                    if (!scan.AnimationNames.Contains(range.FileName))
                    {
                        throw AssetFormatException.At(assemblyFile, -1, "assembly " + entry.Name + " names animation \"" + range.FileName + "\" which has no .wba");
                    }

                    digest.Add("SEG|");
                    digest.Add(range.FileName);
                    digest.Add(range.Description);
                    digest.Add(range.StartFrame);
                    digest.Add(range.EndFrame);
                    digest.Add(range.SlowFps);
                    digest.Add(range.StandardFps);
                    digest.Add(range.TransitionFactor);
                    digest.Add(range.LoopCount);
                }

                for (int trigger = 0; trigger < entry.Triggers.Length; trigger++)
                {
                    AnimationAssemblyTrigger triggerEntry = entry.Triggers[trigger];

                    scan.AssemblyTriggers++;

                    digest.Add("TRG|");
                    digest.Add(triggerEntry.Segment);
                    digest.Add(triggerEntry.Frame);
                    digest.Add(triggerEntry.ActionName);
                    digest.Add(triggerEntry.HasSync ? 1 : 0);
                    digest.Add(triggerEntry.HasSync ? triggerEntry.SyncAssembly : string.Empty);
                    digest.Add(triggerEntry.HasSync ? (int)triggerEntry.SyncSegment : -1);
                    digest.Add(triggerEntry.HasSync ? (int)triggerEntry.SyncFrame : -1);
                }

                scan.AssemblyNames.Add(entry.Name);
            }

            scan.AnimationsDigest = digest.Value;
        }

        // ------------------------------------------------------------------------------------
        // MDM1. Every packed file on the disc is an assets8 bitmap: 1767 MDM1 streams whose
        // decompressed size the header declares. GDI::ReadBitmapFile routes a tagged file through
        // MDM1::Decompress and then treats the payload as a raw DIB, so the probe decodes each one
        // as a bitmap (the palette and RLE DIB forms included).
        // ------------------------------------------------------------------------------------
        private static void LoadMdm1(Scan scan)
        {
            Fnv1a64 digest = new Fnv1a64();

            digest.Add("MDM1|");

            string[] files = Directory.GetFiles(scan.DataPath, "*", SearchOption.AllDirectories);

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                byte[] data;

                try
                {
                    data = File.ReadAllBytes(file);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!Mdm1.HasMagic(data))
                {
                    continue;
                }

                scan.Mdm1Files++;

                byte[] payload = Mdm1.Decompress(data);

                digest.Add("MDM1FILE|");
                digest.Add(RelativePath(scan.DataPath, file));
                digest.Add(payload.Length);

                if (string.Equals(Path.GetExtension(file), ".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    BmpTexture texture = BmpReader.Load(data, file);

                    scan.Mdm1Bitmaps++;

                    if (texture.IsPaletted)
                    {
                        scan.Mdm1Paletted++;
                    }
                    else
                    {
                        scan.Mdm1Direct++;
                    }

                    scan.Mdm1Pixels += texture.PixelCount;

                    digest.Add(texture.Width);
                    digest.Add(texture.Height);
                    digest.Add(texture.BitsPerPixel);
                    digest.Add(texture.Compression);
                    digest.Add(texture.PaletteEntryCount);

                    for (int entry = 0; entry < texture.Palette.Length; entry++)
                    {
                        digest.Add(texture.Palette[entry].Red);
                        digest.Add(texture.Palette[entry].Green);
                        digest.Add(texture.Palette[entry].Blue);
                    }

                    if (texture.Indices != null)
                    {
                        AppendBytes(digest, texture.Indices);
                    }
                    else
                    {
                        AppendBytes(digest, texture.Rgba);
                    }
                }
                else
                {
                    AppendBytes(digest, payload);
                }
            }

            scan.Mdm1Digest = digest.Value;
        }

        // ------------------------------------------------------------------------------------
        // Catalogues: model.lst, animate.lst, the four reaction lists, the model references in
        // *.HTM/*.ENV files and the per-model .wbp palettes. Data\model.lst and Data\animate.lst
        // ship on the disc but no shipped executable reads them; the probe still parses both and
        // reports how far their names resolve, which is the honest coverage of those files.
        // ------------------------------------------------------------------------------------
        private static void LoadCatalogs(Scan scan)
        {
            Fnv1a64 digest = new Fnv1a64();

            digest.Add("CATALOGS|");

            LoadModelList(scan, digest);
            LoadAnimateList(scan, digest);
            LoadReactionLists(scan, digest);
            LoadModelReferences(scan, digest);

            scan.CatalogDigest = digest.Value;
        }

        private static void LoadModelList(Scan scan, Fnv1a64 digest)
        {
            string path = FindChildFile(scan.DataPath, "model.lst");

            if (path == null)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "Data has no model.lst");
            }

            string[] lines = AssetText.SplitLines(AssetText.ReadAllLatin1(path));
            List<string> names = TrimmedNonEmpty(lines);

            scan.ModelListNames = names.ToArray();
            scan.ModelList = names.Count;

            HashSet<string> stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int set = 0; set < scan.Sets.Length; set++)
            {
                foreach (string name in scan.Sets[set].ModelFileNames)
                {
                    stems.Add(name);
                }
            }

            List<string> prefixOnly = new List<string>();
            List<string> unresolved = new List<string>();

            for (int name = 0; name < names.Count; name++)
            {
                string entry = names[name];

                if (stems.Contains(entry))
                {
                    scan.ModelListExact++;
                    continue;
                }

                bool prefix = false;

                foreach (string stem in stems)
                {
                    if (stem.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
                    {
                        prefix = true;
                        break;
                    }
                }

                if (prefix)
                {
                    scan.ModelListPrefixOnly++;
                    prefixOnly.Add(entry);
                }
                else
                {
                    scan.ModelListUnresolved++;
                    unresolved.Add(entry);
                }
            }

            digest.Add("MODELLIST|");

            for (int name = 0; name < names.Count; name++)
            {
                digest.Add(names[name]);
            }

            if (unresolved.Count > 0 || prefixOnly.Count > 0)
            {
                scan.Warnings.Add(
                    "model.lst: " + scan.ModelListExact + " of " + scan.ModelList
                    + " catalogue names match a shipped .wbm exactly, " + prefixOnly.Count + " only prefix a name ("
                    + Join(prefixOnly) + "), " + unresolved.Count + " match nothing; the retail executables never read model.lst, "
                    + "so these are shipped development-catalogue gaps, not missing runtime models");
            }
        }

        private static void LoadAnimateList(Scan scan, Fnv1a64 digest)
        {
            string path = FindChildFile(scan.DataPath, "animate.lst");

            if (path == null)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "Data has no animate.lst");
            }

            string[] lines = AssetText.SplitLines(AssetText.ReadAllLatin1(path));
            List<string> names = TrimmedNonEmpty(lines);

            scan.AnimateList = names.Count;
            digest.Add("ANIMATELIST|");

            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> missing = new List<string>();

            for (int name = 0; name < names.Count; name++)
            {
                string entry = names[name];

                unique.Add(entry);
                digest.Add(entry);

                if (!scan.AnimationNames.Contains(entry))
                {
                    missing.Add(entry);
                }
            }

            scan.AnimateListUnique = unique.Count;

            List<string> distinctMissing = new List<string>();

            for (int name = 0; name < missing.Count; name++)
            {
                if (!ContainsIgnoreCase(distinctMissing, missing[name]))
                {
                    distinctMissing.Add(missing[name]);
                }
            }

            scan.AnimateListMissing = distinctMissing.Count;

            if (distinctMissing.Count > 0 || names.Count != unique.Count)
            {
                scan.Warnings.Add(
                    "animate.lst: " + names.Count + " lines, " + unique.Count + " unique, " + distinctMissing.Count
                    + " name(s) have no .wba (" + Join(distinctMissing) + "); the retail executables never read animate.lst");
            }
        }

        private static void LoadReactionLists(Scan scan, Fnv1a64 digest)
        {
            // PlanVM::InitReactionTables (src/planvm/planvm.cpp lines 389-456) opens
            // Data\HitAnims.lst, ChkAnims.lst, JmpAnims.lst and ThrAnims.lst next to the
            // executable and sscanf's seven names from each of five lines.
            string[] names = { "hitanims.lst", "chkanims.lst", "jmpanims.lst", "thranims.lst" };
            HashSet<string> assemblies = new HashSet<string>(scan.AssemblyNames, StringComparer.OrdinalIgnoreCase);
            List<string> missing = new List<string>();

            scan.ReactionFiles = names.Length;

            for (int file = 0; file < names.Length; file++)
            {
                string path = FindChildFile(scan.DataPath, names[file]);

                if (path == null)
                {
                    throw AssetFormatException.At(scan.DataPath, -1, "Data has no " + names[file]);
                }

                string[] lines = AssetText.SplitLines(AssetText.ReadAllLatin1(path));
                List<string> content = TrimmedNonEmpty(lines);

                if (content.Count < 5)
                {
                    throw AssetFormatException.At(path, -1, "reaction list has " + content.Count + " line(s), the loader reads exactly five");
                }

                digest.Add("REACTION|");
                digest.Add(names[file]);

                for (int line = 0; line < 5; line++)
                {
                    string[] tokens = content[line].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length != 7)
                    {
                        // PlanVM::InitReactionTables reads exactly seven names a line and reports
                        // "Error reading Animation List file." for anything else.
                        throw AssetFormatException.At(path, line, "reaction list line holds " + tokens.Length + " name(s), the loader reads exactly seven");
                    }

                    for (int token = 0; token < tokens.Length; token++)
                    {
                        scan.ReactionNames++;
                        digest.Add(tokens[token]);

                        if (!assemblies.Contains(tokens[token]) && !ContainsIgnoreCase(missing, tokens[token]))
                        {
                            missing.Add(tokens[token]);
                        }
                    }
                }
            }

            scan.ReactionNamesMissing = missing.Count;

            if (missing.Count > 0)
            {
                scan.Warnings.Add(
                    "reaction lists: " + missing.Count + " name(s) have no animation assembly (" + Join(missing)
                    + "); PlanVM::InitReactionTables would resolve them to animation id -1 on the shipped disc");
            }
        }

        private static void LoadModelReferences(Scan scan, Fnv1a64 digest)
        {
            // ObjectDefinitionReader reads modfile= from every *.htm and *.env. The asset probe
            // re-checks the file side of that reference: a named model must exist in both sets.
            string[] files = Directory.GetFiles(scan.DataPath, "*", SearchOption.TopDirectoryOnly);

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> unresolved = new List<string>();

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);

                if (!string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".env", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = AssetText.ReadAllLatin1(file);
                int at = 0;

                while (true)
                {
                    int keyword = IndexOfIgnoreCase(text, "modfile", at);

                    if (keyword < 0)
                    {
                        break;
                    }

                    int cursor = keyword + 7;

                    while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t'))
                    {
                        cursor++;
                    }

                    if (cursor >= text.Length || text[cursor] != '=')
                    {
                        at = cursor;
                        continue;
                    }

                    cursor++;

                    while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t'))
                    {
                        cursor++;
                    }

                    int start = cursor;

                    while (cursor < text.Length && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
                    {
                        cursor++;
                    }

                    string name = text.Substring(start, cursor - start);

                    at = cursor;

                    if (name.Length == 0 || string.Equals(name, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    scan.ModelReferences++;
                    unique.Add(name);
                    digest.Add("MODELREF|");
                    digest.Add(Path.GetFileName(file));
                    digest.Add(name);
                }
            }

            scan.ModelReferenceNames = unique.Count;

            foreach (string name in unique)
            {
                for (int set = 0; set < scan.Sets.Length; set++)
                {
                    if (!scan.Sets[set].ModelFileNames.Contains(name.ToLowerInvariant()))
                    {
                        unresolved.Add(name + " (not in " + scan.Sets[set].Name + ")");
                    }
                }
            }

            if (unresolved.Count > 0)
            {
                throw AssetFormatException.At(scan.DataPath, -1, "model reference(s) name no .wbm in a shipped model set: " + Join(unresolved));
            }
        }

        // ------------------------------------------------------------------------------------
        // Report and expected-count check
        // ------------------------------------------------------------------------------------
        private static string BuildReport(Scan scan)
        {
            int modelFiles = 0;
            int models = 0;
            int skipped = 0;
            int materials = 0;
            int bones = 0;
            int meshBlocks = 0;
            int vertices = 0;
            int normals = 0;
            int coords = 0;
            int faces = 0;
            int textureNameEntries = 0;
            int palettes = 0;
            int paletteEntries = 0;
            int lodOne = 0;
            int lodTwo = 0;

            for (int set = 0; set < scan.Sets.Length; set++)
            {
                ModelSetScan entry = scan.Sets[set];

                modelFiles += entry.ModelFiles;
                models += entry.Models;
                skipped += entry.Skipped.Count;
                materials += entry.Materials;
                bones += entry.Bones;
                meshBlocks += entry.MeshBlocks;
                vertices += entry.Vertices;
                normals += entry.Normals;
                coords += entry.Coords;
                faces += entry.Faces;
                textureNameEntries += entry.TextureNameEntries;
                palettes += entry.Palettes;
                paletteEntries += entry.PaletteEntries;
                lodOne += entry.LodOne;
                lodTwo += entry.LodTwo;
            }

            CheckExpected(scan, "models", ExpectedModels, models);
            CheckExpected(scan, "modelFiles", ExpectedModelFiles, modelFiles);
            CheckExpected(scan, "skippedModels", ExpectedSkippedModels, skipped);
            CheckExpected(scan, "modelsTextured", ExpectedModelsTextured, scan.Sets[0].Models);
            CheckExpected(scan, "modelsShaded", ExpectedModelsShaded, scan.Sets[1].Models);
            CheckExpected(scan, "materials", ExpectedMaterialCount, materials);
            CheckExpected(scan, "bones", ExpectedBoneCount, bones);
            CheckExpected(scan, "meshBlocks", ExpectedMeshBlocks, meshBlocks);
            CheckExpected(scan, "vertices", ExpectedVertices, vertices);
            CheckExpected(scan, "normals", ExpectedNormals, normals);
            CheckExpected(scan, "textureCoords", ExpectedTextureCoords, coords);
            CheckExpected(scan, "faces", ExpectedFaces, faces);
            CheckExpected(scan, "textureNameEntries", ExpectedTextureNameEntries, textureNameEntries);
            CheckExpected(scan, "palettes", ExpectedPaletteFiles, palettes);
            CheckExpected(scan, "paletteEntries", ExpectedPaletteEntries, paletteEntries);
            CheckExpected(scan, "lod1", 94, lodOne);
            CheckExpected(scan, "lod2", 250, lodTwo);

            CheckExpected(scan, "textures", ExpectedTextures, scan.Textures);
            CheckExpected(scan, "texturePixels", ExpectedTexturePixels, scan.TexturePixels);
            CheckExpected(scan, "textures24", ExpectedTextures, scan.Textures24);
            CheckExpected(scan, "mdm1", ExpectedMdm1Files, scan.Mdm1Files);
            CheckExpected(scan, "mdm1Bitmaps", ExpectedMdm1Files, scan.Mdm1Bitmaps);
            CheckExpected(scan, "mdm1Paletted", ExpectedMdm1Paletted, scan.Mdm1Paletted);
            CheckExpected(scan, "mdm1Direct", ExpectedMdm1Direct, scan.Mdm1Direct);

            CheckExpected(scan, "animationSets", ExpectedAnimationSets, scan.AnimationSets);
            CheckExpected(scan, "animationClips", ExpectedAnimationClips, scan.AnimationClips);
            CheckExpected(scan, "animationFrames", ExpectedAnimationFrames, scan.AnimationFrames);
            CheckExpected(scan, "animationFormat1", ExpectedAnimationFormat1, scan.AnimationFormat1);
            CheckExpected(scan, "animationFormat2", ExpectedAnimationFormat2, scan.AnimationFormat2);
            CheckExpected(scan, "animationFormat3", ExpectedAnimationFormat3, scan.AnimationFormat3);
            CheckExpected(scan, "animationTrailing", ExpectedAnimationTrailing, scan.AnimationTrailing);
            CheckExpected(scan, "assemblies", ExpectedAssemblies, scan.Assemblies);
            CheckExpected(scan, "assemblySegments", ExpectedAssemblySegments, scan.AssemblySegments);
            CheckExpected(scan, "assemblyTriggers", ExpectedAssemblyTriggers, scan.AssemblyTriggers);
            CheckExpected(scan, "assemblySegmentFiles", ExpectedAssemblySegmentFiles, scan.AssemblySegmentFiles.Count);

            CheckExpected(scan, "modelList", ExpectedModelListEntries, scan.ModelList);
            CheckExpected(scan, "modelListExact", ExpectedModelListExact, scan.ModelListExact);
            CheckExpected(scan, "modelListPrefixOnly", ExpectedModelListPrefixOnly, scan.ModelListPrefixOnly);
            CheckExpected(scan, "modelListUnresolved", ExpectedModelListUnresolved, scan.ModelListUnresolved);
            CheckExpected(scan, "animateList", ExpectedAnimateListLines, scan.AnimateList);
            CheckExpected(scan, "animateListUnique", ExpectedAnimateListUnique, scan.AnimateListUnique);
            CheckExpected(scan, "animateListMissing", ExpectedAnimateListMissing, scan.AnimateListMissing);
            CheckExpected(scan, "reactionFiles", 4, scan.ReactionFiles);
            CheckExpected(scan, "reactionNames", ExpectedReactionNames, scan.ReactionNames);
            CheckExpected(scan, "reactionNamesMissing", ExpectedReactionNamesMissing, scan.ReactionNamesMissing);
            CheckExpected(scan, "modelReferences", ExpectedModelReferences, scan.ModelReferences);
            CheckExpected(scan, "modelReferenceNames", ExpectedModelReferenceNames, scan.ModelReferenceNames);

            if (ExpectedTotalDigest.Length != 0 && !string.Equals(ExpectedTotalDigest, scan.TotalDigest, StringComparison.Ordinal))
            {
                scan.Mismatches.Add("total digest is " + scan.TotalDigest + ", expected " + ExpectedTotalDigest);
            }

            StringBuilder report = new StringBuilder();

            if (scan.Mismatches.Count == 0)
            {
                report.AppendLine(
                    OkMarker
                    + " models=" + models.ToString(CultureInfo.InvariantCulture)
                    + " materials=" + materials.ToString(CultureInfo.InvariantCulture)
                    + " bones=" + bones.ToString(CultureInfo.InvariantCulture)
                    + " textures=" + scan.Textures.ToString(CultureInfo.InvariantCulture)
                    + " animations=" + scan.AnimationSets.ToString(CultureInfo.InvariantCulture)
                    + " digest=" + scan.TotalDigest);
            }
            else
            {
                report.AppendLine(
                    "UBLADE3_ASSETS_FAILED models=" + models.ToString(CultureInfo.InvariantCulture)
                    + " materials=" + materials.ToString(CultureInfo.InvariantCulture)
                    + " bones=" + bones.ToString(CultureInfo.InvariantCulture)
                    + " textures=" + scan.Textures.ToString(CultureInfo.InvariantCulture)
                    + " animations=" + scan.AnimationSets.ToString(CultureInfo.InvariantCulture)
                    + " digest=" + scan.TotalDigest);
            }

            report.AppendLine("UBLADE3_ASSETS_DETAIL data=" + scan.DataPath);
            report.AppendLine(
                "UBLADE3_ASSETS_COUNTS"
                + " modelSets=" + scan.Sets.Length.ToString(CultureInfo.InvariantCulture)
                + " modelFiles=" + modelFiles.ToString(CultureInfo.InvariantCulture)
                + " models=" + models.ToString(CultureInfo.InvariantCulture)
                + " skipped=" + skipped.ToString(CultureInfo.InvariantCulture)
                + " modelsTextured=" + scan.Sets[0].Models.ToString(CultureInfo.InvariantCulture)
                + " modelsShaded=" + scan.Sets[1].Models.ToString(CultureInfo.InvariantCulture)
                + " lod1=" + lodOne.ToString(CultureInfo.InvariantCulture)
                + " lod2=" + lodTwo.ToString(CultureInfo.InvariantCulture)
                + " meshBlocks=" + meshBlocks.ToString(CultureInfo.InvariantCulture)
                + " vertices=" + vertices.ToString(CultureInfo.InvariantCulture)
                + " normals=" + normals.ToString(CultureInfo.InvariantCulture)
                + " textureCoords=" + coords.ToString(CultureInfo.InvariantCulture)
                + " faces=" + faces.ToString(CultureInfo.InvariantCulture)
                + " materials=" + materials.ToString(CultureInfo.InvariantCulture)
                + " bones=" + bones.ToString(CultureInfo.InvariantCulture)
                + " textureNameEntries=" + textureNameEntries.ToString(CultureInfo.InvariantCulture)
                + " palettes=" + palettes.ToString(CultureInfo.InvariantCulture)
                + " paletteEntries=" + paletteEntries.ToString(CultureInfo.InvariantCulture)
                + " textures=" + scan.Textures.ToString(CultureInfo.InvariantCulture)
                + " textures24=" + scan.Textures24.ToString(CultureInfo.InvariantCulture)
                + " texturePixels=" + scan.TexturePixels.ToString(CultureInfo.InvariantCulture)
                + " textureRefs=" + scan.ReferencedTextureNames.ToString(CultureInfo.InvariantCulture)
                + " mdm1=" + scan.Mdm1Files.ToString(CultureInfo.InvariantCulture)
                + " mdm1Bitmaps=" + scan.Mdm1Bitmaps.ToString(CultureInfo.InvariantCulture)
                + " mdm1Paletted=" + scan.Mdm1Paletted.ToString(CultureInfo.InvariantCulture)
                + " mdm1Direct=" + scan.Mdm1Direct.ToString(CultureInfo.InvariantCulture)
                + " mdm1Pixels=" + scan.Mdm1Pixels.ToString(CultureInfo.InvariantCulture)
                + " animationSets=" + scan.AnimationSets.ToString(CultureInfo.InvariantCulture)
                + " animationClips=" + scan.AnimationClips.ToString(CultureInfo.InvariantCulture)
                + " animationFrames=" + scan.AnimationFrames.ToString(CultureInfo.InvariantCulture)
                + " animationsFmt1=" + scan.AnimationFormat1.ToString(CultureInfo.InvariantCulture)
                + " animationsFmt2=" + scan.AnimationFormat2.ToString(CultureInfo.InvariantCulture)
                + " animationsFmt3=" + scan.AnimationFormat3.ToString(CultureInfo.InvariantCulture)
                + " animationsTrailing=" + scan.AnimationTrailing.ToString(CultureInfo.InvariantCulture)
                + " assemblies=" + scan.Assemblies.ToString(CultureInfo.InvariantCulture)
                + " assemblySegments=" + scan.AssemblySegments.ToString(CultureInfo.InvariantCulture)
                + " assemblySegmentFiles=" + scan.AssemblySegmentFiles.Count.ToString(CultureInfo.InvariantCulture)
                + " assemblyTriggers=" + scan.AssemblyTriggers.ToString(CultureInfo.InvariantCulture)
                + " modelList=" + scan.ModelList.ToString(CultureInfo.InvariantCulture)
                + " modelListExact=" + scan.ModelListExact.ToString(CultureInfo.InvariantCulture)
                + " modelListPrefixOnly=" + scan.ModelListPrefixOnly.ToString(CultureInfo.InvariantCulture)
                + " modelListUnresolved=" + scan.ModelListUnresolved.ToString(CultureInfo.InvariantCulture)
                + " animateList=" + scan.AnimateList.ToString(CultureInfo.InvariantCulture)
                + " animateListUnique=" + scan.AnimateListUnique.ToString(CultureInfo.InvariantCulture)
                + " animateListMissing=" + scan.AnimateListMissing.ToString(CultureInfo.InvariantCulture)
                + " reactionFiles=" + scan.ReactionFiles.ToString(CultureInfo.InvariantCulture)
                + " reactionNames=" + scan.ReactionNames.ToString(CultureInfo.InvariantCulture)
                + " reactionNamesMissing=" + scan.ReactionNamesMissing.ToString(CultureInfo.InvariantCulture)
                + " modelRefs=" + scan.ModelReferences.ToString(CultureInfo.InvariantCulture)
                + " modelRefNames=" + scan.ModelReferenceNames.ToString(CultureInfo.InvariantCulture));
            report.AppendLine(
                "UBLADE3_ASSETS_DIGEST models=" + scan.ModelsDigest
                + " materials=" + scan.MaterialsDigest
                + " textures=" + scan.TexturesDigest
                + " animations=" + scan.AnimationsDigest
                + " catalogs=" + scan.CatalogsDigest
                + " total=" + scan.TotalDigest);

            for (int mismatch = 0; mismatch < scan.Mismatches.Count; mismatch++)
            {
                report.AppendLine("UBLADE3_ASSETS_EXPECTED_MISMATCH " + Flatten(scan.Mismatches[mismatch]));
            }

            for (int warning = 0; warning < scan.Warnings.Count; warning++)
            {
                report.AppendLine("UBLADE3_ASSETS_WARNING " + Flatten(scan.Warnings[warning]));
            }

            report.AppendLine(WarningsMarker + " " + scan.Warnings.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("UBLADE3_ASSETS_CHECK mismatches=" + scan.Mismatches.Count.ToString(CultureInfo.InvariantCulture));

            return report.ToString().TrimEnd();
        }

        private static void CheckExpected(Scan scan, string name, long expected, long actual)
        {
            if (expected != actual)
            {
                scan.Mismatches.Add(name + " is " + actual.ToString(CultureInfo.InvariantCulture) + ", expected " + expected.ToString(CultureInfo.InvariantCulture));
            }
        }

        // ------------------------------------------------------------------------------------
        // Small helpers
        // ------------------------------------------------------------------------------------
        private static string FindChildDirectory(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return null;
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(parent);
            }
            catch (Exception)
            {
                return null;
            }

            for (int i = 0; i < children.Length; i++)
            {
                if (string.Equals(Path.GetFileName(children[i]), name, StringComparison.OrdinalIgnoreCase))
                {
                    return children[i];
                }
            }

            return null;
        }

        private static string FindChildFile(string directory, string name)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                return null;
            }

            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetFileName(files[i]), name, StringComparison.OrdinalIgnoreCase))
                {
                    return files[i];
                }
            }

            return null;
        }

        private static string[] ListFiles(string directory, string extension)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception)
            {
                throw AssetFormatException.At(directory, -1, "the folder could not be listed: " + exception.Message);
            }

            List<string> matches = new List<string>();

            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetExtension(files[i]), extension, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(files[i]);
                }
            }

            matches.Sort(StringComparer.OrdinalIgnoreCase);

            return matches.ToArray();
        }

        private static List<string> TrimmedNonEmpty(string[] lines)
        {
            List<string> result = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.Length != 0)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static string Join(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder();

            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(values[i]);
            }

            return text.ToString();
        }

        private static bool ContainsIgnoreCase(List<string> values, string text)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], text, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfIgnoreCase(string text, string needle, int start)
        {
            int last = text.Length - needle.Length;

            for (int at = start; at <= last; at++)
            {
                int i = 0;

                while (i < needle.Length && char.ToLowerInvariant(text[at + i]) == char.ToLowerInvariant(needle[i]))
                {
                    i++;
                }

                if (i == needle.Length)
                {
                    return at;
                }
            }

            return -1;
        }

        private static string RelativePath(string root, string path)
        {
            string rootWithSlash = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (path.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(rootWithSlash.Length).Replace('\\', '/');
            }

            return path;
        }

        private static void AppendVersion(Fnv1a64 digest, FileVersion version)
        {
            digest.Add(version.Major);
            digest.Add(version.Minor);
            digest.Add(version.Revision);
            digest.Add(version.Unused);
            digest.Add(version.Build);
        }

        private static void AppendVector(Fnv1a64 digest, AssetVector3 vector)
        {
            digest.Add(vector.X);
            digest.Add(vector.Y);
            digest.Add(vector.Z);
        }

        private static void AppendBytes(Fnv1a64 digest, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                digest.Add(bytes[i]);
            }
        }

        private static string FirstLine(string report)
        {
            int newline = report.IndexOf('\n');

            return newline < 0 ? report : report.Substring(0, newline).TrimEnd('\r');
        }

        private static string ExtractValue(string report, string marker, string key)
        {
            int start = report.IndexOf(marker, StringComparison.Ordinal);

            if (start < 0)
            {
                return string.Empty;
            }

            int end = report.IndexOf('\n', start);

            if (end < 0)
            {
                end = report.Length;
            }

            string line = report.Substring(start, end - start);
            string[] parts = line.Split(' ');

            for (int part = 0; part < parts.Length; part++)
            {
                int equals = parts[part].IndexOf('=');

                if (equals > 0 && parts[part].Substring(0, equals) == key)
                {
                    return parts[part].Substring(equals + 1);
                }
            }

            return string.Empty;
        }

        private static string Flatten(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // ------------------------------------------------------------------------------------
        // Data collected by one scan
        // ------------------------------------------------------------------------------------
        private sealed class ModelSetScan
        {
            internal string Name;
            internal string Directory;
            internal int ModelFiles;
            internal int Models;
            internal int Materials;
            internal int Bones;
            internal int MeshBlocks;
            internal int Vertices;
            internal int Normals;
            internal int Coords;
            internal int Faces;
            internal int TextureNameEntries;
            internal int Palettes;
            internal int PaletteEntries;
            internal int LodOne;
            internal int LodTwo;
            internal string ModelDigest = string.Empty;
            internal string MaterialsDigest = string.Empty;
            internal string PaletteDigest = string.Empty;
            internal readonly List<string> Skipped = new List<string>();
            internal readonly HashSet<string> ModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> ModelFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> ReferencedTextureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Scan
        {
            internal string DataPath;
            internal string TexturesDirectory;
            internal string AnimationsDirectory;

            internal ModelSetScan[] Sets;

            internal int Textures;
            internal int Textures24;
            internal long TexturePixels;
            internal int ReferencedTextureNames;
            internal Dictionary<string, string> TextureFiles;

            internal int Mdm1Files;
            internal int Mdm1Bitmaps;
            internal int Mdm1Paletted;
            internal int Mdm1Direct;
            internal long Mdm1Pixels;

            internal int AnimationSets;
            internal int AnimationClips;
            internal int AnimationFrames;
            internal int AnimationFormat1;
            internal int AnimationFormat2;
            internal int AnimationFormat3;
            internal int AnimationTrailing;
            internal HashSet<string> AnimationNames;

            internal int Assemblies;
            internal int AssemblySegments;
            internal int AssemblyTriggers;
            internal readonly HashSet<string> AssemblySegmentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> AssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal int ModelList;
            internal int ModelListExact;
            internal int ModelListPrefixOnly;
            internal int ModelListUnresolved;
            internal string[] ModelListNames = new string[0];

            internal int AnimateList;
            internal int AnimateListUnique;
            internal int AnimateListMissing;

            internal int ReactionFiles;
            internal int ReactionNames;
            internal int ReactionNamesMissing;

            internal int ModelReferences;
            internal int ModelReferenceNames;

            internal string TextureDigest = string.Empty;
            internal string Mdm1Digest = string.Empty;
            internal string CatalogDigest = string.Empty;
            internal string ModelsDigest = string.Empty;
            internal string MaterialsDigest = string.Empty;
            internal string TexturesDigest = string.Empty;
            internal string AnimationsDigest = string.Empty;
            internal string CatalogsDigest = string.Empty;
            internal string TotalDigest = string.Empty;

            internal readonly List<string> Warnings = new List<string>();
            internal readonly List<string> Mismatches = new List<string>();
        }

        // FNV-1a 64 over a canonical byte stream. Every primitive is hashed as little endian
        // bytes; floats through their bit pattern, so the digest is exact and platform stable.
        private sealed class Fnv1a64
        {
            private const ulong Offset = 14695981039346656037UL;

            private const ulong Prime = 1099511628211UL;

            private ulong hash = Offset;

            internal string Value
            {
                get { return hash.ToString("x16", CultureInfo.InvariantCulture); }
            }

            internal void Add(byte value)
            {
                hash ^= value;
                hash *= Prime;
            }

            internal void Add(string text)
            {
                if (text == null)
                {
                    return;
                }

                for (int i = 0; i < text.Length; i++)
                {
                    Add((byte)(text[i] & 0xff));
                }
            }

            internal void Add(int value)
            {
                Add((byte)(value & 0xff));
                Add((byte)((value >> 8) & 0xff));
                Add((byte)((value >> 16) & 0xff));
                Add((byte)((value >> 24) & 0xff));
            }

            internal void Add(float value)
            {
                Add(BitConverter.SingleToInt32Bits(value));
            }
        }
    }
}
