using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // HITANIMS.LST, CHKANIMS.LST, JMPANIMS.LST and THRANIMS.LST. PlanVM::InitReactionTables
    // (third_party/hyperblade-decomp/src/planvm/planvm.cpp lines 389-456) reads exactly five lines
    // from each file and sscanf's seven tab-separated names per line:
    //   sscanf(line, "%s\t%s\t%s\t%s\t%s\t%s\t%s", ...)
    // Any line that does not yield seven names is fatal ("Error reading Animation List file.").
    // The reaction table itself then maps each name to an animation id through the animation
    // registry (g_pAnimRegistry->FindAnimByNameIdx), which is asset loading, not text parsing.
    public sealed class AnimationLists
    {
        public const int LineCount = 5;

        public const int NamesPerLine = 7;

        public const int NameMax = 32;

        public const int ReactionCount = LineCount * NamesPerLine;

        public static readonly string[] FileNames = { "hitanims.lst", "chkanims.lst", "jmpanims.lst", "thranims.lst" };

        private AnimationLists()
        {
        }

        // Five rows of seven names for each file, flattened row-major.
        public string[] Hit { get; private set; }

        public string[] Check { get; private set; }

        public string[] Jump { get; private set; }

        public string[] Throw { get; private set; }

        public string SourcePath { get; private set; }

        public static AnimationLists Load(GameDataRoot root)
        {
            AnimationLists lists = new AnimationLists();

            lists.Hit = ReadFile(root, FileNames[0]);
            lists.Check = ReadFile(root, FileNames[1]);
            lists.Jump = ReadFile(root, FileNames[2]);
            lists.Throw = ReadFile(root, FileNames[3]);

            return lists;
        }

        private static string[] ReadFile(GameDataRoot root, string fileName)
        {
            DataTextFile file = root.OpenText(fileName);
            string[] names = new string[ReactionCount];

            for (int row = 0; row < LineCount; row++)
            {
                TextLine line;

                if (!file.TryReadLine(out line))
                {
                    throw new DataFormatException("Error reading Animation List file.", file.Path, 0);
                }

                List<string> tokens = DataParse.SplitTokens(line.Text, " \t\n\r", DataParse.TokenMax);

                if (tokens.Count != NamesPerLine)
                {
                    throw new DataFormatException("Error reading Animation List file (line " + (row + 1) + " has " + tokens.Count + " names, expected " + NamesPerLine + ").", file.Path, line.LineNumber);
                }

                for (int i = 0; i < NamesPerLine; i++)
                {
                    names[row * NamesPerLine + i] = tokens[i];
                }
            }

            return names;
        }
    }
}
