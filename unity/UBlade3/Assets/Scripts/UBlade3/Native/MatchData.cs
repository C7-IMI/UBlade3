using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UBlade3.Data;
using UBlade3.Sim;
using UnityEngine;
using DataVec3 = UBlade3.Data.Vec3;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // One on-ice player of a loaded UBlade3 match: the team-file ObjectDefinition, the slot the
    // shipped GameEngine::LoadTeamFromFile would give it, the original spawn position from the HTM's
    // SetMotionParams pos, and the WBM/WBA names SkaterPreviewData loads.
    public sealed class MatchPlayerSlot
    {
        public const string RoleHomeSkater = "home-skater-human";
        public const string RoleHomeTeammate = "home-skater-ai";
        public const string RoleHomeGoalie = "home-goalie";
        public const string RoleAwaySkater = "away-skater-ai";
        public const string RoleAwayGoalie = "away-goalie";

        public int Side;              // 0 home, 1 away (LoadTeamFromFile's nTeam)
        public int RosterSlot;        // TeamLoadLayout slot: 0, 1 and 2 are the three Dromers on the ice
        public int SimSlot;           // nRunningId: side + 2 * RosterSlot (0,2,4 home; 1,3,5 away)
        public string Role;           // one of the Role* labels above
        public string TeamName;
        public string TeamFileName;
        public string ModelName;
        public string AssemblyName;   // AA_Skate_Std for skaters, AA_Goalidle for the goalie
        public bool RemapPalette;     // LoadTeamFromFile sets m_bForceRemap on every away definition
        public bool IsGoalie;
        public ObjectDefinition Definition;
        public int RatingRow;         // outfield row (0/1) or -1 for a goalie
        public float[] Ratings;       // the selected table (63 field values or 16 goalie values)
        public DataVec3 SpawnPosition; // the HTM SetMotionParams pos, exactly as parsed

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_PLAYER side=").Append(Side == 0 ? "home" : "away");
            text.Append(" slot=").Append(SimSlot.ToString(CultureInfo.InvariantCulture));
            text.Append(" rosterSlot=").Append(RosterSlot.ToString(CultureInfo.InvariantCulture));
            text.Append(" role=").Append(Role);
            text.Append(" team=\"").Append(TeamName).Append('"');
            text.Append(" model=").Append(ModelName);
            text.Append(" assembly=").Append(AssemblyName);
            text.Append(" remapPalette=").Append(RemapPalette ? "True" : "False");
            text.Append(" spawn=(").Append(F(SpawnPosition.x)).Append(',').Append(F(SpawnPosition.y))
                .Append(',').Append(F(SpawnPosition.z)).Append(')');
            text.Append(" ratings=").Append(Ratings == null ? 0 : Ratings.Length);
            text.Append(" ratingRow=").Append(RatingRow.ToString(CultureInfo.InvariantCulture));

            return text.ToString();
        }

        internal static string F(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }

    // One team as GameEngine::LoadTeamFromFile (HYPERX 0x004858b0) would have built it: the three
    // Dromers that go on the ice (file slots 0, 1, 2), the model ids the loader assigns and the
    // ratings tables the PlayerObject constructor reads.
    public sealed class MatchTeamData
    {
        public int Side;
        public TeamFile File;
        public TeamLoadLayout Layout;
        public string TeamFileName;
        public readonly MatchPlayerSlot[] OnIce = new MatchPlayerSlot[TeamLoadLayout.PlayersOnDrome];

        public string DescribeTeam()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_TEAM side=").Append(Side == 0 ? "home" : "away");
            text.Append(" file=").Append(TeamFileName);
            text.Append(" name=\"").Append(File.Name).Append('"');
            text.Append(" environment=").Append(File.Environment);
            text.Append(" dromers=").Append(OnIce.Length);
            text.Append(" modelIds=").Append(OnIce[0].SimSlot).Append(',').Append(OnIce[1].SimSlot)
                .Append(',').Append(OnIce[2].SimSlot);
            text.Append(" warnings=").Append(File.Warnings.Count);

            return text.ToString();
        }
    }

    // The original data root folder for one match. Nothing here is procedural: the drome objects,
    // WBM models, BMP textures and WBA animations all come from the player's own install.
    public sealed class MatchWorldData
    {
        // Chicago "The Machine" is the home side: chico.htm's first Dromer is "Evans" (CHIOMX, the
        // Human plan list). The shipped away team chosen is Seattle "The Fury": seattle.htm is the
        // only team file whose Dromer SetMotionParams pos entries are all authored on the south half
        // of the drome (S._Vanacher 40 -75 0, Kariotis -50 -65 0, goalie McFarland 0 -192.5 -20.8),
        // so the six skaters line up on their own halves using the file's own spawn data and no
        // invented mirroring.
        public const string HomeTeamFile = "chico.htm";
        public const string AwayTeamFile = "seattle.htm";

        public const string DromeFileName = OriginalAssetRoot.DefaultDromeFile;

        // m_nDifficulty 0 = Easy: PlayerObject reads m_apRatings[m_nDifficulty], which is the
        // file's SetRatings1 / SetGoalieRatings1 / SetSpecialGoalieRatings table.
        public const int DifficultyLevel = 0;

        // The chase camera aims at bone 9 (the head), not the body origin. The 16-bone rig's rest
        // translation from the root to the chest is 3.9484 and chest to head 2.0338 model units
        // (docs/SKATER-PREVIEW.md section 2, measured by the N3 probe).
        public const float HeadHeight = 5.9822f;

        public const int TicksPerSecond = 30;

        public const double FixedTickSeconds = 1.0 / 30.0;

        // The animation assemblies the shipped game uses for a skating Dromer and a standing
        // goalie (docs/SKATER-PREVIEW.md section 1: BehParam::SetPersistentAnimation -> AA_Skate_Std
        // segment 0 azkskat2; the goalie idle is AA_Goalidle segment 0 aplread2).
        public const string SkaterAssembly = SkaterPreviewData.ExpectedSkaterAssembly;
        public const string GoalieAssembly = SkaterPreviewData.ExpectedGoalieAssembly;

        public OriginalAssetRoot Root;
        public DromeSceneData Drome;
        public DromeFile DromeSource;
        public DromeDefinition SimDrome;
        public MatchTeamData Home;
        public MatchTeamData Away;

        public readonly List<string> Missing = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public string Digest;

        // Sim-slot order (0..5); entry i is the player bound to Simulation.Players[i].
        public readonly MatchPlayerSlot[] Players = new MatchPlayerSlot[ObjectConstants.PlayerObjectCount];

        public static MatchWorldData Load(OriginalAssetRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            MatchWorldData world = new MatchWorldData();
            world.Root = root;

            // GameEngine::LoadArena (HYPERX 0x00484e80) loads the environment file's object
            // definitions with the verified data reader, then the original WBM meshes.
            world.DromeSource = DromeFile.Load(root.Data, DromeFileName, DataParseOptions.Default);
            world.Drome = DromeSceneLoader.Load(root, DromeShadingMode.Textured, DromeFileName);

            world.Home = LoadTeam(root, 0, HomeTeamFile);
            world.Away = LoadTeam(root, 1, AwayTeamFile);

            for (int i = 0; i < world.Home.OnIce.Length; i++)
            {
                world.Players[world.Home.OnIce[i].SimSlot] = world.Home.OnIce[i];
            }

            for (int i = 0; i < world.Away.OnIce.Length; i++)
            {
                world.Players[world.Away.OnIce[i].SimSlot] = world.Away.OnIce[i];
            }

            for (int slot = 0; slot < world.Players.Length; slot++)
            {
                if (world.Players[slot] == null)
                {
                    throw new OriginalAssetException(
                        "Match slot " + slot + " has no on-ice player definition; the shipped loader fills every Dromer slot.");
                }
            }

            if (world.Drome.MissingTextures.Count != 0)
            {
                foreach (string missing in world.Drome.MissingTextures)
                {
                    world.Missing.Add("drome texture " + missing);
                }
            }

            if (world.Drome.MissingModels.Count != 0)
            {
                foreach (string missing in world.Drome.MissingModels)
                {
                    world.Missing.Add("drome model " + missing);
                }
            }

            world.SimDrome = BuildDromeDefinition(world);
            world.Digest = ComputeDigest(world);

            return world;
        }

        private static MatchTeamData LoadTeam(OriginalAssetRoot root, int side, string fileName)
        {
            List<TeamsCatalogEntry> catalog = TeamFile.LoadCatalog(root.Data);
            TeamsCatalogEntry entry = null;

            foreach (TeamsCatalogEntry candidate in catalog)
            {
                if (string.Equals(candidate.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                throw new OriginalAssetException(
                    "Team file \"" + fileName + "\" is not listed in teams.cat (" + catalog.Count + " entries).");
            }

            TeamFile file = TeamFile.Load(root.Data, entry, DataParseOptions.Default);

            // GameEngine::LoadTeamFromFile's slot bookkeeping: the away side forces the palette
            // remap and the first three Dromers take the running object ids.
            TeamLoadLayout layout = file.BuildLoadLayout(side);

            float[][] outfieldRows = file.BuildOutfieldRows(DifficultyLevel);

            MatchTeamData team = new MatchTeamData();
            team.Side = side;
            team.File = file;
            team.Layout = layout;
            team.TeamFileName = entry.FileName;

            for (int i = 0; i < TeamLoadLayout.PlayersOnDrome; i++)
            {
                ObjectDefinition definition = file.Slots[i];

                if (definition.Kind != ObjectKind.Dromer)
                {
                    throw new OriginalAssetException(
                        "Team \"" + file.Name + "\" slot " + i + " is " + definition.TypeName + ", not a Dromer.");
                }

                bool isGoalie = i == TeamLoadLayout.GoalieSlot;

                MatchPlayerSlot player = new MatchPlayerSlot();
                player.Side = side;
                player.RosterSlot = i;
                player.SimSlot = layout.ModelIds[i];
                player.TeamName = file.Name;
                player.TeamFileName = entry.FileName;
                player.ModelName = definition.Model;
                player.AssemblyName = isGoalie ? GoalieAssembly : SkaterAssembly;
                player.RemapPalette = (player.SimSlot & 1) != 0 || definition.ForceRemap;
                player.IsGoalie = isGoalie;
                player.Definition = definition;
                player.SpawnPosition = definition.Position;
                player.Role = RoleFor(side, i);

                if (isGoalie)
                {
                    // PlayerObject: the home goalie (GOALIE_SLOT_A) at difficulty 0 reads the
                    // SetSpecialGoalieRatings table when the file carries one; every other goalie
                    // reads the difficulty table (SetGoalieRatings1 here).
                    float[] special = definition.GetRatingTableOrEasy((int)RatingLevel.SpecialGoalie);
                    float[] level = definition.GetRatingTableOrEasy(DifficultyLevel);
                    player.RatingRow = -1;
                    player.Ratings = side == 0 && special != null ? special : level;
                }
                else
                {
                    player.RatingRow = TeamLoadLayout.GetRowIndex(i);
                    player.Ratings = outfieldRows[player.RatingRow];
                }

                team.OnIce[i] = player;
            }

            return team;
        }

        private static string RoleFor(int side, int rosterSlot)
        {
            if (rosterSlot == TeamLoadLayout.GoalieSlot)
            {
                return side == 0 ? MatchPlayerSlot.RoleHomeGoalie : MatchPlayerSlot.RoleAwayGoalie;
            }

            if (side == 0)
            {
                return rosterSlot == 0 ? MatchPlayerSlot.RoleHomeSkater : MatchPlayerSlot.RoleHomeTeammate;
            }

            return MatchPlayerSlot.RoleAwaySkater;
        }

        // GameEngine::LoadArena's object definitions plus the drome's own Rok definition, in the plain
        // structs UBlade3.Sim reads. The placement list mirrors the environment objects that carry a
        // model; objects with modfile=null become engine objects with no mesh in the original and are
        // counted in the data report only.
        private static DromeDefinition BuildDromeDefinition(MatchWorldData world)
        {
            DromeDefinition definition = new DromeDefinition();
            definition.Name = world.Drome.DromeFileName;

            foreach (DromeObjectData item in world.Drome.Objects)
            {
                ObjectDefinition source = item.Definition;

                definition.Placements.Add(new DromeObjectPlacement
                {
                    ModelId = item.ModelId,
                    Type = (GameObjectType)source.TypeIndex,
                    Position = new SimVec3(source.Position.x, source.Position.y, source.Position.z),
                    AzimuthDegrees = source.Azimuth,
                    ElevationDegrees = source.Elevation,
                    Speed = source.Speed,
                    Acceleration = source.Accel,
                    Friction = source.Friction,
                    Elasticity = source.Elasticity,
                    ModelName = source.Model,
                });

                if (source.Kind == ObjectKind.Multiplier)
                {
                    definition.Multipliers.Add(new MultiplierPlacement
                    {
                        Slot = item.ModelId,
                        Position = new SimVec3(source.Position.x, source.Position.y, source.Position.z),
                        AzimuthDegrees = source.Azimuth,
                    });
                }
            }

            if (world.DromeSource.Rok != null)
            {
                definition.RokDefinition = ToMotionParams(world.DromeSource.Rok);
            }

            definition.NorthGoal = GoalDefinition.CreateNorth();
            definition.SouthGoal = GoalDefinition.CreateSouth();

            return definition;
        }

        // Cmd_SetMotionParams' ObjectDef fields in the simulation's plain struct.
        public static ObjectMotionParams ToMotionParams(ObjectDefinition definition)
        {
            ObjectMotionParams motion = new ObjectMotionParams();
            motion.ModelName = definition.Model;
            motion.Type = definition.TypeIndex;
            motion.TypeId = definition.TypeIndex;
            motion.MaterialName = definition.Material;
            motion.Position = new SimVec3(definition.Position.x, definition.Position.y, definition.Position.z);
            motion.Acceleration = definition.Accel;
            motion.Speed = definition.Speed;
            motion.AzimuthDegrees = definition.Azimuth;
            motion.ElevationDegrees = definition.Elevation;
            motion.Friction = definition.Friction;
            motion.Elasticity = definition.Elasticity;
            motion.PlanNames = definition.Plans.ToArray();

            return motion;
        }

        // Where the scene puts a player before the first physics tick. A skater uses the HTM
        // SetMotionParams pos exactly. The goalie's HTM pos is behind the goal line (Seattle's is
        // y = -192.5, outside the 187.5 tracking ellipsoid): the plan-VM goalie agenda that walks it
        // into the mouth (CBungeeToward / CMonitorKeys goalie control) is not ported, so the fallback
        // is Game::InitMatchMarkers' goal-mouth marker (radiusY - 3, z = -14) for its own end. This
        // is documented as a fallback in docs/NATIVE-MATCH.md, not exact plan behaviour.
        public static SimVec3 InitialPosition(MatchPlayerSlot player, ArenaMarkerSet markers)
        {
            if (!player.IsGoalie)
            {
                return new SimVec3(player.SpawnPosition.x, player.SpawnPosition.y, player.SpawnPosition.z);
            }

            SimVec3 mouth = player.Side == 0 ? markers.GoalMouthNorth : markers.GoalMouthSouth;

            return new SimVec3(mouth.x, mouth.y, mouth.z);
        }

        // Every original character the match draws: the three on-ice players of each team. Each is
        // loaded through the verified SkaterPreviewData mapping (WBM rig + one WBA assembly), with
        // the away side's palette remap. Six meshes are created but no scene object is made here, so
        // the headless Check and the capture scene load the same data.
        public List<MatchCharacterData> LoadCharacters()
        {
            List<MatchCharacterData> characters = new List<MatchCharacterData>(ObjectConstants.PlayerObjectCount);

            for (int slot = 0; slot < Players.Length; slot++)
            {
                MatchPlayerSlot player = Players[slot];

                SkaterPreviewOptions options = new SkaterPreviewOptions();
                options.SkaterModel = player.ModelName;
                options.SkaterAssembly = player.AssemblyName;
                options.ModelSet = OriginalModelSet.Textured;
                options.LodIndex = 0;
                options.RemapPalette = player.RemapPalette;
                options.IncludeGoalie = false;

                SkaterPreviewData preview = SkaterPreviewData.Load(Root, options);

                MatchCharacterData character = new MatchCharacterData();
                character.Slot = player;
                character.Preview = preview;
                character.Character = preview.Skater;

                foreach (string missing in character.Character.MissingTextures)
                {
                    Missing.Add("skater " + slot + " (" + player.ModelName + ") texture " + missing);
                }

                characters.Add(character);
            }

            return characters;
        }

        public MatchCharacterData FindCharacter(List<MatchCharacterData> characters, int simSlot)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].Slot.SimSlot == simSlot)
                {
                    return characters[i];
                }
            }

            return null;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_DATA root=").Append(Root.DataPath);
            text.Append(" drome=").Append(Drome.DromeFileName);
            text.Append(" objects=").Append(Drome.Drome.Objects.Count);
            text.Append(" modelObjects=").Append(Drome.Objects.Count);
            text.Append(" noModelObjects=").Append(Drome.ObjectsWithoutModel.Count);
            text.Append(" meshes=").Append(Drome.MeshCount);
            text.Append(" triangles=").Append(Drome.BuiltTriangles);
            text.Append(" textures=").Append(Drome.TextureFiles.Count);
            text.Append(" missingTextures=").Append(Drome.MissingTextures.Count);
            text.Append(" placements=").Append(SimDrome.Placements.Count);
            text.Append(" rokDefinition=").Append(SimDrome.RokDefinition == null ? "none" : SimDrome.RokDefinition.ModelName);
            text.Append(" digest=").Append(Digest).AppendLine();
            text.AppendLine(Home.DescribeTeam());
            text.AppendLine(Away.DescribeTeam());

            for (int slot = 0; slot < Players.Length; slot++)
            {
                text.AppendLine(Players[slot].Describe());
            }

            foreach (string warning in Warnings)
            {
                text.Append("UBLADE3_NATIVE_MATCH_WARNING ").AppendLine(warning);
            }

            foreach (string missing in Missing)
            {
                text.Append("UBLADE3_NATIVE_MATCH_MISSING ").AppendLine(missing);
            }

            return text.ToString().TrimEnd();
        }

        private static string ComputeDigest(MatchWorldData world)
        {
            ulong hash = FnvOffset;

            Fnv(ref hash, world.Root.DataPath);
            Fnv(ref hash, world.Drome.Digest);
            Fnv(ref hash, world.Home.TeamFileName);
            Fnv(ref hash, world.Away.TeamFileName);

            for (int slot = 0; slot < world.Players.Length; slot++)
            {
                MatchPlayerSlot player = world.Players[slot];
                Fnv(ref hash, player.SimSlot);
                Fnv(ref hash, player.ModelName);
                Fnv(ref hash, player.AssemblyName);
                Fnv(ref hash, player.RemapPalette ? 1 : 0);
                Fnv(ref hash, player.SpawnPosition.x);
                Fnv(ref hash, player.SpawnPosition.y);
                Fnv(ref hash, player.SpawnPosition.z);
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        internal static void Fnv(ref ulong hash, string text)
        {
            if (text == null)
            {
                Fnv(ref hash, "<null>");
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= (byte)text[i];
                hash *= FnvPrime;
            }

            hash ^= 0xFF;
            hash *= FnvPrime;
        }

        internal static void Fnv(ref ulong hash, int value)
        {
            Fnv(ref hash, (uint)value);
        }

        internal static void Fnv(ref ulong hash, uint value)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FnvPrime;
            }
        }

        internal static void Fnv(ref ulong hash, float value)
        {
            Fnv(ref hash, (uint)BitConverter.SingleToInt32Bits(value));
        }

        internal static void Fnv(ref ulong hash, bool value)
        {
            Fnv(ref hash, value ? 1 : 0);
        }
    }

    // One loaded original character for the match. The SkaterPreviewData instance is kept because it
    // owns the pose digest and the palette the material conversion used.
    public sealed class MatchCharacterData
    {
        public MatchPlayerSlot Slot;
        public SkaterPreviewData Preview;
        public SkaterCharacter Character;

        public int AnimationFrames
        {
            get { return Character == null || Character.Animation == null ? 0 : Character.Animation.DisplayedFrameCount; }
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_SKATER_LOADED slot=").Append(Slot.SimSlot);
            text.Append(" role=").Append(Slot.Role);
            text.Append(" model=").Append(Character.ModelName);
            text.Append(" set=").Append(Character.ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
            text.Append(" assembly=").Append(Character.Animation.AssemblyName);
            text.Append(" wba=").Append(Character.Animation.WbaName);
            text.Append(" frames=").Append(Character.Animation.DisplayedFrameCount);
            text.Append(" bones=").Append(Character.Rig.BoneCount);
            text.Append(" triangles=").Append(Character.Mesh.TriangleCount);
            text.Append(" verts=").Append(Character.Mesh.UnityVertexCount);
            text.Append(" textures=").Append(Character.TexturePaths.Count);
            text.Append(" missingTextures=").Append(Character.MissingTextures.Count);
            text.Append(" previewDigest=").Append(Preview.Digest);

            return text.ToString();
        }
    }
}
