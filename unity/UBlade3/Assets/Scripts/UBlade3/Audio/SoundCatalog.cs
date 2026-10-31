using System;
using System.Collections.Generic;
using System.IO;
using UBlade3.Data;

namespace UBlade3.Audio
{
    // The player's original sound catalogue: Data\SNDFILES.LST parsed by the already-verified
    // data layer, plus the Data\Sounds wave files resolved case-insensitively.
    //
    // SNDFILES.LST is read by SoundManager::Load (HYPERX 0x00449210): the first non-comment line
    // is a header ("22 kHzAnnouncer"), m_nSoundCount is the remaining non-comment lines - 1, and
    // each later line scans "%s %d %d %d" (name, duration ms, SoundTypes group, load-mode flag).
    // UBlade3.Data.SoundFilesList already implements those fscanf quirks; this class only joins
    // that list to the wave files.
    //
    // This install carries 295 wave files for 363 list entries: 87 entries have no file (the menu
    // group's "button"/"screenon"/theme entries and many alternate takes are absent from this
    // copy), and 23 wave files are named by no entry. The original tolerates both: a missing entry
    // silently fails to load (AudioMgr::LoadSound increments m_nLoadState), and an unreferenced
    // file is never opened. The probe reports both as shipped-install gaps.
    public sealed class SoundCatalog
    {
        // The folder name under the install's Data folder; GameApp::SetPath builds
        // g_gameApp.m_strSoundsPath as <exe dir>/Data/Sounds (m_strExeDir ends with Data\).
        public const string SoundsDirectoryName = "sounds";

        public const string WaveExtension = ".wav";

        private readonly Dictionary<string, string> filesByName;

        private SoundCatalog(
            GameDataRoot root,
            string soundsPath,
            SoundFilesList list,
            Dictionary<string, string> filesByName,
            List<string> missingEntries,
            List<string> orphanFiles,
            int duplicateNames)
        {
            Root = root;
            SoundsPath = soundsPath;
            List = list;
            this.filesByName = filesByName;
            MissingEntries = missingEntries;
            OrphanFiles = orphanFiles;
            DuplicateNames = duplicateNames;
        }

        public GameDataRoot Root { get; }

        public string SoundsPath { get; }

        public SoundFilesList List { get; }

        // Entry names with no Data\Sounds\<name>.wav on this install, in list order.
        public List<string> MissingEntries { get; }

        // Wave files naming no SNDFILES.LST entry, sorted by file name.
        public List<string> OrphanFiles { get; }

        // Entries whose name appears more than once (four do; both share the same file).
        public int DuplicateNames { get; }

        public int FileCount
        {
            get { return filesByName.Count; }
        }

        public static SoundCatalog Load(GameDataRoot root = null)
        {
            if (root == null)
            {
                root = GameDataRoot.Load();
            }

            string soundsDirectory = root.FindChildDirectory(root.DataPath, SoundsDirectoryName);

            if (soundsDirectory == null)
            {
                throw AudioFormatException.At(root.DataPath, -1, "the Data folder has no sounds directory");
            }

            SoundFilesList list = SoundFilesList.Load(root);
            Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> waves = new List<string>();

            string[] paths;

            try
            {
                paths = Directory.GetFiles(soundsDirectory);
            }
            catch (Exception exception)
            {
                throw AudioFormatException.At(soundsDirectory, -1, "the sounds folder could not be listed: " + exception.Message);
            }

            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.Equals(Path.GetExtension(paths[i]), WaveExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string stem = Path.GetFileNameWithoutExtension(paths[i]);

                waves.Add(stem);

                if (!files.ContainsKey(stem))
                {
                    files.Add(stem, paths[i]);
                }
            }

            HashSet<string> entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> missingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> missing = new List<string>();

            for (int i = 0; i < list.Entries.Count; i++)
            {
                SoundListEntry entry = list.Entries[i];

                entryNames.Add(entry.Name);

                int count;

                if (nameCounts.TryGetValue(entry.Name, out count))
                {
                    nameCounts[entry.Name] = count + 1;
                }
                else
                {
                    nameCounts.Add(entry.Name, 1);
                }

                if (!files.ContainsKey(entry.Name) && missingNames.Add(entry.Name))
                {
                    missing.Add(entry.Name);
                }
            }

            int duplicateNames = 0;

            foreach (KeyValuePair<string, int> pair in nameCounts)
            {
                if (pair.Value > 1)
                {
                    duplicateNames++;
                }
            }

            List<string> orphans = new List<string>();

            for (int i = 0; i < waves.Count; i++)
            {
                if (!entryNames.Contains(waves[i]))
                {
                    orphans.Add(waves[i]);
                }
            }

            orphans.Sort(StringComparer.OrdinalIgnoreCase);

            return new SoundCatalog(root, soundsDirectory, list, files, missing, orphans, duplicateNames);
        }

        public SoundListEntry GetEntry(int index)
        {
            if (index < 0 || index >= List.Entries.Count)
            {
                return null;
            }

            return List.Entries[index];
        }

        public string FindFile(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
            {
                return null;
            }

            string path;

            return filesByName.TryGetValue(entryName, out path) ? path : null;
        }

        public bool TryResolve(int index, out string path)
        {
            path = null;

            SoundListEntry entry = GetEntry(index);

            return entry != null && TryResolve(entry.Name, out path);
        }

        public bool TryResolve(string entryName, out string path)
        {
            path = FindFile(entryName);

            return path != null;
        }

        // Loads one list entry's wave. False means the entry has no file on this install; the
        // error names the entry and file searched. A file that exists but is malformed throws
        // AudioFormatException: a shipped wave is expected to be valid.
        public bool TryLoadWave(int index, out WavFile wave, out string error)
        {
            wave = null;
            error = null;

            SoundListEntry entry = GetEntry(index);

            if (entry == null)
            {
                error = "SNDFILES.LST has no entry " + index;
                return false;
            }

            string path;

            if (!TryResolve(entry.Name, out path))
            {
                error = "entry " + index + " (" + entry.Name + ") has no " + entry.Name + ".wav under " + SoundsPath;
                return false;
            }

            wave = WavFile.LoadFile(path);

            return true;
        }

        public override string ToString()
        {
            return "SoundCatalog(" + List.Entries.Count + " entries, " + filesByName.Count + " files)";
        }
    }
}
