using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UBlade3.Native
{
    // The persisted display settings. The file is small JSON so it is inspectable and versioned; a
    // missing/corrupt file falls back to the default preset instead of throwing.
    [Serializable]
    public sealed class MatchDisplaySettings
    {
        public const int CurrentVersion = 1;

        public const int DefaultWidth = 1280;
        public const int DefaultHeight = 720;
        public const string DefaultMode = "windowed";
        public const string DefaultPreset = "1280x720";

        public int Version = CurrentVersion;
        public int Width = DefaultWidth;
        public int Height = DefaultHeight;
        public string Mode = DefaultMode;
        public string Preset = DefaultPreset;

        public MatchDisplaySettings Clone()
        {
            MatchDisplaySettings copy = new MatchDisplaySettings();
            copy.Version = Version;
            copy.Width = Width;
            copy.Height = Height;
            copy.Mode = Mode;
            copy.Preset = Preset;
            return copy;
        }

        public static MatchDisplaySettings Default()
        {
            return new MatchDisplaySettings();
        }

        // The useful presets requested by the milestone. "native" follows the current desktop.
        public static readonly MatchDisplayPreset[] Presets =
        {
            new MatchDisplayPreset("1280x720", "720p", 1280, 720, false),
            new MatchDisplayPreset("1920x1080", "1080p", 1920, 1080, false),
            new MatchDisplayPreset("2560x1440", "QHD/2K", 2560, 1440, false),
            new MatchDisplayPreset("3840x2160", "4K UHD", 3840, 2160, false),
            new MatchDisplayPreset("native", "Native desktop", 0, 0, true),
        };

        public static readonly string[] Modes = { "windowed", "borderless", "fullscreen" };

        public static bool TryParseResolution(string text, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string value = text.Trim().ToLowerInvariant();

            if (value == "native")
            {
                return true;
            }

            int index = value.IndexOf('x');

            if (index <= 0 || index >= value.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(value.Substring(0, index), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out width))
            {
                return false;
            }

            if (!int.TryParse(value.Substring(index + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out height))
            {
                return false;
            }

            return width >= 320 && height >= 200 && width <= 16384 && height <= 16384;
        }

        public static string NormalizeMode(string mode)
        {
            if (string.IsNullOrEmpty(mode))
            {
                return DefaultMode;
            }

            string value = mode.Trim().ToLowerInvariant();

            if (value == "borderless" || value == "fullscreenwindow" || value == "fullscreen-window")
            {
                return "borderless";
            }

            if (value == "fullscreen" || value == "exclusive")
            {
                return "fullscreen";
            }

            return "windowed";
        }

        public static MatchDisplayPreset FindPreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
            {
                return Presets[0];
            }

            string value = preset.Trim().ToLowerInvariant();

            for (int i = 0; i < Presets.Length; i++)
            {
                if (string.Equals(Presets[i].Id, value, StringComparison.OrdinalIgnoreCase))
                {
                    return Presets[i];
                }
            }

            return Presets[0];
        }

        public MatchDisplaySettings ApplyPreset(string preset)
        {
            MatchDisplayPreset found = FindPreset(preset);
            Preset = found.Id;
            Width = found.Width;
            Height = found.Height;
            return this;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static bool TryFromJson(string json, out MatchDisplaySettings settings, out string error)
        {
            settings = Default();
            error = string.Empty;

            if (string.IsNullOrEmpty(json))
            {
                error = "empty";
                return false;
            }

            try
            {
                MatchDisplaySettings parsed = JsonUtility.FromJson<MatchDisplaySettings>(json);

                if (parsed == null)
                {
                    error = "null";
                    return false;
                }

                if (parsed.Width <= 0 || parsed.Height <= 0)
                {
                    error = "invalid-size";
                    return false;
                }

                parsed.Mode = NormalizeMode(parsed.Mode);
                parsed.Preset = string.IsNullOrEmpty(parsed.Preset) ? "custom" : parsed.Preset;
                settings = parsed;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public static MatchDisplaySettings Load(string path, out string detail)
        {
            detail = string.Empty;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                detail = "not-found";
                return Default();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);

                if (TryFromJson(json, out MatchDisplaySettings settings, out string error))
                {
                    detail = "loaded";
                    return settings;
                }

                detail = "invalid(" + error + ")";
                return Default();
            }
            catch (Exception exception)
            {
                detail = "read-error(" + exception.GetType().Name + ")";
                return Default();
            }
        }

        public static bool Save(string path, MatchDisplaySettings settings, out string detail)
        {
            detail = string.Empty;

            if (string.IsNullOrEmpty(path) || settings == null)
            {
                detail = "empty-path";
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, settings.ToJson(), new UTF8Encoding(false));
                detail = "saved";
                return true;
            }
            catch (Exception exception)
            {
                detail = "write-error(" + exception.GetType().Name + ")";
                return false;
            }
        }

        public string Describe()
        {
            return "preset=" + Preset + " size=" + Width + "x" + Height + " mode=" + Mode;
        }
    }

    public struct MatchDisplayPreset
    {
        public readonly string Id;
        public readonly string Label;
        public readonly int Width;
        public readonly int Height;
        public readonly bool Native;

        public MatchDisplayPreset(string id, string label, int width, int height, bool native)
        {
            Id = id;
            Label = label;
            Width = width;
            Height = height;
            Native = native;
        }
    }

    // Pure validation: requested -> applied. Unsupported fullscreen/borderless sizes fall back to the
    // largest supported resolution no larger than the request (or the desktop); windowed sizes larger
    // than the desktop are clamped. This keeps 1440p/4K settings parseable and persisted on a 1080p
    // host while the actual window is a valid supported/desktop size, and logs the exact fallback.
    public static class MatchDisplayPlan
    {
        public struct Result
        {
            public int Width;
            public int Height;
            public string Mode;
            public string Preset;
            public bool Fallback;
            public string Reason;
        }

        public static Result Validate(MatchDisplaySettings requested, Resolution[] supported,
            Resolution desktop, string presetOverride)
        {
            Result result = new Result();
            result.Mode = MatchDisplaySettings.NormalizeMode(requested.Mode);
            result.Preset = string.IsNullOrEmpty(presetOverride) ? requested.Preset : presetOverride;
            result.Reason = string.Empty;

            int width = requested.Width;
            int height = requested.Height;

            MatchDisplayPreset preset = MatchDisplaySettings.FindPreset(result.Preset);

            if (preset.Native || width <= 0 || height <= 0)
            {
                width = desktop.width;
                height = desktop.height;
            }

            int desktopWidth = desktop.width > 0 ? desktop.width : MatchDisplaySettings.DefaultWidth;
            int desktopHeight = desktop.height > 0 ? desktop.height : MatchDisplaySettings.DefaultHeight;

            if (result.Mode == "windowed")
            {
                if (width > desktopWidth || height > desktopHeight)
                {
                    width = Math.Min(width, desktopWidth);
                    height = Math.Min(height, desktopHeight);
                    result.Fallback = true;
                    result.Reason = "windowed-larger-than-desktop";
                }
            }
            else
            {
                bool exact = ResolutionMatches(supported, width, height);

                if (!exact)
                {
                    // A borderless/fullscreen request can never exceed the desktop; fall back to
                    // the largest supported mode that fits the desktop as well as the request.
                    Resolution fallback = BestSupported(supported, Math.Min(width, desktopWidth),
                        Math.Min(height, desktopHeight), desktopWidth, desktopHeight);

                    result.Fallback = true;
                    result.Reason = "unsupported-mode-resolution";

                    if (fallback.width > 0 && fallback.height > 0)
                    {
                        width = fallback.width;
                        height = fallback.height;
                    }
                    else
                    {
                        width = desktopWidth;
                        height = desktopHeight;
                    }
                }
            }

            if (width < 640)
            {
                width = 640;
                result.Fallback = true;
                result.Reason = "minimum-width";
            }

            if (height < 360)
            {
                height = 360;
                result.Fallback = true;
                result.Reason = "minimum-height";
            }

            result.Width = width;
            result.Height = height;
            return result;
        }

        public static bool ResolutionMatches(Resolution[] supported, int width, int height)
        {
            if (supported == null)
            {
                return false;
            }

            for (int i = 0; i < supported.Length; i++)
            {
                if (supported[i].width == width && supported[i].height == height)
                {
                    return true;
                }
            }

            return false;
        }

        public static Resolution BestSupported(Resolution[] supported, int requestedWidth,
            int requestedHeight, int desktopWidth, int desktopHeight)
        {
            if (supported == null || supported.Length == 0)
            {
                return new Resolution { width = desktopWidth, height = desktopHeight };
            }

            Resolution best = new Resolution();
            long bestArea = -1;
            int maxWidth = Math.Max(requestedWidth, desktopWidth);
            int maxHeight = Math.Max(requestedHeight, desktopHeight);

            for (int i = 0; i < supported.Length; i++)
            {
                Resolution candidate = supported[i];

                if (candidate.width > maxWidth || candidate.height > maxHeight)
                {
                    continue;
                }

                long area = (long)candidate.width * candidate.height;

                if (area > bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }

            if (best.width == 0)
            {
                return new Resolution { width = desktopWidth, height = desktopHeight };
            }

            return best;
        }
    }
}
