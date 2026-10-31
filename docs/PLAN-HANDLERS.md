# PLAN-HANDLERS - N12 handler coverage from observed telemetry

N12 implements the plan handler bodies the N9/N10 default roster actually reached but that had explicit
unsupported telemetry. The scope is a bounded coverage increase, not full PlanCond/PlanAction parity.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Original data read-only; no
Data/Assets/Sim/App/ProjectSettings/native-release changes. No commit; the Lead commits.

## 1. Implemented ids and citations

New PlanVM files: `PlanAiMatchActions.cs`, `PlanAiMatchDecisions.cs`, `PlanAiMatchContext.cs`,
`PlanAiMatchActionState.cs`. Existing edits are additive: `PlanAiActions` registers the action bodies;
`MatchPlanAi` publishes match context, dispatches the new state to the real Simulation body, and registers
the decision bodies; `PlanAiDecisionExtensions`/`PlanAiActions` remain the existing seams.

| Id | Body / citation | Match effect in N12 |
| --- | --- | --- |
| action 6 SetSpeedBurst | PlanAction::Set_SpeedBurst (plancond.cpp HYPERX 0x0045eb30) | real friction 0.1, axis.x += 0.25, velocity = velDir when tiny, burst target/boost state recorded; 6 executions |
| action 7 PlaySound | AnimEvent::PlaySnd (animevent.cpp 0x00406ea0) | sound index/request recorded; no sound bank in the mock, no fake audio; 1 execution |
| action 9 IncrementStat | PlanAction::IncrementStat (plancond.cpp 0x00461d20) | target slot/stat request recorded; match stat accumulator is not ported; 1 execution |
| action 18 SetLocation | PlanAction::SetLocation (plancond.cpp 0x0045efc0) | xy float form writes body position and is applied to the real player body; marker/pronoun form (MyGoalPos) recorded as unresolved; 4 executions, 0 marker relocations |
| action 29 SetFriction | PlanAction::SetFriction (plancond.cpp 0x00463790) | real body friction = raw*0.01; steerScale = 1 when raw > 25; 7 executions, 7 applied |
| action 30 SetAnimation | PlanAction::SetAnimation (planactionanim.cpp 0x00401000) | animation index/rate/flags state recorded; animation registry/clip port is absent, no fake rendering; 7 executions |
| action 71 ScaleBubToRating | PlanAction::ScaleBubToRating (plancond.cpp 0x00461ea0) | radius = (high-low)*(stat*0.1)+low computed and recorded; collision bubble port absent; 2 executions |
| decision 6 IsSenderTeammate | PlanCond::IsSenderTeammate (plancond.cpp 0x00462780) | sender slot from the drained scene message via PlanAiMatchContext; 3 executions |
| decision 23 ClosestUncoveredOppn | PlanCond::ClosestUncoveredOppn (plancond.cpp 0x004615e0) | teammate behaviour kind/aim published from the compiled plan block; source near/far and damage-gate branches; 6 executions |
| decision 68 LooseRokWhatToDo | PlanCond::LooseRokWhatToDo (plancond.cpp 0x00467c30) | score via PlanAiMatchContext.IsTeamWinning, four free-ROK ratings copied from the real Sim stats, near/far free-ROK and damage-gate branches; 8 executions |

The remaining unsupported telemetry observed after N12 (per-slot slot lines): action 84, agenda items 8/15,
behaviour parameters 0,2,3,9,10,11,12,15, decisions 51/71/120, plus action 139 (plan shot intent). Those
are reported, not silently dropped; the pre-N12 action ids 6/7/9/18/29/30/71 and decisions 6/23/68 no
longer appear as unsupported ids in the probe.

## 2. Context/seam additions

* `PlanAiMatchContext` carries the live match score (home slots 0/2/4), the scene-message sender
  (MsgInteractor::GetTargetSlot), the per-slot speed ratio (SpeedBurst), teammate behaviour kind/aim
  (ModsTweenNFacing operand read structurally by MatchPlanAi, because behaviour-subclass state itself is
  not ported), arena terrain-Z and position-marker callbacks; `MatchPlanAi` populates it from the real
  Simulation/roster each tick.
* `PlanAiActionState` is keyed by PlanAiObject and holds friction/location/velocity/spin/animation/
  sound/stat/bubble requests; `MatchPlanAi.SyncPlanActionEffects` applies the real-body subset
  (friction, xy SetLocation, speed-burst velocity/axis) after the plan message drain and keeps counters.
* The decision registration uses the existing `PlanAiDecisionExtensions` table; the action registration
  uses the existing `PlanAiActions` table. `MatchPlanAi` calls `PlanAiMatchDecisions.EnsureRegistered()`.
* No existing PlanVM decision/action body was rewritten; the pinned four decisions and three actions keep
  their behaviour and self-check expectations.

## 3. Deterministic N12 probe

`CheckN12` (one Editor file, no MatchPlay edit) builds the normal default scene, stages the N11
free-ROK scenario, drives 140 ticks through the real `MatchPlayControl`, and aggregates the per-slot
plan action/decision counts + action-effect counters from the five roster adapters.

Evidence (artifacts/ublade3/n12-check-2.log):

    UBLADE3_NATIVE_MATCH_N12_RESULT defaultPlanAi=True targetPhysics=True ticks=140 aiPickups=1 aiPasses=1
        actions=[6x6,7x1,9x1,18x4,29x7,30x7,71x2]
        decisions=[6x3,23x6,68x8]
        effects=[friction=7,location=0,animation=7,sound=1,stat=1,bubble=2,speedBurst=6,
                 unsupportedDecisionErrors=7]
        planIds=[CarryRok,Defense,Goalie,OffWing] digest=333565d513ad20e0
    UBLADE3_NATIVE_MATCH_N12_CHECK_OK

Slot-level evidence: slot 1 (Defense/CarryRok) executes actions 5/6/7/9/29/30/93/103 and decisions
6/15/18/23/50/68/78/97/98/111; slot 2/3 Defense executes 6/29/30 and decisions 6/23/68 (slot 3 also
71); both goalies execute 18x2 and 71x1. The new action ids are no longer in their unsupported lists.
A second independent CheckN12 run produced the same digest and counts (see verification), so the probe is
deterministic for the fixed default-roster staging.

## 4. Verification and remaining gaps

| Gate | Result |
| --- | --- |
| N12 CheckN12 | UBLADE3_NATIVE_MATCH_N12_CHECK_OK, mismatches=0 (evidence above) |
| dotnet PlanVM core build | PlanVmCore.csproj build succeeded, 0 warnings/errors |
| N12 determinism | two independent CheckN12 runs: same digest 333565d513ad20e0, same action/decision counts |
| N11 regression | UBLADE3_NATIVE_MATCH_N11_CHECK_OK |
| N10 regression | UBLADE3_NATIVE_MATCH_N10_CHECK_OK |
| N9 regression | UBLADE3_NATIVE_MATCH_N9_CHECK_OK |
| N8 regression | UBLADE3_NATIVE_MATCH_N8_CHECK_OK |
| N7 regression | UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 regression | UBLADE3_NATIVE_PLAY_CHECK_OK |
| N4/N5 + PlanVM/Data | UBLADE3_CHECKS_OK (PlanVM probe 123/123, plans=32 instructions=9420 opcodes=16 digest=141a750a3de1e083) |
| Fast checks | python scripts/check_all.py PASS |

Honest gaps: SetLocation's marker/pronoun form (MyGoalPos) resolves to no match position-marker table,
so only its float form relocates a player and `locationApplied` stayed 0; the goalie body executes but
does not teleport to a marker. Animation, sound, match-stat and collision-bubble bodies have no engine
counterparts in this slice, so they record plan state/telemetry only and do not fake rendering/audio/
stats/bubbles. Decisions 51/71/120 and the remaining behaviour params/agenda items are still explicit
unsupported telemetry; behaviour-subclass state (purge tables, actual aim-object slots beyond the
published kind/aim pair, animation clips) is not ported. Per-slot mock worlds mean decision 23 uses
one-tick-old published teammate metadata rather than a shared engine world; this is documented, not
hidden. Full handler parity is not claimed; the N12 probe is one bounded default-roster path.

## 5. Files

New: `PlanVM/PlanAiMatchActions.cs`, `PlanVM/PlanAiMatchDecisions.cs`, `PlanVM/PlanAiMatchContext.cs`,
`PlanVM/PlanAiMatchActionState.cs`, `docs/PLAN-HANDLERS.md`. Modified additively: `PlanVM/PlanAiActions.cs`
(7 registrations), `Native/MatchPlanAi.cs` (context publish, sender, effects sync, decision registration,
counters), `Assets/Editor/UBlade3MatchPlay.cs` (CheckN12 + N12 probe). No Data/Assets/Sim/App/
ProjectSettings/native-release edits; nothing committed.