# Plan AI slice (N5.5): CARRYROK TurnToMate -> PassingPlay

This is the second bounded slice of the HyperBlade plan-VM port: the first slice (docs/PLAN-VM.md) ported the
PBN loader, the instruction/operand model, the plan-state machine and the ExecuteScript dispatch with every
engine handler left as a seam. This slice ports the engine-side handler bodies and object glue for **one
concrete shipped behaviour flow** so the C# core can drive real plan scripts against a small mock plan
object. It is **not** the match AI and does not claim gameplay parity: only the action, decision and agenda
ids the selected flow reaches are implemented, and every other id stays visible as telemetry.

## Selected flow

Plan **CARRYROK**, the AI carrier's plan (13 behaviours, 15 compiled top-level nodes):

1. **behaviour 5 "TurnToMate"**, entered by an AI carrier: on kMsgInitiate it sends itself the sanity-check
   message and arms a 500 ms timer (SET_TIMEOUTMSG 50, id_MSG_TimeOutMessage, id_PRN_Me).
2. The timer fires, its handler changes to **behaviour 7 "PassingPlay"**.
3. **PassingPlay** on kMsgInitiate runs OkToPassToTeammate, sets the knocked-down state to 0, adds
   CAvoidObjects (unported, telemetry only), arms the 500 ms id_MSG_Extra1 timer and the 1000 ms
   id_MSG_TimeOutMessage timer (both to the actor, data = the team mate's slot).
4. Extra1 runs ShootSituation (the shot/pass decision).
5. The first timeout runs RecvrInRangeNView with the team mate's slot; the mock receiver is out of range,
   so the original's arm re-arms SET_TIMEOUTMSG 50, id_MSG_TimeOutMessage, id_PRN_Me with **no fourth leaf**.
   That is the documented data-0 timer defect from third_party/hyperblade-decomp/docs/plan_bugs.md
   ("GoForGoal"/"PassingPlay") and the trace shows the re-armed timer carrying data=0.
6. The re-armed timer fires with data 0, so RecvrInRangeNView tests slot 0 (the human team mate in the
   fixture). The fixture places slot 0 close and in front, the decision answers 0, and the arm runs
   ReallySendMsg(Me, Extra0, MyTeammate).
7. The immediate Extra0 handler runs OkToPassToTeammate again, sends Incoming data 1 to the team
   mate (a mock object with no plan instance, so the engine drops it and the probe records that), and changes
   to **behaviour 9 "PassingPlayPass"**. The probe stops at that behaviour transition; the queued
   PassingPlayPass Initiate is reported as pending=1, not delivered.

TurnToMate -> PassingPlay -> PassingPlayPass is the shipped carrier's pass-flow; the trace also
delivers the plan's own sanity-check message, which exercises the plan-level handler and
id_DCF_DecidePosition.

## Files (all new, under unity/UBlade3/Assets/Scripts/UBlade3/PlanVM/)

| File | Contents |
| --- | --- |
| PlanAiMath.cs | Vec3 (src/math/vec3.cpp, Math::Vec3Normalize 0x00430110), SquirrelNoise5 Math::Rand (src/math/squirrel5.h), plan constants, difficulty gate (src/planvm/damagegate.h) |
| PlanAiWorld.cs | PlanAiStats, PlanAiPhysicsBody, PlanAiObject (handler table, agenda list head, semaphores), PlanAiWorld (slots, pending scene queue, API-message helpers, clock, trace log), EngineObject GetDistIfFacing (0x00474590) port |
| PlanAiAgenda.cs | AgendaItem base lifecycle (AgendaItem::Activate 0x0040b990, Detach 0x0040bb10, OnListChanged 0x0040b930) and CTimerActivity (Bind 0x00411a10, BindWithNode 0x00411a40, Process 0x00411b00, Release 0x00411c00) |
| PlanAiActions.cs | ported action handler bodies and id table (see below) |
| PlanAiDecisions.cs | ported decision handler bodies and id table (see below) |
| PlanAiHost.cs | IPlanVmHost over PlanAiWorld: scene handler registration/dispatch, TickAndSortMessages, SendTimedMessage, exit-behaviour, plan change, agenda add/remove, action/decision dispatch and unsupported-id telemetry |
| PlanAiInteractorExtensions.cs | MsgInteractor's named-slot read (message id) for the host table |
| PlanAiFixture.cs | the mock match fixture: six players, Rok attached to the carrier, two goals, ratings and positions |
| PlanAiProbe.cs | the gate: loads the shipped CARRYROK plan, runs the deterministic trace, checks anchors, prints UBLADE3_PLAN_AI_OK |
| PlanAiSelfCheck.cs | 40 unit checks for the random, Vec3, damage gate, message queue, timer arithmetic and the ported decisions |

No existing file was edited; only these new files and this document were added.

## Implemented handler ids

Action handler bodies (all cited to src/planvm/plancond.cpp unless noted):

| id | handler | decomp address | body |
| --- | --- | --- | --- |
| 5 | PlanAction::SendMsg | 0x0045e6a0 | resolves the target (pronoun already compiled; _MESSAGE_DATA_ uses nParam; _BROADCAST_ broadcasts), optional fourth-leaf data override, PostSceneMessage |
| 93 | PlanAction::ReallySendMsg | 0x0045e5d0 | same resolution; DispatchSceneMessage immediately instead of queuing |
| 103 | PlanAction::SetKnockedDownState | 0x0045ee20 | two leaves set the actor, three leaves set the player selected by argument two or the message data |

Decision handler bodies:

| id | handler | decomp address | body |
| --- | --- | --- | --- |
| 15 | PlanCond::DecidePosition | 0x00462d20 | damage gate, clock-stopped semaphore, Rok attach target, slot far-half |
| 50 | PlanCond::RecvrInRangeNView | 0x00466b70 | receiver slot check, long-pass enable, squared pass range, facing cone, single-player human mate, damage gate |
| 78 | PlanCond::ShootSituation | 0x00468650 | authored throw range window, goal distance, heading dot, random roll, single-player human mate |
| 111 | PlanCond::OkToPassToTeammate | 0x00462c40 | team mate damage, both clock-stopped semaphores, desired substitute |

Agenda/timer ids implemented: Agenda_CTimerActivity (18). Every other agenda id the flow reaches
(Agenda_CAvoidObjects 8) is recorded as unsupported telemetry.

The handler bodies compute their outcomes from the fixture's object fields with the original formulas; the
fixture supplies the engine state (positions, ratings, semaphores), it does not substitute for a handler.

## Engine glue ported

* scene message table: ResetSceneMessageHandlers 0x004721d0, RegisterSceneMessageHandler 0x00472200 (one
  handler per message id, 50-handler limit), PostSceneMessage 0x00472090 (drops a message with no registered
  handler), DispatchSceneMessage 0x00472110 (active-state check, sender into the interactor, fresh decision-log
  word, the local-player network pack line reported but not simulated);
* pending queue: GameEngine::PostSceneMessage 0x004870b0 and the pending-list pass of
  ProcessInputCommandQueue 0x00487140 (FIFO, messages posted while draining are drained in the same pass);
* GameBehaviorObject::TickAndSortMessages 0x0041c380: behaviour purge bytes, detach of ready/non-persistent
  agenda items, and the reset+re-register of plan-level then behaviour-level message handlers with the
  0/1/2 member phase gate;
* GameBehaviorObject::SendTimedMessage 0x0041c320: post kMsgInitiate to the owner;
* GameBehaviorObject::RemoveAgendaItem 0x0041c5f0 for CTimerActivity: removes every timer (plan VM rule 3);
* exit-behaviour dispatch before a change, EngineObject::FindPlanByName 0x00471150 exact-name lookup,
  EngineObject::ChangePlan recorded (the mock does not instantiate another plan);
* PlanState::Teardown's observer pass.

Timer semantics are the decompiled ones: the authored argument is in 10 ms units, so the fire tick is
now + argument * 10 (CTimerActivity::BindWithNode 0x00411a40); it fires only when the clock is **strictly
greater** than the fire tick (0x00411b00); the optional fourth leaf supplies the message data, otherwise 0
(plan VM rule 6). Entering a non-digressive behaviour detaches every timer (rule 2), which the host's
TickAndSortMessages pass implements from the behaviour's digressive flag.

## Fixture (mock inputs, documented)

| Object | Slot | Position | Ratings / fields |
| --- | --- | --- | --- |
| Human team mate (bug-test receiver) | 0 | (0, 100, 0) | Damage 20, LongPassEnable 0 |
| AI team mate (far receiver) | 1 | (0, 400, 0) | Damage 20 |
| Opponent | 2 | (0, -100, 0) | Damage 20 |
| AI carrier (plan instance) | 3 | (0, 0, 0), heading (0, 1, 0) | Damage 20, CarryRokThrowDist 150 |
| Goalies | 4, 5 | at the goals | - |
| Rok | 6 | attached to slot 3 | AttachTargetSlot 3 |
| North / South goal | 7 / 8 | (0, 250, 0) / (0, -250, 0) | - |

World: TeammateMode 1, difficulty Medium (damage gate 6), owner plan table
(CarryRok, Defense, Human, OffWing, Sideln), Squirrel5 seed 0x5EED1234. The fixture values are chosen so the
trace takes the documented bug path; they are inputs, and the decisions are still the ported formulas.

## Deterministic trace (probe run, clock in ms)

    t=0     activate TurnToMate; Initiate: SendMessage(sanity check); timer TimeOutMessage 500
            sanity check: DecidePosition branch 2 (no change), decision log 0x9002
    t=510   timer fires (500, strict >); TimeOutMessage -> change PassingPlay
            PassingPlay Initiate: SendMessage(sanity check); OkToPass branch 1;
            SetKnockedDownState(0); CAvoidObjects unsupported; timers Extra1 1010 (data 1) and
            TimeOutMessage 1510 (data 1)
    t=1020  Extra1 fires; ShootSituation branch 1
    t=1520  TimeOutMessage data 1 fires; RecvrInRangeNView branch 1 (team mate out of range) ->
            re-arms TimeOutMessage with data 0 at 2020  (the documented bug, visible in the trace)
    t=2030  re-armed timer fires data 0; RecvrInRangeNView tests slot 0, branch 0 ->
            ReallySendMsg(Me, Extra0, MyTeammate)
            Extra0: OkToPass branch 1; SendMessage(team mate, Incoming, 1) dropped (no plan instance);
            change to PassingPlayPass; ExitBehavior handler removes CAvoidObjects (unsupported)
            endpoint: one queued PassingPlayPass Initiate left pending

Counts: messages dispatched 10, posted 9, dropped 2, actions 5, decisions 7, timers armed 4 / fired 4,
unsupported handler ids 7, pending 1, trace digest 73073ff095cd4e00.

## Probe

UBlade3.PlanVM.PlanAiProbe is scene-free and runs two ways:

* process: a small Main-bearing harness calls PlanAiProbe.Run; args[0] may be the install root, otherwise
  HYPERBLADE_GAME_DATA is used;
* Unity batch: -executeMethod UBlade3.PlanVM.PlanAiProbe.Check.

Output contract:

    UBLADE3_PLAN_AI_OK plan=CARRYROK behavior=TurnToMate>PassingPlay messages=10 actions=5 decisions=7
        timers=4 unknowns=0 unsupported=7 pending=1 digest=73073ff095cd4e00
    UBLADE3_PLAN_AI_IMPL actions=5,93,103 decisions=15,50,78,111 behaviorParams=none agenda=timer(18)
    UBLADE3_PLAN_AI_PLAN name=CARRYROK.PBN words=4431 nodes=889 leaves=1323 strings=43 behaviors=13
        unknowns=0 digest=46c109accf61ea5b
    UBLADE3_PLAN_AI_COUNTS messages=10 posted=9 dropped=2 actions=5 decisions=7 timersArmed=4
        timersFired=4 unsupported=7 unsupportedIds=[agenda-item:8,behavior-parameter:10,...] pending=1
    UBLADE3_PLAN_AI_TRACE t=... event ...                                  (101 lines)
    UBLADE3_PLAN_AI_SELFTEST checks=40/40
    UBLADE3_PLAN_AI_FAIL ...                                               (only on failure)

The plan line is checked against the plan-VM slice's independently derived CARRYROK digest
(46c109accf61ea5b, 889 nodes, 1323 leaves, 43 strings, 13 behaviours, 0 unknown opcodes). The trace digest
is FNV-1a 64 over a header, every recorded trace line in order, and an END line with the final behaviour and
pending count; the probe runs the trace twice and fails if the two digests differ. The digest and counts are
also hard-coded as regression anchors.

## Verification performed (2026-10-31)

Temporary projects outside the repository, C# 9 language level, warnings as errors:

    dotnet build PlanVmCore.csproj          # netstandard2.1 (Unity .NET Standard 2.1 profile), LangVersion 9.0
    # -> Build succeeded, 0 warnings, 0 errors (all PlanVM sources, old and new)
    dotnet build PlanAiProbeRunner.csproj   # net9.0, LangVersion 9.0, same sources + a Main
    dotnet run --project PlanAiProbeRunner.csproj -- D:GamesHyperBladeHYB
    # -> exit 0, UBLADE3_PLAN_AI_OK line above

Two fresh runs produced byte-identical output (Compare-Object: 0 differing lines), so the trace is
deterministic. The self-check passes 40/40. The shipped CARRYROK plan loads with its plan-VM anchor digest
and zero unknown opcodes. No Unity editor/batch build was run (the project may be imported by another
process; the sources use no UnityEngine type, so the Unity assembly build sees the same compilation unit).

## Unsupported telemetry (not silent)

The probe reports seven unsupported ids reached by the flow; none is executed or guessed:

* behavior parameters 3 (SetTarget), 9 (SetPersistentAnimation), 10 (SetTargetGoal), 11 (SetAnimation),
  12 (SetAnimationEvent), 15 (SetDigressive). Their handler bodies are behaviour-subclass state: agenda slot
  objects, the animation registry and the persistent-animation/limp swap. The plan's behaviour initialisation
  calls them for all thirteen behaviours, so the probe records them at instantiation. Digressive stays 0 for
  the slice, which is why the host's activation pass uses the non-digressive timer-detach rule.
* agenda item 8 (Agenda_CAvoidObjects) on PassingPlay's Initiate and its ExitBehavior: the avoidance item
  class (steering force over the object's physics body) is not ported. The add and remove are both reported.

Any other action/decision id would be reported the same way and skipped (action) or abort the step with
PlanAiUnportedHandlerException (decision), never silently defaulted.

## Remaining gaps

* the other 132 action handler bodies and 135 decision handler bodies (planaction*.cpp, plancond.cpp);
* the remaining agenda item classes (steering, avoidance, tracking, monitoring, animation step, palette,
  sound, ...) and their Update/Tick bodies;
* behaviour subclasses: their Init extras (animation names, agenda slot bindings, purges), the ten agenda
  slots and their merge in TickAndSortMessages - this is why the six behaviour parameters are telemetry;
* runtime stat-pronoun resolvers (g_apPlanStatFns / planfocusarg.cpp) for the pronouns that are not resolved
  at compile time, plus PlanArg::ResolveFloat and local reals;
* the animation registry, sound manager, statistics objects and physics bodies: the mock supplies their
  fields, and the network pack-enqueue path is reported but not simulated;
* the plan compiler, plan patches, owner plan tables beyond the fixed fixture list, and the engine tick
  ordering (the probe's step order is agenda tick -> pending message drain -> recorded state; the original
  engine's frame order is more interleaved);
* full match integration: the probe is a bounded harness, not a running AI. The native match still uses the
  bundled 20XX runtime; the Lead wires the C# plan AI when the native AI path is scheduled.

## Next slice (proposal)

1. Port the behaviour parameter bodies plus the generic behaviour agenda slots so Target/PersistentAnimation/
   Digressive/Animation/AnimationEvent apply, then the per-kind purge tables.
2. Port the agenda item classes in the order the AI plans use them (CSteerToTarget, CAvoidObjects,
   CMonitorKeys, CStepAnimation, CGlideOnSurface), which unlocks OFFWING/DEFENSE/GOALIE behaviours.
3. Port the remaining action bodies for the carrier plans (Launch/Throw/LaunchAt, animation semaphores,
   stats) and the remaining decisions for those flows.
4. Wire PlanAiProbe.Describe() into PortLab and add Check() to the Unity batch gate once the editor build
   ownership is settled; feed the implemented ids into the port ledger.
