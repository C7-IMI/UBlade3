using System;
using System.Collections.Generic;
using System.IO;

namespace UBlade3.Data
{
    public sealed class DataNotFoundException : DataFormatException
    {
        public DataNotFoundException(string message)
            : base(message)
        {
        }
    }

    // The retail game keeps its data in <install>\Data (spelled "data" on this disc). UBlade3 never
    // commits that data; it is located in this priority order, per the port contract:
    //   1. the HYPERBLADE_GAME_DATA environment variable,
    //   2. Assets/StreamingAssets/game-data-path.txt,
    //   3. a caller-supplied default install path.
    // Every file name is resolved case-insensitively because the shipped disc mixes case
    // (teams.cat names "Chico.htm", the file is "chico.htm"; loader string is "teams.cat", the file
    // is "TEAMS.CAT"). Windows hides this; the importer must not.
    public sealed class GameDataRoot
    {
        public const string EnvironmentVariable = "HYPERBLADE_GAME_DATA";

        public const string StreamingAssetsFileName = "game-data-path.txt";

        public const string DataFolderName = "Data";

        private static readonly string[] RequiredFiles =
        {
            "teams.cat",
            "plans.cat",
            "model.lst",
            "animate.lst",
            "sndfiles.lst",
            "infobtxt.lst",
            "jumbo.txt",
            "booie.ini",
            "dftkeybd.cfg",
            "dftmouse.cfg",
            "dftstick.cfg",
            "game.cfg",
            "common.cfg",
            "gauntlet.cfg"
        };

        private Dictionary<string, string> fileIndex;

        private ModelLibrary modelLibrary;

        private GameDataRoot(string installPath, string dataPath)
        {
            InstallPath = installPath;
            DataPath = dataPath;
        }

        // The folder that contains Data\ (for example D:\Games\HyperBlade\HYB).
        public string InstallPath { get; private set; }

        // The Data folder itself.
        public string DataPath { get; private set; }

        public ModelLibrary Models
        {
            get
            {
                if (modelLibrary == null)
                {
                    modelLibrary = ModelLibrary.Load(this);
                }

                return modelLibrary;
            }
        }

        public static GameDataRoot Load(string streamingAssetsPath = null, string defaultInstallPath = null)
        {
            string installPath;
            string dataPath;

            ResolveInstallPath(streamingAssetsPath, defaultInstallPath, out installPath, out dataPath);

            GameDataRoot root = new GameDataRoot(installPath, dataPath);

            root.Validate();

            return root;
        }

        // Resolves the install root without loading anything. Throws DataNotFoundException with every
        // location that was tried when no copy of the game data can be found.
        public static void ResolveInstallPath(string streamingAssetsPath, string defaultInstallPath, out string installPath, out string dataPath)
        {
            List<string> attempted = new List<string>();

            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                string candidate = NormalizeDirectory(fromEnvironment);

                if (TryFindDataDirectory(candidate, out installPath, out dataPath))
                {
                    return;
                }

                attempted.Add("environment variable " + EnvironmentVariable + "=\"" + fromEnvironment + "\" (no Data\\ found)");
            }
            else
            {
                attempted.Add("environment variable " + EnvironmentVariable + " (not set)");
            }

            List<string> checklist = new List<string>();

            if (!string.IsNullOrWhiteSpace(streamingAssetsPath))
            {
                checklist.Add(streamingAssetsPath);
            }

            checklist.AddRange(DiscoverStreamingAssetsFiles());

            foreach (string checklistPath in checklist)
            {
                string filePath = checklistPath;

                if (Directory.Exists(filePath))
                {
                    filePath = Path.Combine(filePath, StreamingAssetsFileName);
                }

                if (!File.Exists(filePath))
                {
                    attempted.Add("streaming-assets path \"" + filePath + "\" (missing)");
                    continue;
                }

                foreach (string line in ReadPathFile(filePath))
                {
                    string candidate = NormalizeDirectory(line);

                    if (TryFindDataDirectory(candidate, out installPath, out dataPath))
                    {
                        return;
                    }

                    attempted.Add("line in \"" + filePath + "\": \"" + line + "\" (no Data\\ found)");
                }
            }

            if (!string.IsNullOrWhiteSpace(defaultInstallPath))
            {
                string candidate = NormalizeDirectory(defaultInstallPath);

                if (TryFindDataDirectory(candidate, out installPath, out dataPath))
                {
                    return;
                }

                attempted.Add("default install path \"" + defaultInstallPath + "\" (no Data\\ found)");
            }
            else
            {
                attempted.Add("default install path (none configured)");
            }

            System.Text.StringBuilder message = new System.Text.StringBuilder();

            message.AppendLine("HyperBlade game data was not found. UBlade3 does not ship the original data; it must be read from a retail HyperBlade install.");
            message.AppendLine("Set the " + EnvironmentVariable + " environment variable to the install root that contains Data\\ (for example D:\\Games\\HyperBlade\\HYB),");
            message.AppendLine("or write that root into Assets/StreamingAssets/" + StreamingAssetsFileName + " (one line, '#' comments allowed).");
            message.AppendLine("Tried:");

            foreach (string attempt in attempted)
            {
                message.AppendLine("  - " + attempt);
            }

            throw new DataNotFoundException(message.ToString().TrimEnd());
        }

        // Accepts either the install root (contains Data\) or the Data folder itself.
        public static bool TryFindDataDirectory(string installRoot, out string installPath, out string dataPath)
        {
            installPath = null;
            dataPath = null;

            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                return false;
            }

            string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // The folder named itself may be the Data folder when the caller pointed one level too deep.
            if (LooksLikeDataFolder(root))
            {
                dataPath = root;
                installPath = Path.GetDirectoryName(root);

                return true;
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(root);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (string child in children)
            {
                if (string.Equals(Path.GetFileName(child), DataFolderName, StringComparison.OrdinalIgnoreCase) && LooksLikeDataFolder(child))
                {
                    dataPath = child;
                    installPath = root;

                    return true;
                }
            }

            return false;
        }

        public static bool LooksLikeDataFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            return FindFileInFolder(folder, "teams.cat") != null;
        }

        // Clear, complete error when a required data file is missing.
        public void Validate()
        {
            List<string> missing = new List<string>();

            foreach (string required in RequiredFiles)
            {
                if (!FileExists(required))
                {
                    missing.Add(required);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            System.Text.StringBuilder message = new System.Text.StringBuilder();

            message.AppendLine("The HyperBlade Data folder is missing required files:");
            message.AppendLine("  " + DataPath);

            foreach (string file in missing)
            {
                message.AppendLine("  missing: " + file);
            }

            throw new DataFormatException(message.ToString().TrimEnd());
        }

        public string ResolveFile(string relativePath)
        {
            string resolved;

            if (!TryResolveFile(relativePath, out resolved))
            {
                throw new DataFormatException("Original data file not found: \"" + relativePath + "\" (Data folder " + DataPath + ").");
            }

            return resolved;
        }

        public bool FileExists(string relativePath)
        {
            string resolved;

            return TryResolveFile(relativePath, out resolved);
        }

        // Case-insensitive resolution. The lookup covers every file below Data\, with and without a
        // leading "Data\" (the original EXE concatenates its own directory and loader names such as
        // "Data\DftStick.cfg").
        public bool TryResolveFile(string relativePath, out string absolutePath)
        {
            absolutePath = null;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string normalized = NormalizeRelativePath(relativePath);

            EnsureIndex();

            if (fileIndex.TryGetValue(normalized, out absolutePath))
            {
                return true;
            }

            if (normalized.StartsWith(DataFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                if (fileIndex.TryGetValue(normalized.Substring(DataFolderName.Length + 1), out absolutePath))
                {
                    return true;
                }
            }
            else if (fileIndex.TryGetValue(DataFolderName + "/" + normalized, out absolutePath))
            {
                return true;
            }

            // Fall back to the filesystem so the first index build failing does not hide a file.
            string direct = Path.Combine(DataPath, normalized.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(direct))
            {
                absolutePath = direct;

                return true;
            }

            return false;
        }

        // Top-level Data files matching a simple "*.ext" pattern, case-insensitively, sorted.
        public List<string> GetFiles(string pattern)
        {
            List<string> matches = new List<string>();

            if (!Directory.Exists(DataPath))
            {
                return matches;
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(DataPath);
            }
            catch (Exception)
            {
                return matches;
            }

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);

                if (MatchesWildcard(name, pattern))
                {
                    matches.Add(name);
                }
            }

            matches.Sort(StringComparer.OrdinalIgnoreCase);

            return matches;
        }

        public static bool MatchesWildcard(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
            {
                return true;
            }

            int star = pattern.IndexOf('*');

            if (star < 0)
            {
                return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);
            }

            string prefix = pattern.Substring(0, star);
            string suffix = pattern.Substring(star + 1);

            return text.Length >= prefix.Length + suffix.Length
                   && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        public byte[] ReadBytes(string relativePath)
        {
            return File.ReadAllBytes(ResolveFile(relativePath));
        }

        public DataTextFile OpenText(string relativePath)
        {
            return DataTextFile.Open(ResolveFile(relativePath));
        }

        public IniDocument OpenIni(string relativePath)
        {
            return IniDocument.Parse(ReadBytes(relativePath), ResolveFile(relativePath));
        }

        // Case-insensitive child directory lookup below Data\, used by the model library.
        public string FindChildDirectory(string parentAbsolutePath, string name)
        {
            if (string.IsNullOrEmpty(parentAbsolutePath) || !Directory.Exists(parentAbsolutePath))
            {
                return null;
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(parentAbsolutePath);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string child in children)
            {
                if (string.Equals(Path.GetFileName(child), name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        public static string FindFileInFolder(string folder, string fileName)
        {
            string direct = Path.Combine(folder, fileName);

            if (File.Exists(direct))
            {
                return direct;
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string file in files)
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }

        public override string ToString()
        {
            return "GameDataRoot(" + DataPath + ")";
        }

        private static IEnumerable<string> DiscoverStreamingAssetsFiles()
        {
            List<string> bases = new List<string>();

            TryAddBase(bases, Environment.CurrentDirectory);
            TryAddBase(bases, AppDomain.CurrentDomain.BaseDirectory);

            List<string> results = new List<string>();

            foreach (string baseDirectory in bases)
            {
                string current = baseDirectory;

                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
                {
                    string candidate = Path.Combine(current, "Assets", "StreamingAssets", StreamingAssetsFileName);

                    if (!results.Contains(candidate))
                    {
                        results.Add(candidate);
                    }

                    // A repository root also holds unity/<project>/Assets/StreamingAssets.
                    string unityRoot = Path.Combine(current, "unity");

                    if (Directory.Exists(unityRoot))
                    {
                        string[] projects;

                        try
                        {
                            projects = Directory.GetDirectories(unityRoot);
                        }
                        catch (Exception)
                        {
                            projects = new string[0];
                        }

                        foreach (string project in projects)
                        {
                            string projectCandidate = Path.Combine(project, "Assets", "StreamingAssets", StreamingAssetsFileName);

                            if (!results.Contains(projectCandidate))
                            {
                                results.Add(projectCandidate);
                            }
                        }
                    }

                    DirectoryInfo parent = Directory.GetParent(current);

                    current = parent == null ? null : parent.FullName;
                }
            }

            return results;
        }

        private static void TryAddBase(List<string> bases, string directory)
        {
            if (!string.IsNullOrEmpty(directory) && !bases.Contains(directory))
            {
                bases.Add(directory);
            }
        }

        private static List<string> ReadPathFile(string filePath)
        {
            List<string> lines = new List<string>();

            try
            {
                foreach (string rawLine in File.ReadAllLines(filePath))
                {
                    string line = rawLine.Trim();

                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    lines.Add(line);
                }
            }
            catch (Exception exception)
            {
                throw new DataFormatException("Could not read \"" + filePath + "\": " + exception.Message);
            }

            return lines;
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string trimmed = path.Trim().Trim('"');

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch (Exception)
            {
                return trimmed;
            }
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            string text = relativePath.Trim().Trim('"').Replace('\\', '/');

            while (text.StartsWith("./", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }

            while (text.StartsWith("/", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            while (text.Contains("//"))
            {
                text = text.Replace("//", "/");
            }

            return text;
        }

        private void EnsureIndex()
        {
            if (fileIndex != null)
            {
                return;
            }

            fileIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(DataPath))
            {
                return;
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(DataPath, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return;
            }

            List<string> all = new List<string>(files);

            foreach (string file in all)
            {
                string relative = file.Substring(DataPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                relative = relative.Replace('\\', '/');

                if (!fileIndex.ContainsKey(relative))
                {
                    fileIndex.Add(relative, file);
                }
            }
        }
    }

    // data\models\mdlshd and mdlttxt hold the .wbm/.wbp model library. The HTM/ENV modfile fields are
    // model names without extension (src/graphics/modelfile.cpp ModelFile::Open appends
    // g_pszModelExtension); "null" means no model.
    public sealed class ModelLibrary
    {
        private readonly HashSet<string> names;

        private ModelLibrary(HashSet<string> names)
        {
            this.names = names;
        }

        public int Count
        {
            get { return names.Count; }
        }

        public static ModelLibrary Load(GameDataRoot root)
        {
            HashSet<string> stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string modelsDirectory = root.FindChildDirectory(root.DataPath, "models");

            if (!string.IsNullOrEmpty(modelsDirectory))
            {
                LoadFolder(stems, modelsDirectory, "mdlshd");
                LoadFolder(stems, modelsDirectory, "mdltxt");

                // Older layouts keep the models directly in models\.
                LoadFolderFiles(stems, modelsDirectory);
            }

            return new ModelLibrary(stems);
        }

        public bool Has(string modelName)
        {
            if (string.IsNullOrEmpty(modelName) || string.Equals(modelName, "null", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return names.Contains(modelName);
        }

        private static void LoadFolder(HashSet<string> stems, string parent, string folderName)
        {
            string folder = FindChild(parent, folderName);

            if (folder != null)
            {
                LoadFolderFiles(stems, folder);
            }
        }

        private static void LoadFolderFiles(HashSet<string> stems, string folder)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                return;
            }

            foreach (string file in files)
            {
                stems.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        private static string FindChild(string parent, string name)
        {
            string[] children;

            try
            {
                children = Directory.GetDirectories(parent);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string child in children)
            {
                if (string.Equals(Path.GetFileName(child), name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
