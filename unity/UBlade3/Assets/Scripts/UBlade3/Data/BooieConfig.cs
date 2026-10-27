using System;

namespace UBlade3.Data
{
    // BOOIE.INI. The [Options] and [Misc] keys come from src/engine/gameoptions.cpp
    // (HYPERX 0x00479880 constructor, 0x004799a0..0x00479ab0 getters) and
    // src/engine/gameengine.cpp lines 437-450; the [Keys] action names from
    // src/input/keyboarddeviceobj.cpp lines 610-622; the [Gamepad] buttons from
    // src/input/joystickmgr.cpp lines 268-272 and 458; DromeTextured from
    // src/graphics/renderer.cpp lines 45 and 73.
    public sealed class BooieConfig
    {
        public const string FileName = "booie.ini";

        public const string OptionsSection = "Options";
        public const string KeysSection = "Keys";
        public const string MiscSection = "Misc";
        public const string GamepadSection = "Gamepad";

        // KEY_BINDING_COUNT and the ten action names, KeyboardDeviceObj.
        public const int KeyBindingCount = 10;

        public static readonly string[] KeyBindingNames = { "Forward", "Stop", "RotateLeft", "RotateRight", "Attack", "SpeedBurst", "Shoot", "Pass", "QuickShot", "Jump" };

        // g_anKeyBindingDefaults: VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT, 'S', 'D', VK_SPACE, 'C', 'F', 'E'.
        public static readonly int[] DefaultKeyCodes = { 38, 40, 37, 39, 83, 68, 32, 67, 70, 69 };

        // g_apszGamepadButtonKey.
        public static readonly string[] GamepadButtonNames = { "Attack", "Speed", "Shoot", "Pass" };

        // ReadInt default values: ReadDifficulty/ReadBrutality return 1, ReadSfxVolume 7,
        // ReadMusicVolume 4, ReadScreenSize 6, ReadPeriodLength 0x493e0 (300 s), AttackMode and
        // TeammateMode 0, NetNoVerify and ModemPlay 0, InstallLevel 0, DromeTextured 1.
        public const int DefaultDifficulty = 1;
        public const int DefaultBrutality = 1;
        public const int DefaultSfxVolume = 7;
        public const int DefaultMusicVolume = 4;
        public const int DefaultScreenSize = 6;
        public const int DefaultPeriodLength = 0x493e0;
        public const int DefaultAttackMode = 0;
        public const int DefaultTeammateMode = 0;
        public const int DefaultNetNoVerify = 0;
        public const int DefaultModemPlay = 0;
        public const int DefaultInstallLevel = 0;
        public const int DefaultDromeTextured = 1;

        private BooieConfig()
        {
        }

        public bool Present { get; private set; }

        public IniDocument Document { get; private set; }

        public int Difficulty { get; private set; }

        public int Brutality { get; private set; }

        public int SfxVolume { get; private set; }

        public int MusicVolume { get; private set; }

        public int ScreenSize { get; private set; }

        public int PeriodLength { get; private set; }

        public int AttackMode { get; private set; }

        public int TeammateMode { get; private set; }

        public int NetNoVerify { get; private set; }

        public int ModemPlay { get; private set; }

        public int InstallLevel { get; private set; }

        public int DromeTextured { get; private set; }

        public int Potatoe { get; private set; }

        // Ten virtual-key codes, indexed like KeyBindingNames.
        public int[] KeyCodes { get; private set; }

        // Four button indices, indexed like GamepadButtonNames; the key reader subtracts one before use.
        public int[] GamepadButtons { get; private set; }

        public static BooieConfig Load(GameDataRoot root)
        {
            string path;

            if (root.TryResolveFile(FileName, out path))
            {
                return Parse(FileReader.ReadAllBytes(path), path);
            }

            return CreateDefaults();
        }

        public static BooieConfig Parse(byte[] bytes, string path)
        {
            BooieConfig config = new BooieConfig();

            config.Present = true;
            config.Document = IniDocument.Parse(bytes, path);

            IniDocument document = config.Document;

            config.Difficulty = document.GetInt(OptionsSection, "Difficulty", DefaultDifficulty);
            config.Brutality = document.GetInt(OptionsSection, "Brutality", DefaultBrutality);
            config.SfxVolume = document.GetInt(OptionsSection, "SfxVolume", DefaultSfxVolume);
            config.MusicVolume = document.GetInt(OptionsSection, "MusicVolume", DefaultMusicVolume);
            config.ScreenSize = document.GetInt(OptionsSection, "ScreenSize", DefaultScreenSize);
            config.PeriodLength = document.GetInt(OptionsSection, "PeriodLength", DefaultPeriodLength);
            config.AttackMode = document.GetInt(OptionsSection, "AttackMode", DefaultAttackMode);
            config.TeammateMode = document.GetInt(OptionsSection, "TeammateMode", DefaultTeammateMode);
            config.NetNoVerify = document.GetInt(OptionsSection, "NetNoVerify", DefaultNetNoVerify);
            config.ModemPlay = document.GetInt(OptionsSection, "ModemPlay", DefaultModemPlay);

            config.InstallLevel = document.GetInt(MiscSection, "InstallLevel", DefaultInstallLevel);
            config.DromeTextured = document.GetInt(MiscSection, "DromeTextured", DefaultDromeTextured);
            config.Potatoe = document.GetInt(MiscSection, "Potatoe", 0);

            config.KeyCodes = new int[KeyBindingCount];

            for (int i = 0; i < KeyBindingCount; i++)
            {
                config.KeyCodes[i] = document.GetInt(KeysSection, KeyBindingNames[i], DefaultKeyCodes[i]);
            }

            config.GamepadButtons = new int[GamepadButtonNames.Length];

            for (int i = 0; i < GamepadButtonNames.Length; i++)
            {
                // src/input/joystickmgr.cpp: ReadInt("Gamepad", name, i + 1) - 1 is stored.
                config.GamepadButtons[i] = document.GetInt(GamepadSection, GamepadButtonNames[i], i + 1);
            }

            return config;
        }

        public static BooieConfig CreateDefaults()
        {
            BooieConfig config = new BooieConfig();

            config.Present = false;
            config.Document = null;
            config.Difficulty = DefaultDifficulty;
            config.Brutality = DefaultBrutality;
            config.SfxVolume = DefaultSfxVolume;
            config.MusicVolume = DefaultMusicVolume;
            config.ScreenSize = DefaultScreenSize;
            config.PeriodLength = DefaultPeriodLength;
            config.AttackMode = DefaultAttackMode;
            config.TeammateMode = DefaultTeammateMode;
            config.NetNoVerify = DefaultNetNoVerify;
            config.ModemPlay = DefaultModemPlay;
            config.InstallLevel = DefaultInstallLevel;
            config.DromeTextured = DefaultDromeTextured;
            config.Potatoe = 0;

            config.KeyCodes = new int[KeyBindingCount];

            for (int i = 0; i < KeyBindingCount; i++)
            {
                config.KeyCodes[i] = DefaultKeyCodes[i];
            }

            config.GamepadButtons = new int[GamepadButtonNames.Length];

            for (int i = 0; i < GamepadButtonNames.Length; i++)
            {
                config.GamepadButtons[i] = i + 1;
            }

            return config;
        }
    }

    // Small shared file helper the data layer uses when it needs the bytes rather than a text walk.
    internal static class FileReader
    {
        public static byte[] ReadAllBytes(string absolutePath)
        {
            return System.IO.File.ReadAllBytes(absolutePath);
        }
    }
}
