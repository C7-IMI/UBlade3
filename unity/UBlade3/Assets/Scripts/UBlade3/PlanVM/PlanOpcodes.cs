using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The plan instruction vocabulary: opcode categories and members (decomp src/planvm/plannode.h,
    /// 20XX src/planvm/plannode.h), the keyword table the plan compiler seeds
    /// (PlanVM::PlanVM, HYPERX 0x0047d340), and the handler-slot id names the runtime tables fill
    /// (PlanVM::InitRuntimeOpcodeTable, HYPERX 0x0047cd60; BehaviorRegistry::Init, 0x0041cdd0).
    /// Only names defined by the decompilation or the readable 20XX port are used here; names that
    /// exist only in the retail ENUMIDS.H are deliberately not copied.
    /// </summary>
    public static class PlanOpcodes
    {
        public const int CategoryMask = unchecked((int)0xFFFF0000);

        public const int MemberMask = 0xFFFF;

        // Full opcode words, each named for the authored keyword. src/planvm/plannode.h OpcodeCategory,
        // src/planvm/plannode.h CollisionInteractorMember..AgendaMember.
        public const int OpcodeSetCollisionInteractor = 0x00030000;

        public const int OpcodeSetLocCollisionInteractor = 0x00030001;

        public const int OpcodeSetMessageInteractor = 0x00050000;

        public const int OpcodeSetLocMessageInteractor = 0x00050001;

        public const int OpcodeSetNetMessageInteractor = 0x00050002;

        public const int OpcodeSetDebug = 0x00060001;

        public const int OpcodeSetTrace = 0x00060002;

        public const int OpcodeDeclareBehavior = 0x00070000;

        public const int OpcodeChangeToBehavior = 0x00070002;

        public const int OpcodeSetBehaviorParam = 0x00070003;

        public const int OpcodeChangeOfPlan = 0x00160000;

        public const int OpcodeDeclareLocalReals = 0x00160001;

        public const int OpcodeDecideByAmong = 0x00170000;

        public const int OpcodeDecideByWithAmong = 0x00170001;

        public const int OpcodeDoAction = 0x00180000;

        public const int OpcodeSetInteractorParam = 0x00180001;

        public const int OpcodeDoLocalAction = 0x00180002;

        public const int OpcodeDoNetAction = 0x00180003;

        public const int OpcodeAddAgendaItem = 0x001A0001;

        public const int OpcodeSetTimeoutMsg = 0x001A0002;

        public const int OpcodeRemoveAgendaItem = 0x001A0003;

        public const int OpcodeGlobalInteractors = 0x001B0000;

        public const int OpcodeDataBlock = 0x20000000;

        public const int OpcodeBlock = 0x40000000;

        public const int OpcodeEnable = unchecked((int)0x80000000);

        public const int OpcodeEndPlan = unchecked((int)0xFFFF0000);

        public enum Category
        {
            CollisionInteractor = 0x00030000,
            MessageInteractor = 0x00050000,
            Debug = 0x00060000,
            Behaviour = 0x00070000,
            PlanChange = 0x00160000,
            Decide = 0x00170000,
            Action = 0x00180000,
            Agenda = 0x001A0000,
            GlobalInteractors = 0x001B0000,
            DataBlock = 0x20000000,
            Block = 0x40000000,
            Enable = int.MinValue,
            EndPlan = -0x10000,
        }

        public enum CollisionInteractorMember
        {
            SetCollisionInteractor = 0,
            SetLocCollisionInteractor = 1,
        }

        public enum MessageInteractorMember
        {
            SetMessageInteractor = 0,
            SetLocMessageInteractor = 1,
            SetNetMessageInteractor = 2,
        }

        public enum DebugMember
        {
            SetDebug = 1,
            SetTrace = 2,
        }

        public enum BehaviourMember
        {
            DeclareBehavior = 0,
            BehaviorName = 1,
            ChangeToBehavior = 2,
            SetBehaviorParam = 3,
        }

        public enum PlanChangeMember
        {
            ChangeOfPlan = 0,
            DeclareLocalReals = 1,
        }

        public enum DecideMember
        {
            DecideByAmong = 0,
            DecideByWithAmong = 1,
        }

        public enum ActionMember
        {
            DoAction = 0,
            SetInteractorParam = 1,
            DoLocalAction = 2,
            DoNetAction = 3,
        }

        public enum AgendaMember
        {
            AddAgendaItem = 1,
            SetTimeoutMsg = 2,
            RemoveAgendaItem = 3,
        }

        public const int DecisionTableBase = 2;

        public const int ActionHandlerSlotCount = 140;

        public const int DecisionHandlerSlotCount = 139;

        public const int BehaviorParamHandlerSlotCount = 17;

        public const int BehaviorKindCount = 25;

        public const int AgendaItemCount = 35;

        // The fixed object slots (src/engine/objectclasses.h).
        public const int SlotNone = -1;

        public const int SlotRok = 6;

        public const int SlotDromer0 = 0;

        public const int SlotDromer1 = 1;

        public const int SlotDromer2 = 2;

        public const int SlotDromer3 = 3;

        public const int GoalieSlotA = 4;

        public const int GoalieSlotB = 5;

        public const int SlotNgoal = 7;

        public const int SlotSgoal = 8;

        public const int SlotRamp = 12;

        public const int SlotMultiplier = 27;

        public const int SlotTurnstile = 33;

        public const int SlotLimboPole = 40;

        public const int SlotLaserHurdle = 46;

        public const int SlotArmory = 52;

        public const int SlotMine = 54;

        public const int SlotSwarf = 55;

        public const int SlotFastLane = 56;

        public const int SlotSlowLane = 58;

        public const int SlotExplosion = 60;

        public const int SlotHurdle = 66;

        public const int SlotGreenRandomizer = 72;

        public const int SlotRedRandomizer = 80;

        public const int SlotBodyPart = 94;

        public const int PlayerSlotCount = 4;

        public const int PlayerSlotMask = 3;

        // Collision object ids (src/engine/collisionobjectids.h).
        public const int CollisionDromer0 = 0x00;

        public const int CollisionBatonEzone0 = 0x01;

        public const int CollisionDromerBody0 = 0x02;

        public const int CollisionDromer1 = 0x03;

        public const int CollisionNgoalZone = 0x12;

        public const int CollisionNgoalSide = 0x15;

        public const int CollisionSgoalZone = 0x16;

        public const int CollisionSgoalSide = 0x19;

        public const int CollisionRamp = 0x1D;

        public const int CollisionDr0Awareness = 0x29;

        public const int CollisionMultiplier = 0x31;

        public const int CollisionTurnstile = 0x37;

        public const int CollisionLimboPole = 0x3E;

        public const int CollisionLaserHurdle = 0x44;

        public const int CollisionArmory = 0x4A;

        public const int CollisionMine = 0x4C;

        public const int CollisionSwarf = 0x4D;

        public const int CollisionFastLane = 0x4E;

        public const int CollisionSlowLane = 0x50;

        public const int CollisionExplosion = 0x52;

        public const int CollisionHurdle = 0x58;

        public const int CollisionGreenRandomizer = 0x5F;

        public const int CollisionRedRandomizer = 0x67;

        public const int CollisionBodyPart0 = 0x6D;

        public const int CollisionPerDromer = 3;

        // Position ids a team's parity selects (src/planvm/planarg.cpp constants).
        public const int PositionNGoalDefender = 0x02;

        public const int PositionHomeDragStartPos = 0x04;

        public const int PositionNGoalCenter = 0x08;

        /// <summary>objectclasses.h TeammateSlot: a skater's mate is two slots on; a goalie folds onto skater 0/1.</summary>
        public static int TeammateSlot(int slot)
        {
            int teammate = slot < PlayerSlotCount ? slot + 2 : slot;

            return teammate & PlayerSlotMask;
        }

        /// <summary>Action handler slots PlanVM::InitRuntimeOpcodeTable fills. Ids 2, 14, 15, 44 and 121 have no handler.</summary>
        public static bool IsSupportedActionId(int id)
        {
            return id >= 0 && id < ActionHandlerSlotCount && id != 2 && id != 14 && id != 15 && id != 44 && id != 121;
        }

        /// <summary>DECIDE handler slots: planvm.cpp fills all of 0..138.</summary>
        public static bool IsSupportedDecisionId(int id)
        {
            return id >= 0 && id < DecisionHandlerSlotCount;
        }

        /// <summary>SET_BEHAVIOR_PARAM slots: behaviorregistry.cpp fills all of 0..16.</summary>
        public static bool IsSupportedBehaviorParamId(int id)
        {
            return id >= 0 && id < BehaviorParamHandlerSlotCount;
        }

        /// <summary>SET_INTERACTOR_PARAM slots: only SetInteractorName and SetActiveState exist.</summary>
        public static bool IsSupportedInteractorParamId(int id)
        {
            return id == InteractorParamIds.SetInteractorName || id == InteractorParamIds.SetActiveState;
        }

        /// <summary>The behaviour registry ends at its first null; all 25 ids 0..24 are registered (behaviorregistry.cpp).</summary>
        public static bool IsSupportedBehaviorKind(int kind)
        {
            return kind >= 0 && kind < BehaviorKindCount;
        }

        private static readonly Dictionary<int, string> KeywordTable = BuildKeywordTable();

        private static Dictionary<int, string> BuildKeywordTable()
        {
            Dictionary<int, string> table = new Dictionary<int, string>();

            table.Add(OpcodeSetCollisionInteractor, "SET_COLLISION_INTERACTOR");
            table.Add(OpcodeSetLocCollisionInteractor, "SET_LOC_COLLISION_INTERACTOR");
            table.Add(OpcodeSetMessageInteractor, "SET_MESSAGE_INTERACTOR");
            table.Add(OpcodeSetLocMessageInteractor, "SET_LOC_MESSAGE_INTERACTOR");
            table.Add(OpcodeSetNetMessageInteractor, "SET_NET_MESSAGE_INTERACTOR");
            table.Add(OpcodeSetDebug, "SET_DEBUG");
            table.Add(OpcodeSetTrace, "SET_TRACE");
            table.Add(OpcodeDeclareBehavior, "DECLARE_BEHAVIOR");
            table.Add(OpcodeChangeToBehavior, "CHANGE_TO_BEHAVIOR");
            table.Add(OpcodeSetBehaviorParam, "SET_BEHAVIOR_PARAM");
            table.Add(OpcodeChangeOfPlan, "CHANGE_OF_PLAN");
            table.Add(OpcodeDeclareLocalReals, "DECLARE_LOCAL_REALS");
            table.Add(OpcodeDecideByAmong, "DECIDE_BY_AMONG");
            table.Add(OpcodeDecideByWithAmong, "DECIDE_BY_WITH_AMONG");
            table.Add(OpcodeDoAction, "DO_ACTION");
            table.Add(OpcodeSetInteractorParam, "SET_INTERACTOR_PARAM");
            table.Add(OpcodeDoLocalAction, "DO_LOCAL_ACTION");
            table.Add(OpcodeDoNetAction, "DO_NET_ACTION");
            table.Add(OpcodeAddAgendaItem, "ADD_AGENDA_ITEM");
            table.Add(OpcodeSetTimeoutMsg, "SET_TIMEOUTMSG");
            table.Add(OpcodeRemoveAgendaItem, "REMOVE_AGENDA_ITEM");
            table.Add(OpcodeGlobalInteractors, "GLOBAL_INTERACTORS");
            table.Add(OpcodeDataBlock, "DataBlock");
            table.Add(OpcodeBlock, "Block");
            table.Add(OpcodeEnable, "Enable");
            table.Add(OpcodeEndPlan, "END_PLAN");

            return table;
        }

        public static bool TryKeyword(int opcode, out string keyword)
        {
            return KeywordTable.TryGetValue(opcode, out keyword);
        }

        /// <summary>The compiler keyword, or "op_cccc_mmmm" for a word outside the vocabulary.</summary>
        public static string KeywordOf(int opcode)
        {
            string keyword;

            if (KeywordTable.TryGetValue(opcode, out keyword))
            {
                return keyword;
            }

            return "op_" + ((uint)opcode >> 16).ToString("x4") + "_" + ((uint)opcode & 0xFFFF).ToString("x4");
        }

        public static bool IsKnownOpcode(int opcode)
        {
            return KeywordTable.ContainsKey(opcode);
        }

        public const int KnownOpcodeCount = 26;

        public static string CategoryName(int opcode)
        {
            switch (opcode & CategoryMask)
            {
                case (int)Category.CollisionInteractor: return "collision_interactor";
                case (int)Category.MessageInteractor: return "message_interactor";
                case (int)Category.Debug: return "debug";
                case (int)Category.Behaviour: return "behaviour";
                case (int)Category.PlanChange: return "plan_change";
                case (int)Category.Decide: return "decide";
                case (int)Category.Action: return "action";
                case (int)Category.Agenda: return "agenda";
                case (int)Category.GlobalInteractors: return "global_interactors";
                case (int)Category.DataBlock: return "data_block";
                case (int)Category.Block: return "block";
                case (int)Category.Enable: return "enable";
                case (int)Category.EndPlan: return "end_plan";
                default: return "unknown";
            }
        }

        /// <summary>The enum that names a leaf at a given argument position (decomp tools/plan_graph.py ARGUMENT_ENUM).</summary>
        public static string ArgumentEnumFor(int opcode, int position)
        {
            switch (opcode)
            {
                case OpcodeSetMessageInteractor:
                case OpcodeSetLocMessageInteractor:
                case OpcodeSetNetMessageInteractor:
                    return position == 0 ? "MessageIds" : null;
                case OpcodeDecideByAmong:
                case OpcodeDecideByWithAmong:
                    return position == 0 ? "DecisionIds" : null;
                case OpcodeDoAction:
                case OpcodeDoLocalAction:
                case OpcodeDoNetAction:
                    return position == 0 ? "ActionIds" : null;
                case OpcodeAddAgendaItem:
                case OpcodeRemoveAgendaItem:
                    return position == 0 ? "AgendaItemIds" : null;
                case OpcodeSetTimeoutMsg:
                    return position == 1 ? "MessageIds" : null;
                case OpcodeDeclareBehavior:
                    return position == 0 ? "BehaviorIds" : null;
                case OpcodeSetBehaviorParam:
                    return position == 0 ? "BehaviorSetParamIds" : null;
                case OpcodeSetInteractorParam:
                    return position == 0 ? "InteractorParamIds" : null;
                default:
                    return null;
            }
        }

        public static string ActionName(int id)
        {
            string name;

            if (ActionNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "ACF_" + id + "_UNMAPPED";
        }

        public static string DecisionName(int id)
        {
            string name;

            if (DecisionNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "DCF_" + id + "_UNMAPPED";
        }

        public static string BehaviorName(int id)
        {
            string name;

            if (BehaviorNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "BEH_" + id + "_UNMAPPED";
        }

        public static string BehaviorParamName(int id)
        {
            string name;

            if (BehaviorSetParamNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "SET_" + id + "_UNMAPPED";
        }

        public static string InteractorParamName(int id)
        {
            string name;

            if (InteractorParamNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "ISET_" + id + "_UNMAPPED";
        }

        public static string AgendaItemName(int id)
        {
            string name;

            if (AgendaItemNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "AGD_" + id + "_UNMAPPED";
        }

        public static string MessageName(int id)
        {
            string name;

            if (MessageNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "MSG_" + id + "_UNMAPPED";
        }

        public static string PronounName(int id)
        {
            string name;

            if (PronounNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "PRN_" + id + "_UNMAPPED";
        }

        public static string CollisionObjectName(int id)
        {
            string name;

            if (CollisionObjectNames.TryGetValue(id, out name))
            {
                return name;
            }

            return "COB_" + id + "_UNMAPPED";
        }

    // ActionFunctionIDs: the plan action handler slots (src/planvm/planfunctionids.h).
    public static class ActionIds
    {
        public const int SetAttachPak = 0;
        public const int UnsetAttachPak = 1;
        public const int Launch = 3;
        public const int LaunchToward = 4;
        public const int SendMessage = 5;
        public const int SetSpeedBurst = 6;
        public const int PlaySound = 7;
        public const int NoteStats4Possessors = 8;
        public const int IncrementStat = 9;
        public const int StartStatTimer = 10;
        public const int EndStatTimer = 11;
        public const int PlaySndType = 12;
        public const int PanSndType = 13;
        public const int AnimationPeek = 16;
        public const int EngageOpponent = 17;
        public const int SetLocation = 18;
        public const int SetVelocity = 19;
        public const int SetObjectToTrack = 20;
        public const int SetTrackToViewOwner = 21;
        public const int SetTrackingOffset = 22;
        public const int IncrementTrackingOffset = 23;
        public const int IncrementObjectToTrack = 24;
        public const int SetObjectToWatch = 25;
        public const int IncrementObjectToWatch = 26;
        public const int SetInDrag = 27;
        public const int SetOnRim = 28;
        public const int SetFriction = 29;
        public const int SetAnimation = 30;
        public const int RestorePersistentAnimation = 31;
        public const int SetAnimationEvent = 32;
        public const int SetAnimationState = 33;
        public const int HookDragBungeesOn = 34;
        public const int ResetAccTransient = 35;
        public const int SetAnimationDoneMessage = 36;
        public const int LaunchAt = 37;
        public const int Throw = 38;
        public const int SetAnimationSpeed = 39;
        public const int IncrAnimationSpeed = 40;
        public const int ResetCameraBufferSize = 41;
        public const int ThrowBungee = 42;
        public const int ResetKeystateFlags = 43;
        public const int SetCamOffsetToLocation = 45;
        public const int LaunchGunDirection = 46;
        public const int RelaxYawToFace = 47;
        public const int ResetReticle = 48;
        public const int SetHeadCamText = 49;
        public const int InitRearViewPos = 50;
        public const int DopplerSound = 51;
        public const int IncrementBungeeLength = 52;
        public const int SetOffsetValues = 53;
        public const int SetBungeeLength = 54;
        public const int PlaceSound = 55;
        public const int IncrementCameraDir = 56;
        public const int SetTrackHeight = 57;
        public const int SetMinHeight = 58;
        public const int SetLookHeight = 59;
        public const int SetCamBufferFactor = 60;
        public const int SetScanModes = 61;
        public const int SetSolarWindFactor = 62;
        public const int SetPaletteEffect = 63;
        public const int SignalKeystate = 64;
        public const int SetMyGravity = 65;
        public const int RecoilOffWall = 66;
        public const int ClothesLineRecoil = 67;
        public const int ManOnManRecoil = 68;
        public const int GetKeystateFlags = 69;
        public const int SetLocalReal = 70;
        public const int ScaleBubToRating = 71;
        public const int SetRenderFlag = 72;
        public const int IncRokValue = 73;
        public const int SetRokKillState = 74;
        public const int ModifyDamage = 75;
        public const int ReflectOffWall = 76;
        public const int ModifyPitch = 77;
        public const int ReflectOffModel = 78;
        public const int SetTargetWithData = 79;
        public const int BungeeTowardTarget = 80;
        public const int GlideOnRamp = 81;
        public const int GrayscaleRandomizer = 82;
        public const int SetImmunity = 83;
        public const int SpeedBurstSituation = 84;
        public const int IncrAnimSpeedWRating = 85;
        public const int ModifyOpposTeamFriction = 86;
        public const int SetDeviceOwner = 87;
        public const int SetInputMode = 88;
        public const int IncrementScore = 89;
        public const int SwapRandomizers = 90;
        public const int ModifyRating = 91;
        public const int ModifyAvailableReds = 92;
        public const int ReallySendMsg = 93;
        public const int ExplodeFlat = 94;
        public const int SetShotPowerMeter = 95;
        public const int HoverAboveArmory = 96;
        public const int DeployWeapon = 97;
        public const int SetAttackee = 98;
        public const int ModifySpeedRating = 99;
        public const int ExplodeAgainstVelocity = 100;
        public const int ExplodeWithVelocity = 101;
        public const int ExplodeUniform = 102;
        public const int SetKnockedDownState = 103;
        public const int SpinTurnstile = 104;
        public const int LoseHead = 105;
        public const int Trace = 106;
        public const int RegainHead = 107;
        public const int SetKilledDromer = 108;
        public const int AnimationSemaphore = 109;
        public const int ResetCameraTrack = 110;
        public const int ClockStoppedSemaphore = 111;
        public const int SetInfoBarText = 112;
        public const int BounceOffDromer = 113;
        public const int BodySplit = 114;
        public const int CreditDamage = 115;
        public const int TrackTurnstile = 116;
        public const int DesiredSubstitute = 117;
        public const int SubstituteDromer = 118;
        public const int SetInfoTextDromer = 119;
        public const int SetLaserHurdleCharge = 120;
        public const int SetInfoTextTeam = 122;
        public const int ClearBehaviorAnimation = 123;
        public const int SendMessageToMyCamera = 124;
        public const int IfNotRokTrackMe = 125;
        public const int TransferViewOwnership = 126;
        public const int SmallBloodSpurt = 127;
        public const int LargeBloodSpurt = 128;
        public const int SetMaxSpeed = 129;
        public const int EndPeriodNow = 130;
        public const int KillBallStaging = 131;
        public const int KillBallExecute = 132;
        public const int KickOffRim = 133;
        public const int FlyingSemaphore = 134;
        public const int KickDownToSurface = 135;
        public const int SyncPossession = 136;
        public const int SyncSystem = 137;
        public const int SyncDamage = 138;
        public const int SendLaunchMessage = 139;
    }
    // DecisionFunctionIDs: the DECIDE handler slots (src/planvm/planfunctionids.h).
    public static class DecisionIds
    {
        public const int DecideAttachment = 0;
        public const int DecideCelebration = 1;
        public const int DecidePassOrRun = 2;
        public const int DecideHPSpinRailOrNot = 3;
        public const int PickACell = 4;
        public const int Random = 5;
        public const int IsSenderTeammate = 6;
        public const int AmMainDromer = 7;
        public const int RunTimeData = 8;
        public const int IsRokInMyZone = 9;
        public const int AmViewOwner = 10;
        public const int TeammateIsViewOwner = 11;
        public const int OwnerIsViewOwner = 12;
        public const int ViewOwnersMateInFront = 13;
        public const int IHaveRok = 14;
        public const int DecidePosition = 15;
        public const int CollisionDetail = 16;
        public const int EngageWhichOpponent = 17;
        public const int TeammateHasRok = 18;
        public const int GagTheAnnouncer = 19;
        public const int VictimCamIsOn = 20;
        public const int DecideBlockOrGoOut = 21;
        public const int WhichOppnIsClosestToRok = 22;
        public const int ClosestUncoveredOppn = 23;
        public const int AnalyzePadA = 24;
        public const int AnalyzePadB = 25;
        public const int AnalyzePadC = 26;
        public const int AttackerIsInRear = 27;
        public const int SpeedBurstIsRelaxed = 28;
        public const int OffenseOrWRok = 29;
        public const int HostIsViewOwner = 30;
        public const int RokInWhichTeamsPoss = 31;
        public const int IsDoubleScore = 32;
        public const int MyID = 33;
        public const int MessageData = 34;
        public const int BungeeVictimLorR = 35;
        public const int IfInFaceOff = 36;
        public const int ShotPower = 37;
        public const int IsScanningModeOn = 38;
        public const int MessageDataNibl0 = 39;
        public const int MessageDataNibl1 = 40;
        public const int MessageDataNibl4 = 41;
        public const int MsgData4Nibl1X2Nibl0 = 42;
        public const int SideKeysAreDown = 43;
        public const int SpinUp = 44;
        public const int AmIKnockedLoose = 45;
        public const int CatchBlockOrMiss = 46;
        public const int RokScoreValue = 47;
        public const int Jumping = 48;
        public const int ChoosePassingPlay = 49;
        public const int RecvrInRangeNView = 50;
        public const int WhichOppnIsClosestToMe = 51;
        public const int TakeDamage = 52;
        public const int Ducking = 53;
        public const int RokIsCharged = 54;
        public const int RokIsChargedAgainst = 55;
        public const int ClosestTargetToProjectile = 56;
        public const int MeOrTeammateClosestToRok = 57;
        public const int IsAmbient = 58;
        public const int DromerHurdleCollision = 59;
        public const int DromerHasRok = 60;
        public const int RokHurdleCollision = 61;
        public const int DromerMultiplierCollision = 62;
        public const int DromerLimboCollision = 63;
        public const int DromerRandomizerCollision = 64;
        public const int RokValueInMyFavor = 65;
        public const int DromerRampCollision = 66;
        public const int PassOrRunByRatings = 67;
        public const int LooseRokWhatToDo = 68;
        public const int GoThroughMuliplier = 69;
        public const int PickAttackMove = 70;
        public const int OffWingJob = 71;
        public const int PickFlyingMove = 72;
        public const int PickHalfPipeMove = 73;
        public const int AvoidSpinningTurnstile = 74;
        public const int TargetTurnstile = 75;
        public const int UseKillball = 76;
        public const int UseRandomizer = 77;
        public const int ShootSituation = 78;
        public const int RegroupSituation = 79;
        public const int AggressionOnOthers = 80;
        public const int RedIsAvailable = 81;
        public const int TenOrMorePtSpread = 82;
        public const int RatingMaxedOut = 83;
        public const int MyTeamLosingByAtLeast = 84;
        public const int ChooseTeamTune = 85;
        public const int DromerNearMine = 86;
        public const int AttackOrDeployWeapon = 87;
        public const int DecideWithRating = 88;
        public const int NearObjectBy = 89;
        public const int SoundBank = 90;
        public const int HomeOrAwayTeam = 91;
        public const int WinningOrLosing = 92;
        public const int IsDromerKnockedDown = 93;
        public const int RandomEmptyArmory = 94;
        public const int RandomInactiveWeapon = 95;
        public const int WhoKilledMe = 96;
        public const int AnimationSemaphore = 97;
        public const int ClockStoppedSemaphore = 98;
        public const int AttachedToSender = 99;
        public const int AttackedAnim = 100;
        public const int ClosestDromerToRok = 101;
        public const int OkToGetRok = 102;
        public const int BodyPartAnimation = 103;
        public const int RokInRange = 104;
        public const int IsDromerInjured = 105;
        public const int TeammateIsHumanOrInjured = 106;
        public const int DesiredSubstitute = 107;
        public const int SubstitutionSemaphore = 108;
        public const int OkToSwitchToTeammate = 109;
        public const int OkToSubstituteDromer = 110;
        public const int OkToPassToTeammate = 111;
        public const int OkToSubstituteTeammate = 112;
        public const int PickValidSubstitute = 113;
        public const int GetLaserHurdleCharge = 114;
        public const int DromerLaserHurdleCollision = 115;
        public const int OkToLaunchRok = 116;
        public const int GetImmunity = 117;
        public const int Interception = 118;
        public const int SpecialAttacks = 119;
        public const int InRangeToAttack = 120;
        public const int JostledDropRok = 121;
        public const int BouncedInGoal = 122;
        public const int CheckPassingDistance = 123;
        public const int WhichOppnToAttack = 124;
        public const int CheckRenderFlag = 125;
        public const int TeamIsDead = 126;
        public const int TeamIsInjured = 127;
        public const int MyTeamIsInjured = 128;
        public const int WhoWonTheGame = 129;
        public const int WhoHasRok = 130;
        public const int TryForKillball = 131;
        public const int TargetAcquired = 132;
        public const int DecideGoalieLaunch = 133;
        public const int NearestSuspenseMult = 134;
        public const int BallGotBehindMe = 135;
        public const int GoaliePassingTarget = 136;
        public const int ForceRearView = 137;
        public const int CheckScoreOnOwnGoal = 138;
    }
    // Behavior_IDs and BehaviorSetParamFnIDs (src/planvm/behaviorids.h).
    public static class BehaviorIds
    {
        public const int GlideToObject = 0;
        public const int Glide = 1;
        public const int RailSlide = 2;
        public const int Fly = 3;
        public const int MonitorKeyboard = 4;
        public const int UpdateWhileAttached = 5;
        public const int SetInPlace = 6;
        public const int OpenAndClose = 7;
        public const int Tracking = 8;
        public const int Watching = 9;
        public const int TrackTrajectory = 10;
        public const int GlideToPosition = 11;
        public const int FlyToward = 12;
        public const int TrackPassPrep = 13;
        public const int Avoidance = 14;
        public const int UpdateRelativePos = 15;
        public const int SetFlatInPlace = 16;
        public const int DoNothing = 17;
        public const int SetMovingInPlace = 18;
        public const int GlideBetweenFacing = 19;
        public const int HeadTracking = 20;
        public const int CoachTeam = 21;
        public const int CandyTracking = 22;
        public const int GlideLcl = 23;
        public const int FlyLcl = 24;
    }
    public static class BehaviorSetParamIds
    {
        public const int ModsTweenNFacing = 0;
        public const int KeyMonitorMode = 1;
        public const int InteractorLatency = 2;
        public const int Target = 3;
        public const int TargetPosition = 4;
        public const int RelativePosition = 5;
        public const int ModelImRelativeTo = 6;
        public const int ObjectToAvoid = 7;
        public const int ModelImFacing = 8;
        public const int PersistentAnimation = 9;
        public const int TargetGoal = 10;
        public const int Animation = 11;
        public const int AnimationEvent = 12;
        public const int ObjectToFollow = 13;
        public const int DoubleScore = 14;
        public const int Digressive = 15;
        public const int PurgeFlag = 16;
    }
    public static class InteractorParamIds
    {
        public const int SetInteractorName = 0;
        public const int SetActiveState = 1;
    }
    // ActionFunctionIDs names, from planfunctionids.h. Ids 2, 14, 15, 44 and 121 have no handler and no source name.
    public static readonly System.Collections.Generic.Dictionary<int, string> ActionNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "SetAttachPak" },
        { 1, "UnsetAttachPak" },
        { 3, "Launch" },
        { 4, "LaunchToward" },
        { 5, "SendMessage" },
        { 6, "SetSpeedBurst" },
        { 7, "PlaySound" },
        { 8, "NoteStats4Possessors" },
        { 9, "IncrementStat" },
        { 10, "StartStatTimer" },
        { 11, "EndStatTimer" },
        { 12, "PlaySndType" },
        { 13, "PanSndType" },
        { 16, "AnimationPeek" },
        { 17, "EngageOpponent" },
        { 18, "SetLocation" },
        { 19, "SetVelocity" },
        { 20, "SetObjectToTrack" },
        { 21, "SetTrackToViewOwner" },
        { 22, "SetTrackingOffset" },
        { 23, "IncrementTrackingOffset" },
        { 24, "IncrementObjectToTrack" },
        { 25, "SetObjectToWatch" },
        { 26, "IncrementObjectToWatch" },
        { 27, "SetInDrag" },
        { 28, "SetOnRim" },
        { 29, "SetFriction" },
        { 30, "SetAnimation" },
        { 31, "RestorePersistentAnimation" },
        { 32, "SetAnimationEvent" },
        { 33, "SetAnimationState" },
        { 34, "HookDragBungeesOn" },
        { 35, "ResetAccTransient" },
        { 36, "SetAnimationDoneMessage" },
        { 37, "LaunchAt" },
        { 38, "Throw" },
        { 39, "SetAnimationSpeed" },
        { 40, "IncrAnimationSpeed" },
        { 41, "ResetCameraBufferSize" },
        { 42, "ThrowBungee" },
        { 43, "ResetKeystateFlags" },
        { 45, "SetCamOffsetToLocation" },
        { 46, "LaunchGunDirection" },
        { 47, "RelaxYawToFace" },
        { 48, "ResetReticle" },
        { 49, "SetHeadCamText" },
        { 50, "InitRearViewPos" },
        { 51, "DopplerSound" },
        { 52, "IncrementBungeeLength" },
        { 53, "SetOffsetValues" },
        { 54, "SetBungeeLength" },
        { 55, "PlaceSound" },
        { 56, "IncrementCameraDir" },
        { 57, "SetTrackHeight" },
        { 58, "SetMinHeight" },
        { 59, "SetLookHeight" },
        { 60, "SetCamBufferFactor" },
        { 61, "SetScanModes" },
        { 62, "SetSolarWindFactor" },
        { 63, "SetPaletteEffect" },
        { 64, "SignalKeystate" },
        { 65, "SetMyGravity" },
        { 66, "RecoilOffWall" },
        { 67, "ClothesLineRecoil" },
        { 68, "ManOnManRecoil" },
        { 69, "GetKeystateFlags" },
        { 70, "SetLocalReal" },
        { 71, "ScaleBubToRating" },
        { 72, "SetRenderFlag" },
        { 73, "IncRokValue" },
        { 74, "SetRokKillState" },
        { 75, "ModifyDamage" },
        { 76, "ReflectOffWall" },
        { 77, "ModifyPitch" },
        { 78, "ReflectOffModel" },
        { 79, "SetTargetWithData" },
        { 80, "BungeeTowardTarget" },
        { 81, "GlideOnRamp" },
        { 82, "GrayscaleRandomizer" },
        { 83, "SetImmunity" },
        { 84, "SpeedBurstSituation" },
        { 85, "IncrAnimSpeedWRating" },
        { 86, "ModifyOpposTeamFriction" },
        { 87, "SetDeviceOwner" },
        { 88, "SetInputMode" },
        { 89, "IncrementScore" },
        { 90, "SwapRandomizers" },
        { 91, "ModifyRating" },
        { 92, "ModifyAvailableReds" },
        { 93, "ReallySendMsg" },
        { 94, "ExplodeFlat" },
        { 95, "SetShotPowerMeter" },
        { 96, "HoverAboveArmory" },
        { 97, "DeployWeapon" },
        { 98, "SetAttackee" },
        { 99, "ModifySpeedRating" },
        { 100, "ExplodeAgainstVelocity" },
        { 101, "ExplodeWithVelocity" },
        { 102, "ExplodeUniform" },
        { 103, "SetKnockedDownState" },
        { 104, "SpinTurnstile" },
        { 105, "LoseHead" },
        { 106, "Trace" },
        { 107, "RegainHead" },
        { 108, "SetKilledDromer" },
        { 109, "AnimationSemaphore" },
        { 110, "ResetCameraTrack" },
        { 111, "ClockStoppedSemaphore" },
        { 112, "SetInfoBarText" },
        { 113, "BounceOffDromer" },
        { 114, "BodySplit" },
        { 115, "CreditDamage" },
        { 116, "TrackTurnstile" },
        { 117, "DesiredSubstitute" },
        { 118, "SubstituteDromer" },
        { 119, "SetInfoTextDromer" },
        { 120, "SetLaserHurdleCharge" },
        { 122, "SetInfoTextTeam" },
        { 123, "ClearBehaviorAnimation" },
        { 124, "SendMessageToMyCamera" },
        { 125, "IfNotRokTrackMe" },
        { 126, "TransferViewOwnership" },
        { 127, "SmallBloodSpurt" },
        { 128, "LargeBloodSpurt" },
        { 129, "SetMaxSpeed" },
        { 130, "EndPeriodNow" },
        { 131, "KillBallStaging" },
        { 132, "KillBallExecute" },
        { 133, "KickOffRim" },
        { 134, "FlyingSemaphore" },
        { 135, "KickDownToSurface" },
        { 136, "SyncPossession" },
        { 137, "SyncSystem" },
        { 138, "SyncDamage" },
        { 139, "SendLaunchMessage" },
    };
    // DecisionFunctionIDs names, from planfunctionids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> DecisionNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "DecideAttachment" },
        { 1, "DecideCelebration" },
        { 2, "DecidePassOrRun" },
        { 3, "DecideHPSpinRailOrNot" },
        { 4, "PickACell" },
        { 5, "Random" },
        { 6, "IsSenderTeammate" },
        { 7, "AmMainDromer" },
        { 8, "RunTimeData" },
        { 9, "IsRokInMyZone" },
        { 10, "AmViewOwner" },
        { 11, "TeammateIsViewOwner" },
        { 12, "OwnerIsViewOwner" },
        { 13, "ViewOwnersMateInFront" },
        { 14, "IHaveRok" },
        { 15, "DecidePosition" },
        { 16, "CollisionDetail" },
        { 17, "EngageWhichOpponent" },
        { 18, "TeammateHasRok" },
        { 19, "GagTheAnnouncer" },
        { 20, "VictimCamIsOn" },
        { 21, "DecideBlockOrGoOut" },
        { 22, "WhichOppnIsClosestToRok" },
        { 23, "ClosestUncoveredOppn" },
        { 24, "AnalyzePadA" },
        { 25, "AnalyzePadB" },
        { 26, "AnalyzePadC" },
        { 27, "AttackerIsInRear" },
        { 28, "SpeedBurstIsRelaxed" },
        { 29, "OffenseOrWRok" },
        { 30, "HostIsViewOwner" },
        { 31, "RokInWhichTeamsPoss" },
        { 32, "IsDoubleScore" },
        { 33, "MyID" },
        { 34, "MessageData" },
        { 35, "BungeeVictimLorR" },
        { 36, "IfInFaceOff" },
        { 37, "ShotPower" },
        { 38, "IsScanningModeOn" },
        { 39, "MessageDataNibl0" },
        { 40, "MessageDataNibl1" },
        { 41, "MessageDataNibl4" },
        { 42, "MsgData4Nibl1X2Nibl0" },
        { 43, "SideKeysAreDown" },
        { 44, "SpinUp" },
        { 45, "AmIKnockedLoose" },
        { 46, "CatchBlockOrMiss" },
        { 47, "RokScoreValue" },
        { 48, "Jumping" },
        { 49, "ChoosePassingPlay" },
        { 50, "RecvrInRangeNView" },
        { 51, "WhichOppnIsClosestToMe" },
        { 52, "TakeDamage" },
        { 53, "Ducking" },
        { 54, "RokIsCharged" },
        { 55, "RokIsChargedAgainst" },
        { 56, "ClosestTargetToProjectile" },
        { 57, "MeOrTeammateClosestToRok" },
        { 58, "IsAmbient" },
        { 59, "DromerHurdleCollision" },
        { 60, "DromerHasRok" },
        { 61, "RokHurdleCollision" },
        { 62, "DromerMultiplierCollision" },
        { 63, "DromerLimboCollision" },
        { 64, "DromerRandomizerCollision" },
        { 65, "RokValueInMyFavor" },
        { 66, "DromerRampCollision" },
        { 67, "PassOrRunByRatings" },
        { 68, "LooseRokWhatToDo" },
        { 69, "GoThroughMuliplier" },
        { 70, "PickAttackMove" },
        { 71, "OffWingJob" },
        { 72, "PickFlyingMove" },
        { 73, "PickHalfPipeMove" },
        { 74, "AvoidSpinningTurnstile" },
        { 75, "TargetTurnstile" },
        { 76, "UseKillball" },
        { 77, "UseRandomizer" },
        { 78, "ShootSituation" },
        { 79, "RegroupSituation" },
        { 80, "AggressionOnOthers" },
        { 81, "RedIsAvailable" },
        { 82, "TenOrMorePtSpread" },
        { 83, "RatingMaxedOut" },
        { 84, "MyTeamLosingByAtLeast" },
        { 85, "ChooseTeamTune" },
        { 86, "DromerNearMine" },
        { 87, "AttackOrDeployWeapon" },
        { 88, "DecideWithRating" },
        { 89, "NearObjectBy" },
        { 90, "SoundBank" },
        { 91, "HomeOrAwayTeam" },
        { 92, "WinningOrLosing" },
        { 93, "IsDromerKnockedDown" },
        { 94, "RandomEmptyArmory" },
        { 95, "RandomInactiveWeapon" },
        { 96, "WhoKilledMe" },
        { 97, "AnimationSemaphore" },
        { 98, "ClockStoppedSemaphore" },
        { 99, "AttachedToSender" },
        { 100, "AttackedAnim" },
        { 101, "ClosestDromerToRok" },
        { 102, "OkToGetRok" },
        { 103, "BodyPartAnimation" },
        { 104, "RokInRange" },
        { 105, "IsDromerInjured" },
        { 106, "TeammateIsHumanOrInjured" },
        { 107, "DesiredSubstitute" },
        { 108, "SubstitutionSemaphore" },
        { 109, "OkToSwitchToTeammate" },
        { 110, "OkToSubstituteDromer" },
        { 111, "OkToPassToTeammate" },
        { 112, "OkToSubstituteTeammate" },
        { 113, "PickValidSubstitute" },
        { 114, "GetLaserHurdleCharge" },
        { 115, "DromerLaserHurdleCollision" },
        { 116, "OkToLaunchRok" },
        { 117, "GetImmunity" },
        { 118, "Interception" },
        { 119, "SpecialAttacks" },
        { 120, "InRangeToAttack" },
        { 121, "JostledDropRok" },
        { 122, "BouncedInGoal" },
        { 123, "CheckPassingDistance" },
        { 124, "WhichOppnToAttack" },
        { 125, "CheckRenderFlag" },
        { 126, "TeamIsDead" },
        { 127, "TeamIsInjured" },
        { 128, "MyTeamIsInjured" },
        { 129, "WhoWonTheGame" },
        { 130, "WhoHasRok" },
        { 131, "TryForKillball" },
        { 132, "TargetAcquired" },
        { 133, "DecideGoalieLaunch" },
        { 134, "NearestSuspenseMult" },
        { 135, "BallGotBehindMe" },
        { 136, "GoaliePassingTarget" },
        { 137, "ForceRearView" },
        { 138, "CheckScoreOnOwnGoal" },
    };
    // Behavior_IDs names, from behaviorids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> BehaviorNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "GlideToObject" },
        { 1, "Glide" },
        { 2, "RailSlide" },
        { 3, "Fly" },
        { 4, "MonitorKeyboard" },
        { 5, "UpdateWhileAttached" },
        { 6, "SetInPlace" },
        { 7, "OpenAndClose" },
        { 8, "Tracking" },
        { 9, "Watching" },
        { 10, "TrackTrajectory" },
        { 11, "GlideToPosition" },
        { 12, "FlyToward" },
        { 13, "TrackPassPrep" },
        { 14, "Avoidance" },
        { 15, "UpdateRelativePos" },
        { 16, "SetFlatInPlace" },
        { 17, "DoNothing" },
        { 18, "SetMovingInPlace" },
        { 19, "GlideBetweenFacing" },
        { 20, "HeadTracking" },
        { 21, "CoachTeam" },
        { 22, "CandyTracking" },
        { 23, "GlideLcl" },
        { 24, "FlyLcl" },
    };
    // BehaviorSetParamFnIDs names, from behaviorids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> BehaviorSetParamNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "ModsTweenNFacing" },
        { 1, "KeyMonitorMode" },
        { 2, "InteractorLatency" },
        { 3, "Target" },
        { 4, "TargetPosition" },
        { 5, "RelativePosition" },
        { 6, "ModelImRelativeTo" },
        { 7, "ObjectToAvoid" },
        { 8, "ModelImFacing" },
        { 9, "PersistentAnimation" },
        { 10, "TargetGoal" },
        { 11, "Animation" },
        { 12, "AnimationEvent" },
        { 13, "ObjectToFollow" },
        { 14, "DoubleScore" },
        { 15, "Digressive" },
        { 16, "PurgeFlag" },
    };
    // InteractorSetParamFnIDs: the only two slots the opcode table fills (planvm.cpp InitRuntimeOpcodeTable).
    public static readonly System.Collections.Generic.Dictionary<int, string> InteractorParamNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "SetInteractorName" },
        { 1, "SetActiveState" },
    };
    // AgendaItemIDs names, from agendaitemids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> AgendaItemNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "CBeAttached" },
        { 1, "CSteerToTarget" },
        { 2, "CSetPositionRelative" },
        { 3, "CSteerToward" },
        { 4, "CAdjustAcceleration" },
        { 5, "CSteerToPosition" },
        { 6, "CSteerBetweenFacing" },
        { 7, "CBungeeToward" },
        { 8, "CAvoidObjects" },
        { 9, "CMonitorKeys" },
        { 10, "CSpeedBurst" },
        { 11, "CGlideOnSurface" },
        { 12, "CSlideOnRail" },
        { 13, "CProjectAndBounce" },
        { 14, "CStepAnimation" },
        { 15, "CRelaxToPosition" },
        { 16, "CSetAttitude" },
        { 17, "CRelaxYaw" },
        { 18, "CTimerActivity" },
        { 19, "CTrack" },
        { 20, "CTrackTraj" },
        { 21, "CWatch" },
        { 22, "CTrackPasser_Receiver" },
        { 23, "CViewIt" },
        { 24, "CPanSound" },
        { 25, "CHeadTrack" },
        { 26, "CDopplerSnd" },
        { 27, "CPaletteEffect" },
        { 28, "CDisplayShotPower" },
        { 29, "CDisplayPassBar" },
        { 30, "CMonitorHeldKeys" },
        { 31, "CUpdateShotPower" },
        { 32, "CExplosionAnimation" },
        { 33, "CCheckAI" },
        { 34, "CCircleTrack" },
    };
    // Scene message ids, from src/engine/messageids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> MessageNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "Shoot" },
        { 2, "LostIt" },
        { 3, "GotIt" },
        { 7, "GoalScored" },
        { 8, "Jump" },
        { 9, "Fly" },
        { 10, "Landed" },
        { 11, "Attach" },
        { 12, "Hit" },
        { 15, "YerAttacked" },
        { 16, "YerReleased" },
        { 17, "YerHit" },
        { 21, "PassReceived" },
        { 22, "Deke" },
        { 23, "SpunDown" },
        { 25, "Attack" },
        { 28, "SetAnimation" },
        { 30, "MoveEyeTrack" },
        { 34, "ThrowWindUp" },
        { 40, "CellAnimationDone" },
        { 44, "GotThere" },
        { 47, "KillBallChange" },
        { 53, "EndPassMode" },
        { 54, "Blocked" },
        { 55, "Breakaway" },
        { 57, "ExitBehavior" },
        { 58, "Bounce" },
        { 59, "DeployWeapon" },
        { 64, "NoInputForSec" },
        { 67, "NewInput" },
        { 68, "GoalReceived" },
        { 69, "TimeOut" },
        { 70, "SpeedBurst" },
        { 72, "DragOver" },
        { 73, "DoubleScore" },
        { 76, "Initiate" },
        { 77, "Test" },
        { 79, "HeadCamOff" },
        { 80, "ShotOnGoal" },
        { 84, "PlayCD" },
        { 87, "SubstituteDromer" },
        { 91, "StartDrag" },
        { 94, "GlideToStop" },
        { 97, "SyncFrame" },
        { 102, "WatchOutMultiplier" },
        { 110, "WatchOutArmory" },
        { 112, "ReallySetAnimation" },
        { 117, "TeammateCollision" },
        { 118, "OpponentCollision" },
        { 124, "Extra1" },
        { 129, "Extra6" },
        { 130, "Extra7" },
        { 135, "EndOfPeriod" },
        { 136, "EndOfGame" },
        { 139, "SyncPossession" },
        { 140, "SyncSystem" },
        { 141, "SyncDamage" },
    };
    // Pronoun names, from planvm/plannode.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> PronounNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "ME" },
        { 1, "MY_TEAMMATE" },
        { 2, "MY_LEAD_OPPONENT" },
        { 3, "MY_OTHER_OPPONENT" },
        { 4, "MY_GOAL" },
        { 5, "MY_GOAL_SIDE" },
        { 6, "MY_GOAL_ZONE" },
        { 7, "OPPONENTS_GOAL" },
        { 8, "OPPN_GOAL_SIDE" },
        { 9, "OPPN_GOAL_ZONE" },
        { 10, "MY_COLLISION_BUB" },
        { 11, "MY_LEAD_OPPN_COLL_BUB" },
        { 12, "MY_OTHER_OPPN_COLL_BUB" },
        { 13, "MY_BACK_DEF_WG_POS" },
        { 14, "MY_GOAL_POS" },
        { 15, "VIEW_OWNER" },
        { 16, "VIEW_OWNERS_GOAL" },
        { 17, "VIEW_OWNERS_TEAMMATE" },
        { 18, "ROK_CARRIER" },
        { 19, "ROK_CARRIER_COB" },
        { 20, "MY_DRAG_START_POS" },
        { 21, "LAST_ROK_CARRIER" },
        { 22, "PREV_ROK_CARRIER" },
        { 23, "MY_AWARENESS_BUB" },
        { 24, "NON_ROK_CARRYING_OPPN" },
        { 25, "VIEW_ONRS_OPPN1" },
        { 26, "VIEW_ONRS_OPPN2" },
        { 27, "VIEW_OWNER_COB" },
        { 28, "VIEW_ONRS_OPPN1_COB" },
        { 29, "VIEW_ONRS_OPPN2_COB" },
        { 30, "WATCHED_OBJECT" },
        { 31, "VW_OWNR_OPPN_GOALIE" },
        { 32, "MY_BODY_BUBBLE" },
        { 33, "LEAD_OPPN_BODY_BUB" },
        { 34, "OTHER_OPPN_BODY_BUB" },
        { 35, "MY_GOALIE" },
        { 36, "MY_LEAD_TEAMMATE" },
        { 37, "TRACKED_OBJECT" },
        { 38, "TRACKED_OBJS_TEAMMATE" },
        { 39, "VW_OWNR_AWARNS_BUB" },
        { 40, "VW_OWNRS_OPPN_GOAL" },
        { 41, "THIS_MULTIPLIER_COB" },
        { 42, "THIS_TURNSTILE_COB" },
        { 43, "THIS_LIMBO_POLE_COB" },
        { 44, "THIS_LASER_HURDLE_COB" },
        { 45, "THIS_FAST_LANE_COB" },
        { 46, "THIS_EXPLOSION_COB" },
        { 47, "THIS_HURDLE_COB" },
        { 48, "THIS_GREEN_RANDOMIZER_COB" },
        { 49, "THIS_RED_RANDOMIZER_COB" },
        { 50, "THIS_RAMP_COB" },
        { 51, "MY_TEAMMATES_COB" },
        { 52, "THIS_ARMORY_COB" },
        { 53, "THIS_SLOW_LANE_COB" },
        { 54, "THIS_MINE_COB" },
        { 55, "THIS_SWARF_COB" },
        { 56, "THIS_BODY_PART_COB" },
    };
    // Collision object ids, from engine/collisionobjectids.h.
    public static readonly System.Collections.Generic.Dictionary<int, string> CollisionObjectNames = new System.Collections.Generic.Dictionary<int, string>
    {
        { 0, "Dromer0" },
        { 1, "BatonEzone0" },
        { 2, "DromerBody0" },
        { 3, "Dromer1" },
        { 4, "BatonEzone1" },
        { 5, "DromerBody1" },
        { 6, "Dromer2" },
        { 7, "BatonEzone2" },
        { 8, "DromerBody2" },
        { 9, "Dromer3" },
        { 10, "BatonEzone3" },
        { 11, "DromerBody3" },
        { 12, "Dromer4" },
        { 13, "BatonEzone4" },
        { 14, "DromerBody4" },
        { 15, "Dromer5" },
        { 16, "BatonEzone5" },
        { 17, "DromerBody5" },
        { 18, "NgoalZone" },
        { 19, "NgoalCase" },
        { 20, "NgoalPort" },
        { 21, "NgoalSide" },
        { 22, "SgoalZone" },
        { 23, "SgoalCase" },
        { 24, "SgoalPort" },
        { 25, "SgoalSide" },
        { 27, "Rok" },
        { 28, "Camera" },
        { 29, "Ramp" },
        { 41, "Dr0Awareness" },
        { 42, "Dr1Awareness" },
        { 43, "Dr2Awareness" },
        { 44, "Dr3Awareness" },
        { 45, "Dr4Awareness" },
        { 46, "Dr5Awareness" },
        { 47, "StartGun" },
        { 48, "HeadCam" },
        { 49, "Multiplier" },
        { 55, "Turnstile" },
        { 62, "LimboPole" },
        { 68, "LaserHurdle" },
        { 74, "Armory" },
        { 76, "Mine" },
        { 77, "Swarf" },
        { 78, "FastLane" },
        { 80, "SlowLane" },
        { 82, "Explosion" },
        { 88, "Hurdle" },
        { 95, "GreenRandomizer" },
        { 103, "RedRandomizer" },
        { 109, "BodyPart0" },
        { 117, "None" },
    };
    }
}
