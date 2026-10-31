using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UBlade3.Native
{
    // The original HUD text was Windows GDI "MS Sans Serif" (infobar.cpp:970
    // g_abInfoBarFontName; CDDText::CreateFontFace 0x0044fc10 calls
    // CreateFontA(height, 0, 0, 0, FW_MEDIUM, 0, 0, 0, 0, 0, 0,
    //             NONANTIALIASED_QUALITY, VARIABLE_PITCH, "MS Sans Serif")
    // and measures with GetTextExtentPoint32A). The face is not shipped with the game, so this
    // bounded port renders the real installed Windows face at runtime through the same GDI calls.
    // No font file or glyph asset is copied into the repository; if the face is unavailable the N12
    // HUD reports a gap and draws no text rather than substituting an unrelated face.
    public static class MatchHudFont
    {
        public const int Timer = 9;
        public const int Label = 10;
        public const int Pair = 11;
        public const int Body = 15;
        public const int Large = 20;

        public static readonly int[] Sizes = { Timer, Label, Pair, Body, Large };

        public const string FaceName = "MS Sans Serif";

        private const int FwMedium = 500;
        private const int NonantialiasedQuality = 3;
        private const int VariablePitch = 2;
        private const int Transparent = 1;
        private const int BiRgb = 0;
        private const uint DibRgbColors = 0;

        private static bool initialized;
        private static bool available;
        private static string actualFace = string.Empty;
        private static string error = string.Empty;
        private static IntPtr memoryDc;
        private static readonly Dictionary<int, IntPtr> fonts = new Dictionary<int, IntPtr>();

        [StructLayout(LayoutKind.Sequential)]
        private struct Size32
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateFontA(int height, int width, int escapement, int orientation,
            int weight, int italic, int underline, int strikeOut, int charSet, int outPrecision,
            int clipPrecision, int quality, int pitchAndFamily, string faceName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetTextFaceA(IntPtr hdc, int count, StringBuilder faceName);

        [DllImport("gdi32.dll")]
        private static extern bool GetTextExtentPoint32A(IntPtr hdc, string text, int count, out Size32 size);

        [DllImport("gdi32.dll")]
        private static extern bool TextOutA(IntPtr hdc, int x, int y, string text, int count);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern int SetTextColor(IntPtr hdc, int color);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        public static bool Available
        {
            get
            {
                EnsureInitialized();
                return available;
            }
        }

        public static string ActualFace
        {
            get
            {
                EnsureInitialized();
                return actualFace;
            }
        }

        public static string LastError
        {
            get
            {
                EnsureInitialized();
                return error;
            }
        }

        public static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            try
            {
                memoryDc = CreateCompatibleDC(IntPtr.Zero);

                if (memoryDc == IntPtr.Zero)
                {
                    error = "CreateCompatibleDC failed";
                    return;
                }

                for (int i = 0; i < Sizes.Length; i++)
                {
                    IntPtr font = CreateFontA(Sizes[i], 0, 0, 0, FwMedium, 0, 0, 0, 0, 0, 0,
                        NonantialiasedQuality, VariablePitch, FaceName);

                    if (font == IntPtr.Zero)
                    {
                        error = "CreateFontA failed for height " + Sizes[i];
                        return;
                    }

                    fonts[Sizes[i]] = font;
                }

                IntPtr old = SelectObject(memoryDc, fonts[Pair]);
                StringBuilder face = new StringBuilder(128);
                GetTextFaceA(memoryDc, face.Capacity, face);
                actualFace = face.ToString();
                SelectObject(memoryDc, old);

                available = actualFace.IndexOf(FaceName, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!available)
                {
                    error = "installed face is \"" + actualFace + "\", not \"" + FaceName + "\"";
                }

                Debug.Log("UBLADE3_NATIVE_HUD_FONT requested=\"" + FaceName + "\" actual=\"" + actualFace
                          + "\" available=" + (available ? "True" : "False")
                          + " sizes=9,10,11,15,20 quality=NONANTIALIASED weight=FW_MEDIUM pitch=VARIABLE"
                          + (available ? string.Empty : " error=" + error));
            }
            catch (Exception exception)
            {
                available = false;
                error = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("UBLADE3_NATIVE_HUD_FONT_FAILED " + error);
            }
        }

        private static bool SelectFont(int height, out IntPtr old)
        {
            EnsureInitialized();
            old = IntPtr.Zero;

            IntPtr font;

            if (!available || !fonts.TryGetValue(height, out font))
            {
                return false;
            }

            old = SelectObject(memoryDc, font);
            return true;
        }

        public static bool Measure(string text, int height, out int width, out int heightPixels)
        {
            width = 0;
            heightPixels = 0;
            IntPtr old;

            if (!SelectFont(height, out old))
            {
                return false;
            }

            try
            {
                Size32 size;

                if (!GetTextExtentPoint32A(memoryDc, text ?? string.Empty, (text ?? string.Empty).Length, out size))
                {
                    return false;
                }

                width = size.cx;
                heightPixels = size.cy;
                return true;
            }
            finally
            {
                SelectObject(memoryDc, old);
            }
        }

        public static bool Draw(Color32[] layer, int layerWidth, int layerHeight, int x, int y,
            string text, int height, Color32 colour)
        {
            if (string.IsNullOrEmpty(text) || layer == null)
            {
                return false;
            }

            int width;
            int textHeight;

            if (!Measure(text, height, out width, out textHeight) || width <= 0 || textHeight <= 0)
            {
                return false;
            }

            IntPtr old;

            if (!SelectFont(height, out old))
            {
                return false;
            }

            int dibWidth = width + 4;
            int dibHeight = textHeight + 4;
            BitmapInfo info = new BitmapInfo();
            info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
            info.bmiHeader.biWidth = dibWidth;
            info.bmiHeader.biHeight = -dibHeight; // top-down
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = BiRgb;
            info.bmiHeader.biSizeImage = (uint)(dibWidth * dibHeight * 4);
            info.bmiHeader.biClrUsed = DibRgbColors;

            IntPtr bits = IntPtr.Zero;
            IntPtr dib = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                dib = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);

                if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    return false;
                }

                oldBitmap = SelectObject(memoryDc, dib);

                // black background; TextOut with transparent background leaves it untouched, so
                // every non-zero RGB pixel is glyph coverage (the face is non-antialiased).
                for (int i = 0; i < dibWidth * dibHeight; i++)
                {
                    Marshal.WriteInt32(bits, i * 4, 0);
                }

                SetBkMode(memoryDc, Transparent);
                SetTextColor(memoryDc, 0x00ffffff);
                TextOutA(memoryDc, 2, 2, text, text.Length);

                for (int py = 0; py < dibHeight; py++)
                {
                    int targetY = y + py - 2;

                    if (targetY < 0 || targetY >= layerHeight)
                    {
                        continue;
                    }

                    for (int px = 0; px < dibWidth; px++)
                    {
                        int targetX = x + px - 2;

                        if (targetX < 0 || targetX >= layerWidth)
                        {
                            continue;
                        }

                        int bgra = Marshal.ReadInt32(bits, ((py * dibWidth) + px) * 4);
                        byte coverage = (byte)(Math.Max(bgra & 0xff,
                            Math.Max((bgra >> 8) & 0xff, (bgra >> 16) & 0xff)));

                        if (coverage == 0)
                        {
                            continue;
                        }

                        int index = (targetY * layerWidth) + targetX;
                        Color32 source = new Color32(colour.r, colour.g, colour.b, coverage);
                        layer[index] = source;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }

                if (dib != IntPtr.Zero)
                {
                    DeleteObject(dib);
                }

                SelectObject(memoryDc, old);
            }
        }

        public static string Describe()
        {
            EnsureInitialized();
            StringBuilder text = new StringBuilder();
            text.Append("requested=\"").Append(FaceName).Append("\" actual=\"").Append(actualFace)
                .Append("\" available=").Append(available ? "True" : "False");

            if (available)
            {
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int width;
                    int height;
                    Measure("0:00", Sizes[i], out width, out height);
                    text.Append(" h").Append(Sizes[i]).Append("=\"0:00\"=>").Append(width).Append('x').Append(height);
                }
            }
            else
            {
                text.Append(" error=").Append(error);
            }

            return text.ToString();
        }
    }
}
