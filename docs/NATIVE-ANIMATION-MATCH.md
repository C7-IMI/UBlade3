# NATIVE-ANIMATION-MATCH - plan animation ids in the native C# match (N15)

This is the bounded N15 animation-consumer slice: the native match now resolves the plan VM's
animation ids through the shipped AnimAssm/AnimPlayer registry and drives the real skater/goalie
MatchSkaterView clips, with clip changes, loop/persistent rules, animation-trigger events and
explicit unsupported-id telemetry. It is animation/consumer wiring, not full render parity. Nothing
was committed; the Lead owns the commit.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| src/planvm/planactionanim.cpp PlanAction::SetAnimation HYPERX 0x00401000 | Resolves the plan's animation argument as a short index; message-data uses nParam; applies the damaged limp swap; writes the behaviour animation/rate/flags and calls StartClipPlayback. |
| src/planvm/behparam.cpp BehParam::SetPersistentAnimation 0x0041cb20, SetAnimation 0x0041cce0, SetAnimationEvent 0x0041cd40 | The persistent/temporary animation ids, rate and flags, and the animation-event binding channel/target/kind/param. |
| src/anim/animregistry.cpp FindAnimByNameIdx 0x004170a0, AnimScriptTable::ParseScript 0x00418210, game.cpp 157-162 | AnimAssm.txt becomes one AnimDB per assembly line in file order; a plan animation id is exactly that table index; FindAnimByNameIdx walks the same table. |
| src/anim/animplayer.cpp SelectAnimationSet 0x00425080, SetClip 0x00425220, CheckLoopPoint 0x00424770, UpdateFrame 0x00424880, StartClipPlayback | The chosen AnimDB starts its first node (segment 0) at StartFrame; stepping is delta * StandardFps * playbackRate; LoopCount 0 loops forever, >0 repeats that many passes, negative is one-shot; triggers fire when their frame is crossed. |
| Data\AnimData\AnimAssm.txt | 249 assembly names, 388 segments, 103 triggers; the C# AnimationAssemblyFile keeps the same order. |
| src/ui and the existing MatchSkaterView/SkaterAnimationClip/SkaterCharacter pipeline | The real WBA clip, rig pose and mesh apply path; one clip per character bone. |

## 2. Implementation

New files:

| File | Contents |
| --- | --- |
| Native/MatchAnimationRegistry.cs | Loads AnimAssm into the plan-id table, resolves id -> assembly name -> segment 0 WBA through the existing SkaterAnimationClip loader, checks the character's bone count, caches clips and records load failures as unsupported. |
| Native/MatchAnimationPlayer.cs | One slot's clip player: default/persistent/temporary clips, segment-0 frame range, StandardFps stepping, LoopCount loop/finish rules, deferred flags==1, frame-crossing AnimAssm trigger events, and the Pose() call that writes the existing SkaterCharacter mesh. |
| Native/MatchAnimationConsumer.cs | Per-slot consumer: reads the plan action-30 state and behaviour 9/11/12 state, applies ids/rates/flags/persistent rules, logs clip changes/events/unsupported ids and a deterministic fixture entry point. |
| Native/MatchAnimationPreviewProbe.cs | Preview-009 player probe: --n15-probe=<png>; confirms the default consumer/audio/HUD, runs the plan trace plus a celebration fixture, captures one staged frame, quits 0. |
| docs/NATIVE-ANIMATION-MATCH.md | This document. |

Edited (additive; default-off for deterministic capture paths):

* Native/MatchScene.cs: optional Animation consumer, hooks in TickScene/ApplyRender/Build,
  EnableAnimationConsumer and AttachPlanAnimation. Null unless the interactive/N15 path enables it.
* Native/MatchPlay.cs: attaches Control.PlanRoster to the consumer after AttachDefaultPlanAi.
* Native/MatchPlanAi.cs: read-only AnimationActionState/CurrentBehavior/CurrentBehaviorState
  accessors for the consumer; the plan still owns every value.
* Assets/Editor/UBlade3MatchPlay.cs: CheckAnimation and BuildPreview009.

### 2.1 Default gating and regression safety

The consumer is created only by MatchScene.Build when MatchAnimationConsumer.ShouldEnableByDefault()
is true: an interactive player (Application.isPlaying, not batch mode) with no
--play-capture/--n7-capture/--hud-capture/--display-probe/--n13-probe/--gameplay-capture/
--match-capture/--n13-capture argument. ForceEnabled is used by the N15 Editor trace. Therefore every
deterministic N4-N14 capture/display path keeps its old clip clock; the N5 capture gate below is
byte-identical. The normal interactive player and a --n15-probe build get the consumer by default.

### 2.2 Playback rules implemented

* Segment 0 only, exactly the AnimPlayer SetClip path: StartFrame..EndFrame inclusive,
  StandardFps stepping; LoopCount 0 = forever, >0 = that many passes, negative = one-shot.
* The plan rate argument is recorded as RequestedRate. In the decompiled AnimPlayer it is passed to
  SelectAnimationSet as the SetClip blend value, not as playback speed; playback remains 1.0. This
  slice does not implement crossfades, so the rate is honest telemetry rather than a speed multiplier.
* flags == 1 queues a deferred clip until the current clip finishes (SelectAnimationSet parks
  m_pending); other flags switch immediately. A refused/missing id is explicit unsupported telemetry.
* AnimAssm triggers whose Segment is 0 fire when their frame is crossed; the consumer logs the action
  name, sync assembly/segment/frame and the current behaviour 12 binding when one exists.
* Persistent behaviour 9 replaces the persistent clip; behaviour exit/one-shot completion restores
  it. Behaviour 11 temporary animation plays over the persistent one. Action 30 is immediate/deferred
  according to flags.

## 3. Deterministic trace and pose captures

Command (graphics required for the capture; do not add -nographics):

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CheckAnimation ^
        -logFile <temp>/n15-animation-check.log

Exit 0, UBLADE3_NATIVE_ANIMATION_CHECK_OK. Key log:

    UBLADE3_NATIVE_ANIMATION_CHECK_COUNTS planSlots=5 planRequests=3 planApplied=9
      behaviorChanges=5 persistentChanges=5 clipChanges=20 events=114 resolvedIds=7
      unsupported=0 captures=5
    resolvedPlanNames=[AA_Skate_Std,AA_Goalidle,AA_CelbAjdn,AA_OvhndThrw,AA_TorsoLaunch,
      AA_Fall,AA_idle1]

The natural plan trace (before the fixture) exercised plan action 30 and behaviour 9: it logged
action-30 requests for id 105 AA_Skate_Std (the persistent skate; the same clip does not restart,
matching SelectAnimationSet) and behaviour 9 persistent 183 AA_Goalidle for the goalies. It also
recorded 104 AnimAssm frame events, predominantly id_MSG_SyncFrame with the AA_Glide/AA_Skatefast
sync references. In this bounded scenario the local plan decisions did not request a different
temporary clip and did not apply a behaviour 11 animation, so the trace is honest about that limit
and uses the labelled fixture below for visible category changes.

The fixture drives the same consumer/resolution/player path with shipped ids (the log source tags
name the intended category):

| Slot | Id | Name | Category | Capture |
| --- | --- | --- | --- | --- |
| 1 | 0 | AA_CelbAjdn | celebration | native-animation-frame-00.png |
| 2 | 49 | AA_OvhndThrw | throw | native-animation-frame-01.png |
| 3 | 225 | AA_TorsoLaunch | launch | native-animation-frame-02.png |
| 4 | 11 | AA_Fall | fall/knockdown | native-animation-frame-03.png |
| 0 | 151 | AA_idle1 | idle | native-animation-frame-04.png |
| 5 | 183 | AA_Goalidle | persistent goalie | no clip change (already persistent) |

All six fixture requests resolved and applied; five changed the clip and produced capture files.
The captures are staged pose evidence, not gameplay-camera frames: the other skater views are
hidden, the captured view is placed at centre ice and the match camera is put on a fixed
three-quarter angle. The log labels the source/fixture and the consumer snapshot for each frame.

| File | Size | SHA-256 |
| --- | --- | --- |
| artifacts/native-animation-frame-00.png | 1280x720 122887 B | be3203f5d1874c06795c632e5ec543537278f3269c21e38af8a6fb5867fafc22 |
| artifacts/native-animation-frame-01.png | 1280x720 122751 B | 874b910cbede6688a5811af22e58ba6ee036cd289213c0cdfa78e806210d839b |
| artifacts/native-animation-frame-02.png | 1280x720 120212 B | 2de750cc62b1cbb7d6591343229555c8ac7875b9c43a49ae001f298e9c9bf8ed |
| artifacts/native-animation-frame-03.png | 1280x720 126096 B | 2ec62a661f124753d66be9874fa5b7a98fd22caf064dc35ac91bc03bed3d32e4 |
| artifacts/native-animation-frame-04.png | 1280x720 122044 B | 3c0d48da62adb593c3fc6f997036915fe08c2c323a86822ebd105c406290104f |

## 4. Preview 009

Editor entry point:

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.BuildPreview009 ^
        -logFile <temp>/n15-preview009-build.log

Exit 0, UBLADE3_NATIVE_PREVIEW_009_BUILD_OK. Package:

* unity/UBlade3/build/preview-009/UBlade3NativePreview009.exe
  667136 bytes, SHA-256 9806abde4a406ed68647e9505852c49f6aa72a372f4f084e2aa2821affea57a2
* unity/UBlade3/build/ublade3-native-preview-009.zip
  40008265 bytes, SHA-256 5021ac02f945c989973417b2c3665d2ec31cc65e6aa3188563f0c7e7389f3e37,
  161 entries

The zip was extracted to %TEMP%\ublade3-preview-009-extracted and the extracted executable (not the
build-directory exe) was launched with --n15-probe=<png> -logFile <player log>. Exit 0. No Unity,
20XX or hyperblade process remained afterwards.

Evidence from the extracted player:

    UBLADE3_MATCH_AUDIO_DEFAULT enabled=True ready=True channelCount=8
      outputSampleRate=48000 speakerMode=Stereo ...
    UBLADE3_NATIVE_ANIMATION_CONSUMER_BUILT ... registry=249
    UBLADE3_NATIVE_PLAY_READY ... audio=on
    UBLADE3_NATIVE_ANIMATION_PROBE_READY hudOriginal=True audioReady=True registry=249 planSlots=5
    UBLADE3_NATIVE_ANIMATION_PROBE_PLAN_STATE ... planRequests=3 planApplied=3
      behaviorChanges=5 persistentChanges=5 clipChanges=6 animationEvents=32 unsupported=0
    UBLADE3_NATIVE_ANIMATION_PROBE_HUD period=1 clock="4:55"
      score="THE MACHINE 0-0 THE FURY" originalHud=True
    UBLADE3_NATIVE_ANIMATION_PROBE_FIXTURE ... id=0 name=AA_CelbAjdn temp=True ...
    UBLADE3_NATIVE_ANIMATION_PROBE_OK clips=7 events=32 resolvedIds=3 audioReady=True
      hudOriginal=True

Artifact: artifacts/native-preview-009-animation.png (1280x720, 124751 bytes, SHA-256
531805a86dcc6ee8f1e468296d41e447936fbefb4949a153d2e68e30596dfbad), the staged celebration frame
from the extracted player. Audio/HUD regression detection: audio ready and playing with the N13
device/channel configuration, HUD original and score/clock sane; no exceptions.

## 5. Regressions

After the N15 changes, the full local N4-N14 check batch exited 0:

    N4  UBLADE3_NATIVE_MATCH_CHECK_OK digest=f54169057f5800dd mismatches=0
    N5  UBLADE3_NATIVE_GAMEPLAY_CHECK_OK mismatches=0
    N6  UBLADE3_NATIVE_PLAY_CHECK_OK mismatches=0
    N7  UBLADE3_NATIVE_MATCH_N7_CHECK_OK mismatches=0
    N8  UBLADE3_NATIVE_MATCH_N8_CHECK_OK mismatches=0
    N9  UBLADE3_NATIVE_MATCH_N9_CHECK_OK mismatches=0
    N10 UBLADE3_NATIVE_MATCH_N10_CHECK_OK mismatches=0
    N11 UBLADE3_NATIVE_MATCH_N11_CHECK_OK mismatches=0
    N12 UBLADE3_NATIVE_MATCH_N12_CHECK_OK mismatches=0
    N13 UBLADE3_NATIVE_MATCH_N13_CHECK_OK mismatches=0
    N14 UBLADE3_NATIVE_MATCH_N14_CHECK_OK mismatches=0
    UBLADE3_DISPLAY_CHECK_OK; UBLADE3_NATIVE_HUD_CHECK_OK (mismatches=0)

The seven N5 Editor captures were regenerated and are byte-identical to HEAD. python
scripts/check_all.py PASS (209 text files in the shared working tree at verification time; the
concurrent PlanVM files are also present). No N11/N12/N13
audio/HUD regression was observed: the N12 HUD probe is still 0 mismatches, the N13 check is green,
and the N15 preview player reports audio ready/HUD original. No external 20XX process was running.

## 6. Gaps

* The natural plan trace observed action 30 and behaviour 9 but not a different temporary clip or a
  behaviour 11/12 application; those paths are wired and covered by the labelled fixture, not by a
  natural plan decision in this bounded window.
* Only segment 0 of an assembly is played. AnimPlayer can advance to the next segment on clip end;
  this consumer restores the persistent clip instead. No crossfade/blend (the decompile rate
  argument is a blend factor) and no animation-driven physics force.
* Animation trigger events are logged with their frame/action/sync/binding telemetry; they are not
  re-injected as plan scene messages yet.
* Action 9 IncrementStat and action 71 ScaleBubToRating already have exact PlanVM telemetry
  (StatEvents=1, BubbleScaleEvents=2, LastStatId/LastBubbleRadius in the N12/N13 checks) but no
  match-side stat accumulator or collision/awareness bubble physics consumer exists; this slice does
  not invent one.
* The pose captures are staged for visibility (other views hidden, fixed camera), not gameplay
  camera frames. The N15 player capture is also staged.
* The default consumer is for the interactive player and --n15-probe; deterministic N4-N14 paths are
  deliberately unchanged, so their screenshots do not show the new animation consumer.
* The preview package is an untracked unsigned development build, not an installer.

## 7. Files changed

New: Native/MatchAnimationRegistry.cs, MatchAnimationPlayer.cs, MatchAnimationConsumer.cs,
MatchAnimationPreviewProbe.cs (+ .meta), docs/NATIVE-ANIMATION-MATCH.md, five
artifacts/native-animation-frame-*.png and artifacts/native-preview-009-animation.png.
Edited: Native/MatchScene.cs, MatchPlay.cs, MatchPlanAi.cs and Assets/Editor/UBlade3MatchPlay.cs.
No Data/Assets/Sim/PlanVM/App/ProjectSettings/native release files were edited by this slice.
