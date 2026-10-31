using System;
using System.Collections.Generic;
using System.Globalization;

namespace UBlade3.Assets
{
    // One segment line of AnimAssm.txt: a .wba file, its frame range and the three playback rates.
    // src/anim/animscripttable.cpp ParseSegment (HYPERX 0x00418670) reads, after three tabs,
    //   FileName<tab>Description<tab>Start<tab>End<tab>Slow<tab>Standard<tab>Trans.Fact.<tab>Loop
    // into AnimNode. The third float lands in AnimNode::m_flFastFps (the file header calls it
    // "Trans. Fact."), so it is exposed under both names; the animation player reads
    // m_flSlowFps and m_flStandardFps, and the loader clamps slow to standard.
    public sealed class AnimationAssemblySegment
    {
        internal AnimationAssemblySegment(
            string fileName,
            string description,
            short startFrame,
            short endFrame,
            float slowFps,
            float standardFps,
            float transitionFactor,
            short loopCount)
        {
            FileName = fileName;
            Description = description;
            StartFrame = startFrame;
            EndFrame = endFrame;
            SlowFps = slowFps;
            StandardFps = standardFps;
            TransitionFactor = transitionFactor;
            LoopCount = loopCount;
        }

        // The .wba stem the segment plays, without extension.
        public string FileName { get; }

        public string Description { get; }

        public short StartFrame { get; }

        public short EndFrame { get; }

        public float SlowFps { get; }

        public float StandardFps { get; }

        // AnimNode::m_flFastFps; the shipped values are 0.10, a transition factor.
        public float TransitionFactor { get; }

        public short LoopCount { get; }
    }

    // One trigger line: an animation event. src/anim/animscripttable.cpp ParseTrigger
    // (HYPERX 0x004189c0) reads, after four tabs,
    //   SegmentIndex<tab>FrameNumber<tab>ActionName[<tab>SyncAssembly<tab>SyncSegment<tab>SyncFrame]
    // and AnimDB::AddClipEntry (HYPERX 0x004176d0) stores it as an AnimClipEntry; a sync frame
    // message also carries an AnimNamedRef to the other assembly. The action names are plan VM
    // symbols (id_MSG_*) or event names such as Contact.
    public sealed class AnimationAssemblyTrigger
    {
        internal AnimationAssemblyTrigger(short segment, short frame, string actionName, string syncAssembly, short syncSegment, short syncFrame, bool hasSync)
        {
            Segment = segment;
            Frame = frame;
            ActionName = actionName;
            SyncAssembly = syncAssembly;
            SyncSegment = syncSegment;
            SyncFrame = syncFrame;
            HasSync = hasSync;
        }

        // The index into the owning assembly's segment list.
        public short Segment { get; }

        public short Frame { get; }

        public string ActionName { get; }

        public bool HasSync { get; }

        public string SyncAssembly { get; }

        public short SyncSegment { get; }

        public short SyncFrame { get; }
    }

    // One assembly: the animation script entry AnimDB (src/anim/animdb.cpp) that the player starts
    // by name. It owns an ordered segment list and the triggers that fire while it plays.
    public sealed class AnimationAssembly
    {
        internal AnimationAssembly(string name, string description, AnimationAssemblySegment[] segments, AnimationAssemblyTrigger[] triggers)
        {
            Name = name;
            Description = description;
            Segments = segments;
            Triggers = triggers;
        }

        public string Name { get; }

        public string Description { get; }

        // The segment lines in file order; a trigger's Segment is an index into this list.
        public AnimationAssemblySegment[] Segments { get; }

        public AnimationAssemblyTrigger[] Triggers { get; }
    }

    // AnimData\AnimAssm.txt, the animation assembly script the game loads at start-up:
    // src/engine/game.cpp line 162 calls g_pAnimRegistry->LoadFromPath("animdata\\AnimAssm.txt");
    // AnimScriptTable::ParseScript (HYPERX 0x00418210) walks it line by line with
    // AnimScriptTable::ParseScriptLine (HYPERX 0x004183e0). The file's own header documents the
    // three line shapes, and every shipped line is one of them.
    public sealed class AnimationAssemblyFile
    {
        internal AnimationAssemblyFile(string sourceName, AnimationAssembly[] assemblies, int lineCount)
        {
            SourceName = sourceName;
            Assemblies = assemblies;
            LineCount = lineCount;
        }

        public string SourceName { get; }

        public AnimationAssembly[] Assemblies { get; }

        public int LineCount { get; }
    }

    public static class AnimationAssemblyReader
    {
        public static AnimationAssemblyFile LoadFile(string path)
        {
            return Load(AssetText.ReadAllLatin1(path), path);
        }

        // Parses the assembly script. The original's token delimiter is a tab alone
        // (src/globals.cpp g_pszAnimTokenDelimiters = "\t"), and ParseSegment/ParseTrigger count
        // the leading tabs to decide which shape a line is; the reader uses the same rule.
        public static AnimationAssemblyFile Load(string text, string sourceName)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string[] lines = AssetText.SplitLines(text);
            List<AnimationAssembly> assemblies = new List<AnimationAssembly>();
            List<AnimationAssemblySegment> pendingSegments = null;
            List<AnimationAssemblyTrigger> pendingTriggers = null;
            string pendingName = null;
            string pendingDescription = null;
            int contentLines = 0;

            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                string line = lines[lineNumber];

                if (line.Length == 0 || line[0] == '#' || line.Trim().Length == 0)
                {
                    // Config::IsCommentLine skips the header and comments; blank lines are skipped.
                    continue;
                }

                contentLines++;

                int tabs = 0;

                while (tabs < line.Length && line[tabs] == '\t')
                {
                    tabs++;
                }

                string[] columns = line.Split('\t');

                if (tabs == 1)
                {
                    Close(assemblies, ref pendingName, ref pendingDescription, ref pendingSegments, ref pendingTriggers);

                    pendingName = Column(columns, 1);

                    if (pendingName.Length == 0)
                    {
                        throw AssetFormatException.At(sourceName, lineNumber, "assembly line has no name");
                    }

                    pendingDescription = Column(columns, 2);
                    pendingSegments = new List<AnimationAssemblySegment>();
                    pendingTriggers = new List<AnimationAssemblyTrigger>();
                }
                else if (tabs == 3)
                {
                    RequireAssembly(sourceName, lineNumber, pendingSegments);

                    short start = ParseShort(sourceName, lineNumber, "start frame", Column(columns, 5));
                    short end = ParseShort(sourceName, lineNumber, "end frame", Column(columns, 6));
                    float slow = ParseFloat(sourceName, lineNumber, "slow fps", Column(columns, 7));
                    float standard = ParseFloat(sourceName, lineNumber, "standard fps", Column(columns, 8));
                    float transition = ParseFloat(sourceName, lineNumber, "transition factor", Column(columns, 9));
                    short loop = ParseShort(sourceName, lineNumber, "loop count", Column(columns, 10));

                    pendingSegments.Add(new AnimationAssemblySegment(
                        Column(columns, 3),
                        Column(columns, 4),
                        start,
                        end,
                        slow,
                        standard,
                        transition,
                        loop));
                }
                else if (tabs == 4)
                {
                    RequireAssembly(sourceName, lineNumber, pendingTriggers);

                    short segment = ParseShort(sourceName, lineNumber, "trigger segment", Column(columns, 4));
                    short frame = ParseShort(sourceName, lineNumber, "trigger frame", Column(columns, 5));
                    string action = Column(columns, 6);
                    string sync = Column(columns, 7);

                    if (action.Length == 0)
                    {
                        throw AssetFormatException.At(sourceName, lineNumber, "trigger line has no action name");
                    }

                    bool hasSync = sync.Length != 0;
                    short syncSegment = hasSync ? ParseShort(sourceName, lineNumber, "sync segment", Column(columns, 8)) : (short)-1;
                    short syncFrame = hasSync ? ParseShort(sourceName, lineNumber, "sync frame", Column(columns, 9)) : (short)-1;

                    pendingTriggers.Add(new AnimationAssemblyTrigger(segment, frame, action, sync, syncSegment, syncFrame, hasSync));
                }
                else
                {
                    throw AssetFormatException.At(
                        sourceName,
                        lineNumber,
                        "line begins with " + tabs + " tab(s); the assembly script's three shapes begin with one, three or four");
                }
            }

            Close(assemblies, ref pendingName, ref pendingDescription, ref pendingSegments, ref pendingTriggers);

            return new AnimationAssemblyFile(sourceName, assemblies.ToArray(), contentLines);
        }

        private static void RequireAssembly(string sourceName, int lineNumber, object pending)
        {
            if (pending == null)
            {
                throw AssetFormatException.At(sourceName, lineNumber, "segment or trigger line before the first assembly");
            }
        }

        private static void Close(
            List<AnimationAssembly> assemblies,
            ref string name,
            ref string description,
            ref List<AnimationAssemblySegment> segments,
            ref List<AnimationAssemblyTrigger> triggers)
        {
            if (name == null)
            {
                return;
            }

            assemblies.Add(new AnimationAssembly(name, description, segments.ToArray(), triggers.ToArray()));

            name = null;
            description = null;
            segments = null;
            triggers = null;
        }

        private static string Column(string[] columns, int index)
        {
            if (index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index].Trim();
        }

        private static short ParseShort(string sourceName, int lineNumber, string field, string text)
        {
            short value;

            if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw AssetFormatException.At(sourceName, lineNumber, "bad " + field + " value \"" + text + "\"");
            }

            return value;
        }

        private static float ParseFloat(string sourceName, int lineNumber, string field, string text)
        {
            float value;

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw AssetFormatException.At(sourceName, lineNumber, "bad " + field + " value \"" + text + "\"");
            }

            return value;
        }
    }
}
