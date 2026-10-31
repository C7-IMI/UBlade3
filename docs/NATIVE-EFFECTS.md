# NATIVE-EFFECTS - remaining PlanVM effects in the native C# match (N16)

This is the bounded N16 slice that consumes the remaining PlanVM effects: action 9 IncrementStat
into a real match-stat accumulator, action 71 ScaleBubToRating into an explicit awareness/bubble
adapter, and AnimAssm frame triggers back into the PlanVM message path, with next-segment and sync
transitions in the N15 animation player. It is effect-consumer wiring, not full render/stat/HUD
parity. Nothing was committed; the Lead owns the commit.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| src/planvm/plancond.cpp PlanAction::IncrementStat HYPERX 0x00461d20 | Only the pronoun target form acts: target.m_matchStats.AccumulateStat(statId, sourceSlot). |
| src/engine/matchstats.cpp MatchStats::AccumulateStat 0x004918d0, matchstats.h, statids.h | The exact 49-float MatchStats array, the special goal/shooting/possession/assist/pass switches and the default one-slot increment. |
| src/planvm/plancond.cpp PlanAction::ScaleBubToRating 0x00461ea0 | Writes m_flRadius on the BoundBubble returned by GetColObjPtr(colObject); radius = (high-low)*(stat*PlanStatScale)+low. |
| src/anim/animplayer.cpp UpdateFrame 0x00424880, SelectAnimationSet 0x00425080, OnClipEnd 0x00425?, SetClip 0x00425220 | A trigger with MSG_SYNC_FRAME is a ConditionalDispatch transition; other trigger messages go to the target object via PostEvent. On clip end the next AnimDB node (segment) is selected, or the set is done. |
| Data\AnimData\AnimAssm.txt | Segment ranges/loops and the trigger action/sync table (249 assemblies, 388 segments, 103 triggers). |
| N15 files MatchAnimationRegistry/Player/Consumer | The already-verified id -> assembly -> WBA -> real skater mesh path. |

## 2. Implementation

New Native files:

| File | Contents |
| --- | --- |
| MatchStatAccumulator.cs | Per-slot 49-float MatchStats table. Reads PlanAiActionState.StatEvents/LastStatSlot/LastStatId and applies the exact MatchStats::AccumulateStat switch. Exposes GetStat/GetStats/Describe/TraceText; no value is synthesized. |
| MatchBubbleAdapter.cs | Per-actor action-71 radius table. Reads PlanAiActionState.BubbleScaleEvents/LastBubbleRadius, exposes GetRadius and CountAwarenessNeighbours. It is an adapter-only API because the Sim has no per-player BoundBubble/colObject collision path; the Sim collision shell is not modified. |
| MatchAnimationMessageBridge.cs (+ MatchEffectsDriver) | Maps non-sync AnimAssm trigger action names id_MSG_* through PlanOpcodes.MessageNames and calls the target MatchPlanAi.InjectSceneMessage. Sync triggers are counted/handled by the animation player. The Auto driver installs the three consumers in interactive/N16-probe runs only. |
| docs/NATIVE-EFFECTS.md + artifacts | This record and the deterministic captures. |

Edited (animation files only; capture paths stay default-off):

* MatchAnimationPlayer.cs: current segment index/range/fps/loop, next-segment transition on clip end, sync-trigger transition (assembly/segment/frame), trigger list per segment, and sync/segment counters. Crossfade itself is not implemented (hard switch; documented gap).
* MatchAnimationConsumer.cs: aggregate sync/segment transition counters in the state/describe output.
* MatchAnimationPreviewProbe.cs: accepts --n16-probe and reports the effects driver/state in the extracted player.

The N15 capture/display gating is unchanged: MatchEffectsDriver and the animation consumer are off
for --play-capture/--n7-capture/--hud-capture/--display-probe/--n13-probe/--n15-probe and batch
captures. Normal interactive and --n16-probe runs get them.

## 3. Deterministic evidence (CheckEffects)

Command (graphics for the capture; do not add -nographics):

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CheckEffects ^
        -logFile <temp>/n16-effects-check.log

Exit 0, UBLADE3_NATIVE_EFFECTS_CHECK_OK. The run uses the N12 default-PlanVM scenario
(CreateDefaultPlayScene + MatchPlayN11Script.Stage) because that shipped-plan window actually
executes action 9 and action 71; the stat/bubble values below are observed state, not injected.

    UBLADE3_NATIVE_EFFECTS_STAT_CHANGE tick=66 applied=1 lastStat=37 lastSlot=0 value=2199
    UBLADE3_NATIVE_EFFECTS_STAT_CHANGE tick=210 applied=2 lastStat=37 lastSlot=0 value=6999
    UBLADE3_NATIVE_EFFECTS_STAT_STATE applied=2 rejected=0 multiplePerTick=0
      lastSlot=0 lastStat=37 lastValue=6999 [slot=0 nonzero=1 37=6999]
    UBLADE3_NATIVE_EFFECTS_BUBBLE_CHANGE tick=1 applied=2 lastSlot=5 radius=30 neighbours=0
    UBLADE3_NATIVE_EFFECTS_BUBBLE_STATE applied=2 rejected=0 lastSlot=5 lastRadius=30
      simCollisionPath=none(adapter-only) [slot=4 radius=30] [slot=5 radius=30]
    UBLADE3_NATIVE_EFFECTS_MESSAGE_STATE attached=6 triggers=87 syncTriggers=86 injected=1
      failures=0 unmapped=0
    UBLADE3_NATIVE_EFFECTS_SEGMENT_FIXTURE id=0 name=AA_CelbAjdn segmentTransitions=1
      syncTransitions=127 ...
    UBLADE3_NATIVE_EFFECTS_CAPTURE path=...\artifacts\native-effects-00.png bytes=122424
    UBLADE3_NATIVE_EFFECTS_CHECK_COUNTS statApplied=2 statNonzero=1 bubbleApplied=2
      bubbleRadius=30 messagesInjected=1 syncTriggers=127 unmappedTriggers=0
      attachedPlayers=6 syncTransitions=127 segmentTransitions=1 unsupported=1 captures=1

Stat id 37 is MATCH_STAT_POSS_START (0x25 decimal 37); the exact MatchStats switch stores the tick
timestamp, so the observed values 2199 and 6999 are the real timestamps written by two action-9
executions. The accumulator also implements the full switch for every other stat id; this
deterministic plan window only uses the possession-start id. The API is public
(MatchStatAccumulator.GetStat/GetStats); the plan VM has no ported match-stat read handler yet, so
exposing the values into a plan decision remains a documented gap (PlanVM is out of this slice's
scope).

The bubble values are the real action-71 LastBubbleRadius values (30 on slots 4 and 5). The N16
adapter exposes them and counts awareness neighbours from live Sim positions; it does not write a
Sim collision shape because there is no per-player BoundBubble/colObject in the Sim slice.

The message bridge's one injection came from a real AnimAssm non-sync trigger in AA_Htrokfrnt
(id 66, action id_MSG_YerHit) played through the live consumer; the message id was resolved through
PlanOpcodes.MessageNames and delivered to the target plan object. The 127 sync triggers were handled
by the animation player's sync transitions, not injected as ordinary messages. YerDown triggers have
no PlanOpcodes.MessageNames entry in the ported table and stay explicit unmapped telemetry.

Artifact: artifacts/native-effects-00.png (1280x720, 122424 bytes, SHA-256
70b6efca0bcdc9f2857767038f00e9a741c1da8b29c7098d18a6c1fa0b431b1a). It is the staged pose capture
of the animated character at the end of the effects trace; it is labelled as staged, not a gameplay
camera frame.

## 4. Preview 010

Editor entry point:

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.BuildPreview010 ^
        -logFile <temp>/n16-preview010-build.log

Exit 0, UBLADE3_NATIVE_PREVIEW_010_BUILD_OK. Package:

* unity/UBlade3/build/preview-010/UBlade3NativePreview010.exe
  667136 bytes, SHA-256 9806abde4a406ed68647e9505852c49f6aa72a372f4f084e2aa2821affea57a2
* unity/UBlade3/build/ublade3-native-preview-010.zip
  40013909 bytes, SHA-256 7b9c7e66f7b53a1bb392a64eb605b9038af6e8aa7739fe07f571f9ca5acb2220,
  161 entries

The zip was extracted to %TEMP%\ublade3-preview-010-extracted and the extracted executable (not the
build-directory exe) was run with --n16-probe=<png> -logFile <player log>. Exit 0; no Unity, 20XX,
hyperblade or player process remained. Log:

    UBLADE3_MATCH_AUDIO_DEFAULT enabled=True ready=True channelCount=8 ...
    UBLADE3_NATIVE_ANIMATION_CONSUMER_BUILT ... registry=249
    UBLADE3_NATIVE_PLAY_READY ... audio=on
    UBLADE3_NATIVE_ANIMATION_PROBE_READY hudOriginal=True audioReady=True registry=249 planSlots=5
    UBLADE3_NATIVE_EFFECTS_READY animation=5 planRoster=5
    UBLADE3_NATIVE_ANIMATION_PROBE_PLAN_STATE ... clipChanges=34 syncTransitions=32 ...
    UBLADE3_NATIVE_ANIMATION_PROBE_HUD period=1 clock="4:55"
      score="THE MACHINE 0-0 THE FURY" originalHud=True
    UBLADE3_NATIVE_ANIMATION_PROBE_EFFECTS driver=True stats="... applied=0 ..."
      bubbles="... applied=2 lastSlot=5 lastRadius=30 simCollisionPath=none(adapter-only)"
      messages="... attached=6 triggers=35 syncTriggers=35 injected=0 ..."
    UBLADE3_NATIVE_ANIMATION_PROBE_OK clips=38 events=35 resolvedIds=3 audioReady=True
      hudOriginal=True

The short interactive probe window did not execute action 9 (stats applied=0) or a non-sync message
trigger; that is reported, not hidden. The action-71 bubble path did run (applied=2, radius 30).
Artifact: artifacts/native-preview-010-effects.png (1280x720, 148759 bytes, SHA-256
bc1ab19aa027061e8a72d14f841c28e941eb3623e99b0e1f1adfa7a1f86cd7b6).

## 5. Regressions

The full local N4-N15 batch exited 0 after the N16 changes:

    N4 MATCH_CHECK_OK digest=f54169057f5800dd mismatches=0
    N5 GAMEPLAY_CHECK_OK mismatches=0
    N6 PLAY_CHECK_OK mismatches=0
    N7/N8/N9/N10/N11/N12/N13/N14 CHECK_OK mismatches=0
    N15 CheckAnimation CHECK_OK (counts updated for N16 segment/sync behaviour:
      planRequests=3 planApplied=9 behaviorChanges=5 persistentChanges=5 clipChanges=115
      events=98 resolvedIds=7 unsupported=1 captures=5)
    UBLADE3_DISPLAY_CHECK_OK; UBLADE3_NATIVE_HUD_CHECK_OK mismatches=0

The seven N5 Editor captures were regenerated and are byte-identical to HEAD. python
scripts/check_all.py PASS (213 text files in the shared working tree at verification time; the
concurrent PlanVM files are also present). No N11/N12/N13/
N15 audio/HUD regression was observed; the N15 check remains green and its capture artifacts were
regenerated by the N15 check after the N16 animation-player additions.

## 6. Gaps

* The plan VM has no ported match-stat read handler. The accumulator exposes real values through
  MatchStatAccumulator.GetStat/GetStats and the N16 driver, but no plan decision currently reads a
  match stat. The deterministic scenario exercised only StatID 37 (MATCH_STAT_POSS_START); the
  full switch is implemented but other ids need a plan window that calls them.
* There is no per-player BoundBubble/colObject collision shape in the Sim. The action-71 radius is
  exposed by MatchBubbleAdapter only (GetRadius/CountAwarenessNeighbours); the Sim collision shell
  and response are unchanged. This is an explicit adapter, not a faked collision effect.
* Animation triggers are re-injected only when their action name maps through
  PlanOpcodes.MessageNames. Sync triggers take the player's hard sync transition. Contact/Catch/
  release are not id_MSG_ messages and remain trigger telemetry; YerDown has no Ported message-table
  entry and remains unmapped.
* Next-segment transitions are implemented and observed (AA_CelbAjdn segmentTransitions=1). Sync
  transitions are hard switches: there is no crossfade/blend interpolation in the CPU pose path.
  Animation-driven physics force is not wired.
* The stat accumulator's PlanVM telemetry carries one target/stat pair per tick; multiple different
  stat ids in one tick cannot be separated. Observed cases were one per tick (multiplePerTick=0).
* Effects are off for deterministic N4-N15 capture/display paths by design; preview010's short
  interactive window did not hit action 9 or a non-sync message trigger. The deterministic Editor
  probe is the action-9/message evidence.
* The captures are staged pose evidence, not gameplay-camera frames. The preview package is an
  unsigned untracked dev build, not an installer.

## 7. Files changed

New: Native/MatchStatAccumulator.cs, MatchBubbleAdapter.cs, MatchAnimationMessageBridge.cs (+
.meta), docs/NATIVE-EFFECTS.md, artifacts/native-effects-00.png,
artifacts/native-preview-010-effects.png.
Edited: Native/MatchAnimationPlayer.cs, MatchAnimationConsumer.cs, MatchAnimationPreviewProbe.cs,
Assets/Editor/UBlade3MatchPlay.cs. No Data/Assets/Sim/PlanVM/App/ProjectSettings/native release files
were edited by this slice.
