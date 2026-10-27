using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // One entry of TEAMS.CAT plus the .HTM file it names.
    public sealed class TeamsCatalogEntry
    {
        public readonly string FileName;
        public readonly int Order;

        public TeamsCatalogEntry(string fileName, int order)
        {
            FileName = fileName;
            Order = order;
        }

        public override string ToString()
        {
            return Order + ": " + FileName;
        }
    }

    // What GameEngine::LoadTeamFromFile (HYPERX 0x004858b0, third_party/hyperblade-decomp/
    // src/engine/gameengine.cpp lines 3210-3403) derives for one side of a match: the ten object
    // definitions in file order, the model id each slot is created with, which slots go on the ice,
    // and which four slots own a TeamPlayerRow ratings row.
    public sealed class TeamLoadLayout
    {
        public const int SlotCount = 10;
        public const int RowCount = 4;

        // The five player definitions, then four body parts, then the team's Rok. Only slots 0-2 are
        // put on the ice at load; slots 3 and 4 are the reserves.
        public const int PlayersOnDrome = 3;
        public const int SlotRok = 9;
        public const int BodyPartFirst = 5;
        public const int BodyPartLast = 8;
        public const int GoalieSlot = 2;

        // kRowRosterSlots, src/engine/teamblock.h line 20.
        public static readonly int[] RowRosterSlots = { 0, 1, 3, 4 };

        // g_anBodyPartModelIds, gameengine.cpp line 5599: home's four then the away four.
        public static readonly int[] BodyPartModelIds = { 94, 95, 96, 97, 98, 99, 100, 101 };

        // MODEL_ID_NONE, src/engine/modelids.h.
        public const int ModelIdNone = 0x66;

        // kModelIDUnassigned, gameengine.cpp line 3196.
        public const int ModelIdUnassigned = -1;

        public readonly int[] ModelIds = new int[SlotCount];

        public readonly bool[] OnIce = new bool[SlotCount];

        public TeamLoadLayout()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                ModelIds[i] = ModelIdNone;
            }
        }

        // The row index a slot feeds into aRows, or -1 when the slot has no ratings row.
        public static int GetRowIndex(int slot)
        {
            switch (slot)
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 3:
                    return 2;
                case 4:
                    return 3;
                default:
                    return -1;
            }
        }
    }

    // A team definition: DefineTeam header plus the ten object definitions in the .HTM file.
    //
    // ParseTeamHTMFile            HYPERX 0x00482e70  (gameengine.cpp lines 1945-2161)
    // LoadTeamFromFile            HYPERX 0x004858b0  (gameengine.cpp lines 3210-3403)
    // TeamsBlock::TeamsBlock      HYPERX 0x004847d0  (src/engine/teamsblock.cpp)
    public sealed class TeamFile
    {
        public const int HeaderKeywordCount = 9;

        public static readonly string[] HeaderKeywords =
        {
            "name", "idlogo", "tslogo", "tstext", "tstitle", "tsanim", "environment", "DefineTeam", "dromext"
        };

        private TeamFile()
        {
        }

        // The catalogue's spelling of the file name, for example "Chico.htm".
        public string FileName { get; private set; }

        // szName, with the header's '_' separators turned into spaces.
        public string Name { get; private set; }

        public string IdLogo { get; private set; }

        public string TsLogo { get; private set; }

        public string TsText { get; private set; }

        public string TsTitle { get; private set; }

        public string TsAnim { get; private set; }

        // The .ENV environment file, e.g. "chidro.env".
        public string Environment { get; private set; }

        // The drome exterior model, e.g. "chiext".
        public string DromeExt { get; private set; }

        // The ten definitions in file order.
        public List<ObjectDefinition> Objects { get; private set; }

        // The same definitions indexed by LoadTeamFromFile's slot order.
        public ObjectDefinition[] Slots { get; private set; }

        // Non-fatal oddities found while loading (for example an unexpected type in slot 9).
        public List<string> Warnings { get; private set; }

        public int SourceLine { get; private set; }

        public string SourcePath { get; private set; }

        public IList<ObjectDefinition> Dromers
        {
            get
            {
                List<ObjectDefinition> players = new List<ObjectDefinition>();

                for (int i = 0; i < 5; i++)
                {
                    players.Add(Slots[i]);
                }

                return players;
            }
        }

        public IList<ObjectDefinition> BodyParts
        {
            get
            {
                List<ObjectDefinition> parts = new List<ObjectDefinition>();

                for (int i = TeamLoadLayout.BodyPartFirst; i <= TeamLoadLayout.BodyPartLast; i++)
                {
                    parts.Add(Slots[i]);
                }

                return parts;
            }
        }

        public ObjectDefinition Goalie
        {
            get { return Slots[TeamLoadLayout.GoalieSlot]; }
        }

        public ObjectDefinition Rok
        {
            get { return Slots[TeamLoadLayout.SlotRok]; }
        }

        public static List<TeamsCatalogEntry> LoadCatalog(GameDataRoot root)
        {
            // TeamsBlock::TeamsBlock: one file name per line, '#' comments, at most 30 teams and a
            // 32-character name limit (teamsblock.cpp lines 36-126).
            const int teamMax = 30;
            const int filenameMax = 32;

            List<TeamsCatalogEntry> entries = new List<TeamsCatalogEntry>();
            DataTextFile file = root.OpenText("teams.cat");
            TextLine line;

            while (file.TryReadLine(out line))
            {
                int position = 0;
                string token;

                if (!DataParse.ParseToken(line.Text, ref position, " \t\n\r", line.Text.Length, out token) || token[0] == '#')
                {
                    continue;
                }

                if (entries.Count == teamMax)
                {
                    throw new DataFormatException("More than max allowed teams ( " + teamMax + " ) in catalog!", file.Path, line.LineNumber);
                }

                if (token.Length >= filenameMax)
                {
                    throw new DataFormatException("Team filename to long " + token + "!", file.Path, line.LineNumber);
                }

                entries.Add(new TeamsCatalogEntry(token, entries.Count));
            }

            if (entries.Count < 1)
            {
                throw new DataFormatException("Error: empty team catalog!", file.Path, 0);
            }

            return entries;
        }

        public static TeamFile Load(GameDataRoot root, TeamsCatalogEntry entry, DataParseOptions options)
        {
            DataTextFile file = root.OpenText(entry.FileName);

            return Parse(root, file, entry.FileName, options);
        }

        public static TeamFile Parse(GameDataRoot root, DataTextFile file, string fileName, DataParseOptions options)
        {
            TeamFile team = new TeamFile();

            team.FileName = fileName;
            team.Objects = new List<ObjectDefinition>();
            team.Slots = new ObjectDefinition[TeamLoadLayout.SlotCount];
            team.Warnings = new List<string>();
            team.SourcePath = file.Path;

            int headerPosition;
            TextLine headerLine = FindTeamDefinition(file, out headerPosition);

            if (headerLine == null)
            {
                throw new DataFormatException("Team definition error in file " + fileName + ": no DefineTeam line.", file.Path, 0);
            }

            team.SourceLine = headerLine.LineNumber;
            team.ParseHeader(headerLine, headerPosition);

            ObjectDefinitionReader reader = new ObjectDefinitionReader(file, file.Path, options);

            for (int slot = 0; slot < TeamLoadLayout.SlotCount; slot++)
            {
                ObjectDefinition definition = reader.ReadNext();

                if (definition == null)
                {
                    throw new DataFormatException("Team definition file incomplete " + fileName + " (slot " + slot + " missing).", file.Path, 0);
                }

                team.Objects.Add(definition);
                team.Slots[slot] = definition;
            }

            if (reader.ReadNext() != null)
            {
                team.Warnings.Add("File has object definitions past the tenth; the 1996 loader ignores them.");
            }

            team.ValidateStructure();

            return team;
        }

        // The header keywords are parsed from the rest of the DefineTeam line. The original stops the
        // header walk at the first keyword it does not know (ParseTeamHTMFile lines 1988-2144).
        private void ParseHeader(TextLine headerLine, int position)
        {
            string text = headerLine.Text;
            string token;

            // The DefineTeam token itself has already been found; position starts after it.
            while (position >= 0 && DataParse.ParseToken(text, ref position, " =\t", DataParse.TokenMax, out token))
            {
                int equals = DataParse.SkipToChar(text, position, '=');

                if (equals < 0)
                {
                    break;
                }

                int keyword = DataParse.FindKeyword(token, HeaderKeywords);

                if (keyword < 0)
                {
                    break;
                }

                position = equals + 1;

                if (keyword == 7)
                {
                    // A second "DefineTeam": no case in the original switch, and the retry fails.
                    continue;
                }

                string value;

                if (!DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out value))
                {
                    throw new DataFormatException("Team asgn error: " + HeaderKeywords[keyword] + " has no value.", SourcePath, headerLine.LineNumber);
                }

                switch (keyword)
                {
                    case 0:
                        Name = DataParse.ReplaceNameSpacers(value);
                        break;
                    case 1:
                        IdLogo = value;
                        break;
                    case 2:
                        TsLogo = value;
                        break;
                    case 3:
                        TsText = value;
                        break;
                    case 4:
                        TsTitle = value;
                        break;
                    case 5:
                        TsAnim = value;
                        break;
                    case 6:
                        Environment = value;
                        break;
                    case 8:
                        DromeExt = value;
                        break;
                }
            }

            if (string.IsNullOrEmpty(Name))
            {
                throw new DataFormatException("Incomplete Team def: " + text.Trim(), SourcePath, headerLine.LineNumber);
            }
        }

        private static TextLine FindTeamDefinition(DataTextFile file, out int positionAfterToken)
        {
            TextLine line;

            positionAfterToken = 0;

            while (file.TryReadLine(out line))
            {
                int position = 0;
                string token;

                if (DataParse.ParseToken(line.Text, ref position, " \t", DataParse.TokenMax, out token)
                    && DataParse.EqualsIgnoreCase(token, "DefineTeam"))
                {
                    positionAfterToken = position;

                    return line;
                }
            }

            return null;
        }

        // The two structural checks LoadTeamFromFile performs: five Dromers then four BodyParts.
        private void ValidateStructure()
        {
            for (int slot = 0; slot < 5; slot++)
            {
                if (Slots[slot].Kind != ObjectKind.Dromer)
                {
                    throw new DataFormatException("Team definition file invalid " + FileName + ": slot " + slot + " is " + Slots[slot].TypeName + ", expected Dromer.", SourcePath, Slots[slot].SourceLine);
                }
            }

            for (int slot = TeamLoadLayout.BodyPartFirst; slot <= TeamLoadLayout.BodyPartLast; slot++)
            {
                if (Slots[slot].Kind != ObjectKind.BodyPart)
                {
                    throw new DataFormatException("Team definition file invalid " + FileName + ": slot " + slot + " is " + Slots[slot].TypeName + ", expected BodyPart.", SourcePath, Slots[slot].SourceLine);
                }
            }

            if (Slots[TeamLoadLayout.SlotRok].Kind != ObjectKind.Rok)
            {
                Warnings.Add("Slot 9 is " + Slots[TeamLoadLayout.SlotRok].TypeName + " and not Rok; the 1996 loader does not check this slot.");
            }
        }

        // GameEngine::LoadTeamFromFile's slot bookkeeping: side 0 is home, 1 is away. The away team's
        // definitions get m_bForceRemap. Model ids: the first three Dromers run side, side+2, side+4;
        // reserves keep MODEL_ID_NONE; body parts take ids 94..97 (home) or 98..101 (away); the Rok
        // slot is suppressed to kModelIDUnassigned.
        public TeamLoadLayout BuildLoadLayout(int side)
        {
            TeamLoadLayout layout = new TeamLoadLayout();

            int nRunningId = side;
            int nPlayers = 0;

            for (int slot = 0; slot < TeamLoadLayout.SlotCount; slot++)
            {
                ObjectDefinition definition = Slots[slot];

                if (side == 1)
                {
                    definition.ForceRemap = true;
                }

                if (nPlayers < TeamLoadLayout.PlayersOnDrome)
                {
                    definition.ModelId = nRunningId;
                    layout.ModelIds[slot] = nRunningId;
                    layout.OnIce[slot] = true;
                }
                else if (slot == TeamLoadLayout.SlotRok)
                {
                    definition.ModelId = TeamLoadLayout.ModelIdUnassigned;
                    layout.ModelIds[slot] = TeamLoadLayout.ModelIdUnassigned;
                }
                else if (slot >= TeamLoadLayout.BodyPartFirst && slot <= TeamLoadLayout.BodyPartLast)
                {
                    int bodyPartRow = slot - TeamLoadLayout.BodyPartFirst;

                    if (side == 1)
                    {
                        bodyPartRow += 4;
                    }

                    definition.ModelId = TeamLoadLayout.BodyPartModelIds[bodyPartRow];
                    layout.ModelIds[slot] = definition.ModelId;
                    layout.OnIce[slot] = true;
                }
                else
                {
                    definition.ModelId = TeamLoadLayout.ModelIdNone;
                    layout.ModelIds[slot] = TeamLoadLayout.ModelIdNone;
                }

                if (definition.Kind == ObjectKind.Dromer)
                {
                    nPlayers++;
                    nRunningId += 2;
                }
            }

            return layout;
        }

        // The four outfield ratings rows LoadTeamFromFile fills at load time (only slots 0, 1, 3 and 4
        // take ratings). Each row is 63 floats (kRatingCount). The difficulty level is the engine's
        // m_nDifficulty 0..2; a definition with no table at that level falls back to easy.
        public float[][] BuildOutfieldRows(int difficultyLevel)
        {
            float[][] rows = new float[TeamLoadLayout.RowCount][];

            for (int row = 0; row < TeamLoadLayout.RowCount; row++)
            {
                int slot = TeamLoadLayout.RowRosterSlots[row];
                ObjectDefinition definition = Slots[slot];
                float[] source = definition.GetRatingTableOrEasy(difficultyLevel);
                float[] copy = new float[Ratings.PlayerCount];

                if (source != null)
                {
                    Array.Copy(source, copy, Math.Min(source.Length, copy.Length));
                }

                rows[row] = copy;
            }

            return rows;
        }

        // The goalie's sixteen ratings at a level, or null.
        public float[] GetGoalieRatings(int difficultyLevel)
        {
            return Goalie.GetRatingTableOrEasy(difficultyLevel);
        }

        // The roster name for a ratings row: GetRosterName in teamblock.h returns the definition
        // name at kRowRosterSlots[row].
        public string GetRosterName(int row)
        {
            if (row < 0 || row >= TeamLoadLayout.RowCount)
            {
                return null;
            }

            return Slots[TeamLoadLayout.RowRosterSlots[row]].Name;
        }

        public override string ToString()
        {
            return "TeamFile(" + FileName + " \"" + Name + "\")";
        }
    }
}
