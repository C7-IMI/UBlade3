# PLAN-HANDLERS-N13 - remaining observed PlanVM handlers

N13 closes the default-match handler gaps left after N12: action 84/139, decisions 51/71/120,
behaviour parameters 0/2/3/9/10/11/12/15, agenda items 8/15, and the recorded-only
audio/animation/stat/bubble effects. It is a bounded exact-body port, not full PlanVM parity.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Original data read-only; no
Data/Assets/Sim/App/ProjectSettings/native-release changes. Nothing committed; the Lead commits.

## 1. Implemented ids, bodies and citations

| Id | Ported body / citation | Result in the N13 probe |
| --- | --- | --- |
| action 84 SpeedBurstSituation | PlanAction::SpeedBurstSituation (plancond.cpp HYPERX 0x00467760) | 7 executions; speed-ratio gate 0.25, team-winner rating doubling, speed-burst stat gate 0x11, real friction 0.1/axisX+0.25/velocity fill/target+boost state |
| action 139 SendLaunchMessage | PlanAction::SendLaunchMessage (planactionlaunch.cpp HYPERX 0x004031e0) | posts MSG_SHOOT (0) with SHOT_UNAIMED 0xffff0000; 1 execution in the pinned shot scenario; the AI branch is exact, the Human head-camera pack falls back to unaimed and is recorded |
| decision 51 WhichOppnIsClosestToMe | PlanCond::WhichOppnIsClosestToMe (plancond.cpp HYPERX 0x00460f20) | 3 executions; GetDistIfFacing(0) + damage gate + the source zero-distance fallback branches |
| decision 71 OffWingJob | PlanCond::OffWingJob (plancond.cpp HYPERX 0x004682d0) | 1 execution; Win/LoseOffWingJob ratings 37/38 + damage-gate branch to 2 |
| decision 120 InRangeToAttack | PlanCond::InRangeToAttack (plancond.cpp HYPERX 0x00460240) | 6 executions; both opponents beyond FAR_RANGE_SQUARED 500 -> 0, otherwise 1 |
| behaviour 0 ModsTweenNFacing | BehParam::SetModsTweenNFacing (behparam.cpp HYPERX 0x0041c780) | 3 slots resolved (pronoun-aware), objectA/B/aim and blend step 1/period; published for decision 23 |
| behaviour 2 InteractorLatency | BehParam::SetInteractorLatency (behparam.cpp HYPERX 0x0041c8a0) | Latency = short(InvDelta * value * 0.01); InvDelta comes from the real Sim EngineClock (30 Hz) |
| behaviour 3 Target | BehParam::SetTarget (behparam.cpp HYPERX 0x0041c920) | target object/slot state set (CSteerToTarget target); adapter motion already consumes the plan Target exactly |
| behaviour 9 PersistentAnimation | BehParam::SetPersistentAnimation (behparam.cpp HYPERX 0x0041cb20) | animation id plus persistent rate/flags recorded; animation registry/clip playback is not in this slice |
| behaviour 10 TargetGoal | BehParam::SetTargetGoal (behparam.cpp HYPERX 0x0041cdb0) | target-goal slot stored and applied once to the real Sim player (1 application) |
| behaviour 11 Animation | BehParam::SetAnimation (behparam.cpp HYPERX 0x0041cce0) | animation id/rate/flags recorded; no fake rendering |
| behaviour 12 AnimationEvent | BehParam::SetAnimationEvent (behparam.cpp HYPERX 0x0041cd40) | channel/target/kind/param written to the behaviour fields exactly |
| behaviour 15 Digressive | BehParam::SetDigressive (behparam.cpp HYPERX 0x0041c8e0) | behaviour.Digressive set, which the existing TickAndSortMessages purge pass reads |
| agenda 8 CAvoidObjects | Bind/BindWithNode/Process (cavoidobjects.cpp HYPERX 0x0040f830/0x0040f890/0x0040f9c0) | 22 adds; documented range 80/strength -6.5/swerve min speed 2/wrong-side 0x14/blocked 0.3; 42 smoothVel applications, 1 breakaway event, real plan MSG_BLOCKED/BREAKAWAY posts |
| agenda 15 CRelaxToPosition | Bind/BindWithNode/Process (crelaxtoposition.cpp HYPERX 0x00411c90/0x00411cf0/0x00411e60) | 2 adds; exact 15-entry relax-rate table (0.0483 at 30 Hz), forward-speed clamp and goal-marker destination; 478 real position advances |

New PlanVM files: `PlanAiMatchActionsN13.cs`, `PlanAiMatchDecisionsN13.cs`, `PlanAiMatchBehaviorParams.cs`,
`PlanAiMatchAgendaItems.cs`, `PlanAiMatchAgendaState.cs`. `PlanAiMatchContext.cs` gained the real Sim
clock/forward-speed/target-goal fields and goal-marker (8/9) positions. `PlanAiActions` registers actions
84/139; `MatchPlanAi` registers decisions 51/71/120, applies behaviour/agenda/audio effects and publishes
context; `MatchPlanAiRoster`/`MatchPossession` enable the N13 handler set for the default roster;

`Assets/Editor/UBlade3MatchPlay.cs` has the `CheckN13` probe and pins N4-N12 checks to the N12 handler set.

### Approved host seam

The Lead approved the minimal additive extension registry/calls in `PlanVM/PlanAiHost.cs`. Existing
behaviour is unchanged: `SetBehaviorParam` first tries the original `PlanAiBehaviorParams` and then the
N13 table only when enabled; `AddAgendaItem` keeps the timer path first and then offers non-timer ids to
the N13 agenda factory; `RemoveAgendaItem` keeps the timer path and only removes handled N13 items. The
N13 flag is off by default and enabled only by the default/N13 roster, so the pinned N4-N12 suites keep
their original handler set and evidence.

## 2. Effects: applied vs exact telemetry

| Effect | Engine path in this slice | N13 outcome |
| --- | --- | --- |
| Audio action 7 PlaySound | `MatchPlanAi.Control.Audio.Service.PlayIndex(index)` is the AudioMgr PlaySpecific route; ids are SNDFILES.LST positions | actively wired; headless CheckN13 has no audio service so the probe records `soundUnavailable=1` (exact telemetry), the interactive player installs the default service and would call PlayIndex; not player-verified in this N13 run |
| Animation action 30 / behparams 9/11/12 | MatchSkaterView has the original WBA/WBM clip port, but no allowed N13 file can consume a plan animation id into the match renderer | exact plan state/telemetry only (`animationEvents=8`, animationApplied=0); no fake clip is played |
| Stat action 9 IncrementStat | match-stat accumulator/stat screen path is not ported in this slice | exact request telemetry (`statEvents=1`, statApplied=0) |
| Bubble action 71 ScaleBubToRating | match collision does not consume awareness-bubble radii (Sim collision comment says bubbles are not in this slice) | exact radius formula/telemetry (`bubbleApplied=0`) |
| Friction/location/speed-burst/agenda position/heading/smoothVel | real PhysicsBody/Sim path | applied and counted (friction, `location` now resolves the goal marker (the N12 probe under the N13 code reports locationApplied=2), speed-burst velocity/axis, 42 agenda smoothVel, 478 agenda positions) |

## 3. Deterministic default-match probe

`CheckN13` builds the normal default scene, enables the N13 handler set, stages the N10 shot staging and
drives 240 ticks; a second scene with the N13 flag reverted to the N12 set runs the same staging to prove
the action-139 shot body (the N13 default plan reaches a different branch and does not shoot in this
window). The probe aggregates the per-slot plan action/decision counts and the N13 effect counters.

Evidence (artifacts/ublade3/n13-check-final.log):

    UBLADE3_NATIVE_MATCH_N13_RESULT defaultPlanAi=True targetPhysics=True ticks=240 aiPickups=1 aiPasses=1 aiShots=0
        actions=[84x7,139x0] decisions=[51x3,71x1,120x6]
        effects=[soundApplied=0,soundUnavailable=1,animationApplied=0,statApplied=0,bubbleApplied=0,
                 soundEvents=1,animationEvents=8,statEvents=1,behParams=11,targetGoal=1,
                 agendaAdds8=22,agendaAdds15=2,agendaPos=478,agendaSmoothVel=42,
                 agendaBlocked=697,agendaBreakaway=1,agendaRelax=478]
        digest=27f18b0e2713eb78
    UBLADE3_NATIVE_MATCH_N13_LEGACY_SHOT actions=[139x1] aiShots=1 ticks=240
    UBLADE3_NATIVE_MATCH_N13_CHECK_OK

Two independent default-handler runs produced the same digest 27f18b0e2713eb78 and the same counts.

## 4. Unsupported telemetry reduction

Before N13: behaviour parameters 0,2,3,9,10,11,12,15; agenda 8/15; decisions 51/71/120; actions 84/139
were explicit unsupported telemetry. After N13 the default N13 slots report no agenda or behaviour-param
unsupported ids, and actions 84/139 plus decisions 51/71/120 execute. The remaining default-roster
unsupported decisions observed in the probe are 31, 57 and 123 (the next uncovered bodies); nothing is
silently dropped.

## 5. Verification / regressions

| Gate | Result |
| --- | --- |
| N13 CheckN13 (x2) | UBLADE3_NATIVE_MATCH_N13_CHECK_OK, same digest 27f18b0e2713eb78, mismatches=0 |
| N12 | UBLADE3_NATIVE_MATCH_N12_CHECK_OK (pinned N12 handler set) |
| N11 | UBLADE3_NATIVE_MATCH_N11_CHECK_OK (pinned N12 handler set) |
| N10 | UBLADE3_NATIVE_MATCH_N10_CHECK_OK (pinned N12 handler set) |
| N9 | UBLADE3_NATIVE_MATCH_N9_CHECK_OK |
| N8 | UBLADE3_NATIVE_MATCH_N8_CHECK_OK |
| N7 | UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 | UBLADE3_NATIVE_PLAY_CHECK_OK |
| N4/N5 + PlanVM/Data | UBLADE3_CHECKS_OK |
| dotnet PlanVM core | PlanVmCore.csproj build succeeds, 0 warnings/errors |

## 6. Honest gaps

* Animation clip/registry playback: the plan animation ids/rates are exact telemetry; the match
  renderer still plays its own clip clock. Wiring a plan animation id to MatchSkaterView needs a
  MatchScene/MatchSkaterView edit outside the N13 file scope.
* Match-stat accumulator, collision bubble radius and audio playback outside an interactive player are
  telemetry/partial paths; the headless N13 probe records them explicitly instead of faking effects.
* Action 139's Human head-camera packing is not ported; AI uses SHOT_UNAIMED exactly, the Human branch is
  recorded and falls back to unaimed.
* The agenda Release phase (CRelaxToPosition netTarget, 0x00412050) is not ticked by the one-pass mock
  agenda; only Process is ported and reported. CAvoidObjects is kept persistent across the mock's
  purge pass and its per-item wrong-side counter replaces the original process-wide counter.
* CRelaxToPosition's camera-bias term needs the head-camera view fraction and the Checking rating; it is
  omitted and logged. Only goal-center markers 8/9 exist; other position markers stay unresolved.
* Per-slot mock worlds give decision 23 one-tick-old published teammate metadata, and decisions 31/57/123
  remain unsupported. Full PlanVM/PlanAction parity is not claimed.

## 7. Files

New: `PlanVM/PlanAiMatchActionsN13.cs`, `PlanVM/PlanAiMatchDecisionsN13.cs`,
`PlanVM/PlanAiMatchBehaviorParams.cs`, `PlanVM/PlanAiMatchAgendaItems.cs`,
`PlanVM/PlanAiMatchAgendaState.cs`, `docs/PLAN-HANDLERS-N13.md`. Modified additively: `PlanVM/PlanAiActions.cs`
(2 registrations), `PlanVM/PlanAiHost.cs` (approved extension calls), `PlanVM/PlanAiMatchContext.cs`,
`PlanVM/PlanAiMatchActions.cs` (goal-marker pronoun), `Native/MatchPlanAi.cs` (context/effects/audio/
telemetry), `Native/MatchPlanAiRoster.cs` (EnableN13Handlers), `Native/MatchPossession.cs` (default enable),
`Assets/Editor/UBlade3MatchPlay.cs` (CheckN13 + legacy pinning). Nothing committed.