using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // The original HUD art the match HUD reuses, read from the player's own Data\assets8 folder:
    //   frper    - InfoBar::InfoBar's period/clock frame (infobar.cpp:982 LoadByName("frper"));
    //   frdamgA  - team A frame (infobar.cpp:1003 LoadByName("frdamgA"));
    //   frdamgB  - team B frame (infobar.cpp:1010 LoadByName("frdamgB"));
    //   hud<id>  - the team logo strip (chico.htm's idlogo=hudChi, seattle.htm's hudsea), loaded
    //              through HUD::SetHomeLogo/SetAwayLogo (hud.cpp:1125/1134 SetFile(name, 1)).
    // The bitmaps are MDM1-wrapped 8-bit DIBs; BmpReader handles that shape. The shipped colour key
    // is the corner pixel (CDDBitMap::CreateFromHBitmap / BringUp::CornerColourKey), applied here as
    // alpha zero. The HUD text itself was Windows GDI "MS Sans Serif" (ddraw.cpp:2532;
    // infobar.cpp:970), not shipped art, so a clearly labelled interim 3x5 bitmap font supplies the
    // numbers, clock, team names and labels.
    public sealed class MatchHudData
    {
        public BmpTexture PeriodFrame;
        public BmpTexture TeamAFrame;
        public BmpTexture TeamBFrame;
        public BmpTexture HomeLogo;
        public BmpTexture AwayLogo;

        public string PeriodFramePath;
        public string TeamAFramePath;
        public string TeamBFramePath;
        public string HomeLogoPath;
        public string AwayLogoPath;

        public readonly List<string> Missing = new List<string>();

        public static MatchHudData Load(MatchWorldData world)
        {
            MatchHudData data = new MatchHudData();

            data.PeriodFrame = LoadArt(world, "frper", out data.PeriodFramePath, data.Missing);
            data.TeamAFrame = LoadArt(world, "frdamgA", out data.TeamAFramePath, data.Missing);
            data.TeamBFrame = LoadArt(world, "frdamgB", out data.TeamBFramePath, data.Missing);

            string homeLogoName = world.Home.File.IdLogo;
            string awayLogoName = world.Away.File.IdLogo;

            data.HomeLogo = LoadArt(world, homeLogoName, out data.HomeLogoPath, data.Missing);
            data.AwayLogo = LoadArt(world, awayLogoName, out data.AwayLogoPath, data.Missing);

            return data;
        }

        private static BmpTexture LoadArt(MatchWorldData world, string name, out string path, List<string> missing)
        {
            path = null;

            if (string.IsNullOrEmpty(name))
            {
                missing.Add("<null>");
                return null;
            }

            string relative = "assets8/" + name + ".bmp";
            path = world.Root.ResolveDataFile(relative);

            if (path == null)
            {
                missing.Add(relative);
                return null;
            }

            try
            {
                return BmpReader.LoadFile(path);
            }
            catch (Exception exception)
            {
                missing.Add(relative + " (" + exception.GetType().Name + ": " + exception.Message + ")");
                return null;
            }
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_GAMEPLAY_HUD_ART");
            Append(text, "frper", PeriodFrame, PeriodFramePath);
            Append(text, "frdamgA", TeamAFrame, TeamAFramePath);
            Append(text, "frdamgB", TeamBFrame, TeamBFramePath);
            Append(text, "homeLogo", HomeLogo, HomeLogoPath);
            Append(text, "awayLogo", AwayLogo, AwayLogoPath);
            text.Append(" missing=").Append(Missing.Count);
            text.Append(" textFont=interim3x5 originalTextFont=\"MS Sans Serif\"(not shipped)");

            return text.ToString();
        }

        private static void Append(StringBuilder text, string role, BmpTexture bitmap, string path)
        {
            text.Append(' ').Append(role).Append('=');

            if (bitmap == null)
            {
                text.Append("missing");
                return;
            }

            text.Append(bitmap.Width).Append('x').Append(bitmap.Height);

            if (!string.IsNullOrEmpty(path))
            {
                text.Append("(").Append(System.IO.Path.GetFileName(path)).Append(')');
            }
        }
    }

    // The match HUD. Two front ends share one layout: the interactive game draws it in OnGUI (play
    // mode), and the deterministic capture composites exactly the same layout into the rendered
    // framebuffer pixels on the CPU before writing the PNG. That keeps the captured evidence honest
    // in an Editor batch run, where OnGUI/post-render callbacks are not part of a camera.Render()
    // pass, while the in-game HUD still uses the original art textures.
    [DisallowMultipleComponent]
    public sealed class MatchHud : MonoBehaviour
    {
        // The deployment is 1280x720; the original InfoBar laid the HUD out on the 640x480 back
        // buffer, so the original frames/logos are drawn at 2x their shipped pixel size.
        public const float FrameScale = 2f;

        public const int FontCellWidth = 4;

        public const int FontGlyphWidth = 3;

        public const int FontGlyphHeight = 5;

        public MatchScene Scene { get; private set; }

        public MatchGameplay Gameplay { get; private set; }

        public MatchHudData Data { get; private set; }

        public bool Built { get; private set; }

        public bool Visible = true;

        public int FramesDrawn { get; private set; }

        public string LastModelText { get; private set; }

        // Optional N6 status line (possession/shot-power/controls). Empty by default so the N5 HUD
        // capture frames keep their exact layout and bytes.
        public string StatusLine = string.Empty;

        public bool GuiDrawn { get; private set; }

        private sealed class HudArt
        {
            public string Role;
            public int Width;
            public int Height;
            // RGBA, four bytes per pixel, top row first, with the shipped corner colour keyed to
            // alpha 0.
            public byte[] Rgba;
            public Texture2D Texture;
        }

        private struct HudSprite
        {
            public HudArt Art;
            public Rect Dest;
        }

        private struct HudText
        {
            public string Text;
            public Rect Dest;
            public float Scale;
            public Color32 Colour;
        }

        private readonly List<HudArt> arts = new List<HudArt>();

        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        private readonly Dictionary<char, Texture2D> glyphTextures = new Dictionary<char, Texture2D>();

        private static readonly string[] GlyphRows =
        {
            "111,101,101,101,111", "010,110,010,010,111", "111,001,111,100,111",
            "111,001,111,001,111", "101,101,111,001,001", "111,100,111,001,111",
            "111,100,111,101,111", "111,001,001,010,010", "111,101,111,101,111",
            "111,101,111,001,111", "010,101,111,101,101", "110,101,110,101,110",
            "011,100,100,100,011", "110,101,101,101,110", "111,100,110,100,111",
            "111,100,110,100,100", "011,100,101,101,011", "101,101,111,101,101",
            "111,010,010,010,111", "001,001,001,101,010", "101,101,110,101,101",
            "100,100,100,100,111", "101,111,111,101,101", "101,111,111,111,101",
            "010,101,101,101,010", "110,101,110,100,100", "010,101,101,110,011",
            "110,101,110,101,101", "011,100,010,001,110", "111,010,010,010,010",
            "101,101,101,101,111", "101,101,101,101,010", "101,101,111,111,101",
            "101,101,010,101,101", "101,101,010,010,010", "111,001,010,100,111",
            "000,010,000,010,000", "000,000,111,000,000", "000,000,000,000,010",
            "000,000,000,000,000", "001,001,010,100,100", "010,010,010,000,010",
            "001,010,010,010,001", "100,010,010,010,100", "101,101,010,101,101",
        };

        private static readonly string GlyphCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:-. /!()x";

        public void Build(MatchScene scene, MatchGameplay gameplay, MatchHudData data)
        {
            if (Built)
            {
                return;
            }

            Scene = scene;
            Gameplay = gameplay;
            Data = data;

            if (data == null || data.Missing.Count != 0)
            {
                throw new OriginalAssetException(
                    "The original HUD art is incomplete: "
                    + (data == null ? "no data" : string.Join(", ", data.Missing.ToArray())));
            }

            arts.Add(CreateArt("frper", data.PeriodFrame));
            arts.Add(CreateArt("frdamgA", data.TeamAFrame));
            arts.Add(CreateArt("frdamgB", data.TeamBFrame));
            arts.Add(CreateArt("homeLogo", data.HomeLogo));
            arts.Add(CreateArt("awayLogo", data.AwayLogo));

            BuildGlyphTextures();

            Built = true;

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_HUD_BUILT art=" + data.Describe()
                      + " frameScale=" + FrameScale.ToString("0.##", CultureInfo.InvariantCulture)
                      + " font=interim3x5 glyphs=" + glyphTextures.Count
                      + " draw=gui-interactive+cpu-capture-composite");
        }

        private HudArt CreateArt(string role, BmpTexture bitmap)
        {
            if (bitmap == null)
            {
                throw new OriginalAssetException("HUD art \"" + role + "\" did not load.");
            }

            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] source = bitmap.Rgba;
            byte[] rgba = new byte[width * height * 4];

            // CDDBitMap::CreateFromHBitmap's colour key is the corner pixel's colour.
            byte keyR = source[0];
            byte keyG = source[1];
            byte keyB = source[2];
            int keyed = 0;

            for (int pixel = 0; pixel < width * height; pixel++)
            {
                byte red = source[(pixel * 4) + 0];
                byte green = source[(pixel * 4) + 1];
                byte blue = source[(pixel * 4) + 2];
                bool key = red == keyR && green == keyG && blue == keyB;

                rgba[(pixel * 4) + 0] = red;
                rgba[(pixel * 4) + 1] = green;
                rgba[(pixel * 4) + 2] = blue;
                rgba[(pixel * 4) + 3] = key ? (byte)0 : (byte)255;

                if (key)
                {
                    keyed++;
                }
            }

            // GUI texture: the RGBA rows are top-down, Unity's upload order is bottom-up.
            byte[] bottomUp = new byte[rgba.Length];

            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(rgba, row * width * 4, bottomUp, ((height - 1) - row) * width * 4, width * 4);
            }

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "UBlade3 HUD " + role;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadRawTextureData(bottomUp);
            texture.Apply(false, true);
            owned.Add(texture);

            Debug.Log("UBLADE3_NATIVE_GAMEPLAY_HUD_SPRITE role=" + role
                      + " size=" + width + "x" + height
                      + " colourKey=" + keyR + "," + keyG + "," + keyB
                      + " keyedPixels=" + keyed
                      + " wrapped=" + (bitmap.IsMdm1Wrapped ? "mdm1" : "dib"));

            HudArt art = new HudArt();
            art.Role = role;
            art.Width = width;
            art.Height = height;
            art.Rgba = rgba;
            art.Texture = texture;

            return art;
        }

        private void BuildGlyphTextures()
        {
            for (int cell = 0; cell < GlyphCharacters.Length && cell < GlyphRows.Length; cell++)
            {
                char character = GlyphCharacters[cell];
                string[] rows = GlyphRows[cell].Split(',');
                Texture2D texture = new Texture2D(FontGlyphWidth, FontGlyphHeight, TextureFormat.RGBA32, false);
                texture.name = "UBlade3 HUD glyph " + character;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Color32[] pixels = new Color32[FontGlyphWidth * FontGlyphHeight];

                for (int row = 0; row < FontGlyphHeight; row++)
                {
                    string bits = rows[row];

                    for (int column = 0; column < FontGlyphWidth && column < bits.Length; column++)
                    {
                        bool on = bits[column] == '1';
                        int y = (FontGlyphHeight - 1) - row;
                        pixels[(y * FontGlyphWidth) + column] = on
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(0, 0, 0, 0);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                owned.Add(texture);
                glyphTextures[character] = texture;
            }
        }

        // --- shared layout -----------------------------------------------------------------

        private void Layout(int width, int height, MatchHudModel model, List<HudSprite> sprites, List<HudText> texts)
        {
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 yellow = new Color32(255, 255, 0, 255);
            Color32 grey = new Color32(190, 190, 190, 255);

            // Period frame centred at the top, clock inside it (InfoBar::LayoutSurfaces: frame
            // centred at y = 2, clock at frame + 25,9 - scaled 2x here).
            HudArt period = arts[0];
            float periodWidth = period.Width * FrameScale;
            float periodHeight = period.Height * FrameScale;
            float periodX = (width - periodWidth) * 0.5f;
            float periodY = 4f;

            AddSprite(sprites, period, periodX, periodY, periodWidth, periodHeight);

            float clockScale = 4f;
            float clockWidth = TextWidth(model.ClockText, clockScale);
            AddText(texts, model.ClockText, periodX + ((periodWidth - clockWidth) * 0.5f), periodY + 16f,
                clockScale, white);

            string periodText = "P" + model.Period.ToString(CultureInfo.InvariantCulture);
            AddText(texts, periodText, periodX + ((periodWidth - TextWidth(periodText, 2f)) * 0.5f),
                periodY + 62f, 2f, white);

            // Team frames at the bottom left (team A above team B, logos inside the frames at x = 2,
            // scores at frame width - 0x20, scaled 2x here).
            HudArt teamA = arts[1];
            HudArt teamB = arts[2];
            HudArt homeLogo = arts[3];
            HudArt awayLogo = arts[4];

            float frameWidth = teamA.Width * FrameScale;
            float frameHeight = teamA.Height * FrameScale;
            float homeY = height - (frameHeight * 2f) - 6f;
            float awayY = height - frameHeight - 10f;

            AddSprite(sprites, teamA, 0f, homeY, frameWidth, frameHeight);
            AddSprite(sprites, teamB, 0f, awayY, frameWidth, frameHeight);

            AddSprite(sprites, homeLogo, 4f, homeY + 44f, homeLogo.Width * FrameScale, homeLogo.Height * FrameScale);
            AddSprite(sprites, awayLogo, 4f, awayY + 8f, awayLogo.Width * FrameScale, awayLogo.Height * FrameScale);

            AddText(texts, model.HomeScoreText, frameWidth - 70f, homeY + 16f, 5f, white);
            AddText(texts, model.AwayScoreText, frameWidth - 70f, awayY + 50f, 5f, white);
            AddText(texts, model.HomeTeam, 156f, homeY + 30f, 2f, white);
            AddText(texts, model.AwayTeam, 156f, awayY + 66f, 2f, white);
            AddText(texts, model.RokText, frameWidth - 70f, homeY - 4f, 2f, yellow);

            // Clearly labelled interim line: the original text face was Windows GDI "MS Sans Serif",
            // which is not shipped art, and the AI is the labelled plan-VM fallback.
            float noteWidth = TextWidth(model.StateLine, 1f);
            AddText(texts, model.StateLine, width - noteWidth - 8f, height - 10f, 1f, grey);

            if (!string.IsNullOrEmpty(StatusLine))
            {
                float statusWidth = TextWidth(StatusLine, 2f);
                AddText(texts, StatusLine, (width - statusWidth) * 0.5f, height - 42f, 2f, yellow);
            }
        }

        private static void AddSprite(List<HudSprite> sprites, HudArt art, float x, float y, float width, float height)
        {
            HudSprite sprite = new HudSprite();
            sprite.Art = art;
            sprite.Dest = new Rect(x, y, width, height);
            sprites.Add(sprite);
        }

        private static void AddText(List<HudText> texts, string text, float x, float y, float scale, Color32 colour)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            HudText item = new HudText();
            item.Text = text;
            item.Dest = new Rect(x, y, TextWidth(text, scale), FontGlyphHeight * scale);
            item.Scale = scale;
            item.Colour = colour;
            texts.Add(item);
        }

        private static float TextWidth(string text, float scale)
        {
            return string.IsNullOrEmpty(text) ? 0f : text.Length * FontCellWidth * scale;
        }

        // --- interactive GUI rendering -------------------------------------------------------

        private void OnGUI()
        {
            if (!Built || !Visible || Scene == null)
            {
                return;
            }

            MatchHudModel model = Gameplay != null ? Gameplay.HudModel : null;

            if (model == null)
            {
                model = MatchHudModel.From(Scene.World, Scene.Sim.Sim);
            }

            List<HudSprite> sprites = new List<HudSprite>();
            List<HudText> texts = new List<HudText>();
            Layout(Screen.width, Screen.height, model, sprites, texts);

            Color saved = GUI.color;

            for (int i = 0; i < sprites.Count; i++)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(sprites[i].Dest, sprites[i].Art.Texture);
            }

            for (int i = 0; i < texts.Count; i++)
            {
                DrawGuiText(texts[i]);
            }

            GUI.color = saved;
            LastModelText = model.Describe();
            GuiDrawn = true;
            FramesDrawn++;
        }

        private void DrawGuiText(HudText text)
        {
            float cellWidth = FontCellWidth * text.Scale;
            float glyphWidth = FontGlyphWidth * text.Scale;
            float glyphHeight = FontGlyphHeight * text.Scale;
            GUI.color = text.Colour;

            for (int i = 0; i < text.Text.Length; i++)
            {
                char character = char.ToUpperInvariant(text.Text[i]);

                if (character == ' ')
                {
                    continue;
                }

                Texture2D glyph;

                if (!glyphTextures.TryGetValue(character, out glyph))
                {
                    continue;
                }

                GUI.DrawTexture(new Rect(text.Dest.x + (i * cellWidth), text.Dest.y, glyphWidth, glyphHeight), glyph);
            }
        }

        // --- capture compositing -------------------------------------------------------------

        // Composites the HUD into a camera framebuffer read back as Color32 pixels in Unity's
        // bottom-up row order. Sprites are alpha-blended with the shipped corner colour key, glyphs
        // are drawn from the interim font; the same layout as the interactive HUD is used.
        public void Composite(Color32[] pixels, int width, int height, MatchHudModel model)
        {
            if (!Built || model == null || pixels == null)
            {
                return;
            }

            List<HudSprite> sprites = new List<HudSprite>();
            List<HudText> texts = new List<HudText>();
            Layout(width, height, model, sprites, texts);

            for (int i = 0; i < sprites.Count; i++)
            {
                BlitArt(pixels, width, height, sprites[i].Art, sprites[i].Dest);
            }

            for (int i = 0; i < texts.Count; i++)
            {
                PlotText(pixels, width, height, texts[i]);
            }

            LastModelText = model.Describe();
            FramesDrawn++;
        }

        private static void BlitArt(Color32[] pixels, int width, int height, HudArt art, Rect dest)
        {
            int destX = Mathf.RoundToInt(dest.x);
            int destY = Mathf.RoundToInt(dest.y);
            int destWidth = Mathf.RoundToInt(dest.width);
            int destHeight = Mathf.RoundToInt(dest.height);

            for (int y = 0; y < destHeight; y++)
            {
                int targetY = destY + y;

                if (targetY < 0 || targetY >= height)
                {
                    continue;
                }

                int sourceY = (y * art.Height) / destHeight;

                for (int x = 0; x < destWidth; x++)
                {
                    int targetX = destX + x;

                    if (targetX < 0 || targetX >= width)
                    {
                        continue;
                    }

                    int sourceX = (x * art.Width) / destWidth;
                    int at = (((sourceY * art.Width) + sourceX) * 4);
                    byte alpha = art.Rgba[at + 3];

                    if (alpha == 0)
                    {
                        continue;
                    }

                    int target = ((height - 1 - targetY) * width) + targetX;
                    Color32 source = new Color32(art.Rgba[at], art.Rgba[at + 1], art.Rgba[at + 2], 255);
                    pixels[target] = Blend(pixels[target], source);
                }
            }
        }

        private static void PlotText(Color32[] pixels, int width, int height, HudText text)
        {
            float cellWidth = FontCellWidth * text.Scale;

            for (int i = 0; i < text.Text.Length; i++)
            {
                char character = char.ToUpperInvariant(text.Text[i]);

                if (character == ' ')
                {
                    continue;
                }

                int cell = GlyphCharacters.IndexOf(character);

                if (cell < 0)
                {
                    continue;
                }

                string[] rows = GlyphRows[cell].Split(',');
                int baseX = Mathf.RoundToInt(text.Dest.x + (i * cellWidth));
                int baseY = Mathf.RoundToInt(text.Dest.y);

                for (int row = 0; row < FontGlyphHeight; row++)
                {
                    string bits = rows[row];

                    for (int column = 0; column < FontGlyphWidth && column < bits.Length; column++)
                    {
                        if (bits[column] != '1')
                        {
                            continue;
                        }

                        int pixelX = baseX + Mathf.RoundToInt(column * text.Scale);
                        int pixelY = baseY + Mathf.RoundToInt(row * text.Scale);
                        int pixelWidth = Mathf.Max(1, Mathf.RoundToInt(text.Scale));
                        int pixelHeight = Mathf.Max(1, Mathf.RoundToInt(text.Scale));

                        for (int sy = 0; sy < pixelHeight; sy++)
                        {
                            int targetY = pixelY + sy;

                            if (targetY < 0 || targetY >= height)
                            {
                                continue;
                            }

                            for (int sx = 0; sx < pixelWidth; sx++)
                            {
                                int targetX = pixelX + sx;

                                if (targetX < 0 || targetX >= width)
                                {
                                    continue;
                                }

                                int target = ((height - 1 - targetY) * width) + targetX;
                                pixels[target] = Blend(pixels[target], text.Colour);
                            }
                        }
                    }
                }
            }
        }

        private static Color32 Blend(Color32 destination, Color32 source)
        {
            if (source.a == 255)
            {
                return source;
            }

            float alpha = source.a / 255f;
            float inverse = 1f - alpha;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((source.r * alpha) + (destination.r * inverse)), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((source.g * alpha) + (destination.g * inverse)), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((source.b * alpha) + (destination.b * inverse)), 0, 255),
                255);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_GAMEPLAY_HUD built=").Append(Built ? "True" : "False");
            text.Append(" framesDrawn=").Append(FramesDrawn.ToString(CultureInfo.InvariantCulture));
            text.Append(" art=").Append(Data == null ? "none" : Data.Describe());
            text.Append(" model=\"").Append(LastModelText).Append('"');
            text.Append(" textFont=interim3x5 originalTextFont=MS Sans Serif(not shipped)");
            text.Append(" draw=gui-interactive+cpu-capture-composite");

            return text.ToString();
        }

        public void Destroy()
        {
            for (int i = 0; i < owned.Count; i++)
            {
                UnityEngine.Object value = owned[i];

                if (value == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(value);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            owned.Clear();
            glyphTextures.Clear();
            arts.Clear();
            Built = false;
        }
    }
}
