using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UBlade3.PlanVM
{
    /// <summary>A malformed or unreadable plan file. The original logs "Misread binary plan" and carries on.</summary>
    public sealed class PlanFileException : Exception
    {
        public PlanFileException(string message)
        : base(message)
        {
        }
    }

    /// <summary>
    /// A .PBN as it sits in the retail install: a little-endian 32-bit word count, then that many
    /// 32-bit words. The original PlanFile::ReadBinary (HYPERX 0x00436200) reads the count and then
    /// the words, tolerating a short read; plan_graph.py and this port require the exact size so a
    /// damaged install is reported rather than silently reinterpreted.
    /// </summary>
    public sealed class PlanFileImage
    {
        public string Name;

        public string Path;

        public int DeclaredWordCount;

        public PlanWord[] Words;

        public long ByteLength;

        public int WordCount
        {
            get { return Words.Length; }
        }

        public uint RawWord(int index)
        {
            if (index < 0 || index >= Words.Length)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            return unchecked((uint)Words[index].Value);
        }
    }

    /// <summary>
    /// Reads PLANS/*.PBN the way the plan machine does. The same file backs
    /// PlanFile::OpenBinary (HYPERX 0x00436270) and the Probe: path resolution against a HyperBlade
    /// install, exact-size validation, and name-catalog cross-checks.
    /// </summary>
    public static class PlanFileLoader
    {
        public const string PlanExtension = ".pbn";

        public const int CountWordBytes = 4;

        /// <summary>Resolves the game data directory: accepts the install root (with a data\ child) or the data dir itself.</summary>
        public static string ResolveDataDirectory(string pathOrInstallRoot)
        {
            if (string.IsNullOrEmpty(pathOrInstallRoot))
            {
                throw new PlanFileException("No HyperBlade game data path was given.");
            }

            string full = Path.GetFullPath(pathOrInstallRoot);

            if (Directory.Exists(Path.Combine(full, "plans")))
            {
                return full;
            }

            string nested = Path.Combine(full, "data");

            if (Directory.Exists(Path.Combine(nested, "plans")))
            {
                return nested;
            }

            // Case-insensitive fallback: a path written with the retail casing.
            foreach (string candidate in new string[] { full, nested })
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                foreach (string child in Directory.GetDirectories(candidate))
                {
                    if (string.Equals(Path.GetFileName(child), "plans", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            throw new PlanFileException("No plans directory under " + full + " (expected <install>\\data\\plans or <data>\\plans).");
        }

        public static string ResolvePlanDirectory(string dataDirectory)
        {
            string data = ResolveDataDirectory(dataDirectory);

            return ResolveChildDirectory(data, "plans");
        }

        public static string[] EnumeratePlanFiles(string dataDirectory)
        {
            string plans = ResolvePlanDirectory(dataDirectory);
            List<string> found = new List<string>();

            foreach (string path in Directory.GetFiles(plans, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetExtension(path), PlanExtension, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(path);
                }
            }

            found.Sort(delegate(string left, string right)
            {
                string a = Path.GetFileName(left);
                string b = Path.GetFileName(right);
                int cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

                return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.Ordinal);
            });

            return found.ToArray();
        }

        /// <summary>The names in PLANS.CAT, in file order. The original opens the catalog as a name list.</summary>
        public static string[] ReadCatalog(string dataDirectory)
        {
            string data = ResolveDataDirectory(dataDirectory);
            string path = ResolveDataFile(data, "plans.cat");

            if (path == null)
            {
                return new string[0];
            }

            byte[] bytes = File.ReadAllBytes(path);
            char[] chars = new char[bytes.Length];

            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            string[] rows = new string(chars).Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            List<string> names = new List<string>();

            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.EndsWith(PlanExtension, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - PlanExtension.Length);
                }

                names.Add(trimmed.ToUpperInvariant());
            }

            return names.ToArray();
        }

        /// <summary>Loads and validates one plan image. Mirrors PlanFile::ReadBinary/OpenBinary (0x00436200/0x00436270).</summary>
        public static PlanFileImage Load(string path, string name)
        {
            if (!File.Exists(path))
            {
                throw new PlanFileException("Binary Plan " + name + " could not be opened!: " + path);
            }

            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length < CountWordBytes)
            {
                throw new PlanFileException("Misread binary plan " + name + ": " + bytes.Length + " bytes, no word count.");
            }

            uint count = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
            long expected = CountWordBytes + (long)count * CountWordBytes;

            if (bytes.Length != expected)
            {
                throw new PlanFileException(
                    "Misread binary plan " + name + ": file is " + bytes.Length + " bytes but the leading count " + count +
                    " asks for " + expected + " ((count + 1) * 4). The original would read " + count + " words and ignore the rest.");
            }

            PlanWord[] words = new PlanWord[count];

            for (int i = 0; i < words.Length; i++)
            {
                int offset = CountWordBytes + i * CountWordBytes;
                int value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
                words[i] = new PlanWord(value);
            }

            PlanFileImage image = new PlanFileImage();
            image.Name = name;
            image.Path = path;
            image.DeclaredWordCount = unchecked((int)count);
            image.Words = words;
            image.ByteLength = bytes.Length;

            return image;
        }

        /// <summary>Loads one shipped plan by name (case-insensitive) from the plans directory.</summary>
        public static PlanFileImage LoadShipped(string dataDirectory, string planName)
        {
            string plans = ResolvePlanDirectory(dataDirectory);
            string upper = planName.ToUpperInvariant();

            if (upper.EndsWith(PlanExtension, StringComparison.OrdinalIgnoreCase))
            {
                upper = upper.Substring(0, upper.Length - PlanExtension.Length);
            }

            foreach (string path in EnumeratePlanFiles(dataDirectory))
            {
                string file = Path.GetFileNameWithoutExtension(path);

                if (string.Equals(file, upper, StringComparison.OrdinalIgnoreCase))
                {
                    return Load(path, file.ToUpperInvariant());
                }
            }

            throw new PlanFileException("No shipped plan named " + upper + " under " + plans + ".");
        }

        /// <summary>The plan fix files the modern port reads from a pak; the retail data folder ships none.</summary>
        public static int CountFixFiles(string dataDirectory)
        {
            string plans = ResolvePlanDirectory(dataDirectory);
            int count = 0;

            foreach (string path in Directory.GetFiles(plans, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetExtension(path), ".diff", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveChildDirectory(string parent, string name)
        {
            if (!Directory.Exists(parent))
            {
                return null;
            }

            foreach (string child in Directory.GetDirectories(parent))
            {
                if (string.Equals(Path.GetFileName(child), name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static string ResolveDataFile(string dataDirectory, string name)
        {
            if (!Directory.Exists(dataDirectory))
            {
                return null;
            }

            foreach (string path in Directory.GetFiles(dataDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
