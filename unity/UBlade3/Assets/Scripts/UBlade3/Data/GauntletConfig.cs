using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // GAUNTLET.CFG: one line per home team, "#" comments, then up to twenty team indices.
    // SaveObject::ParseGauntletConfig (HYPERX 0x00448ee0,
    // third_party/hyperblade-decomp/src/ui/saveobject.cpp lines 128-181) fills m_aBracket with -1
    // first (SaveObject constructor, lines 20-28) and stores atoi of each space-separated value.
    public sealed class GauntletConfig
    {
        public const string FileName = "gauntlet.cfg";

        // GAUNTLET_ROUNDS and GAUNTLET_ROUND_SLOTS, src/ui/saveobject.h lines 5-6.
        public const int RoundCount = 0x14;
        public const int SlotCount = 0x14;

        private GauntletConfig()
        {
        }

        // m_aBracket: [round][slot], -1 where the config leaves the slot empty.
        public int[,] Bracket { get; private set; }

        // The team name before the first space on each non-comment line, in file order.
        public List<string> RoundTeams { get; private set; }

        // The parsed non-empty rounds, each an int array of slot values (file order, not padded).
        public List<int[]> Rounds { get; private set; }

        public string SourcePath { get; private set; }

        public static GauntletConfig Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);

            return Parse(file, file.Path);
        }

        public static GauntletConfig Parse(DataTextFile file, string sourcePath)
        {
            GauntletConfig config = new GauntletConfig();

            config.SourcePath = sourcePath;
            config.Bracket = new int[RoundCount, SlotCount];
            config.RoundTeams = new List<string>();
            config.Rounds = new List<int[]>();

            for (int round = 0; round < RoundCount; round++)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    config.Bracket[round, slot] = -1;
                }
            }

            int roundIndex = 0;
            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text;

                if (text.Length == 0 || text[0] == '#')
                {
                    continue;
                }

                int firstSpace = text.IndexOf(' ');

                if (firstSpace < 0)
                {
                    throw new DataFormatException("Gauntlet Error--Opponent Team errors on line: " + roundIndex + "!", sourcePath, line.LineNumber);
                }

                string teamName = text.Substring(0, firstSpace).TrimEnd(':', ' ', '\t');
                List<int> values = new List<int>();

                int space = firstSpace;

                for (int slot = 0; slot < SlotCount && space >= 0; slot++)
                {
                    int cursor = space + 1;
                    int value = ScanAtoi(text, cursor);
                    int nextSpace = text.IndexOf(' ', cursor);

                    values.Add(value);

                    if (roundIndex < RoundCount && slot < SlotCount)
                    {
                        config.Bracket[roundIndex, slot] = value;
                    }

                    space = nextSpace;
                }

                config.RoundTeams.Add(teamName);
                config.Rounds.Add(values.ToArray());
                roundIndex++;
            }

            return config;
        }

        // atoi: leading whitespace, optional sign, digits; anything else reads as zero.
        private static int ScanAtoi(string text, int position)
        {
            int i = position;

            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }

            bool negative = false;

            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                negative = text[i] == '-';
                i++;
            }

            long value = 0;
            int digits = 0;

            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                value = value * 10 + (text[i] - '0');
                i++;
                digits++;
            }

            if (digits == 0)
            {
                return 0;
            }

            return (int)(negative ? -value : value);
        }

        public int GetAwayTeamForRound(int homeTeam, int slot)
        {
            if (homeTeam < 0 || homeTeam >= RoundCount || slot < 0 || slot >= SlotCount)
            {
                return -1;
            }

            return Bracket[homeTeam, slot];
        }

        public override string ToString()
        {
            return "GauntletConfig(" + Rounds.Count + " rounds)";
        }
    }
}
