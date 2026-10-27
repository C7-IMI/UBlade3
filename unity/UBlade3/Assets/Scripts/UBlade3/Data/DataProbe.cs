using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UBlade3.Data
{
    // The U3-M1 gate: loads the original data from the player's HyperBlade install and prints a
    // stable, scene-independent report. PortLab calls Describe() by reflection; batch mode calls
    // Check() (for example Unity -batchmode -quit -projectPath <project>
    // -executeMethod UBlade3.Data.DataProbe.Check). No UnityEngine type is used here.
    //
    // Output contract:
    //   UBLADE3_DATA_OK teams=14 dromes=14 digest=<16 hex digits>
    //   UBLADE3_DATA_DETAIL data=<path>
    //   UBLADE3_DATA_COUNTS ...
    //   UBLADE3_DATA_DIGEST teams=... dromes=... catalogs=... configs=... total=...
    //   UBLADE3_DATA_WARNINGS <n> (only when the shipped data produced any)
    public static class DataProbe
    {
        public const int ExpectedTeams = 14;

        public const int ExpectedDromes = 14;

        private static GameData cached;

        private static string cachedReport;

        private static Exception cachedError;

        public static string Digest
        {
            get
            {
                string report = EnsureReport();

                return ExtractValue(report, "UBLADE3_DATA_OK", "digest");
            }
        }

        public static string LastError
        {
            get
            {
                EnsureReport();

                return cachedError == null ? string.Empty : cachedError.Message;
            }
        }

        public static GameData Data
        {
            get
            {
                if (cached == null)
                {
                    cached = GameData.Load();
                }

                return cached;
            }
        }

        // Called by UBlade3.App.PortLab through reflection. Never throws and never needs a scene.
        public static string Describe()
        {
            string report = EnsureReport();

            if (cachedError != null)
            {
                return "UBLADE3_DATA_ERROR " + Flatten(cachedError.Message);
            }

            string first = FirstLine(report);

            if (cached.Warnings.Count > 0)
            {
                return first + " warnings=" + cached.Warnings.Count;
            }

            return first;
        }

        // Batch-mode entry point. Prints the report and throws on a failed load so that
        // -executeMethod makes the editor exit non-zero.
        public static void Check()
        {
            string report = EnsureReport();

            Console.Out.WriteLine(report);

            if (cachedError != null)
            {
                throw new DataFormatException("UBLADE3_DATA_FAILED: " + cachedError.Message);
            }

            if (!report.StartsWith("UBLADE3_DATA_OK", StringComparison.Ordinal))
            {
                throw new DataFormatException("UBLADE3_DATA_FAILED: " + FirstLine(report));
            }
        }

        // Same as Check but returns a process exit code instead of throwing.
        public static int Run()
        {
            try
            {
                Check();

                return 0;
            }
            catch (Exception exception)
            {
                Console.Out.WriteLine("UBLADE3_DATA_FAILED " + Flatten(exception.Message));

                return 1;
            }
        }

        public static string Report()
        {
            return EnsureReport();
        }

        public static void Reset()
        {
            cached = null;
            cachedReport = null;
            cachedError = null;
        }

        private static string EnsureReport()
        {
            if (cachedReport != null)
            {
                return cachedReport;
            }

            try
            {
                cached = GameData.Load();
                cachedReport = BuildReport(cached);
            }
            catch (Exception exception)
            {
                cachedError = exception;
                cachedReport = "UBLADE3_DATA_ERROR " + Flatten(exception.Message);
            }

            return cachedReport;
        }

        private static string BuildReport(GameData data)
        {
            int teamCount = data.Teams.Count;
            int dromeCount = data.Dromes.Count;
            int modelRefs = 0;
            int planRefs = 0;

            CountReferences(data, out modelRefs, out planRefs);

            Fnv1a64 teamsHash = new Fnv1a64();
            Fnv1a64 dromesHash = new Fnv1a64();
            Fnv1a64 catalogsHash = new Fnv1a64();
            Fnv1a64 configsHash = new Fnv1a64();
            Fnv1a64 totalHash = new Fnv1a64();

            // One canonical stream, split into four named categories; every line goes to the total too.
            foreach (TeamFile team in data.Teams)
            {
                AppendTeam(teamsHash, team);
            }

            foreach (DromeFile drome in data.Dromes)
            {
                AppendDrome(dromesHash, drome);
            }

            AppendCatalogs(catalogsHash, data);
            AppendConfigs(configsHash, data);

            // Rebuild the categories into the combined digest.
            AppendToString(totalHash, "GAME");
            AppendCategories(totalHash, data);

            string teamsDigest = teamsHash.Value;
            string dromesDigest = dromesHash.Value;
            string catalogsDigest = catalogsHash.Value;
            string configsDigest = configsHash.Value;
            string totalDigest = totalHash.Value;

            StringBuilder report = new StringBuilder();

            report.AppendLine("UBLADE3_DATA_OK teams=" + teamCount + " dromes=" + dromeCount + " digest=" + totalDigest);
            report.AppendLine("UBLADE3_DATA_DETAIL data=" + data.Root.DataPath);
            report.AppendLine(
                "UBLADE3_DATA_COUNTS teams=" + teamCount
                + " dromes=" + dromeCount
                + " teamObjects=" + data.TeamObjectCount
                + " dromeObjects=" + data.DromeObjectCount
                + " plans=" + data.Plans.Names.Count
                + " models=" + data.ModelList.Names.Count
                + " modelsLibrary=" + data.Root.Models.Count
                + " animates=" + data.AnimateList.Names.Count
                + " sounds=" + data.SoundFiles.Entries.Count
                + " soundsDeclared=" + data.SoundFiles.DeclaredSoundCount
                + " info=" + data.InfoText.Entries.Count
                + " jumbo=" + data.Jumbo.Entries.Count
                + " anims=" + AnimationLists.LineCount + "x" + AnimationLists.NamesPerLine
                + " keyRows=" + data.ActiveKeyConfig.CompleteRows
                + " modelRefs=" + modelRefs
                + " planRefs=" + planRefs);
            report.AppendLine("UBLADE3_DATA_DIGEST teams=" + teamsDigest + " dromes=" + dromesDigest + " catalogs=" + catalogsDigest + " configs=" + configsDigest + " total=" + totalDigest);

            foreach (string warning in data.Warnings)
            {
                report.AppendLine("UBLADE3_DATA_WARNING " + Flatten(warning));
            }

            if (data.Warnings.Count == 0)
            {
                report.AppendLine("UBLADE3_DATA_WARNINGS 0");
            }

            return report.ToString().TrimEnd();
        }

        private static void CountReferences(GameData data, out int modelRefs, out int planRefs)
        {
            modelRefs = 0;
            planRefs = 0;

            foreach (TeamFile team in data.Teams)
            {
                CountReferences(team.Objects, ref modelRefs, ref planRefs);
            }

            foreach (DromeFile drome in data.Dromes)
            {
                CountReferences(drome.Objects, ref modelRefs, ref planRefs);
            }

            CountReferences(data.CommonObjects, ref modelRefs, ref planRefs);
        }

        private static void CountReferences(List<ObjectDefinition> definitions, ref int modelRefs, ref int planRefs)
        {
            foreach (ObjectDefinition definition in definitions)
            {
                if (!string.IsNullOrEmpty(definition.Model) && !DataParse.EqualsIgnoreCase(definition.Model, "null"))
                {
                    modelRefs++;
                }

                planRefs += definition.Plans.Count;
            }
        }

        private static void AppendCategories(Fnv1a64 total, GameData data)
        {
            foreach (TeamFile team in data.Teams)
            {
                AppendTeam(total, team);
            }

            foreach (DromeFile drome in data.Dromes)
            {
                AppendDrome(total, drome);
            }

            AppendCatalogs(total, data);
            AppendConfigs(total, data);
        }

        private static void AppendTeam(Fnv1a64 hash, TeamFile team)
        {
            AppendToString(hash, "TEAM|" + team.FileName + "|" + team.Name + "|" + team.IdLogo + "|" + team.TsLogo + "|" + team.TsText + "|" + team.TsTitle + "|" + team.TsAnim + "|" + team.Environment + "|" + team.DromeExt);

            foreach (ObjectDefinition definition in team.Objects)
            {
                AppendDefinition(hash, definition);
            }
        }

        private static void AppendDrome(Fnv1a64 hash, DromeFile drome)
        {
            AppendToString(hash, "DROME|" + drome.FileName + "|" + drome.Objects.Count);

            foreach (ObjectDefinition definition in drome.Objects)
            {
                AppendDefinition(hash, definition);
            }
        }

        private static void AppendDefinition(Fnv1a64 hash, ObjectDefinition definition)
        {
            StringBuilder line = new StringBuilder();

            line.Append("OBJ|");
            line.Append(definition.TypeIndex).Append('|');
            line.Append((int)definition.Kind).Append('|');
            line.Append(definition.Name).Append('|');
            line.Append(definition.Model).Append('|');
            line.Append(definition.Material).Append('|');
            line.Append(definition.OwnPalette ? 1 : 0).Append('|');
            line.Append(F(definition.Position.x)).Append(',').Append(F(definition.Position.y)).Append(',').Append(F(definition.Position.z)).Append('|');
            line.Append(F(definition.Accel)).Append(',').Append(F(definition.Speed)).Append(',').Append(F(definition.Azimuth)).Append(',').Append(F(definition.Elevation)).Append(',').Append(F(definition.Friction)).Append(',').Append(F(definition.Elasticity)).Append('|');
            line.Append(Join(definition.Plans)).Append('|');
            line.Append((int)definition.RatingsMode).Append('|');

            for (int level = 0; level < definition.Ratings.Length; level++)
            {
                float[] table = definition.Ratings[level];

                if (table == null)
                {
                    line.Append("L").Append(level).Append(":-;");
                    continue;
                }

                line.Append("L").Append(level).Append(':').Append(table.Length).Append(':');

                for (int i = 0; i < table.Length; i++)
                {
                    if (i > 0)
                    {
                        line.Append(',');
                    }

                    line.Append(F(table[i]));
                }

                line.Append(';');
            }

            foreach (CollisionBubble bubble in definition.Collision)
            {
                line.Append("B|");
                line.Append((int)bubble.Kind).Append('|');
                line.Append((int)bubble.Shape).Append('|');
                line.Append(bubble.Slot).Append('|');
                line.Append(F(bubble.Radius)).Append('|');
                line.Append(bubble.HasCentroid ? F(bubble.Centroid.x) + "," + F(bubble.Centroid.y) + "," + F(bubble.Centroid.z) : "-").Append('|');
                line.Append(bubble.HasAttachPoint ? F(bubble.AttachPoint.x) + "," + F(bubble.AttachPoint.y) + "," + F(bubble.AttachPoint.z) : "-").Append('|');
                line.Append(bubble.AttachLinkId).Append('|');
                line.Append(bubble.UseDetail ? 1 : 0).Append('|');
                line.Append(bubble.ChildCount).Append(';');
            }

            foreach (RatingIssue issue in definition.RatingIssues)
            {
                line.Append("R?").Append(issue.Token).Append(';');
            }

            AppendToString(hash, line.ToString());
        }

        private static void AppendCatalogs(Fnv1a64 hash, GameData data)
        {
            foreach (string name in data.Plans.Names)
            {
                AppendToString(hash, "PLAN|" + name);
            }

            foreach (string name in data.ModelList.Names)
            {
                AppendToString(hash, "MODEL|" + name);
            }

            foreach (string name in data.AnimateList.Names)
            {
                AppendToString(hash, "ANIMATE|" + name);
            }

            AppendToString(hash, "SOUNDHEADER|" + data.SoundFiles.HeaderSampleRate + "|" + data.SoundFiles.HeaderName + "|" + data.SoundFiles.DeclaredSoundCount);

            foreach (SoundListEntry entry in data.SoundFiles.Entries)
            {
                AppendToString(hash, "SOUND|" + entry.Index + "|" + entry.Name + "|" + entry.DurationMs + "|" + entry.Group + "|" + entry.LoadMode);
            }

            for (int group = 0; group < SoundFilesList.GroupCount; group++)
            {
                AppendToString(hash, "SOUNDGROUP|" + group + "|" + data.SoundFiles.GroupFirst[group] + "|" + data.SoundFiles.GroupCounts[group] + "|" + data.SoundFiles.GroupSamples[group]);
            }

            foreach (InfoTextEntry entry in data.InfoText.Entries)
            {
                AppendToString(hash, "INFO|" + entry.Index + "|" + entry.Color + "|" + entry.Text);
            }

            foreach (string text in data.Jumbo.Entries)
            {
                AppendToString(hash, "JUMBO|" + text);
            }

            foreach (string name in data.Animations.Hit)
            {
                AppendToString(hash, "HIT|" + name);
            }

            foreach (string name in data.Animations.Check)
            {
                AppendToString(hash, "CHK|" + name);
            }

            foreach (string name in data.Animations.Jump)
            {
                AppendToString(hash, "JMP|" + name);
            }

            foreach (string name in data.Animations.Throw)
            {
                AppendToString(hash, "THR|" + name);
            }
        }

        private static void AppendConfigs(Fnv1a64 hash, GameData data)
        {
            AppendToString(hash, "GAMECFG|" + data.GameConfig.HomeTeam + "|" + data.GameConfig.AwayTeam + "|" + data.GameConfig.Environment);

            for (int round = 0; round < GauntletConfig.RoundCount; round++)
            {
                StringBuilder line = new StringBuilder("GAUNTLET|" + round + "|");

                for (int slot = 0; slot < GauntletConfig.SlotCount; slot++)
                {
                    if (slot > 0)
                    {
                        line.Append(',');
                    }

                    line.Append(data.Gauntlet.Bracket[round, slot]);
                }

                AppendToString(hash, line.ToString());
            }

            BooieConfig booie = data.Booie;

            AppendToString(hash, "BOOIE|" + booie.Difficulty + "|" + booie.Brutality + "|" + booie.SfxVolume + "|" + booie.MusicVolume + "|" + booie.ScreenSize + "|" + booie.PeriodLength + "|" + booie.AttackMode + "|" + booie.TeammateMode + "|" + booie.NetNoVerify + "|" + booie.ModemPlay + "|" + booie.InstallLevel + "|" + booie.DromeTextured + "|" + booie.Potatoe);

            for (int i = 0; i < BooieConfig.KeyBindingCount; i++)
            {
                AppendToString(hash, "KEY|" + BooieConfig.KeyBindingNames[i] + "|" + booie.KeyCodes[i]);
            }

            for (int i = 0; i < BooieConfig.GamepadButtonNames.Length; i++)
            {
                AppendToString(hash, "PAD|" + BooieConfig.GamepadButtonNames[i] + "|" + booie.GamepadButtons[i]);
            }

            List<KeyConfigFile> hashedPresets = new List<KeyConfigFile>();

            foreach (KeyValuePair<string, KeyConfigFile> preset in data.KeyPresets)
            {
                if (hashedPresets.Contains(preset.Value))
                {
                    continue;
                }

                hashedPresets.Add(preset.Value);
                AppendKeyConfig(hash, "PRESET|" + preset.Key, preset.Value);
            }

            AppendKeyConfig(hash, "ACTIVE|" + data.ActiveKeyConfig.FileName, data.ActiveKeyConfig);

            foreach (ObjectDefinition definition in data.CommonObjects)
            {
                AppendDefinition(hash, definition);
            }
        }

        private static void AppendKeyConfig(Fnv1a64 hash, string label, KeyConfigFile config)
        {
            StringBuilder line = new StringBuilder();

            line.Append(label).Append('|');
            line.Append(F(config.ForceIncrement)).Append(',').Append(F(config.MaxAcceleration)).Append(',').Append(F(config.MaxSpeed)).Append(',').Append(F(config.ForceRelaxation)).Append('|');
            line.Append(config.VictimCam).Append('|');
            line.Append(F(config.Bandwidth)).Append(',').Append(F(config.NetTracking)).Append(',').Append(F(config.StopForceFactor)).Append(',').Append(F(config.SpeedBurstFactor)).Append(',').Append(F(config.ViewSteepnessZ)).Append('|');
            line.Append(F(config.BirdsEyeTrackOffset.x)).Append(',').Append(F(config.BirdsEyeTrackOffset.y)).Append(',').Append(F(config.BirdsEyeTrackOffset.z)).Append('|');
            line.Append(F(config.FirstPersonTrackOffset.x)).Append(',').Append(F(config.FirstPersonTrackOffset.y)).Append(',').Append(F(config.FirstPersonTrackOffset.z)).Append('|');
            line.Append(F(config.OverShoulderTrackOffset.x)).Append(',').Append(F(config.OverShoulderTrackOffset.y)).Append(',').Append(F(config.OverShoulderTrackOffset.z)).Append('|');
            line.Append(F(config.OverShoulderRokTrackOffset.x)).Append(',').Append(F(config.OverShoulderRokTrackOffset.y)).Append(',').Append(F(config.OverShoulderRokTrackOffset.z)).Append('|');

            foreach (KeyRow row in config.Rows)
            {
                line.Append(row.Flags).Append(',').Append(row.Key).Append(',').Append(row.Index).Append(';');
            }

            AppendToString(hash, line.ToString());
        }

        private static void AppendToString(Fnv1a64 hash, string value)
        {
            hash.Add(value == null ? string.Empty : value);
            hash.Add("\n");
        }

        private static string Join(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(values[i]);
            }

            return builder.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string FirstLine(string report)
        {
            int newline = report.IndexOf('\n');

            return newline < 0 ? report : report.Substring(0, newline).TrimEnd('\r');
        }

        private static string ExtractValue(string report, string marker, string key)
        {
            int start = report.IndexOf(marker, StringComparison.Ordinal);

            if (start < 0)
            {
                return string.Empty;
            }

            int end = report.IndexOf('\n', start);

            if (end < 0)
            {
                end = report.Length;
            }

            string line = report.Substring(start, end - start);

            foreach (string part in line.Split(' '))
            {
                int equals = part.IndexOf('=');

                if (equals > 0 && part.Substring(0, equals) == key)
                {
                    return part.Substring(equals + 1);
                }
            }

            return string.Empty;
        }

        private static string Flatten(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // FNV-1a 64 over UTF-8-ish bytes; only ASCII and Latin-1 data reaches it.
        private sealed class Fnv1a64
        {
            private const ulong Offset = 14695981039346656037UL;

            private const ulong Prime = 1099511628211UL;

            private ulong hash = Offset;

            public string Value
            {
                get { return hash.ToString("x16", CultureInfo.InvariantCulture); }
            }

            public void Add(string text)
            {
                if (text == null)
                {
                    return;
                }

                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= (byte)(text[i] & 0xff);
                    hash *= Prime;
                }
            }
        }
    }
}
