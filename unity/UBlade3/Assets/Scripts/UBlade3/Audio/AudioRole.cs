using System;

namespace UBlade3.Audio
{
    // One named original sound the native engine plays. Every shiped sound is identified by its
    // position in DATA\SNDFILES.LST counting only the sound lines, exactly as
    // third_party/hyperblade-decomp/src/sound/soundids.h states: "Sound ids: positions in
    // SNDFILES.LST, counting only the sound lines, named for their files."
    //
    // The group is SNDFILES.LST's third column, which is a value of DATA/ENUMIDS.H's enum
    // SoundTypes for the live groups (0 id_STP_Hit, 1 id_STP_Catch, 2 id_STP_Shoot) or one of the
    // sound manager's own values: 666 = SOUND_GROUP_LOOPING (looped) and 777 = SOUND_GROUP_PRELOAD
    // (menu group, held loaded outside a match), per src/sound/soundids.h.
    public sealed class AudioRole
    {
        internal AudioRole(string id, int index, string soundListName, int group, string purpose, string playRoute, string citation)
        {
            Id = id;
            Index = index;
            SoundListName = soundListName;
            Group = group;
            Purpose = purpose;
            PlayRoute = playRoute;
            Citation = citation;
        }

        // Lower-case stable id used by AudioService, the probe and docs/NATIVE-AUDIO.md.
        public string Id { get; }

        // Position in SNDFILES.LST's sound lines.
        public int Index { get; }

        // The list's first column, i.e. the wave file stem.
        public string SoundListName { get; }

        // SNDFILES.LST's group column.
        public int Group { get; }

        public string Purpose { get; }

        // How the original engine reaches this sound.
        public string PlayRoute { get; }

        // Where the claim comes from: decomp file/function and/or the shipped data plan.
        public string Citation { get; }

        // The menu/UI click. UIScreen's button handlers play PlaySpecific(SOUND_SXBUTT1); the sound
        // is the first sound of SNDFILES.LST's preload (menu) group 777 (SOUND_GROUP_PRELOAD 0x309).
        // Citation: src/sound/soundids.h SOUND_SXBUTT1 0x121; src/ui/uiscreen.cpp lines 451-568;
        // Data/sndfiles.lst entry 289 group 777.
        public static readonly AudioRole MenuUi = new AudioRole(
            "menu-ui",
            289,
            "Sxbutt1",
            777,
            "menu and screen button click",
            "UIScreen button handlers call g_pAudioMgr->PlaySpecific(SOUND_SXBUTT1).",
            "src/sound/soundids.h (SOUND_SXBUTT1 0x121), src/ui/uiscreen.cpp lines 451-568, Data/sndfiles.lst entry 289 (group 777 = SOUND_GROUP_PRELOAD)");

        // The first skater hit sound. listen.pbn's id_MSG_Hit handler calls
        // id_ACF_PlaySndType(0), which is AudioMgr::PlayGroup(0); enumids.h SoundTypes index 0 is
        // id_STP_Hit and SNDFILES.LST group 0 holds entries 0-3. SoundManager::PickFromGroup
        // alternates the group's first two entries, so the first hit plays Sxhhite2 and the next
        // Sxhhitf2.
        // Citation: src/sound/soundids.h SOUND_SXHHITE2 0; src/sound/soundmanager.cpp
        // SoundManager::PickFromGroup (HYPERX 0x00449680); src/planvm plan handler
        // ACF_PLAY_SND_TYPE -> AnimEvent::PlaySndType (HYPERX 0x00406ef0); Data/plans/listen.pbn
        // handler id_MSG_Hit; Data/enumids.h enum SoundTypes.
        public static readonly AudioRole ImpactFirst = new AudioRole(
            "impact-first",
            0,
            "Sxhhite2",
            0,
            "skater hit, first alternating hit-group sound",
            "listen.pbn id_MSG_Hit -> id_ACF_PlaySndType(0) -> AudioMgr::PlayGroup(0) -> SoundManager::PickFromGroup(0).",
            "src/sound/soundids.h (SOUND_SXHHITE2 0), src/sound/soundmanager.cpp PickFromGroup, src/planvm/planvm.cpp ACF_PLAY_SND_TYPE, Data/plans/listen.pbn, Data/enumids.h SoundTypes");

        // The second skater hit sound, the other half of the same alternating group pair.
        public static readonly AudioRole ImpactSecond = new AudioRole(
            "impact-second",
            1,
            "Sxhhitf2",
            0,
            "skater hit, second alternating hit-group sound",
            "Same PlayGroup(0) route as impact-first; PickFromGroup alternates entries 0 and 1.",
            "src/sound/soundmanager.cpp PickFromGroup; Data/sndfiles.lst entry 1 (group 0)");

        // The 1x goal reaction. ngoal.pbn and sgoal.pbn handle id_MSG_NGoalScored /
        // id_MSG_SGoalScored and, in the id_DCF_RokScoreValue arm for a one point goal, call
        // id_ACF_PlaySound(74); the 2x and 4x arms call 75 and 76. The same handler sends
        // id_MSG_PlayCrowdResponse to slot 24 for the crowd reaction (see CrowdGoalCheer/Boo).
        // Citation: Data/plans/ngoal.pbn and sgoal.pbn, id_MSG_NGoalScored/id_MSG_SGoalScored;
        // src/planvm/planvm.cpp ACF_PLAY_SOUND -> AnimEvent::PlaySnd (HYPERX 0x00406ea0);
        // Data/enumids.h enum SoundTypes id_STP_RokScore = 13, DecRule RokScoreValue.
        public static readonly AudioRole GoalOnePoint = new AudioRole(
            "goal-1x",
            74,
            "Sxrscra2",
            999,
            "goal scored, one point",
            "ngoal/sgoal plan id_MSG_*GoalScored -> id_DCF_RokScoreValue arm 0 -> id_ACF_PlaySound(74).",
            "Data/plans/ngoal.pbn and sgoal.pbn (id_MSG_NGoalScored/id_MSG_SGoalScored), src/planvm/planvm.cpp ACF_PLAY_SOUND");

        // The 2x goal reaction (same plan arm as GoalOnePoint with sound 75).
        public static readonly AudioRole GoalTwoPoint = new AudioRole(
            "goal-2x",
            75,
            "Sxrscra3",
            999,
            "goal scored, two points",
            "ngoal/sgoal plan id_MSG_*GoalScored -> id_DCF_RokScoreValue arm 1 -> id_ACF_PlaySound(75).",
            "Data/plans/ngoal.pbn and sgoal.pbn (id_MSG_NGoalScored/id_MSG_SGoalScored)");

        // The 4x goal reaction (same plan arm as GoalOnePoint with sound 76).
        public static readonly AudioRole GoalFourPoint = new AudioRole(
            "goal-4x",
            76,
            "Sxrscra4",
            999,
            "goal scored, four points",
            "ngoal/sgoal plan id_MSG_*GoalScored -> id_DCF_RokScoreValue arm 2/3 -> id_ACF_PlaySound(76).",
            "Data/plans/ngoal.pbn and sgoal.pbn (id_MSG_NGoalScored/id_MSG_SGoalScored)");

        // The crowd cheer that answers a goal into the south goal. sgoal.pbn sends
        // id_MSG_PlayCrowdResponse with data 0/1/2 (by score value) to slot 24; listen.pbn's
        // id_MSG_PlayCrowdResponse handler waits 53 ms and then, on id_MSG_GotIt, plays
        // PlaySound(18/19/20) for data 0/1/2. Entries 18-20 are Sxcrwdc2, Sxcrwdd2, Sxcrwde2.
        // Citation: Data/plans/sgoal.pbn (id_MSG_SGoalScored), Data/plans/listen.pbn
        // (id_MSG_PlayCrowdResponse / id_MSG_GotIt), Data/sndfiles.lst entries 18-20.
        public static readonly AudioRole CrowdGoalCheer = new AudioRole(
            "crowd-goal-cheer",
            18,
            "Sxcrwdc2",
            999,
            "crowd cheer answering a goal into the south goal",
            "sgoal.pbn id_MSG_SGoalScored -> SendMessage(24, id_MSG_PlayCrowdResponse, 0..2) -> listen.pbn -> id_ACF_PlaySound(18..20).",
            "Data/plans/sgoal.pbn (id_MSG_SGoalScored), Data/plans/listen.pbn (id_MSG_PlayCrowdResponse), Data/sndfiles.lst entries 18-20");

        // The crowd boo that answers a goal into the north goal. ngoal.pbn sends
        // id_MSG_PlayCrowdResponse with data 3/4/5; listen.pbn maps those to PlaySound(21/22/23),
        // i.e. Sxcbooa2/Sxcboob2/Sxcbooc2.
        // Citation: Data/plans/ngoal.pbn (id_MSG_NGoalScored), Data/plans/listen.pbn
        // (id_MSG_PlayCrowdResponse / id_MSG_GotIt), Data/sndfiles.lst entries 21-23.
        public static readonly AudioRole CrowdGoalBoo = new AudioRole(
            "crowd-goal-boo",
            21,
            "Sxcbooa2",
            999,
            "crowd boo answering a goal into the north goal",
            "ngoal.pbn id_MSG_NGoalScored -> SendMessage(24, id_MSG_PlayCrowdResponse, 3..5) -> listen.pbn -> id_ACF_PlaySound(21..23).",
            "Data/plans/ngoal.pbn (id_MSG_NGoalScored), Data/plans/listen.pbn (id_MSG_PlayCrowdResponse), Data/sndfiles.lst entries 21-23");

        // The looping crowd ambience. Game::StartMatch plays PlaySpecific(SOUND_SXXAMBA2) and the
        // match/attract path plays SOUND_SXXAMBB2; both are SNDFILES.LST group 666 =
        // SOUND_GROUP_LOOPING, and DSoundMgr::Play starts a group-666 entry with the looping flag.
        // On this install Sxxamba2 (entry 14) is absent, so the usable loop file is Sxxambb2
        // (entry 15).
        // Citation: src/engine/game.cpp lines 312 and 587, src/sound/soundids.h
        // (SOUND_GROUP_LOOPING 0x29a), src/sound/dsoundmgr.cpp Play (HYPERX 0x00490780),
        // Data/sndfiles.lst entries 14-15.
        public static readonly AudioRole CrowdAmbient = new AudioRole(
            "crowd-ambient",
            15,
            "Sxxambb2",
            666,
            "looping crowd ambience",
            "Game::StartMatch plays SOUND_SXXAMBA2/SOUND_SXXAMBB2; group 666 loops in DSoundMgr::Play.",
            "src/engine/game.cpp lines 312 and 587, src/sound/soundids.h (SOUND_GROUP_LOOPING 0x29a), src/sound/dsoundmgr.cpp Play, Data/sndfiles.lst entries 14-15");

        // Every role AudioProbe requires and AudioService can play by default.
        public static readonly AudioRole[] All =
        {
            MenuUi,
            ImpactFirst,
            ImpactSecond,
            GoalOnePoint,
            GoalTwoPoint,
            GoalFourPoint,
            CrowdGoalCheer,
            CrowdGoalBoo,
            CrowdAmbient
        };

        public static AudioRole Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            return null;
        }

        public override string ToString()
        {
            return Id + "=" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + SoundListName;
        }
    }
}
