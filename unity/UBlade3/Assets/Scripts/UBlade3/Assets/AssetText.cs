using System;
using System.IO;

namespace UBlade3.Assets
{
    // Text decoding for the shipped data files. The disc's text is pure ASCII and its binary name
    // fields are byte strings compared with the original's multibyte functions, so every byte is
    // decoded to the same code point (Latin-1) and nothing is replaced. This avoids
    // Encoding.Latin1, which .NET Standard 2.1 (the Unity profile) does not provide.
    internal static class AssetText
    {
        internal static string DecodeLatin1(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            char[] chars = new char[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                chars[i] = (char)data[i];
            }

            return new string(chars);
        }

        internal static string ReadAllLatin1(string path)
        {
            return DecodeLatin1(File.ReadAllBytes(path));
        }

        // CR, LF and CRLF all end a line; a trailing line ending produces no extra empty line
        // beyond the array slot the original's GetNextLine loop would also see.
        internal static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }
    }
}
