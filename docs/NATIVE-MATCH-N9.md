# NATIVE-MATCH-N9 - the whole-match PlanVM AI roster

N9 generalises the N8 single-flow adapter into a **whole-match plan roster**: one persistent shipped
PlanVM instance runs for every non-human slot (three AI skaters + two goalies), over each slot's own
`SetPlans` table from the team file. The carrier's engine-plan change reaches CARRYROK and the plan's
own TurnToMate -> PassingPlay -> pass chain throws a real match pass; LostIt then returns the slot to
a non-carrier plan; every goalie runs its GOALIE plan. Plan changes, cross-slot plan messages,
behaviours, agenda/timers and actions/decisions all stay inside the real UBlade3.PlanVM. It is still
**bounded and honest**: many decision/action bodies remain unported and are explicit telemetry, and
motion uses the documented input-mask bridge. It is not full plan-VM AI parity or a release gate.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. No commit; the Lead owns the
commit. Original game data stayed read-only. Nothing in the 20XX runtime, native plugin or release
path was touched.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| third_party/hyperblade-decomp | ObjectDef::Cmd_SetPlans (0x00484460), EngineObject::SelectAndActivatePlan (0x00471b60) and InstantiateAIPlan (0x00471da0), MsgInteractor::Process's plan change (0x0046bd..), PlanCond/PlanAction bodies, message ids, behaviour ids. |
| third_party/hyperblade-20XX (plan_dump.py) | Decoded shipped plans: the plan tables' referenced names and every behaviour/message/handler shown below come from the shipped PBN bytes. |
| D:\Games\HyperBlade\HYB\data | The read-only shipped install every run reads (HTM team files, PLANS/*.PBN). |
| unity/UBlade3/Assets/Scripts/UBlade3/PlanVM | The verified PBN loader/parser, PlanState/behaviour/agenda/timer/interactor core and the N5 Plan AI slice. N9 adds only a new decision-extension file and a two-line host fallback; the four pinned Plan AI decisions and their 40 self-checks are unchanged. |
| unity/UBlade3/Assets/Scripts/UBlade3/Native | N4-N8 native match, Sim bridge, MatchPlanAi (N8) and MatchPlayControl. |

## 2. Plan selection from the shipped data (mapping)

`Cmd_SetPlans` appends each `SetPlans` token list, in file order, to the ObjectDef's plan table. The
team file's `DefineObject type=Dromer` blocks own those lists; MatchWorldData already keeps the parsed
ObjectDefinition on `MatchPlayerSlot.Definition`, so the roster reads the real tables (`Plans`).
EngineObject::SelectAndActivatePlan then chooses the starting plan:

* a **non-goalie AI skater** searches its table for `"Defense"` (case-insensitive) and activates behaviour 0;
* a **goalie** instantiates plan table **index 0** and activates behaviour 0;
* the human Dromer slot uses `"Human"`; N9 leaves slot 0 human and gives it no plan instance.

The tables observed in the N9 run (chico.htm home vs seattle.htm away):

    slot 1 (AI skater)  [Defense, Human,   CarryRok, OffWing, Sideln]  -> Defense:0
    slot 2 (AI skater)  [Defense, CarryRok, Human,   OffWing, Sideln]  -> Defense:0
    slot 3 (AI skater)  [Defense, CarryRok, OffWing, Human,   Sideln]  -> Defense:0
    slots 4/5 (goalies) [Goalie]                                       -> Goalie:0

Role changes are not invented: the plans themselves request them with `CHANGE_OF_PLAN name behavior`
(resolved against the owner's plan table by the shipped compiler/VM), and the roster adopts those
requests. Shipped examples: every skater plan's global `id_MSG_GotIt` handler requests
`CHANGE_OF_PLAN "CarryRok" 1 "GoForGoal"`; the CARRYROK `id_MSG_LostIt` handler asks TeammateHasRok and
requests `OffWing` ChooseJob or `Defense` ManOnMan; the SanityCheck/DecidePosition chain requests
Defense/OffWing/Sideln depending on the position state.

The 140-tick deterministic N9 scenario exercised the Defense -> CarryRok change (GotIt) and the CarryRok
-> Defense change (LostIt via TeammateHasRok); it did not itself produce an OffWing/Sideln request, so
those two names are mapped/resolvable and share the same adopted-change path but are not claimed as
exercised. That is reported as a gap, not filled with a synthetic plan change.

**Documented carrier entry mapping:** the engine's request for CARRYROK behaviour 1 (GoForGoal) is
recorded, but GoForGoal's Initiate path immediately needs the unported attack/weapon decision bodies
(WhichOppnToAttack, InRangeToAttack, PickAttackMove, ...). The bounded roster therefore enters
CARRYROK at the verified behaviour **5 TurnToMate** (the N8 pass path) and reports
`requested=CarryRok:1 mapped=5` in the per-slot telemetry. The plan result after that entry is the
plan's own (TurnToMate timer -> PassingPlay -> RecvrInRangeNView/OkToPassToTeammate/Incoming pass).

## 3. What is implemented

### Roster and tick

`Native/MatchPlanAiRoster.cs` owns one `MatchPlanAi` per slot in {1,2,3,4,5}. Every live match tick,
after the heuristic update and before the match's pending AI requests, the roster:

1. detects possession changes and injects the engine plan messages into the right plan instance:

   `id_MSG_GotIt` (3) to the new carrier, `id_MSG_LostIt` (2) to the previous carrier, and
   `id_MSG_RokIsFree` (13) to the five plan slots when the ROK is loose;
2. ticks each plan instance (mirror sync from Simulation, match clock, agenda/timers, per-message
   plan-message drain, plan actions/decisions, behaviour tracking);
3. relays the plan-to-plan message traffic it sees (explicit cross-slot targets and broadcasts) into
   the target plan instances with a **64 messages/tick, 2048 total budget** (hits are failures);
4. lets each adapter adopt the plan's own `CHANGE_OF_PLAN` request by rebinding that slot to the named
   shipped PBN at the requested behaviour (with the documented CARRYROK 1->5 entry mapping above).

The adapter's N8 path is unchanged: the old constructor still produces the single-slot possession-
bound CARRYROK TurnToMate flow, and N8's check/capture still pass.

### Pinned and newly ported handler bodies

| Id | Body | Status |
| --- | --- | --- |
| decision 15 DecidePosition | plancond.cpp 0x00462d20 | existing N5 body |
| decision 50 RecvrInRangeNView | plancond.cpp 0x00466b70 | existing N5 body |
| decision 78 ShootSituation | plancond.cpp 0x00468650 | existing N5 body |
| decision 111 OkToPassToTeammate | plancond.cpp 0x00462c40 | existing N5 body |
| decision 5 Random | plancond.cpp 0x0045f6.. | **new** PlanAiDecisionExtensions body |
| decision 14 IHaveRok | plancond.cpp PlanCond::IHaveRok | **new** (plain 0/1, flagged 2/3) |
| decision 18 TeammateHasRok | plancond.cpp 0x00461f.. | **new** |
| decision 33 MyID | plancond.cpp 0x00462820 | **new** |
| decision 97 AnimationSemaphore | plancond.cpp PlanAction::AnimationSemaphore | **new** (read; 2-word form writes) |
| decision 98 ClockStoppedSemaphore | plancond.cpp PlanAction::ClockStoppedSemaphore | **new** (read; 2-word form writes) |
| actions 5/93/103 | SendMsg/ReallySendMsg/SetKnockedDownState | existing N5 bodies; action 5/93 with message 20 becomes the carrier's pass request, exactly as N8 |
| action 109/111 and the rest | semaphore writes, animation, sounds, weapons, launch/throw, stats, ... | unported: explicit telemetry only |

`PlanVM/PlanAiDecisionExtensions.cs` is the N9 seam: `PlanAiHost.Decide` consults it only after
`PlanAiDecisions` misses, so the pinned four-decision slice and its self-checks stay intact. N9
registers the six bodies above from `Native/MatchPlanAiDecisions.cs`.

### Unported-handler policy (honest, bounded)

The stock mock `DrainPendingMessages` leaves a failed message at the head, which would replay an
unported decision every tick and stall the slot. The N9 persistent tick drains one plan message at a
time and catches `PlanAiUnportedHandlerException`: the handler id is recorded per slot, **no branch is
guessed**, the current behaviour is kept and the rest of the queue still runs. That is a documented
deviation from the stock mock drain and is what lets the queue reach the GotIt message after an
unported decision earlier in the same tick. It is not presented as engine behaviour.

### Motion and match effects

* movement: the plan's behaviour Target operand (SET_BEHAVIOR_PARAM id_SET_Target) is followed with the N4/N5
  held-mask steering bridge; glide kinds 0/1 move, in-place kinds 16-18 hold; every other behaviour kind
  uses the **labelled fallback toward the skater's goal** and increments telemetry;
* pass: the carrier plan's Incoming action (action 5/93, message id 20) becomes
  `MatchPlayControl.RequestAiPass` and the shipped `MatchPossessionRules.Pass` throws the ROK;
* shot: plan launch/throw action ids 3/37/38/139 are mapped to `RequestAiShot` only while the actor
  carries, including the unported-action telemetry path (a goalie `LaunchAt` is not a shot and is not
  mapped). No N9 plan path executed a shot action; the pass is the run's match effect;
* pickup/attach stays the match's real RokInRange/Attach rule; the roster starts once possession exists.

## 4. Deterministic N9 probe

`Native/MatchPlayN9Script.cs` stages the same deterministic scenario headless and in the scene:
human held at (-28,28); plan skater slot 1 on the ROK spawn (real pickup on tick 1); passing target
slot 3 held at (0,120); slot 2 at (30,-20); goalies at (0,-180) and (0,180); all six slots bound to
their shipped plan tables. Only the human and the held pass target are frozen (labelled staging);
the plans' own decisions drive everything else. The run is bounded to 240 ticks and checks: five
simultaneous bound plan instances, a plan pass, the CARRYROK plan id on the carrier, GOALIE plan ids
on both goalies, at least two plan/role rebinds, the per-slot rebind cap (8), the relay budget and the
tick cap (so no infinite plan-change/relay loop).

Headless evidence (artifacts/ublade3/n9-check-7.log):

    UBLADE3_PLAN_AI_N9_IMPL planDecisions=5,14,18,33,97,98 planActions=5,93,103
        planTables=ObjectDefinition.Plans initial=Defense:0,Goalie:0 carrier=CARRYROK mapped=1->5
    UBLADE3_PLAN_AI_ROSTER slots=5 active=5 maxActive=5 planIds=[CarryRok,Defense,Goalie]
        ticks=140 planTicks=700 rebinds=2 relays=4 relayBudgetHits=0 possessionEvents=2 gotIt=1
        lostIt=1 rokIsFree=1 planPassRequests=1 planShotRequests=0 unsupportedDecisions=9
        runtimeErrors=0 maxRebindsPerSlot=2
    UBLADE3_PLAN_AI_SLOT slot=1 plan=Defense bound=True planIds=[Defense,CarryRok]
        behaviors=[ManOnMan>TurnToMate>PassingPlay>PassingPlayPass>ManOnMan] planTicks=140 rebinds=2
        planChangesAdopted=2 actions=[5x6,93x3,103x3]
        decisions=[6x1,15x4,18x1,23x2,50x1,78x1,97x2,98x2,111x3] passRequests=1 shotRequests=0
        targets=4 fallbackMotion=91 unsupportedDecisions=3 digest=2efbe03fb09a3a4a
    UBLADE3_PLAN_AI_SLOT slot=2 plan=Defense bound=True planIds=[Defense]
        behaviors=[ManOnMan>LooseRokAttackOpponent] planTicks=140 rebinds=0
        actions=[5x2,93x2,103x2] decisions=[6x1,15x2,23x1,68x1,97x2,98x2] digest=f58a4f25d2208a7b
    UBLADE3_PLAN_AI_SLOT slot=3 plan=Defense bound=True planIds=[Defense]
        behaviors=[ManOnMan>LooseRokAttackOpponent] planTicks=140 rebinds=0
        actions=[5x2,93x2,103x2] decisions=[6x1,15x2,23x1,68x1,97x2,98x2] digest=6b5e059b220a2285
    UBLADE3_PLAN_AI_SLOT slot=4 plan=Goalie bound=True planIds=[Goalie] behaviors=[SitInGoal]
        planTicks=140 decisions=[14x1] fallbackMotion=0 digest=4e1f0633a466e85d
    UBLADE3_PLAN_AI_SLOT slot=5 plan=Goalie bound=True planIds=[Goalie] behaviors=[SitInGoal]
        planTicks=140 decisions=[14x1] fallbackMotion=0 digest=d0339ed4aa58a3ad
    UBLADE3_NATIVE_MATCH_N9_CHECK_RESULT aiPickups=1 aiPasses=1 aiShots=0 pickupTick=1 passTick=50
        planPassRequests=1 planShotRequests=0 maxActiveSlots=5 planIds=[CarryRok,Defense,Goalie]
        rebinds=2 maxRebindsPerSlot=2 relays=4 relayBudgetHits=0 possessionEvents=2
        unsupportedDecisions=9 ticks=140 planDigest=1ab07ec24368547e failures=0
    UBLADE3_NATIVE_MATCH_N9_DETERMINISM ok=True digest1=digest2=1ab07ec24368547e
    UBLADE3_NATIVE_MATCH_N9_CHECK_OK

The per-slot lines are the simultaneous-plan evidence: all five slots are bound and ticking at the
same time, each with its own plan id, behaviour chain, plan tick count, action and decision counts
and trace digest. Slot 1's chain is the carrier path (Defense -> CARRYROK TurnToMate -> PassingPlay ->
PassingPlayPass -> LostIt -> Defense); slots 2/3 run Defense and switch behaviour on the relayed
messages; both goalies run the shipped GOALIE plan (Setup not shown because it passes straight to
SitInGoal) and stay active. The run's real match effect is aiPasses=1 from the plan's own pass
request (planPassRequests=1); no plan shot path executed, so aiShots=0 is reported rather than faked.

## 5. Verification

Environment: Unity 6000.6.7f1, D:\Games\HyperBlade\HYB, Windows. Headless gates use -nographics;
captures use the graphics device.

| Gate | Marker | Result |
| --- | --- | --- |
| N9 headless (CheckN9) | UBLADE3_NATIVE_MATCH_N9_CHECK_OK | mismatches=0, deterministic digest 1ab07ec24368547e |
| N8 regression (CheckN8) | UBLADE3_NATIVE_MATCH_N8_CHECK_OK | exit 0 |
| N7 regression (CheckN7) | UBLADE3_NATIVE_MATCH_N7_CHECK_OK | exit 0 |
| N6 regression (Check) | UBLADE3_NATIVE_PLAY_CHECK_OK | exit 0 |
| N4/N5 + PlanVM/Data/Sim (UBlade3Checks.RunAll) | UBLADE3_CHECKS_OK | exit 0 (PlanVM probe, data probe, Sim self-check) |
| N9 Editor capture (CaptureAllN9) | UBLADE3_NATIVE_MATCH_N9_CAPTURES_OK frames=6 1280x720 | exit 0; scene evidence aiPickups=2 aiPasses=1 maxActiveSlots=5 planIds=[CarryRok,Defense,Goalie] rebinds=3 relays=8 unsupportedDecisions=12 planDigest=25e2607dcad029e3 |
| N9 Windows player capture (BuildAndRunWindowsN9) | UBLADE3_NATIVE_MATCH_N9_PLAYER_CAPTURES_OK frames=6 | exit 0; player log: aiPickups=2 aiPasses=1 aiShots=0 maxActiveSlots=5 planIds=[CarryRok,Defense,Goalie] planPassRequests=1 rebinds=3 relays=8 unsupportedDecisions=12 planDigest=25e2607dcad029e3 activeNow=5, the same frame bytes as the Editor capture |

Editor capture artifacts (1280x720, HUD composited):

    artifacts/native-match-n9-00-start.png
    artifacts/native-match-n9-01-plans-bound.png
    artifacts/native-match-n9-02-ai-pickup.png
    artifacts/native-match-n9-03-plan-switch.png
    artifacts/native-match-n9-04-plan-pass.png
    artifacts/native-match-n9-05-result.png

The six frames cover the roster binding, the AI pickup, the Defense -> CARRYROK plan change, the
plan pass and the post-pass role switch. Frame 03's HUD reads "ROK CARRIED BY AI SLOT 1"; frame 04
reads "ROK IN FLIGHT" after the plan's pass. The scene run reports the same five active plan
instances and plan-id set as the headless gate and the six player frames are byte-identical to the
Editor frames. The scene/player run shows a second pickup (aiPickups=2) after the pass because it
keeps ticking to the 140-tick beat while the headless probe stops earlier; in the player log the
second carrier is slot 3, whose plan changes Defense -> CARRYROK and enters
TurnToMate>PassingPlay>Throwing (one more plan instance exercising the same carrier entry). Both are
reported as captured, not assumed.

## 6. Unsupported telemetry observed (explicit, not silent)

Per-slot unsupported ids seen in the N9 run (each plan instance keeps its own set):

* decisions **6** IsSenderTeammate, **23** ClosestUncoveredOppn, **68** LooseRokWhatToDo - the plan
  message/behaviour paths that reach them record the id and keep the current behaviour (9 records in
  the headless run);
* actions **6** SetFriction, **7** PlaySound, **9** SetAnimation, **18** ScaleBubToRating, **29**/**30**
  animation/semaphore helpers, **71** goalie helper, plus the launch/throw/weapon actions not exercised;
* agenda items **8** CAvoidObjects and **15** (goalie relax) - agenda item classes not ported;
* behaviour parameters 0,2,3,9,10,11,12,15 - behaviour-subclass/agenda/animation registry state, not
  faked; the motion bridge reads only the shipped Target operand it can support.

Never guessed: an unported decision raises and is caught; the adapter records the id and does not pick
a branch. Runtime errors (non-handler plan exceptions) are counted separately and were 0 in this run.

## 7. Remaining gaps

* the rest of PlanCond/PlanAction bodies (attack/weapon, animation, sound, stats, launch/throw,
  substitution, injury, celebration, end-of-period, goalie save/launch, ...); the observed unsupported
  ids above are the concrete list;
* exact behaviour motion: CSteerToTarget/CGlideOnSurface, GlideBetweenFacing/GlideToPosition/
  SetMovingInPlace target placement, behaviour-subclass agenda slots and purge tables; the N9 motion is
  the labelled held-mask bridge and only SET_Target is read (SET_TargetPosition/SetLocation are not);
* plan-change fidelity: because a whole drain is processed before adoption, only the last
  CHANGE_OF_PLAN request in a tick is adopted (engine instantiates immediately per request); the roster
  documents this coalescing and adopts the carrier's CarryRok 1 request at the documented 1->5 entry;
* message fidelity: plan-to-plan messages are relayed from the mock's event log (explicit targets and
  broadcasts) with bounded budgets; the original's network pack queue, `fromInput` flags and scene
  handler slot limits are not simulated; the 4 relays observed are the subset the plans generated;
* collision/agenda classes: collision interactors do not run (no collision events are fed), so
  collision-driven plan transitions (attack, jostle, hurdle, goalie save) are not exercised;
* animation/sound bodies: the animation port still supplies no clip/event data, so CellAnimationDone/
  SendLaunchMessage and goalie LaunchAt do not produce match effects; the plan shot mapping exists but
  no N9 path executes it;
* the interactive N4-N8 match path remains heuristic-driven by default; the roster is attached
  explicitly by the N9 check/capture (`AttachPlanRoster`) until plan coverage is broad enough to
  become the default; full AI parity/release is not claimed; the bundled 20XX runtime is untouched.

## 8. Next slice (proposal)

1. Port the next tranche of decisions/actions that the observed unsupported ids need (animation/sound
   semaphores, ClosestUncoveredOppn's aim-object state, LooseRokWhatToDo, goalie save/launch) plus the
   behaviour-subclass target/agenda state so more of each plan's own chain runs.
2. Feed real match events (collisions, shots on goal, goal scored, end of period, substitution) into
   the plan instances and add the collision interactor pass, replacing the current subset.
3. Adopt every CHANGE_OF_PLAN request immediately (per message) and support multi-plan tables per
   actor, then measure the full six-plan roster on a full period before enabling it as the
   interactive default.
4. Keep the deterministic N9 (and N8/N7/N6) gates in the regression list and add the player capture
   to the release checklist.
