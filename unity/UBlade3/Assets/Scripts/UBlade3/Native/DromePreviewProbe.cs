using System;
using System.Collections.Generic;
using UnityEngine;
using UBlade3.Assets;
using UBlade3.Data;

namespace UBlade3.Native
{
    // The N2 validation probe: it re-derives the expected geometry from the loaded WBM records and
    // checks the Unity meshes against it, then checks the shipped-CHIDRO inventory constants from
    // docs/DATA-FORMATS.md section 5.2. Every line starts with UBLADE3_DROME_PREVIEW_.
    public static class DromePreviewProbe
    {
        public sealed class Report
        {
            public int Mismatches;

            public readonly List<string> Lines = new List<string>();
        }

        // Loads chidro.env in both render paths, checks each and returns the mismatch count.
        public static int Run(OriginalAssetRoot root, string dromeFile)
        {
            Report shaded = CheckMode(root, DromeShadingMode.Shaded, dromeFile);
            Report textured = CheckMode(root, DromeShadingMode.Textured, dromeFile);

            int mismatches = shaded.Mismatches + textured.Mismatches;

            Debug.Log("UBLADE3_DROME_PREVIEW_CHECK mismatches=" + mismatches);
            Debug.Log(mismatches == 0 ? "UBLADE3_DROME_PREVIEW_CHECK_OK" : "UBLADE3_DROME_PREVIEW_CHECK_FAILED");

            return mismatches;
        }

        public static Report CheckMode(OriginalAssetRoot root, DromeShadingMode mode, string dromeFile)
        {
            DromeSceneData data;

            try
            {
                data = DromeSceneLoader.Load(root, mode, dromeFile);
            }
            catch (Exception exception)
            {
                Report failed = new Report();
                failed.Lines.Add("UBLADE3_DROME_PREVIEW_FAILED mode=" + Mode(mode) + " load: " + exception.Message);
                failed.Mismatches++;
                Debug.Log(failed.Lines[0]);
                return failed;
            }

            Report report = Check(data);
            report.Lines.Insert(0, "UBLADE3_DROME_PREVIEW_CHECK mode=" + Mode(mode).ToUpperInvariant()
                                   + " objects=" + data.Drome.Objects.Count
                                   + " modelObjects=" + data.Objects.Count
                                   + " meshes=" + data.MeshCount
                                   + " sourceVerts=" + data.SourceVertices
                                   + " sourceFaces=" + data.SourceFaces
                                   + " builtVerts=" + data.BuiltVertices
                                   + " triangles=" + data.BuiltTriangles
                                   + " textures=" + data.TextureFiles.Count
                                   + " missingTextures=" + data.MissingTextures.Count
                                   + " digest=" + data.Digest
                                   + " mismatches=" + report.Mismatches);

            foreach (string line in report.Lines)
            {
                Debug.Log(line);
            }

            return report;
        }

        public static Report Check(DromeSceneData data)
        {
            Report report = new Report();
            string mode = Mode(data.Mode);

            if (data.Drome.Objects.Count != DromeSceneLoader.ExpectedObjectCount)
            {
                Mismatch(report, mode, "object count " + data.Drome.Objects.Count
                    + " != shipped " + DromeSceneLoader.ExpectedObjectCount);
            }

            if (data.Objects.Count != DromeSceneLoader.ExpectedModelObjectCount)
            {
                Mismatch(report, mode, "model object count " + data.Objects.Count
                    + " != shipped " + DromeSceneLoader.ExpectedModelObjectCount
                    + " (objects with a modfile other than null)");
            }

            if (data.MeshCount != data.Objects.Count)
            {
                Mismatch(report, mode, "mesh count " + data.MeshCount + " != model object count " + data.Objects.Count);
            }

            if (data.MissingModels.Count != 0)
            {
                Mismatch(report, mode, data.MissingModels.Count + " models missing");
            }

            if (data.MissingTextures.Count != 0)
            {
                Mismatch(report, mode, data.MissingTextures.Count + " textures missing: "
                    + string.Join(", ", data.MissingTextures.ToArray()));
            }

            if (data.Palette.LoadedEntryCount != OriginalPalette.EntryCount)
            {
                Mismatch(report, mode, "palette has " + data.Palette.LoadedEntryCount + " entries");
            }

            if (!data.Palette.IsInRamp(data.Palette.RampFirst) || data.Palette.RampLast != 223)
            {
                Mismatch(report, mode, "palette ramp bounds " + data.Palette.RampFirst + ".." + data.Palette.RampLast
                    + " are not the shipped 32..223");
            }

            if (data.BuiltVertices != data.BuiltTriangles * 3)
            {
                Mismatch(report, mode, "built vertices " + data.BuiltVertices
                    + " != 3 * triangles " + (data.BuiltTriangles * 3) + " (flat corners are duplicated)");
            }

            foreach (DromeObjectData item in data.Objects)
            {
                string objectName = item.Definition.Name;

                if (item.Model == null)
                {
                    Mismatch(report, mode, objectName + " has no model");
                    continue;
                }

                if (item.Built == null || item.Built.Mesh == null)
                {
                    Mismatch(report, mode, objectName + " has no mesh");
                    continue;
                }

                OriginalModelSet expectedSet = data.Mode == DromeShadingMode.Textured
                    ? OriginalModelSet.Textured
                    : OriginalModelSet.Shaded;

                if (item.ModelSet != expectedSet)
                {
                    Mismatch(report, mode, objectName + " loaded the "
                        + (item.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd")
                        + " model set, expected " + (expectedSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd"));
                }

                if (item.Built.SourceFaces <= 0 || item.Built.SourceVertices <= 0)
                {
                    Mismatch(report, mode, objectName + " has an empty source block");
                }

                if (item.Built.BuiltVertices != item.Built.BuiltTriangles * 3)
                {
                    Mismatch(report, mode, objectName + " built vertices " + item.Built.BuiltVertices
                        + " != 3 * triangles " + (item.Built.BuiltTriangles * 3));
                }

                if (item.Built.Mesh.subMeshCount != item.Built.SubmeshTextureNames.Length)
                {
                    Mismatch(report, mode, objectName + " has " + item.Built.Mesh.subMeshCount
                        + " mesh sub-meshes but " + item.Built.SubmeshTextureNames.Length + " material groups");
                }

                int expectedTriangles = ExpectedTriangles(item.Model, item.Materials);

                if (item.Built.BuiltTriangles != expectedTriangles)
                {
                    Mismatch(report, mode, objectName + " triangles " + item.Built.BuiltTriangles
                        + " != the WBM's face fan total " + expectedTriangles);
                }

                if (!IsFinite(item.BoundsMin) || !IsFinite(item.BoundsMax))
                {
                    Mismatch(report, mode, objectName + " bounds are not finite");
                }

                // Every textured sub-mesh must name a texture that resolved.
                if (data.Mode == DromeShadingMode.Textured)
                {
                    foreach (string textureName in item.Built.SubmeshTextureNames)
                    {
                        if (!string.IsNullOrEmpty(textureName) && !data.TextureFiles.ContainsKey(textureName))
                        {
                            Mismatch(report, mode, objectName + " texture \"" + textureName + "\" did not resolve");
                        }
                    }
                }
            }

            if (data.Mode == DromeShadingMode.Textured && data.TextureFiles.Count == 0)
            {
                Mismatch(report, mode, "the textured path resolved no textures; every textured material would draw textureless");
            }

            if (string.IsNullOrEmpty(data.Digest))
            {
                Mismatch(report, mode, "mesh digest is empty");
            }

            report.Lines.Add("UBLADE3_DROME_PREVIEW_CHECK_MODE mode=" + mode + " mismatches=" + report.Mismatches);

            return report;
        }

        // The face fan's triangle count, derived independently of the builder: a front fan of n
        // corners is n-2 triangles, and a two sided material adds the reversed copy.
        private static int ExpectedTriangles(WbmModel model, DromeMaterialTable materials)
        {
            int triangles = 0;
            ModelLod lod = model.Lods[0];

            foreach (MeshBlock block in lod.Blocks)
            {
                foreach (MeshFace face in block.Faces)
                {
                    int corners = face.Corners.Length;

                    if (corners < 3 || face.MaterialIndex >= materials.Materials.Length)
                    {
                        continue;
                    }

                    triangles += corners - 2;

                    DromeMaterialState front = materials.Materials[face.MaterialIndex];

                    if (front.BackIndex >= 0 && front.BackIndex < materials.Materials.Length)
                    {
                        triangles += corners - 2;
                    }
                }
            }

            return triangles;
        }

        private static void Mismatch(Report report, string mode, string message)
        {
            report.Mismatches++;
            report.Lines.Add("UBLADE3_DROME_PREVIEW_MISMATCH mode=" + mode + " " + message);
        }

        private static string Mode(DromeShadingMode mode)
        {
            return mode == DromeShadingMode.Shaded ? "shaded" : "textured";
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
