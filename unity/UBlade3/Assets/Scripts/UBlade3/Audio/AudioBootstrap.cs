using System;
using UnityEngine;

namespace UBlade3.Audio
{
    // The bounded, clearly labelled audio hook for the native C# match. It is off by default and
    // only installs an AudioService when the player is explicitly asked to:
    //
    //   environment variable UBLADE3_AUDIO=1
    //   or command line switch --ublade3-audio
    //
    // In every other run (including the headless match and capture modes) this class does
    // nothing, so existing captures and the release path are untouched. The match code is not
    // edited: a future N6 scene can also call InstallAudio() directly, or put an AudioService on
    // a GameObject itself.
    public static class AudioBootstrap
    {
        public const string EnvironmentVariable = "UBLADE3_AUDIO";

        public const string CommandLineSwitch = "--ublade3-audio";

        public const string HostObjectName = "UBlade3 Audio";

        public const string InstalledMarker = "UBLADE3_AUDIO_HOOK_INSTALLED";

        // AfterSceneLoad: the build scene's NativeAudioListener (Editor/UBlade3Build.cs) already
        // exists by then, so the service finds an AudioListener instead of adding a duplicate.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!Application.isPlaying || !IsRequested())
            {
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<AudioService>() != null)
            {
                return;
            }

            AudioService service = InstallAudio();

            Debug.Log(InstalledMarker + " source=" + (Environment.GetEnvironmentVariable(EnvironmentVariable) == "1" ? "environment" : "command line")
                      + " ready=" + service.IsReady);
        }

        // True when the run explicitly opted into the bounded audio hook.
        public static bool IsRequested()
        {
            if (string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], CommandLineSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Adds an AudioService to the active scene and returns it (or the existing one). Safe to
        // call from a future N6 scene setup; the probe verifies the same load path independently.
        public static AudioService InstallAudio()
        {
            AudioService existing = UnityEngine.Object.FindFirstObjectByType<AudioService>();

            if (existing != null)
            {
                return existing;
            }

            GameObject host = new GameObject(HostObjectName);

            return host.AddComponent<AudioService>();
        }
    }
}
