namespace UBlade3.Assets
{
    // Little-endian random access over a loaded asset file, with every read bounds checked so a
    // short or corrupt record fails with the byte offset instead of reading past the array. The
    // original reads through FileObj; this is the same data without the stream.
    internal sealed class AssetBytes
    {
        internal AssetBytes(byte[] data, string sourceName)
        {
            Data = data;
            SourceName = sourceName;
        }

        internal byte[] Data { get; }

        internal string SourceName { get; }

        internal int Length
        {
            get { return Data.Length; }
        }

        internal void Require(long at, long count, string what)
        {
            if (at < 0 || count < 0 || at + count > Data.Length)
            {
                throw AssetFormatException.At(
                    SourceName,
                    at,
                    what + " needs " + count + " byte(s) at 0x" + at.ToString("x") + " but the file is " + Data.Length + " bytes");
            }
        }

        internal byte U8(long at)
        {
            Require(at, 1, "byte");

            return Data[at];
        }

        internal sbyte I8(long at)
        {
            return unchecked((sbyte)U8(at));
        }

        internal short I16(long at)
        {
            Require(at, 2, "int16");

            return unchecked((short)(Data[at] | (Data[at + 1] << 8)));
        }

        internal ushort U16(long at)
        {
            return unchecked((ushort)I16(at));
        }

        internal int I32(long at)
        {
            Require(at, 4, "int32");

            return Data[at]
                   | (Data[at + 1] << 8)
                   | (Data[at + 2] << 16)
                   | (Data[at + 3] << 24);
        }

        internal uint U32(long at)
        {
            return unchecked((uint)I32(at));
        }

        // Reads the four bytes as an IEEE-754 single, the same bit pattern the original loads.
        internal float F32(long at)
        {
            return System.BitConverter.Int32BitsToSingle(I32(at));
        }

        internal byte[] Slice(long at, int count)
        {
            Require(at, count, "block");

            byte[] slice = new byte[count];

            System.Array.Copy(Data, at, slice, 0, count);

            return slice;
        }

        // Decodes one byte per character (Latin-1). The shipped text and name fields are bytes the
        // original compares with the multibyte string functions; no byte is replaced.
        internal string Ascii(long at, int count)
        {
            Require(at, count, "text");

            char[] chars = new char[count];

            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)Data[at + i];
            }

            return new string(chars);
        }

        // A fixed width C string: everything before the first NUL, in Latin-1.
        internal string FixedAscii(long at, int count)
        {
            string text = Ascii(at, count);
            int terminator = text.IndexOf('\0');

            return terminator < 0 ? text : text.Substring(0, terminator);
        }
    }
}
