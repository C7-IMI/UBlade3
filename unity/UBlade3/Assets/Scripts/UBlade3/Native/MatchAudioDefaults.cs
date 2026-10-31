using System;
using UnityEngine;
using UBlade3.Audio;

namespace UBlade3.Native
{
    // N13: the original-wave audio plays by default in the interactive native C# player. The
    // explicit --ublade3-audio / UBLADE3_AUDIO=1 contract stays as the opt-in path for capture and
    // tooling; this default only installs for a real interactive player (not -batchmode and not one
    // of the deterministic capture/probe entry points). UBLADE3_AUDIO=0 opts out for troubleshooting.
    public static class MatchAudioDefaults
    {
        public const string OptOutEnvironmentVariable = "UBLADE3_AUDIO";

        public const string EnabledMarker = "UBLADE3_MATCH_AUDIO_DEFAULT";

        public static AudioService Service { get; private set; }

        public static bool Attempted { get; private set; }

        public static bool Enabled { get; private set; }

        public static string Detail = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallForInteractivePlayer()
        {
            if (!Application.isPlaying || !ShouldUseDefault())
            {
                return;
            }

            Attempted = true;

            try
            {
                // The audio layer resolves the install through GameDataRoot; give it the same
                // install the match layer already resolved before creating the service.
                var root = MatchScene.ResolveRoot();
                Environment.SetEnvironmentVariable("HYPERBLADE_GAME_DATA", root.InstallPath);

                Service = AudioBootstrap.InstallAudio();

                if (Service != null && !Service.IsReady)
                {
                    Service.Initialize();
                }

                Enabled = Service != null && Service.IsReady;
                Detail = DescribeAudioDevice();

                Debug.Log(EnabledMarker + " enabled=" + (Enabled ? "True" : "False")
                          + " service=" + (Service == null ? "null" : "present")
                          + " ready=" + (Service != null && Service.IsReady ? "True" : "False")
                          + " channelCount=" + AudioService.ChannelCount
                          + " cache=" + (Service == null ? 0 : Service.CacheCount)
                          + " warnings=" + (Service == null ? 0 : Service.Warnings.Count)
                          + " detail=\"" + Detail + "\"");
            }
            catch (Exception exception)
            {
                Enabled = false;
                Detail = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning(EnabledMarker + " enabled=False reason=" + Detail);
            }
        }

        public static bool ShouldUseDefault()
        {
            if (Application.isBatchMode)
            {
                return false;
            }

            string optOut = Environment.GetEnvironmentVariable(OptOutEnvironmentVariable);

            if (string.Equals(optOut, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(optOut, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];

                if (argument.StartsWith("--play-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--n7-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--hud-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--display-probe=", StringComparison.Ordinal)
                    || argument.StartsWith("--gameplay-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--match-capture=", StringComparison.Ordinal)
                    || argument.StartsWith("--n13-capture=", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryGetService(out AudioService service)
        {
            if (Service != null)
            {
                service = Service;
                return true;
            }

            if (UnityEngine.Object.FindFirstObjectByType<AudioService>() != null)
            {
                Service = UnityEngine.Object.FindFirstObjectByType<AudioService>();
                service = Service;
                return true;
            }

            service = null;
            return false;
        }

        public static string DescribeAudioDevice()
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            return "outputSampleRate=" + AudioSettings.outputSampleRate
                   + " speakerMode=" + config.speakerMode
                   + " dspBufferSize=" + config.dspBufferSize
                   + " realVoices=" + config.numRealVoices
                   + " virtualVoices=" + config.numVirtualVoices;
        }

        public static string Describe()
        {
            return "attempted=" + (Attempted ? "True" : "False")
                   + " enabled=" + (Enabled ? "True" : "False")
                   + " ready=" + (Service != null && Service.IsReady ? "True" : "False")
                   + " " + DescribeAudioDevice()
                   + " detail=\"" + Detail + "\"";
        }
    }
}
