using System;
using System.Collections.Generic;
using System.IO;

namespace UBlade3.Data
{
    // One line as FileObj::GetNextLine (HYPERX 0x004406b0) hands it back.
    public sealed class TextLine
    {
        public readonly string Text;

        // One-based physical line number in the file, for diagnostics.
        public readonly int LineNumber;

        // True when the 256-character FileObj buffer filled before the line ended; the original then
        // returns the same line again in two pieces.
        public readonly bool Truncated;

        public TextLine(string text, int lineNumber, bool truncated)
        {
            Text = text ?? string.Empty;
            LineNumber = lineNumber;
            Truncated = truncated;
        }

        public override string ToString()
        {
            return LineNumber + ": " + Text;
        }
    }

    // Port of FileObj::GetNextLine. Original text files are CRLF, but the original treats CR, LF and
    // CRLF alike and consumes every consecutive line break after a line, so a run of blank lines is
    // folded into the line before it. A zero-length line stops every caller's
    // "while (GetNextLine(...) > 0)" walk; TryReadLine reproduces that.
    public sealed class DataTextFile
    {
        private readonly byte[] data;
        private readonly string path;
        private int position;
        private int nextLineNumber = 1;
        private bool atEnd;

        public DataTextFile(byte[] data, string path)
        {
            this.data = data ?? new byte[0];
            this.path = path ?? string.Empty;
        }

        public string Path
        {
            get { return path; }
        }

        public int Position
        {
            get { return position; }
        }

        public int Length
        {
            get { return data.Length; }
        }

        public static DataTextFile Open(string absolutePath)
        {
            return new DataTextFile(File.ReadAllBytes(absolutePath), absolutePath);
        }

        public void Rewind()
        {
            position = 0;
            nextLineNumber = 1;
            atEnd = false;
        }

        // FileObj::GetNextLine: returns the line length (0 for an empty line or end of file).
        public int ReadRawLine(out TextLine line)
        {
            line = null;

            if (atEnd)
            {
                return 0;
            }

            int startLineNumber = nextLineNumber;
            int length = 0;
            List<byte> bytes = new List<byte>(64);
            bool truncated = false;

            while (length < DataParse.LineMax)
            {
                if (position >= data.Length)
                {
                    atEnd = true;

                    if (length == 0)
                    {
                        return 0;
                    }

                    line = new TextLine(DataParse.DecodeText(bytes.ToArray(), 0, bytes.Count), startLineNumber, false);

                    return length;
                }

                byte value = data[position++];

                if (value == '\n' || value == '\r')
                {
                    if (value == '\r' && position < data.Length && data[position] == '\n')
                    {
                        position++;
                    }

                    nextLineNumber++;

                    position = ConsumeLineBreaks(position, ref nextLineNumber);

                    line = new TextLine(DataParse.DecodeText(bytes.ToArray(), 0, bytes.Count), startLineNumber, false);

                    return length;
                }

                bytes.Add(value);
                length++;
            }

            // FileObj's buffer filled before a break: the next call continues the same line.
            truncated = true;

            line = new TextLine(DataParse.DecodeText(bytes.ToArray(), 0, bytes.Count), startLineNumber, truncated);

            return length;
        }

        // The callers' "while (GetNextLine(&pLine) > 0)" loop: a zero-length result ends the walk.
        public bool TryReadLine(out TextLine line)
        {
            int length = ReadRawLine(out line);

            return length > 0;
        }

        // Every non-empty line in file order; stops where the original walk stops.
        public List<TextLine> ReadAllLines()
        {
            List<TextLine> lines = new List<TextLine>();
            TextLine line;

            while (TryReadLine(out line))
            {
                lines.Add(line);
            }

            return lines;
        }

        // Position just past the current line's break, folding CR LF and CRLF into single breaks so
        // the reported line numbers match an editor's.
        private int ConsumeLineBreaks(int at, ref int lineNumber)
        {
            int i = at;

            while (i < data.Length)
            {
                byte value = data[i];

                if (value == '\r')
                {
                    i++;

                    if (i < data.Length && data[i] == '\n')
                    {
                        i++;
                    }

                    lineNumber++;
                }
                else if (value == '\n')
                {
                    i++;
                    lineNumber++;
                }
                else
                {
                    break;
                }
            }

            return i;
        }
    }
}
