using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBlade3.Native
{
    // N12 HUD probe: compares the ported original InfoBar rectangles against the decomp formulas at
    // the original 640x480 design, checks the decoded art dimensions against the documented shipped
    // sizes, and records the real GDI MS Sans Serif metrics. Every mismatch is counted; the gaps are
    // listed in docs/NATIVE-HUD.md.
    public static class MatchHudProbe
    {
        public static int Check(MatchWorldData world, out string detail)
        {
            StringBuilder text = new StringBuilder();
            int mismatches = 0;

            MatchHudOriginal hud = new MatchHudOriginal();
            hud.Build(world);

            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_ART " + hud.Describe());

            if (hud.Missing.Count != 0)
            {
                mismatches++;
            }

            // Documented art sizes from docs/NATIVE-GAMEPLAY.md (frper, frdamgA/B, logos).
            mismatches += Expect(text, "frper", hud.Period, 83, 60);
            mismatches += Expect(text, "frdamgA", hud.TeamA, 220, 47);
            mismatches += Expect(text, "frdamgB", hud.TeamB, 220, 47);
            mismatches += Expect(text, "homeLogo", hud.HomeLogo, 73, 21);
            mismatches += Expect(text, "awayLogo", hud.AwayLogo, 73, 21);

            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_FONT " + MatchHudFont.Describe());

            if (!MatchHudFont.Available)
            {
                mismatches++;
            }

            MatchHudModel model = new MatchHudModel();
            model.HomeTeam = "THE MACHINE";
            model.AwayTeam = "THE FURY";
            model.HomeScoreText = "0";
            model.AwayScoreText = "0";
            model.Period = 1;
            model.ClockText = "4:59";
            model.RokText = "x0";

            MatchHudOriginal.Layout design = hud.ComputeLayout(640, 480, model);
            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_LAYER design=640x480 layer="
                            + design.LayerWidth + "x" + design.LayerHeight + " scale=" + design.Scale
                            + " origin=" + design.OriginX + "," + design.OriginY);
            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_RECTS " + DescribeLayout(design));

            // Decomp LayoutSurfaces at a 640x480 layer: period (W-83)/2,2; team A y=H-47*2-3=383;
            // team B y=H-47-5=428; bars x=0x4e and the documented row offsets; scores x=220-0x20;
            // radar at (W-radarW-0x14, H-radarH-0x14); head-cam frame at (W-shotW,0).
            mismatches += Rect(text, "period", design.PeriodFrame, 278, 2, 83, 60);
            mismatches += Rect(text, "teamA", design.TeamAFrame, 0, 383, 220, 47);
            mismatches += Rect(text, "teamB", design.TeamBFrame, 0, 428, 220, 47);
            mismatches += Vec(text, "teamALogo", design.TeamALogo, 2, 383 + 0x16);
            mismatches += Vec(text, "teamBLogo", design.TeamBLogo, 2, 428 + 4);
            mismatches += Vec(text, "homeScore", design.HomeScore, 220 - 0x20, 383 + 0xa);
            mismatches += Vec(text, "awayScore", design.AwayScore, 220 - 0x20, 428 + 0x19);
            mismatches += Vec(text, "rok", design.RokValue, 220 - 0x20 - 3, 383 + 0xa - 8);
            mismatches += Rect(text, "damageA1", design.DamageA1, 0x4e, 383 + 0x18, 9 * 11, 8);
            mismatches += Rect(text, "damageA2", design.DamageA2, 0x4e, 383 + 0x18 + 0xc, 9 * 11, 8);
            mismatches += Rect(text, "damageB1", design.DamageB1, 0x4e, 428 + 4, 9 * 11, 8);
            mismatches += Rect(text, "damageB2", design.DamageB2, 0x4e, 428 + 4 + 0xc, 9 * 11, 8);
            mismatches += Rect(text, "radar", design.RadarFrame,
                640 - hud.RadarFrame.Width - 0x14, 480 - hud.RadarFrame.Height - 0x14,
                hud.RadarFrame.Width, hud.RadarFrame.Height);
            mismatches += Rect(text, "headCam", design.HeadCamFrame, 640 - hud.ShotFrame.Width, 0,
                hud.ShotFrame.Width, hud.ShotFrame.Height);
            mismatches += Vec(text, "message", new Vector2Int(design.MessageFrame.x, design.MessageFrame.y),
                220 - 0x14, 428 - 0xc);
            mismatches += Vec(text, "network", new Vector2Int(design.Network.x, design.Network.y),
                640 - hud.Network.Width - 0x14, 0x14);
            mismatches += Vec(text, "teamAName", design.TeamAName, 6, hud.SpeedFrame.Height);
            mismatches += Rect(text, "speed", design.SpeedFrame, 0, 0, hud.SpeedFrame.Width,
                hud.SpeedFrame.Height);
            mismatches += Rect(text, "messageFrame", design.MessageFrame, 200, 428 - 0xc,
                hud.MessageFrame.Width, hud.MessageFrame.Height);

            // Same formulas at the two scaling layers used by the captures.
            mismatches += CheckLayer(text, hud, model, 1280, 720, 1, 1280, 720);
            mismatches += CheckLayer(text, hud, model, 1920, 1080, 2, 960, 540);
            mismatches += CheckLayer(text, hud, model, 3840, 2160, 4, 960, 540);
            mismatches += CheckLayer(text, hud, model, 2560, 1440, 3, 852, 480);

            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_COUNTS mismatches=" + mismatches
                            + " missing=" + hud.Missing.Count
                            + " fontAvailable=" + (MatchHudFont.Available ? "True" : "False")
                            + " art=" + (hud.Period.Width + hud.TeamA.Width + hud.TeamB.Width));

            detail = text.ToString();
            return mismatches;
        }

        private static int CheckLayer(StringBuilder text, MatchHudOriginal hud, MatchHudModel model,
            int width, int height, int expectedScale, int expectedLayerWidth, int expectedLayerHeight)
        {
            MatchHudOriginal.Layout layout = hud.ComputeLayout(width, height, model);
            bool ok = layout.Scale == expectedScale
                      && layout.LayerWidth == expectedLayerWidth
                      && layout.LayerHeight == expectedLayerHeight
                      && layout.OriginX == (width - expectedLayerWidth * expectedScale) / 2
                      && layout.OriginY == (height - expectedLayerHeight * expectedScale) / 2;

            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_SCALE screen=" + width + "x" + height
                            + " layer=" + layout.LayerWidth + "x" + layout.LayerHeight
                            + " scale=" + layout.Scale
                            + " origin=" + layout.OriginX + "," + layout.OriginY
                            + " ok=" + (ok ? "True" : "False"));

            return ok ? 0 : 1;
        }

        private static int Expect(StringBuilder text, string role, MatchHudOriginal.Art art,
            int expectedWidth, int expectedHeight)
        {
            bool ok = art != null && art.Width == expectedWidth && art.Height == expectedHeight;
            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_SIZE role=" + role + " expected="
                            + expectedWidth + "x" + expectedHeight + " actual="
                            + (art == null ? "missing" : art.Width + "x" + art.Height)
                            + " ok=" + (ok ? "True" : "False"));
            return ok ? 0 : 1;
        }

        private static int Rect(StringBuilder text, string role, RectInt actual, int x, int y, int w, int h)
        {
            bool ok = actual.x == x && actual.y == y && actual.width == w && actual.height == h;
            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_RECT role=" + role + " expected="
                            + x + "," + y + "," + w + "x" + h + " actual=" + actual.x + ","
                            + actual.y + "," + actual.width + "x" + actual.height
                            + " ok=" + (ok ? "True" : "False"));
            return ok ? 0 : 1;
        }

        private static int Vec(StringBuilder text, string role, Vector2Int actual, int x, int y)
        {
            bool ok = actual.x == x && actual.y == y;
            text.AppendLine("UBLADE3_NATIVE_HUD_PROBE_POINT role=" + role + " expected=" + x + "," + y
                            + " actual=" + actual.x + "," + actual.y + " ok=" + (ok ? "True" : "False"));
            return ok ? 0 : 1;
        }

        private static string DescribeLayout(MatchHudOriginal.Layout layout)
        {
            StringBuilder text = new StringBuilder();
            text.Append("period=").Append(Format(layout.PeriodFrame));
            text.Append(" clock=").Append(layout.Clock.x).Append(',').Append(layout.Clock.y);
            text.Append(" teamA=").Append(Format(layout.TeamAFrame));
            text.Append(" teamB=").Append(Format(layout.TeamBFrame));
            text.Append(" homeScore=").Append(layout.HomeScore.x).Append(',').Append(layout.HomeScore.y);
            text.Append(" awayScore=").Append(layout.AwayScore.x).Append(',').Append(layout.AwayScore.y);
            text.Append(" rok=").Append(layout.RokValue.x).Append(',').Append(layout.RokValue.y);
            text.Append(" radar=").Append(Format(layout.RadarFrame));
            text.Append(" headCam=").Append(Format(layout.HeadCamFrame));
            text.Append(" damageA1=").Append(Format(layout.DamageA1));
            text.Append(" damageB1=").Append(Format(layout.DamageB1));
            return text.ToString();
        }

        private static string Format(RectInt rect)
        {
            return rect.x.ToString(CultureInfo.InvariantCulture) + ","
                   + rect.y.ToString(CultureInfo.InvariantCulture) + ","
                   + rect.width.ToString(CultureInfo.InvariantCulture) + "x"
                   + rect.height.ToString(CultureInfo.InvariantCulture);
        }
    }
}
