using System;

namespace UBlade3.Audio
{
    // Raised when a shipped HyperBlade wave file does not match the layout the decompilation and
    // the 20XX reference reader document. The source name and byte offset are carried so callers
    // fail loudly and point at the exact chunk; UBlade3 never substitutes silent placeholder data
    // for a wave it could not read.
    public sealed class AudioFormatException : Exception
    {
        public AudioFormatException(string message)
            : base(message)
        {
        }

        public AudioFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        private AudioFormatException(string message, string sourceName, long offset)
            : base(message)
        {
            SourceName = sourceName;
            Offset = offset;
        }

        // The wave file the parser was reading, or null when the caller did not name one.
        public string SourceName { get; private set; }

        // The byte offset inside that file, or -1 when the failure was not tied to a read.
        public long Offset { get; private set; } = -1;

        internal static AudioFormatException At(string sourceName, long offset, string message)
        {
            string where = sourceName == null ? "wave" : sourceName;

            if (offset >= 0)
            {
                where += " at 0x" + offset.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            }

            return new AudioFormatException(where + ": " + message);
        }
    }
}
