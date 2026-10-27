// UBlade3 - exact HyperBlade (1996) simulation core.
// Player state and steering. ObjectStats: src/engine/objectstats.cpp (0x004748e0 InitDefaults,
// 0x00474930 GetWeightedStat, 0x00474950 GetScalar, 0x00474960 ScaleTo, 0x00474b30
// CopyBaseStats, 0x00474bb0 UpdateMomentumAndEnergy). Player input: src/engine/player.cpp
// ProcessTrickInput (0x004110f0), src/planvm/cmonitorkeys.cpp (0x00410e90), src/physics/
// physicsbody.cpp UpdateMovementAxes/ApplySteeringForce, src/planvm/cglideonsurface.cpp
// UpdateTerrainPhysics (0x0040c270), src/engine/cspeedburst.cpp Process (0x004113f0).
// Jump, slash and the full plan side of carrying live in the ADLIB plan VM; this file exposes
// their exact hook points and state fields, never invented timings.

using System;

namespace UBlade3.Sim
{
    // src/engine/statids.h RATING_* are on ObjectConstants; ObjectStats keeps rating n at n + 1.
    public sealed class PlayerStats
    {
        public const int StatsCount = 0x3f;

        public readonly float[] Stats = new float[64];          // m_aStats, 0x000
        public readonly float[] Backup = new float[StatsCount]; // m_aBackup, 0x100
        public float SpeedRatio;                                // m_flSpeedRatio, 0x1fc
        public float Scale;                                     // m_flScale, 0x200

        // objectstats.cpp ObjectStats::InitDefaults (0x004748e0).
        public PlayerStats()
        {
            Stats[0] = GlideConstants.DefaultEnergy;

            for (int i = 1; i < 64; i++)
            {
                Stats[i] = GlideConstants.DefaultRating;
            }

            Stats[ObjectConstants.RatingSlotOffset + ObjectConstants.RatingSpeed] = GlideConstants.DefaultSpeedRating;
            Stats[ObjectConstants.RatingSlotOffset + ObjectConstants.RatingDamage] = GlideConstants.DefaultDamageRating;

            Scale = GlideConstants.BaseStatsSentinel;
            SpeedRatio = GlideConstants.BaseStatsSentinel;
        }

        public void SetStat(int slot, float value)
        {
            Stats[slot] = value;
        }

        public float GetScalar(int rating)
        {
            return Stats[rating + 1];
        }

        // objectstats.cpp GetWeightedStat (0x00474930): the energy term scales every weighted read.
        public float GetWeightedStat(int rating)
        {
            float energy = Stats[0];
            energy *= GlideConstants.StatWeightScale;
            energy += GlideConstants.StatWeightBias;
            return energy * Stats[rating + 1];
        }

        // objectstats.cpp ScaleTo (0x00474960): only eight of the 63 ratings are scaled, four cap
        // at twelve and four at fifteen. The tournament bracket position is a data-port value.
        public void ScaleTo(int slot, EngineClock clock, bool tournamentMode, float bracketPos, bool sessionActive)
        {
            if (slot > ObjectConstants.SlotDromer3)
            {
                return;
            }

            if (!sessionActive || (slot & 1) == 0 || Scale != GlideConstants.BaseStatsSentinel)
            {
                if (tournamentMode)
                {
                    Scale = (bracketPos - 3.0f) * 0.03f + Scale;
                    Scale = (float)(clock.Period - 1) * 0.05f + Scale;
                }
                else
                {
                    Scale = (float)clock.Period * 0.05f + Scale;
                }
            }

            if (Scale == GlideConstants.BaseStatsSentinel)
            {
                return;
            }

            for (int index = 0; index < StatsCount; index++)
            {
                switch (index)
                {
                    case ObjectConstants.RatingWeightClass:
                    case ObjectConstants.RatingStamina:
                    case ObjectConstants.RatingBalance:
                    case ObjectConstants.RatingRokHandling:
                    {
                        Backup[index] = Stats[index + 1];
                        float value = Stats[index + 1] * Scale;
                        if (12.0f <= value || 0.0f <= value)
                        {
                            if (12.0f < value)
                            {
                                value = 12.0f;
                            }
                        }
                        else
                        {
                            value = 0.0f;
                        }
                        Stats[index + 1] = value;
                        break;
                    }

                    case ObjectConstants.RatingChecking:
                    case ObjectConstants.RatingSpeed:
                    case ObjectConstants.RatingShotPower:
                    case ObjectConstants.RatingOffensiveAbility:
                    {
                        Backup[index] = Stats[index + 1];
                        float value = Stats[index + 1] * Scale;
                        if (15.0f <= value || 0.0f <= value)
                        {
                            if (15.0f < value)
                            {
                                value = 15.0f;
                            }
                        }
                        else
                        {
                            value = 0.0f;
                        }
                        Stats[index + 1] = value;
                        break;
                    }
                }
            }
        }

        // objectstats.cpp CopyBaseStats (0x00474b30).
        public void CopyBaseStats()
        {
            if (Scale != GlideConstants.BaseStatsSentinel)
            {
                for (int i = 0; i < StatsCount; i++)
                {
                    switch (i)
                    {
                        case ObjectConstants.RatingWeightClass:
                        case ObjectConstants.RatingStamina:
                        case ObjectConstants.RatingChecking:
                        case ObjectConstants.RatingBalance:
                        case ObjectConstants.RatingSpeed:
                        case ObjectConstants.RatingShotPower:
                        case ObjectConstants.RatingOffensiveAbility:
                        case ObjectConstants.RatingRokHandling:
                            Stats[i + 1] = Backup[i];
                            break;
                    }
                }
            }

            Scale = GlideConstants.BaseStatsSentinel;
        }

        // objectstats.cpp UpdateMomentumAndEnergy (0x00474bb0). energyDraining is a process-global
        // in the original (g_bEnergyDraining, one flag for every skater); the caller owns it.
        public void UpdateMomentumAndEnergy(EngineClock clock, bool energyOn, ref bool energyDraining)
        {
            float momentum = (GetScalar(ObjectConstants.RatingStamina) - 4.0f) * GlideConstants.StaminaMomentumScale
                             + GlideConstants.StaminaMomentumBias;
            momentum = momentum * clock.GetDelta() + GlideConstants.BaseStatsSentinel;
            momentum *= SpeedRatio;

            if (momentum > GlideConstants.BaseStatsSentinel)
            {
                momentum = GlideConstants.BaseStatsSentinel;
            }

            SpeedRatio = momentum;

            if (!energyOn)
            {
                return;
            }

            if (energyDraining)
            {
                if (Stats[0] < 30.0f)
                {
                    energyDraining = false;
                }
            }
            else if (Stats[0] > 100.0f)
            {
                energyDraining = true;
            }

            if (energyDraining)
            {
                float drain = Stats[0] - (GlideConstants.SpeedRateBase - GetScalar(ObjectConstants.RatingStamina) * GlideConstants.StaminaDrainScale) * clock.GetDelta();
                if (drain > 100.0f)
                {
                    drain = 100.0f;
                }
                else if (drain < 0.0f)
                {
                    drain = 0.0f;
                }
                Stats[0] = drain;
                return;
            }

            float accel = GlideConstants.SpeedRateBase + GlideConstants.HalfRokObject;
            float charged = (accel - GetScalar(ObjectConstants.RatingStamina) * GlideConstants.StaminaDrainScale) * clock.GetDelta() + Stats[0];
            if (charged > 100.0f)
            {
                charged = 100.0f;
            }
            else if (charged < 0.0f)
            {
                charged = 0.0f;
            }
            Stats[0] = charged;
        }
    }

    // src/input/keyboarddeviceobj.h INPUT_SLOT_COUNT and actionbindings.cpp: the ten binding keys
    // become the eight held-mask bits the physics reads (default bindings from
    // keyboarddeviceobj.cpp: Forward=Up, Stop=Down, RotateLeft=Left, RotateRight=Right,
    // Attack='S', SpeedBurst='D', Shoot=Space, Pass='C', QuickShot='F', Jump='E';
    // g_anKeyBindingOrder swaps entries four and five). The keyboard table has eight key slots, so
    // QuickShot and Jump cannot be produced by the keyboard device in the original (TODO U3-M3).
    public struct PlayerInput
    {
        public HeldBinding HeldMask;
        public HeldBinding PrevHeldMask;
    }

    // src/engine/playerobject.h PlayerObject's state the simulation slice needs.
    public sealed class PlayerState : ITrackedEntity
    {
        public int Slot;
        public EngineObjectKind Kind = EngineObjectKind.Dromer;
        public readonly PhysicsBodyState Body = new PhysicsBodyState();
        public readonly PlayerStats Stats = new PlayerStats();

        public PlayerInput Input;

        // PlayerObject fields: the goal this player shoots at (7 or 8); the keeper is three below.
        public short TargetGoalSlot;
        public short AttackeeSlot = ObjectConstants.SlotNone;
        public int KnockedDownState;
        public short KilledBySlot = ObjectConstants.SlotNone;
        public short AnimationSemaphore;
        public short ClockStoppedSemaphore;
        public short DesiredSubstitute = ObjectConstants.SlotNone;
        public short FlyingSemaphore;

        // The forward vector the friction >= 1 "drive by animation" branch reads from the local
        // transform's second column in physicsbody/cglideonsurface. The render/animation port owns
        // that transform; until it lands the caller supplies the same direction here.
        public Vec3 LocalForward = new Vec3(0.0f, 1.0f, 0.0f);

        // src/engine/objectstats.cpp RATING_DAMAGE is stored in Stats; this helper mirrors the
        // many GetScalar(RATING_DAMAGE) call sites.
        public float Damage { get { return Stats.GetScalar(ObjectConstants.RatingDamage); } }

        // The plan behaviour the object currently runs (planvm/behaviorids.h Behavior_IDs); the
        // plan VM port owns the value, the chase camera only reads it.
        public int PlanBehaviorKind;
        // The world position the chase camera aims at: bone 9's world translation in the original
        // (ctrack.cpp reads pTracked->m_pBones->m_world column 3). The animation port writes it;
        // until then the body position stands in, which docs/SIM-SLICE.md records as a limit.
        public Vec3 AimPosition;

        // src/engine/cspeedburst.cpp: the per-player burst agenda item.
        public readonly SpeedBurstState SpeedBurst = new SpeedBurstState();

        public PlayerState(int slot)
        {
            Slot = slot;
            Body.Slot = slot;
            Body.OwnerKind = EngineObjectKind.Dromer;
            SpeedBurst.OwnerSlot = slot;
        }

        public bool IsGoalie { get { return Slot == ObjectConstants.GoalieSlotA || Slot == ObjectConstants.GoalieSlotB; } }
        public bool IsCarrying(AttachPak pak) { return pak != null && pak.Slot == Slot; }

        // --- ITrackedEntity ---
        int ITrackedEntity.Slot { get { return Slot; } }
        PhysicsBodyState ITrackedEntity.Body { get { return Body; } }
        bool ITrackedEntity.IsPlayer { get { return true; } }
        int ITrackedEntity.PlanBehaviorKind { get { return PlanBehaviorKind; } }
        int ITrackedEntity.KnockedDownState { get { return KnockedDownState; } }
        bool ITrackedEntity.HasShootHeld { get { return (Body.HeldBindings & HeldBinding.Shoot) != 0; } }
        int ITrackedEntity.TargetGoalSlot { get { return TargetGoalSlot; } }
        Vec3 ITrackedEntity.AimPosition { get { return AimPosition; } }

        // The action state the plan VM and animation consume; derived only from the shipped fields.
        public PlayerActionState ActionState
        {
            get
            {
                if (KilledBySlot != ObjectConstants.SlotNone)
                {
                    return PlayerActionState.Dead;
                }
                if (KnockedDownState != 0)
                {
                    return PlayerActionState.KnockedDown;
                }
                if (FlyingSemaphore != 0)
                {
                    return PlayerActionState.Airborne;
                }
                if ((Input.HeldMask & HeldBinding.Attack) != 0)
                {
                    return PlayerActionState.Attacking;
                }
                if (SpeedBurst.Active)
                {
                    return PlayerActionState.SpeedBurst;
                }
                return PlayerActionState.Grounded;
            }
        }
    }

    // The action states the presentation/AI layer observes. Jump and slash transitions are driven
    // by plan actions and animation events (MSG_JUMP, MSG_ATTACK, MSG_SPUN_DOWN); this enum only
    // names the state the C++ fields express.
    public enum PlayerActionState
    {
        Grounded = 0,
        Airborne = 1,
        Attacking = 2,
        KnockedDown = 3,
        Dead = 4,
        SpeedBurst = 5,
    }

    // src/engine/cspeedburst.cpp CSpeedBurst (0x004112e0..0x004113f0).
    public sealed class SpeedBurstState
    {
        public int OwnerSlot;
        public int OpponentA = ObjectConstants.SlotNone;
        public int OpponentB = ObjectConstants.SlotNone;
        public float Target;
        public float Boost = SpeedBurstConstants.BoostStart;
        public bool Active;
        public bool Ready;

        // CSpeedBurst::Bind: the players either side of the owner in slot order, wrapping through four.
        public void Bind(int ownerSlot)
        {
            OwnerSlot = ownerSlot;
            OpponentA = (ownerSlot + 1) % 4;
            OpponentB = (ownerSlot + 3) % 4;
            Boost = SpeedBurstConstants.BoostStart;
            Target = 0.0f;
            Active = true;
            Ready = false;
        }

        // CSpeedBurst::Process (0x004113f0). carrierSlot is the Rok's carrier (-1 for none).
        public SpeedBurstResult Process(EngineClock clock, PlayerStats stats, int carrierSlot, ref float steerScale)
        {
            SpeedBurstResult result = new SpeedBurstResult();

            if (Ready)
            {
                Active = false;
                return result;
            }

            if (Boost > SpeedBurstConstants.BoostStart)
            {
                bool floored = false;
                float stamina = stats.SpeedRatio;
                float delta = clock.GetDelta();

                float rate;
                if (carrierSlot >= 0 && carrierSlot < ObjectConstants.PlayerObjectCount && carrierSlot == OwnerSlot)
                {
                    rate = delta * SpeedBurstConstants.CarrierRate;
                }
                else
                {
                    rate = delta * 1.0f;
                }

                stamina = (1.0f - rate) * stamina;

                if (stamina < SpeedBurstConstants.StaminaFloor)
                {
                    stamina = SpeedBurstConstants.StaminaFloor;
                    floored = true;
                }

                stats.SpeedRatio = stamina;

                if (Target > Boost)
                {
                    Boost = clock.GetDelta() + Boost;
                }
                else if (floored)
                {
                    float decay = 1.0f - delta * SpeedBurstConstants.DecayRate;
                    Target = 0.0f;
                    Boost = decay * Boost;

                    if (Boost < 1.0f + SpeedBurstConstants.BoostEpsilon)
                    {
                        Boost = 1.0f;
                        steerScale = Boost;
                        Ready = true;
                        Active = false;
                        result.Finished = true;
                        return result;
                    }
                }
            }

            steerScale = Boost;
            result.Boost = Boost;
            return result;
        }
    }

    public struct SpeedBurstResult
    {
        public float Boost;
        public bool Finished;
    }

    // src/engine/player.cpp ProcessTrickInput (0x004110f0) and src/planvm/cmonitorkeys.cpp:
    // the keyboard agenda item's per-frame work for a human-controlled skater.
    public static class PlayerSteering
    {
        // player.cpp process globals: g_nTrickSeqStep, g_nBoostGrabStep, g_nTrickHoldStamp.
        public const int TrickHoldMs = 100;
        public static int TrickSeqStep;
        public static int BoostGrabStep;
        public static int TrickHoldStamp = -1;

        // Player::ProcessTrickInput. postEvent is (message, param) and mirrors PostSceneEvent.
        public static void ProcessTrickInput(PlayerState player, EngineClock clock, Action<int, int> postEvent)
        {
            if (player.Body.Friction >= 1.0f)
            {
                player.Body.UpdateBanking(clock, null);
            }

            player.Body.UpdateMovementAxes(player.Stats, clock, player.Body.Tuning);

            HeldBinding input = player.Input.HeldMask;

            if ((input & HeldBinding.Forward) == 0)
            {
                if ((input & HeldBinding.Movement) != 0 || TrickSeqStep != 0 || player.Body.SteerScale > 1.0001f)
                {
                    int step = TrickSeqStep;

                    if ((input & HeldBinding.Stop) == 0 && (input & HeldBinding.Rotate) != 0)
                    {
                        if (step == 0)
                        {
                            TrickSeqStep = 1;
                            BoostGrabStep = 0;

                            if ((input & HeldBinding.RotateLeft) != 0)
                            {
                                if (postEvent != null) postEvent(MessageIds.NoInputForSec, 1);
                                return;
                            }

                            if (postEvent != null) postEvent(MessageIds.NoInputForSec, 2);
                            return;
                        }
                    }
                    else if (step == 0 && (input & HeldBinding.Rotate) != 0)
                    {
                        TrickSeqStep = 1;
                        BoostGrabStep = 0;

                        if ((input & HeldBinding.RotateLeft) != 0)
                        {
                            if (postEvent != null) postEvent(MessageIds.NoInputForSec, 3);
                            return;
                        }

                        if (postEvent != null) postEvent(MessageIds.NoInputForSec, 4);
                        return;
                    }

                    TrickSeqStep = (step + 1) % 8;
                    BoostGrabStep = 0;
                    return;
                }

                if (TrickHoldStamp < 0)
                {
                    TrickHoldStamp = (int)clock.TickMs;
                    return;
                }

                if (clock.TickMs - (uint)TrickHoldStamp > TrickHoldMs)
                {
                    TrickSeqStep = 1;
                    TrickHoldStamp = -1;
                    BoostGrabStep = 0;
                    if (postEvent != null) postEvent(MessageIds.NoInputForSec, 0);
                    return;
                }
            }
            else
            {
                int grab = BoostGrabStep;

                if (grab == 0)
                {
                    if (postEvent != null) postEvent(MessageIds.NewInput, 0);
                    BoostGrabStep = 1;
                    if (postEvent != null) postEvent(MessageIds.DragOver, 0);
                    return;
                }

                BoostGrabStep = (grab + 1) % 8;
                TrickSeqStep = 0;
            }
        }

        // The CMonitorKeys agenda item's own body (src/planvm/cmonitorkeys.cpp 0x00410e90), which
        // copies the keyboard device's held mask into the body and runs the trick input.
        public static void MonitorKeysTick(PlayerState player, EngineClock clock, HeldBinding deviceHeldMask,
            Action<int, int> postEvent, bool twoPlayers)
        {
            player.Input.HeldMask = deviceHeldMask;
            player.Body.HeldBindings = deviceHeldMask;

            ProcessTrickInput(player, clock, postEvent);

            player.Body.ApplySteeringForce(twoPlayers);

            player.Input.PrevHeldMask = player.Input.HeldMask;
            player.Body.PrevHeldBindings = player.Body.HeldBindings;
        }

        // The goalie-vs-skater keyboard clamp from CMonitorKeys::Process:
        // MON_GOALIE_CTRL is 1 and MON_DRIVE_YER_GUY is 0.
        public const int MonitorDriveYerGuy = 0;
        public const int MonitorGoalieCtrl = 1;
        public const int MonitorArcadeStyle = 2;
        public const float ArenaLimit = 6.0f;
        public const float ClampRate = 5.0f;

        public static void ApplyGoalieControl(PlayerState player, CollisionEllipsoid arena)
        {
            player.Body.VelDirLatched = player.Body.VelDir;

            if (player.Body.Pos.x < -ArenaLimit && player.Body.Axis.y > 0.0f)
            {
                player.Body.Axis.y = 0.0f;
            }

            if (player.Body.Pos.x > 6.0f && player.Body.Axis.y < 0.0f)
            {
                player.Body.Axis.y = 0.0f;
            }

            float edge = arena.GetRadiusY();

            if (edge - player.Body.Pos.y > 4.0f && player.Body.Axis.x > 0.0f)
            {
                player.Body.Axis.x = 0.0f;
            }

            edge = arena.GetRadiusY();

            if (edge - player.Body.Pos.y < -4.0f && player.Body.Axis.x < 0.0f)
            {
                player.Body.Axis.x = 0.0f;
            }

            player.Body.Pos.x -= player.Body.Axis.y * ClampRate;
            player.Body.Pos.y -= player.Body.Axis.x * ClampRate;
        }
    }

    // src/planvm/cglideonsurface.cpp CGlideOnSurface::UpdateTerrainPhysics (0x0040c270). The Rok's
    // speed page read is the one documented 20XX fix; the original read past the Rok object
    // (20XX comment: "The rok glides too, and has no stats; the original read past its object").
    public static class GlideOnSurface
    {
        public static bool UpdateTerrainPhysics(PlayerState owner, PhysicsBodyState body, EngineClock clock,
            CollisionEllipsoid arena, int carrierSlot, PhysicsTuning tuning,
            Action<int, int> postEvent, bool onSurface, bool useTwentyXxStatGuard)
        {
            Vec3 saved = body.SurfNormal;

            float frictionDouble = body.Friction * GlideConstants.FrictionDouble;

            body.PrevPos = body.Pos;
            body.VelDirLatched = body.VelDir;

            if (body.Friction >= 1.0f)
            {
                body.VelDir = owner.LocalForward;
                body.Vel = Vec3.ScaleTo(body.VelDir, 0.01f);

                body.SmoothVel = Vec3.Zero;
                body.BankForce = Vec3.Zero;

                if (onSurface)
                {
                    return onSurface;
                }
            }
            else
            {
                Vec3 target = body.Vel + body.PrevPos;
                body.Shell.RayIntersection(body.SurfNormal, target, out body.Pos);

                Vec3 diff = body.Pos.Minus(body.PrevPos);

                Vec3 step;
                step.x = diff.x + body.SmoothVel.x;
                step.y = diff.y + body.SmoothVel.y;
                step.z = diff.z + body.SmoothVel.z;

                step.x = step.x + body.BankForce.x;
                step.y = step.y + body.BankForce.y;
                step.z = step.z + body.BankForce.z;

                Vec3 outStep = step;

                body.Speed = (float)Math.Sqrt(step.z * step.z + step.y * step.y + step.x * step.x);

                if (clock.InvDelta * body.Speed > GlideConstants.GlideSpeedEpsilon)
                {
                    float length = body.Speed;
                    body.VelDir = new Vec3(
                        step.x * (1.0f / length),
                        step.y * (1.0f / length),
                        step.z * (1.0f / length));
                }
                else
                {
                    float length = (float)Math.Sqrt(body.VelDir.x * body.VelDir.x + body.VelDir.y * body.VelDir.y + body.VelDir.z * body.VelDir.z);

                    if (length == 0.0f)
                    {
                        length = (float)Math.Sqrt(body.VelDirLatched.x * body.VelDirLatched.x
                                                  + body.VelDirLatched.y * body.VelDirLatched.y
                                                  + body.VelDirLatched.z * body.VelDirLatched.z);

                        if (length != 0.0f)
                        {
                            body.VelDir = body.VelDirLatched;
                        }
                        else
                        {
                            Vec3 posDir;
                            if (MathOps.NormalizeTo(body.Pos, out posDir))
                            {
                                body.VelDir = posDir;
                            }
                        }
                    }
                }

                Vec3 normal;
                if (body.Shell.NormalAtPoint(body.Pos, out normal))
                {
                    body.SurfNormal = normal;
                }

                if (body.Pos.z > 0.0f && postEvent != null)
                {
                    postEvent(MessageIds.Fly, 0);
                }

                float bankScale = body.GravDeltaVel;

                Vec3 bank = new Vec3(
                    normal.z * bankScale * normal.x,
                    normal.y * bankScale,
                    (-(normal.x * normal.x) - normal.y * normal.y) * bankScale);
                bank.y *= normal.z;

                if (owner.Slot < ObjectConstants.PlayerObjectCount)
                {
                    float alt = bank.y * body.VelDir.y + bank.z * body.VelDir.z + bank.x * body.VelDir.x;

                    if (alt < 0.0f)
                    {
                        bank = Vec3.ScaleTo(body.VelDir, alt);
                    }
                    else
                    {
                        bank = Vec3.Zero;
                    }
                }

                frictionDouble = clock.GetDelta() * bankScale * normal.z * frictionDouble * GlideConstants.BankTermGain;

                int slot = owner.Slot;

                if (clock.GetInvDelta() * body.Speed < GlideConstants.BankFreeSpeed
                    && (slot <= ObjectConstants.GoalieSlotB || (slot >= 0x5e && slot <= 0x65)))
                {
                    if (body.HeldBindings == HeldBinding.None)
                    {
                        float glideDot = body.VelDir.z * body.VelDirLatched.z
                                         + body.VelDir.y * body.VelDirLatched.y
                                         + body.VelDirLatched.x * body.VelDir.x;

                        if (glideDot > 0.0f)
                        {
                            if (postEvent != null) postEvent(MessageIds.GlideToStop, 0);
                        }
                        else if (slot < 0x5e || slot > 0x65)
                        {
                            // CRelaxYaw (src/engine/crelaxyaw.cpp) is an agenda item allocated here.
                            // TODO(U3-M2): port CRelaxYaw; the post-frame yaw relax is what starts it.
                        }
                    }
                }
                else
                {
                    outStep.x += bank.x;
                    outStep.y += bank.y;
                    outStep.z += bank.z;
                }

                body.SmoothVel = Vec3.Zero;
                body.BankForce = Vec3.Zero;

                bool carryingRok = carrierSlot == slot;

                float altScale;
                if (body.Pos.z > 0.0f)
                {
                    altScale = 1.0f;
                }
                else
                {
                    altScale = body.SteerScale;
                }

                float targetSpeed = carryingRok ? GlideConstants.RokCarrierSpeedScale : 1.0f;

                PlayerStats stats = owner.Stats;
                float statScale;
                if (useTwentyXxStatGuard && owner.Kind != EngineObjectKind.Dromer)
                {
                    statScale = 1.0f;
                }
                else if (stats != null)
                {
                    statScale = stats.GetWeightedStat(ObjectConstants.RatingSpeed) * GlideConstants.SpeedStatScale
                                + GlideConstants.SpeedStatBias;
                }
                else
                {
                    statScale = 1.0f;
                }

                targetSpeed = body.ForwardSpeedDt * statScale * targetSpeed * altScale * tuning.MaxSpeed;

                if (body.Speed > targetSpeed)
                {
                    Vec3 normalized;
                    if (MathOps.NormalizeTo(outStep, out normalized))
                    {
                        body.VelDir = normalized;
                    }

                    body.Speed = targetSpeed;
                    targetSpeed -= frictionDouble;

                    outStep = Vec3.ScaleTo(body.VelDir, targetSpeed);
                }
                else if (frictionDouble <= body.Speed)
                {
                    Vec3 drag = Vec3.ScaleTo(body.VelDir, frictionDouble);
                    Vec3 next = outStep - drag;

                    if (next.z * body.Vel.z + next.y * body.Vel.y + next.x * body.Vel.x < 0.0f)
                    {
                        if (slot <= ObjectConstants.GoalieSlotB)
                        {
                            if (body.HeldBindings == HeldBinding.None)
                            {
                                if (postEvent != null) postEvent(MessageIds.GlideToStop, 0);
                                outStep = Vec3.Zero;
                            }
                        }
                    }
                    else
                    {
                        outStep = next;
                    }
                }

                body.Vel = outStep;

                Vec3 world = body.Pos; // GetWorldPos is the object transform's translation; owned by the render port.
                Vec3 arenaNormal;
                if (arena.NormalAtPoint(world, out arenaNormal))
                {
                    body.SurfNormal = arenaNormal;
                    body.Shell = arena;
                    saved = arenaNormal;
                }

                float dot = saved.z * body.VelDir.z + saved.y * body.VelDir.y + saved.x * body.VelDir.x;
                Vec3 proj = Vec3.ScaleTo(saved, dot);

                body.VelDir = body.VelDir - proj;
                Vec3 normalizedDir;
                if (MathOps.NormalizeTo(body.VelDir, out normalizedDir))
                {
                    body.VelDir = normalizedDir;
                }

                if (onSurface)
                {
                    return onSurface;
                }
            }

            body.Step = Vec3.ScaleTo(body.Vel, clock.GetInvDelta());
            return onSurface;
        }
    }

    // src/engine/object.cpp Object::CopyPhysicsStateTo (0x00402dc0): the substitution/body-split
    // jump. The receiving body always leaves upwards at 1.2x speed and raises MSG_JUMP.
    public static class PlayerJumpHooks
    {
        public const float CopyBoostScale = 1.2f;

        public static void ApplyJumpCopy(PlayerState destination, PhysicsBodyState source, Action<int, int> postEvent)
        {
            destination.Body.Pos = source.Pos;

            Vec3 velocity = Vec3.ScaleTo(source.Vel, CopyBoostScale);

            if (velocity.z < 0.0f)
            {
                velocity.z = -velocity.z;
            }

            // EngineObject::SetVelocity (0x004722a0): vel, prevPos = pos - vel, speed and the
            // normalised heading.
            destination.Body.Vel = velocity;
            destination.Body.PrevPos = new Vec3(
                destination.Body.Pos.x - velocity.x,
                destination.Body.Pos.y - velocity.y,
                destination.Body.Pos.z - velocity.z);
            destination.Body.Speed = (float)Math.Sqrt(Vec3.DotInline(velocity, velocity));
            if (destination.Body.Speed != 0.0f)
            {
                Vec3 normalized;
                if (MathOps.NormalizeTo(velocity, out normalized))
                {
                    destination.Body.VelDir = normalized;
                }
            }
            destination.Body.Facing = 0.0f;

            if (postEvent != null)
            {
                postEvent(MessageIds.Jump, 0);
            }
        }
    }
}
