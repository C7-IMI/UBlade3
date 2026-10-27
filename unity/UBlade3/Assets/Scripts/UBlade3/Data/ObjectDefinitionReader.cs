using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // Port of GameEngine::ParseObjectDefinition (HYPERX 0x00482170) and the five command parsers
    // that fill an ObjectDef. This one reader serves .HTM team files, .ENV drome files and
    // COMMON.CFG because the 1996 executable reads all three through the same function.
    //
    // Citations, all in third_party/hyperblade-decomp/src/engine/gameengine.cpp:
    //   ParseObjectDefinition   HYPERX 0x00482170  (lines 1340-1446)
    //   Cmd_DefineObject        HYPERX 0x00482390  (lines 1484-1739)
    //   Cmd_SetMotionParams     HYPERX 0x00482b00  (lines 1757-1882)
    //   Cmd_SetCollisionObject  HYPERX 0x00483300  (lines 2166-2433)
    //   Cmd_SetRatings          HYPERX 0x00483b50  (lines 2442-2711)
    //   Cmd_SetPlans            HYPERX 0x00484460  (lines 2832-2856)
    public sealed class ObjectDefinitionReader
    {
        public const int CommandDefineObject = 0;
        public const int CommandEndObjectDefinition = 1;
        public const int CommandMotionParams = 2;
        public const int CommandRatings = 3;
        public const int CommandRatings1 = 4;
        public const int CommandRatings2 = 5;
        public const int CommandRatings3 = 6;
        public const int CommandGoalieRatings = 7;
        public const int CommandGoalieRatings1 = 8;
        public const int CommandGoalieRatings2 = 9;
        public const int CommandGoalieRatings3 = 10;
        public const int CommandGoalieRatingsSpecial = 11;
        public const int CommandCollisionObject = 12;
        public const int CommandPlans = 13;

        public static readonly string[] CommandNames =
        {
            "DefineObject", "EndObjectDefinition", "SetMotionParams", "SetRatings", "SetRatings1",
            "SetRatings2", "SetRatings3", "SetGoalieRatings", "SetGoalieRatings1",
            "SetGoalieRatings2", "SetGoalieRatings3", "SetSpecialGoalieRatings",
            "SetCollisionObject", "SetPlans"
        };

        private static readonly string[] DefineObjectAttributes = { "None", "type", "modfile", "name", "mat", "UseMyPallette" };

        private static readonly string[] MotionKeywords = { "None", "name", "pos", "accel", "speed", "azim", "elev", "fric", "elas" };

        private static readonly string[] CollisionAttributes = { "type", "component", "radius", "centroid", "attachPoint", "useDetail" };

        private readonly DataTextFile file;

        private readonly DataParseOptions options;

        private readonly string sourcePath;

        public ObjectDefinitionReader(DataTextFile file, string sourcePath, DataParseOptions options)
        {
            this.file = file;
            this.sourcePath = sourcePath ?? (file == null ? string.Empty : file.Path);
            this.options = options ?? DataParseOptions.Default;
        }

        public DataParseOptions Options
        {
            get { return options; }
        }

        public string SourcePath
        {
            get { return sourcePath; }
        }

        public static List<ObjectDefinition> ReadAll(GameDataRoot root, string relativePath, DataParseOptions options)
        {
            DataTextFile textFile = root.OpenText(relativePath);
            ObjectDefinitionReader reader = new ObjectDefinitionReader(textFile, textFile.Path, options);
            return reader.ReadAll();
        }

        public List<ObjectDefinition> ReadAll()
        {
            List<ObjectDefinition> definitions = new List<ObjectDefinition>();
            ObjectDefinition definition;

            while ((definition = ReadNext()) != null)
            {
                definitions.Add(definition);
            }

            return definitions;
        }

        // Returns the next complete definition, or null at end of file.
        public ObjectDefinition ReadNext()
        {
            ObjectDefinition definition = null;
            bool complete = false;
            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text;
                int position = 0;
                string command;

                if (!DataParse.ParseToken(text, ref position, " \t", DataParse.TokenMax, out command))
                {
                    continue;
                }

                int commandIndex = FindCommand(command);

                if (commandIndex < 0)
                {
                    // The original ignores a line whose first token is not one of the fourteen.
                    continue;
                }

                switch (commandIndex)
                {
                    case CommandDefineObject:
                        definition = ParseDefineObject(text, position, line.LineNumber);
                        break;

                    case CommandEndObjectDefinition:
                        if (definition != null)
                        {
                            ApplyEndDefinitionRules(definition);
                            complete = true;
                        }

                        break;

                    case CommandMotionParams:
                        if (definition != null)
                        {
                            ParseMotionParams(definition, text, position, line.LineNumber);
                        }

                        break;

                    case CommandRatings:
                    case CommandRatings1:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Field, RatingLevel.Easy);
                        }

                        break;

                    case CommandRatings2:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Field, RatingLevel.Medium);
                        }

                        break;

                    case CommandRatings3:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Field, RatingLevel.Hard);
                        }

                        break;

                    case CommandGoalieRatings:
                    case CommandGoalieRatings1:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Goalie, RatingLevel.Easy);
                        }

                        break;

                    case CommandGoalieRatings2:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Goalie, RatingLevel.Medium);
                        }

                        break;

                    case CommandGoalieRatings3:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Goalie, RatingLevel.Hard);
                        }

                        break;

                    case CommandGoalieRatingsSpecial:
                        if (definition != null)
                        {
                            ParseRatings(definition, text, position, line.LineNumber, RatingsMode.Goalie, RatingLevel.SpecialGoalie);
                        }

                        break;

                    case CommandCollisionObject:
                        if (definition != null)
                        {
                            ParseCollisionObject(definition, text, position, line.LineNumber);
                        }

                        break;

                    case CommandPlans:
                        if (definition != null)
                        {
                            ParsePlans(definition, text, position, line.LineNumber);
                        }

                        break;
                }

                if (definition == null)
                {
                    throw new DataFormatException(
                        "Game Config file error: missing DefineObject before other parameters \"" + text.Trim() + "\"",
                        sourcePath,
                        line.LineNumber);
                }

                if (complete)
                {
                    return definition;
                }
            }

            if (definition != null)
            {
                throw new DataFormatException(
                    "Game Config file error: missing EndObjectDefinition for " + definition.Name,
                    sourcePath,
                    definition.SourceLine);
            }

            return null;
        }

        // SetSpecialRatings is JAMCA.HTM's misspelling; only PlanFixes::On reads it as the special
        // goalie block (hyperblade-20XX gameengine.cpp lines 1283-1294).
        private int FindCommand(string command)
        {
            int commandIndex = DataParse.FindKeyword(command, CommandNames);

            if (commandIndex < 0 && options.PlanFixes && DataParse.EqualsIgnoreCase(command, "SetSpecialRatings"))
            {
                return CommandGoalieRatingsSpecial;
            }

            return commandIndex;
        }

        // Cmd_DefineObject.
        private ObjectDefinition ParseDefineObject(string text, int position, int lineNumber)
        {
            ObjectDefinition definition = new ObjectDefinition();

            definition.SourceLine = lineNumber;
            definition.SourcePath = sourcePath;

            int seen = 0;
            string token;

            while (position >= 0 && DataParse.ParseToken(text, ref position, " =\t", DataParse.TokenMax, out token))
            {
                int attribute = DataParse.FindKeyword(token, DefineObjectAttributes);

                if (attribute <= 0)
                {
                    break;
                }

                switch (attribute)
                {
                    case 1: // type
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: bad type keyword", lineNumber);
                        }

                        int typeIndex = ObjectTypes.Find(token);

                        if (typeIndex < 0)
                        {
                            throw Fail("Game Config file error: bad type keyword \"" + token + "\"", lineNumber);
                        }

                        definition.TypeIndex = typeIndex;
                        definition.Kind = ObjectTypes.Kinds[typeIndex];
                        definition.TypeName = ObjectTypes.Names[typeIndex];
                        seen |= 1;
                        break;

                    case 2: // modfile
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: Bad model name", lineNumber);
                        }

                        definition.Model = token;
                        seen |= 2;
                        break;

                    case 3: // name
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: Bad object name", lineNumber);
                        }

                        definition.Name = DataParse.ReplaceNameSpacers(token);
                        seen |= 4;
                        break;

                    case 4: // mat
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: Bad matXref name", lineNumber);
                        }

                        definition.Material = token;
                        break;

                    case 5: // UseMyPallette (sic)
                        definition.OwnPalette = true;
                        break;
                }
            }

            if ((seen & 7) != 7)
            {
                throw Fail("Incomplete Game Object def: \"" + text.Trim() + "\"", lineNumber);
            }

            definition.RatingsMode = RatingsMode.None;

            return definition;
        }

        // Cmd_SetMotionParams.
        private void ParseMotionParams(ObjectDefinition definition, string text, int position, int lineNumber)
        {
            string token;

            while (position >= 0 && DataParse.ParseToken(text, ref position, " =\t", DataParse.TokenMax, out token))
            {
                position = DataParse.SkipToChar(text, position, '=');

                if (position < 0)
                {
                    break;
                }

                int keyword = DataParse.FindKeyword(token, MotionKeywords);

                if (keyword <= 0)
                {
                    break;
                }

                position++;

                switch (keyword)
                {
                    case 2: // pos
                        definition.Position = ReadVec3(text, ref position, lineNumber, "pos");
                        break;

                    case 3: // accel
                        definition.Accel = ReadFloat(text, ref position, lineNumber, "accel");
                        break;

                    case 4: // speed
                        definition.Speed = ReadFloat(text, ref position, lineNumber, "speed");
                        break;

                    case 5: // azim (degrees)
                        definition.Azimuth = ReadFloat(text, ref position, lineNumber, "azim");
                        break;

                    case 6: // elev (degrees)
                        definition.Elevation = ReadFloat(text, ref position, lineNumber, "elev");
                        break;

                    case 7: // fric
                        definition.Friction = ReadFloat(text, ref position, lineNumber, "fric");
                        break;

                    case 8: // elas
                        definition.Elasticity = ReadFloat(text, ref position, lineNumber, "elas");
                        break;
                }
            }
        }

        // Cmd_SetRatings. The two tables are the original's apszPlayer (64 entries, EndRatings last)
        // and apszGoalie (17 entries, EndRatings last).
        private void ParseRatings(ObjectDefinition definition, string text, int position, int lineNumber, RatingsMode mode, RatingLevel level)
        {
            int levelIndex = (int)level;

            if (levelIndex < 0 || levelIndex >= definition.Ratings.Length)
            {
                return;
            }

            int ratingCount = mode == RatingsMode.Field ? Ratings.PlayerCount : Ratings.GoalieCount;
            string[] names = mode == RatingsMode.Field ? Ratings.PlayerNames : Ratings.GoalieNames;

            if (definition.Ratings[levelIndex] == null)
            {
                definition.RatingsMode = mode;
                definition.Ratings[levelIndex] = new float[ratingCount];

                if (mode == RatingsMode.Field)
                {
                    for (int i = 0; i < ratingCount; i++)
                    {
                        definition.Ratings[levelIndex][i] = 0.0f;
                    }

                    if (options.PlanFixes)
                    {
                        definition.Ratings[levelIndex][RatingIndex.SubInDamage] = Ratings.UnsetValue;
                        definition.Ratings[levelIndex][RatingIndex.MinSubIn] = Ratings.UnsetValue;
                    }
                }
                else
                {
                    for (int i = 0; i < ratingCount; i++)
                    {
                        definition.Ratings[levelIndex][i] = Ratings.GoalieDefaultValue;
                    }
                }
            }

            float[] table = definition.Ratings[levelIndex];
            string token;

            while (position >= 0 && DataParse.ParseToken(text, ref position, " =\t", DataParse.TokenMax, out token))
            {
                position = DataParse.SkipToChar(text, position, '=');

                if (position < 0)
                {
                    break;
                }

                int rating = DataParse.FindKeyword(token, names);

                if (rating < 0 && options.PlanFixes && mode == RatingsMode.Field)
                {
                    string canonical = Ratings.ResolvePlayerAlias(token);

                    if (canonical != null)
                    {
                        rating = DataParse.FindKeyword(canonical, names);
                    }
                }

                if (rating < 0)
                {
                    definition.RatingIssues.Add(new RatingIssue(token, null, lineNumber));
                    break;
                }

                position++;

                string valueToken;

                if (!DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out valueToken))
                {
                    throw Fail("Game Config file error: ratings for object \"" + definition.Name + "\"", lineNumber);
                }

                int value;

                if (!DataParse.TryScanInt(valueToken, out value))
                {
                    throw Fail("Game Config file error: ratings for object \"" + definition.Name + "\" value \"" + valueToken + "\"", lineNumber);
                }

                if (rating >= ratingCount)
                {
                    // EndRatings: the original writes one past the array and then dies.
                    throw Fail("Game Config file error: EndRatings has no storage for object \"" + definition.Name + "\"", lineNumber);
                }

                table[rating] = (float)value;
            }
        }

        // Cmd_SetCollisionObject.
        private void ParseCollisionObject(ObjectDefinition definition, string text, int position, int lineNumber)
        {
            int bubbleKind = -1;
            short component = -1;
            float radius = -1.0f; // COLLISION_NO_RADIUS
            Vec3 centroid = new Vec3();
            bool hasCentroid = false;
            Vec3 attachPoint = new Vec3();
            bool hasAttachPoint = false;
            bool useDetail = false;
            string token;

            while (position >= 0 && DataParse.ParseToken(text, ref position, " =\t", DataParse.TokenMax, out token))
            {
                int attribute = DataParse.FindKeyword(token, CollisionAttributes);

                if (attribute < 0)
                {
                    break;
                }

                switch (attribute)
                {
                    case 0: // type
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: bad collision object type keyword", lineNumber);
                        }

                        bubbleKind = CollisionBubbles.Find(token);

                        if (bubbleKind < 0)
                        {
                            throw Fail("Game Config file error: bad collision object type keyword \"" + token + "\"", lineNumber);
                        }

                        // A RandomizerBubble on a RedRandomizer becomes the kludge kind
                        // ("MAJOR KLUDGE HERE", gameengine.cpp lines 2283-2286).
                        if (bubbleKind == (int)CollisionBubbleKind.RandomizerBubble && definition.TypeIndex == ObjectTypes.GobRedRandomizer)
                        {
                            bubbleKind = CollisionBubbles.KludgeKind;
                        }

                        break;

                    case 1: // component
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: collision radius", lineNumber);
                        }

                        if (!DataParse.TryScanShort(token, out component))
                        {
                            throw Fail("Game Config file error: collision component \"" + token + "\"", lineNumber);
                        }

                        break;

                    case 2: // radius
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
                        {
                            throw Fail("Game Config file error: collision radius", lineNumber);
                        }

                        if (!DataParse.TryScanFloat(token, out radius))
                        {
                            throw Fail("Game Config file error: collision radius \"" + token + "\"", lineNumber);
                        }

                        break;

                    case 3: // centroid
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0)
                        {
                            throw Fail("Game Config file error: collision centroid", lineNumber);
                        }

                        centroid = ReadVec3(text, ref position, lineNumber, "collision centroid");
                        hasCentroid = true;
                        break;

                    case 4: // attachPoint
                        position = DataParse.SkipPastChar(text, position, '=');

                        if (position < 0)
                        {
                            throw Fail("Game Config file error: collision attachPoint", lineNumber);
                        }

                        attachPoint = ReadVec3(text, ref position, lineNumber, "collision attachPoint");
                        hasAttachPoint = true;
                        break;

                    case 5: // useDetail
                        useDetail = true;
                        break;
                }
            }

            if (bubbleKind < 0)
            {
                throw Fail("Game Config error: collision object type missing: \"" + text.Trim() + "\"", lineNumber);
            }

            if (!hasCentroid)
            {
                throw Fail("Game Config error: collision centroid missing: \"" + text.Trim() + "\"", lineNumber);
            }

            CollisionBubble bubble = new CollisionBubble();

            bubble.Kind = (CollisionBubbleKind)bubbleKind;
            bubble.Shape = CollisionBubbles.Shapes[bubbleKind];
            bubble.Slot = bubbleKind;
            bubble.Radius = radius;
            bubble.HasCentroid = true;
            bubble.Centroid = centroid;
            bubble.SourceLine = lineNumber;

            if (hasAttachPoint)
            {
                bubble.HasAttachPoint = true;
                bubble.AttachPoint = attachPoint;
                bubble.AttachLinkId = component;
            }
            else if (useDetail)
            {
                bubble.UseDetail = true;
            }

            if (definition.Collision.Count > 0)
            {
                CollisionBubble tail = definition.Collision[definition.Collision.Count - 1];

                tail.ChildCount++;
                bubble.Parent = tail;
            }

            definition.Collision.Add(bubble);
        }

        // Cmd_SetPlans.
        private void ParsePlans(ObjectDefinition definition, string text, int position, int lineNumber)
        {
            string token;

            while (position >= 0 && DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
            {
                definition.Plans.Add(token);
            }
        }

        // The EndObjectDefinition hook, only with PlanFixes on (hyperblade-20XX gameengine.cpp
        // lines 1372-1406): the easy block's missing SubInDamage and MinSubIn become 50 and 40, the
        // medium and hard blocks become 0.
        private void ApplyEndDefinitionRules(ObjectDefinition definition)
        {
            if (!options.PlanFixes || definition.RatingsMode != RatingsMode.Field)
            {
                return;
            }

            for (int level = 0; level < 3; level++)
            {
                float[] table = definition.Ratings[level];

                if (table == null)
                {
                    continue;
                }

                if (table[RatingIndex.SubInDamage] == Ratings.UnsetValue)
                {
                    table[RatingIndex.SubInDamage] = level == 0 ? 50.0f : 0.0f;
                }

                if (table[RatingIndex.MinSubIn] == Ratings.UnsetValue)
                {
                    table[RatingIndex.MinSubIn] = level == 0 ? 40.0f : 0.0f;
                }
            }
        }

        private float ReadFloat(string text, ref int position, int lineNumber, string field)
        {
            string token;

            if (!DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out token))
            {
                throw Fail("Game Config file error: " + field + " param missing", lineNumber);
            }

            float value;

            if (!DataParse.TryScanFloat(token, out value))
            {
                throw Fail("Game Config file error: " + field + " param \"" + token + "\"", lineNumber);
            }

            return value;
        }

        private Vec3 ReadVec3(string text, ref int position, int lineNumber, string field)
        {
            float x = ReadFloat(text, ref position, lineNumber, field);
            float y = ReadFloat(text, ref position, lineNumber, field);
            float z = ReadFloat(text, ref position, lineNumber, field);

            return new Vec3(x, y, z);
        }

        private DataFormatException Fail(string message, int lineNumber)
        {
            return new DataFormatException(message, sourcePath, lineNumber);
        }
    }
}
