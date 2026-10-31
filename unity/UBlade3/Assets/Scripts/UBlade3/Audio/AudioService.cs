using System;
using System.Collections.Generic;
using UBlade3.Data;
using UnityEngine;

namespace UBlade3.Audio
{
    // The Unity player for the original HyperBlade waves. It reads nothing but the player's own
    // install: UBlade3.Data.GameDataRoot locates it and SoundCatalog joins Data\SNDFILES.LST to
    // the Data\Sounds files, then each wave is converted from its shipped PCM to an AudioClip
    // with WavFile.DecodeToFloat (8 bit unsigned or 16 bit signed, mono or stereo, at the wave's
    // own rate).
    //
    // The channel count mirrors the original mixer: DSOUND_CHANNEL_COUNT is eight
    // (src/sound/dsoundmgr.h), with looping for SNDFILES.LST group 666
    // (SOUND_GROUP_LOOPING; DSoundMgr::Play, HYPERX 0x00490780) and at most one ambient loop.
    //
    // This component is deliberately not wired into the match by an edit to another namespace's
    // files. Add it to a scene, call AudioBootstrap.Install(), or launch the player with
    // --ublade3-audio (see AudioBootstrap). The probe (AudioProbe) is the loud acceptance gate.
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        // src/sound/dsoundmgr.h: #define DSOUND_CHANNEL_COUNT 8.
        public const int ChannelCount = 8;

        // Log markers, kept stable for the native scripts and for docs/NATIVE-AUDIO.md.
        public const string ReadyMarker = "UBLADE3_AUDIO_SERVICE_READY";
        public const string ErrorMarker = "UBLADE3_AUDIO_SERVICE_ERROR";
        public const string ListenerMarker = "UBLADE3_AUDIO_LISTENER_ADDED";
        public const string MissingMarker = "UBLADE3_AUDIO_SOUND_MISSING";

        [Range(0.0f, 1.0f)]
        public float MasterVolume = 1.0f;

        private SoundCatalog catalog;
        private AudioSource[] channels;
        private AudioSource ambientSource;
        private AudioClip ambientClip;
        private readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
        private readonly List<string> warnings = new List<string>();
        private int nextChannel;
        private int nextImpact;
        private bool ready;
        private string lastError;

        public bool IsReady
        {
            get { return ready; }
        }

        public string LastError
        {
            get { return lastError ?? string.Empty; }
        }

        public SoundCatalog Catalog
        {
            get { return catalog; }
        }

        public int CacheCount
        {
            get { return clips.Count; }
        }

        // The shipped-install gaps SoundCatalog found, as one-line warnings (missing list entries,
        // orphan waves). The probe reports the same defects with counts.
        public List<string> Warnings
        {
            get { return warnings; }
        }

        private void Awake()
        {
            Initialize();
        }

        // Resolves the install, loads SNDFILES.LST and prepares the eight mixer channels. Returns
        // false and records LastError when the original data cannot be found; the component stays
        // inert rather than substituting placeholder audio.
        public bool Initialize()
        {
            if (ready)
            {
                return true;
            }

            try
            {
                catalog = SoundCatalog.Load();

                for (int i = 0; i < catalog.MissingEntries.Count; i++)
                {
                    warnings.Add("SNDFILES.LST entry \"" + catalog.MissingEntries[i] + "\" has no wave file on this install");
                }

                for (int i = 0; i < catalog.OrphanFiles.Count; i++)
                {
                    warnings.Add("wave file \"" + catalog.OrphanFiles[i] + "\" is not named by SNDFILES.LST");
                }

                EnsureListener();
                CreateChannels();

                ready = true;
                Debug.Log(ReadyMarker + " entries=" + catalog.List.Entries.Count + " files=" + catalog.FileCount);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        // Plays one AudioRole. Missing or unreadable files are reported once and answer false; the
        // component never throws into a game frame.
        public bool PlayRole(AudioRole role)
        {
            return role != null && PlayIndex(role.Index);
        }

        public bool PlayMenuUi()
        {
            return PlayRole(AudioRole.MenuUi);
        }

        // The original hit route: listen.pbn's id_MSG_Hit calls id_ACF_PlaySndType(0), i.e.
        // AudioMgr::PlayGroup(0), and SoundManager::PickFromGroup alternates the group's first two
        // entries. This mirrors that alternation for group 0 (Sxhhite2 / Sxhhitf2).
        public bool PlayImpact()
        {
            AudioRole role = (nextImpact & 1) == 0 ? AudioRole.ImpactFirst : AudioRole.ImpactSecond;

            nextImpact++;

            return PlayRole(role);
        }

        // ngoal.pbn/sgoal.pbn's id_DCF_RokScoreValue arms play id_ACF_PlaySound(74/75/76) for a
        // one, two or four point goal (AudioRole.GoalOnePoint/GoalTwoPoint/GoalFourPoint).
        public bool PlayGoal(int scoreValue)
        {
            switch (scoreValue)
            {
                case 1:
                    return PlayRole(AudioRole.GoalOnePoint);

                case 2:
                    return PlayRole(AudioRole.GoalTwoPoint);

                case 4:
                    return PlayRole(AudioRole.GoalFourPoint);

                default:
                    return PlayRole(AudioRole.GoalOnePoint);
            }
        }

        // The crowd answer the goal object sends with id_MSG_PlayCrowdResponse: south goals cheer
        // (listen.pbn data 0-2 -> Sxcrwdc2..), north goals boo (data 3-5 -> Sxcbooa2..).
        public bool PlayGoalCrowd(bool southGoal)
        {
            return PlayRole(southGoal ? AudioRole.CrowdGoalCheer : AudioRole.CrowdGoalBoo);
        }

        // Starts the looping crowd ambience. SNDFILES.LST group 666 is SOUND_GROUP_LOOPING and
        // DSoundMgr::Play starts such an entry with the looping flag set.
        public bool PlayCrowdAmbient()
        {
            if (!ready)
            {
                return false;
            }

            AudioClip clip;

            if (!TryGetClip(AudioRole.CrowdAmbient.Index, out clip))
            {
                return false;
            }

            if (ambientSource == null)
            {
                ambientSource = gameObject.AddComponent<AudioSource>();
                ambientSource.playOnAwake = false;
                ambientSource.loop = true;
                ambientSource.spatialBlend = 0.0f;
            }

            if (ambientClip != clip)
            {
                ambientClip = clip;
                ambientSource.clip = clip;
            }

            ambientSource.volume = MasterVolume;

            if (!ambientSource.isPlaying)
            {
                ambientSource.Play();
            }

            return true;
        }

        public void StopCrowdAmbient()
        {
            if (ambientSource != null)
            {
                ambientSource.Stop();
            }
        }

        public bool PlayIndex(int index)
        {
            if (!ready)
            {
                ReportMissing("index " + index + ": audio service is not ready (" + LastError + ")");
                return false;
            }

            AudioClip clip;

            if (!TryGetClip(index, out clip))
            {
                return false;
            }

            AudioSource channel = NextChannel();

            channel.volume = MasterVolume;
            channel.PlayOneShot(clip);

            return true;
        }

        public bool PlayName(string soundListName)
        {
            if (!ready)
            {
                ReportMissing("name " + soundListName + ": audio service is not ready (" + LastError + ")");
                return false;
            }

            SoundListEntry entry = catalog.List.FindEntry(soundListName);

            if (entry == null)
            {
                ReportMissing("name " + soundListName + ": no SNDFILES.LST entry");
                return false;
            }

            return PlayIndex(entry.Index);
        }

        public void SetMasterVolume(float volume)
        {
            MasterVolume = Mathf.Clamp01(volume);

            if (channels != null)
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    channels[i].volume = MasterVolume;
                }
            }

            if (ambientSource != null)
            {
                ambientSource.volume = MasterVolume;
            }
        }

        public bool TryGetClip(int index, out AudioClip clip)
        {
            clip = null;

            if (clips.TryGetValue(index, out clip))
            {
                return true;
            }

            WavFile wave;
            string error;

            if (!catalog.TryLoadWave(index, out wave, out error))
            {
                ReportMissing("index " + index + ": " + error);
                return false;
            }

            float[] samples;

            try
            {
                samples = wave.DecodeToFloat();
            }
            catch (Exception exception)
            {
                ReportMissing("index " + index + ": " + exception.Message);
                return false;
            }

            // Wave files are short effects (longest about 15 seconds); decoding to an AudioClip is
            // the Unity equivalent of AudioMgr::LoadSound's whole-file load mode. AudioClip.Create
            // takes samples per channel and the interleaved float data.
            clip = AudioClip.Create(
                "ublade3_" + index + "_" + SafeClipName(wave.SourceName),
                wave.FrameCount,
                wave.Channels,
                wave.SampleRate,
                false);

            if (!clip.SetData(samples, 0))
            {
                Destroy(clip);
                clip = null;
                ReportMissing("index " + index + ": AudioClip.SetData failed");
                return false;
            }

            clips.Add(index, clip);
            return true;
        }

        private static string SafeClipName(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path ?? string.Empty);

            if (name.Length == 0)
            {
                name = "wave";
            }

            char[] chars = name.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private void CreateChannels()
        {
            channels = new AudioSource[ChannelCount];

            for (int i = 0; i < channels.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();

                source.playOnAwake = false;
                source.loop = false;
                // The original mixes stereo pan/volume only; there is no 3D attenuation, so the
                // Unity channels are 2D like the DirectSound buffers.
                source.spatialBlend = 0.0f;
                source.volume = MasterVolume;
                channels[i] = source;
            }
        }

        private AudioSource NextChannel()
        {
            AudioSource source = channels[nextChannel & (ChannelCount - 1)];

            nextChannel++;

            return source;
        }

        private void EnsureListener()
        {
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null)
            {
                return;
            }

            gameObject.AddComponent<AudioListener>();
            Debug.Log(ListenerMarker);
        }

        private void ReportMissing(string message)
        {
            Debug.LogWarning(MissingMarker + " " + message);
        }

        private void Fail(Exception exception)
        {
            ready = false;
            lastError = exception.Message;
            Debug.LogError(ErrorMarker + " " + exception.Message);
        }

        private void OnDestroy()
        {
            StopCrowdAmbient();

            foreach (KeyValuePair<int, AudioClip> pair in clips)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            clips.Clear();
            ambientClip = null;
        }
    }
}
