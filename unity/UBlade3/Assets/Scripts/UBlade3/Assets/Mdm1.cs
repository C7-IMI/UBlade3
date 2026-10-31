using System;

namespace UBlade3.Assets
{
    // MDM1: the LZSS packing every bitmap and wave on the disc may carry. This is a field for field
    // port of the decompilation's decoder, with the 20XX reference build used as the readable
    // oracle for the hand written assembly.
    //
    // Sources of truth:
    //   third_party/hyperblade-decomp/src/graphics/mdm1.h/.cpp
    //     MDM1::Decompress             HYPERX 0x00440140
    //     MDM1::ReadCallback           HYPERX 0x004401e0
    //     MDM1::LZSSDecompress         HYPERX 0x00493f80   (naked __asm)
    //     MDM1::RefillInputWindow      HYPERX 0x0049403a   (naked __asm)
    //   third_party/hyperblade-20XX/src/graphics/mdm1.cpp  (clean C++ port of the same algorithm)
    //
    // Container: 4 byte tag "MDM1" (0x314d444d little endian), then a 4 byte signed size, then the
    // LZSS stream. The decoder writes exactly the declared size.
    //
    // LZSS coding, as the assembly documents it: a 16 bit flag word governs sixteen items, its low
    // bit first (each shift of the flag register is one item and a set carry means a reference). A
    // clear bit is a literal byte from the stream. A set bit is a 16 bit reference word: bits 0-3
    // are the length nibble and bits 4-15 the distance, so length = nibble + 3 and source =
    // output position - distance. A length of 0x12 (nibble 0xf) is long form: one more byte is
    // added to the length. If that addition carries past 0xff the stream ends with the written
    // count, except when the wrapped length is 0x10, which copies nothing and continues.
    public static class Mdm1
    {
        // "MDM1" read as a little endian dword: src/graphics/mdm1.h #define MDM1_MAGIC.
        public const uint Magic = 0x314D444D;

        // Magic plus the declared output size.
        public const int HeaderBytes = 8;

        // The sliding window and its refill margin: MDM1::Decompress's 0x1000 byte malloc and
        // WINDOW_MARGIN 0x80 in mdm1.cpp.
        private const int WindowBytes = 0x1000;
        private const int WindowMargin = 0x80;

        private const int MatchLengthBias = 3;
        private const int MatchLengthLong = 0x12;
        private const int MatchLengthEnd = 0x10;

        // The original tests the tag before handing a file to the decompressor:
        // GDI::ReadBitmapFile (HYPERX 0x0043fff0) reads a dword and compares it with MDM1_MAGIC.
        public static bool HasMagic(byte[] data)
        {
            return data != null && data.Length >= 4 && ReadUInt32(data, 0) == Magic;
        }

        // Decodes an MDM1 packed byte array. Every malformation is an AssetFormatException; the
        // original answers null and its callers report the file, so the port fails loudly instead.
        public static byte[] Decompress(byte[] data)
        {
            return Decompress(data, 0);
        }

        // Decodes from an offset, for a tagged stream inside a larger file (GDI::ReadBitmapFile
        // reads the magic in front of the decoder).
        public static byte[] Decompress(byte[] data, int offset)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0 || offset + HeaderBytes > data.Length)
            {
                throw AssetFormatException.At(null, offset, "MDM1 header runs past the end of the data");
            }

            if (ReadUInt32(data, offset) != Magic)
            {
                throw AssetFormatException.At(null, offset, "expected the MDM1 tag 0x314d444d");
            }

            int declared = ReadInt32(data, offset + 4);

            if (declared < 0)
            {
                // MDM1::Decompress frees the window and returns when malloc refuses the size; the
                // 20XX port states the same check.
                throw AssetFormatException.At(null, offset + 4, "MDM1 declares a negative output size " + declared);
            }

            byte[] output = new byte[declared];
            int written = LzssDecompress(data, offset + HeaderBytes, output);

            if (written != declared)
            {
                // MDM1::Decompress checks nDecompressed against the size it read and answers null.
                throw AssetFormatException.At(
                    null,
                    offset + 4,
                    "MDM1 stream wrote " + written + " byte(s) but the header declares " + declared);
            }

            return output;
        }

        // Tries to read one MDM1 packed stream; false means the data did not start with the tag.
        public static bool TryDecompress(byte[] data, out byte[] result)
        {
            result = null;

            if (!HasMagic(data))
            {
                return false;
            }

            result = Decompress(data);

            return true;
        }

        // The LZSS stream decoder. Returns the number of output bytes the stream terminates at,
        // which MDM1::Decompress compares with its declared size.
        private static int LzssDecompress(byte[] input, int inputAt, byte[] output)
        {
            // The window starts full of nothing, like the malloc'd buffer; the read position starts
            // at its end so the first refill fills it. ESI/EDX in LZSSDecompress and the window
            // frame slots CALLER_WINDOW/CALLER_WINDOW_SIZE in RefillInputWindow.
            byte[] window = new byte[WindowBytes];

            int windowAt = WindowBytes;
            int windowEnd = WindowBytes;
            int readAt = inputAt;
            int outAt = 0;

            if (!Refill(input, ref readAt, window, ref windowAt, ref windowEnd))
            {
                throw AssetFormatException.At(null, inputAt, "MDM1 stream has no first window");
            }

            while (true)
            {
                // LZSSDecompress: cmp esi, [window + size - 0x80]; jnc refill. One flag word and its
                // sixteen items never take the 0x80 byte margin.
                if (windowAt >= WindowBytes - WindowMargin)
                {
                    if (!Refill(input, ref readAt, window, ref windowAt, ref windowEnd))
                    {
                        throw AssetFormatException.At(null, readAt, "MDM1 stream ended before its terminator");
                    }
                }

                if (windowAt + 2 > windowEnd)
                {
                    throw AssetFormatException.At(null, readAt, "MDM1 flag word runs past the input");
                }

                int flags = window[windowAt] | (window[windowAt + 1] << 8);

                windowAt += 2;

                for (int bit = 0; bit < 16; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        // Literal: mov al, [esi]; mov [edi], al.
                        if (windowAt >= windowEnd)
                        {
                            throw AssetFormatException.At(null, readAt, "MDM1 literal runs past the input");
                        }

                        if (outAt >= output.Length)
                        {
                            throw AssetFormatException.At(null, readAt, "MDM1 stream writes past its declared size");
                        }

                        output[outAt] = window[windowAt];
                        outAt++;
                        windowAt++;

                        continue;
                    }

                    // Reference: a 16 bit word, distance in the high twelve bits, length nibble in
                    // the low four.
                    if (windowAt + 2 > windowEnd)
                    {
                        throw AssetFormatException.At(null, readAt, "MDM1 reference runs past the input");
                    }

                    int word = window[windowAt] | (window[windowAt + 1] << 8);

                    windowAt += 2;

                    int from = outAt - (word >> 4);
                    int length = (word & 0xf) + MatchLengthBias;

                    if (length == MatchLengthLong)
                    {
                        // longMatch: add cl, [esi]; inc esi; jc lengthCarry.
                        if (windowAt >= windowEnd)
                        {
                            throw AssetFormatException.At(null, readAt, "MDM1 long length runs past the input");
                        }

                        length += window[windowAt];
                        windowAt++;

                        if (length > 0xff)
                        {
                            // lengthCarry: cmp cl, MATCH_LENGTH_END; jz nextFlag. 0x10 wraps to a
                            // no-copy item; any other carry is the end of the stream marker.
                            if ((length & 0xff) == MatchLengthEnd)
                            {
                                continue;
                            }

                            return outAt;
                        }
                    }

                    if (from < 0 || outAt + length > output.Length)
                    {
                        // The original copies without a bounds test; 20XX returns -1 instead.
                        throw AssetFormatException.At(
                            null,
                            readAt,
                            "MDM1 reference from " + from + " length " + length + " does not fit the " + output.Length + " byte output");
                    }

                    // rep movsb: a reference may overlap the bytes it is writing.
                    for (int i = 0; i < length; i++)
                    {
                        output[outAt + i] = output[from + i];
                    }

                    outAt += length;
                }
            }
        }

        // RefillInputWindow: move the unread tail to the front of the window, ask the read callback
        // for the rest, and leave the read position at the front. The callback is a plain array
        // copy here; a short read at end of file is success with the tail, which is why the
        // original's
        //
        //     call RefillInputWindow
        //     jge loadFlags
        //
        // continues at EOF because FileObj::Read answered zero rather than a negative count.
        private static bool Refill(byte[] input, ref int readAt, byte[] window, ref int windowAt, ref int windowEnd)
        {
            int tail = windowEnd - windowAt;

            if (tail > 0 && windowAt != 0)
            {
                Buffer.BlockCopy(window, windowAt, window, 0, tail);
            }

            int want = WindowBytes - tail;
            int available = input.Length - readAt;
            int read = available <= 0 ? 0 : Math.Min(want, available);

            if (read > 0)
            {
                Buffer.BlockCopy(input, readAt, window, tail, read);
                readAt += read;
            }

            windowAt = 0;
            windowEnd = tail + read;

            return windowEnd > 0;
        }

        private static uint ReadUInt32(byte[] data, int at)
        {
            return (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24));
        }

        private static int ReadInt32(byte[] data, int at)
        {
            return data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
        }
    }
}
