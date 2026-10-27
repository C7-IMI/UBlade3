using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // KeyRow, src/globals.h lines 303-309: one of the forty key-config rows DFTKEYBD/DFTMOUSE/
    // DFTSTICK hold.
    public struct KeyRow
    {
        public byte Flags;
        public ushort Key;
        public ushort Index;

        public KeyRow(byte flags, ushort key, ushort index)
        {
            Flags = flags;
            Key = key;
            Index = index;
        }

        public override string ToString()
        {
            return Flags + " " + Key + " " + Index;
        }
    }

    // The DFT*.CFG preference files. Config::ParseFile (HYPERX 0x0046d8b0,
    // third_party/hyperblade-decomp/src/app/config.cpp lines 136-186) reads them with fscanf, not
    // line by line:
    //
    //   for (row = 0; row < 40; row++)
    //       fscanf(file, "%d %d %d: ", &flags, &key, &index);
    //   fscanf(file, " %f %f %f %f", &forceIncrement, &maxAcceleration, &maxSpeed, &forceRelaxation);
    //   fscanf(file, "  @v02 %d %f %f %f %f %f :", ...);
    //   four times: fscanf(file, " %f %f %f :", offset);
    //
    // The three shipped files hold fewer than forty full rows, so the remaining row reads keep the
    // last converted values (hyperblade-20XX/src/app/config.cpp lines 133-147 documents the same
    // behaviour and initialises the three locals to zero). The values below therefore match the
    // retail disc exactly, including rows 38 and 39 of DFTKEYBD.CFG that were never authored.
    public sealed class KeyConfigFile
    {
        public const int RowCount = 40;

        // src/app/config.cpp: g_flForceIncrement, g_flMaxAcceleration, g_flMaxSpeed,
        // g_flForceRelaxation, g_flBandwidth, g_flNetTracking, g_flStopForceFactor,
        // g_flSpeedBurstFactor, g_vViewSteepness.z, g_bVictimCam.
        public float ForceIncrement;
        public float MaxAcceleration;
        public float MaxSpeed;
        public float ForceRelaxation;
        public int VictimCam;
        public float Bandwidth;
        public float NetTracking;
        public float StopForceFactor;
        public float SpeedBurstFactor;
        public float ViewSteepnessZ;
        public Vec3 BirdsEyeTrackOffset;
        public Vec3 FirstPersonTrackOffset;
        public Vec3 OverShoulderTrackOffset;
        public Vec3 OverShoulderRokTrackOffset;

        private KeyConfigFile()
        {
        }

        public string FileName { get; private set; }

        public string SourcePath { get; private set; }

        public KeyRow[] Rows { get; private set; }

        // How many of the forty rows contained a complete "flags key index:" triple.
        public int CompleteRows { get; private set; }

        // False when the trailing floating-point block did not parse as the original expects.
        public bool TuningComplete { get; private set; }

        public List<string> Warnings { get; private set; }

        // g_aDefaultKeyBindings, src/app/config.cpp lines 305-307: the forty-row fallback table,
        // three shorts per row.
        public static readonly short[] DefaultKeyBindings =
        {
            1, 38, 0, 1, 40, 1, 1, 37, 2, 1, 39, 3, 5, 37, 2, 5, 39, 3, 5, 38, 0, 1, 13, 4,
            1, 32, 7, 9, 37, 2, 9, 39, 3, 9, 38, 0, 1, 0, 58, 1, 0, 58, 1, 0, 58, 1, 0, 58,
            1, 81, 36, 1, 88, 42, 5, 88, 42, 1, 89, 43, 5, 89, 43, 1, 90, 44, 5, 90, 44,
            1, 9, 6, 1, 65, 22, 1, 27, 5, 1, 2, 9, 1, 69, 26, 1, 70, 27, 1, 113, 11, 1, 115, 13,
            1, 118, 16, 1, 119, 17, 1, 13, 4, 1, 32, 7, 1, 67, 24, 1, 79, 32, 5, 13, 4, 1, 66, 23,
            5, 66, 23
        };

        // The three shipped preference presets, and the file Prefs.txt names by default.
        public static readonly string[] PresetFileNames = { "Data/DftKeybd.cfg", "Data/DftMouse.cfg", "Data/DftStick.cfg" };

        public const string PreferencesFile = "prefs.txt";

        public const string DefaultPreferencesPath = "Data/DftKeybd.cfg";

        public static KeyRow[] CreateDefaultRows()
        {
            KeyRow[] rows = new KeyRow[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                rows[i] = new KeyRow(
                    (byte)DefaultKeyBindings[i * 3],
                    (ushort)DefaultKeyBindings[i * 3 + 1],
                    (ushort)DefaultKeyBindings[i * 3 + 2]);
            }

            return rows;
        }

        public static KeyConfigFile LoadPreset(GameDataRoot root, string fileName)
        {
            return Parse(root.ReadBytes(fileName), root.ResolveFile(fileName), fileName);
        }

        public static Dictionary<string, KeyConfigFile> LoadAllPresets(GameDataRoot root)
        {
            Dictionary<string, KeyConfigFile> presets = new Dictionary<string, KeyConfigFile>(StringComparer.OrdinalIgnoreCase);

            foreach (string fileName in PresetFileNames)
            {
                string resolved;

                if (!root.TryResolveFile(fileName, out resolved))
                {
                    continue;
                }

                KeyConfigFile file = Parse(root.ReadBytes(fileName), resolved, fileName);

                presets[fileName] = file;
                presets[System.IO.Path.GetFileName(fileName)] = file;
            }

            return presets;
        }

        // Config::LoadPreferences: read one whitespace-delimited token from Prefs.txt (fscanf "%s");
        // on a missing file fall back to "Data\DftKeybd.cfg".
        public static string LoadPreferencesPath(GameDataRoot root, out bool preferencesPresent)
        {
            string prefsFile;

            preferencesPresent = root.TryResolveFile(PreferencesFile, out prefsFile);

            if (!preferencesPresent)
            {
                return DefaultPreferencesPath;
            }

            byte[] bytes = root.ReadBytes(PreferencesFile);

            int position = 0;

            while (position < bytes.Length && IsSpace((char)bytes[position]))
            {
                position++;
            }

            int start = position;

            while (position < bytes.Length && !IsSpace((char)bytes[position]))
            {
                position++;
            }

            if (position == start)
            {
                return DefaultPreferencesPath;
            }

            char[] chars = new char[position - start];

            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)bytes[start + i];
            }

            return new string(chars);
        }

        public static KeyConfigFile LoadActive(GameDataRoot root)
        {
            bool present;
            string path = LoadPreferencesPath(root, out present);

            string resolved;

            if (!root.TryResolveFile(path, out resolved))
            {
                // The exe directory is the install root in the original; a preferences token is
                // usually "Data\DftStick.cfg" and resolves against Data\ here.
                if (!root.TryResolveFile(System.IO.Path.GetFileName(path), out resolved))
                {
                    throw new DataFormatException("Configuration file \"" + path + "\" is missing or cannot be opened.");
                }
            }

            return Parse(root.ReadBytes(resolved), resolved, path);
        }

        public static KeyConfigFile Parse(byte[] bytes, string sourcePath, string fileName)
        {
            KeyConfigFile config = new KeyConfigFile();
            ScanfStream stream = new ScanfStream(bytes);

            config.SourcePath = sourcePath;
            config.FileName = fileName;
            config.Rows = new KeyRow[RowCount];
            config.Warnings = new List<string>();

            int flags = 0;
            int key = 0;
            int index = 0;

            // fscanf("%d %d %d: ") forty times. A short row keeps whatever the last successful
            // conversion of each field left; C leaves the variable untouched on a matching failure.
            for (int row = 0; row < RowCount; row++)
            {
                int value;
                bool full = false;

                if (stream.TryInt(out value))
                {
                    flags = value;

                    if (stream.TryInt(out value))
                    {
                        key = value;

                        if (stream.TryInt(out value))
                        {
                            index = value;
                            full = stream.TryLiteral(':');
                        }
                    }
                }

                if (full)
                {
                    stream.SkipWhitespace();
                    config.CompleteRows++;
                }

                config.Rows[row] = new KeyRow((byte)flags, (ushort)key, (ushort)index);
            }

            if (config.CompleteRows < RowCount)
            {
                config.Warnings.Add("Only " + config.CompleteRows + " of " + RowCount + " key rows are complete; the rest keep the last values, as the 1996 loader does.");
            }

            // fscanf(" %f %f %f %f"). The four conversions belong to one call, so the first failure
            // stops the rest of this call; the fields already converted keep their values.
            bool read = false;
            float scratch;

            if (stream.TryFloat(out scratch))
            {
                config.ForceIncrement = scratch;
                read = true;

                if (stream.TryFloat(out scratch))
                {
                    config.MaxAcceleration = scratch;

                    if (stream.TryFloat(out scratch))
                    {
                        config.MaxSpeed = scratch;

                        if (stream.TryFloat(out scratch))
                        {
                            config.ForceRelaxation = scratch;
                        }
                        else
                        {
                            read = false;
                        }
                    }
                    else
                    {
                        read = false;
                    }
                }
                else
                {
                    read = false;
                }
            }

            if (!read)
            {
                config.Warnings.Add("The four steering floats did not all convert.");
            }

            // fscanf("  @v02 %d %f %f %f %f %f :"). One call: the first failure ends it.
            bool cameraRead = false;

            // The fields convert one by one; a later failure of the same call leaves the earlier
            // conversions written, exactly as the pointers handed to fscanf would.
            if (stream.TryToken("@v02"))
            {
                int convertedVictim;

                if (stream.TryInt(out convertedVictim))
                {
                    config.VictimCam = convertedVictim;
                    cameraRead = true;

                    if (stream.TryFloat(out scratch))
                    {
                        config.Bandwidth = scratch;

                        if (stream.TryFloat(out scratch))
                        {
                            config.NetTracking = scratch;

                            if (stream.TryFloat(out scratch))
                            {
                                config.StopForceFactor = scratch;

                                if (stream.TryFloat(out scratch))
                                {
                                    config.SpeedBurstFactor = scratch;

                                    if (stream.TryFloat(out scratch))
                                    {
                                        config.ViewSteepnessZ = scratch;
                                        cameraRead = stream.TryLiteral(':');
                                    }
                                    else
                                    {
                                        cameraRead = false;
                                    }
                                }
                                else
                                {
                                    cameraRead = false;
                                }
                            }
                            else
                            {
                                cameraRead = false;
                            }
                        }
                        else
                        {
                            cameraRead = false;
                        }
                    }
                    else
                    {
                        cameraRead = false;
                    }
                }
            }

            if (!cameraRead)
            {
                config.Warnings.Add("The @v02 camera block did not convert completely; the fields before the failure were kept.");
            }

            // Four separate fscanf(" %f %f %f :") calls, each attempted whatever the last did, and
            // each keeping the axes that did convert when a later one fails.
            bool bird = ReadTrack(stream, ref config.BirdsEyeTrackOffset);
            bool first = ReadTrack(stream, ref config.FirstPersonTrackOffset);
            bool shoulder = ReadTrack(stream, ref config.OverShoulderTrackOffset);
            bool shoulderRok = ReadTrack(stream, ref config.OverShoulderRokTrackOffset);

            config.TuningComplete = read && cameraRead && bird && first && shoulder && shoulderRok;

            if (!config.TuningComplete)
            {
                config.Warnings.Add("The trailing steering/camera block is short; the values that did convert are kept, as the 1996 loader keeps them.");
            }

            return config;
        }

        private static bool ReadTrack(ScanfStream stream, ref Vec3 target)
        {
            Vec3 value = target;
            float axis;
            bool ok = stream.TryFloat(out axis);

            if (ok)
            {
                value.x = axis;
                ok = stream.TryFloat(out axis);
            }

            if (ok)
            {
                value.y = axis;
                ok = stream.TryFloat(out axis);
            }

            if (ok)
            {
                value.z = axis;
                ok = stream.TryLiteral(':');
            }

            target = value;

            return ok;
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f';
        }

        // C stdio's whitespace-skipping conversions, restricted to the specifiers the loader uses.
        private sealed class ScanfStream
        {
            private readonly byte[] data;
            private int position;

            public ScanfStream(byte[] data)
            {
                this.data = data ?? new byte[0];
            }

            public void SkipWhitespace()
            {
                while (position < data.Length && IsSpace((char)data[position]))
                {
                    position++;
                }
            }

            public bool TryInt(out int value)
            {
                value = 0;
                SkipWhitespace();

                int start = position;
                int i = position;
                bool negative = false;

                if (i < data.Length && (data[i] == '+' || data[i] == '-'))
                {
                    negative = data[i] == '-';
                    i++;
                }

                long result = 0;
                int digits = 0;

                while (i < data.Length && data[i] >= '0' && data[i] <= '9')
                {
                    if (result < 214748364L)
                    {
                        result = result * 10 + (data[i] - '0');
                    }

                    i++;
                    digits++;
                }

                if (digits == 0)
                {
                    position = start;
                    return false;
                }

                position = i;
                value = (int)(negative ? -result : result);

                return true;
            }

            public bool TryFloat(out float value)
            {
                value = 0.0f;
                SkipWhitespace();

                int start = position;
                int i = position;

                if (i < data.Length && (data[i] == '+' || data[i] == '-'))
                {
                    i++;
                }

                int digits = 0;

                while (i < data.Length && data[i] >= '0' && data[i] <= '9')
                {
                    i++;
                    digits++;
                }

                if (i < data.Length && data[i] == '.')
                {
                    i++;

                    while (i < data.Length && data[i] >= '0' && data[i] <= '9')
                    {
                        i++;
                        digits++;
                    }
                }

                if (digits == 0)
                {
                    position = start;
                    return false;
                }

                int numberEnd = i;

                if (i < data.Length && (data[i] == 'e' || data[i] == 'E'))
                {
                    int exponent = i + 1;

                    if (exponent < data.Length && (data[exponent] == '+' || data[exponent] == '-'))
                    {
                        exponent++;
                    }

                    int exponentDigits = 0;

                    while (exponent < data.Length && data[exponent] >= '0' && data[exponent] <= '9')
                    {
                        exponent++;
                        exponentDigits++;
                    }

                    if (exponentDigits > 0)
                    {
                        numberEnd = exponent;
                    }
                }

                char[] chars = new char[numberEnd - start];

                for (int c = 0; c < chars.Length; c++)
                {
                    chars[c] = (char)data[start + c];
                }

                double parsed;

                if (!double.TryParse(new string(chars), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    position = start;
                    return false;
                }

                value = (float)parsed;
                position = numberEnd;

                return true;
            }

            // A literal in the format string: a leading space in the format skips whitespace first,
            // then the characters must match exactly.
            public bool TryToken(string literal)
            {
                SkipWhitespace();

                if (position + literal.Length > data.Length)
                {
                    return false;
                }

                for (int i = 0; i < literal.Length; i++)
                {
                    if ((char)data[position + i] != literal[i])
                    {
                        return false;
                    }
                }

                position += literal.Length;

                return true;
            }

            public bool TryLiteral(char literal)
            {
                SkipWhitespace();

                if (position >= data.Length || (char)data[position] != literal)
                {
                    return false;
                }

                position++;

                return true;
            }

            public bool TryVec3WithColon(out Vec3 value)
            {
                value = new Vec3();
                float x;
                float y;
                float z;

                if (!TryFloat(out x) || !TryFloat(out y) || !TryFloat(out z) || !TryLiteral(':'))
                {
                    return false;
                }

                value = new Vec3(x, y, z);

                return true;
            }
        }
    }
}
