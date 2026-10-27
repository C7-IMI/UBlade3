using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // The tiny vector type the loaders store: src/math/vec3.h. Units are the original's: world
    // units for pos/centroid/attachPoint, degrees for azimuth and elevation (objectdef.h line 13).
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString()
        {
            return "(" + x.ToString("R") + ", " + y.ToString("R") + ", " + z.ToString("R") + ")";
        }
    }

    // EngineObject::m_nKind values, src/engine/objectkind.h. Two goals share KIND_GOAL, both cameras
    // share KIND_CAMERA, SoundMan shares KIND_GENERIC and no keyword produces 0x0e.
    public enum ObjectKind
    {
        Null = 0x00,
        Dromer = 0x01,
        Rok = 0x02,
        Drome = 0x03,
        Ramp = 0x04,
        Goal = 0x05,
        Camera = 0x06,
        StartGun = 0x07,
        Turnstile = 0x08,
        Multiplier = 0x09,
        LaserHurdle = 0x0a,
        GreenRandomizer = 0x0b,
        RedRandomizer = 0x0c,
        God = 0x0d,
        LimboPole = 0x0f,
        Hurdle = 0x10,
        FastLane = 0x11,
        Explosion = 0x12,
        Armory = 0x13,
        Keyboard = 0x14,
        Gamepad = 0x15,
        Joystick = 0x16,
        Mouse = 0x17,
        Generic = 0x18,
        SlowLane = 0x19,
        Mine = 0x1a,
        Swarf = 0x1b,
        Coach = 0x1c,
        BodyPart = 0x1d
    }

    // id_GOB_* type indices from DATA/ENUMIDS.H lines 86-117, in the order of
    // GameEngine::Cmd_DefineObject's apszTypes/anTypeIds tables (HYPERX 0x00482390,
    // third_party/hyperblade-decomp/src/engine/gameengine.cpp lines 1509-1578).
    public static class ObjectTypes
    {
        public const int Count = 32;

        public const int GobNull = 0;
        public const int GobDromer = 1;
        public const int GobRok = 2;
        public const int GobDrome = 3;
        public const int GobRamp = 4;
        public const int GobNGoal = 5;
        public const int GobSGoal = 6;
        public const int GobMainCamera = 7;
        public const int GobHeadCam = 8;
        public const int GobStartGun = 9;
        public const int GobTurnstile = 10;
        public const int GobMultiplier = 11;
        public const int GobLaserHurdle = 12;
        public const int GobGreenRandomizer = 13;
        public const int GobRedRandomizer = 14;
        public const int GobGod = 15;
        public const int GobSoundMan = 16;
        public const int GobLimboPole = 17;
        public const int GobHurdle = 18;
        public const int GobFastLane = 19;
        public const int GobExplosion = 20;
        public const int GobArmory = 21;
        public const int GobKeyboard = 22;
        public const int GobGamePad = 23;
        public const int GobJoystick = 24;
        public const int GobMouse = 25;
        public const int GobSlowLane = 26;
        public const int GobMine = 27;
        public const int GobSwarf = 28;
        public const int GobCoach = 29;
        public const int GobBodyPart = 30;
        public const int GobGeneric = 31;

        public static readonly string[] Names =
        {
            "Null", "Dromer", "Rok", "Drome", "Ramp", "NGoal", "SGoal", "MainCamera", "HeadCam",
            "StartGun", "Turnstile", "Multiplier", "LaserHurdle", "GreenRandomizer", "RedRandomizer",
            "God", "SoundMan", "LimboPole", "Hurdle", "FastLane", "Explosion", "Armory", "Keyboard",
            "Gamepad", "Joystick", "Mouse", "SlowLane", "Mine", "Swarf", "Coach", "BodyPart", "Generic"
        };

        public static readonly ObjectKind[] Kinds =
        {
            ObjectKind.Null, ObjectKind.Dromer, ObjectKind.Rok, ObjectKind.Drome, ObjectKind.Ramp,
            ObjectKind.Goal, ObjectKind.Goal, ObjectKind.Camera, ObjectKind.Camera, ObjectKind.StartGun,
            ObjectKind.Turnstile, ObjectKind.Multiplier, ObjectKind.LaserHurdle,
            ObjectKind.GreenRandomizer, ObjectKind.RedRandomizer, ObjectKind.God, ObjectKind.Generic,
            ObjectKind.LimboPole, ObjectKind.Hurdle, ObjectKind.FastLane, ObjectKind.Explosion,
            ObjectKind.Armory, ObjectKind.Keyboard, ObjectKind.Gamepad, ObjectKind.Joystick,
            ObjectKind.Mouse, ObjectKind.SlowLane, ObjectKind.Mine, ObjectKind.Swarf,
            ObjectKind.Coach, ObjectKind.BodyPart, ObjectKind.Generic
        };

        public static int Find(string typeName)
        {
            return DataParse.FindKeyword(typeName, Names);
        }
    }

    // BubbleCmd::m_wShape, src/physics/bubblecmd.h lines 12-14.
    public enum BubbleShape
    {
        Sphere = 1,
        Attached = 4,
        Caged = 5
    }

    // GameEngine::Cmd_SetCollisionObject's apszBubbles order (HYPERX 0x00483300,
    // third_party/hyperblade-decomp/src/engine/gameengine.cpp lines 2182-2237).
    public enum CollisionBubbleKind
    {
        DromerAwarenessBubble = 0,
        DromerModelBubble = 1,
        DromerBodyBubble = 2,
        DromerScoopBubble = 3,
        RampBubble = 4,
        RokBubble = 5,
        GoalSideBubble = 6,
        GoalZoneBubble = 7,
        GoalCaseBubble = 8,
        GoalPortBubble = 9,
        CameraBubble = 10,
        HeadCamBubble = 11,
        MultiplierBubble = 12,
        TurnstileBubble = 13,
        StartgunBubble = 14,
        LaserHurdleBubble = 15,
        ArmoryBubble = 16,
        LimboPoleBubble = 17,
        FastLaneBubble = 18,
        ExplosionBubble = 19,
        HurdleBubble = 20,
        RandomizerBubble = 21,
        KludgeBubble = 22,
        SlowLaneBubble = 23,
        MineBubble = 24,
        SwarfBubble = 25,
        BodyPartBubble = 26
    }

    public static class CollisionBubbles
    {
        public const int Count = 27;

        public const int KludgeKind = 22;

        public static readonly string[] Names =
        {
            "DromerAwarenessBubble", "DromerModelBubble", "DromerBodyBubble", "DromerScoopBubble",
            "RampBubble", "RokBubble", "GoalSideBubble", "GoalZoneBubble", "GoalCaseBubble",
            "GoalPortBubble", "CameraBubble", "HeadCamBubble", "MultiplierBubble", "TurnstileBubble",
            "StartgunBubble", "LaserHurdleBubble", "ArmoryBubble", "LimboPoleBubble", "FastLaneBubble",
            "ExplosionBubble", "HurdleBubble", "RandomizerBubble", "MAJOR KLUDGE HERE", "SlowLaneBubble",
            "MineBubble", "SwarfBubble", "BodyPartBubble"
        };

        // anBubbleShapes: sphere except the scoop (attached) and the ramp (caged).
        public static readonly BubbleShape[] Shapes =
        {
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Attached,
            BubbleShape.Caged, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere,
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere,
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere,
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere,
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere,
            BubbleShape.Sphere, BubbleShape.Sphere, BubbleShape.Sphere
        };

        public static int Find(string name)
        {
            return DataParse.FindKeyword(name, Names);
        }
    }

    // One BubbleCmd in an ObjectDef's collision chain. The original links each new command as the
    // child of the previous tail (gameengine.cpp lines 2377-2432); the data layer keeps the flat
    // order and the parent link so both readings survive.
    public sealed class CollisionBubble
    {
        public CollisionBubbleKind Kind;
        public BubbleShape Shape;

        // The value of BubbleCmd::m_nSlot before GameEngine::MapModifierToId (HYPERX 0x00484070)
        // rewrites it to a CollisionObjectID. The rewrite needs the object's model ID, so it belongs
        // to the object-creation layer, not the text parser.
        public int Slot;

        public CollisionBubble Parent;
        public int ChildCount;

        public bool UseDetail;
        public float Radius = -1.0f; // COLLISION_NO_RADIUS, gameengine.cpp line 1927
        public bool HasCentroid;
        public Vec3 Centroid;
        public bool HasAttachPoint;
        public Vec3 AttachPoint;
        public short AttachLinkId;
        public int SourceLine;
    }

    // Rating ids, DATA/ENUMIDS.H lines 256-315 / src/engine/statids.h. A skater has 63 ratings
    // (index 7 is PassAccuracy; statids.h leaves it unnamed because nothing reads it by macro).
    public static class RatingIndex
    {
        public const int WeightClass = 0;
        public const int Stamina = 1;
        public const int Checking = 2;
        public const int Balance = 3;
        public const int Speed = 4;
        public const int ShotPower = 5;
        public const int ShotAccuracy = 6;
        public const int PassAccuracy = 7;
        public const int OffensiveAbility = 8;
        public const int LongShotEnable = 9;
        public const int Immunity = 10;
        public const int LongPassEnable = 11;
        public const int MultiPointSpread = 12;
        public const int RokHandling = 13;
        public const int ArmoryRange = 14;
        public const int Damage = 15;
        public const int Gender = 16;
        public const int Speedburst0a = 17;
        public const int Speedburst0b = 18;
        public const int Speedburst1a = 19;
        public const int Speedburst1b = 20;
        public const int Speedburst2a = 21;
        public const int Speedburst2b = 22;
        public const int Speedburst3a = 23;
        public const int Speedburst3b = 24;
        public const int Speedburst4a = 25;
        public const int Speedburst4b = 26;
        public const int Bungee0 = 27;
        public const int Bungee1 = 28;
        public const int Bungee2 = 29;
        public const int Pass0 = 30;
        public const int Pass1 = 31;
        public const int Pass2 = 32;
        public const int WinCloseFreeRok = 33;
        public const int LoseCloseFreeRok = 34;
        public const int WinFarFreeRok = 35;
        public const int LoseFarFreeRok = 36;
        public const int WinOffWingJob = 37;
        public const int LoseOffWingJob = 38;
        public const int Multiplier0 = 39;
        public const int Multiplier1 = 40;
        public const int Turnstile0 = 41;
        public const int Turnstile1 = 42;
        public const int Killball0 = 43;
        public const int Killball1 = 44;
        public const int Randomize0 = 45;
        public const int Randomize1 = 46;
        public const int Fighting0 = 47;
        public const int Fighting1 = 48;
        public const int Fighting2 = 49;
        public const int Fighting3 = 50;
        public const int Fighting4 = 51;
        public const int FlyingRepetoire = 52;
        public const int HalfpipeRepetoire = 53;
        public const int CarryRokThrowDist = 54;
        public const int WinCarryRokRegroupDist = 55;
        public const int LoseCarryRokRegroupDist = 56;
        public const int WinAggressionOnOthers = 57;
        public const int LoseAggressionOnOthers = 58;
        public const int SubOutDamage = 59;
        public const int SubInDamage = 60;
        public const int MultiRange = 61;
        public const int MinSubIn = 62;

        public const int Count = 0x3f;
    }

    public static class GoalieRatingIndex
    {
        public const int WeightClass = 0;
        public const int Strength = 1;
        public const int ReactionTime = 2;
        public const int Balance = 3;
        public const int Speed = 4;
        public const int PassAccuracy = 5;
        public const int Intelligence = 6;
        public const int UnderPressure = 7;
        public const int SaveRight = 8;
        public const int SaveLeft = 9;
        public const int SaveHigh = 10;
        public const int SaveLow = 11;
        public const int ShieldBlocking = 12;
        public const int CatchingAbility = 13;
        public const int Aggression = 14;
        public const int Overall = 15;

        public const int Count = 16;
    }

    // The two ratings tables from GameEngine::Cmd_SetRatings (HYPERX 0x00483b50,
    // third_party/hyperblade-decomp/src/engine/gameengine.cpp lines 2444-2588).
    public static class Ratings
    {
        public const int PlayerCount = RatingIndex.Count;
        public const int GoalieCount = GoalieRatingIndex.Count;

        // GOALIE_RATING_DEFAULT, gameengine.cpp line 2438.
        public const float GoalieDefaultValue = 5.0f;

        // kRatingUnset with the plan fixes on, gameengine.cpp line 1297 of hyperblade-20XX.
        public const float UnsetValue = -1.0f;

        public const string EndRatings = "EndRatings";

        public static readonly string[] PlayerNames =
        {
            "id_RAT_WeightClass", "id_RAT_Stamina", "id_RAT_Checking", "id_RAT_Balance",
            "id_RAT_Speed", "id_RAT_ShotPower", "id_RAT_ShotAccuracy", "id_RAT_PassAccuracy",
            "id_RAT_OffensiveAbility", "id_RAT_LongShotEnable", "id_RAT_Immunity",
            "id_RAT_LongPassEnable", "id_RAT_MultiPointSpread", "id_RAT_RokHandling",
            "id_RAT_ArmoryRange", "id_RAT_Damage", "id_RAT_Gender", "id_RAT_Speedburst0a",
            "id_RAT_Speedburst0b", "id_RAT_Speedburst1a", "id_RAT_Speedburst1b",
            "id_RAT_Speedburst2a", "id_RAT_Speedburst2b", "id_RAT_Speedburst3a",
            "id_RAT_Speedburst3b", "id_RAT_Speedburst4a", "id_RAT_Speedburst4b",
            "id_RAT_Bungee0", "id_RAT_Bungee1", "id_RAT_Bungee2", "id_RAT_Pass0", "id_RAT_Pass1",
            "id_RAT_Pass2", "id_RAT_WinCloseFreeRok", "id_RAT_LoseCloseFreeRok",
            "id_RAT_WinFarFreeRok", "id_RAT_LoseFarFreeRok", "id_RAT_WinOffWingJob",
            "id_RAT_LoseOffWingJob", "id_RAT_Multiplier0", "id_RAT_Multiplier1",
            "id_RAT_Turnstile0", "id_RAT_Turnstile1", "id_RAT_Killball0", "id_RAT_Killball1",
            "id_RAT_Randomize0", "id_RAT_Randomize1", "id_RAT_Fighting0", "id_RAT_Fighting1",
            "id_RAT_Fighting2", "id_RAT_Fighting3", "id_RAT_Fighting4", "id_RAT_FlyingRepetoire",
            "id_RAT_HalfpipeRepetoire", "id_RAT_CarryRokThrowDist", "id_RAT_WinCarryRokRegroupDist",
            "id_RAT_LoseCarryRokRegroupDist", "id_RAT_WinAggressionOnOthers",
            "id_RAT_LoseAggressionOnOthers", "id_RAT_SubOutDamage", "id_RAT_SubInDamage",
            "id_RAT_MultiRange", "id_RAT_MinSubIn"
        };

        public static readonly string[] GoalieNames =
        {
            "id_GRT_WeightClass", "id_GRT_Strength", "id_GRT_ReactionTime", "id_GRT_Balance",
            "id_GRT_Speed", "id_GRT_PassAccuracy", "id_GRT_Intelligence", "id_GRT_UnderPressure",
            "id_GRT_SaveRight", "id_GRT_SaveLeft", "id_GRT_SaveHigh", "id_GRT_SaveLow",
            "id_GRT_ShieldBlocking", "id_GRT_CatchingAbility", "id_GRT_Aggression", "id_GRT_Overall"
        };

        // Old names and typos in the shipped team files. The 1996 loader stops the line at the first
        // unknown rating name; hyperblade-20XX reads them with PlanFixes::On (gameengine.cpp lines
        // 2666-2683) and so does UBlade3 when DataParseOptions.PlanFixes is true (the default).
        public static readonly string[][] PlayerAliases =
        {
            new string[] { "id_RAT_RokReceiving", "id_RAT_MultiPointSpread" },
            new string[] { "id_RAT_Stealing", "id_RAT_RokHandling" },
            new string[] { "d_RAT_SubInDamage", "id_RAT_SubInDamage" },
            new string[] { "id_RAT_Pass", "id_RAT_Pass2" }
        };

        public static int FindPlayer(string token)
        {
            return DataParse.FindKeyword(token, PlayerNames);
        }

        public static int FindGoalie(string token)
        {
            return DataParse.FindKeyword(token, GoalieNames);
        }

        // Gives the canonical rating name for an alias, or null.
        public static string ResolvePlayerAlias(string token)
        {
            foreach (string[] alias in PlayerAliases)
            {
                if (DataParse.EqualsIgnoreCase(alias[0], token))
                {
                    return alias[1];
                }
            }

            return null;
        }
    }

    public enum RatingLevel
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        SpecialGoalie = 3
    }

    // RATINGS_FIELD / RATINGS_GOALIE / RATINGS_SPECIAL_GOALIE, src/engine/objectdef.h.
    public enum RatingsMode
    {
        None = 0,
        Field = 1,
        Goalie = 2
    }

    // A rating name the loader did not recognise (with the fixes off the rest of that line is skipped,
    // as in the 1996 executable). Kept for the format documentation and the data probe digest.
    public sealed class RatingIssue
    {
        public readonly string Token;
        public readonly string Value;
        public readonly int LineNumber;

        public RatingIssue(string token, string value, int lineNumber)
        {
            Token = token;
            Value = value;
            LineNumber = lineNumber;
        }
    }

    public sealed class DataParseOptions
    {
        // True matches hyperblade-20XX's default: read the shipped typos and the JAMCA
        // "SetSpecialRatings" line, and fill the easy SubInDamage/MinSubIn defaults. False reproduces
        // the 1996 executable, which stops the rating line at the first unknown name.
        public bool PlanFixes = true;

        public static DataParseOptions Default
        {
            get { return new DataParseOptions(); }
        }

        public static DataParseOptions Original1996
        {
            get { return new DataParseOptions { PlanFixes = false }; }
        }
    }

    // ObjectDef, src/engine/objectdef.h. Field names follow the original's members and units.
    public sealed class ObjectDefinition
    {
        public string Name;
        public string Model;
        public string Material;
        public bool OwnPalette;
        public int TypeIndex;
        public ObjectKind Kind;
        public string TypeName;

        // Cmd_SetMotionParams (HYPERX 0x00482b00). Azimuth and elevation are degrees.
        public Vec3 Position;
        public float Accel;
        public float Speed;
        public float Azimuth;
        public float Elevation;
        public float Friction;
        public float Elasticity;

        // Cmd_SetPlans (HYPERX 0x00484460): plan names in file order.
        public readonly List<string> Plans = new List<string>();

        // Cmd_SetCollisionObject (HYPERX 0x00483300): the bubble chain in file order.
        public readonly List<CollisionBubble> Collision = new List<CollisionBubble>();

        // m_nRatingsMode and one table per level: Easy, Medium, Hard, and the special goalie set.
        // A table is null when no rating command assigned to it.
        public RatingsMode RatingsMode = RatingsMode.None;
        public readonly float[][] Ratings = new float[4][];

        // The model ID the loader would assign this definition; -1 when none is assigned yet.
        public int ModelId = -1;

        // ObjectDef::m_bForceRemap, set on every away-team definition (gameengine.cpp line 3291).
        public bool ForceRemap;

        public int SourceLine;

        public string SourcePath;

        // Rating tokens the loader did not recognise (typos in the shipped files), in file order.
        public readonly List<RatingIssue> RatingIssues = new List<RatingIssue>();

        public bool HasRatingTable(int level)
        {
            return level >= 0 && level < Ratings.Length && Ratings[level] != null;
        }

        public float[] GetRatingTableOrEasy(int level)
        {
            if (HasRatingTable(level))
            {
                return Ratings[level];
            }

            if (HasRatingTable(0))
            {
                return Ratings[0];
            }

            return null;
        }

        public float GetPlayerRating(int level, int rating, float defaultValue)
        {
            float[] table = GetRatingTableOrEasy(level);

            if (table == null || rating < 0 || rating >= table.Length)
            {
                return defaultValue;
            }

            return table[rating];
        }

        public float GetGoalieRating(int level, int rating, float defaultValue)
        {
            return GetPlayerRating(level, rating, defaultValue);
        }

        public bool IsKind(ObjectKind kind)
        {
            return Kind == kind;
        }

        public override string ToString()
        {
            return TypeName + " " + Name + " (" + Model + ")";
        }
    }
}
