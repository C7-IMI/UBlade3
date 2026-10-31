# NATIVE-MATCH-N8 - the first real PlanVM-driven AI in the native C# match
This is the first native-match milestone where the shipped **CARRYROK plan** runs through the realUBlade3.PlanVM core inside the match and changes the match: one AI skater picks the ROK up throughthe match's own pickup rule, the plan binds to that skater's live Simulation state, its TurnToMate ->PassingPlay behaviour/timer path runs, and its own pass decision becomes a realMatchPossessionRules.Pass. It is **bounded and honest**: one behaviour path, one plan, one skater, andthe heuristic fallback still drives everything else. It is not full AI parity, not a release gate.
Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Only Native/Match*, the oneEditor file Assets/Editor/UBlade3MatchPlay.cs, the new adapter/script files and this document weretouched. Original data stayed read-only. Nothing was committed; the Lead owns the commit.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| third_party/hyperblade-decomp | Byte-matching decompilation of HYPERX.EXE v1.0.0.1: PBN plan VM, behaviours, interactors, agendas, PlanAction/PlanCond bodies, message rules. |
| third_party/hyperblade-20XX / tools/plan_dump.py | Readable plan VM port and the decoded shipped plan scripts; the CARRYROK behaviour indexes and action/decision ids come from the shipped bytes (CARRYROK.PBN) with the dump tool. |
| D:\Games\HyperBlade\HYB\data | The shipped install every run reads (plans, drome, HUD, audio); read-only. |
| unity/UBlade3/Assets/Scripts/UBlade3/PlanVM | The verified plan VM core and Plan AI slice (PlanVM: 32/32 plans, 9420 instructions, digest 141a750a3de1e083; Plan AI: CARRYROK TurnToMate>PassingPlay mock-world trace, 123 + 40 self-check checks). |
| unity/UBlade3/Assets/Scripts/UBlade3/Native | The N4-N7 native match, Sim bridge and heuristic fallback AI this builds on. |
| docs/PLAN-VM.md, docs/PLAN-AI.md, docs/NATIVE-MATCH-N7.md | The earlier milestones' acceptance records. |

## 2. Files

New:

| File | Contents |
| --- | --- |
| Native/MatchPlanAi.cs | The adapter: loads a shipped PBN plan, binds it to one AI skater's live Simulation state, mirrors the six players/Rok/goals into a PlanAiWorld, runs the plan VM each tick (agenda, timers, message pump, behaviours), maps the plan Target to input-mask steering and the plan's pass/shot actions to the match's shipped pass/shot rules. |
| Native/MatchPlayN8Script.cs | The deterministic N8 scenario: real pickup, plan bind, TurnToMate -> PassingPlay, plan pass; plus the heuristic comparison scenario. |
| docs/NATIVE-MATCH-N8.md | This document. |

Edited (additive; N4-N7 paths untouched):

* Native/MatchAiController.cs: a SuppressSlot predicate so a plan-controlled slot keeps the
  pickup/possession path but does not receive heuristic shot/pass requests.
* Native/MatchPossession.cs (MatchPlayControl): the PlanAi field, AttachPlanAi, the input-map
  branch for the plan slot, the per-tick PlanAi.BeforeMatchTick, and the plan telemetry in
  Describe().
* Native/MatchPlay.cs: N8 capture beats/labels, --n8-capture=, RunDeterministicCaptureN8 and
  RunCommandCaptureN8.
* Assets/Editor/UBlade3MatchPlay.cs: CheckN8, CaptureAllN8, BuildAndRunWindowsN8, the N8
  artifact list and the N8 trace/evidence logs.

Plans/materials are loaded through the existing PlanFileLoader (the install data/plans folder);
no original asset is copied into the repository.

## 3. What the plan actually drives

### Binding and tick

1. The N8 scenario stages the AI skater on slot 1 (dromer 1) and the free ROK nearby. The heuristic
   controller's real pickup approach runs; the match's own MatchPossessionRules.CanPickup (ported
   PlanCond::RokInRange 0x00468c40) + RokState.Attach path performs the attach.
2. On the next live tick the adapter sees Sim.Rok.AttachedSlot == 1, compiles the shipped
   CARRYROK.PBN for actor slot 1 (PlanState.SetAndInit, the verified PBN loader/compiler) and
   activates shipped behaviour **5 "TurnToMate"**.
3. Every live tick after that:
   * the six players' position/velocity/heading/semaphores/desired-substitute and the ROK attach
     target are copied into the Plan AI mirror world from Simulation (the plan-to-object glue);
   * the mirror clock is set to the match's millisecond clock, the agenda list ticks
     (CTimerActivity::Process), and the pending plan scene messages drain through the plan VM's
     DispatchSceneMessage path;
   * the plan VM runs its own behaviours and scripts with the ported PlanAiActions /
     PlanAiDecisions bodies; unsupported ids go to explicit telemetry;
   * real match events (ROK loss) unbind the plan; a plan pass request becomes a real match pass.
4. At the end of the run the plan instance is torn down through PlanState::Teardown.

### Behaviour/target to movement

The behaviour motion comes from the plan's behaviour block, not from the heuristic:

* behaviour kind 0 (GlideToObject) and kind 1 (Glide) steer toward the behaviour's shipped
  SET_BEHAVIOR_PARAM id_SET_Target operand (CARRYROK TurnToMate/PassingPlay target their teammate
  slot; GoForGoal targets the opponents' goal);
* the in-place kinds 16-18 (SetFlatInPlace, DoNothing, SetMovingInPlace) hold position;
* any other behaviour kind uses the **labelled fallback motion bridge** (steer toward the skater's
  target goal) and increments telemetry; it never fabricates a plan decision/action.

The target is converted to the same held-mask steering the N4/N5 match already uses
(MatchAiController.SteerToward); the exact CSteerToTarget physics of the behaviour subclasses is
not ported yet, so this translation is the documented motion bridge, not a plan result.

### Plan results that change the match

| Plan id | Handler body (already ported in PlanVM) | Match effect |
| --- | --- | --- |
| action 5 / 93 | PlanAction::SendMsg 0x0045e6a0 / ReallySendMsg 0x0045e5d0 | a plan message to the team mate with id_MSG_Incoming (20) is the adapter's pass intent: it calls MatchPlayControl.RequestAiPass(1), whose shipped MatchPossessionRules.Pass (PlanAction::Throw) throws the ROK |
| action 103 | PlanAction::SetKnockedDownState 0x0045ee20 | the action body writes the match player's knocked-down state through the mirror action body |
| actions 3/37/38/139 | Launch / LaunchAt / Throw / SendLaunchMessage | mapped to RequestAiShot when a plan path executes them (available; not reached by this behaviour path) |
| decision 15 | PlanCond::DecidePosition 0x00462d20 | plan-level sanity check: keeps the carrier in the plan (branch 2 no change here) |
| decision 111 | PlanCond::OkToPassToTeammate 0x00462c40 | plan decides the pass is on (branch 1) |
| decision 78 | PlanCond::ShootSituation 0x00468650 | plan decides the long shot is not on (branch 1 no change) |
| decision 50 | PlanCond::RecvrInRangeNView 0x00466b70 | plan decides the receiver is in range/view (branch 0), which runs the plan's ReallySendMsg(Me, Extra0, MyTeammate) and then the Incoming pass |
| agenda 18 | CTimerActivity (Bind 0x00411a10, Process 0x00411b00) | plan timers: 500 ms TurnToMate -> PassingPlay transition, 500 ms Extra1, 1000 ms TimeOutMessage, then the plan's own re-arm |

Pickup/attach stays the match's real rule; the plan takes over once possession exists. Shot/pass ids
outside the implemented action bodies remain telemetry. The heuristic fallback still fills motion and
the ROK chase before possession, and every other skater.

## 4. Deterministic probe and capture scenario

The N8 script uses the same state machine headless and in the scene (no Unity in the script; the
Editor supplies the scene tick):

* stage: human at the shipped face-off corner (-28, 28), passing target slot 3 held at (0, 120), plan
  skater slot 1 at (0, -40), ROK at the spawn; the two staged non-plan skaters are held each tick
  (labelled deterministic staging - the same kind of staging N6/N7 use, not a plan result);
* tick sequence: heuristic pursuit of the free ROK -> real RokInRange/Attach pickup -> plan binds ->
  TurnToMate sanity check -> 500 ms timer -> PassingPlay -> OkToPassToTeammate -> Extra1
  ShootSituation -> 1000 ms TimeOutMessage -> RecvrInRangeNView -> ReallySendMsg Extra0 ->
  plan Incoming action -> RequestAiPass -> shipped Throw -> ROK in flight -> unbind/teardown.

Evidence from the headless Editor gate (artifacts/ublade3/n8-check-final.log):

    UBLADE3_PLAN_AI_MATCH_IMPL actions=5,93,103 decisions=15,50,78,111 motion=glideToObject(0),glide(1),inPlace(16..18) fallback=labelled-input-mask-bridge
    UBLADE3_PLAN_AI_MATCH_OPCODES plan=CARRYROK nodes=889 opcodes=Enable=105,END_PLAN=1,SET_COLLISION_INTERACTOR=2,SET_MESSAGE_INTERACTOR=127,DECLARE_BEHAVIOR=13,CHANGE_TO_BEHAVIOR=130,SET_BEHAVIOR_PARAM=27,CHANGE_OF_PLAN=30,DECIDE_BY_AMONG=57,DO_ACTION=281,ADD_AGENDA_ITEM=13,SET_TIMEOUTMSG=23,REMOVE_AGENDA_ITEM=9,GLOBAL_INTERACTORS=1,Block=70
    UBLADE3_PLAN_AI_MATCH slot=1 plan=CARRYROK bound=False binds=1 unbinds=1 planTicks=48
        behaviors=[TurnToMate>PassingPlay>PassingPlayPass] behaviorChanges=3 targetChanges=2
        targets=[player,none] planActions=[5x3,93x1,103x1]
        planDecisions=[15x2,50x1,78x1,111x3] passRequests=1 shotRequests=0
        fallbackMotionTicks=0 unsupported=[action:7,agenda-item:8,behavior-parameter:10,
        behavior-parameter:11,behavior-parameter:12,behavior-parameter:15,
        behavior-parameter:3,behavior-parameter:9] digest=4fb06b4458419444
    UBLADE3_NATIVE_MATCH_N8_CHECK_RESULT aiPickups=1 aiPasses=1 aiShots=0 planBoundTick=52
        passingPlayTick=68 planPassTick=99 planPassRequests=1 planBehaviorChanges=3
        planTargetChanges=2 planTicks=48 unsupported=8 planDigest=4fb06b4458419444
    UBLADE3_NATIVE_MATCH_N8_DETERMINISM ok=True digest1=4fb06b4458419444 digest2=4fb06b4458419444
    UBLADE3_NATIVE_MATCH_N8_COMPARISON heuristic: aiPickups=2 aiShots=2 aiPasses=0 ticks=480
        planAi=null (no behaviour/decision telemetry) | plan: aiPasses=1 planBehaviorChanges=3
        planPassRequests=1 planDigest=4fb06b4458419444
    UBLADE3_NATIVE_MATCH_N8_CHECK_OK

The comparison is the bounded before/after: the heuristic controller (N7) picks up and takes its
fallback unaimed shot path (2 pickups / 2 shots / 0 passes, no plan telemetry); the N8 plan-driven
slot picks up once and **passes on the plan's own decision**, with its behaviour, target, action and
decision ids recorded and a stable trace digest.

## 5. Verification

Environment: Unity 6000.6.7f1, data D:\Games\HyperBlade\HYB, Windows. Headless gates run with
-nographics; captures run with the graphics device.

| Gate | Execute method | Result |
| --- | --- | --- |
| N8 headless | UBlade3.EditorTools.UBlade3MatchPlay.CheckN8 | exit 0, UBLADE3_NATIVE_MATCH_N8_CHECK_OK, mismatches=0 |
| N7 regression | UBlade3.EditorTools.UBlade3MatchPlay.CheckN7 | exit 0, UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 regression | UBlade3.EditorTools.UBlade3MatchPlay.Check | exit 0, UBLADE3_NATIVE_PLAY_CHECK_OK |
| N4/N5 regression | UBlade3.EditorTools.UBlade3Checks.RunAll | exit 0, UBLADE3_CHECKS_OK (PlanVM probe, Data probe, Sim self-check) |
| N8 Editor capture | UBlade3.EditorTools.UBlade3MatchPlay.CaptureAllN8 | exit 0, UBLADE3_NATIVE_MATCH_N8_CAPTURES_OK frames=6 1280x720 |
| N8 Windows player capture | UBlade3.EditorTools.UBlade3MatchPlay.BuildAndRunWindowsN8 | exit 0, UBLADE3_NATIVE_MATCH_N8_PLAYER_CAPTURES_OK frames=6; player log reports aiPickups=1 aiPasses=1 planDigest=4fb06b4458419444 |

Editor capture artifacts (1280x720, HUD composited):

    artifacts/native-match-n8-00-start.png
    artifacts/native-match-n8-01-ai-pickup.png
    artifacts/native-match-n8-02-plan-bound.png
    artifacts/native-match-n8-03-passing-play.png   ("ROK CARRIED BY AI SLOT 1")
    artifacts/native-match-n8-04-plan-pass.png      ("ROK IN FLIGHT")
    artifacts/native-match-n8-05-result.png

Windows player capture artifacts: the same six labels with the native-match-n8-player- prefix; the
player run recorded the same plan result line and digest as the Editor and headless runs.

The captured frames show the plan-controlled AI skater carrying the ROK (frame 03) and, after the
plan's RecvrInRangeNView/OkToPassToTeammate/ReallySendMsg chain, the ROK in flight from the plan's
pass (frame 04). The scene run reports the same plan telemetry as the headless gate (6 frames,
aiPickups=1, aiPasses=1, aiShots=0, plan pass request=1, digest 4fb06b4458419444).

## 6. Unsupported plan telemetry (explicit, not silent)

Eight distinct ids reached by this path are reported by PlanAiHost.UnsupportedHandlers:

* behavior-parameter:3 SetTarget, :9 SetPersistentAnimation, :10 SetTargetGoal, :11 SetAnimation,
  :12 SetAnimationEvent, :15 SetDigressive - behaviour-subclass agenda-slot and animation-registry
  state; the motion bridge reads the Target operand directly (labelled above) but the original
  BehParam handler bodies remain unported;
* agenda-item:8 CAvoidObjects - the avoidance agenda item class is not ported; its add/remove on
  PassingPlay/PassingPlayPass is recorded;
* action:7 PlaySound - reached by the follow-on PassingPlayPass Initiate arm after the change; the
  sound action body is not ported, so it is skipped and reported, never faked.

Plan-change requests are also recorded (0 in this run). Any unported decision id would raise
PlanAiUnportedHandlerException instead of returning a guessed branch.

## 7. Remaining gaps

* the other shipped AI plans and behaviour paths (OFFWING, DEFENSE, GOALIE, SIDELN, the remaining
  CARRYROK behaviours); only CARRYROK TurnToMate -> PassingPlay is exercised end-to-end;
* the behaviour physics: the exact CSteerToTarget/CGlideOnSurface plan behaviour motion, the
  behaviour subclasses' Init/agenda slots/purge tables, and the remaining behaviour parameters;
* the other agenda item classes (steering, avoidance, monitoring, animation, sound, palette, camera)
  and the rest of PlanAction/PlanCond bodies; shot/pass ids beyond 5/93/103;
* runtime stat-pronoun resolvers (g_apPlanStatFns) and PlanArg::ResolveFloat;
* animation, sound and stats bodies behind the action seam; the animation port still supplies no
  clip/event data to actions;
* plan changes: a CHANGE_OF_PLAN request is recorded but the adapter keeps the current plan (the
  shipped multi-plan behaviour is not adopted yet);
* the match tick ordering is the N8 harness order (agenda tick -> pending message drain -> plan
  intent -> sim tick); the original engine interleaves more, and the player capture uses the same
  scene path;
* full parity/release is not claimed; the bundled native 20XX runtime and release path are untouched.

## 8. Next slice (proposal)

1. Generalise MatchPlanAi over plan table + per-slot bindings and adopt CHANGE_OF_PLAN requests
   (instantiate the target plan/behaviour), then add OFFWING/DEFENSE carrier behaviours.
2. Port the behaviour classes' Init extras and agenda slots, CSteerToTarget motion and the avoidance
   agenda item so the motion bridge can be replaced by the exact physics.
3. Port the rest of the action/decision bodies the AI plans use (SendLaunchMessage -> real shot,
   animation semaphores, stats updates), then run the N8 gate across multiple plan paths.
4. Keep the deterministic N8 check/captures in the regression list and add the plan-driven AI to the
   interactive match once the plan coverage is broad enough.
