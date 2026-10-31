using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBlade3.Native
{
    // MeshMaterial::CalcLightIntensity (HYPERX 0x00433820) answers one of the FaceShade values
    // (src/graphics/meshmaterial.h enum FaceShade: SHADE_BLACK, SHADE_WHITE, SHADE_SCALED).
    public enum OriginalShade : byte
    {
        Black = 0,
        White = 1,
        Scaled = 2
    }

    public readonly struct OriginalRgb
    {
        public OriginalRgb(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public byte Red { get; }

        public byte Green { get; }

        public byte Blue { get; }

        public override string ToString()
        {
            return Red + "," + Green + "," + Blue;
        }
    }

    // The original 256 entry colour palette exactly as the shipped software renderer used it:
    // CUSTOM.PAL's text header (ramps and team ranges) plus the referenced GIF global colour table
    // (ColorPalette::LoadCustomPalette, HYPERX 0x00425ec0), and the per-model .wbp replacements
    // (ColorPalette::LoadPaletteEntries, HYPERX 0x004264a0).
    //
    // The shipped game's default renderer is the software one (its Direct3D texture support needed
    // mdmtex.dll, which never shipped), and the software path shades a face by walking the palette
    // ramps: SWRend::DrawFace (HYPERX 0x00496e00) calls LookupColorIntensity (HYPERX 0x004263a0)
    // with the shade MeshMaterial::CalcLightIntensity answered, then writes the returned palette
    // INDEX into the 8 bit framebuffer. The displayed colour is the palette entry at that index.
    public sealed class OriginalPalette
    {
        // ColorPalette::ColorPalette sets m_wVersion = 0x303; LoadPaletteEntries accepts a .wbp
        // that claims PALETTE_VERSION_ANY = 0x302 (colorpalette.cpp lines 20-21 and 741-749).
        public const int RuntimeVersion = 0x303;

        public const int AnyVersion = 0x302;

        public const int EntryCount = 256;

        private readonly byte[] red = new byte[EntryCount];
        private readonly byte[] green = new byte[EntryCount];
        private readonly byte[] blue = new byte[EntryCount];

        private OriginalPalette()
        {
            // ColorPalette::ColorPalette's field defaults (colorpalette.cpp lines 38-49); the
            // shipped CUSTOM.PAL repeats every one of them.
            RampFirst = 0x20;
            RampLast = 0xEF;
            SmallRampFirst = 0x80;
            SmallRampLast = 0xEF;
            LargeRampFirst = 0x20;
            LargeRampLast = 0x7F;
            SmallRampLength = 7;
            LargeRampLength = 0xF;
            ArenaFirst = 0x20;
            ArenaCount = 0x40;
            ObjectFirst = 0x60;
            ObjectCount = 0x20;
            TeamAFirst = 0x80;
            TeamACount = 0x20;
            TeamBFirst = 0xA0;
            TeamBCount = 0x20;
            RokFirst = 0xC0;
            RokCount = 8;
            OpenFirst = 0xC8;
            OpenCount = 8;
            GeneralFirst = 0xD0;
            GeneralCount = 0x10;
            RemapFirst = TeamAFirst;
            RemapLast = TeamAFirst + TeamACount - 1;
        }

        // The two CUSTOM.PAL sources, for the log.
        public string CustomPalettePath { get; private set; }

        public string GifPath { get; private set; }

        // CUSTOM.PAL declares 771 = 0x303 on this disc.
        public int Version { get; private set; }

        public int DeclaredEntryCount { get; private set; }

        public int LoadedEntryCount { get; private set; }

        // CUSTOM.PAL's ramp/ranges block, in file order (colorpalette.cpp lines 484-613).
        public int RampFirst { get; private set; }

        public int RampLast { get; private set; }

        public int SmallRampFirst { get; private set; }

        public int SmallRampLast { get; private set; }

        public int LargeRampFirst { get; private set; }

        public int LargeRampLast { get; private set; }

        // RampLength: small in First, large in Count (zero based).
        public int SmallRampLength { get; private set; }

        public int LargeRampLength { get; private set; }

        public int ArenaFirst { get; private set; }

        public int ArenaCount { get; private set; }

        public int ObjectFirst { get; private set; }

        public int ObjectCount { get; private set; }

        public int TeamAFirst { get; private set; }

        public int TeamACount { get; private set; }

        public int TeamBFirst { get; private set; }

        public int TeamBCount { get; private set; }

        public int RokFirst { get; private set; }

        public int RokCount { get; private set; }

        public int OpenFirst { get; private set; }

        public int OpenCount { get; private set; }

        // Not read by the loader (the file ends at Open); kept for the record.
        public int GeneralFirst { get; private set; }

        public int GeneralCount { get; private set; }

        // m_remapRange = (m_teamARange.nFirst, m_teamARange.nCount + nFirst - 1).
        public int RemapFirst { get; private set; }

        public int RemapLast { get; private set; }

        public int WbpFilesApplied { get; private set; }

        public int WbpEntriesApplied { get; private set; }

        // The engine's g_bDromePass: Renderer::DrawAllMeshes (HYPERX 0x00442d20) sets it while it
        // relights the drome during loading/network frames. Normal gameplay frames run it false,
        // where LookupColorIntensity walks the ramp linearly; the drome pass raises the ramp base
        // to the light level instead (colorpalette.cpp lines 691-706). The preview renders the
        // normal frame, but the flag is modelled so the difference is visible in the source.
        public bool DromePass { get; set; }

        public static OriginalPalette Load(OriginalAssetRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            OriginalPalette palette = new OriginalPalette();

            string customPath = root.ResolveDataFile("CUSTOM.PAL");

            if (customPath == null)
            {
                customPath = root.FindFileInDirectory(root.DataPath, "CUSTOM.PAL");
            }

            if (customPath == null)
            {
                throw new OriginalAssetException("CUSTOM.PAL was not found in " + root.DataPath + ".");
            }

            string gifName;
            palette.ReadCustomPaletteHeader(customPath, out gifName);
            palette.CustomPalettePath = customPath;

            string gifPath = root.ResolveDataFile(gifName);

            if (gifPath == null)
            {
                gifPath = root.FindFileInDirectory(root.DataPath, gifName);
            }

            if (gifPath == null)
            {
                throw new OriginalAssetException(
                    "The palette definition " + customPath + " names \"" + gifName
                    + "\", but that file is not in " + root.DataPath + ".");
            }

            palette.GifPath = gifPath;
            palette.ReadGifColourTable(gifPath);

            return palette;
        }

        // LoadCustomPalette, HYPERX 0x00425ec0: %s name, then %hu version %hu count, then ten
        // "%d %d" range pairs, each read after a skip to the end of its line.
        private void ReadCustomPaletteHeader(string path, out string gifName)
        {
            byte[] bytes = File.ReadAllBytes(path);
            PaletteTextScanner scanner = new PaletteTextScanner(bytes, path);

            gifName = scanner.ReadToken();

            if (string.IsNullOrEmpty(gifName))
            {
                throw new OriginalAssetException("Palette definition " + path + " has no GIF file name.");
            }

            scanner.SkipToEndOfLine();
            Version = (int)scanner.ReadUnsigned("palette version");
            DeclaredEntryCount = (int)scanner.ReadUnsigned("palette entry count");

            if (Version != RuntimeVersion)
            {
                throw new OriginalAssetException(
                    "Palette definition file " + path + " contains wrong version "
                    + Version.ToString("X", CultureInfo.InvariantCulture) + "; the engine expects "
                    + RuntimeVersion.ToString("X", CultureInfo.InvariantCulture) + ".");
            }

            scanner.SkipToEndOfLine();

            RampFirst = scanner.ReadSigned("RampBounds lower");
            RampLast = scanner.ReadSigned("RampBounds upper");
            scanner.SkipToEndOfLine();

            SmallRampFirst = scanner.ReadSigned("SmallRampBounds lower");
            SmallRampLast = scanner.ReadSigned("SmallRampBounds upper");
            scanner.SkipToEndOfLine();

            LargeRampFirst = scanner.ReadSigned("LargeRampBounds lower");
            LargeRampLast = scanner.ReadSigned("LargeRampBounds upper");
            scanner.SkipToEndOfLine();

            SmallRampLength = scanner.ReadSigned("RampLength small");
            LargeRampLength = scanner.ReadSigned("RampLength large");
            scanner.SkipToEndOfLine();

            ArenaFirst = scanner.ReadSigned("Arenas first");
            ArenaCount = scanner.ReadSigned("Arenas count");
            scanner.SkipToEndOfLine();

            ObjectFirst = scanner.ReadSigned("Objects first");
            ObjectCount = scanner.ReadSigned("Objects count");
            scanner.SkipToEndOfLine();

            TeamAFirst = scanner.ReadSigned("Team A first");
            TeamACount = scanner.ReadSigned("Team A count");
            scanner.SkipToEndOfLine();

            TeamBFirst = scanner.ReadSigned("Team B first");
            TeamBCount = scanner.ReadSigned("Team B count");
            scanner.SkipToEndOfLine();

            RokFirst = scanner.ReadSigned("Rok first");
            RokCount = scanner.ReadSigned("Rok count");
            scanner.SkipToEndOfLine();

            OpenFirst = scanner.ReadSigned("Open first");
            OpenCount = scanner.ReadSigned("Open count");
            // The loader stops here: the General line follows but is never read.
            GeneralFirst = TeamBFirst + TeamBCount;
            GeneralCount = 8;
        }

        // CGPalette::ReadGifScreen (HYPERX 0x004496e0). The shipped palette is a 256 entry GIF
        // global colour table; only the table is read, the image data is irrelevant.
        private void ReadGifColourTable(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length < 13)
            {
                throw new OriginalAssetException("Palette GIF " + path + " is shorter than a GIF screen descriptor.");
            }

            string signature = Encoding.ASCII.GetString(bytes, 0, 3);
            string version = Encoding.ASCII.GetString(bytes, 3, 3);

            if (signature != "GIF")
            {
                throw new OriginalAssetException(
                    "Palette colour table " + path + " is not a GIF (signature \"" + signature + "\").");
            }

            int flags = bytes[10];

            if ((flags & 0x80) == 0)
            {
                throw new OriginalAssetException(
                    "Palette GIF " + path + " (" + signature + version + ") has no global colour table.");
            }

            int count = 1 << ((flags & 0x07) + 1);

            if (count < 1 || count > EntryCount)
            {
                throw new OriginalAssetException(
                    "Palette GIF " + path + " declares " + count + " colours; " + EntryCount + " are expected.");
            }

            if (bytes.Length < 13 + (count * 3))
            {
                throw new OriginalAssetException(
                    "Palette GIF " + path + " ends inside its global colour table.");
            }

            for (int i = 0; i < count; i++)
            {
                int offset = 13 + (i * 3);
                red[i] = bytes[offset];
                green[i] = bytes[offset + 1];
                blue[i] = bytes[offset + 2];
            }

            LoadedEntryCount = count;
            DeclaredEntryCount = DeclaredEntryCount == 0 ? count : DeclaredEntryCount;
        }

        // ColorPalette::LoadPaletteEntries (HYPERX 0x004264a0): "%d %d" version and count, a skip
        // to the line end, then count rows of "%u %u %u %u" index, red, green, blue, each followed
        // by a skip to its line end. A version that is neither 0x303 nor 0x302 is reported and the
        // file's entries are ignored (the original does not stop).
        public bool ApplyWbpFile(string path, bool remap)
        {
            if (path == null)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            PaletteTextScanner scanner = new PaletteTextScanner(bytes, path);

            int version = scanner.ReadSigned("palette version");
            int count = scanner.ReadSigned("palette entry count");
            scanner.SkipToEndOfLine();

            if (version != RuntimeVersion && version != AnyVersion)
            {
                WbpVersionErrors++;
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                long index = scanner.ReadUnsigned("palette entry index");
                long r = scanner.ReadUnsigned("palette entry red");
                long g = scanner.ReadUnsigned("palette entry green");
                long b = scanner.ReadUnsigned("palette entry blue");
                scanner.SkipToEndOfLine();

                int target = (int)index;

                if (remap)
                {
                    if (target < RemapFirst || target > RemapLast)
                    {
                        target &= 0xFF;
                    }
                    else
                    {
                        target = (TeamACount + target) & 0xFF;
                    }
                }

                target &= 0xFF;

                red[target] = (byte)r;
                green[target] = (byte)g;
                blue[target] = (byte)b;
                WbpEntriesApplied++;
            }

            WbpFilesApplied++;

            return true;
        }

        public int WbpVersionErrors { get; private set; }

        public OriginalRgb Entry(int index)
        {
            int i = index & 0xFF;

            return new OriginalRgb(red[i], green[i], blue[i]);
        }

        public bool IsInRamp(int colour)
        {
            return colour >= RampFirst && colour <= RampLast;
        }

        // ColorPalette::LookupColorIntensity (HYPERX 0x004263a0). Answers the palette INDEX the
        // software renderer displays, not an RGB triple.
        public byte LookupColorIntensity(OriginalShade shade, float intensity, int colour)
        {
            switch (shade)
            {
                case OriginalShade.Black:
                    return (byte)colour;

                case OriginalShade.White:
                    if (colour >= LargeRampFirst && colour <= LargeRampLast)
                    {
                        return (byte)(LargeRampLength + colour);
                    }

                    return (byte)(SmallRampLength + colour);

                case OriginalShade.Scaled:
                    if (DromePass)
                    {
                        if (colour < LargeRampFirst || colour > LargeRampLast)
                        {
                            int rampOut = SmallRampLength + 1;

                            return (byte)((int)Math.Pow(rampOut, intensity) + colour - 1);
                        }

                        int rampIn = LargeRampLength + 1;

                        return (byte)((int)Math.Pow(rampIn, intensity) + colour - 1);
                    }

                    if (colour >= LargeRampFirst && colour <= LargeRampLast)
                    {
                        return (byte)((int)(LargeRampLength * intensity) + colour);
                    }

                    return (byte)((int)(SmallRampLength * intensity) + colour);
            }

            return 0;
        }

        // EngineObject::InitModel (HYPERX 0x00470790): nShifted = teamA count + colour when the
        // remap is on and the material's low channel sits in m_remapRange, else the channel itself.
        // The result is what the software path stores in MeshMaterial::nColour's low byte
        // (colorpalette.cpp lines 809-845).
        public int ApplyMaterialRemap(int colour, bool remap)
        {
            int index = colour & 0xFF;

            if (remap && index >= RemapFirst && index <= RemapLast)
            {
                index = TeamACount + index;
            }

            return index & 0xFF;
        }

        // ColorPalette::GetRemappedEntry (HYPERX 0x00426980): the Direct3D path replaces a ramp
        // base index with the bright end of its ramp to recover the material's RGB.
        public OriginalRgb GetRemappedEntry(int index)
        {
            if (index >= RampFirst && index <= RampLast)
            {
                if (index >= LargeRampFirst && index <= LargeRampLast)
                {
                    return Entry(LargeRampLength + index);
                }

                return Entry(SmallRampLength + index);
            }

            return Entry(index);
        }

        // A tiny fscanf replacement matching PaletteTextScanner's consumer usage.
        private sealed class PaletteTextScanner
        {
            private readonly byte[] bytes;
            private readonly string source;
            private int position;

            public PaletteTextScanner(byte[] bytes, string source)
            {
                this.bytes = bytes;
                this.source = source;
            }

            public string ReadToken()
            {
                SkipWhitespace();

                int start = position;

                while (position < bytes.Length && !IsWhitespace(bytes[position]))
                {
                    position++;
                }

                return Encoding.ASCII.GetString(bytes, start, position - start);
            }

            public long ReadUnsigned(string field)
            {
                SkipWhitespace();

                int start = position;

                while (position < bytes.Length && bytes[position] >= '0' && bytes[position] <= '9')
                {
                    position++;
                }

                if (position == start)
                {
                    throw new OriginalAssetException(
                        source + " byte " + position + ": expected the " + field + " as an unsigned integer.");
                }

                long value = 0;

                for (int i = start; i < position; i++)
                {
                    value = (value * 10) + (bytes[i] - '0');
                }

                return value;
            }

            public int ReadSigned(string field)
            {
                SkipWhitespace();

                bool negative = false;

                if (position < bytes.Length && (bytes[position] == '-' || bytes[position] == '+'))
                {
                    negative = bytes[position] == '-';
                    position++;
                }

                long value = ReadUnsigned(field);

                int result = (int)(negative ? -value : value);

                return result;
            }

            public void SkipToEndOfLine()
            {
                while (position < bytes.Length && bytes[position] != '\n')
                {
                    position++;
                }

                // Leave the newline consumed; the next reader skips whitespace anyway.
                if (position < bytes.Length)
                {
                    position++;
                }
            }

            private void SkipWhitespace()
            {
                while (position < bytes.Length && IsWhitespace(bytes[position]))
                {
                    position++;
                }
            }

            private static bool IsWhitespace(byte value)
            {
                return value == 0x20 || value == 0x09 || value == 0x0A || value == 0x0D || value == 0x0B || value == 0x0C;
            }
        }
    }

    // Thrown when the player's original data cannot supply the drome preview (not a format
    // defect in shipped files; the data layer's DataFormatException still covers parsing).
    public sealed class OriginalAssetException : Exception
    {
        public OriginalAssetException(string message)
            : base(message)
        {
        }
    }
}
