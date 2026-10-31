# UBlade3 plan VM (ADLIB p-code plans)

This is the first slice of the **HyperBlade plan VM** port for the native C# engine. "ADLIB" here is
HyperBlade's PBN p-code plan machine (the AI state machine the original calls the plan VM), **not** the
AdLib audio chip. The new namespace is UBlade3.PlanVM; no UBlade2 code or assets are used.

> Status: **verified loader, instruction model, VM state and the documented execution core.**
> The engine-side handlers (all action bodies, all condition bodies, agenda items, animation/physics)
> are seams this slice records and reports; full AI/gameplay parity is **not** claimed. See
> [Remaining port gaps](#remaining-port-gaps).

## Source of truth

| Source | Role |
| --- | --- |
| third_party/hyperblade-decomp | byte-matching decompilation of HYPERX.EXE v1.0.0.1; every ported function/field cites its file and FUNCTION: HYPERX 0x...... address. |
| third_party/hyperblade-20XX/src/planvm/ | readable modern C++ reference, used for cross-checks and the same HYPERX address markers. |
| D:\Games\HyperBlade\HYB\data | the retail install used to verify the loader; proprietary, never copied into the repository. |
| third_party/hyperblade-decomp/tools/plan_graph.py and plan_dump.py | independent read-only decoder used to derive the expected per-plan digests in the probe. |

The original's own DATA\ENUMIDS.H is game data and is **not** copied. Every name printed by the port
comes from the decompilation or the 20XX source headers (planfunctionids.h, behaviorids.h,
agendaitemids.h, messageids.h, plannode.h, collisionobjectids.h).

## Files

All under unity/UBlade3/Assets/Scripts/UBlade3/PlanVM/. The code is scene-free (no UnityEngine
types) and compiles as C# 9 against the Unity .NET Standard 2.1 profile.

| File | Contents | Decompilation references |
| --- | --- | --- |
| PlanWord.cs | operand union, tags, markers, compiler tokens, node model (PlanNode, PlanOperand) | src/planvm/plannode.h (union PlanWord, PlanNode, markers); PlanWord::Pack |
| PlanOpcodes.cs | opcode categories/members, keyword table, handler-id names, fixed slots, collision-object ids, pronoun names | src/planvm/plannode.h; planvm.cpp PlanVM::PlanVM 0x0047d340; src/engine/objectclasses.h, collisionobjectids.h |
| PlanFile.cs | install/data path resolution, PLANS.CAT, PBN reader with exact word-count validation | PlanFile::ReadBinary 0x00436200; PlanFile::OpenBinary 0x00436270 |
| PlanArguments.cs | compile-time argument resolvers for all 57 pronouns; plan-name resolver seam | src/planvm/planarg.cpp/planarg.h 0x0047c780-0x0047d050; EngineObject::FindPlanByName 0x00471150 |
| PlanStreamParser.cs | ParseNodeStream plus the top-level Compile loop | PlanState::ParseNodeStream 0x0047b430; PlanState::Compile 0x0047b2d0 |
| PlanState.cs | the plan instance: compiled top nodes, behaviour chain, global interactors, local reals, activation, re-arm, teardown, reinitialize | src/planvm/planstate.{h,cpp}, 0x0047aef0-0x0047b990; GLOBAL 0x004c689c g_aReinitPlanImage |
| PlanBehavior.cs | one behaviour: setup pass, priority/event-kind assignment, interactor activation, poll countdown, pass-on, reset | GameBehaviorObject src/planvm/gamebehaviorobject.cpp, 0x0041bf50, 0x0041bff0, 0x0041c2c0, 0x0041c320, 0x0041c360, 0x0041c380, 0x0041c5d0, 0x0041c6c0 |
| PlanInteractor.cs | message/collision interactor setup, Enable capture, dispatch, collision process, factory linking | interactor.h/.cpp 0x0045d860; msginteractor.cpp 0x0045dfc0/0x0045e0b0/0x0045e0d0; collinteractor.cpp 0x0045dd00/0x0045ddf0; planinteractorfactories.cpp 0x0045dca0/0x0045df60 |
| PlanScriptRunner.cs | the exact Interactor::ExecuteScript opcode dispatch | Interactor::ExecuteScript 0x0045d860 |
| DecisionLog.cs | the packed decision log, recording and replay | src/planvm/decisionlog.h; ExecuteScript 0x0045d860/0x0045d963 |
| PlanVmHost.cs | the engine seam: phase, slot, action/condition/param/agenda dispatch, plan change, exit-behaviour, re-arm events | every handler table in planvm.cpp/plancond.cpp/planaction*.cpp |
| PlanVmRecordingHost.cs | scene-free host that records the seams and raises explicit unsupported-handler telemetry | probe support |
| PlanStatistics.cs | structural counts, opcode/leaf histograms and the canonical digest | independent of the decomp; mirrors tools/plan_graph.py |
| PlanVmSelfCheck.cs | 123 synthetic checks: loader, parser quirks, pronoun table, decision log, state machine, script dispatch, record/replay | drives the ported functions above |
| PlanVmProbe.cs | the gate: loads all 32 shipped plans, checks expected lines/digest, instantiates and sweeps every plan, prints known-plan traces | this document's output contract |

## PBN format (exact)

A .PBN is a little-endian 32-bit **word count**, then that many 32-bit words. The stream is a run of
compound nodes:

    node    := 0xABCDEFFF opcode childCount child*
    child   := node | string | leaf
    string  := 0xABCDEFFE charWord* 0
    leaf    := one word (integer, float bits, pronoun, local-real slot, interactor-name slot, token)
    END_PLAN:= 0xABCDEFFF 0xFFFF0000        (the one compound with no count word)

Details ported from PlanState::ParseNodeStream (0x0047b430):

* the stored child count is a **short**; the parsed child array length is count & 0x7fff;
* END_PLAN consumes two words and the top-level compile stops there (Compile would otherwise throw
  the original's "ERROR: No Instruction returned from DecodeArg at Top Level", string 0x004c68c0);
* strings are one character per word (low byte), zero-terminated; the original copies into
  char szName[32], so the port rejects longer literals instead of overflowing (shipped maximum is 24,
  in CAMERA.PBN);
* a leaf with high half 0xFEDC is a **pronoun** and is resolved at compile time for the actor slot
  (the compiled node stores the number); 0xEDCB is a local-real slot; 0xDCBA is an interactor-name
  slot; 0xABCDABCD/CC/CE/CB/CA are the compiler's seeded tokens;
* CHANGE_OF_PLAN's first child is a string naming another plan; the parser resolves it through
  EngineObject::FindPlanByName and stores the plan index (-1 when absent);
* the named-interactor counter in ParseNodeStream is the original's own quirk (it tests the parent
  opcode and first operand while walking children). No shipped plan contains SET_INTERACTOR_PARAM,
  so its value is 0 for all retail data; the parser reproduces the 20XX value-initialised reading and
  documents the decomp's uninitialised first read.

The loader is deliberately strict about the file size ((count + 1) * 4): the original logs
"Misread binary plan" (string 0x004bdf94) and can keep a short/partly initialised plan, which would
hide a damaged install. There is no truncation on the retail disc.

## Instruction vocabulary

Opcode = (category << 16) | member (plannode.h). Categories: collision interactor 0x0003, message
interactor 0x0005, debug 0x0006, behaviour 0x0007, plan change 0x0016, decide 0x0017, action 0x0018,
agenda 0x001A, global interactors 0x001B, data block 0x20000000, block 0x40000000, enable 0x80000000,
END_PLAN 0xFFFF0000. The full keyword table (26 words, including END_PLAN) is in PlanOpcodes.

Handler-slot tables (all numbers from the decomp/20XX source):

* actions ActionFunctionIDs 0-139, 135 implemented by PlanVM::InitRuntimeOpcodeTable
  (0x0047cd60); ids **2, 14, 15, 44, 121** have no handler;
* decisions 0-138, all implemented;
* behaviour parameters 0-16, all implemented;
* interactor parameters 0 (SetInteractorName) and 1 (SetActiveState);
* behaviours 0-24 in the 25-slot registry (BehaviorRegistry::Init 0x0041cdd0);
* agenda ids 0-34 (AgendaItemIDs).

## VM state

PlanState keeps the exact planstate.h fields: the compiled top-level array (m_ppBehaviors, END_PLAN
included), behaviour cursor/index/previous, behaviour chain head/current, the plan's global
collision/message interactor chains, the interactor count split, the collision re-arm counter, the
local-real array and the named-interactor slots.

* Compile/SetAndInit (0x0047b2d0 / 0x0047b110) parse the stream for one actor and count named
  interactors.
* Instantiate (0x0047b680) builds the behaviour chain in declaration order, runs each behaviour's
  setup pass, assigns collision event kinds, builds the global interactor chains, allocates/zeroes the
  local reals, and arms the collision re-arm counter at 10 ticks.
* ActivateBehavior (0x0047b1c0) records the previous behaviour only when leaving a non-digressive one,
  steps the chain (wrapping to the head when a standby link is missing), ticks and sends the new
  behaviour's messages, then follows every pass-on behaviour (CanPassOn: no interactors and a
  standby).
* PostUpdateCleanup (0x0047b290) re-arms the plan's global collision interactors when the counter
  reaches 0 and advances the current behaviour's poll countdown.
* Teardown (0x0047b900) drops local reals, discards both plan-level interactor chains, clears the
  owner's agenda observer list, resets the behaviour chain through the standby cascade and clears
  head/current.
* Reinitialize (0x0047b990) compiles the original nine-word image (g_aReinitPlanImage,
  GLOBAL 0x004c689c), reproduced word for word as PlanState.ReinitializeImage.

## Execution core (Interactor::ExecuteScript, 0x0045d860)

PlanScriptRunner.ExecuteScript is a line-for-line port of the original dispatch:

| Node | Behaviour |
| --- | --- |
| DO_ACTION | call the action handler with arg0 as its id |
| DO_NET_ACTION | skipped while the object is local, else action handler |
| DO_LOCAL_ACTION | skipped while the object is network, else action handler (falls through to DO_ACTION) |
| SET_INTERACTOR_PARAM | call the interactor-parameter handler (arg0 = id) |
| CHANGE_TO_BEHAVIOR | write the interactor's m_nBehaviour |
| SET_BEHAVIOR_PARAM | call the behaviour-parameter handler (arg0 = id) |
| CHANGE_OF_PLAN | write the interactor's plan slot and behaviour from arguments 0 and 1 |
| DECIDE_BY_AMONG / DECIDE_BY_WITH_AMONG | if the state word records, call the decision handler and push the branch; else pop the oldest branch. Then execute arm 1 + branch exactly like the original (the count test is argc - 1 > branch) |
| ADD_AGENDA_ITEM / SET_TIMEOUTMSG / REMOVE_AGENDA_ITEM | agenda seam (Bind vs BindWithNode forms preserved by passing the node or null) |
| Block | recurse into the block's children |
| anything else | throw PlanVmException("Invalid Instruction object in Interactor Process", opcode) - the original's string at 0x004c4494 |

Interactor setup follows MsgInteractor::Setup (0x0045dfc0) and CollInteractor::Setup (0x0045dd00):
arguments 0/1 are the collision pair or the message id, following children may be debug
instructions (ignored), a CHANGE_TO_BEHAVIOR (sticky m_nBehaviour), a SET_INTERACTOR_PARAM call, and
one Enable block. Dispatch (0x0045e0d0) / Process (0x0045ddf0) run the Enable block, apply the sticky
behaviour change (m_nBehaviour < _NO_BEH_CHANGE_ and enabled), reset the collision re-arm to 12, and
apply a CHANGE_OF_PLAN through the host.

The decision log is ported with its exact packing, including the third-branch masking fault called out
in the decomp notes (DecisionLog.Push uses ((word >> 2) & 0x1e0) | (word & 0x1f) | (branch & 0xe00)).
The local recording word carries bit 0x8000; a network copy replays a word with that bit masked off,
which is how the probe's replay check feeds it back.

## Probe

UBlade3.PlanVM.PlanVmProbe is scene-free and can run two ways:

* process: a small Main-bearing harness (the verification build below) calls PlanVmProbe.Run;
* Unity batch: -executeMethod UBlade3.PlanVM.PlanVmProbe.Check (environment HYPERBLADE_GAME_DATA must
  point at the install root or data folder).

Describe() is the one-line reflection entry point for a future PortLab status line.

Output contract (stable lines):

    UBLADE3_PLANVM_OK plans=32 instructions=9420 opcodes=16 unknowns=0 digest=141a750a3de1e083
    UBLADE3_PLANVM_DETAIL data=<dir> plans=<dir> catalog=32 fixes=0
    UBLADE3_PLANVM_COUNTS words=46068 nodes=9420 leaves=14207 strings=322 behaviors=185 globals=24 ...
    UBLADE3_PLANVM_OPS opcodes=16 list=<keyword=count,...>
    UBLADE3_PLANVM_PLAN name=... words=... nodes=... ... digest=<16 hex>   (one per plan)
    UBLADE3_PLANVM_COVERAGE ...
    UBLADE3_PLANVM_TRACE ...                                            (known-plan traces)
    UBLADE3_PLANVM_SELFTEST checks=123/123
    UBLADE3_PLANVM_FAIL ...                                             (only when failed)

The digest is FNV-1a 64 over a canonical text stream: a PLAN name count header, one line per
node/leaf/string in pre-order (NODE opcode count, LEAF raw, STR raw character words, END), then
ENDWALK consumed words. It covers the file words and tree shape only, so it is independent of the
actor slot. The per-plan expected lines and the aggregate digest were derived by a separate Python
reader of the same retail files (tools/plan_graph.py plus a reference walker), not by this port; they
are hard-coded in PlanVmProbe so a re-imported or damaged install fails loudly.

## Verification performed (2026-10-31)

Environment: .NET SDK 9.0.320 on Windows; retail data D:\Games\HyperBlade\HYB (32 /plans/*.pbn,
46,068 words). The Unity .NET Standard 2.1 profile was checked with a temporary project outside the
repository (C# 9 language level, warnings as errors):

    dotnet build PlanVmCore.csproj          # TargetFramework netstandard2.1, LangVersion 9.0
    # -> Build succeeded, 0 warnings, 0 errors

    dotnet build PlanVmProbeRunner.csproj   # TargetFramework net9.0, LangVersion 9.0, same sources + Main
    dotnet run --project PlanVmProbeRunner.csproj -- D:\Games\HyperBlade\HYB
    # -> exit 0, UBLADE3_PLANVM_OK line above

Probe results on the retail data:

* plans=32 instructions=9420 opcodes=16 unknowns=0 digest=141a750a3de1e083; all 32 per-plan lines
  match the independent expected values, including per-plan digests;
* totals: words 46,068; nodes 9,420; leaves 14,207; strings 322; behaviours 185; global-interactor
  blocks 24; collision interactors 256; message interactors 1,174; Enable blocks 1,183; DECIDE nodes
  578; DO_* action nodes 3,323; agenda add/remove nodes 168; max node depth 7; 0 unknown opcodes;
  0 unresolved CHANGE_OF_PLAN references; 0 .diff fix files in the retail data.
* PLANS.CAT cross-check: 32 names, every name backs a loaded file and every file is named;
* instantiation: 32/32 plans instantiate; 185/185 behaviours; 14 global collision and 227 global
  message interactors; every behaviour kind the data references
  (21 distinct ids of 0-24) is in the behaviour registry;
* dispatch sweep (every message interactor's Dispatch and every collision interactor's Process with a
  recording decision policy): 1,173 message script runs (one script raises the documented missing
  action 14) and 256 collision script runs; 102 of the 135 action ids, 88 of the 139 decision ids and
  12 of the 17 behaviour-parameter ids are exercised by the shipped data; 0 interactor-parameter
  sites; 182 timer nodes, 73 agenda adds, 73 removes, 75 plan changes and 558 exit-behaviour messages
  were reached; teardown ran for all 32 plans.
* synthetic self-check: **123/123** checks pass (decision log packing/replay, 57-pronoun table,
  parser masks/strings/END/plan-refs, loader rejections, reinitialize image, chain activation and
  pass-on, cursor/previous, invalid-behaviour-kind and END-before-behaviour guards, script dispatch,
  recording vs replay, with-among DataBlock arm, unsupported handlers);
* known-plan traces all pass: MINIMAL, COACH (pass-on into "CoachTheTeam", two message handlers 76/57,
  agenda CCheckAI 33 add+remove), CARRYROK (13 behaviours, GoForGoal index 1, the documented
  SET_TIMEOUTMSG 15, id_MSG_TimeOutMessage, id_PRN_Me data-0 timer), CRSHDUM0 (the documented
  id_ACF_CrashTestSetup(14) hole reachable from id_MSG_Test(77)), DEFENSE (the six documented
  slot-pronoun collision interactors) and LISTEN (EndOfPeriod 135: record PlaySound, WhoWonTheGame=0,
  SendMessage, then replay from the log with the same actions and no handler call).

No Unity editor/batch build was run: this checkout's Unity project may be imported by another
process concurrently (the same constraint recorded in docs/ASSET-FORMATS.md section 15). The explicit
verification is the dotnet C# 9 compile against netstandard2.1 plus the probe run above; the sources
contain no UnityEngine reference, so the Unity assembly build sees the same compilation unit. .meta
files are present for every new asset.

## Remaining port gaps

The following are unimplemented, explicitly reported by the VM rather than guessed:

* **All action handler bodies** - planaction.cpp, planactionlaunch.cpp, planactionrecoil.cpp,
  planactiontracking.cpp, planactionanim.cpp (135 slots). The VM dispatches the exact id and argument
  node; the handler itself is the host's job.
* **All decision handler bodies** - plancond.cpp (139 slots; ~250 KB of source). The probe calls no
  decision logic; a host supplies the branch.
* **Agenda items and timers** - all AgendaItem classes and pools (conditiontable.h,
  ctimeractivity.cpp, cavoidobjects.cpp, ...) plus GameBehaviorObject::TickAndSortMessages
  (0x0041c380) and SendTimedMessage's event/animation side. The VM models the nodes, records the
  agenda calls and the timer argument counts, but does not run an agenda list.
* **Behaviour subclasses** - the per-kind BehObj_*::Init extras (name overrides, agenda slot binds,
  digressive flags, PurgeAgendaFor* tables) and the behaviour pools. PlanBehavior is the generic
  GameBehaviorObject state model; IPlanVmHost.CreateBehavior stands in for the registry.
* **SET_BEHAVIOR_PARAM handler bodies** - behparam.cpp (17 slots). The VM dispatches the exact id; the
  state fields (m_bDigressive, latency, animation, targets) are not applied yet, so a behaviour
  entered in the probe keeps its constructor defaults.
* **SET_INTERACTOR_PARAM handlers** - PlanCond::SetInteractorName (0x0045e2f0) and SetActiveState
  (0x0045e320). No shipped plan uses them; the named-interactor array exists.
* **Runtime stat-pronoun resolvers** - g_apPlanStatFns / planfocusarg.cpp and PlanArg::ResolveFloat
  (0x0045e390) including local reals. Shipped plans declare no local reals; the compile-time pronoun
  table (all 57) is complete.
* **Plan tooling** - the text plan compiler (PlanVM::Compile, tokeniser, symbol tables, SaveBinary) is
  not ported; retail ships binaries only. PlanPatch / the 20XX .diff fix compiler is not applied (no
  .diff ships in the data directory; report shows fixes=0).
* **Owner plan tables** - EngineObject::FindPlanByName walks the object's plan list built by the data
  layer. The probe resolves the five names actually referenced (CarryRok, Defense, Human, OffWing,
  Sideln) case-insensitively onto the shipped files and reports a miss otherwise; the real per-object
  plan table remains a data-layer concern.
* **Palette operands** - union PlanWord also has a PaletteEntry view (colour leaves); no shipped plan
  uses that form (every leaf is int/float/pronoun/sentinel), so it is not modelled.
* **PlanUtil** - ComputeConeHalfAngle (0x004600c0) and CheckSequenceStep (0x004693d0) belong to the
  pass/AI layer, not the VM core.

Original undefined-behaviour paths are guarded with explicit exceptions instead of being reproduced:
a stream ending before END_PLAN, an unterminated/oversized string, END_PLAN before any
DECLARE_BEHAVIOR (the original dereferences the null behaviour), a behaviour kind outside the
25-slot registry, and a walk off the end of the behaviour chain. None is reachable from the retail
data; the probe's failure lines name the case if it ever is.

## Next slice (proposal)

1. Port the seventeen SET_BEHAVIOR_PARAM handlers (behparam.cpp) - they are pure state writes and
   unlock digressive behaviour, latency and the sticky-behaviour rules.
2. Port the agenda item classes and TickAndSortMessages with the per-kind purge tables, then the
   behaviour subclasses' Init extras.
3. Port plancond.cpp decisions plus g_apPlanStatFns/planfocusarg.cpp, then planaction*.cpp bodies with
   the physics/engine seams behind an engine adapter.
4. Wire PlanVmProbe.Describe() into UBlade3.App.PortLab as a status line and add the probe's Check()
   to the Unity batch gate once the project's build ownership is settled.
