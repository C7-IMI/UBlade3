using System;

namespace UBlade3.Assets
{
    // One colour table row of a DIB, normalised to RGB plus the reserved byte.
    // A Windows BITMAPINFOHEADER stores it as RGBQUAD { blue, green, red, reserved }; an OS/2
    // BITMAPCOREHEADER stores an RGBTRIPLE { red, green, blue }.
    public readonly struct BmpPaletteEntry
    {
        public BmpPaletteEntry(byte red, byte green, byte blue, byte reserved)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Reserved = reserved;
        }

        public byte Red { get; }

        public byte Green { get; }

        public byte Blue { get; }

        public byte Reserved { get; }
    }

    // A decoded bitmap: the original's Data\Textures 24 bit sets and the MDM1 packed 8 bit
    // palette bitmaps under Data\assets8.
    //
    // Read path in the decompilation: GDI::ReadBitmapFile (HYPERX 0x0043fff0) tests the MDM1 tag
    // and either decompresses the rest or GDI::ReadDIB (HYPERX 0x00440200) walks it; the optional
    // 14 byte BITMAPFILEHEADER is skipped, a 12 byte OS/2 core header is widened to the 40 byte
    // Windows layout, the colour table is read as biClrUsed entries (or 2^bitCount for <= 8 bits)
    // and the pixel bytes are read as biSizeImage bytes. The two shipped texture consumers are
    // CD3DTexture::LoadBitmapFile (HYPERX 0x00435310, 24 bit only, untextured software path never
    // shipped its mdmtex.dll) and SoftTex::Load (HYPERX 0x00492920, 8 bit only).
    public sealed class BmpTexture
    {
        // BITMAPFILEHEADER.bfType is "BM" as a little endian word. GDI::ReadDIB's BFT_BITMAP.
        public const ushort FileTypeBm = 0x4d42;

        public const int FileHeaderSize = 14;

        // BITMAPCOREHEADER.bcSize.
        public const int CoreHeaderSize = 12;

        // BITMAPINFOHEADER.biSize.
        public const int InfoHeaderSize = 40;

        // src/graphics/gdi.cpp BI_RGB and the compressed forms. The shipped disc uses RGB, RLE8
        // and RLE4 only.
        public const int CompressionRgb = 0;
        public const int CompressionRle8 = 1;
        public const int CompressionRle4 = 2;
        public const int CompressionBitFields = 3;

        internal BmpTexture(
            string sourceName,
            bool isMdm1Wrapped,
            bool hasFileHeader,
            int headerSize,
            int width,
            int height,
            int sourceHeight,
            bool bottomUp,
            int bitsPerPixel,
            int compression,
            int paletteEntryCount,
            BmpPaletteEntry[] palette,
            byte[] indices,
            byte[] rgba,
            long imageDataOffset,
            int imageDataBytes,
            int rowStride,
            int trailingBytes)
        {
            SourceName = sourceName;
            IsMdm1Wrapped = isMdm1Wrapped;
            HasFileHeader = hasFileHeader;
            HeaderSize = headerSize;
            Width = width;
            Height = height;
            SourceHeight = sourceHeight;
            BottomUp = bottomUp;
            BitsPerPixel = bitsPerPixel;
            Compression = compression;
            PaletteEntryCount = paletteEntryCount;
            Palette = palette;
            Indices = indices;
            Rgba = rgba;
            ImageDataOffset = imageDataOffset;
            ImageDataBytes = imageDataBytes;
            RowStride = rowStride;
            TrailingBytes = trailingBytes;
        }

        public string SourceName { get; }

        // True when the file began with the MDM1 tag and this texture came out of
        // MDM1::Decompress: 1767 shipped assets8 bitmaps do.
        public bool IsMdm1Wrapped { get; }

        // True when a 14 byte BITMAPFILEHEADER was present; a raw DIB (the MDM1 payload) has none.
        public bool HasFileHeader { get; }

        // 12 for BITMAPCOREHEADER, 40 (or larger) for BITMAPINFOHEADER.
        public int HeaderSize { get; }

        public int Width { get; }

        // The absolute value of the stored height.
        public int Height { get; }

        // The stored biHeight: positive rows are stored bottom up, negative top down.
        public int SourceHeight { get; }

        public bool BottomUp { get; }

        public int BitsPerPixel { get; }

        public int Compression { get; }

        // The colour table size the header declares (biClrUsed, or 2^bitCount for <= 8 bits).
        public int PaletteEntryCount { get; }

        public BmpPaletteEntry[] Palette { get; }

        // One palette index per pixel, top row first. Null for direct colour bitmaps.
        public byte[] Indices { get; }

        // RGBA, four bytes per pixel, top row first (row 0 is the top of the image). Alpha is
        // always 255: the original never stores or reads an alpha channel for model textures.
        public byte[] Rgba { get; }

        // The offset of the pixel bytes inside the file (or inside the decompressed stream).
        public long ImageDataOffset { get; }

        // For BI_RGB the computed row stride times the height; for RLE the header's biSizeImage.
        public int ImageDataBytes { get; }

        public int RowStride { get; }

        // Bytes after the pixel data. Every shipped 24 bit texture in Data\Textures carries two
        // zero bytes here, which GDI::ReadBitmapFile never reads; four assets8 DIBs do too.
        public int TrailingBytes { get; }

        public bool IsPaletted
        {
            get { return BitsPerPixel <= 8; }
        }

        public int PixelCount
        {
            get { return Width * Height; }
        }

        // RGBA with row 0 at the bottom, ready for a Unity Texture2D (whose texture origin is the
        // bottom left). Computed on first use.
        public byte[] RgbaBottomUp
        {
            get
            {
                if (rgbaBottomUp == null)
                {
                    byte[] flipped = new byte[Rgba.Length];
                    int rowBytes = Width * 4;

                    for (int row = 0; row < Height; row++)
                    {
                        Array.Copy(Rgba, row * rowBytes, flipped, (Height - 1 - row) * rowBytes, rowBytes);
                    }

                    rgbaBottomUp = flipped;
                }

                return rgbaBottomUp;
            }
        }

        private byte[] rgbaBottomUp;

        public override string ToString()
        {
            return "BmpTexture(" + Width + "x" + Height + ", " + BitsPerPixel + "bpp, compression=" + Compression + ")";
        }
    }

    public static class BmpReader
    {
        public static BmpTexture LoadFile(string path)
        {
            return Load(System.IO.File.ReadAllBytes(path), path);
        }

        // Decodes a BMP file, a raw DIB, or an MDM1 packed DIB (GDI::ReadBitmapFile's three
        // accepted shapes). Malformed input is an AssetFormatException.
        public static BmpTexture Load(byte[] data, string sourceName)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            bool wrapped = false;
            byte[] bytes = data;

            if (Mdm1.HasMagic(data))
            {
                bytes = Mdm1.Decompress(data);
                wrapped = true;
            }

            bool hasFileHeader = bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
            int dibOffset = 0;
            uint fileDataOffset = 0;

            if (hasFileHeader)
            {
                if (bytes.Length < BmpTexture.FileHeaderSize)
                {
                    throw AssetFormatException.At(sourceName, 0, "BMP file header is truncated");
                }

                fileDataOffset = ReadU32(bytes, 10);
                dibOffset = BmpTexture.FileHeaderSize;
            }

            AssetBytes reader = new AssetBytes(bytes, sourceName);

            if (dibOffset + 4 > bytes.Length)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB header is truncated");
            }

            uint headerSize = reader.U32(dibOffset);
            bool coreHeader = headerSize == BmpTexture.CoreHeaderSize;
            int width;
            int sourceHeight;
            int planes;
            int bitsPerPixel;
            int compression;
            uint sizeImage;
            uint clrUsed;

            if (coreHeader)
            {
                // BITMAPCOREHEADER: bcWidth, bcHeight, bcPlanes, bcBitCount as 16 bit words.
                if (dibOffset + BmpTexture.CoreHeaderSize > bytes.Length)
                {
                    throw AssetFormatException.At(sourceName, dibOffset, "OS/2 core header is truncated");
                }

                width = reader.I16(dibOffset + 4);
                sourceHeight = reader.I16(dibOffset + 6);
                planes = reader.U16(dibOffset + 8);
                bitsPerPixel = reader.U16(dibOffset + 10);
                compression = BmpTexture.CompressionRgb;
                sizeImage = 0;
                clrUsed = 0;
            }
            else if (headerSize >= BmpTexture.InfoHeaderSize)
            {
                // BITMAPINFOHEADER and its V4/V5 extensions share the leading fields.
                if (dibOffset + BmpTexture.InfoHeaderSize > bytes.Length)
                {
                    throw AssetFormatException.At(sourceName, dibOffset, "BITMAPINFOHEADER is truncated");
                }

                width = reader.I32(dibOffset + 4);
                sourceHeight = reader.I32(dibOffset + 8);
                planes = reader.U16(dibOffset + 12);
                bitsPerPixel = reader.U16(dibOffset + 14);
                compression = (int)reader.U32(dibOffset + 16);
                sizeImage = reader.U32(dibOffset + 20);
                clrUsed = reader.U32(dibOffset + 32);
            }
            else
            {
                throw AssetFormatException.At(sourceName, dibOffset, "unknown DIB header size " + headerSize);
            }

            if (planes != 1)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB plane count " + planes + " is not 1");
            }

            if (width <= 0)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB width " + width + " is not positive");
            }

            if (sourceHeight == 0)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB height is zero");
            }

            if (bitsPerPixel != 1 && bitsPerPixel != 4 && bitsPerPixel != 8 && bitsPerPixel != 24 && bitsPerPixel != 32)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB bit depth " + bitsPerPixel + " is not one the shipped loader reads");
            }

            if (compression != BmpTexture.CompressionRgb && compression != BmpTexture.CompressionRle8 && compression != BmpTexture.CompressionRle4)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB compression " + compression + " is not supported (the shipped disc uses RGB, RLE8 and RLE4)");
            }

            if (compression == BmpTexture.CompressionRle8 && bitsPerPixel != 8)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "RLE8 needs 8 bits per pixel, not " + bitsPerPixel);
            }

            if (compression == BmpTexture.CompressionRle4 && bitsPerPixel != 4)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "RLE4 needs 4 bits per pixel, not " + bitsPerPixel);
            }

            // GDI::ReadDIB: nColors = biClrUsed, or 2^biBitCount when biClrUsed is zero and the
            // depth is at most eight.
            int paletteEntries;

            if (bitsPerPixel <= 8)
            {
                paletteEntries = clrUsed != 0 ? (int)clrUsed : (1 << bitsPerPixel);
            }
            else
            {
                paletteEntries = 0;
            }

            if (paletteEntries < 0 || paletteEntries > 0x10000)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB colour table size " + paletteEntries + " is implausible");
            }

            int paletteStride = coreHeader ? 3 : 4;
            long paletteAt = dibOffset + headerSize;
            long imageDataOffset = paletteAt + ((long)paletteEntries * paletteStride);

            // BITMAPFILEHEADER.bfOffBits wins when present, exactly as GDI::ReadDIB's final seek
            // does (it seeks to start + bfOffBits and never validates it against the palette).
            bool useFileOffset = hasFileHeader && fileDataOffset != 0;

            if (useFileOffset)
            {
                imageDataOffset = (long)fileDataOffset;
            }

            if (imageDataOffset < 0 || imageDataOffset > bytes.Length)
            {
                throw AssetFormatException.At(sourceName, imageDataOffset, "pixel data offset is outside the file");
            }

            BmpPaletteEntry[] palette = new BmpPaletteEntry[paletteEntries];

            for (int entry = 0; entry < paletteEntries; entry++)
            {
                long at = paletteAt + ((long)entry * paletteStride);

                if (at + paletteStride > bytes.Length)
                {
                    throw AssetFormatException.At(sourceName, at, "colour table runs past the file");
                }

                if (coreHeader)
                {
                    // RGBTRIPLE: red, green, blue.
                    palette[entry] = new BmpPaletteEntry(bytes[at], bytes[at + 1], bytes[at + 2], 0);
                }
                else
                {
                    // RGBQUAD: blue, green, red, reserved.
                    palette[entry] = new BmpPaletteEntry(bytes[at + 2], bytes[at + 1], bytes[at], bytes[at + 3]);
                }
            }

            int height = sourceHeight < 0 ? -sourceHeight : sourceHeight;
            bool bottomUp = sourceHeight > 0;

            // The shipped bitmaps are at most 640x480; the cap keeps every later int product
            // (row stride, pixel count, RGBA byte count) inside 32 bits before the bounds checks.
            if (width > 0x10000 || height > 0x10000 || (long)width * height > int.MaxValue / 4)
            {
                throw AssetFormatException.At(sourceName, dibOffset, "DIB dimensions " + width + "x" + height + " are implausible");
            }

            byte[] indices = null;
            byte[] rgba = new byte[width * height * 4];
            int imageBytes;
            int rowStride;
            int trailing;

            if (compression == BmpTexture.CompressionRgb)
            {
                rowStride = ((width * bitsPerPixel) + 31) / 32 * 4;
                imageBytes = rowStride * height;

                if (imageDataOffset + imageBytes > bytes.Length)
                {
                    throw AssetFormatException.At(
                        sourceName,
                        imageDataOffset,
                        "pixel data needs " + imageBytes + " byte(s) but only " + (bytes.Length - imageDataOffset) + " remain");
                }

                trailing = bytes.Length - (int)(imageDataOffset + imageBytes);

                if (bitsPerPixel <= 8)
                {
                    indices = DecodeIndexedRows(bytes, imageDataOffset, width, height, bitsPerPixel, rowStride, bottomUp);
                }
                else
                {
                    DecodeDirectRows(bytes, imageDataOffset, width, height, bitsPerPixel, rowStride, bottomUp, rgba);
                }
            }
            else
            {
                // RLE: the header's biSizeImage bounds the stream; GDI would need it too.
                if (sizeImage == 0 || sizeImage > int.MaxValue)
                {
                    throw AssetFormatException.At(sourceName, dibOffset, "RLE bitmap has no usable biSizeImage");
                }

                imageBytes = (int)sizeImage;
                rowStride = 0;

                if (imageDataOffset + imageBytes > bytes.Length)
                {
                    throw AssetFormatException.At(sourceName, imageDataOffset, "RLE stream runs past the file");
                }

                trailing = bytes.Length - (int)(imageDataOffset + imageBytes);
                indices = DecodeRle(bytes, sourceName, (int)imageDataOffset, imageBytes, width, height, bitsPerPixel == 4, bottomUp);
            }

            if (indices != null)
            {
                for (int pixel = 0; pixel < indices.Length; pixel++)
                {
                    int index = indices[pixel];
                    BmpPaletteEntry entry = index < palette.Length ? palette[index] : default(BmpPaletteEntry);
                    int at = pixel * 4;

                    rgba[at] = entry.Red;
                    rgba[at + 1] = entry.Green;
                    rgba[at + 2] = entry.Blue;
                    rgba[at + 3] = 255;
                }
            }

            return new BmpTexture(
                sourceName,
                wrapped,
                hasFileHeader,
                (int)headerSize,
                width,
                height,
                sourceHeight,
                bottomUp,
                bitsPerPixel,
                compression,
                paletteEntries,
                palette,
                indices,
                rgba,
                imageDataOffset,
                imageBytes,
                rowStride,
                trailing);
        }

        // Unwraps an MDM1 packed bitmap; null when the data is not MDM1. GDI::ReadBitmapFile uses
        // the same tag test before handing the rest to MDM1::Decompress.
        public static byte[] UnwrapMdm1(byte[] data)
        {
            if (!Mdm1.HasMagic(data))
            {
                return null;
            }

            return Mdm1.Decompress(data);
        }

        // BI_RGB paletted rows: one index per pixel (1, 4 or 8 bits), rows padded to a dword.
        private static byte[] DecodeIndexedRows(byte[] bytes, long imageDataOffset, int width, int height, int bitsPerPixel, int rowStride, bool bottomUp)
        {
            byte[] indices = new byte[width * height];

            for (int row = 0; row < height; row++)
            {
                long sourceRow = imageDataOffset + ((long)row * rowStride);
                int targetRow = bottomUp ? height - 1 - row : row;
                long targetAt = (long)targetRow * width;

                for (int x = 0; x < width; x++)
                {
                    int value;

                    if (bitsPerPixel == 8)
                    {
                        value = bytes[sourceRow + x];
                    }
                    else if (bitsPerPixel == 4)
                    {
                        byte packed = bytes[sourceRow + (x / 2)];

                        value = (x & 1) == 0 ? (packed >> 4) & 0xf : packed & 0xf;
                    }
                    else
                    {
                        byte packed = bytes[sourceRow + (x / 8)];

                        value = (packed >> (7 - (x & 7))) & 1;
                    }

                    indices[targetAt + x] = (byte)value;
                }
            }

            return indices;
        }

        // BI_RGB direct colour rows: three or four bytes per pixel, blue first; the fourth byte of
        // a 32 bit bitmap is ignored (the original never uses an alpha channel).
        private static void DecodeDirectRows(byte[] bytes, long imageDataOffset, int width, int height, int bitsPerPixel, int rowStride, bool bottomUp, byte[] rgba)
        {
            int sourceBytes = bitsPerPixel / 8;

            for (int row = 0; row < height; row++)
            {
                long sourceRow = imageDataOffset + ((long)row * rowStride);
                int targetRow = bottomUp ? height - 1 - row : row;
                long targetAt = (long)targetRow * width * 4;

                for (int x = 0; x < width; x++)
                {
                    long source = sourceRow + ((long)x * sourceBytes);
                    long target = targetAt + ((long)x * 4);

                    rgba[target] = bytes[source + 2];
                    rgba[target + 1] = bytes[source + 1];
                    rgba[target + 2] = bytes[source];
                    rgba[target + 3] = 255;
                }
            }
        }

        // BI_RLE8 / BI_RLE4. Count byte first: a positive count repeats the next byte (8 bit) or
        // alternates the two nibbles of the next byte (4 bit); a zero count starts a command:
        // 0 end of line, 1 end of bitmap, 2 delta (dx, dy), 3..255 absolute pixels.
        private static byte[] DecodeRle(byte[] bytes, string sourceName, int offset, int length, int width, int height, bool fourBit, bool bottomUp)
        {
            byte[] indices = new byte[width * height];
            int at = offset;
            int end = offset + length;
            int x = 0;
            int y = 0;

            while (at < end)
            {
                int count = bytes[at];

                at++;

                if (count > 0)
                {
                    if (at >= end)
                    {
                        throw AssetFormatException.At(sourceName, at, "RLE run is missing its pixel byte");
                    }

                    byte value = bytes[at];

                    at++;

                    for (int i = 0; i < count; i++)
                    {
                        int index = fourBit ? ((i & 1) == 0 ? (value >> 4) & 0xf : value & 0xf) : value;

                        Plot(indices, sourceName, x, y, width, height, bottomUp, index);
                        x++;
                    }

                    continue;
                }

                if (at >= end)
                {
                    throw AssetFormatException.At(sourceName, at, "RLE command is truncated");
                }

                int command = bytes[at];

                at++;

                if (command == 0)
                {
                    x = 0;
                    y++;
                }
                else if (command == 1)
                {
                    break;
                }
                else if (command == 2)
                {
                    if (at + 2 > end)
                    {
                        throw AssetFormatException.At(sourceName, at, "RLE delta is truncated");
                    }

                    x += bytes[at];
                    y += bytes[at + 1];
                    at += 2;
                }
                else
                {
                    int absolute = command;

                    if (fourBit)
                    {
                        int packedBytes = (absolute + 1) / 2;

                        if (at + packedBytes > end)
                        {
                            throw AssetFormatException.At(sourceName, at, "RLE absolute run is truncated");
                        }

                        for (int i = 0; i < absolute; i++)
                        {
                            byte packed = bytes[at + (i / 2)];
                            int index = (i & 1) == 0 ? (packed >> 4) & 0xf : packed & 0xf;

                            Plot(indices, sourceName, x, y, width, height, bottomUp, index);
                            x++;
                        }

                        at += packedBytes;

                        if ((packedBytes & 1) != 0)
                        {
                            at++;
                        }
                    }
                    else
                    {
                        if (at + absolute > end)
                        {
                            throw AssetFormatException.At(sourceName, at, "RLE absolute run is truncated");
                        }

                        for (int i = 0; i < absolute; i++)
                        {
                            Plot(indices, sourceName, x, y, width, height, bottomUp, bytes[at + i]);
                            x++;
                        }

                        at += absolute;

                        if ((absolute & 1) != 0)
                        {
                            at++;
                        }
                    }
                }
            }

            return indices;
        }

        private static void Plot(byte[] indices, string sourceName, int x, int y, int width, int height, bool bottomUp, int index)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                // GDI tolerates a run past the edge; a shipped stream never does, so a stray run
                // here means the stream is corrupt.
                throw AssetFormatException.At(sourceName, 0, "RLE run leaves the " + width + "x" + height + " bitmap at (" + x + ", " + y + ")");
            }

            int row = bottomUp ? height - 1 - y : y;

            indices[(row * width) + x] = (byte)index;
        }

        private static uint ReadU32(byte[] bytes, int at)
        {
            return (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
        }
    }
}
