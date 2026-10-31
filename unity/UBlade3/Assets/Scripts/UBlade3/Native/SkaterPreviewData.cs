using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UnityEngine;

// UnityEngine also defines AnimationClip; the animation the preview plays is the asset layer's.
using AnimationClip = UBlade3.Assets.AnimationClip;

namespace UBlade3.Native
{
    // Which original characters and animation assemblies the N3 preview draws. The defaults are the
    // shipped mapping described in docs/SKATER-PREVIEW.md:
    //
    //   chico.htm ("The_Machine", Chicago): the first Dromer "Evans" carries modfile=CHIOMX and the
    //   "Human" plan list, i.e. the home side's human controlled skater; the third Dromer "Viveros"
    //   carries modfile=CHIGMX and holds the file's SetSpecialGoalieRatings block, i.e. the goalie.
    //   The skate action is AA_Skate_Std (the persistent animation BehParam::SetPersistentAnimation
    //   resolves for a skating player; azkskat2 "normal skate", 0..26 at 30 fps, loop 0 = forever).
    //   The goalie stand is AA_Goalidle (aplread2 "stand ready", 0..80 at 30 fps).
    public sealed class SkaterPreviewOptions
    {
        public string SkaterModel = SkaterPreviewData.ExpectedSkaterModel;

        public string GoalieModel = SkaterPreviewData.ExpectedGoalieModel;

        public string SkaterAssembly = SkaterPreviewData.ExpectedSkaterAssembly;

        public string GoalieAssembly = SkaterPreviewData.ExpectedGoalieAssembly;

        public OriginalModelSet ModelSet = OriginalModelSet.Textured;

        public int LodIndex;

        // LoadTeamFromFile starts nRunningId at the team index (0 for the home side) and steps it by
        // two per Dromer, so every home player's object id is even: (id & 1) == 0 and
        // EngineObject::InitModel does not remap the palette. An away side preview passes true.
        public bool RemapPalette;

        public bool IncludeGoalie = true;
    }

    // One loaded original character: WBM + rig + WBA animation + built mesh + resolved textures.
    public sealed class SkaterCharacter
    {
        public string Role;

        public string ModelName;

        public OriginalModelSet ModelSet;

        public SkaterRig Rig;

        public SkaterAnimationClip Animation;

        public SkaterMeshData Mesh;

        public Mesh UnityMesh;

        // Sub-mesh order is SkaterMeshData.SubmeshTextureNames; "" draws white (untextured faces).
        public string[] SubmeshTextureNames;

        public readonly Dictionary<string, string> TexturePaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public readonly Dictionary<string, BmpTexture> Textures =
            new Dictionary<string, BmpTexture>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> MissingTextures = new List<string>();

        // Poses the mesh at the given animation frame under the character's placement, and returns
        // the bone world matrices that produced it.
        public HbMat4[] PoseFrame(int frame)
        {
            HbMat4[] world = Rig.PoseFrame(Animation.Clips, frame, ObjectTransform());
            Mesh.Pose(world);

            return world;
        }

        // Frame the preview should show for a playback time (see SkaterAnimationClip.FrameAtSeconds).
        public int FrameAtSeconds(double seconds)
        {
            return Animation.FrameAtSeconds(seconds);
        }

        // The bone matrices are in the model's own space plus the preview facing. The lateral
        // placement is the GameObject's transform, not baked into the vertices, so a single mesh
        // can move without re-posing.
        public HbMat4 ObjectTransform()
        {
            return Rig.ObjectTransform(YawRadians);
        }

        // Placement in the preview frame: OffsetX is Unity's lateral axis, YawRadians is the
        // original world's yaw (Z up, which the mesh conversion turns into Unity's Y).
        public float OffsetX;

        public float OffsetY;

        public float OffsetZ;

        public float YawRadians;

        // Digest of the posed mesh over the requested frames: positions, normals and colours of
        // every corner, taken from the same pose function the renderer uses.
        public string PoseDigest(int[] frames)
        {
            ulong hash = SkaterHash.Offset;

            SkaterHash.Add(ref hash, Role);
            SkaterHash.Add(ref hash, ModelName);
            SkaterHash.Add(ref hash, Animation.AssemblyName);
            SkaterHash.Add(ref hash, Animation.WbaName);

            for (int i = 0; i < frames.Length; i++)
            {
                HbMat4[] world = Rig.PoseFrame(Animation.Clips, frames[i]);
                Mesh.Pose(world);

                SkaterHash.Add(ref hash, frames[i]);

                for (int k = 0; k < Mesh.UnityVertexCount; k++)
                {
                    Vector3 position = Mesh.Positions[k];
                    SkaterHash.Add(ref hash, position.x);
                    SkaterHash.Add(ref hash, position.y);
                    SkaterHash.Add(ref hash, position.z);

                    Color32 colour = Mesh.Colours[k];
                    SkaterHash.Add(ref hash, (uint)((colour.r << 16) | (colour.g << 8) | colour.b));
                }
            }

            return SkaterHash.Format(hash);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_SKATER_PREVIEW_OBJECT role=").Append(Role);
            text.Append(" model=").Append(ModelName);
            text.Append(" set=").Append(ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
            text.Append(" lod=").Append(LodIndex);
            text.Append(" lodCount=").Append(Rig.LodCount);
            text.Append(" bones=").Append(Rig.BoneCount);
            text.Append(" meshSlots=").Append(Rig.MeshSlotCount);
            text.Append(" materials=").Append(Mesh.Materials.Length);
            text.Append(" submeshes=").Append(Mesh.SubmeshCount);
            text.Append(" textures=").Append(TexturePaths.Count);
            text.Append(" missingTextures=").Append(MissingTextures.Count);
            text.Append(" sourceVerts=").Append(Mesh.SourceVertices);
            text.Append(" sourceFaces=").Append(Mesh.SourceFaces);
            text.Append(" verts=").Append(Mesh.UnityVertexCount);
            text.Append(" triangles=").Append(Mesh.TriangleCount);
            text.Append(" twoSided=").Append(Mesh.TwoSidedFaces);
            text.Append(" skippedFaces=").Append(Mesh.SkippedFaces);
            text.Append(" warnings=").Append(Mesh.Warnings.Count);
            text.AppendLine();

            foreach (string warning in Mesh.Warnings)
            {
                text.Append("UBLADE3_SKATER_PREVIEW_WARNING role=").Append(Role).Append(' ').AppendLine(warning);
            }

            foreach (string missing in MissingTextures)
            {
                text.Append("UBLADE3_SKATER_PREVIEW_MISSING_TEXTURE role=").Append(Role)
                    .Append(" name=").AppendLine(missing);
            }

            return text.ToString().TrimEnd();
        }

        private int LodIndex
        {
            get { return Mesh.LodIndex; }
        }
    }

    // Everything the N3 preview needs: the player's own data root, both characters, the palette and
    // the shared animation assembly script. Nothing here creates a scene object except the Unity
    // meshes, so the same data drives the Editor capture and the scene-free probe.
    public sealed class SkaterPreviewData
    {
        public const string ExpectedSkaterModel = "CHIOMX";

        public const string ExpectedGoalieModel = "CHIGMX";

        public const string ExpectedSkaterAssembly = "AA_Skate_Std";

        public const string ExpectedGoalieAssembly = "AA_Goalidle";

        // Counts measured on the shipped Mdltxt set and recorded in docs/SKATER-PREVIEW.md.
        public const int ExpectedBoneCount = 16;

        public const int ExpectedLodCount = 2;

        public const int ExpectedMeshBlocks = 15;

        public const int ExpectedSkaterMaterials = 14;

        public const int ExpectedGoalieMaterials = 13;

        public const int ExpectedSkaterTextures = 13;

        public const int ExpectedGoalieTextures = 12;

        // azkskat2 (AA_Skate_Std segment 0) / aplread2 (AA_Goalidle segment 0).
        public const int ExpectedSkaterClips = 16;

        public const int ExpectedSkaterAnimationFrames = 27;

        public const int ExpectedGoalieClips = 16;

        public const int ExpectedGoalieAnimationFrames = 81;

        // The .wba names AnimAssm.txt's segment 0 lines name for the two assemblies.
        public const string ExpectedSkaterWba = "azkskat2";

        public const string ExpectedGoalieWba = "aplread2";

        // AA_Skate_Std: 0..26 at 30 fps, loop 0 (AnimPlayer loops a zero loop count forever).
        // AA_Goalidle: 0..80 at 30 fps, loop 1.
        public const int ExpectedSkaterStartFrame = 0;

        public const int ExpectedSkaterEndFrame = 26;

        public const int ExpectedGoalieStartFrame = 0;

        public const int ExpectedGoalieEndFrame = 80;

        public const float ExpectedStandardFps = 30f;

        // Mdltxt LOD 0 block counts (WBM records) and the mesh the builder produces from them:
        // 195 faces fan to 314 triangles (942 corners); the goalie's 96 faces fan to 165 triangles
        // (495 corners). No shipped skater face names a back material, so nothing is doubled.
        public const int ExpectedSkaterSourceVertices = 224;

        public const int ExpectedSkaterSourceFaces = 195;

        public const int ExpectedSkaterTriangles = 314;

        public const int ExpectedSkaterVertices = 942;

        public const int ExpectedSkaterSubmeshes = 14;

        public const int ExpectedGoalieSourceVertices = 153;

        public const int ExpectedGoalieSourceFaces = 96;

        public const int ExpectedGoalieTriangles = 165;

        public const int ExpectedGoalieVertices = 495;

        public const int ExpectedGoalieSubmeshes = 12;

        // azkskat2/aplread2: the root clip is format 1 (translation only), the other fifteen are
        // format 2 (rotation only, bind translation kept unscaled).
        public const int ExpectedFormat1Clips = 1;

        public const int ExpectedFormat2Clips = 15;

        public const string ExpectedAssemblyName = "AnimAssm.txt";

        public OriginalAssetRoot Root;

        public OriginalPalette Palette;

        public AnimationAssemblyFile Assemblies;

        public SkaterPreviewOptions Options;

        public SkaterCharacter Skater;

        public SkaterCharacter Goalie;

        public string Digest;

        public readonly List<string> Warnings = new List<string>();

        public static SkaterPreviewData Load(OriginalAssetRoot root, SkaterPreviewOptions options)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (options == null)
            {
                options = new SkaterPreviewOptions();
            }

            SkaterPreviewData data = new SkaterPreviewData();
            data.Root = root;
            data.Options = options;

            // The palette the Direct3D material conversion reads (custom.pal + its GIF table, the
            // shipped wbp files are the drome's own and are not applied here).
            data.Palette = OriginalPalette.Load(root);

            data.Assemblies = SkaterAnimationClip.LoadAssemblies(root);

            data.Skater = LoadCharacter(
                root, data, "skater", options.SkaterModel, options.SkaterAssembly);

            if (options.IncludeGoalie)
            {
                data.Goalie = LoadCharacter(
                    root, data, "goalie", options.GoalieModel, options.GoalieAssembly);
            }

            data.Digest = ComputeDigest(data);

            return data;
        }

        private static SkaterCharacter LoadCharacter(
            OriginalAssetRoot root,
            SkaterPreviewData data,
            string role,
            string modelName,
            string assemblyName)
        {
            SkaterCharacter character = new SkaterCharacter();
            character.Role = role;
            character.ModelName = modelName;
            character.Animation = SkaterAnimationClip.Load(root, data.Assemblies, assemblyName);
            character.Rig = SkaterRig.Load(root, modelName, data.Options.ModelSet);
            character.ModelSet = character.Rig.ModelSet;

            character.Mesh = SkaterMeshBuilder.Build(
                root,
                character.Rig.Model,
                character.Rig,
                data.Palette,
                data.Options.LodIndex,
                character.ModelSet,
                data.Options.RemapPalette);

            character.SubmeshTextureNames = character.Mesh.SubmeshTextureNames;

            for (int i = 0; i < character.SubmeshTextureNames.Length; i++)
            {
                string textureName = character.SubmeshTextureNames[i];

                if (string.IsNullOrEmpty(textureName) || character.TexturePaths.ContainsKey(textureName))
                {
                    continue;
                }

                string texturePath = root.FindTextureFile(textureName);

                if (texturePath == null)
                {
                    if (!character.MissingTextures.Contains(textureName))
                    {
                        character.MissingTextures.Add(textureName);
                    }

                    continue;
                }

                character.TexturePaths[textureName] = texturePath;
                character.Textures[textureName] = BmpReader.LoadFile(texturePath);
            }

            foreach (string warning in character.Mesh.Warnings)
            {
                data.Warnings.Add(role + ": " + warning);
            }

            character.UnityMesh = character.Mesh.CreateMesh(modelName + " (" + role + ")");
            character.UnityMesh.name = modelName + " (" + role + ")";

            return character;
        }

        // Four evenly spaced poses per character; the same frames the probe verifies and the
        // capture documents.
        public static int[] SampleFrames(SkaterCharacter character)
        {
            int start = character.Animation.StartFrame;
            int span = character.Animation.DisplayedFrameCount;

            return new int[]
            {
                start,
                start + (span / 3),
                start + ((span * 2) / 3),
                start + span - 1
            };
        }

        private static string ComputeDigest(SkaterPreviewData data)
        {
            ulong hash = SkaterHash.Offset;

            SkaterHash.Add(ref hash, data.Root.DataPath);
            SkaterHash.Add(ref hash, data.Options.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");

            AddCharacter(ref hash, data, data.Skater);

            if (data.Goalie != null)
            {
                AddCharacter(ref hash, data, data.Goalie);
            }

            return SkaterHash.Format(hash);
        }

        private static void AddCharacter(ref ulong hash, SkaterPreviewData data, SkaterCharacter character)
        {
            SkaterHash.Add(ref hash, character.Role);
            SkaterHash.Add(ref hash, character.ModelName);
            SkaterHash.Add(ref hash, character.Rig.ModelPath);
            SkaterHash.Add(ref hash, character.Animation.AssemblyName);
            SkaterHash.Add(ref hash, character.Animation.WbaPath);

            for (int i = 0; i < character.Rig.BoneCount; i++)
            {
                SkaterBone bone = character.Rig.Bones[i];
                SkaterHash.Add(ref hash, bone.LinkId);
                SkaterHash.Add(ref hash, bone.ParentIndex);
                SkaterHash.Add(ref hash, bone.Rest);
                SkaterHash.Add(ref hash, bone.Scale);

                for (int c = 0; c < bone.ChildIds.Length; c++)
                {
                    SkaterHash.Add(ref hash, (int)bone.ChildIds[c]);
                }
            }

            for (int i = 0; i < character.Mesh.Materials.Length; i++)
            {
                DromeMaterialState material = character.Mesh.Materials[i];
                SkaterHash.Add(ref hash, material.ColourRaw);
                SkaterHash.Add(ref hash, material.PaletteIndex);
                SkaterHash.Add(ref hash, material.Ambient);
                SkaterHash.Add(ref hash, material.Diffuse);
                SkaterHash.Add(ref hash, material.Emissive);
                SkaterHash.Add(ref hash, material.BackIndex);
                SkaterHash.Add(ref hash, material.TextureName);
            }

            for (int i = 0; i < character.Animation.Clips.Clips.Length; i++)
            {
                AnimationClip clip = character.Animation.Clips.Clips[i];
                SkaterHash.Add(ref hash, clip.Format);
                SkaterHash.Add(ref hash, clip.AxisOrder);
                SkaterHash.Add(ref hash, clip.DataSize);
            }

            SkaterHash.Add(ref hash, character.Animation.StartFrame);
            SkaterHash.Add(ref hash, character.Animation.EndFrame);
            SkaterHash.Add(ref hash, character.Animation.StandardFps);

            for (int i = 0; i < character.SubmeshTextureNames.Length; i++)
            {
                SkaterHash.Add(ref hash, character.SubmeshTextureNames[i]);
            }

            List<string> textureNames = new List<string>(character.TexturePaths.Keys);
            textureNames.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < textureNames.Count; i++)
            {
                SkaterHash.Add(ref hash, textureNames[i]);
                SkaterHash.Add(ref hash, character.TexturePaths[textureNames[i]]);
            }

            // The mesh arrays the renderer draws: base positions, UVs, bone assignment and face
            // normals, then the posed result over the sample frames.
            for (int i = 0; i < character.Mesh.UnityVertexCount; i++)
            {
                SkaterHash.Add(ref hash, character.Mesh.RestPositions[i].x);
                SkaterHash.Add(ref hash, character.Mesh.RestPositions[i].y);
                SkaterHash.Add(ref hash, character.Mesh.RestPositions[i].z);
                SkaterHash.Add(ref hash, character.Mesh.Uvs[i].x);
                SkaterHash.Add(ref hash, character.Mesh.Uvs[i].y);
                SkaterHash.Add(ref hash, character.Mesh.BoneIndices[i]);
                SkaterHash.Add(ref hash, character.Mesh.MaterialIndices[i]);
                SkaterHash.Add(ref hash, character.Mesh.Reversed[i] ? 1 : 0);
                SkaterHash.Add(ref hash, character.Mesh.Textured[i] ? 1 : 0);
                SkaterHash.Add(ref hash, character.Mesh.LocalNormals[i].x);
                SkaterHash.Add(ref hash, character.Mesh.LocalNormals[i].y);
                SkaterHash.Add(ref hash, character.Mesh.LocalNormals[i].z);
            }

            SkaterHash.Add(ref hash, character.PoseDigest(SampleFrames(character)));
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_SKATER_PREVIEW_DATA root=").Append(Root.DataPath);
            text.Append(" set=").Append(Options.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
            text.Append(" assemblies=").Append(Assemblies.Assemblies.Length);
            text.Append(" paletteEntries=").Append(Palette.LoadedEntryCount);
            text.AppendLine();
            text.AppendLine(Skater.Describe());

            if (Goalie != null)
            {
                text.AppendLine(Goalie.Describe());
            }

            text.Append("UBLADE3_SKATER_PREVIEW_ANIM role=skater ").AppendLine(Skater.Animation.Describe());

            if (Goalie != null)
            {
                text.Append("UBLADE3_SKATER_PREVIEW_ANIM role=goalie ").AppendLine(Goalie.Animation.Describe());
            }

            text.Append("UBLADE3_SKATER_PREVIEW_SUMMARY models=").Append(Goalie == null ? 1 : 2);
            text.Append(" skaterBones=").Append(Skater.Rig.BoneCount);
            text.Append(" goalieBones=").Append(Goalie == null ? 0 : Goalie.Rig.BoneCount);
            text.Append(" skaterClips=").Append(Skater.Animation.Clips.Clips.Length);
            text.Append(" goalieClips=").Append(Goalie == null ? 0 : Goalie.Animation.Clips.Clips.Length);
            text.Append(" skaterFrames=").Append(Skater.Animation.DisplayedFrameCount);
            text.Append(" goalieFrames=").Append(Goalie == null ? 0 : Goalie.Animation.DisplayedFrameCount);
            text.Append(" textures=").Append(Skater.TexturePaths.Count + (Goalie == null ? 0 : Goalie.TexturePaths.Count));
            text.Append(" missingTextures=").Append(Skater.MissingTextures.Count + (Goalie == null ? 0 : Goalie.MissingTextures.Count));
            text.Append(" digest=").Append(Digest);

            return text.ToString();
        }

        public int TotalTriangles
        {
            get
            {
                int total = Skater.Mesh.TriangleCount;

                if (Goalie != null)
                {
                    total += Goalie.Mesh.TriangleCount;
                }

                return total;
            }
        }
    }
}
