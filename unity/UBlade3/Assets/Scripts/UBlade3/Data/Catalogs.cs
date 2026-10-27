using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // PLANS.CAT: the plan catalogue the network asset check walks and the plan files are named from.
    // GameSession::GetPlansEntry (HYPERX 0x00441f60,
    // third_party/hyperblade-decomp/src/net/gamesession.cpp lines 862-912):
    //   ParseUtil::ParseToken(pLine, szToken, 0, " \t\n\r.", strlen(pLine), &pLine)
    // so a line yields its first token up to a blank or a dot; '#' starts a comment; the stored name
    // is truncated to SCAT_NAME_MAX = 15 characters.
    public sealed class PlansCatalog
    {
        public const string FileName = "plans.cat";

        public const int NameMax = 15;

        private PlansCatalog()
        {
        }

        public List<string> Names { get; private set; }

        public List<string> RawTokens { get; private set; }

        public string SourcePath { get; private set; }

        public static PlansCatalog Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);
            PlansCatalog catalog = new PlansCatalog();

            catalog.SourcePath = file.Path;
            catalog.Names = new List<string>();
            catalog.RawTokens = new List<string>();

            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text;
                int position = 0;
                string token;

                if (!DataParse.ParseToken(text, ref position, " \t\n\r.", text.Length, out token))
                {
                    continue;
                }

                if (token[0] == '#')
                {
                    continue;
                }

                string name = token.Length > NameMax ? token.Substring(0, NameMax) : token;

                catalog.Names.Add(name);
                catalog.RawTokens.Add(token);
            }

            return catalog;
        }

        public bool ContainsPlan(string planName)
        {
            foreach (string name in Names)
            {
                if (DataParse.EqualsIgnoreCase(name, planName))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SNDFILES.LST. SoundManager::Load (HYPERX 0x00449280,
    // third_party/hyperblade-decomp/src/sound/soundmanager.cpp lines 61-183):
    //   - the first non-comment line is a header scanned with sscanf("%d %s ");
    //   - m_nSoundCount = (non-comment lines) - 1;
    //   - each later non-comment line scans sscanf("%s %d %d %d"; name, duration ms, group,
    //     load-mode flag) and a line with fewer conversions keeps the previous values, exactly like
    //     the fscanf readers. nGroup with nScanned < 3 keeps the previous group.
    //   - the group table records first index, count and the group's longest duration scaled by
    //     SAMPLES_PER_MS (44.1 per millisecond) for the fifteen SoundTypes groups; group ids outside
    //     0..14 (the list uses 666, 777 and 999 as "no group") are stored per entry but do not open a
    //     group.
    public sealed class SoundListEntry
    {
        public int Index;
        public string Name;
        public int DurationMs;
        public int Group;
        public int LoadMode;

        // Whether this line's own text supplied the value (false: inherited from a previous line, as
        // the fscanf loader would leave it).
        public bool HasDuration;
        public bool HasGroup;
        public bool HasFlag;
        public int LineNumber;

        public override string ToString()
        {
            return Name + " " + DurationMs + " " + Group;
        }
    }

    public sealed class SoundFilesList
    {
        public const string FileName = "sndfiles.lst";

        public const int GroupCount = 15; // SOUNDMGR_SLOTS

        public const double SamplesPerMs = 44.1; // SAMPLES_PER_MS

        private SoundFilesList()
        {
        }

        public string SourcePath { get; private set; }

        public int HeaderSampleRate { get; private set; }

        public string HeaderName { get; private set; }

        // m_nSoundCount from the first walk (non-comment lines minus the header).
        public int DeclaredSoundCount { get; private set; }

        public List<SoundListEntry> Entries { get; private set; }

        public short[] GroupFirst { get; private set; }

        public short[] GroupCounts { get; private set; }

        public int[] GroupSamples { get; private set; }

        public static SoundFilesList Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);

            return Parse(file, file.Path);
        }

        public static SoundFilesList Parse(DataTextFile file, string sourcePath)
        {
            SoundFilesList list = new SoundFilesList();

            list.SourcePath = sourcePath;
            list.Entries = new List<SoundListEntry>();
            list.GroupFirst = new short[GroupCount];
            list.GroupCounts = new short[GroupCount];
            list.GroupSamples = new int[GroupCount];

            List<TextLine> lines = new List<TextLine>();
            TextLine line;

            file.Rewind();

            while (file.TryReadLine(out line))
            {
                lines.Add(line);
            }

            int nonComment = 0;

            foreach (TextLine candidate in lines)
            {
                if (candidate.Text.Length > 0 && candidate.Text[0] != '#')
                {
                    nonComment++;
                }
            }

            list.DeclaredSoundCount = nonComment - 1;

            int headerIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Text.Length > 0 && lines[i].Text[0] != '#')
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex >= 0)
            {
                int position = 0;
                string sampleToken;

                if (DataParse.ParseToken(lines[headerIndex].Text, ref position, " \t\n\r", DataParse.TokenMax, out sampleToken))
                {
                    int sampleRate;

                    if (DataParse.TryScanInt(sampleToken, out sampleRate))
                    {
                        list.HeaderSampleRate = sampleRate;

                        string nameToken;

                        if (DataParse.ParseToken(lines[headerIndex].Text, ref position, " \t\n\r", DataParse.TokenMax, out nameToken))
                        {
                            list.HeaderName = nameToken;
                        }
                    }
                }
            }

            int nDuration = 0;
            int nGroup = 0;
            int nFlag = 0;
            int nLongest = 0;
            int nGroupFirst = 0;
            int nGroupPrev = -1;
            int nIndex = 0;

            for (int i = headerIndex + 1; i < lines.Count; i++)
            {
                string text = lines[i].Text;

                if (text.Length == 0 || text[0] == '#')
                {
                    continue;
                }

                int position = 0;
                string nameToken;

                if (!DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out nameToken))
                {
                    continue;
                }

                SoundListEntry entry = new SoundListEntry();

                entry.Index = nIndex;
                entry.Name = nameToken;
                entry.LineNumber = lines[i].LineNumber;

                string token;
                int conversions = 1;

                if (DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token) && DataParse.TryScanInt(token, out nDuration))
                {
                    conversions = 2;
                    entry.HasDuration = true;

                    if (DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token) && DataParse.TryScanInt(token, out nGroup))
                    {
                        conversions = 3;
                        entry.HasGroup = true;

                        if (DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token) && DataParse.TryScanInt(token, out nFlag))
                        {
                            conversions = 4;
                            entry.HasFlag = true;
                        }
                    }
                }

                entry.DurationMs = nDuration;
                entry.Group = nGroup;
                entry.LoadMode = conversions >= 4 ? nFlag : 0;

                list.Entries.Add(entry);

                if (nGroup >= 0 && nGroup < GroupCount && nGroupPrev != nGroup)
                {
                    list.GroupFirst[nGroup] = (short)nIndex;

                    if (nGroupPrev >= 0)
                    {
                        list.GroupCounts[nGroupPrev] = (short)(nIndex - nGroupFirst);
                        nGroupFirst = nIndex;
                        list.GroupSamples[nGroupPrev] = (int)(nLongest * SamplesPerMs);
                        nLongest = 0;
                    }

                    nGroupPrev = nGroup;
                }

                if (nLongest < nDuration)
                {
                    nLongest = nDuration;
                }

                nIndex++;
            }

            if (nGroupPrev >= 0 && nGroupPrev < GroupCount)
            {
                list.GroupCounts[nGroupPrev] = (short)(nIndex - nGroupFirst);
                list.GroupSamples[nGroupPrev] = (int)(nLongest * SamplesPerMs);
            }

            return list;
        }

        public SoundListEntry FindEntry(string name)
        {
            foreach (SoundListEntry entry in Entries)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }

    // INFOBTXT.LST. MessageQueue::LoadList (HYPERX 0x00452ba0,
    // third_party/hyperblade-decomp/src/ui/messagequeue.cpp lines 190-271). A line is
    //   <identifier> <colour id> <message text>
    // where the colour is atoi of the second field (0 green, 1 blue, 2 red), and the message is
    // everything after the second space. The identifier is discarded by the loader: messages are
    // found by their position, which is why the file comments say "Starts at 79".
    public sealed class InfoTextEntry
    {
        public int Index;
        public string Identifier;
        public int Color;
        public string Text;
        public int LineNumber;

        public override string ToString()
        {
            return Index + ": " + Color + " " + Text;
        }
    }

    public sealed class InfoTextList
    {
        public const string FileName = "infobtxt.lst";

        private InfoTextList()
        {
        }

        public string SourcePath { get; private set; }

        public List<InfoTextEntry> Entries { get; private set; }

        public static InfoTextList Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);
            InfoTextList list = new InfoTextList();

            list.SourcePath = file.Path;
            list.Entries = new List<InfoTextEntry>();

            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text;

                if (text.Length == 0 || text[0] == '#')
                {
                    continue;
                }

                InfoTextEntry entry = new InfoTextEntry();

                entry.Index = list.Entries.Count;
                entry.LineNumber = line.LineNumber;

                int firstSpace = text.IndexOf(' ');

                if (firstSpace < 0)
                {
                    entry.Identifier = text;
                    entry.Color = 0;
                    entry.Text = "!EEK empty description on line: " + entry.Index + "\n";
                }
                else
                {
                    entry.Identifier = text.Substring(0, firstSpace);

                    int cursor = firstSpace + 1;
                    entry.Color = ScanAtoi(text, cursor);

                    int secondSpace = text.IndexOf(' ', cursor);

                    if (secondSpace >= 0)
                    {
                        entry.Text = text.Substring(secondSpace + 1);
                    }
                    else
                    {
                        entry.Text = "!EEK no string on line: " + entry.Index + "\n";
                    }
                }

                list.Entries.Add(entry);
            }

            return list;
        }

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
    }

    // JUMBO.TXT. JumboTextList::Load (HYPERX 0x0046c010,
    // third_party/hyperblade-decomp/src/ui/jumbotextlist.cpp lines 60-117): every non-comment,
    // non-empty line is one advert filename, kept verbatim.
    public sealed class JumboTextList
    {
        public const string FileName = "jumbo.txt";

        private JumboTextList()
        {
        }

        public string SourcePath { get; private set; }

        public List<string> Entries { get; private set; }

        public List<int> LineNumbers { get; private set; }

        public static JumboTextList Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);
            JumboTextList list = new JumboTextList();

            list.SourcePath = file.Path;
            list.Entries = new List<string>();
            list.LineNumbers = new List<int>();

            TextLine line;

            while (file.TryReadLine(out line))
            {
                if (line.Text.Length == 0 || line.Text[0] == '#')
                {
                    continue;
                }

                list.Entries.Add(line.Text);
                list.LineNumbers.Add(line.LineNumber);
            }

            return list;
        }
    }

    // MODEL.LST and ANIMATE.LST. No loader for either file exists in the byte-matching decompilation
    // (searched src/ and docs/) and hyperblade-20XX does not read them either; they are the model and
    // animation authoring lists. UBlade3 preserves them as ordered name lists, without pretending to
    // know a runtime consumer.
    public sealed class NameListFile
    {
        public const string ModelListFileName = "model.lst";

        public const string AnimateListFileName = "animate.lst";

        private NameListFile()
        {
        }

        public string FileName { get; private set; }

        public string SourcePath { get; private set; }

        public List<string> Names { get; private set; }

        public List<int> LineNumbers { get; private set; }

        public static NameListFile Load(GameDataRoot root, string fileName)
        {
            DataTextFile file = root.OpenText(fileName);
            NameListFile list = new NameListFile();

            list.FileName = fileName;
            list.SourcePath = file.Path;
            list.Names = new List<string>();
            list.LineNumbers = new List<int>();

            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text.Trim();

                if (text.Length == 0 || text[0] == '#')
                {
                    continue;
                }

                list.Names.Add(text);
                list.LineNumbers.Add(line.LineNumber);
            }

            return list;
        }
    }
}
