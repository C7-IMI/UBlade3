using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UBlade3.Sim;
using UnityEngine;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The original InfoBar layout, ported from src/ui/infobar.cpp LayoutSurfaces (0x00450e60) and
    // the 20XX HudLayer scaling (src/graphics/hudlayer.cpp: the HUD is a layer with kHUDLines=480,
    // integer scale = max(1, backBufferHeight / 480), centring the layer). Positions are the exact
    // decomp formulas; the original art is decoded from Data\assets8 and text is drawn with the real
    // installed GDI MS Sans Serif through MatchHudFont (CreateFontA heights 9/10/11/15/20).
    //
    // The N12 renderer is opt-in: MatchHud.OriginalLayout is false for every existing N4-N11 capture
    // path, so those capture bytes are unchanged. The interactive player enables it by default
    // (MatchHudOriginal.ShouldUseByDefault), and the N12 HUD probe/capture enables it explicitly.
    public sealed class MatchHudOriginal
    {
        public sealed class Art
        {
            public string Name;
            public int Width;
            public int Height;
            public Color32[] Pixels;
        }

        public struct Layout
        {
            public int Width;
            public int Height;
            public int LayerWidth;
            public int LayerHeight;
            public int Scale;
            public int OriginX;
            public int OriginY;

            public RectInt PeriodFrame;
            public Vector2Int Clock;
            public RectInt SpeedFrame;
            public Vector2Int Roster;
            public Vector2Int FrameRate;

            public RectInt TeamAFrame;
            public RectInt TeamBFrame;
            public Vector2Int TeamALogo;
            public Vector2Int TeamBLogo;
            public Vector2Int HomeScore;
            public Vector2Int AwayScore;
            public Vector2Int RokValue;

            public RectInt DamageA1;
            public RectInt DamageA2;
            public RectInt DamageB1;
            public RectInt DamageB2;

            public RectInt MessageFrame;
            public Vector2Int MessageText;
            public RectInt RadarFrame;
            public Vector2Int Radar;
            public RectInt Network;
            public RectInt HeadCamFrame;
            public Vector2Int HeadCamText;
            public RectInt ShotPower;
            public Vector2Int Reticle;
            public Vector2Int ReticleSize;

            public Vector2Int TeamAName;
            public Vector2Int TeamBName;
            public Vector2Int[] TeamARows;
            public Vector2Int[] TeamBRows;
        }

        public bool IsBuilt;
        public readonly List<string> Missing = new List<string>();
        public Art Period;
        public Art TeamA;
        public Art TeamB;
        public Art HomeLogo;
        public Art AwayLogo;
        public Art SpeedFrame;
        public Art SpeedStrip;
        public Art RadarFrame;
        public Art ShotFrame;
        public Art MessageFrame;
        public Art Network;
        public Art ShotPower;
        public Art Reticle;

        private Texture2D guiTexture;

        public const int DamageFrameCount = 12;
        public const int DamageUnitWidth = 9;
        public const int DamageMinHeight = 2;
        public const int DamageMaxHeight = 8;

        public const int PeriodLampTop = 0x28;
        public const int PeriodLampSide = 9;

        public static readonly int[] PeriodLightOffsets = { 19, 38, 57 };

        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Yellow = new Color32(255, 255, 0, 255);
        private static readonly Color32 Green = new Color32(0, 255, 0, 255);
        private static readonly Color32 Red = new Color32(255, 1, 0, 255);
        private static readonly Color32 Black = new Color32(0, 0, 0, 255);

        // The interactive player gets the original HUD; the deterministic capture entry points keep
        // the N6/N7/N11 legacy layout so their committed evidence stays byte-identical.
        public static bool ShouldUseByDefault()
        {
            if (!Application.isPlaying || Application.isBatchMode)
            {
                return false;
            }

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];

                if (argument.StartsWith("--play-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--n7-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--display-probe=", StringComparison.Ordinal)
                    || argument.StartsWith("--hud-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--hud-check", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public void Build(MatchWorldData world)
        {
            if (IsBuilt)
            {
                return;
            }

            Period = Load(world, "frper");
            TeamA = Load(world, "frdamgA");
            TeamB = Load(world, "frdamgB");
            HomeLogo = Load(world, world.Home.File.IdLogo);
            AwayLogo = Load(world, world.Away.File.IdLogo);
            SpeedFrame = Load(world, "frspeed");
            SpeedStrip = Load(world, "speed01");
            RadarFrame = Load(world, "frradar");
            ShotFrame = Load(world, "frshot");
            MessageFrame = Load(world, "frmsg");
            Network = Load(world, "network");
            ShotPower = Load(world, "nhshotp");
            Reticle = Load(world, "reticl2");
            IsBuilt = true;

            Debug.Log(Describe());
        }

        private Art Load(MatchWorldData world, params string[] names)
        {
            if (names == null || names.Length == 0 || string.IsNullOrEmpty(names[0]))
            {
                Missing.Add("<null>");
                return null;
            }

            for (int i = 0; i < names.Length; i++)
            {
                Art art = LoadOne(world, names[i], false);

                if (art != null)
                {
                    return art;
                }
            }

            Missing.Add(names[0]);
            return null;
        }

        private Art LoadOne(MatchWorldData world, string name, bool recordMissing)
        {
            if (string.IsNullOrEmpty(name))
            {
                if (recordMissing)
                {
                    Missing.Add("<null>");
                }

                return null;
            }

            string path = world.Root.ResolveDataFile("assets8/" + name + ".bmp");

            if (path == null)
            {
                if (recordMissing)
                {
                    Missing.Add(name);
                }

                return null;
            }

            try
            {
                BmpTexture bitmap = BmpReader.LoadFile(path);
                Art art = new Art();
                art.Name = name;
                art.Width = bitmap.Width;
                art.Height = bitmap.Height;
                art.Pixels = new Color32[bitmap.Width * bitmap.Height];

                byte keyR = bitmap.Rgba[0];
                byte keyG = bitmap.Rgba[1];
                byte keyB = bitmap.Rgba[2];

                for (int i = 0; i < art.Pixels.Length; i++)
                {
                    int at = i * 4;
                    byte r = bitmap.Rgba[at];
                    byte g = bitmap.Rgba[at + 1];
                    byte b = bitmap.Rgba[at + 2];
                    bool key = r == keyR && g == keyG && b == keyB;
                    art.Pixels[i] = new Color32(r, g, b, key ? (byte)0 : (byte)255);
                }

                return art;
            }
            catch (Exception exception)
            {
                if (recordMissing)
                {
                    Missing.Add(name + "(" + exception.GetType().Name + ")");
                }

                return null;
            }
        }

        public Layout ComputeLayout(int width, int height, MatchHudModel model)
        {
            Layout layout = new Layout();
            layout.Width = Math.Max(1, width);
            layout.Height = Math.Max(1, height);
            layout.Scale = Math.Max(1, layout.Height / 480);
            layout.LayerWidth = Math.Max(1, (layout.Width / layout.Scale) & ~3);
            layout.LayerHeight = Math.Max(1, layout.Height / layout.Scale);
            layout.OriginX = (layout.Width - (layout.LayerWidth * layout.Scale)) / 2;
            layout.OriginY = (layout.Height - (layout.LayerHeight * layout.Scale)) / 2;

            int w = layout.LayerWidth;
            int h = layout.LayerHeight;

            // infobar.cpp LayoutSurfaces: period frame centred at y=2, clock inside it.
            layout.PeriodFrame = new RectInt((w - Period.Width) / 2, 2, Period.Width, Period.Height);
            int clockWidth;
            int clockHeight;
            MeasureOr("0:00", MatchHudFont.Large, out clockWidth, out clockHeight);
            layout.Clock = new Vector2Int(
                layout.PeriodFrame.x + ((layout.PeriodFrame.width - clockWidth) / 2), 9);

            layout.SpeedFrame = new RectInt(0, 0, SpeedFrame.Width, SpeedFrame.Height);
            layout.Roster = new Vector2Int(6, SpeedFrame.Height);
            layout.FrameRate = new Vector2Int(0x14, SpeedFrame.Height + 0x14);

            layout.TeamAFrame = new RectInt(0, h - (TeamA.Height * 2) - 3, TeamA.Width, TeamA.Height);
            layout.DamageA1 = new RectInt(0x4e, layout.TeamAFrame.y + 0x18, DamageUnitWidth * (DamageFrameCount - 1), DamageMaxHeight);
            layout.DamageA2 = new RectInt(0x4e, layout.DamageA1.y + 0xc, layout.DamageA1.width, layout.DamageA1.height);
            layout.TeamALogo = new Vector2Int(2, layout.TeamAFrame.y + 0x16);
            layout.HomeScore = new Vector2Int(TeamA.Width - 0x20, layout.TeamAFrame.y + 0xa);
            layout.RokValue = new Vector2Int(layout.HomeScore.x - 3, layout.HomeScore.y - 8);

            layout.TeamBFrame = new RectInt(0, h - TeamB.Height - 5, TeamB.Width, TeamB.Height);
            layout.DamageB1 = new RectInt(0x4e, layout.TeamBFrame.y + 4, layout.DamageA1.width, layout.DamageA1.height);
            layout.DamageB2 = new RectInt(0x4e, layout.DamageB1.y + 0xc, layout.DamageA1.width, layout.DamageA1.height);
            layout.TeamBLogo = new Vector2Int(2, layout.TeamBFrame.y + 4);
            layout.AwayScore = new Vector2Int(TeamB.Width - 0x20, layout.TeamBFrame.y + 0x19);

            layout.MessageFrame = new RectInt(TeamB.Width - 0x14, layout.TeamBFrame.y - 0xc,
                MessageFrame.Width, MessageFrame.Height);
            layout.MessageText = new Vector2Int(layout.MessageFrame.x + 6, layout.MessageFrame.y + 5);

            layout.RadarFrame = new RectInt(w - RadarFrame.Width - 0x14, h - RadarFrame.Height - 0x14,
                RadarFrame.Width, RadarFrame.Height);
            layout.Radar = new Vector2Int(layout.RadarFrame.x, layout.RadarFrame.y);

            layout.Network = new RectInt(w - Network.Width - 0x14, 0x14, Network.Width, Network.Height);

            layout.HeadCamFrame = new RectInt(w - ShotFrame.Width, 0, ShotFrame.Width, ShotFrame.Height);
            layout.HeadCamText = new Vector2Int(layout.HeadCamFrame.x + 0x1e, ShotFrame.Height);
            layout.ShotPower = new RectInt(layout.HeadCamFrame.x + 0x23, layout.HeadCamFrame.y + 0x10,
                ShotPower.Width, ShotPower.Height);
            layout.Reticle = new Vector2Int(layout.HeadCamFrame.x + 0x19, layout.HeadCamFrame.y + 0x19);
            layout.ReticleSize = new Vector2Int(Reticle.Width, Reticle.Height);

            int nameHeight;
            int rowHeight;
            MeasureOr("X", MatchHudFont.Pair, out int ignoredNameWidth, out nameHeight);
            MeasureOr("X", MatchHudFont.Timer, out int ignoredRowWidth, out rowHeight);

            layout.TeamAName = layout.Roster;
            layout.TeamARows = new Vector2Int[4];
            int y = layout.TeamAName.y + nameHeight + 2;
            layout.TeamARows[0] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2;
            layout.TeamARows[1] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2 + 6;
            layout.TeamARows[2] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2;
            layout.TeamARows[3] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2 + 0x1c;
            layout.TeamBName = new Vector2Int(layout.Roster.x, y);
            y += nameHeight + 2;
            layout.TeamBRows = new Vector2Int[4];
            layout.TeamBRows[0] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2;
            layout.TeamBRows[1] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2 + 6;
            layout.TeamBRows[2] = new Vector2Int(layout.Roster.x, y);
            y += rowHeight + 2;
            layout.TeamBRows[3] = new Vector2Int(layout.Roster.x, y);
            return layout;
        }

        private static void MeasureOr(string text, int size, out int width, out int height)
        {
            if (!MatchHudFont.Measure(text, size, out width, out height))
            {
                width = text.Length * (size / 2);
                height = size;
            }
        }

        public Color32[] Render(MatchHudModel model, Simulation sim, MatchHud hud, int width, int height,
            out Layout layout)
        {
            layout = ComputeLayout(width, height, model);
            Color32[] layer = new Color32[layout.LayerWidth * layout.LayerHeight];
            int lw = layout.LayerWidth;
            int lh = layout.LayerHeight;

            // DrawPeriodLight: frame then the current-period lamp.
            Blit(layer, lw, lh, Period, layout.PeriodFrame.x, layout.PeriodFrame.y);
            int lamp = Mathf.Clamp(sim.Rules.Clock.Period - 1, 0, 2);
            Fill(layer, lw, lh, layout.PeriodFrame.x + PeriodLightOffsets[lamp],
                layout.PeriodFrame.y + PeriodLampTop, PeriodLampSide, PeriodLampSide, Red);
            Text(layer, lw, lh, layout.Clock.x, layout.Clock.y, model.ClockText, MatchHudFont.Large, White);

            // DrawScene: speed frame, logos/frames, damage bars, message, radar, head camera.
            // Speed meter: CDDSurfaceOwner::Draw (0x0044f9b0) blits the first
            // nUnitWidth * nFrame columns of the speed01 sprite; SetStrip(width, 1, 0, 0), so the
            // fraction is the followed player's SpeedRatio. The frame art is drawn over it.
            float speedRatio = hud.HudSpeedRatioOverride;

            if (speedRatio < 0f && sim.ViewedSlot >= 0 && sim.ViewedSlot < sim.Players.Length)
            {
                speedRatio = sim.Players[sim.ViewedSlot].Stats.SpeedRatio;
            }

            speedRatio = Mathf.Clamp01(speedRatio);

            if (SpeedStrip != null && speedRatio > 0f)
            {
                int speedWidth = Mathf.RoundToInt(SpeedStrip.Width * speedRatio);

                if (speedWidth > 0)
                {
                    BlitClipped(layer, lw, lh, SpeedStrip, layout.SpeedFrame.x, layout.SpeedFrame.y,
                        speedWidth, SpeedStrip.Height);
                }
            }

            Blit(layer, lw, lh, SpeedFrame, layout.SpeedFrame.x, layout.SpeedFrame.y);
            Blit(layer, lw, lh, HomeLogo, layout.TeamALogo.x, layout.TeamALogo.y);
            Blit(layer, lw, lh, TeamA, layout.TeamAFrame.x, layout.TeamAFrame.y);
            DrawDamageBar(layer, lw, lh, layout.DamageA1, 0, sim);
            DrawDamageBar(layer, lw, lh, layout.DamageA2, 2, sim);
            Blit(layer, lw, lh, AwayLogo, layout.TeamBLogo.x, layout.TeamBLogo.y);
            Blit(layer, lw, lh, TeamB, layout.TeamBFrame.x, layout.TeamBFrame.y);
            DrawDamageBar(layer, lw, lh, layout.DamageB1, 1, sim);
            DrawDamageBar(layer, lw, lh, layout.DamageB2, 3, sim);

            // Radar: blips are drawn before the frame, as in InfoBar::DrawScene.
            DrawRadar(layer, lw, lh, layout, sim);
            Blit(layer, lw, lh, RadarFrame, layout.RadarFrame.x, layout.RadarFrame.y);

            if (!string.IsNullOrEmpty(hud.StatusLine))
            {
                Blit(layer, lw, lh, MessageFrame, layout.MessageFrame.x, layout.MessageFrame.y);
                Text(layer, lw, lh, layout.MessageText.x, layout.MessageText.y, Clip(hud.StatusLine, 32),
                    MatchHudFont.Body, White);
            }

            // Head camera frame, shot power and reticle. The capture probe can pin a state; an
            // interactive run reads the MatchPlay control when it exists.
            int shotPower = hud.HudShotPowerOverride;
            bool headCam = hud.HudHeadCamOverride;
            bool reticle = hud.HudReticleOverride;
            float reticleX = hud.HudReticleX;
            float reticleY = hud.HudReticleY;
            MatchPlay play = UnityEngine.Object.FindAnyObjectByType<MatchPlay>();

            if (play != null && play.Control != null && hud.HudShotPowerOverride < 0)
            {
                shotPower = play.Control.ShotPowerLevel;
                headCam = play.Control.Carrying;
                reticle = play.Control.Carrying;
                reticleX = play.Control.HeadCam.ViewFracX;
                reticleY = play.Control.HeadCam.ViewFracY;
            }

            if (headCam || shotPower >= 0)
            {
                Blit(layer, lw, lh, ShotFrame, layout.HeadCamFrame.x, layout.HeadCamFrame.y);
                Text(layer, lw, lh, layout.HeadCamText.x, layout.HeadCamText.y, "SHOT CAMERA",
                    MatchHudFont.Label, Yellow);

                if (shotPower >= 0)
                {
                    int clipped = Mathf.Clamp(shotPower, 0, 100);
                    int drawWidth = Mathf.RoundToInt((ShotPower.Width * clipped) / 100f);

                    if (drawWidth > 0)
                    {
                        BlitClipped(layer, lw, lh, ShotPower, layout.ShotPower.x, layout.ShotPower.y,
                            drawWidth, ShotPower.Height);
                    }
                }

                if (reticle && Reticle != null)
                {
                    int rx = layout.Reticle.x + Mathf.RoundToInt(
                        Mathf.Clamp01(reticleX) * Mathf.Max(1, ShotFrame.Width - 0x32 - Reticle.Width));
                    int ry = layout.Reticle.y + Mathf.RoundToInt(
                        Mathf.Clamp01(reticleY) * Mathf.Max(1, ShotFrame.Height - 0x32 - Reticle.Height));
                    Blit(layer, lw, lh, Reticle, rx, ry);
                }
            }

            // Team names/rosters: the original font sizes 11 and 9, original colours (reserve green,
            // followed yellow, on-drome red, absent black).
            Text(layer, lw, lh, layout.TeamAName.x, layout.TeamAName.y, model.HomeTeam,
                MatchHudFont.Pair, Green);
            Text(layer, lw, lh, layout.TeamBName.x, layout.TeamBName.y, model.AwayTeam,
                MatchHudFont.Pair, Green);
            DrawRoster(layer, lw, lh, layout.TeamARows, model.HomeTeam, sim, true);
            DrawRoster(layer, lw, lh, layout.TeamBRows, model.AwayTeam, sim, false);

            Text(layer, lw, lh, layout.HomeScore.x, layout.HomeScore.y, model.HomeScoreText,
                MatchHudFont.Body, White);
            Text(layer, lw, lh, layout.AwayScore.x, layout.AwayScore.y, model.AwayScoreText,
                MatchHudFont.Body, White);
            Text(layer, lw, lh, layout.RokValue.x, layout.RokValue.y, model.RokText,
                MatchHudFont.Timer, Yellow);

            return layer;
        }

        private void DrawRoster(Color32[] layer, int lw, int lh, Vector2Int[] rows, string team,
            Simulation sim, bool home)
        {
            int slotA = home ? 0 : 1;
            int slotB = home ? 2 : 3;
            int goalie = home ? ObjectConstants.GoalieSlotA : ObjectConstants.GoalieSlotB;
            int[] slots = { slotA, slotB, goalie };
            string code = TeamCode(team);
            string[] labels = { "1. " + code + " F1", "2. " + code + " F2", "G. " + code + " G" };

            for (int i = 0; i < 3 && i < rows.Length; i++)
            {
                Text(layer, lw, lh, rows[i].x, rows[i].y, labels[i], MatchHudFont.Timer, Green);

                float damage = sim.Players[slots[i]].Stats.GetScalar(ObjectConstants.RatingDamage);
                string value = "DMG " + Mathf.Clamp(Mathf.RoundToInt(damage), 0, 999).ToString(
                    CultureInfo.InvariantCulture);
                Text(layer, lw, lh, rows[i].x + 78, rows[i].y, value, MatchHudFont.Timer,
                    damage <= 0f ? Black : White);
            }

            // Row four remains the second reserve seat in the original; the simulation has no bench
            // roster, so it stays blank (documented gap). Row three uses the actual goalie data that
            // does exist, labelled G. TEAM G.
        }

        private static string TeamCode(string team)
        {
            if (string.IsNullOrEmpty(team))
            {
                return "TEAM";
            }

            string value = team.Trim();

            if (value.StartsWith("THE ", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(4);
            }

            return value.Length <= 3 ? value : value.Substring(0, 3);
        }

        private void DrawDamageBar(Color32[] layer, int lw, int lh, RectInt rect, int slot, Simulation sim)
        {
            PlayerState player = sim.Players[slot];
            float fraction = Mathf.Clamp01(player.Stats.GetScalar(ObjectConstants.RatingDamage) * 0.01f);
            float scalar = fraction * DamageFrameCount;
            int frame = (int)scalar;

            if (frame >= DamageFrameCount)
            {
                frame = DamageFrameCount - 1;
            }

            if (frame < 0)
            {
                frame = 0;
            }

            Color32 colour = sim.ViewedSlot == slot ? Yellow : Red;
            int x = rect.x;
            int y = rect.y;
            int far = DamageMaxHeight;
            float step = (DamageMaxHeight - DamageMinHeight) / (float)DamageFrameCount;

            if (frame == 0)
            {
                if (scalar > 0f)
                {
                    int width = (int)(DamageUnitWidth * scalar);

                    if (width <= 0)
                    {
                        width = 1;
                    }

                    Fill(layer, lw, lh, x, y - DamageMinHeight + far, width, DamageMinHeight, colour);
                }

                return;
            }

            for (int i = 0; i < frame; i++)
            {
                int height = (int)(i * step) + DamageMinHeight;
                Fill(layer, lw, lh, x, y - height + far, DamageUnitWidth, height, colour);
                x += DamageUnitWidth;
            }
        }

        private void DrawRadar(Color32[] layer, int lw, int lh, Layout layout, Simulation sim)
        {
            int halfWidth = (RadarFrame.Width - 4) / 2;
            int halfHeight = (RadarFrame.Height - 4) / 2;
            int centreX = layout.RadarFrame.x + halfWidth;
            int centreY = layout.RadarFrame.y + halfHeight;
            float divisor = Mathf.Max(sim.Arena.State.Ellipsoid.GetRadiusX(),
                sim.Arena.State.Ellipsoid.GetRadiusY());
            float scaleX = halfWidth / divisor;
            float scaleY = halfHeight / divisor;

            int viewed = sim.ViewedSlot;

            if (viewed < 0 || viewed >= sim.Players.Length)
            {
                return;
            }

            SimVec3 centre = sim.Players[viewed].Body.Pos;
            SimVec3 camera = sim.MainCamera.Body.VelDir;
            double angle = Math.Atan2(camera.x, camera.y);
            float sin = (float)Math.Sin(angle);
            float cos = (float)Math.Cos(angle);

            bool carriedByViewed = sim.Rok.IsCarried && sim.Rok.AttachedSlot == viewed;

            if (!carriedByViewed)
            {
                DrawRadarBlip(layer, lw, lh, sim.Rok.Body.Pos, centre, centreX, centreY, scaleX, scaleY,
                    sin, cos, Rgb(0, 255, 255), true);
            }

            Color32[] colours =
            {
                Rgb(0, 200, 200), Rgb(200, 0, 0), Rgb(0, 255, 255), Rgb(255, 0, 0),
            };

            for (int slot = 0; slot < 4; slot++)
            {
                if (slot == viewed)
                {
                    continue;
                }

                DrawRadarBlip(layer, lw, lh, sim.Players[slot].Body.Pos, centre, centreX, centreY,
                    scaleX, scaleY, sin, cos, colours[slot], false);
            }

            if (carriedByViewed)
            {
                int originX = layout.RadarFrame.x + halfWidth + 2;
                int originY = layout.RadarFrame.y + halfHeight + 2;
                DrawArrow(layer, lw, lh, originX, originY, scaleX, scaleY, sin, cos, viewed, Rgb(230, 0, 0));
            }
        }

        private void DrawRadarBlip(Color32[] layer, int lw, int lh, SimVec3 position, SimVec3 centre,
            int centreX, int centreY, float scaleX, float scaleY, float sin, float cos,
            Color32 colour, bool trail)
        {
            float dx = position.x - centre.x;
            float dy = position.y - centre.y;

            if (dx > 170f || dx < -170f || dy > 170f || dy < -170f)
            {
                return;
            }

            float fx = dx * scaleX;
            float fy = dy * scaleY;
            int x = (int)((fx * cos) - (fy * sin)) + centreX;
            int y = centreY - (int)((fx * sin) + (fy * cos));

            Fill(layer, lw, lh, x, y, 4, 4, colour);

            if (trail)
            {
                DrawLine(layer, lw, lh, centreX + 2, centreY + 2, x + 2, y + 2, Rgb(255, 255, 224));
            }
        }

        private static void DrawArrow(Color32[] layer, int lw, int lh, int originX, int originY,
            float scaleX, float scaleY, float sin, float cos, int viewed, Color32 colour)
        {
            Vector2Int a;
            Vector2Int b;
            Vector2Int c;

            if ((viewed & 1) != 0)
            {
                a = new Vector2Int(0, 0xbe);
                b = new Vector2Int(-0xf, 0x8c);
                c = new Vector2Int(0xf, 0x8c);
            }
            else
            {
                a = new Vector2Int(0, -0xbe);
                b = new Vector2Int(0xf, -0x8c);
                c = new Vector2Int(-0xf, -0x8c);
            }

            Vector2Int[] points = { a, b, c };

            for (int i = 0; i < points.Length; i++)
            {
                float fx = points[i].x * scaleX;
                float fy = points[i].y * scaleY;
                points[i] = new Vector2Int(
                    (int)((fx * cos) - (fy * sin)) + originX,
                    originY - (int)((fx * sin) + (fy * cos)));
            }

            FillTriangle(layer, lw, lh, points[0], points[1], points[2], colour);
        }

        private static Color32 Rgb(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }

        private static string Clip(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= max ? text : text.Substring(0, max);
        }

        private static void Text(Color32[] layer, int lw, int lh, int x, int y, string text, int size,
            Color32 colour)
        {
            if (!MatchHudFont.Draw(layer, lw, lh, x, y, text, size, colour))
            {
                // Font unavailable: the probe logs the gap; no unrelated face is substituted.
            }
        }

        private static void Fill(Color32[] layer, int lw, int lh, int x, int y, int width, int height,
            Color32 colour)
        {
            for (int py = 0; py < height; py++)
            {
                int ty = y + py;

                if (ty < 0 || ty >= lh)
                {
                    continue;
                }

                for (int px = 0; px < width; px++)
                {
                    int tx = x + px;

                    if (tx < 0 || tx >= lw)
                    {
                        continue;
                    }

                    layer[(ty * lw) + tx] = colour;
                }
            }
        }

        private static void Blit(Color32[] layer, int lw, int lh, Art art, int x, int y)
        {
            if (art == null || art.Pixels == null)
            {
                return;
            }

            BlitClipped(layer, lw, lh, art, x, y, art.Width, art.Height);
        }

        private static void BlitClipped(Color32[] layer, int lw, int lh, Art art, int x, int y,
            int width, int height)
        {
            if (art == null || art.Pixels == null)
            {
                return;
            }

            int copyWidth = Math.Min(width, art.Width);
            int copyHeight = Math.Min(height, art.Height);

            for (int py = 0; py < copyHeight; py++)
            {
                int ty = y + py;

                if (ty < 0 || ty >= lh)
                {
                    continue;
                }

                for (int px = 0; px < copyWidth; px++)
                {
                    int tx = x + px;

                    if (tx < 0 || tx >= lw)
                    {
                        continue;
                    }

                    Color32 source = art.Pixels[(py * art.Width) + px];

                    if (source.a == 0)
                    {
                        continue;
                    }

                    layer[(ty * lw) + tx] = source;
                }
            }
        }

        private static void DrawLine(Color32[] layer, int lw, int lh, int x0, int y0, int x1, int y1,
            Color32 colour)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            int x = x0;
            int y = y0;

            for (int guard = 0; guard < 4096; guard++)
            {
                if (x >= 0 && x < lw && y >= 0 && y < lh)
                {
                    layer[(y * lw) + x] = colour;
                }

                if (x == x1 && y == y1)
                {
                    return;
                }

                int e2 = 2 * error;

                if (e2 >= dy)
                {
                    error += dy;
                    x += sx;
                }

                if (e2 <= dx)
                {
                    error += dx;
                    y += sy;
                }
            }
        }

        private static void FillTriangle(Color32[] layer, int lw, int lh, Vector2Int a, Vector2Int b,
            Vector2Int c, Color32 colour)
        {
            int minX = Math.Min(a.x, Math.Min(b.x, c.x));
            int maxX = Math.Max(a.x, Math.Max(b.x, c.x));
            int minY = Math.Min(a.y, Math.Min(b.y, c.y));
            int maxY = Math.Max(a.y, Math.Max(b.y, c.y));
            long area = (long)(b.x - a.x) * (c.y - a.y) - (long)(b.y - a.y) * (c.x - a.x);

            if (area == 0)
            {
                return;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long w0 = (long)(b.x - a.x) * (y - a.y) - (long)(b.y - a.y) * (x - a.x);
                    long w1 = (long)(c.x - b.x) * (y - b.y) - (long)(c.y - b.y) * (x - b.x);
                    long w2 = (long)(a.x - c.x) * (y - c.y) - (long)(a.y - c.y) * (x - c.x);

                    if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0))
                    {
                        if (x >= 0 && x < lw && y >= 0 && y < lh)
                        {
                            layer[(y * lw) + x] = colour;
                        }
                    }
                }
            }
        }

        public void Composite(MatchHud hud, Color32[] pixels, int width, int height, MatchHudModel model)
        {
            if (!IsBuilt || model == null || pixels == null || hud == null || hud.Scene == null)
            {
                return;
            }

            Simulation sim = hud.Scene.Sim.Sim;
            Layout layout;
            Color32[] layer = Render(model, sim, hud, width, height, out layout);

            for (int ly = 0; ly < layout.LayerHeight; ly++)
            {
                for (int lx = 0; lx < layout.LayerWidth; lx++)
                {
                    Color32 source = layer[(ly * layout.LayerWidth) + lx];

                    if (source.a == 0)
                    {
                        continue;
                    }

                    for (int sy = 0; sy < layout.Scale; sy++)
                    {
                        int screenY = layout.OriginY + (ly * layout.Scale) + sy;

                        if (screenY < 0 || screenY >= height)
                        {
                            continue;
                        }

                        int targetY = height - 1 - screenY;

                        for (int sx = 0; sx < layout.Scale; sx++)
                        {
                            int screenX = layout.OriginX + (lx * layout.Scale) + sx;

                            if (screenX < 0 || screenX >= width)
                            {
                                continue;
                            }

                            int target = (targetY * width) + screenX;

                            if (source.a == 255)
                            {
                                pixels[target] = source;
                            }
                            else
                            {
                                pixels[target] = Blend(pixels[target], source);
                            }
                        }
                    }
                }
            }
        }

        public void DrawGui(MatchHud hud)
        {
            if (!IsBuilt || hud == null || hud.Scene == null)
            {
                return;
            }

            MatchHudModel model = hud.Gameplay != null ? hud.Gameplay.HudModel : null;

            if (model == null)
            {
                model = MatchHudModel.From(hud.Scene.World, hud.Scene.Sim.Sim);
            }

            Layout layout;
            Color32[] layer = Render(model, hud.Scene.Sim.Sim, hud, Screen.width, Screen.height, out layout);

            if (guiTexture == null || guiTexture.width != layout.LayerWidth
                || guiTexture.height != layout.LayerHeight)
            {
                if (guiTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(guiTexture);
                }

                guiTexture = new Texture2D(layout.LayerWidth, layout.LayerHeight, TextureFormat.RGBA32, false);
                guiTexture.name = "UBlade3 Original HUD";
                guiTexture.filterMode = FilterMode.Point;
                guiTexture.wrapMode = TextureWrapMode.Clamp;
            }

            // The layout layer is top-down; Unity's SetPixels32 uploads bottom-up. Flip rows so the
            // interactive GUI shows the same upright HUD as the CPU capture composite.
            Color32[] upload = new Color32[layer.Length];
            for (int row = 0; row < layout.LayerHeight; row++)
            {
                Array.Copy(layer, row * layout.LayerWidth, upload,
                    (layout.LayerHeight - 1 - row) * layout.LayerWidth, layout.LayerWidth);
            }

            guiTexture.SetPixels32(upload);
            guiTexture.Apply(false, false);

            GUI.DrawTexture(new Rect(layout.OriginX, layout.OriginY,
                layout.LayerWidth * layout.Scale, layout.LayerHeight * layout.Scale), guiTexture);
        }

        private static Color32 Blend(Color32 destination, Color32 source)
        {
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
            text.Append("UBLADE3_NATIVE_HUD_ART");
            Append(text, Period);
            Append(text, TeamA);
            Append(text, TeamB);
            Append(text, HomeLogo);
            Append(text, AwayLogo);
            Append(text, SpeedFrame);
            Append(text, SpeedStrip);
            Append(text, RadarFrame);
            Append(text, ShotFrame);
            Append(text, MessageFrame);
            Append(text, Network);
            Append(text, ShotPower);
            Append(text, Reticle);
            text.Append(" missing=").Append(Missing.Count);
            text.Append(" text=").Append(MatchHudFont.Describe());
            return text.ToString();
        }

        private static void Append(StringBuilder text, Art art)
        {
            text.Append(' ').Append(art == null ? "missing" : art.Name + "=" + art.Width + "x" + art.Height);
        }
    }
}
