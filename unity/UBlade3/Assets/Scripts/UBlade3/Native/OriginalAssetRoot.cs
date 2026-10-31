using System;
using System.Collections.Generic;
using System.IO;
using UBlade3.Data;

namespace UBlade3.Native
{
    // The two original model sets the shipped disc carries.
    // src/app/gameapp.cpp GameApp::SetModelPath (HYPERX 0x00418c10) selects
    // "Models\Mdltxt" for the Direct3D/textured path (the 0x004bba8c string) and
    // "Models\Mdlshd" for the software/shaded path (the 0x004bba7c string); the two model
    // folders are sibling children of the install's Data folder. UBlade3 never copies them.
    public enum OriginalModelSet
    {
        Shaded = 0,
        Textured = 1
    }

    // Locates the player's own HyperBlade install and the original asset folders the N2 drome
    // renderer reads. Path resolution itself is the already-verified data layer
    // (UBlade3.Data.GameDataRoot): HYPERBLADE_GAME_DATA first, then
    // Assets/StreamingAssets/game-data-path.txt, then the caller's local default. This class adds
    // only the model/texture folder resolution the asset readers need, with the case-insensitive
    // lookups the shipped disc requires.
    public sealed class OriginalAssetRoot
    {
        // The drome preview's local default install (the retail layout: <root>\Data).
        public const string DefaultInstallPath = @"D:\Games\HyperBlade\HYB";

        // GameDataRoot already documents the variable; repeated here so the renderer's contract is
        // readable in one place.
        public const string EnvironmentVariable = GameDataRoot.EnvironmentVariable;

        public const string DefaultDromeFile = "CHIDRO.ENV";

        private readonly Dictionary<string, Dictionary<string, string>> directoryIndex =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private OriginalAssetRoot(GameDataRoot data)
        {
            Data = data;
            ModelsPath = FindChildDirectory(Data.DataPath, "Models");
            ShadedModelsPath = FindChildDirectory(ModelsPath, "Mdlshd");
            TexturedModelsPath = FindChildDirectory(ModelsPath, "Mdltxt");
            TexturesPath = FindChildDirectory(Data.DataPath, "Textures");
            AnimDataPath = FindChildDirectory(Data.DataPath, "AnimData");
        }

        public GameDataRoot Data { get; }

        public string InstallPath
        {
            get { return Data.InstallPath; }
        }

        public string DataPath
        {
            get { return Data.DataPath; }
        }

        // <install>\Data\Models (child directory names resolved case-insensitively).
        public string ModelsPath { get; }

        public string ShadedModelsPath { get; }

        public string TexturedModelsPath { get; }

        public string TexturesPath { get; }

        public string AnimDataPath { get; }

        public static OriginalAssetRoot Load(string streamingAssetsPath, string defaultInstallPath)
        {
            return new OriginalAssetRoot(GameDataRoot.Load(streamingAssetsPath, defaultInstallPath));
        }

        // Convenience for the Editor/MonoBehaviour callers that have Unity's StreamingAssets
        // folder; GameDataRoot also discovers it on its own when the argument is null.
        public static OriginalAssetRoot Load(string streamingAssetsPath)
        {
            return Load(streamingAssetsPath, DefaultInstallPath);
        }

        public string ModelDirectory(OriginalModelSet set)
        {
            return set == OriginalModelSet.Textured ? TexturedModelsPath : ShadedModelsPath;
        }

        // EngineObject::InitModel (HYPERX 0x00470790) asks the texture manager for the model by its
        // ObjectDef modfile name through the active model path. A missing preferred set falls back
        // to the other one (documented in docs/DROME-PREVIEW.md); null means the object may carry
        // no model at all ("null").
        public string FindModelFile(string modelName, OriginalModelSet preferredSet, out OriginalModelSet foundSet)
        {
            foundSet = preferredSet;

            if (string.IsNullOrEmpty(modelName))
            {
                return null;
            }

            string path = FindFileInDirectory(ModelDirectory(preferredSet), modelName + ".wbm");

            if (path != null)
            {
                return path;
            }

            OriginalModelSet other = preferredSet == OriginalModelSet.Shaded
                ? OriginalModelSet.Textured
                : OriginalModelSet.Shaded;

            path = FindFileInDirectory(ModelDirectory(other), modelName + ".wbm");

            if (path != null)
            {
                foundSet = other;
            }

            return path;
        }

        // ColorPalette::LoadPaletteEntries (HYPERX 0x004264a0) builds "<modelpath>\<name>.wbp".
        // The drome palette files live beside the models (28 per set: the fourteen *.wbp dro
        // files and fourteen gmx files; ASSET-FORMATS section 6).
        public string FindPaletteFile(string modelName, OriginalModelSet set)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                return null;
            }

            string path = FindFileInDirectory(ModelDirectory(set), modelName + ".wbp");

            if (path != null)
            {
                return path;
            }

            OriginalModelSet other = set == OriginalModelSet.Shaded
                ? OriginalModelSet.Textured
                : OriginalModelSet.Shaded;

            return FindFileInDirectory(ModelDirectory(other), modelName + ".wbp");
        }

        // The model texture name table is one-based; CD3DTextureManager loads
        // Data\Textures\<name>.bmp (24 bits per pixel on the shipped disc).
        public string FindTextureFile(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                return null;
            }

            return FindFileInDirectory(TexturesPath, textureName + ".bmp");
        }

        // Relative to the Data folder, case-insensitively, through the data layer's verified
        // index ("common.cfg" finds COMMON.CFG).
        public string ResolveDataFile(string relativePath)
        {
            string resolved;

            return Data.TryResolveFile(relativePath, out resolved) ? resolved : null;
        }

        public string FindFileInDirectory(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory))
            {
                return null;
            }

            Dictionary<string, string> index = GetDirectoryIndex(directory);

            string path;

            if (index.TryGetValue(fileName, out path))
            {
                return path;
            }

            return null;
        }

        // Every file in the directory, sorted, as absolute paths. Used by the preview probe to
        // count a model set without guessing its size.
        public List<string> ListFiles(string directory, string extension)
        {
            List<string> files = new List<string>();

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return files;
            }

            Dictionary<string, string> index = GetDirectoryIndex(directory);

            foreach (string path in index.Values)
            {
                if (string.IsNullOrEmpty(extension)
                    || path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(path);
                }
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);

            return files;
        }

        private Dictionary<string, string> GetDirectoryIndex(string directory)
        {
            Dictionary<string, string> index;

            if (directoryIndex.TryGetValue(directory, out index))
            {
                return index;
            }

            index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    index[Path.GetFileName(file)] = file;
                }
            }
            catch (Exception)
            {
                // A missing/unreadable folder answers no files; the caller reports the miss.
            }

            directoryIndex[directory] = index;

            return index;
        }

        private string FindChildDirectory(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent))
            {
                return null;
            }

            string resolved = Data.FindChildDirectory(parent, name);

            return resolved;
        }

        public override string ToString()
        {
            return "OriginalAssetRoot(" + DataPath + ")";
        }
    }
}
