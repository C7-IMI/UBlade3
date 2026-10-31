using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Assets;

// UnityEngine also defines AnimationClip; the animation the preview plays is the asset layer's.
using AnimationClip = UBlade3.Assets.AnimationClip;

namespace UBlade3.Native
{
    // The N3 validation probe. Scene free: it loads the original skater and goalie, rebuilds every
    // count the preview draws from the .wbm/.wba records, checks the skeleton, the animation
    // mapping, the frame timing and the posed geometry, and prints UBLADE3_SKATER_PREVIEW_* lines.
    // It throws no Unity mesh or scene object, so the same check runs in a batch-mode editor, in a
    // player and in a plain C# harness with a UnityEngine shim.
    public static class SkaterPreviewProbe
    {
        public sealed class Report
        {
            public int Mismatches;

            public string Digest = string.Empty;

            public readonly List<string> Lines = new List<string>();
        }

        public static int Run(OriginalAssetRoot root)
        {
            return Run(root, new SkaterPreviewOptions());
        }

        // Loads the default characters, checks them and returns the mismatch count.
        public static int Run(OriginalAssetRoot root, SkaterPreviewOptions options)
        {
            Report report = Check(root, options);

            for (int i = 0; i < report.Lines.Count; i++)
            {
                Debug.Log(report.Lines[i]);
            }

            Debug.Log("UBLADE3_SKATER_PREVIEW_CHECK mismatches=" + report.Mismatches + " digest=" + report.Digest);

            if (report.Mismatches == 0)
            {
                Debug.Log("UBLADE3_SKATER_PREVIEW_PROBE_OK");
            }
            else
            {
                Debug.LogError("UBLADE3_SKATER_PREVIEW_PROBE_FAILED mismatches=" + report.Mismatches);
            }

            return report.Mismatches;
        }

        public static Report Check(OriginalAssetRoot root, SkaterPreviewOptions options)
        {
            Report report = new Report();

            if (root == null)
            {
                Fail(report, "no original asset root");
                return report;
            }

            if (options == null)
            {
                options = new SkaterPreviewOptions();
            }

            SkaterPreviewData data;

            try
            {
                data = SkaterPreviewData.Load(root, options);
            }
            catch (Exception exception)
            {
                Fail(report, "load: " + exception.GetType().Name + ": " + exception.Message);
                return report;
            }

            report.Digest = data.Digest;

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_PROBE root=" + root.DataPath
                             + " set=" + (options.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd"));

            foreach (string line in data.Describe().Split('\n'))
            {
                report.Lines.Add(line.TrimEnd('\r'));
            }

            if (data.Palette.LoadedEntryCount != OriginalPalette.EntryCount)
            {
                Fail(report, "palette has " + data.Palette.LoadedEntryCount + " entries; the shipped table has "
                             + OriginalPalette.EntryCount);
            }

            if (string.IsNullOrEmpty(data.Digest))
            {
                Fail(report, "preview digest is empty");
            }

            CheckCharacter(report, data, data.Skater, true);

            if (data.Goalie != null)
            {
                CheckCharacter(report, data, data.Goalie, false);
            }
            else if (options.IncludeGoalie)
            {
                Fail(report, "the goalie was requested but no goalie character loaded");
            }

            CheckOtherModelSet(report, root, data.Skater, options);
            CheckOtherModelSet(report, root, data.Goalie, options);

            int triangles = data.TotalTriangles;
            int bones = data.Skater.Rig.BoneCount + (data.Goalie == null ? 0 : data.Goalie.Rig.BoneCount);
            int clips = data.Skater.Animation.Clips.Clips.Length
                        + (data.Goalie == null ? 0 : data.Goalie.Animation.Clips.Clips.Length);
            int displayed = data.Skater.Animation.DisplayedFrameCount
                            + (data.Goalie == null ? 0 : data.Goalie.Animation.DisplayedFrameCount);
            int textures = data.Skater.TexturePaths.Count
                           + (data.Goalie == null ? 0 : data.Goalie.TexturePaths.Count);
            int missing = data.Skater.MissingTextures.Count
                          + (data.Goalie == null ? 0 : data.Goalie.MissingTextures.Count)
                          + data.Warnings.Count;

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_COUNTS models=" + (data.Goalie == null ? 1 : 2)
                             + " meshes=" + (data.Goalie == null ? 1 : 2)
                             + " bones=" + bones
                             + " clips=" + clips
                             + " displayedFrames=" + displayed
                             + " triangles=" + triangles
                             + " verts=" + (triangles * 3)
                             + " textures=" + textures
                             + " missingRefs=" + missing
                             + " digest=" + data.Digest);

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_COUNTS_OK mismatches=" + report.Mismatches);

            return report;
        }

        private static void CheckCharacter(Report report, SkaterPreviewData data, SkaterCharacter character, bool isSkater)
        {
            string role = character.Role;
            SkaterRig rig = character.Rig;
            SkaterMeshData mesh = character.Mesh;
            SkaterAnimationClip animation = character.Animation;

            Use(character, isSkater, out string expectedModel, out string expectedAssembly,
                out string expectedWba, out int expectedFrames, out int expectedMaterials,
                out int expectedTextures, out int expectedStart, out int expectedEnd,
                out int expectedSourceVerts, out int expectedSourceFaces, out int expectedTriangles,
                out int expectedVertices, out int expectedSubmeshes);

            // ---- model / rig ---------------------------------------------------------------
            if (!string.Equals(rig.ModelName, expectedModel, StringComparison.OrdinalIgnoreCase))
            {
                Fail(report, role + " loaded model \"" + rig.ModelName + "\", expected " + expectedModel);
            }

            Expect(report, role, "bone count", rig.BoneCount, SkaterPreviewData.ExpectedBoneCount);
            Expect(report, role, "lod count", rig.LodCount, SkaterPreviewData.ExpectedLodCount);
            Expect(report, role, "mesh slots", rig.MeshSlotCount, SkaterPreviewData.ExpectedMeshBlocks);
            Expect(report, role, "material count", mesh.Materials.Length, expectedMaterials);
            Expect(report, role, "source vertices", mesh.SourceVertices, expectedSourceVerts);
            Expect(report, role, "source faces", mesh.SourceFaces, expectedSourceFaces);
            Expect(report, role, "triangles", mesh.TriangleCount, expectedTriangles);
            Expect(report, role, "vertices", mesh.UnityVertexCount, expectedVertices);
            Expect(report, role, "sub-meshes", mesh.SubmeshCount, expectedSubmeshes);

            if (mesh.UnityVertexCount != mesh.TriangleCount * 3)
            {
                Fail(report, role + " built " + mesh.UnityVertexCount + " corners for "
                              + mesh.TriangleCount + " triangles; every triangle corner must be its own vertex");
            }

            if (mesh.TriangleCount <= 0)
            {
                Fail(report, role + " produced no triangles");
            }

            if (mesh.SkippedFaces != 0)
            {
                Fail(report, role + " skipped " + mesh.SkippedFaces + " faces");
            }

            if (character.TexturePaths.Count != expectedTextures)
            {
                Fail(report, role + " resolved " + character.TexturePaths.Count + " textures, expected " + expectedTextures);
            }

            if (character.MissingTextures.Count != 0)
            {
                Fail(report, role + " is missing " + character.MissingTextures.Count + " textures: "
                              + string.Join(", ", character.MissingTextures.ToArray()));
            }

            if (mesh.Warnings.Count != 0)
            {
                Fail(report, role + " has " + mesh.Warnings.Count + " build warnings");
            }

            for (int i = 0; i < mesh.SubmeshTextureNames.Length; i++)
            {
                string textureName = mesh.SubmeshTextureNames[i];

                if (!string.IsNullOrEmpty(textureName) && !character.TexturePaths.ContainsKey(textureName))
                {
                    Fail(report, role + " sub-mesh " + i + " texture \"" + textureName + "\" did not resolve");
                }
            }

            StringBuilder submeshList = new StringBuilder();

            for (int i = 0; i < mesh.SubmeshTextureNames.Length; i++)
            {
                string name = mesh.SubmeshTextureNames[i];

                if (i != 0)
                {
                    submeshList.Append(' ');
                }

                submeshList.Append(i).Append(':')
                    .Append(string.IsNullOrEmpty(name) ? "(no texture)" : name)
                    .Append('/').Append(mesh.SubmeshCornerCount[i] / 3).Append("tris");
            }

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_SUBMESHES role=" + role
                             + " count=" + mesh.SubmeshCount
                             + " list='" + submeshList.ToString() + "'");

            List<string> textureNames = new List<string>(character.TexturePaths.Keys);
            textureNames.Sort(StringComparer.OrdinalIgnoreCase);

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_TEXTURES role=" + role
                             + " count=" + textureNames.Count
                             + " names='" + string.Join(",", textureNames.ToArray()) + "'");

            // ---- skeleton reference -------------------------------------------------------
            StringBuilder skeleton = new StringBuilder();
            skeleton.Append("UBLADE3_SKATER_PREVIEW_SKELETON role=").Append(role)
                .Append(" bones=").Append(rig.BoneCount)
                .Append(" angle=").Append(Format(rig.Model.Component.AngleDegrees));

            report.Lines.Add(skeleton.ToString());

            for (int i = 0; i < rig.BoneCount; i++)
            {
                SkaterBone bone = rig.Bones[i];
                report.Lines.Add("UBLADE3_SKATER_PREVIEW_BONE role=" + role
                                 + " index=" + i
                                 + " link=" + bone.LinkId
                                 + " parent=" + bone.ParentIndex
                                 + " children=" + bone.ChildIds.Length
                                 + " rest=" + Format(bone.Rest)
                                 + " scale=" + Format(bone.Scale));
            }

            // ---- animation mapping --------------------------------------------------------
            if (!string.Equals(animation.AssemblyName, expectedAssembly, StringComparison.OrdinalIgnoreCase))
            {
                Fail(report, role + " assembly is \"" + animation.AssemblyName + "\", expected " + expectedAssembly);
            }

            if (!string.Equals(animation.WbaName, expectedWba, StringComparison.OrdinalIgnoreCase))
            {
                Fail(report, role + " assembly \"" + animation.AssemblyName + "\" resolves to \""
                              + animation.WbaName + "\", expected " + expectedWba);
            }

            Expect(report, role, "clip count", animation.Clips.Clips.Length, SkaterPreviewData.ExpectedBoneCount);
            Expect(report, role, "animation frames", animation.Clips.AnimationHeader.FrameCount, expectedFrames);
            Expect(report, role, "displayed frames", animation.DisplayedFrameCount, (expectedEnd - expectedStart) + 1);
            Expect(report, role, "start frame", animation.StartFrame, expectedStart);
            Expect(report, role, "end frame", animation.EndFrame, expectedEnd);

            if (Math.Abs(animation.StandardFps - SkaterPreviewData.ExpectedStandardFps) > 0.0001f)
            {
                Fail(report, role + " standard rate is " + animation.StandardFps + ", expected "
                              + SkaterPreviewData.ExpectedStandardFps);
            }

            if (animation.Clips.Clips.Length != rig.BoneCount)
            {
                Fail(report, role + " has " + animation.Clips.Clips.Length + " clips for "
                              + rig.BoneCount + " bones; AnimPlayer::UpdateFrame indexes one clip per bone");
            }

            int format1 = 0;
            int format2 = 0;

            for (int i = 0; i < animation.Clips.Clips.Length; i++)
            {
                AnimationClip clip = animation.Clips.Clips[i];

                if (clip.Format == AnimationClip.FormatTranslations)
                {
                    format1++;
                }
                else if (clip.Format == AnimationClip.FormatSingleTranslationEuler
                         || clip.Format == AnimationClip.FormatSingleTranslationEuler4)
                {
                    format2++;
                }
                else
                {
                    Fail(report, role + " clip " + i + " uses unsupported format " + clip.Format);
                }
            }

            Expect(report, role, "format 1 clips", format1, SkaterPreviewData.ExpectedFormat1Clips);
            Expect(report, role, "format 2 clips", format2, SkaterPreviewData.ExpectedFormat2Clips);

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_ANIM role=" + role
                             + " assembly=" + animation.AssemblyName
                             + " wba=" + animation.WbaName
                             + " clips=" + animation.Clips.Clips.Length
                             + " frameCount=" + animation.Clips.AnimationHeader.FrameCount
                             + " range=" + animation.StartFrame + ".." + animation.EndFrame
                             + " displayedFrames=" + animation.DisplayedFrameCount
                             + " standardFps=" + Format(animation.StandardFps)
                             + " loop=" + animation.Segment.LoopCount
                             + " format1=" + format1
                             + " format2=" + format2);

            // ---- frame timing -------------------------------------------------------------
            CheckTiming(report, character);

            // ---- skeleton/pose ------------------------------------------------------------
            int[] samples = SkaterPreviewData.SampleFrames(character);
            HashSet<string> digests = new HashSet<string>(StringComparer.Ordinal);
            Vector3 firstMin = Vector3.zero;
            Vector3 firstMax = Vector3.zero;

            for (int i = 0; i < samples.Length; i++)
            {
                HbMat4[] world = rig.PoseFrame(animation.Clips, samples[i]);
                mesh.Pose(world);

                if (!Finite(world))
                {
                    Fail(report, role + " frame " + samples[i] + " produced a non finite bone matrix");
                }

                if (!Finite(mesh.BoundsMin) || !Finite(mesh.BoundsMax))
                {
                    Fail(report, role + " frame " + samples[i] + " produced non finite bounds");
                }

                float height = mesh.BoundsMax.y - mesh.BoundsMin.y;

                if (height < 3f || height > 9f)
                {
                    Fail(report, role + " frame " + samples[i] + " is " + Format(height)
                                  + " units tall; a shipped skater is about 5 model units");
                }

                if (mesh.BoundsMin.y < -1.5f || mesh.BoundsMin.y > 1.5f)
                {
                    Fail(report, role + " frame " + samples[i] + " has its lowest vertex at "
                                  + Format(mesh.BoundsMin.y) + " model units; the shipped animation keeps feet on the ground");
                }

                string poseDigest = PoseHash(character, samples[i], world);
                digests.Add(poseDigest);

                report.Lines.Add("UBLADE3_SKATER_PREVIEW_FRAME role=" + role
                                 + " frame=" + samples[i]
                                 + " time=" + Format(samples[i] / animation.StandardFps)
                                 + " boundsMin=" + Format(mesh.BoundsMin)
                                 + " boundsMax=" + Format(mesh.BoundsMax)
                                 + " center=" + Format((mesh.BoundsMin + mesh.BoundsMax) * 0.5f)
                                 + " poseDigest=" + poseDigest);

                if (i == 0)
                {
                    firstMin = mesh.BoundsMin;
                    firstMax = mesh.BoundsMax;
                }
            }

            if (digests.Count < samples.Length - 1)
            {
                Fail(report, role + " only " + digests.Count + " distinct poses in " + samples.Length
                              + " sample frames; the clip should move the mesh");
            }

            // The original skeleton reference at the clip's first frame: the engine's root, pelvis,
            // chest, head and hand world translations.
            HbMat4[] reference = rig.PoseFrame(animation.Clips, samples[0]);

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_POSE role=" + role
                             + " frame=" + samples[0]
                             + " root=" + Joint(reference, 0)
                             + " pelvis=" + Joint(reference, 1)
                             + " chest=" + Joint(reference, 8)
                             + " head=" + Joint(reference, 9)
                             + " leftHand=" + Joint(reference, 12)
                             + " rightHand=" + Joint(reference, 15)
                             + " boundsMin=" + Format(firstMin)
                             + " boundsMax=" + Format(firstMax));

            // The clip must change the geometry between two adjacent frames.
            mesh.Pose(rig.PoseFrame(animation.Clips, animation.StartFrame));
            Vector3[] before = (Vector3[])mesh.Positions.Clone();
            mesh.Pose(rig.PoseFrame(animation.Clips, animation.StartFrame + 1));

            float maxDelta = 0f;

            for (int i = 0; i < before.Length; i++)
            {
                float delta = Vector3.Distance(before[i], mesh.Positions[i]);

                if (delta > maxDelta)
                {
                    maxDelta = delta;
                }
            }

            if (!(maxDelta > 0.002f))
            {
                Fail(report, role + " frame " + animation.StartFrame + " and frame "
                              + (animation.StartFrame + 1) + " differ by at most " + Format(maxDelta)
                              + " model units; the sampled frames do not animate");
            }

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_MOVED role=" + role
                             + " frameA=" + animation.StartFrame
                             + " frameB=" + (animation.StartFrame + 1)
                             + " maxVertexDelta=" + Format(maxDelta));
        }

        // The timing mapping AnimPlayer uses: int(time * standardFps) frames from the segment start,
        // wrapping at the segment end (SkaterAnimationClip.FrameAtSeconds).
        private static void CheckTiming(Report report, SkaterCharacter character)
        {
            SkaterAnimationClip animation = character.Animation;
            int start = animation.StartFrame;
            int span = animation.DisplayedFrameCount;

            if (animation.FrameAtSeconds(0.0) != start)
            {
                Fail(report, character.Role + " time 0 maps to frame " + animation.FrameAtSeconds(0.0)
                              + ", expected " + start);
            }

            StringBuilder samples = new StringBuilder();

            for (int step = 0; step < span * 2; step++)
            {
                double seconds = step / (double)animation.StandardFps;
                int expected = start + (step % span);
                int actual = animation.FrameAtSeconds(seconds);

                if (actual != expected)
                {
                    Fail(report, character.Role + " time " + Format(seconds) + " maps to frame " + actual
                                  + ", expected " + expected);
                    break;
                }

                if (step < 6 || step == span - 1 || step == span)
                {
                    samples.Append(step).Append(':').Append(actual).Append(' ');
                }
            }

            if (animation.FrameAtSeconds(animation.DurationSeconds) != start)
            {
                Fail(report, character.Role + " one clip duration (" + Format(animation.DurationSeconds)
                              + " s) maps to frame " + animation.FrameAtSeconds(animation.DurationSeconds)
                              + ", expected a wrap to " + start);
            }

            report.Lines.Add("UBLADE3_SKATER_PREVIEW_TIMING role=" + character.Role
                             + " standardFps=" + Format(animation.StandardFps)
                             + " span=" + span
                             + " start=" + start
                             + " end=" + animation.EndFrame
                             + " duration=" + Format(animation.DurationSeconds)
                             + " samples=\"" + samples.ToString().TrimEnd() + "\"");
        }

        // The other model set ships a copy of the same model; the preview draws Mdltxt but the probe
        // verifies both sets load with the same rig, so the N2 evidence and this one agree.
        private static void CheckOtherModelSet(Report report, OriginalAssetRoot root, SkaterCharacter character, SkaterPreviewOptions options)
        {
            if (character == null)
            {
                return;
            }

            OriginalModelSet other = options.ModelSet == OriginalModelSet.Textured
                ? OriginalModelSet.Shaded
                : OriginalModelSet.Textured;

            OriginalModelSet found;
            string path = root.FindModelFile(character.ModelName, other, out found);

            if (path == null)
            {
                Fail(report, character.Role + " model \"" + character.ModelName + "\" has no copy in the "
                              + (other == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd") + " set");
                return;
            }

            try
            {
                WbmModel model = WbmReader.LoadFile(path);

                if (model.Component.SubComponentCount != character.Rig.BoneCount)
                {
                    Fail(report, character.Role + " " + other + " copy has "
                                  + model.Component.SubComponentCount + " bones, expected " + character.Rig.BoneCount);
                }

                report.Lines.Add("UBLADE3_SKATER_PREVIEW_MODELSET role=" + character.Role
                                 + " set=" + (found == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd")
                                 + " path=" + path
                                 + " bones=" + model.Component.SubComponentCount
                                 + " materials=" + model.Materials.Length);
            }
            catch (Exception exception)
            {
                Fail(report, character.Role + " " + other + " copy failed to load: " + exception.Message);
            }
        }

        private static void Use(
            SkaterCharacter character,
            bool isSkater,
            out string expectedModel,
            out string expectedAssembly,
            out string expectedWba,
            out int expectedFrames,
            out int expectedMaterials,
            out int expectedTextures,
            out int expectedStart,
            out int expectedEnd,
            out int expectedSourceVerts,
            out int expectedSourceFaces,
            out int expectedTriangles,
            out int expectedVertices,
            out int expectedSubmeshes)
        {
            if (isSkater)
            {
                expectedModel = SkaterPreviewData.ExpectedSkaterModel;
                expectedAssembly = SkaterPreviewData.ExpectedSkaterAssembly;
                expectedWba = SkaterPreviewData.ExpectedSkaterWba;
                expectedFrames = SkaterPreviewData.ExpectedSkaterAnimationFrames;
                expectedMaterials = SkaterPreviewData.ExpectedSkaterMaterials;
                expectedTextures = SkaterPreviewData.ExpectedSkaterTextures;
                expectedStart = SkaterPreviewData.ExpectedSkaterStartFrame;
                expectedEnd = SkaterPreviewData.ExpectedSkaterEndFrame;
                expectedSourceVerts = SkaterPreviewData.ExpectedSkaterSourceVertices;
                expectedSourceFaces = SkaterPreviewData.ExpectedSkaterSourceFaces;
                expectedTriangles = SkaterPreviewData.ExpectedSkaterTriangles;
                expectedVertices = SkaterPreviewData.ExpectedSkaterVertices;
                expectedSubmeshes = SkaterPreviewData.ExpectedSkaterSubmeshes;
            }
            else
            {
                expectedModel = SkaterPreviewData.ExpectedGoalieModel;
                expectedAssembly = SkaterPreviewData.ExpectedGoalieAssembly;
                expectedWba = SkaterPreviewData.ExpectedGoalieWba;
                expectedFrames = SkaterPreviewData.ExpectedGoalieAnimationFrames;
                expectedMaterials = SkaterPreviewData.ExpectedGoalieMaterials;
                expectedTextures = SkaterPreviewData.ExpectedGoalieTextures;
                expectedStart = SkaterPreviewData.ExpectedGoalieStartFrame;
                expectedEnd = SkaterPreviewData.ExpectedGoalieEndFrame;
                expectedSourceVerts = SkaterPreviewData.ExpectedGoalieSourceVertices;
                expectedSourceFaces = SkaterPreviewData.ExpectedGoalieSourceFaces;
                expectedTriangles = SkaterPreviewData.ExpectedGoalieTriangles;
                expectedVertices = SkaterPreviewData.ExpectedGoalieVertices;
                expectedSubmeshes = SkaterPreviewData.ExpectedGoalieSubmeshes;
            }
        }

        private static string PoseHash(SkaterCharacter character, int frame, HbMat4[] world)
        {
            ulong hash = SkaterHash.Offset;
            SkaterHash.Add(ref hash, character.Role);
            SkaterHash.Add(ref hash, frame);

            for (int i = 0; i < world.Length; i++)
            {
                for (int e = 0; e < 16; e++)
                {
                    SkaterHash.Add(ref hash, world[i].M[e]);
                }
            }

            for (int i = 0; i < character.Mesh.UnityVertexCount; i++)
            {
                Vector3 position = character.Mesh.Positions[i];
                SkaterHash.Add(ref hash, position.x);
                SkaterHash.Add(ref hash, position.y);
                SkaterHash.Add(ref hash, position.z);
            }

            return SkaterHash.Format(hash);
        }

        private static void Expect(Report report, string role, string what, int actual, int expected)
        {
            if (actual != expected)
            {
                Fail(report, role + " " + what + " is " + actual + ", expected " + expected);
            }
        }

        private static void Fail(Report report, string message)
        {
            report.Mismatches++;
            report.Lines.Add("UBLADE3_SKATER_PREVIEW_MISMATCH " + message);
        }

        private static string Joint(HbMat4[] world, int index)
        {
            if (index < 0 || index >= world.Length)
            {
                return "none";
            }

            return Format(world[index].M[3], world[index].M[7], world[index].M[11]);
        }

        private static bool Finite(HbMat4[] world)
        {
            for (int i = 0; i < world.Length; i++)
            {
                for (int e = 0; e < 16; e++)
                {
                    float value = world[i].M[e];

                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static string Format(AssetVector3 value)
        {
            return Format(value.X, value.Y, value.Z);
        }

        private static string Format(float x, float y, float z)
        {
            return Format(x) + "," + Format(y) + "," + Format(z);
        }

        private static string Format(Vector3 value)
        {
            return Format(value.x, value.y, value.z);
        }

        private static string Format(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
