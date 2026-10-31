using System;
using System.Collections.Generic;
using System.Globalization;

namespace UBlade3.Assets
{
    // One colour table row of a .wbp palette definition file.
    // src/graphics/colorpalette.cpp ColorPalette::LoadPaletteEntries (HYPERX 0x004264a0) reads
    //   %u %u %u %u   Index<space>Red<space>Green<space>Blue
    // and stores Red/Green/Blue in a PALETTEENTRY. Index is the entry that is replaced.
    public readonly struct WbpPaletteEntry
    {
        public WbpPaletteEntry(int index, byte red, byte green, byte blue)
        {
            Index = index;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public int Index { get; }

        public byte Red { get; }

        public byte Green { get; }

        public byte Blue { get; }
    }

    // A .wbp palette definition. This is not animation data: src/globals.cpp defines
    // g_pszPaletteExtension = "wbp", and ColorPalette::LoadPaletteEntries opens the file with
    // fopen(path, "r"), fscanf's "%d %d" (version, entry count), skips to the end of the line and
    // then reads one "%u %u %u %u" row per entry. EngineObject::InitModel calls it with the
    // object's model name from the active model path, which is where Models\Mdltxt and
    // Models\Mdlshd keep 28 files each (the fourteen dro and fourteen gmx drome models).
    //
    // Version note: the constructor sets m_wVersion = 0x303, and the loader accepts a file version
    // of PALETTE_VERSION_ANY (0x302) as well. Every shipped .wbp says 770 decimal (= 0x302).
    public sealed class WbpPalette
    {
        // PALETTE_VERSION_ANY in src/graphics/colorpalette.cpp.
        public const int AnyVersion = 0x302;

        // ColorPalette::ColorPalette's m_wVersion.
        public const int RuntimeVersion = 0x303;

        internal WbpPalette(string sourceName, int version, int declaredCount, WbpPaletteEntry[] entries, int ignoredTrailingLines)
        {
            SourceName = sourceName;
            Version = version;
            DeclaredCount = declaredCount;
            Entries = entries;
            IgnoredTrailingLines = ignoredTrailingLines;
        }

        public string SourceName { get; }

        // The "%d" version on the first line.
        public int Version { get; }

        // The "%d" entry count on the first line.
        public int DeclaredCount { get; }

        public WbpPaletteEntry[] Entries { get; }

        // Blank or comment lines after the declared entries; none on the shipped disc.
        public int IgnoredTrailingLines { get; }

        public bool IsAcceptedVersion
        {
            get { return Version == AnyVersion || Version == RuntimeVersion; }
        }
    }

    public static class WbpReader
    {
        public static WbpPalette LoadFile(string path)
        {
            return Load(AssetText.ReadAllLatin1(path), path);
        }

        public static WbpPalette Load(string text, string sourceName)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string[] lines = AssetText.SplitLines(text);
            int line = 0;
            int version;
            int count;

            // fscanf("%d %d") over the first non-empty line. The shipped line is
            // "770 064\t\t\t# Version & NumPaletteEntries".
            while (line < lines.Length && lines[line].Trim().Length == 0)
            {
                line++;
            }

            if (line >= lines.Length)
            {
                throw AssetFormatException.At(sourceName, 0, "palette definition file is empty");
            }

            string[] header = Tokenize(lines[line]);

            if (header.Length < 2
                || !int.TryParse(header[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
                || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                throw AssetFormatException.At(sourceName, line, "palette header does not begin with a version and an entry count");
            }

            if (count < 0 || count > 256)
            {
                throw AssetFormatException.At(sourceName, line, "palette entry count " + count + " is outside 0..256");
            }

            line++;

            List<WbpPaletteEntry> entries = new List<WbpPaletteEntry>(count);

            while (line < lines.Length && entries.Count < count)
            {
                string current = lines[line];

                if (current.Trim().Length == 0 || current.TrimStart()[0] == '#')
                {
                    line++;
                    continue;
                }

                string[] fields = Tokenize(current);

                if (fields.Length < 4)
                {
                    throw AssetFormatException.At(sourceName, line, "palette entry line has fewer than four fields");
                }

                int index;
                int red;
                int green;
                int blue;

                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out red)
                    || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out green)
                    || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out blue))
                {
                    throw AssetFormatException.At(sourceName, line, "palette entry line does not begin with four integers");
                }

                if (index < 0 || index > 255)
                {
                    throw AssetFormatException.At(sourceName, line, "palette entry index " + index + " is outside 0..255");
                }

                if (red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255)
                {
                    throw AssetFormatException.At(sourceName, line, "palette entry colour is outside 0..255");
                }

                entries.Add(new WbpPaletteEntry(index, (byte)red, (byte)green, (byte)blue));
                line++;
            }

            if (entries.Count != count)
            {
                throw AssetFormatException.At(sourceName, line, "palette declares " + count + " entries but only " + entries.Count + " were read");
            }

            int ignored = 0;

            while (line < lines.Length)
            {
                if (lines[line].Trim().Length != 0)
                {
                    ignored++;
                }

                line++;
            }

            return new WbpPalette(sourceName, version, count, entries.ToArray(), ignored);
        }

        // Whitespace separated fields; the shipped rows end in a '#' comment after the numbers.
        private static string[] Tokenize(string line)
        {
            int comment = line.IndexOf('#');

            if (comment >= 0)
            {
                line = line.Substring(0, comment);
            }

            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
