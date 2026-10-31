using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UBlade3.Data;

namespace UBlade3.Audio
{
    // The audio gate: loads the player's original HyperBlade wave files through Data\SNDFILES.LST
    // and prints a stable, scene independent report. No UnityEngine type is used. Batch mode can
    // call Check(); Describe() is the one line form; Run() is Check() as a process exit code.
    //
    // The game root comes from UBlade3.Data.GameDataRoot, which owns the retail-install contract:
    // HYPERBLADE_GAME_DATA, then Assets/StreamingAssets/game-data-path.txt, then a caller default.
    // Nothing from the original game is committed.
    //
    // Output contract:
    //   UBLADE3_AUDIO_OK files=<n> entries=<n> durationMs=<n> digest=<16 hex>
    //   UBLADE3_AUDIO_DETAIL data=<path> sounds=<path>
    //   UBLADE3_AUDIO_COUNTS <key=value ...>
    //   UBLADE3_AUDIO_FORMATS <rate>/<bits>/<channels>=<count> ...
    //   UBLADE3_AUDIO_ROLES <role-id>=<index>:<list-name> ...
    //   UBLADE3_AUDIO_DIGEST files=.. lists=.. roles=.. total=..
    //   UBLADE3_AUDIO_WARNING <text>      (one per shipped-install/data gap)
    //   UBLADE3_AUDIO_WARNINGS <n>
    //   UBLADE3_AUDIO_CHECK mismatches=<n>
    // and, instead of the first line, UBLADE3_AUDIO_ERROR <reason> when a wave cannot be read.
    public static class AudioProbe
    {
        public const string OkMarker = "UBLADE3_AUDIO_OK";
        public const string ErrorMarker = "UBLADE3_AUDIO_ERROR";
        public const string WarningsMarker = "UBLADE3_AUDIO_WARNINGS";

        // The shipped wave set measured on the retail disc. These constants make a parser or data
        // change fail the gate instead of passing quietly.
        public const int ExpectedWavFiles = 295;
        public const int ExpectedEntries = 363;
        public const int ExpectedResolvedEntries = 276;
        public const int ExpectedMissingEntries = 87;
        public const int ExpectedOrphanFiles = 23;
        public const int ExpectedDuplicateNames = 4;
        public const long ExpectedTotalDataBytes = 35442651;
        public const long ExpectedTotalFrames = 19297803;
        public const long ExpectedTotalDurationMs = 875224;
        public const int ExpectedFactChunkFiles = 31;
        public const int ExpectedFmtChunk18Files = 4;
        public const int ExpectedRiffSizeMismatchFiles = 40;
        public const int ExpectedTrailingByteFiles = 74;
        public const int ExpectedTrailingZeroFiles = 71;
        public const int ExpectedTrailingMetadataFiles = 1;
        public const int ExpectedTrailingSampleBytesFiles = 2;
        public const int ExpectedTrailingBytes = 265;
        public const int ExpectedRoles = 9;
        public const int ExpectedRoleFiles = 9;

        public const int ExpectedFormat22050_8_1 = 157;
        public const int ExpectedFormat22050_8_2 = 49;
        public const int ExpectedFormat22050_16_1 = 59;
        public const int ExpectedFormat22050_16_2 = 23;
        public const int ExpectedFormat44100_16_1 = 1;
        public const int ExpectedFormat11025_8_1 = 6;

        // Set from the first verified run against the shipped disc; a change to the reader or the
        // data moves it and the gate fails. See docs/NATIVE-AUDIO.md.
        public const string ExpectedTotalDigest = "1a8d0a92b89a6019";

        private static Scan cachedScan;

        private static string cachedReport;

        private static Exception cachedError;

        public static string Digest
        {
            get { return ExtractValue(EnsureReport(), OkMarker, "digest"); }
        }

        public static string LastError
        {
            get
            {
                EnsureReport();

                return cachedError == null ? string.Empty : cachedError.Message;
            }
        }

        // Called from a port scene or test through reflection. Never throws and needs no scene.
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

        // Batch-mode entry point: print the report, then throw on a failed load or a count/digest
        // that no longer matches the shipped data so the caller exits non-zero.
        public static void Check()
        {
            string report = EnsureReport();

            Console.Out.WriteLine(report);

            if (cachedError != null)
            {
                throw cachedError is AudioFormatException
                    ? cachedError
                    : new AudioFormatException("UBLADE3_AUDIO_FAILED: " + cachedError.Message, cachedError);
            }

            if (!report.StartsWith(OkMarker, StringComparison.Ordinal))
            {
                throw new AudioFormatException("UBLADE3_AUDIO_FAILED: " + FirstLine(report));
            }

            if (cachedScan.Mismatches.Count > 0)
            {
                throw new AudioFormatException("UBLADE3_AUDIO_FAILED: " + string.Join("; ", cachedScan.Mismatches.ToArray()));
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

                if (!message.StartsWith("UBLADE3_AUDIO_FAILED", StringComparison.Ordinal))
                {
                    message = "UBLADE3_AUDIO_FAILED " + message;
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

            SoundCatalog catalog = SoundCatalog.Load();

            scan.DataPath = catalog.Root.DataPath;
            scan.SoundsPath = catalog.SoundsPath;
            scan.Catalog = catalog;

            Fnv1a64 filesDigest = new Fnv1a64();
            Fnv1a64 listsDigest = new Fnv1a64();
            Fnv1a64 rolesDigest = new Fnv1a64();

            filesDigest.Add("WAVEFILES|");

            string[] waveFiles = Directory.GetFiles(catalog.SoundsPath);

            Array.Sort(waveFiles, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < waveFiles.Length; i++)
            {
                if (!string.Equals(Path.GetExtension(waveFiles[i]), SoundCatalog.WaveExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                WavFile wave = WavFile.LoadFile(waveFiles[i]);
                string key = FormatKey(wave);

                int count;

                scan.Formats.TryGetValue(key, out count);
                scan.Formats[key] = count + 1;

                scan.WavFiles++;
                scan.TotalDataBytes += wave.DataBytes;
                scan.TotalFrames += wave.FrameCount;
                scan.TotalDurationMs += wave.DurationMilliseconds;

                if (wave.HasFactChunk)
                {
                    scan.FactChunkFiles++;
                }

                if (wave.FormatChunkSize == 18)
                {
                    scan.FmtChunk18Files++;
                }

                if (!wave.RiffSizeMatchesFile)
                {
                    scan.RiffSizeMismatchFiles++;
                }

                if (wave.TrailingBytes > 0)
                {
                    scan.TrailingByteFiles++;
                    scan.TrailingBytes += wave.TrailingBytes;

                    if (wave.TrailingAllZero)
                    {
                        scan.TrailingZeroFiles++;
                    }
                    else if (wave.TrailingChunkId != null)
                    {
                        // A well formed chunk the loader never reads (one LIST INFO chunk ships).
                        scan.TrailingMetadataFiles++;
                        scan.TrailingMetadataNames.Add(wave.SourceName);
                    }
                    else
                    {
                        // Real sample bytes beyond the declared data chunk; the original reads only
                        // the data chunk, so these bytes are lost on this disc.
                        scan.TrailingSampleBytesFiles++;
                        scan.TrailingSampleByteNames.Add(Path.GetFileName(wave.SourceName));
                    }
                }

                filesDigest.Add("FILE|");
                filesDigest.Add(Path.GetFileName(wave.SourceName));
                filesDigest.Add(wave.FormatTag);
                filesDigest.Add(wave.FormatChunkSize);
                filesDigest.Add(wave.Channels);
                filesDigest.Add(wave.SampleRate);
                filesDigest.Add(wave.ByteRate);
                filesDigest.Add(wave.BlockAlign);
                filesDigest.Add(wave.BitsPerSample);
                filesDigest.Add(wave.CbSize);
                filesDigest.Add((int)wave.DataOffset);
                filesDigest.Add(wave.DataBytes);
                filesDigest.Add(wave.FrameCount);
                filesDigest.Add((int)wave.DurationMilliseconds);
                filesDigest.Add(wave.RiffSizeMatchesFile ? 1 : 0);

                for (int chunk = 0; chunk < wave.Chunks.Length; chunk++)
                {
                    filesDigest.Add("CHUNK|");
                    filesDigest.Add(wave.Chunks[chunk].Id);
                    filesDigest.Add(wave.Chunks[chunk].Size);
                }

                for (int b = 0; b < wave.RawData.Length; b++)
                {
                    filesDigest.Add(wave.RawData[b]);
                }
            }

            scan.FilesDigest = filesDigest.Value;

            // SNDFILES.LST through the verified data layer's fscanf port, plus the resolved files.
            SoundFilesList list = catalog.List;

            listsDigest.Add("SOUNDLIST|");
            listsDigest.Add(list.HeaderSampleRate);
            listsDigest.Add(list.HeaderName);
            listsDigest.Add(list.DeclaredSoundCount);

            List<string> missingNames = new List<string>();
            HashSet<string> resolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < list.Entries.Count; i++)
            {
                SoundListEntry entry = list.Entries[i];
                string path;

                bool resolved = catalog.TryResolve(entry.Name, out path);

                if (resolved)
                {
                    scan.ResolvedEntries++;
                    resolvedNames.Add(entry.Name);
                }
                else
                {
                    scan.MissingEntries++;
                    missingNames.Add(entry.Name);
                }

                listsDigest.Add("ENTRY|");
                listsDigest.Add(entry.Index);
                listsDigest.Add(entry.Name);
                listsDigest.Add(entry.DurationMs);
                listsDigest.Add(entry.Group);
                listsDigest.Add(entry.LoadMode);
                listsDigest.Add(entry.HasDuration ? 1 : 0);
                listsDigest.Add(entry.HasGroup ? 1 : 0);
                listsDigest.Add(entry.HasFlag ? 1 : 0);
                listsDigest.Add(resolved ? 1 : 0);
                listsDigest.Add(resolved ? Path.GetFileName(path) : string.Empty);
            }

            for (int group = 0; group < SoundFilesList.GroupCount; group++)
            {
                listsDigest.Add("GROUP|");
                listsDigest.Add(group);
                listsDigest.Add(list.GroupFirst[group]);
                listsDigest.Add(list.GroupCounts[group]);
                listsDigest.Add(list.GroupSamples[group]);
            }

            scan.MissingEntries = missingNames.Count;
            scan.OrphanFiles = catalog.OrphanFiles.Count;
            scan.DuplicateNames = catalog.DuplicateNames;

            scan.FilesDigest = filesDigest.Value;
            scan.ListsDigest = listsDigest.Value;

            // The roles UBlade3.Audio plays, checked against the list and the wave files.
            rolesDigest.Add("ROLES|");

            for (int i = 0; i < AudioRole.All.Length; i++)
            {
                AudioRole role = AudioRole.All[i];
                SoundListEntry entry = catalog.GetEntry(role.Index);
                string filePath;

                if (entry == null)
                {
                    scan.Mismatches.Add("role " + role.Id + " index " + role.Index + " is outside SNDFILES.LST");
                    continue;
                }

                if (!string.Equals(entry.Name, role.SoundListName, StringComparison.OrdinalIgnoreCase))
                {
                    scan.Mismatches.Add(
                        "role " + role.Id + " index " + role.Index + " names " + entry.Name + " in SNDFILES.LST, expected " + role.SoundListName);
                }

                WavFile roleWave = null;

                if (catalog.TryResolve(role.SoundListName, out filePath))
                {
                    roleWave = WavFile.LoadFile(filePath);
                    scan.RoleFiles++;
                }

                scan.Roles++;

                rolesDigest.Add("ROLE|");
                rolesDigest.Add(role.Id);
                rolesDigest.Add(role.Index);
                rolesDigest.Add(role.SoundListName);
                rolesDigest.Add(entry.Name);
                rolesDigest.Add(entry.Group);
                rolesDigest.Add(roleWave == null ? 0 : 1);

                if (roleWave != null)
                {
                    rolesDigest.Add(roleWave.Channels);
                    rolesDigest.Add(roleWave.SampleRate);
                    rolesDigest.Add(roleWave.BitsPerSample);
                    rolesDigest.Add(roleWave.FrameCount);
                }
            }

            scan.RolesDigest = rolesDigest.Value;

            Fnv1a64 total = new Fnv1a64();

            total.Add("FILES|");
            total.Add(scan.FilesDigest);
            total.Add("LISTS|");
            total.Add(scan.ListsDigest);
            total.Add("ROLES|");
            total.Add(scan.RolesDigest);
            scan.TotalDigest = total.Value;

            // Shipped-install gaps, each reproduced exactly by the shipped data and tolerated by the
            // original loader; none of them is a parser failure.
            if (missingNames.Count > 0)
            {
                scan.Warnings.Add(
                    "sndfiles.lst: " + missingNames.Count + " of " + list.Entries.Count
                    + " entries have no .wav under Data\\Sounds (" + JoinLimited(missingNames, 8)
                    + "); AudioMgr::LoadSound leaves a missing entry unloaded and silent");
            }

            if (catalog.OrphanFiles.Count > 0)
            {
                scan.Warnings.Add(
                    "sounds: " + catalog.OrphanFiles.Count + " .wav file(s) are not named by sndfiles.lst and are never opened by the original sound manager ("
                    + JoinLimited(catalog.OrphanFiles, 8) + ")");
            }

            if (catalog.DuplicateNames > 0)
            {
                scan.Warnings.Add(
                    "sndfiles.lst: " + catalog.DuplicateNames
                    + " name(s) appear on more than one line; both lines resolve to the same wave file");
            }

            if (scan.TrailingSampleBytesFiles > 0)
            {
                scan.Warnings.Add(
                    "sounds: " + scan.TrailingSampleBytesFiles + " wave file(s) carry sample bytes beyond their declared data chunk ("
                    + JoinLimited(scan.TrailingSampleByteNames, 4) + "); the original reads only the declared data chunk");
            }

            return scan;
        }

        private static string BuildReport(Scan scan)
        {
            CheckExpected(scan, "wavFiles", ExpectedWavFiles, scan.WavFiles);
            CheckExpected(scan, "sndfilesEntries", ExpectedEntries, scan.Catalog.List.Entries.Count);
            CheckExpected(scan, "resolvedEntries", ExpectedResolvedEntries, scan.ResolvedEntries);
            CheckExpected(scan, "missingEntries", ExpectedMissingEntries, scan.MissingEntries);
            CheckExpected(scan, "orphanFiles", ExpectedOrphanFiles, scan.OrphanFiles);
            CheckExpected(scan, "duplicateNames", ExpectedDuplicateNames, scan.DuplicateNames);
            CheckExpected(scan, "totalDataBytes", ExpectedTotalDataBytes, scan.TotalDataBytes);
            CheckExpected(scan, "totalFrames", ExpectedTotalFrames, scan.TotalFrames);
            CheckExpected(scan, "totalDurationMs", ExpectedTotalDurationMs, scan.TotalDurationMs);
            CheckExpected(scan, "factChunkFiles", ExpectedFactChunkFiles, scan.FactChunkFiles);
            CheckExpected(scan, "fmtChunk18Files", ExpectedFmtChunk18Files, scan.FmtChunk18Files);
            CheckExpected(scan, "riffSizeMismatchFiles", ExpectedRiffSizeMismatchFiles, scan.RiffSizeMismatchFiles);
            CheckExpected(scan, "trailingByteFiles", ExpectedTrailingByteFiles, scan.TrailingByteFiles);
            CheckExpected(scan, "trailingZeroFiles", ExpectedTrailingZeroFiles, scan.TrailingZeroFiles);
            CheckExpected(scan, "trailingMetadataFiles", ExpectedTrailingMetadataFiles, scan.TrailingMetadataFiles);
            CheckExpected(scan, "trailingSampleBytesFiles", ExpectedTrailingSampleBytesFiles, scan.TrailingSampleBytesFiles);
            CheckExpected(scan, "trailingBytes", ExpectedTrailingBytes, scan.TrailingBytes);
            CheckExpected(scan, "roles", ExpectedRoles, scan.Roles);
            CheckExpected(scan, "roleFiles", ExpectedRoleFiles, scan.RoleFiles);

            CheckFormat(scan, "22050/8/1", ExpectedFormat22050_8_1);
            CheckFormat(scan, "22050/8/2", ExpectedFormat22050_8_2);
            CheckFormat(scan, "22050/16/1", ExpectedFormat22050_16_1);
            CheckFormat(scan, "22050/16/2", ExpectedFormat22050_16_2);
            CheckFormat(scan, "44100/16/1", ExpectedFormat44100_16_1);
            CheckFormat(scan, "11025/8/1", ExpectedFormat11025_8_1);

            if (ExpectedTotalDigest.Length != 0 && !string.Equals(ExpectedTotalDigest, scan.TotalDigest, StringComparison.Ordinal))
            {
                scan.Mismatches.Add("total digest is " + scan.TotalDigest + ", expected " + ExpectedTotalDigest);
            }

            StringBuilder report = new StringBuilder();

            if (scan.Mismatches.Count == 0)
            {
                report.AppendLine(
                    OkMarker
                    + " files=" + scan.WavFiles.ToString(CultureInfo.InvariantCulture)
                    + " entries=" + scan.Catalog.List.Entries.Count.ToString(CultureInfo.InvariantCulture)
                    + " durationMs=" + scan.TotalDurationMs.ToString(CultureInfo.InvariantCulture)
                    + " digest=" + scan.TotalDigest);
            }
            else
            {
                report.AppendLine(
                    "UBLADE3_AUDIO_FAILED files=" + scan.WavFiles.ToString(CultureInfo.InvariantCulture)
                    + " entries=" + scan.Catalog.List.Entries.Count.ToString(CultureInfo.InvariantCulture)
                    + " durationMs=" + scan.TotalDurationMs.ToString(CultureInfo.InvariantCulture)
                    + " digest=" + scan.TotalDigest);
            }

            report.AppendLine("UBLADE3_AUDIO_DETAIL data=" + scan.DataPath + " sounds=" + scan.SoundsPath);
            report.AppendLine(
                "UBLADE3_AUDIO_COUNTS"
                + " files=" + scan.WavFiles.ToString(CultureInfo.InvariantCulture)
                + " entries=" + scan.Catalog.List.Entries.Count.ToString(CultureInfo.InvariantCulture)
                + " resolvedEntries=" + scan.ResolvedEntries.ToString(CultureInfo.InvariantCulture)
                + " missingEntries=" + scan.MissingEntries.ToString(CultureInfo.InvariantCulture)
                + " orphanFiles=" + scan.OrphanFiles.ToString(CultureInfo.InvariantCulture)
                + " duplicateNames=" + scan.DuplicateNames.ToString(CultureInfo.InvariantCulture)
                + " dataBytes=" + scan.TotalDataBytes.ToString(CultureInfo.InvariantCulture)
                + " frames=" + scan.TotalFrames.ToString(CultureInfo.InvariantCulture)
                + " durationMs=" + scan.TotalDurationMs.ToString(CultureInfo.InvariantCulture)
                + " factChunkFiles=" + scan.FactChunkFiles.ToString(CultureInfo.InvariantCulture)
                + " fmtChunk18Files=" + scan.FmtChunk18Files.ToString(CultureInfo.InvariantCulture)
                + " riffSizeMismatchFiles=" + scan.RiffSizeMismatchFiles.ToString(CultureInfo.InvariantCulture)
                + " trailingByteFiles=" + scan.TrailingByteFiles.ToString(CultureInfo.InvariantCulture)
                + " trailingZeroFiles=" + scan.TrailingZeroFiles.ToString(CultureInfo.InvariantCulture)
                + " trailingMetadataFiles=" + scan.TrailingMetadataFiles.ToString(CultureInfo.InvariantCulture)
                + " trailingSampleBytesFiles=" + scan.TrailingSampleBytesFiles.ToString(CultureInfo.InvariantCulture)
                + " trailingBytes=" + scan.TrailingBytes.ToString(CultureInfo.InvariantCulture)
                + " roles=" + scan.Roles.ToString(CultureInfo.InvariantCulture)
                + " roleFiles=" + scan.RoleFiles.ToString(CultureInfo.InvariantCulture));

            report.AppendLine("UBLADE3_AUDIO_FORMATS " + FormatLine(scan));

            StringBuilder roles = new StringBuilder("UBLADE3_AUDIO_ROLES");

            for (int i = 0; i < AudioRole.All.Length; i++)
            {
                roles.Append(' ').Append(AudioRole.All[i].Id).Append('=')
                     .Append(AudioRole.All[i].Index.ToString(CultureInfo.InvariantCulture)).Append(':')
                     .Append(AudioRole.All[i].SoundListName);
            }

            report.AppendLine(roles.ToString());
            report.AppendLine(
                "UBLADE3_AUDIO_DIGEST files=" + scan.FilesDigest
                + " lists=" + scan.ListsDigest
                + " roles=" + scan.RolesDigest
                + " total=" + scan.TotalDigest);

            for (int i = 0; i < scan.Mismatches.Count; i++)
            {
                report.AppendLine("UBLADE3_AUDIO_EXPECTED_MISMATCH " + Flatten(scan.Mismatches[i]));
            }

            for (int i = 0; i < scan.Warnings.Count; i++)
            {
                report.AppendLine("UBLADE3_AUDIO_WARNING " + Flatten(scan.Warnings[i]));
            }

            report.AppendLine(WarningsMarker + " " + scan.Warnings.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("UBLADE3_AUDIO_CHECK mismatches=" + scan.Mismatches.Count.ToString(CultureInfo.InvariantCulture));

            return report.ToString().TrimEnd();
        }

        private static string FormatLine(Scan scan)
        {
            string[] keys = { "11025/8/1", "22050/8/1", "22050/8/2", "22050/16/1", "22050/16/2", "44100/16/1" };
            StringBuilder line = new StringBuilder();

            for (int i = 0; i < keys.Length; i++)
            {
                int count;

                scan.Formats.TryGetValue(keys[i], out count);

                if (i > 0)
                {
                    line.Append(' ');
                }

                line.Append(keys[i]).Append('=').Append(count.ToString(CultureInfo.InvariantCulture));
            }

            return line.ToString();
        }

        private static string FormatKey(WavFile wave)
        {
            return wave.SampleRate.ToString(CultureInfo.InvariantCulture)
                   + "/" + wave.BitsPerSample.ToString(CultureInfo.InvariantCulture)
                   + "/" + wave.Channels.ToString(CultureInfo.InvariantCulture);
        }

        private static void CheckFormat(Scan scan, string key, int expected)
        {
            int count;

            scan.Formats.TryGetValue(key, out count);

            if (count != expected)
            {
                scan.Mismatches.Add("format " + key + " has " + count + " file(s), expected " + expected);
            }
        }

        private static void CheckExpected(Scan scan, string name, long expected, long actual)
        {
            if (expected != actual)
            {
                scan.Mismatches.Add(name + " is " + actual.ToString(CultureInfo.InvariantCulture) + ", expected " + expected.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string JoinLimited(List<string> values, int limit)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder();

            for (int i = 0; i < values.Count && i < limit; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(values[i]);
            }

            if (values.Count > limit)
            {
                text.Append(", ...");
            }

            return text.ToString();
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

        private sealed class Scan
        {
            internal string DataPath;
            internal string SoundsPath;
            internal SoundCatalog Catalog;

            internal int WavFiles;
            internal int ResolvedEntries;
            internal int MissingEntries;
            internal int OrphanFiles;
            internal int DuplicateNames;
            internal long TotalDataBytes;
            internal long TotalFrames;
            internal long TotalDurationMs;
            internal int FactChunkFiles;
            internal int FmtChunk18Files;
            internal int RiffSizeMismatchFiles;
            internal int TrailingByteFiles;
            internal int TrailingZeroFiles;
            internal int TrailingMetadataFiles;
            internal int TrailingSampleBytesFiles;
            internal int TrailingBytes;
            internal int Roles;
            internal int RoleFiles;

            internal readonly Dictionary<string, int> Formats = new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly List<string> TrailingMetadataNames = new List<string>();
            internal readonly List<string> TrailingSampleByteNames = new List<string>();

            internal string FilesDigest = string.Empty;
            internal string ListsDigest = string.Empty;
            internal string RolesDigest = string.Empty;
            internal string TotalDigest = string.Empty;

            internal readonly List<string> Warnings = new List<string>();
            internal readonly List<string> Mismatches = new List<string>();
        }

        // FNV-1a 64 over a canonical byte stream: every primitive little endian, floats through
        // their bit pattern, so the digest is exact and platform stable.
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

            internal void Add(long value)
            {
                Add((int)(value & 0xffffffffL));
                Add((int)((value >> 32) & 0xffffffffL));
            }
        }
    }
}
