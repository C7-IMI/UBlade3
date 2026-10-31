# PLAN-HANDLERS-N14 - default-match handler closure

N14 closes the last observed default-roster PlanVM gaps: decisions 31/57/123, the CRelaxToPosition
Release/netTarget phase, CAvoidObjects purge/global wrong-side behaviour, CRelax camera bias, and the
default-slot unsupported-telemetry check. It is bounded exact-body work, not full PlanVM parity.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3. Original data read-only; no
Data/Assets/Sim/App/ProjectSettings/native-release changes. Nothing committed; the Lead commits and
handles preview 008; no release package was built here.

## 1. Implemented bodies and citations

| Id / seam | Port / citation | N14 evidence |
| --- | --- | --- |
| decision 31 RokInWhichTeamsPoss | PlanCond::RokInWhichTeamsPoss plancond.cpp HYPERX 0x00461b20 | 1 execution; ROK attach slot <6 -> slot&1, otherwise 2 |
| decision 57 MeOrTeammateClosestToRok | PlanCond::MeOrTeammateClosestToRok plancond.cpp HYPERX 0x004663f0 | 1 execution; actor/teammate squared distance, teammate damage gate, bound-carrier override |
| decision 123 CheckPassingDistance | PlanCond::CheckPassingDistance plancond.cpp HYPERX 0x00466a80 | 2 executions; LongPassEnable rating 11, PASS_NEAR_RANGE_SQUARED 22000, knocked-down gate |
| agenda 15 Release / netTarget | CRelaxToPosition::Release crelaxtoposition.cpp HYPERX 0x00412050 | Release called from MatchPlanAi after the agenda Process pass; 478 releases, 478 NetTarget applications to the real Sim body |
| CRelax camera bias | `ViewFracX` from the real MatchHeadCamera, `(0.5 - viewFrac)` * ±16, `Checking` rating id 2 * rate * 0.2 | 956 camera-bias uses across Process+Release; head-camera view fraction is live from `MatchPlayControl.HeadCam` |
| CAvoidObjects purge | Source `AgendaItem`/`GameBehaviorObject::TickAndSortMessages`: non-persistent CAvoid is detached on behaviour activation; source `IsReplaceable` 1 | CAvoid now non-persistent + replaceable under N14; 105 detach events observed in the probe |
| CAvoid wrong-side nuance | Source file-scope `g_nWrongSideFrames` (one counter per process), limit 0x14, then MSG_BREAKAWAY | N14 uses one process-wide counter; the N13 per-item counter is kept only for the pinned N13 handler set |
| CRelax purge semantics | Source `GetReadyFlag` returns 1 and the item is not persistent; `TickAndSortMessages` detaches it on behaviour activation | N14 overrides `GetReadyFlag` true and marks CRelax non-persistent; `relaxDetach=0` is reported because the staged goalie never activates a second behaviour in this window |

New file: `PlanVM/PlanAiMatchDecisionsN14.cs` (the `PlanAiMatchDecisionsN14.Enabled` flag and decisions
31/57/123). The N14 flag is enabled by the default/N13+/N14 roster before its plans are instantiated
(`MatchPossession`, `MatchPlanAiRoster.EnableN14Handlers`), and deliberately off for the pinned N4-N13
regression suites. The N13 agenda code now switches to the source purge/wrong-side/Release semantics only
when the N14 flag is on; with it off the pinned N13 evidence is unchanged.

### Real effects

* decisions 31/57/123 execute in the real default PlanVM roster (1/1/2 executions in the probe).
* CRelax Release writes the plan `netTarget`; `MatchPlanAi.SyncAgendaEffects` publishes it to the real
  Sim `PhysicsBodyState.NetTarget` and position (478 applications), with the source `NetTargetPending`
  publication and terrain normal/aim update.
* CRelax camera bias reads the live head-camera `ViewFracX` and the real `Checking` rating (copied from
  Sim stats into the mirror); the bias and rate are applied in both Process and Release.
* CAvoid parent source semantics are applied: non-persistent item, replaceable class, one process-wide
  wrong-side counter, real MSG_BLOCKED/MSG_BREAKAWAY plan posts, and detach on behaviour activation.
* Friction, location, speed-burst, agenda smoothVel/position/heading/netTarget remain real Sim effects.

## 2. Default-slot unsupported telemetry

`CheckN14` aggregates every roster adapter's `PlanAiHost.UnsupportedHandlers`; the default N14 run reports
`unsupportedHandlers=0`, and all five `UBLADE3_PLAN_AI_SLOT` lines print `unsupported=[]`. This covers
decisions, actions, behaviour parameters and agenda items for the current Defense/CarryRok/OffWing/Goalie
plan set. The pinned N4-N13 suites still report their historical unsupported sets because they are pinned
to the earlier handler sets.

## 3. Deterministic probe (CheckN14)

The normal default build is run twice at 240 ticks with the N10 shot staging and the N14 handler set.

Evidence (artifacts/ublade3/n14-check-final.log):

    UBLADE3_NATIVE_MATCH_N14_RESULT defaultPlanAi=True targetPhysics=True ticks=240
        aiPickups=1 aiPasses=1 aiShots=0 decisions=[31x1,57x1,123x2]
        effects=[behParams=12,targetGoal=1,agendaAdds8=25,agendaAdds15=2,
                 agendaSmoothVel=42,agendaPos=478,agendaRelax=478,release=478,
                 netTarget=478,cameraBias=956,avoidDetach=105,relaxDetach=0,
                 soundEvents=1,soundApplied=0,soundUnavailable=1,
                 animationEvents=10,animationApplied=0,statEvents=1,statApplied=0,
                 bubbleEvents=2,bubbleApplied=0,unsupportedHandlers=0]
        digest=90ee53431fc98757
    UBLADE3_NATIVE_MATCH_N14_DETERMINISM ok=True digest1=digest2=90ee53431fc98757
    UBLADE3_NATIVE_MATCH_N14_CHECK_OK     (mismatches=0)
    slot unsupported=[]  (all five slots, both runs)

## 4. Effects with/without engine consumers

| Effect | Status |
| --- | --- |
| Audio action 7 | Wired to `MatchPlayControl.Audio.Service.PlayIndex` (AudioMgr PlaySpecific). Headless `CheckN14` has no audio service and reports `soundUnavailable=1` exactly; the interactive player installs the default service. Not player-verified in this N14 run. |
| Animation action 30 / behparams 9/11/12 | Plan ids/rates are exact telemetry; the match's SkaterView clip clock has no consumer wired from a plan animation id, and adding one needs a MatchScene/MatchSkaterView edit outside the allowed file scope. No fake clip is played. |
| Stat action 9 | No match-stat accumulator/stat screen path in this slice; exact request telemetry (`statApplied=0`). |
| Bubble action 71 | Sim collision does not consume awareness-bubble radii; exact radius/telemetry (`bubbleApplied=0`). |

## 5. Verification

| Gate | Result |
| --- | --- |
| N14 CheckN14 x2 | UBLADE3_NATIVE_MATCH_N14_CHECK_OK, same digest 90ee53431fc98757, mismatches=0, unsupportedHandlers=0 |
| N13 | UBLADE3_NATIVE_MATCH_N13_CHECK_OK (pinned N13 set; N14 flag off) |
| N12 | UBLADE3_NATIVE_MATCH_N12_CHECK_OK |
| N11 | UBLADE3_NATIVE_MATCH_N11_CHECK_OK |
| N10 | UBLADE3_NATIVE_MATCH_N10_CHECK_OK |
| N9 | UBLADE3_NATIVE_MATCH_N9_CHECK_OK |
| N8 | UBLADE3_NATIVE_MATCH_N8_CHECK_OK |
| N7 | UBLADE3_NATIVE_MATCH_N7_CHECK_OK |
| N6 | UBLADE3_NATIVE_PLAY_CHECK_OK |
| N4/N5 + PlanVM/Data | UBLADE3_CHECKS_OK |
| Fast checks | python scripts/check_all.py PASS |

## 6. Honest gaps

* CRelax Release is invoked from the mock's one-pass agenda in the adapter; the original physics phase
  ordering is approximated. `relaxDetach=0` because the staged goalie stays in one behaviour; the purge
  code is present and CAvoid purge is exercised (105 detaches). CAvoid Release is a no-op (base class).
* CAvoidObjects uses the documented range/strength/global wrong-side behaviour but the mock has no
  behaviour agenda slots; item lifetime is the owner observer list, not the original per-behaviour slots.
* Animation/stat/bubble ids are exact telemetry; no render/stat/bubble consumer was added because the
  allowed file scope has no seam and no fake was acceptable.
* Audio is record-only in headless (no device/service); interactive playback uses the existing
  AudioService path. Action 139's Human head-camera packing still falls back to unaimed.
* The default plan set is empty-unsupported only for the current plan set/240-tick staging; a plan change
  can reach bodies outside these sets. Full PlanVM/PlanAction parity is not claimed. Per-slot mock worlds
  remain a structural approximation of the original shared GameEngine object table.

## 7. Files

New: `PlanVM/PlanAiMatchDecisionsN14.cs`, `docs/PLAN-HANDLERS-N14.md`. Additive N14 edits:
`PlanVM/PlanAiMatchAgendaItems.cs` (Release/purge/global wrong-side/camera bias),
`PlanVM/PlanAiMatchAgendaState.cs` (NetTarget/Release/camera counters),
`PlanVM/PlanAiMatchContext.cs` (head-camera view fraction), `PlanVM/PlanAiHost.cs` (approved seam already
used; no new host edit for N14), `Native/MatchPlanAi.cs` (N14 decisions, Release call, NetTarget apply,
live headcam view, telemetry, long-pass/checking mirror stats), `Native/MatchPlanAiRoster.cs`
(`EnableN14Handlers` + reset), `Native/MatchPossession.cs` (default enable/reset),
`Assets/Editor/UBlade3MatchPlay.cs` (CheckN14; legacy N14 pinning). Nothing committed.