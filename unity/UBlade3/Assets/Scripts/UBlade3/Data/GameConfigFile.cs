using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // GAME.CFG: three keyword lines. GameEngine::LoadGameConfig (HYPERX 0x00481e50,
    // third_party/hyperblade-decomp/src/engine/gameengine.cpp lines 1227-1310) reads only
    // Environment, HomeTeam and AwayTeam; every other line is ignored. The shipped file is:
    //   HomeTeam = LOSAN.HTM / AwayTeam = SEATTLE.HTM / Environment = HOUDRO.ENV
    public sealed class GameConfigFile
    {
        public const string FileName = "game.cfg";

        public static readonly string[] Keywords = { "Environment", "HomeTeam", "AwayTeam" };

        private GameConfigFile()
        {
        }

        public string Environment { get; private set; }

        public string HomeTeam { get; private set; }

        public string AwayTeam { get; private set; }

        public string SourcePath { get; private set; }

        public static GameConfigFile Load(GameDataRoot root)
        {
            DataTextFile file = root.OpenText(FileName);

            return Parse(file, file.Path);
        }

        public static GameConfigFile Parse(DataTextFile file, string sourcePath)
        {
            GameConfigFile config = new GameConfigFile();

            config.SourcePath = sourcePath;
            TextLine line;

            while (file.TryReadLine(out line))
            {
                string text = line.Text;
                int position = 0;
                string token;

                if (!DataParse.ParseToken(text, ref position, " \t", DataParse.TokenMax, out token))
                {
                    continue;
                }

                int keyword = DataParse.FindKeyword(token, Keywords);

                if (keyword < 0)
                {
                    continue;
                }

                position = DataParse.SkipPastChar(text, position, '=');

                string value;

                if (position < 0 || !DataParse.ParseToken(text, ref position, " \t\n\r", DataParse.TokenMax, out value))
                {
                    throw new DataFormatException("Badly formed Game Config file " + Keywords[keyword] + " declaration.", sourcePath, line.LineNumber);
                }

                switch (keyword)
                {
                    case 0:
                        config.Environment = value;
                        break;
                    case 1:
                        config.HomeTeam = value;
                        break;
                    case 2:
                        config.AwayTeam = value;
                        break;
                }
            }

            return config;
        }

        public override string ToString()
        {
            return "GameConfigFile(home=" + HomeTeam + ", away=" + AwayTeam + ", env=" + Environment + ")";
        }
    }
}
