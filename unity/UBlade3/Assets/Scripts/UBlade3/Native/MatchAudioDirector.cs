using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UBlade3.Audio;

namespace UBlade3.Native
{
    // Routes the real match events to the original-wave player. Everything it calls is the existing
    // UBlade3.Audio.AudioService / AudioRole mapping (goal id 74/75/76 by score value, crowd cheer 18
    // for a south goal and boo 21 for a north goal, alternating hit group 0, menu click 289, looping
    // crowd 15). The service is optional: the headless gate constructs this with a null service and
    // only records the event plan, which is exactly what the capture frames can compare against.
    public sealed class MatchAudioDirector
    {
        public const string EventMarker = "UBLADE3_MATCH_AUDIO_EVENT";
        public const string CountsMarker = "UBLADE3_MATCH_AUDIO_COUNTS";
        public const string UnavailableMarker = "UBLADE3_MATCH_AUDIO_UNAVAILABLE";

        public readonly AudioService Service;

        public bool Ready
        {
            get { return Service != null && Service.IsReady; }
        }

        public int RequestedEvents;
        public int PlayedCalls;
        public int GoalEvents;
        public int CrowdEvents;
        public int ImpactEvents;
        public int PickupEvents;
        public int ShotEvents;
        public int PassEvents;
        public int FallbackImpacts;
        public int MenuEvents;
        public int AmbientStarts;
        public int FailedCalls;

        public readonly List<string> Events = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public MatchAudioDirector(AudioService service)
        {
            // N13: the interactive player installs the default service before the scene builds; a
            // director constructed by MatchPlay with no explicit service picks it up here so the
            // real match events play by default. Capture/deterministic paths never install it.
            if (service == null)
            {
                AudioService discovered;
                if (MatchAudioDefaults.TryGetService(out discovered))
                {
                    service = discovered;
                }
            }

            Service = service;

            if (Service == null)
            {
                Debug.Log(UnavailableMarker + " reason=no-service (record-only event plan)");
            }
            else if (!Service.IsReady)
            {
                Debug.Log(UnavailableMarker + " reason=" + Service.LastError);
            }
            else
            {
                Debug.Log("UBLADE3_MATCH_AUDIO_SERVICE ready=True channels=" + AudioService.ChannelCount
                          + " cached=" + Service.CacheCount + " warnings=" + Service.Warnings.Count);
            }
        }

        public void Goal(int points, bool southGoal, int homeScore, int awayScore, uint tickMs)
        {
            RequestedEvents++;
            GoalEvents++;
            CrowdEvents++;

            bool goalPlayed = false;
            bool crowdPlayed = false;

            if (Ready)
            {
                goalPlayed = Service.PlayGoal(points);
                crowdPlayed = Service.PlayGoalCrowd(southGoal);
                PlayedCalls += (goalPlayed ? 1 : 0) + (crowdPlayed ? 1 : 0);

                if (!goalPlayed || !crowdPlayed)
                {
                    FailedCalls++;
                }
            }

            Log(EventMarker + " kind=goal points=" + points
                + " goalSide=" + (southGoal ? "south-home-scores" : "north-away-scores")
                + " audioRole=" + (points == 1 ? "goal-1x" : points == 2 ? "goal-2x" : "goal-4x")
                + " crowdRole=" + (southGoal ? "crowd-goal-cheer" : "crowd-goal-boo")
                + " played=" + (goalPlayed ? "True" : "False") + "/" + (crowdPlayed ? "True" : "False")
                + " homeScore=" + homeScore + " awayScore=" + awayScore
                + " tickMs=" + tickMs);
        }

        // The real event routes: SNDFILES.LST group 1 catches (Sxrctha3) for a ROK pickup, group 2
        // shoots/throws (Sxthrwc4 shot, Sxthrwc5 pass) for a release, and the original alternating
        // hit group 0 for an arena bounce/other impact. If a named original entry is missing the
        // event falls back to the original hit group and says so in the log.
        public void Impact(string reason, uint tickMs)
        {
            RequestedEvents++;
            // Compatibility aggregate for the existing N6-N12 checks and the event plan: every
            // physical event (pickup/shot/pass/bounce) remains an "impact" event, while the specific
            // counters below identify the original sound route.
            ImpactEvents++;

            string lower = string.IsNullOrEmpty(reason) ? string.Empty : reason.ToLowerInvariant();
            string kind = "impact";
            string audioRole = "impact-first/second";
            string soundName = null;
            bool played = false;
            bool fallbackUsed = false;

            if (lower.Contains("pickup"))
            {
                kind = "pickup";
                PickupEvents++;
                audioRole = "catch-group-1";
                soundName = "Sxrctha3";
            }
            else if (lower.Contains("shot-release"))
            {
                kind = "shot";
                ShotEvents++;
                audioRole = "shoot-group-2";
                soundName = "Sxthrwc4";
            }
            else if (lower.Contains("pass-release"))
            {
                kind = "pass";
                PassEvents++;
                audioRole = "throw-group-2";
                soundName = "Sxthrwc5";
            }
            if (Ready)
            {
                if (soundName != null)
                {
                    played = Service.PlayName(soundName);

                    if (!played)
                    {
                        FallbackImpacts++;
                        fallbackUsed = true;
                        played = Service.PlayImpact();
                    }
                }
                else
                {
                    played = Service.PlayImpact();
                }

                PlayedCalls += played ? 1 : 0;

                if (!played)
                {
                    FailedCalls++;
                }
            }

            Log(EventMarker + " kind=" + kind + " reason=" + reason
                + " audioRole=" + (soundName == null ? audioRole : soundName + "(" + audioRole + ")")
                + " fallbackImpact=" + (fallbackUsed ? "True" : "False")
                + " played=" + (played ? "True" : "False")
                + " tickMs=" + tickMs);
        }

        public void Menu(string reason, uint tickMs)
        {
            RequestedEvents++;
            MenuEvents++;

            bool played = false;

            if (Ready)
            {
                played = Service.PlayMenuUi();
                PlayedCalls += played ? 1 : 0;

                if (!played)
                {
                    FailedCalls++;
                }
            }

            Log(EventMarker + " kind=menu reason=" + reason
                + " audioRole=menu-ui played=" + (played ? "True" : "False") + " tickMs=" + tickMs);
        }

        public void AmbientStart()
        {
            RequestedEvents++;
            AmbientStarts++;

            bool played = false;

            if (Ready)
            {
                played = Service.PlayCrowdAmbient();
                PlayedCalls += played ? 1 : 0;

                if (!played)
                {
                    FailedCalls++;
                }
            }

            Log(EventMarker + " kind=ambient loop=crowd-ambient played=" + (played ? "True" : "False"));
        }

        public void AmbientStop()
        {
            if (Ready)
            {
                Service.StopCrowdAmbient();
            }
        }

        private void Log(string line)
        {
            Events.Add(line);
            Debug.Log(line);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(CountsMarker);
            text.Append(" requested=").Append(RequestedEvents);
            text.Append(" played=").Append(PlayedCalls);
            text.Append(" failed=").Append(FailedCalls);
            text.Append(" goal=").Append(GoalEvents);
            text.Append(" crowd=").Append(CrowdEvents);
            text.Append(" impact=").Append(ImpactEvents);
            text.Append(" pickup=").Append(PickupEvents);
            text.Append(" shot=").Append(ShotEvents);
            text.Append(" pass=").Append(PassEvents);
            text.Append(" fallbackImpacts=").Append(FallbackImpacts);
            text.Append(" menu=").Append(MenuEvents);
            text.Append(" ambient=").Append(AmbientStarts);
            text.Append(" service=").Append(Service == null ? "none" : (Service.IsReady ? "ready" : "not-ready"));
            text.Append(" events=").Append(Events.Count);
            text.Append(" warnings=").Append(Warnings.Count + (Service == null ? 0 : Service.Warnings.Count));

            return text.ToString();
        }
    }
}
