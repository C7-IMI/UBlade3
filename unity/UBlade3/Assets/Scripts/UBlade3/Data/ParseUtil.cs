using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UBlade3.Data
{
    // Thrown when an original data file is missing, malformed, or disagrees with the loader being ported.
    public class DataFormatException : Exception
    {
        public string SourcePath { get; private set; }

        public int SourceLine { get; private set; }

        public DataFormatException(string message)
            : base(message)
        {
        }

        public DataFormatException(string message, string sourcePath, int sourceLine)
            : base(FormatMessage(message, sourcePath, sourceLine))
        {
            SourcePath = sourcePath ?? string.Empty;
            SourceLine = sourceLine;
        }

        private static string FormatMessage(string message, string sourcePath, int sourceLine)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return message;
            }

            if (sourceLine <= 0)
            {
                return message + " [" + sourcePath + "]";
            }

            return message + " [" + sourcePath + ":" + sourceLine + "]";
        }
    }

    // Port of src/common/parseutil.cpp from third_party/hyperblade-decomp. Every loader in this
    // data layer reads original DOS-era files through these exact token rules so that field order
    // and failure behaviour match the 1996 executable.
    public static class DataParse
    {
        // PARSE_TOKEN_MAX, src/common/parseutil.h.
        public const int TokenMax = 0x78;

        // FileObj::m_szLine, src/common/fileobj.h; GetNextLine returns at most this many characters.
        public const int LineMax = 0x100;

        // ParseUtil::FindKeyword (HYPERX 0x00435f90): _mbsicmp over the table, first match wins.
        public static int FindKeyword(string word, string[] table)
        {
            if (word == null || table == null)
            {
                return -1;
            }

            for (int i = 0; i < table.Length; i++)
            {
                if (EqualsIgnoreCase(table[i], word))
                {
                    return i;
                }
            }

            return -1;
        }

        // ParseUtil::ParseToken (HYPERX 0x00487370). All callers ported here pass a null
        // delimitersA, so only the leading space/tab skip and the delimitersB terminator are kept.
        // position starts as the input index and is left at the terminating delimiter (or -1 at the
        // end of the line, matching the original's null pointer).
        public static bool ParseToken(string input, ref int position, string delimitersB, int maxLength, out string token)
        {
            token = null;

            if (input == null || position < 0 || position >= input.Length)
            {
                position = -1;
                return false;
            }

            int i = position;

            while (i < input.Length && (input[i] == ' ' || input[i] == '\t'))
            {
                i++;
            }

            if (i >= input.Length)
            {
                position = -1;
                return false;
            }

            int start = i;

            while (i < input.Length && delimitersB.IndexOf(input[i]) < 0)
            {
                i++;
            }

            int length = i - start;

            if (length == 0)
            {
                position = i < input.Length ? i : -1;
                return false;
            }

            if (i >= input.Length)
            {
                // The original takes strlen and does not bound-check this branch; a token that runs to
                // the end of the line is accepted whatever its length.
                position = -1;
            }
            else
            {
                if (length > maxLength - 1)
                {
                    position = i;
                    return false;
                }

                position = i;
            }

            token = input.Substring(start, length);

            return true;
        }

        // ParseUtil::SkipToChar (HYPERX 0x00487420): skip spaces/tabs, then require the stop char.
        // Returns the index of the stop char, or -1 when it is not the next non-blank character.
        public static int SkipToChar(string input, int position, char stop)
        {
            if (input == null || position < 0 || position >= input.Length)
            {
                return -1;
            }

            int i = position;

            while (i < input.Length)
            {
                char c = input[i];

                if (c == stop)
                {
                    return i;
                }

                if (c != ' ' && c != '\t')
                {
                    return -1;
                }

                i++;
            }

            return -1;
        }

        // ParseUtil::SkipPastChar (HYPERX 0x00487460).
        public static int SkipPastChar(string input, int position, char stop)
        {
            int at = SkipToChar(input, position, stop);

            return at < 0 ? -1 : at + 1;
        }

        // sscanf(token, "%f") as the loaders call it: leading whitespace, an optional sign, digits
        // with an optional fraction and exponent; trailing characters are ignored.
        public static bool TryScanFloat(string token, out float value)
        {
            value = 0.0f;

            if (token == null)
            {
                return false;
            }

            int i = 0;

            while (i < token.Length && IsScanfSpace(token[i]))
            {
                i++;
            }

            int numberStart = i;

            if (i < token.Length && (token[i] == '+' || token[i] == '-'))
            {
                i++;
            }

            int digitsBefore = 0;

            while (i < token.Length && token[i] >= '0' && token[i] <= '9')
            {
                i++;
                digitsBefore++;
            }

            int digitsAfter = 0;

            if (i < token.Length && token[i] == '.')
            {
                int dot = i;
                i++;

                while (i < token.Length && token[i] >= '0' && token[i] <= '9')
                {
                    i++;
                    digitsAfter++;
                }

                if (digitsBefore == 0 && digitsAfter == 0)
                {
                    i = dot;
                }
            }

            if (digitsBefore == 0 && digitsAfter == 0)
            {
                return false;
            }

            int numberEnd = i;

            if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
            {
                int exponent = i + 1;

                if (exponent < token.Length && (token[exponent] == '+' || token[exponent] == '-'))
                {
                    exponent++;
                }

                int exponentDigits = 0;

                while (exponent < token.Length && token[exponent] >= '0' && token[exponent] <= '9')
                {
                    exponent++;
                    exponentDigits++;
                }

                if (exponentDigits > 0)
                {
                    numberEnd = exponent;
                }
            }

            string number = token.Substring(numberStart, numberEnd - numberStart);

            double parsed;

            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            value = (float)parsed;

            return true;
        }

        // sscanf(token, "%d").
        public static bool TryScanInt(string token, out int value)
        {
            value = 0;

            if (token == null)
            {
                return false;
            }

            int i = 0;

            while (i < token.Length && IsScanfSpace(token[i]))
            {
                i++;
            }

            long result = 0;
            bool negative = false;

            if (i < token.Length && (token[i] == '+' || token[i] == '-'))
            {
                negative = token[i] == '-';
                i++;
            }

            int digits = 0;

            while (i < token.Length && token[i] >= '0' && token[i] <= '9')
            {
                if (result < 214748364L)
                {
                    result = result * 10 + (token[i] - '0');
                }

                i++;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }

            if (negative)
            {
                result = -result;
            }

            value = (int)result;

            return true;
        }

        // sscanf(token, "%hd"), the collision object's component field.
        public static bool TryScanShort(string token, out short value)
        {
            int wide;

            if (!TryScanInt(token, out wide))
            {
                value = 0;
                return false;
            }

            value = (short)wide;

            return true;
        }

        // _mbsicmp, ASCII case folding as the 1996 multibyte CRT did for these files.
        public static bool EqualsIgnoreCase(string a, string b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (FoldAscii(a[i]) != FoldAscii(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static char FoldAscii(char c)
        {
            if (c >= 'A' && c <= 'Z')
            {
                return (char)(c + ('a' - 'A'));
            }

            return c;
        }

        public static bool IsScanfSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f';
        }

        // The DefineObject and DefineTeam readers turn '_' into a space in names (NAME_SPACER).
        public static string ReplaceNameSpacers(string text)
        {
            if (text == null || text.IndexOf('_') < 0)
            {
                return text;
            }

            char[] chars = text.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '_')
                {
                    chars[i] = ' ';
                }
            }

            return new string(chars);
        }

        // The text files in Data are plain ASCII; decode byte-for-byte as the Windows ANSI code page
        // would for these files so no byte is silently replaced.
        public static string DecodeText(byte[] bytes, int offset, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            char[] chars = new char[count];

            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)bytes[offset + i];
            }

            return new string(chars);
        }

        public static List<string> SplitTokens(string input, string delimitersB, int maxLength)
        {
            List<string> tokens = new List<string>();

            if (input == null)
            {
                return tokens;
            }

            int position = 0;
            string token;

            while (ParseToken(input, ref position, delimitersB, maxLength, out token))
            {
                tokens.Add(token);
            }

            return tokens;
        }
    }
}
