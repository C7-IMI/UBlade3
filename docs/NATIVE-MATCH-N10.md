# NATIVE-MATCH-N10 - the default PlanVM AI in the playable native C# match

N10 makes the whole-match PlanVM roster the **default interactive AI**. `MatchPlay.Build` now attaches
one persistent shipped-plan instance per non-human slot (AI skaters 1/2/3 and goalies 4/5) on the
normal build path, so a plain player session - not only a capture suite - runs Defense/CarryRok/Goalie
plans with possession plan changes, plan passes and a plan-driven shot. The heuristic
`MatchAiController` remains only as the explicit fallback (pickup approach and a slot whose plan cannot
bind); its update/request path is suppressed for plan slots and every fallback tick is counted.
Bounded and honest: unported handler bodies remain explicit telemetry, the motion is the labelled
held-mask bridge, and the plan shot path uses one documented animation bridge for the missing cell event.
This is the first default PlanVM-AI playable match, not full parity.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. No commit; the Lead commits.
Original game data was read-only; the 20XX runtime, native plugin and release path are untouched.

## 1. What changed in the default build

* `MatchPlay.Build` attaches `MatchPlanAiRoster` for slots 1-5 after creating the control, via
  `MatchPlayControl.AttachDefaultPlanAi(scene.World, dataPath)`. The AI pickup controller is attached
  with slots 1/2/3 so the real RokInRange/Attach rule can see all three AI skaters; the roster's
  `SuppressSlot` predicate keeps `MatchAiController.Update` from requesting heuristic shots/passes or
  steering plan slots.
* `InputForSlot` resolves a roster slot through its plan adapter first; every plan instance is bound
  when the roster is constructed (before the first live tick), so no heuristic takeover is needed for
  movement. If a plan cannot bind, `MatchPlanAi.GetInput` returns the heuristic input and counts a
  labelled `heuristicFallback` tick; there were **0** such ticks in the measured default run.
* The default roster injects the initial free-ROK plan message (`id_MSG_RokIsFree`) on the first live
  tick (the explicit N9 roster keeps its old no-initial-event semantics). This is what makes the
  default AI skaters switch from Defense ManOnMan to LooseRokAttackOpponent and pursue the loose ROK
  at the opening face-off.
* Default roster plans are deterministic: the roster seeds `PlanAiRandom` from a fixed seed only for the
  default-roster constructor, so two normal-build runs replay the same decisions. The explicit N9
  roster keeps its previous sequence/evidence.
* N6/N7/N8 deterministic capture runners detach the default roster before their explicit scripts; the
  N9 runner detaches and rebuilds its explicit roster. Their headless checks and prior capture
  semantics are preserved (see section 6).

## 2. Plan mapping (unchanged from N9, now the default)

`Cmd_SetPlans` appends each HTM `SetPlans` list to the ObjectDef table; MatchWorldData keeps it on
`MatchPlayerSlot.Definition.Plans`. `EngineObject::SelectAndActivatePlan` picks `Defense` behaviour 0
for a non-goalie AI skater, plan table index 0 (`Goalie`) behaviour 0 for a goalie, and `Human` for the
human slot. The N10 run's tables were slot 1 [Defense,Human,CarryRok,OffWing,Sideln], slot 2
[Defense,CarryRok,Human,OffWing,Sideln], slot 3 [Defense,CarryRok,OffWing,Human,Sideln], both goalies
[Goalie]. GotIt requests `CarryRok 1 GoForGoal` (adopted at the documented 1->5 TurnToMate entry);
LostIt uses TeammateHasRok to return to Defense/OffWing; the plans' own message/agenda/timer machinery
runs each tick. OffWing/Sideln are named and adoption-compatible but were not requested by the bounded
default run; this remains a reported gap rather than a synthetic change.

## 3. Plan shot path (new, bounded)

The default run reaches CARRYROK `Throwing`. The CARRYROK plan's Throwing behaviour authors
`SET_BEHAVIOR_PARAM id_SET_AnimationEvent` and its `id_MSG_Extra0` handler runs
`DO_ACTION SendLaunchMessage` (action id **139**). The animation port supplies no cell event, so the
default roster enables a **labelled animation bridge** (default roster only; the explicit N9 roster
keeps it off): while the carrier's current behaviour is Throwing, the adapter delivers the authored
Extra0 (message 123) once after a fixed 100 ms bridge delay. The plan's own Extra0 handler then executes
action 139; the adapter's existing carrier-only shot mapping turns that unported action's intent into
`MatchPlayControl.RequestAiShot`, whose body is the shipped `MatchPossessionRules.LaunchAiShot` port.
Result: `planShotRequests=1`, `aiShots=1`, action 139 appears in slot 3's explicit unsupported-action
telemetry, and the captured frame 05 shows the ROK in flight. Action ids 3/37/38 remain mapped but were
not reached; a goalie LaunchAt is explicitly excluded from the shot mapping because it launches the
goalie, not the ROK. The 100 ms bridge is a documented stand-in for the missing animation event, not
engine animation timing, and the shot aim is the existing match shot math rather than an unported plan
aim (the plan supplied the launch intent/animation event, not a target vector).

## 4. Deterministic default-match probe

`Native/MatchPlayN10Script.cs` is the default-path probe: it **never** attaches or replaces the roster.
It requires that the normal build already attached the default PlanVM AI, stages deterministic
positions (slot 1 on the ROK spawn, pass target slot 3 at (0,120), human and target held; labelled
staging only) and drives the same control the player uses for a bounded 160 ticks. It asserts: default
roster present and attached by Build, five simultaneously bound plan instances, every slot bound with
planTicks>0 at the end, heuristic fallback ticks bounded, heuristic update suppression, AI pickup plus a
plan pass, plan id coverage, relay budget and tick bounds.

Headless evidence (artifacts/ublade3/n10-check-5.log):

    UBLADE3_NATIVE_MATCH_N10_CHECK root=D:\Games\HyperBlade\HYB\data ... captureFlag=False no20xx=True
    UBLADE3_PLAN_AI_ROSTER slots=5 active=5 maxActive=5 planIds=[CarryRok,Defense,Goalie]
        ticks=160 planTicks=800 rebinds=4 relays=8 relayBudgetHits=0 possessionEvents=5
        gotIt=2 lostIt=2 rokIsFree=3 planPassRequests=1 planShotRequests=1 unsupportedDecisions=18
        runtimeErrors=0 heuristicFallbackTicks=0 animationBridgeEvents=49 initialRokEvent=True
        maxRebindsPerSlot=2
    UBLADE3_PLAN_AI_SLOT slot=1 plan=Defense planIds=[Defense,CarryRok]
        behaviors=[LooseRokAttackOpponent>TurnToMate>PassingPlay>PassingPlayPass>ManOnMan>...]
        planTicks=160 rebinds=2 passRequests=1 shotRequests=0 heuristicFallback=0
    UBLADE3_PLAN_AI_SLOT slot=2 plan=Defense planTicks=160 heuristicFallback=0
    UBLADE3_PLAN_AI_SLOT slot=3 plan=Defense planIds=[Defense,CarryRok]
        behaviors=[LooseRokAttackOpponent>TurnToMate>PassingPlay>Throwing>ManOnMan]
        planTicks=160 rebinds=2 shotRequests=1 unsupported=[...,action:139,...]
    UBLADE3_PLAN_AI_SLOT slot=4/5 plan=Goalie behaviors=[SitInGoal] decisions=[14x1]
    UBLADE3_NATIVE_MATCH_N10_CHECK_RESULT defaultPlanAi=True aiPickups=2 aiPasses=1 aiShots=1
        pickupTick=1 passTick=50 planPassRequests=1 planShotRequests=1 maxActiveSlots=5 activeAtEnd=5
        planIds=[CarryRok,Defense,Goalie] rebinds=4 relays=8 relayBudgetHits=0
        unsupportedDecisions=18 heuristicFallbackTicks=0 suppressedUpdateTicks=480 ticks=160
        planDigest=67159e33ed58442d failures=0
    UBLADE3_NATIVE_MATCH_N10_DETERMINISM ok=True digest1=digest2=67159e33ed58442d
    UBLADE3_NATIVE_MATCH_N10_CHECK_OK

The default plan instances are the proof of the mission's default requirement: `defaultPlanAi=True`, five
active slots on slots 1-5, plan ticks/behaviours/actions/decisions per slot, `heuristicFallbackTicks=0`,
`suppressedUpdateTicks=480` (three heuristic update passes skipped over 160 live ticks), two AI
pickups, one plan pass and one plan shot.

## 5. Captures

Normal-player capture: `--n10-capture=` (no N9 flag) builds the same default match in the Editor and in
the Windows player, then runs the N10 script. Seven frames at 1280x720:

    artifacts/native-match-n10-00-start.png
    artifacts/native-match-n10-01-plans-active.png
    artifacts/native-match-n10-02-ai-pickup.png
    artifacts/native-match-n10-03-plan-switch.png
    artifacts/native-match-n10-04-plan-pass.png
    artifacts/native-match-n10-05-plan-shot.png
    artifacts/native-match-n10-06-result.png

Editor `CaptureAllN10`: UBLADE3_NATIVE_MATCH_N10_CAPTURES_OK frames=7; evidence defaultPlanAi=True
aiPickups=2 aiPasses=1 aiShots=1 planPassRequests=1 maxActiveSlots=5 activeAtEnd=5
planIds=[CarryRok,Defense,Goalie] heuristicFallbackTicks=0 suppressedUpdateTicks=480
planDigest=67159e33ed58442d. Frame 04 shows ROK IN FLIGHT after the plan pass; frame 05 shows the ROK in
flight from the plan's SendLaunchMessage shot; frame 06 is the follow-up result.

Windows player `BuildAndRunWindowsN10`: UBLADE3_NATIVE_MATCH_N10_PLAYER_CAPTURES_OK frames=7; the player
log reports the same result/counts/digest (defaultPlanAi=True, aiPickups=2, aiPasses=1, aiShots=1,
maxActiveSlots=5, planPassRequests=1, planShotRequests=1, heuristicFallbackTicks=0,
suppressedUpdateTicks=480, planDigest=67159e33ed58442d), and all seven player PNGs are byte-identical
to the Editor frames.

## 6. Verification and preserved suites

| Gate | Marker / result |
| --- | --- |
| N10 default headless (CheckN10) | UBLADE3_NATIVE_MATCH_N10_CHECK_OK, mismatches=0, deterministic digest 67159e33ed58442d |
| N10 Editor capture | UBLADE3_NATIVE_MATCH_N10_CAPTURES_OK frames=7 |
| N10 Windows player capture | UBLADE3_NATIVE_MATCH_N10_PLAYER_CAPTURES_OK frames=7, same counts/digest |
| N9 regression | UBLADE3_NATIVE_MATCH_N9_CHECK_OK |
| N8 regression | UBLADE3_NATIVE_MATCH_N8_CHECK_OK |
| N7 regression | UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 regression | UBLADE3_NATIVE_PLAY_CHECK_OK |
| Legacy captures rechecked | N9 UBLADE3_NATIVE_MATCH_N9_CAPTURES_OK frames=6, N8 UBLADE3_NATIVE_MATCH_N8_CAPTURES_OK frames=6, N7 UBLADE3_NATIVE_MATCH_N7_CAPTURES_OK frames=12, N6 UBLADE3_NATIVE_PLAY_CAPTURES_OK frames=7 (frames restored to their committed evidence; the N9 capture log's plan digest changes only because the N10 trace/telemetry text is wider, its six frames are unchanged) |
| N4/N5 + PlanVM/Data | UBLADE3_CHECKS_OK (UBLADE3_PLANVM_OK plans=32 instructions=9420 opcodes=16 digest=141a750a3de1e083) |
| Fast checks | python scripts/check_all.py PASS (176 text files) |

Preservation detail: the N6/N7/N8 capture runners call `DetachPlanRoster()` before their explicit
scripts (N7 also restores its original AI slot list), and the N9 script detaches the build-time default
roster and rebuilds its explicit roster with the old semantics (no initial free-ROK event, animation
bridge off). Their headless checks pass unchanged and the existing N4-N9 capture artifacts/logs are
left in place. Only the default interactive/N10 path uses the new roster defaults.

## 7. Default behavioural changes and fallback policy

* The default AI is now plan-driven on every non-human slot; the heuristic controller only supplies the
  pre-possession pickup approach if a plan instance is not yet bound, and any such use is counted as
  heuristicFallback telemetry. In the measured run it was zero.
* Unported decisions are caught per plan instance and recorded per slot; no branch is guessed and the
  current behaviour is kept. Unported actions/behaviour parameters/agenda items are recorded as before;
  behaviour motion kinds without a plan target use the labelled fallback motion to the skater's goal.
* The default opening free-ROK plan message changes the first behaviour of the AI skaters from
  ManOnMan to LooseRokAttackOpponent; that is the plans' own reaction to the engine-style event.
* The 100 ms animation bridge is default-roster only and counted (`animationBridgeEvents`); it is not
  applied to the explicit N9/N8/N7/N6 suites.
* Plan-to-plan messages are relayed with the N9 budgets (64/tick, 2048 total), and plan changes are
  coalesced to the last request per drain (N9 behaviour, documented gap).
* Match flow (goals/face-off/celebration) is unchanged; plan instances are not ticked outside Live and
  possession events are re-injected when live play resumes.

## 8. Files and remaining gaps

New: Native/MatchPlayN10Script.cs (and .meta), docs/NATIVE-MATCH-N10.md.
Modified additively: Native/MatchPlay.cs (default roster in Build, detach in N6/N7/N8 runners, N10
capture/command suite), Native/MatchPossession.cs (default attach/detach, defaultPlanAi and fallback
telemetry, roster wiring), Native/MatchPlanAi.cs (persistent fallback counter, animation bridge for the
plan shot), Native/MatchPlanAiRoster.cs (initial free-ROK event, default RNG seed, animation-bridge
switch, fallback counters), Native/MatchPlayN9Script.cs (detach an existing default roster before its
explicit roster), Native/MatchAiController.cs (suppressed-update telemetry), Assets/Editor/UBlade3MatchPlay.cs
(CheckN10/CaptureAllN10/BuildAndRunWindowsN10 and the N10 artifact list).
Remaining gaps: the N9 list still applies - most PlanCond/PlanAction bodies (attack/weapon, animation,
sound, stats, substitution, injury, celebration, end of period, goalie save/launch) are unported and
reported; exact behaviour steering/collision/agenda classes are not ported; only SET_Target motion is
bridged; collision-driven transitions are not fed; OffWing/Sideln adoption is not exercised by the
bounded run; plan changes are coalesced; the shot path depends on the labelled animation bridge and the
match shot aim; the relay is a bounded approximation. Full AI parity and release claims are not made.