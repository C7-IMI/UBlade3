using System;

namespace UBlade3.Assets
{
    // Raised when a shipped HyperBlade asset file does not match the layout recorded in the
    // byte-matching decompilation (third_party/hyperblade-decomp). The source name and the byte
    // offset are carried so a caller can fail loudly and point at the exact record; UBlade3 never
    // falls back to placeholder geometry.
    public sealed class AssetFormatException : Exception
    {
        public AssetFormatException(string message)
            : base(message)
        {
        }

        public AssetFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        private AssetFormatException(string message, string sourceName, long offset)
            : base(message)
        {
            SourceName = sourceName;
            Offset = offset;
        }

        // The asset file the parser was reading, or null when the caller did not name one.
        public string SourceName { get; private set; }

        // The byte offset inside that file, or -1 when the failure was not tied to a read.
        public long Offset { get; private set; } = -1;

        internal static AssetFormatException At(string sourceName, long offset, string message)
        {
            string where = sourceName == null ? "asset" : sourceName;

            if (offset >= 0)
            {
                where += " at 0x" + offset.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            }

            return new AssetFormatException(where + ": " + message);
        }
    }
}
