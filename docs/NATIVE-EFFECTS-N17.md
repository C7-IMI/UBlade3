# NATIVE-EFFECTS-N17 - MatchStats read seam, Sim awareness bubbles, animation trigger map

N17 closes the remaining effect-consumer gaps recorded by N16: the plan context now reads the
MatchStatAccumulator table through a documented read seam, action-71 radii are bound to a real
Sim awareness-bubble/collision query (not the N16 adapter alone), and the remaining AnimAssm trigger
names are classified exactly (YerDown mapped/injected; Contact/Catch/release are not id_MSG_ symbols).

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Original data read-only;
Bubble API in Sim is additive. Nothing committed; no release/player package was built by this slice.

## 1. MatchStats: sources and the read seam

Decomp audit (`third_party/hyperblade-decomp/src`): PlanAction::IncrementStat (plancond.cpp
0x00461d20), StartStatTimer (0x00461dc0), EndStatTimer (0x00461e30) and NoteStats4Possessors
(empty inline body) are the PlanAction bodies that touch MatchStats. SyncPossession (0x0045e770)
writes the packed possession row. The only MatchStats::GetAt reader in the decomp is
RokObject::Postpass (engine/rokobject.cpp 0x00473cc0); **no PlanCond/PlanAction body calls
MatchStats::GetAt or otherwise reads the table in the decomp.** That is reported rather than
inventing a read body.

N17 implementation:

* `PlanVM/PlanAiMatchStats.cs` exposes `PlanAiMatchStats.Bind/Read/Available`, with read telemetry
  (`Reads`, `LastSlot`, `LastStatId`, `LastValue`), and `ProbeDecision`/`ProbeDecisionAt` as the
  **diagnostic** plan-context decision read body (no shipped plan id is registered for it).
* `PlanAiMatchStatsActions` ports the documented action bodies 8/10/11 (8 records its empty source;
  10/11 only act on the source's pronoun target and forward the stat id). `PlanAiActions` registers
  8/10/11. `MatchStatAccumulator` gained exact `MatchStats::StartTimer`/`StopTimer` bodies
  (engine/matchstats.cpp 0x00491c90/0x00491cd0) and consumes their plan requests.
* `MatchPlanAiRoster` owns the default-match `MatchStatAccumulator` and ticks it before the plan
  adapters; its constructor binds the accumulator's `GetStat` into `PlanAiMatchStats`. The
  MatchEffectsDriver reuses the roster accumulator instead of creating a second table.
* `MatchPlanAi.ProbeMatchStatDecision(slot, statId)` runs the diagnostic decision read against the
  live table; the probe proves a plan-context decision-path read of a real accumulated value.

Evidence: action 9 executed once, MatchStats id 37 (MATCH_STAT_POSS_START) at slot 0 = 2199;
the diagnostic decision read returned branch 1, reads=1. `timerEvents=0` in this short default
window: actions 10/11 were not reached by the current plan set, so their exact bodies are ported
but not observed in this probe.

## 2. Sim awareness bubbles and action 71

Source: PlanAction::ScaleBubToRating (plancond.cpp 0x00461ea0) writes `BoundBubble::m_flRadius` on
`GetColObjPtr(colObject)`; the six awareness collision objects are COB_DR0_AWARENESS (0x29)
through COB_DR5_AWARENESS (0x2e) in engine/collisionobjectids.h / DATA/ENUMIDS.H.

N17 adds `Sim/AwarenessBubbles.cs` (additive, no existing Sim file changed):
* `SetRadius(sim, slot, radius)` / `GetRadius` with NaN/infinity rejection, keyed by `PlayerState`;
* `TryGetSlotForCollisionObject` maps the six documented COB ids to player slots;
* `NearestOpponent(sim, slot, ...)` — the nearest opposing body inside the actor's bubble;
* `CollectCollisions(sim, list)` — all player/other pairs inside the owner's bubble, with distance,
  radius and teammate/opponent parity. The original shape is a bound bubble/capsule; this bounded
  port treats the radius as a sphere around the body centre and documents that approximation.
`MatchPlanAi.SyncPlanActionEffects` now maps the action's colObject to the real slot and calls the
Sim API; `MatchBubbleAdapter` also writes the Sim API and counts its overlaps from the Sim query.
The N16 adapter-only path is superseded for radius ownership/telemetry.

Evidence: after the default run, slot 4 has radius 30 from action 71; the Sim query reports 2
pairs and nearest opponent slot 5 at distance 7.5. Setting all radii to zero makes the pair count
0, then the action-derived radii are restored. The probe's staged pair proves the radius changes
the Sim collision/nearest query, not just adapter telemetry.

## 3. Animation triggers

`AnimAssm.txt` has six trigger action names: id_MSG_YerDown (29), Contact (23), release (19),
id_MSG_YerHit (19), id_MSG_SyncFrame (10), Catch (3). `AnimScriptTable::ParseScriptLine`
(0x004183e0) resolves a trigger name through the plan symbol table only when it has the id_MSG_
prefix; otherwise it stores MSG_NOMSG. Therefore Contact/Catch/release are **not plan messages** and
the original does not post them; they are classified/counted exactly and never injected.

N17's `MatchAnimationMessageBridge` adds the missing `YerDown` alias (DATA/ENUMIDS.H id 56, between
Breakaway 55 and ExitBehavior 57) plus explicit non-message classification. Evidence fixtures:
YerDown -> AA_Htdwnflp injected +1; Contact -> AA_ClthslneL nonMessage +1, injected +0;
Catch -> AA_Rechigh nonMessage +1; release -> AA_BkhndThrw nonMessage +1. The bridge reports
injected=1, mapped=1, yerDownInjected=1, nonMessage=3, unmapped=0 for this run. No unmapped id_MSG_
trigger remains in the shipped trigger table; sync triggers are handled by the player's sync path.

## 4. Deterministic CheckN17 evidence

`CheckN17` builds the normal default build, enables the animation consumer, stages the N11 default
scenario, drives 240 plan ticks, reads the real MatchStats table through the plan context, binds/
queries Sim awareness bubbles, and plays one real clipping fixture per trigger name. It was run twice;

both runs produced the same counts (no digest counter is emitted by this check):

    UBLADE3_NATIVE_MATCH_N17_STATS ... applied=1 ... lastStat=37 lastValue=2199 [slot=0 nonzero=1 37=2199]
    UBLADE3_NATIVE_MATCH_N17_STAT_DECISION slot=0 stat=37 value=2199 branch=1 reads=1 last=0/37/2199
    UBLADE3_NATIVE_MATCH_N17_BUBBLE owner=4 radius=30 pairs=2 nearest=1/5/7.5
    UBLADE3_NATIVE_MATCH_N17_TRIGGER_FIXTURE action=id_MSG_YerDown ... injected+1 nonMessage+0
    UBLADE3_NATIVE_MATCH_N17_TRIGGER_FIXTURE action=Contact ... injected+0 nonMessage+1
    UBLADE3_NATIVE_MATCH_N17_TRIGGER_FIXTURE action=Catch ... injected+0 nonMessage+1
    UBLADE3_NATIVE_MATCH_N17_TRIGGER_FIXTURE action=release ... injected+0 nonMessage+1
    UBLADE3_NATIVE_MATCH_N17_COUNTS statApplied=1 statReads=1 statBranch=1 bubbleOwner=4
      bubblePairs=2 bubbleCollisions=2491 messagesInjected=1 yerDownInjected=1 nonMessageTriggers=3
      failures=0
    UBLADE3_NATIVE_MATCH_N17_CHECK_OK

## 5. Regressions

N4-N16 batch (all exit 0): N17 twice, N16 CheckEffects, N15 CheckAnimation, N14-N7, N6 Check,
CheckDisplay, CheckHud, and UBlade3Checks.RunAll -> UBLADE3_CHECKS_OK. The N16 effects check still
passes with the roster-owned accumulator and Sim bubble API. python scripts/check_all.py PASS.

## 6. Honest gaps

* No decompiled PlanCond/PlanAction MatchStats **read** body exists. The read seam is wired and
  verified with a clearly-labelled diagnostic decision body; no shipped decision id was invented.
* MatchStats timer action bodies 8/10/11 are ported but timerEvents=0 in this default window; a plan
  window that executes Start/EndStatTimer is still needed to exercise the timer path.
* The awareness bubble is a sphere approximation of the original bound bubble/capsule; it does not
  resolve a physical collision response or damage/attack outcome, only the radius, nearest opponent
  and collision/overlap query/telemetry. The Sim player collision shell remains otherwise unchanged.
* The action state still carries one LastBubble/LastStat pair per tick, so multiple different ids in
  one tick cannot be separated (N16 limitation); observed N17 cases are single per tick.
* Animation next-segment/sync transitions are still hard switches; no crossfade/blend interpolation
* Audio is unchanged from N16 (headless record-only; interactive uses AudioService).
* No player/release package was built or claimed; preview 011 is left to the Lead.

## 7. Files

New: `PlanVM/PlanAiMatchStats.cs`, `Sim/AwarenessBubbles.cs`, `docs/NATIVE-EFFECTS-N17.md`.
Edited additively: `PlanVM/PlanAiActions.cs` (actions 8/10/11), `PlanVM/PlanAiMatchActionState.cs`
(colObject/timer state), `PlanVM/PlanAiMatchActions.cs` (colObject on action 71),
`Native/MatchStatAccumulator.cs` (plan binding + exact timer bodies), `Native/MatchPlanAi.cs`
(Sim bubble write/telemetry + diagnostic stat decision), `Native/MatchPlanAiRoster.cs` (roster
MatchStats), `Native/MatchBubbleAdapter.cs` (Sim API consumer), `Native/MatchAnimationMessageBridge.cs`
(YerDown/non-message map), `Native/MatchAnimationMessageBridge.cs` driver reuse, and
`Assets/Editor/UBlade3MatchPlay.cs` (CheckN17). Nothing committed.