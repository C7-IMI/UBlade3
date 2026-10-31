# NATIVE-AUDIO-MATCH - default match audio and N13 HUD details

This is the N13 bounded follow-up for the native C# player: the original-wave audio service plays by
default in the interactive player, the goal/crowd/impact/menu/pickup/shot routes are exercised in a
real extracted player build, and the remaining N12 HUD details (dynamic speed01 speed meter, goalie
roster row, live shot-power/reticle path, score/clock/period checks) are wired without changing the
720p/1080p/1440p/4K layout or scaling. The output is native C# preview 006. It is not a parity or
release claim and nothing was committed; the Lead owns the commit.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3.

## 1. Audio default and event routing

The audio service was explicit-only before N13 (UBLADE3_AUDIO=1 or --ublade3-audio). New
Native/MatchAudioDefaults.cs installs it by default only for a real interactive player:

    Application.isPlaying && !Application.isBatchMode
    no --play-capture/--n7-capture/--hud-capture/--display-probe/--gameplay-capture/
       --match-capture/--n13-capture argument
    UBLADE3_AUDIO is not 0/false (opt-out)

The hook runs BeforeSceneLoad, points HYPERBLADE_GAME_DATA at the same resolved install MatchScene
uses, calls AudioBootstrap.InstallAudio(), initialises it if needed and logs
UBLADE3_MATCH_AUDIO_DEFAULT. If the service or device is unavailable it logs the reason and the game
continues with a record-only/silent director; no exception escapes.

MatchAudioDirector now discovers that installed service when MatchPlay constructs it with a null
service, so the normal interactive path and the existing explicit --ublade3-audio path both use it.
The physical event routes are the original SNDFILES.LST entries:

| Event | Route | Original entry |
| --- | --- | --- |
| pickup / catch | SNDFILES group 1 catch | Sxrctha3 |
| shot release | group 2 shoot | Sxthrwc4 |
| pass release | group 2 throw | Sxthrwc5 |
| arena bounce/other impact | alternating hit group 0 | AudioRole.ImpactFirst/ImpactSecond |
| goal | goal 1x/2x/4x | AudioRole.GoalOnePoint/TwoPoint/FourPoint |
| goal crowd | cheer south / boo north | AudioRole.CrowdGoalCheer/CrowdGoalBoo |
| menu | menu UI | AudioRole.MenuUi |
| ambience | looping crowd | AudioRole.CrowdAmbient |

The existing N6-N12 compatibility aggregate is preserved: pickup/shot/pass still increment
ImpactEvents as well as their specific PickupEvents/ShotEvents/PassEvents counters, so the old
audio-routing assertion stays true. Named-entry failure falls back to the hit group and logs
fallbackImpact=True.

MatchPlay.Start also gained the missing normal interactive autostart (it previously built only for
capture commands or --play-autostart). The N11 display probe keeps its own deterministic build: a
--display-probe run returns from Start before autostart. Capture command paths are unchanged.

## 2. HUD details added

* Dynamic speed meter: the original speed01 art (162x51, same size as frspeed) is loaded and drawn
  as the first floor(width * speedRatio) columns of the sprite at the speed-frame position, before
  the frame art, matching CDDSurfaceOwner::Draw (0x0044f9b0) and infobar.cpp's
  SetStrip(ptSpeedSize.x, 1, 0, 0) (lines 995-999). The ratio is the followed player's live
  SpeedRatio, or HudSpeedRatioOverride in the deterministic N12 probe. N13 preview run logged
  speedRatio=1 and the speed bar is visible in the captures.
* Goalie roster row: rows 1-2 are the two on-ice forwards and row 3 now uses the actual goalie data
  (sim slots 4/5, labelled G. TEAM G); row 4 remains the original second reserve seat and stays
  blank because the simulation has no bench roster. The goalie damage values are the real
  RATING_DAMAGE scalars (8 in the preview).
* Score/clock/period: the original HUD already draws MatchHudModel; the preview probe checks the
  model against the simulation and logged scoreOk=True periodOk=True clockOk=True (period 1,
  clock 4:54, score THE MACHINE 0-0 THE FURY).
* Shot power/reticle live state: when HudShotPowerOverride is negative (the normal interactive
  case), MatchHudOriginal reads MatchPlay.Control.ShotPowerLevel, Control.Carrying and
  Control.HeadCam.ViewFracX/Y every frame, so the live shot bar and reticle follow the player. The
  preview probe did not take a shot (shotPower=0, carrying=False) and correctly shows the shot frame
  with no power bar; the N12 pinned-state captures remain the visual proof of the bar/reticle art.
* Layout/scaling unchanged: the N12 CheckHud probe still reports mismatches=0 for the 640x480
  design and the 1280x720 scale 1 / 1920x1080 scale 2 / 2560x1440 scale 3 (852x480, origin 2,0) /
  3840x2160 scale 4 layers. speed01 is 162x51 and uses the existing speed-frame rect 0,0.

## 3. Preview 006 build and extracted-player verification

Editor entry point:

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.BuildPreview006 ^
        -logFile <temp>/n13-preview-build.log

Exit 0, UBLADE3_NATIVE_PREVIEW_006_BUILD_OK. Output and package (untracked build tree):

* player: unity/UBlade3/build/preview-006/UBlade3NativePreview006.exe
  667136 bytes, SHA-256 9806abde4a406ed68647e9505852c49f6aa72a372f4f084e2aa2821affea57a2
* zip: unity/UBlade3/build/ublade3-native-preview-006.zip
  39992568 bytes, SHA-256 5612b0f9fe59e15363cf393555cac22b164afcce6933f7ae47fc14b194841f84,
  161 entries

The package was extracted to %TEMP%\ublade3-preview-006-extracted and the extracted executable (not
the build-directory exe) was launched with:

    UBlade3NativePreview006.exe -logFile <temp>/n13-preview-player-final2.log ^
        --n13-probe=<temp>/n13-preview-hud.png

The probe waited for the default interactive build, verified the default audio, drove the public
TickPlay(HeldBinding.Forward) path for 45 ticks so the speed meter was live, captured 1080p and 4K
frames, and quit 0. No Unity, 20XX or hyperblade process remained afterwards; the extracted player
was the only process during the run and it exited cleanly.

### 3.1 Audio evidence from the extracted player

    UBLADE3_MATCH_AUDIO_DEFAULT enabled=True service=present ready=True channelCount=8 cache=0
      warnings=110 detail="outputSampleRate=48000 speakerMode=Stereo dspBufferSize=1024
      realVoices=32 virtualVoices=512"
    UBLADE3_MATCH_AUDIO_SERVICE ready=True channels=8 cached=0 warnings=110
    UBLADE3_NATIVE_PLAY_READY controls=... audio=on
    UBLADE3_NATIVE_PREVIEW_AUDIO_BEFORE ready=True device=... defaults=... attempted=True
      enabled=True
    UBLADE3_MATCH_AUDIO_EVENT kind=pickup reason=rok-pickup audioRole=Sxrctha3(catch-group-1)
      fallbackImpact=False played=True
    UBLADE3_MATCH_AUDIO_EVENT kind=shot reason=shot-release audioRole=Sxthrwc4(shoot-group-2)
      fallbackImpact=False played=True
    UBLADE3_MATCH_AUDIO_EVENT kind=pass reason=pass-release audioRole=Sxthrwc5(throw-group-2)
      fallbackImpact=False played=True
    UBLADE3_MATCH_AUDIO_EVENT kind=impact reason=rok-bounce audioRole=impact-first/second
      fallbackImpact=False played=True
    UBLADE3_MATCH_AUDIO_EVENT kind=goal points=1 ... goal-1x crowd-goal-cheer played=True/True
    UBLADE3_MATCH_AUDIO_EVENT kind=menu reason=n13-probe audioRole=menu-ui played=True
    UBLADE3_NATIVE_PREVIEW_AUDIO_RESULT ready=True device=...
      counts="UBLADE3_MATCH_AUDIO_COUNTS requested=8 played=9 failed=0 goal=1 crowd=1 impact=4
      pickup=1 shot=1 pass=1 fallbackImpacts=0 menu=1 ambient=2 service=ready events=8 warnings=110"
      playedOk=True

The 110 warnings are the pre-existing AudioService catalogue warning count (110 SNDFILES.LST entries
without shipped wave files); the event route entries used here are present and PlayName returned
true (PlayIndex only returns true after the clip loads). No exception occurred. Physical speaker
output cannot be measured from a log; the audible claim is bounded to the real Unity audio device
configuration (48 kHz stereo, 8 channels), ready service and successful one-shot plays, with the
honest limit recorded in section 5.

### 3.2 HUD evidence from the extracted player

    UBLADE3_NATIVE_PREVIEW_READY tick=0 hudOriginal=True audioReady=True
    UBLADE3_NATIVE_PREVIEW_HUD_STATE original=True period=1 clock="4:54"
      score="THE MACHINE 0-0 THE FURY" speedRatio=1 shotPower=0 carrying=False
      scoreOk=True periodOk=True clockOk=True
    UBLADE3_NATIVE_PREVIEW_HUD_CAPTURE frame=1920x1080 bytes=223764 hudOriginal=True
    UBLADE3_NATIVE_PREVIEW_HUD_CAPTURE frame=3840x2160 bytes=648231 hudOriginal=True
    UBLADE3_NATIVE_PREVIEW_PROBE_OK ... hudOriginal=True

Artifacts (untracked):

* artifacts/native-preview-006-1080p.png 1920x1080 223764 bytes
  SHA-256 5ee8757373b4762e017d70d025df98817e86929a858ab6d084c060a6f307a27b
* artifacts/native-preview-006-4k.png 3840x2160 648231 bytes
  SHA-256 bd17791c3bfe4dfc44968e65cf67f6a87f9942cb27571092383e4ab97f40c74c

The frames show the dynamic colored speed bar at 0,0, team names and forward rows plus the G. TEAM G
goalie row with damage, CHI/SEA team frames and damage bars, period frame/clock/lamps (period 1,
4:54), shot-camera frame, ROK-free status message and the radar circle/blips/trail. The 4K frame is
the same 960x540 HUD layer at scale 4; 1080p is scale 2.

## 4. Regression evidence

After the N13 changes, the following were run on this tree:

    N4  UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd
    N5  UBLADE3_NATIVE_GAMEPLAY_CHECK_OK (homeScore=2 awayScore=1 goals=2 mismatches=0)
    N6  UBLADE3_NATIVE_PLAY_CHECK_OK (pickups=2 shots=1 passes=1 goals=1 score=1-0 mismatches=0)
    N7  UBLADE3_NATIVE_MATCH_N7_CHECK_OK
    N11 UBLADE3_DISPLAY_CHECK_OK
    N12 UBLADE3_NATIVE_HUD_CHECK_OK (probe mismatches=0 missing=0 fontAvailable=True)
    N5  seven Editor captures regenerated and byte-identical to HEAD blobs

N6 initially failed its audio-routing assertion because pickup/shot/pass now had specific counters;
the compatibility aggregate was restored and the rerun is the N6 line above. N11's display player
probe was rebuilt and rerun at 1920x1080: UBLADE3_DISPLAY_PROBE_OK, exit 0, PNG 1920x1080 249369
bytes, SHA-256 2be8ddb66757f34c6322aca660bef54d5be47d2066e9868024d08ea4ebb8fb82, which is exactly
the committed artifacts/native-display-1080p-probe.png blob. No external process remained.

python scripts/check_all.py PASS (195 text files in the shared working tree at verification time;
the concurrent PlanVM files are also present).

The u3-planvm teammate also reported its own N4-N12 batch green after its concurrent PlanVM changes
(N12 twice with digest 333565d513ad20e0, N11/N10/N9/N8/N7/N6 and RunAll CHECKS_OK); that is a
separate teammate's evidence and is not re-claimed here beyond the checks listed above that this
slice ran itself.

## 5. Gaps

* Audibility is bounded to Unity device configuration, service readiness, channel count and
  successful PlayOneShot calls; no acoustic measurement is possible in this environment. A host with
  no audio device produces the logged silent/record-only fallback and still renders the HUD.
* AudioService's per-channel playing state is not exposed, so the log proves 8 channels exist and 9
  one-shots were accepted, not which channel carried which sample.
* The 110 AudioService warnings are missing original wave entries in the shipped data (pre-existing,
  not introduced here); all event sounds used by this slice are present.
* The original four-row roster needs names/bench data the simulation does not have: rows 1-2 are
  TEAM F1/F2, row 3 is the real goalie, row 4 remains blank. No bench names were invented.
* The preview probe did not carry the ROK or charge a shot, so shot-power/reticle appears only in the
  N12 pinned captures; the live path is wired and reads MatchPlay.Control every frame but was not
  visually exercised with a charged shot in preview 006.
* The speed meter was verified at speedRatio=1; zero/low-speed visuals were not separately captured.
* The N13 HUD captures come from the probe camera/render path, not a desktop window screenshot; the
  built player did start and exit 0 with the default scene, but the default window contents were not
  separately screenshotted.
* The preview package is an untracked development build, not an installer or code-signed release.

## 6. Files changed

New:

* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchAudioDefaults.cs (+ .meta)
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchAudioHudProbe.cs (+ .meta)
* docs/NATIVE-AUDIO-MATCH.md (this file)
* artifacts/native-preview-006-1080p.png, artifacts/native-preview-006-4k.png

Edited:

* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchAudioDirector.cs: default service discovery,
  pickup/shot/pass routes, fallback, counters and compatibility aggregate.
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHudOriginal.cs: speed01 dynamic meter and goalie
  roster row.
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchPlay.cs: normal interactive autostart plus the
  display-probe guard that preserves N11's deterministic probe.
* unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs: BuildPreview006.

No Data/Assets/Sim/PlanVM/ProjectSettings/native release files were edited by this slice.
