using System;
using System.Collections.Generic;
using System.IO;

namespace UBlade3.Data
{
    public sealed class IniEntry
    {
        public readonly string Key;
        public readonly string Value;
        public readonly int LineNumber;

        public IniEntry(string key, string value, int lineNumber)
        {
            Key = key;
            Value = value;
            LineNumber = lineNumber;
        }
    }

    public sealed class IniSection
    {
        public readonly string Name;
        public readonly int LineNumber;
        public readonly List<IniEntry> Entries = new List<IniEntry>();

        public IniSection(string name, int lineNumber)
        {
            Name = name;
            LineNumber = lineNumber;
        }

        public bool TryGetValue(string key, out string value)
        {
            foreach (IniEntry entry in Entries)
            {
                if (DataParse.EqualsIgnoreCase(entry.Key, key))
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }

    // booie.ini reader with the semantics of the Win32 profile API the original calls
    // (GetPrivateProfileIntA / GetPrivateProfileStringA, src/engine/gameoptions.cpp
    // HYPERX 0x004798d0 and 0x00479960; the modern reference is
    // hyperblade-20XX/src/common/inifile.cpp):
    //   - section and key names ignore case,
    //   - a missing file, section or key yields the caller's default,
    //   - a found value is trimmed, a negative integer reads as zero,
    //   - only whole-line comments are recognised; there are no inline comments.
    public sealed class IniDocument
    {
        private readonly List<IniSection> sections = new List<IniSection>();

        private IniDocument(string path)
        {
            Path = path ?? string.Empty;
        }

        public string Path { get; private set; }

        public IList<IniSection> Sections
        {
            get { return sections; }
        }

        public static IniDocument Parse(byte[] bytes, string path)
        {
            IniDocument document = new IniDocument(path);
            List<string> lines = SplitLines(bytes, 0, bytes == null ? 0 : bytes.Length);

            IniSection current = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();

                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                {
                    continue;
                }

                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = new IniSection(line.Substring(1, line.Length - 2).Trim(), i + 1);
                    document.sections.Add(current);
                    continue;
                }

                int equals = lines[i].IndexOf('=');

                if (equals < 0 || current == null)
                {
                    continue;
                }

                string key = lines[i].Substring(0, equals).Trim();
                string value = lines[i].Substring(equals + 1).Trim();

                if (key.Length == 0)
                {
                    continue;
                }

                current.Entries.Add(new IniEntry(key, value, i + 1));
            }

            return document;
        }

        public static IniDocument Parse(DataTextFile file)
        {
            return Parse(ReadAllBytes(file), file.Path);
        }

        public bool TryGetSection(string name, out IniSection section)
        {
            foreach (IniSection candidate in sections)
            {
                if (DataParse.EqualsIgnoreCase(candidate.Name, name))
                {
                    section = candidate;
                    return true;
                }
            }

            section = null;
            return false;
        }

        public bool TryGetString(string section, string key, out string value)
        {
            IniSection found;

            if (!TryGetSection(section, out found))
            {
                value = null;
                return false;
            }

            return found.TryGetValue(key, out value);
        }

        public string GetString(string section, string key, string defaultValue)
        {
            string value;

            return TryGetString(section, key, out value) ? value : defaultValue;
        }

        // GetPrivateProfileIntA: a found value goes through atoi (no digits reads as 0) and a
        // negative value reads as zero; a missing value returns the default.
        public int GetInt(string section, string key, int defaultValue)
        {
            string value;

            if (!TryGetString(section, key, out value))
            {
                return defaultValue;
            }

            return ParseProfileInt(value);
        }

        public static int ParseProfileInt(string text)
        {
            if (text == null)
            {
                return 0;
            }

            int i = 0;

            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }

            bool negative = false;

            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                negative = text[i] == '-';
                i++;
            }

            long value = 0;
            int digits = 0;

            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                if (value < 214748364L)
                {
                    value = value * 10 + (text[i] - '0');
                }

                i++;
                digits++;
            }

            if (digits == 0)
            {
                return 0;
            }

            if (negative)
            {
                return 0;
            }

            return (int)value;
        }

        public static List<string> SplitLines(byte[] bytes, int offset, int count)
        {
            List<string> lines = new List<string>();
            List<char> current = new List<char>();
            int end = offset + count;

            for (int i = offset; i < end; i++)
            {
                char c = (char)bytes[i];

                if (c == '\r')
                {
                    if (i + 1 < end && bytes[i + 1] == '\n')
                    {
                        i++;
                    }

                    lines.Add(new string(current.ToArray()));
                    current.Clear();
                }
                else if (c == '\n')
                {
                    lines.Add(new string(current.ToArray()));
                    current.Clear();
                }
                else
                {
                    current.Add(c);
                }
            }

            if (current.Count > 0)
            {
                lines.Add(new string(current.ToArray()));
            }

            return lines;
        }

        private static byte[] ReadAllBytes(DataTextFile file)
        {
            // DataTextFile does not expose its bytes; re-open from the path instead.
            return File.ReadAllBytes(file.Path);
        }
    }
}
