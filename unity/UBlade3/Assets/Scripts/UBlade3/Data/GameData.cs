using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // Everything the original executable reads out of Data\ at start-up and when a match starts,
    // loaded once into one object. This is the whole original data surface of U3-M1:
    //   TEAMS.CAT + 14 *.HTM        Teams / TeamEntries
    //   *.ENV (14)                  Dromes
    //   BOOIE.INI                   Booie
    //   GAME.CFG, COMMON.CFG        GameConfig / CommonObjects
    //   GAUNTLET.CFG                Gauntlet
    //   TEAMS/PLANS/MODEL/ANIMATE/SNDFILES/INFOBTXT/JUMBO lists
    //   DFTKEYBD/DFTMOUSE/DFTSTICK and PREFS.TXT
    //   HITANIMS/CHKANIMS/JMPANIMS/THRANIMS
    public sealed class GameData
    {
        private GameData()
        {
        }

        public GameDataRoot Root { get; private set; }

        public DataParseOptions Options { get; private set; }

        public BooieConfig Booie { get; private set; }

        public GameConfigFile GameConfig { get; private set; }

        public GauntletConfig Gauntlet { get; private set; }

        public List<TeamsCatalogEntry> TeamEntries { get; private set; }

        public List<TeamFile> Teams { get; private set; }

        public List<DromeFile> Dromes { get; private set; }

        public Dictionary<string, DromeFile> DromesByFileName { get; private set; }

        public PlansCatalog Plans { get; private set; }

        public NameListFile ModelList { get; private set; }

        public NameListFile AnimateList { get; private set; }

        public SoundFilesList SoundFiles { get; private set; }

        public InfoTextList InfoText { get; private set; }

        public JumboTextList Jumbo { get; private set; }

        public AnimationLists Animations { get; private set; }

        public List<ObjectDefinition> CommonObjects { get; private set; }

        public KeyConfigFile ActiveKeyConfig { get; private set; }

        public Dictionary<string, KeyConfigFile> KeyPresets { get; private set; }

        public List<string> Warnings { get; private set; }

        public static GameData Load(GameDataRoot root = null, DataParseOptions options = null)
        {
            GameData data = new GameData();

            data.Root = root ?? GameDataRoot.Load();
            data.Options = options ?? DataParseOptions.Default;
            data.Warnings = new List<string>();

            data.Booie = BooieConfig.Load(data.Root);
            data.GameConfig = GameConfigFile.Load(data.Root);
            data.Gauntlet = GauntletConfig.Load(data.Root);
            data.Plans = PlansCatalog.Load(data.Root);
            data.ModelList = NameListFile.Load(data.Root, NameListFile.ModelListFileName);
            data.AnimateList = NameListFile.Load(data.Root, NameListFile.AnimateListFileName);
            data.SoundFiles = SoundFilesList.Load(data.Root);
            data.InfoText = InfoTextList.Load(data.Root);
            data.Jumbo = JumboTextList.Load(data.Root);
            data.Animations = AnimationLists.Load(data.Root);
            data.CommonObjects = ObjectDefinitionReader.ReadAll(data.Root, "common.cfg", data.Options);
            data.KeyPresets = KeyConfigFile.LoadAllPresets(data.Root);
            data.ActiveKeyConfig = KeyConfigFile.LoadActive(data.Root);

            data.LoadTeams();
            data.LoadDromes();
            data.ValidateCrossReferences();

            return data;
        }

        private void LoadTeams()
        {
            TeamEntries = TeamFile.LoadCatalog(Root);
            Teams = new List<TeamFile>();

            foreach (TeamsCatalogEntry entry in TeamEntries)
            {
                Teams.Add(TeamFile.Load(Root, entry, Options));
            }
        }

        private void LoadDromes()
        {
            Dromes = new List<DromeFile>();
            DromesByFileName = new Dictionary<string, DromeFile>(StringComparer.OrdinalIgnoreCase);

            List<string> files = Root.GetFiles("*.env");

            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string fileName in files)
            {
                DromeFile drome = DromeFile.Load(Root, fileName, Options);

                Dromes.Add(drome);
                DromesByFileName[fileName] = drome;
            }
        }

        // Checks every name the loaders resolve from inside a definition against the catalogues it
        // would look in at run time, and reports anything that would be a missing file or a fatal
        // name lookup in the original. The shipped 1996 data resolves completely.
        private void ValidateCrossReferences()
        {
            foreach (TeamFile team in Teams)
            {
                if (string.IsNullOrEmpty(team.Environment))
                {
                    Warnings.Add("Team " + team.FileName + " has no environment.");
                }
                else if (!DromesByFileName.ContainsKey(team.Environment))
                {
                    Warnings.Add("Team " + team.FileName + " names environment " + team.Environment + " which was not loaded.");
                }

                foreach (ObjectDefinition definition in team.Objects)
                {
                    ValidateDefinitionNames(team.FileName, definition);
                }
            }

            foreach (DromeFile drome in Dromes)
            {
                foreach (ObjectDefinition definition in drome.Objects)
                {
                    ValidateDefinitionNames(drome.FileName, definition);
                }
            }

            foreach (ObjectDefinition definition in CommonObjects)
            {
                ValidateDefinitionNames("common.cfg", definition);
            }

            if (GameConfig != null)
            {
                if (!FindTeamByFileNameOrStem(GameConfig.HomeTeam))
                {
                    Warnings.Add("GAME.CFG HomeTeam " + GameConfig.HomeTeam + " is not in TEAMS.CAT.");
                }

                if (!FindTeamByFileNameOrStem(GameConfig.AwayTeam))
                {
                    Warnings.Add("GAME.CFG AwayTeam " + GameConfig.AwayTeam + " is not in TEAMS.CAT.");
                }

                if (!string.IsNullOrEmpty(GameConfig.Environment))
                {
                    string resolved;

                    if (!Root.TryResolveFile(GameConfig.Environment, out resolved))
                    {
                        Warnings.Add("GAME.CFG Environment " + GameConfig.Environment + " is not in Data.");
                    }
                }
            }
        }

        private void ValidateDefinitionNames(string owner, ObjectDefinition definition)
        {
            if (!string.IsNullOrEmpty(definition.Model)
                && !DataParse.EqualsIgnoreCase(definition.Model, "null")
                && !Root.Models.Has(definition.Model))
            {
                Warnings.Add(owner + ": model \"" + definition.Model + "\" for " + definition.TypeName + " " + definition.Name + " was not found in models.");
            }

            foreach (string plan in definition.Plans)
            {
                if (!Plans.ContainsPlan(plan))
                {
                    Warnings.Add(owner + ": plan \"" + plan + "\" for " + definition.TypeName + " " + definition.Name + " is not in PLANS.CAT.");
                }
            }
        }

        public bool FindTeamByFileNameOrStem(string fileName)
        {
            return GetTeam(fileName) != null;
        }

        public TeamFile GetTeam(string fileNameOrStem)
        {
            if (string.IsNullOrEmpty(fileNameOrStem))
            {
                return null;
            }

            foreach (TeamFile team in Teams)
            {
                if (DataParse.EqualsIgnoreCase(team.FileName, fileNameOrStem))
                {
                    return team;
                }

                int dot = team.FileName.LastIndexOf('.');

                if (dot > 0 && DataParse.EqualsIgnoreCase(team.FileName.Substring(0, dot), fileNameOrStem))
                {
                    return team;
                }

                if (DataParse.EqualsIgnoreCase(team.Name, fileNameOrStem))
                {
                    return team;
                }
            }

            return null;
        }

        public DromeFile GetDrome(string fileName)
        {
            DromeFile drome;

            if (!string.IsNullOrEmpty(fileName) && DromesByFileName.TryGetValue(fileName, out drome))
            {
                return drome;
            }

            return null;
        }

        public int TeamObjectCount
        {
            get
            {
                int total = 0;

                foreach (TeamFile team in Teams)
                {
                    total += team.Objects.Count;
                }

                return total;
            }
        }

        public int DromeObjectCount
        {
            get
            {
                int total = 0;

                foreach (DromeFile drome in Dromes)
                {
                    total += drome.Objects.Count;
                }

                return total;
            }
        }

        public override string ToString()
        {
            return "GameData(" + Teams.Count + " teams, " + Dromes.Count + " dromes, " + Warnings.Count + " warnings)";
        }
    }
}
