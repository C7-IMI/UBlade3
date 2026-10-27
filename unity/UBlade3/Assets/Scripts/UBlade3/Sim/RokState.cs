// UBlade3 - exact HyperBlade (1996) simulation core.
// The Rok: src/engine/rokobject.h (0xd30 bytes) and src/engine/rokobject.cpp RokObject
// 0x00473a80, SetRenderSlotFlags 0x00473b90, ActivateAtSpawnSlot 0x00473c50.
// Carry attachment: src/planvm/planactionanim.cpp Set_AttachPak (0x00401460),
// Unset_AttachPak (0x004015c0), src/engine/cbeattached.cpp CBeAttached (0x0040dc80/0x0040de50).
// Throwing: src/planvm/planactionlaunch.cpp Throw (0x00403a30), SendLaunchMessage (0x004031e0),
// Launch (0x00403290). The aimed Launch path is only a hook in this slice; see docs/SIM-SLICE.md.

using System;

namespace UBlade3.Sim
{
    // src/engine/rokobject.h RokObject.
    public sealed class RokState : ITrackedEntity
    {
        public int Slot = ObjectConstants.SlotRok;
        public readonly PhysicsBodyState Body = new PhysicsBodyState();

        public int CarrierSlot = RokConstants.InitialCarrierSlot;        // 0xd14
        public int PrevCarrierSlot = RokConstants.InitialPrevCarrierSlot; // 0xd18
        public int OddCarrierSlot = RokConstants.InitialOddCarrierSlot;   // 0xd1c
        public int OddPrevCarrierSlot = RokConstants.InitialOddPrevCarrierSlot; // 0xd20
        public short RokValue;        // 0xd24
        public int KillState = RokConstants.InitialKillState; // 0xd28
        public bool AtSpawnSlot;      // 0xd2c

        public int RenderBone = -1;
        public int PlanBehaviorKind;

        // --- ITrackedEntity: the chase camera can track the Rok's body. ---
        int ITrackedEntity.Slot { get { return Slot; } }
        PhysicsBodyState ITrackedEntity.Body { get { return Body; } }
        bool ITrackedEntity.IsPlayer { get { return false; } }
        int ITrackedEntity.PlanBehaviorKind { get { return PlanBehaviorKind; } }
        int ITrackedEntity.KnockedDownState { get { return 0; } }
        bool ITrackedEntity.HasShootHeld { get { return false; } }
        int ITrackedEntity.TargetGoalSlot { get { return -1; } }
        Vec3 ITrackedEntity.AimPosition { get { return Body.Pos; } }

        public RokState()
        {
            Body.Slot = Slot;
            Body.OwnerKind = EngineObjectKind.Rok;
        }

        // rokobject.cpp RokObject::RokObject (0x00473a80): every mesh bone past the first hidden;
        // the simulation only records which bone is visible.
        public void ResetToSpawnState()
        {
            RokValue = 0;
            KillState = RokConstants.InitialKillState;
            CarrierSlot = RokConstants.InitialCarrierSlot;
            PrevCarrierSlot = RokConstants.InitialPrevCarrierSlot;
            OddCarrierSlot = RokConstants.InitialOddCarrierSlot;
            OddPrevCarrierSlot = RokConstants.InitialOddPrevCarrierSlot;
            AtSpawnSlot = false;
            RenderBone = 0;
        }

        // rokobject.cpp SetRenderSlotFlags (0x00473b90): every slot hidden, then one shown; an
        // uncovered multiplier leaves every bone hidden (RenderBone = -1).
        public void SetRenderSlotFlags()
        {
            RenderBone = MultiplierRules.RenderSlotBone(RokValue, AtSpawnSlot, KillState);
        }

        // rokobject.cpp ActivateAtSpawnSlot (0x00473c50). The definition swap is the data port's
        // job; this records the spawn state and re-runs the render flags.
        public void ActivateAtSpawnSlot(uint spawnSlot)
        {
            if (spawnSlot == RokConstants.SpawnSlotAny)
            {
                AtSpawnSlot = false;
                SetRenderSlotFlags();
                return;
            }

            AtSpawnSlot = true;
            SetRenderSlotFlags();
        }

        // planactionanim.cpp PlanAction::Set_AttachPak (0x00401460): the attach pak comes from the
        // bubble's own AttachPak; the carrier history and the two messages are all side effects.
        // Return the events the caller must broadcast.
        public void Attach(AttachPak pak, Action<int, int, int> postMessage)
        {
            if (AtSpawnSlot)
            {
                AtSpawnSlot = false;
                SetRenderSlotFlags();
            }

            if (pak == null)
            {
                Body.Attach = null;
                return;
            }

            int oldCarrier = Body.Attach != null ? Body.Attach.Slot : ObjectConstants.SlotNone;

            pak.Index = (short)pak.Slot;
            Body.Attach = pak;

            if (oldCarrier >= 0 && postMessage != null)
            {
                postMessage(oldCarrier, MessageIds.LostIt, 0);
            }

            if (postMessage != null)
            {
                postMessage(pak.Slot, MessageIds.GotIt, 0);
            }

            PrevCarrierSlot = CarrierSlot;

            if ((CarrierSlot & 1) != 0)
            {
                OddPrevCarrierSlot = CarrierSlot;
            }

            CarrierSlot = pak.Slot;

            if ((CarrierSlot & 1) != 0)
            {
                OddCarrierSlot = CarrierSlot;
            }

            if (postMessage != null)
            {
                postMessage(ObjectConstants.SlotSoundMan, MessageIds.Attach, 0);
            }
        }

        // planactionanim.cpp PlanAction::Unset_AttachPak (0x004015c0): only the pointer clears; the
        // carrier history and the render flags are left as they were.
        public void Detach()
        {
            Body.Attach = null;
        }

        public bool IsCarried { get { return Body.Attach != null; } }
        public int AttachedSlot { get { return Body.Attach != null ? Body.Attach.Slot : ObjectConstants.SlotNone; } }

        // planactionlaunch.cpp PlanAction::Throw (0x00403a30). Returns false when its four gates
        // fail: the actor must be the Rok, local phase, carrying, and the carrier must be the
        // viewed slot. Uses the carrier's shot-power rating.
        public bool Throw(EngineClock clock, PlayerStats carrierStats, float carrierShotPowerWeighted,
            Vec3 carrierVelDir, int carrierSlot, int viewedSlot)
        {
            if (Slot != ObjectConstants.SlotRok || Body.Attach == null)
            {
                return false;
            }

            if (Body.Attach.Slot != carrierSlot || carrierSlot != viewedSlot)
            {
                return false;
            }

            Vec3 dir = carrierVelDir;

            float speed = (carrierShotPowerWeighted * ThrowConstants.ThrowPowerScale + ThrowConstants.ThrowPowerBias)
                          * ThrowConstants.ThrowSpeedBase * clock.GetDelta();

            dir.z += ThrowConstants.ThrowRise;

            Vec3 normalized;
            if (MathOps.NormalizeTo(dir, out normalized))
            {
                dir = normalized;
            }

            Body.VelDir = dir;
            Body.Speed = speed;
            Body.Vel = Vec3.ScaleTo(dir, speed);
            return true;
        }

        // planactionlaunch.cpp PlanAction::SendLaunchMessage (0x004031e0): the human packs the helmet
        // camera's aim and shot power into three signed bytes and resets the meter to 50.
        public static uint PackShotWord(float viewFracX, int shotPowerLevel, float viewFracY, out int resetShotPowerLevel)
        {
            int x = (int)(viewFracX * CameraConstants.ShotFracScale);
            int y = (int)(viewFracY * CameraConstants.ShotFracScale);
            resetShotPowerLevel = CameraConstants.ShotPowerReset;
            return (uint)(((byte)(sbyte)x) | ((uint)(byte)(sbyte)shotPowerLevel << 8) | ((uint)(byte)(sbyte)y << 16));
        }

        public static int ShotWordAimX(uint shotWord) { return (sbyte)(shotWord & 0xff); }
        public static int ShotWordPower(uint shotWord) { return (sbyte)((shotWord >> 8) & 0xff); }
        public static int ShotWordAimY(uint shotWord) { return (sbyte)((shotWord >> 16) & 0xff); }
    }

    // The unaimed Launch branch (planactionlaunch.cpp 0x00403290, flRange < 0) and the non-Rok
    // bounce branch. The aimed branch (flRange >= 0) needs the plan's shot word and is TODO.
    public static class RokLaunch
    {
        public struct UnaimedLaunch
        {
            public Vec3 Dir;
            public float Speed;
            public bool ShootsOnGoal;
        }

        // Returns the velocity the plan's unaimed shot gives the Rok. carrierStats supplies the
        // offensive-ability rating; targetGoalSlot is SLOT_NGOAL or SLOT_SGOAL.
        public static UnaimedLaunch LaunchUnaimed(RokState rok, PlayerStats carrierStats, int targetGoalSlot,
            EngineClock clock, CollisionEllipsoid arena, bool sessionActive, float random01x, float random01z)
        {
            float delta = clock.GetDelta();

            float speedScale = (random01x * ThrowConstants.UnaimedRandScale + ThrowConstants.UnaimedRandScale)
                               * carrierStats.GetScalar(ObjectConstants.RatingOffensiveAbility)
                               * ThrowConstants.UnaimedAbilityScale
                               + ThrowConstants.UnaimedBias;
            speedScale *= delta;

            if (sessionActive)
            {
                speedScale *= ThrowConstants.UnaimedNetworkScale;
            }

            float jitterX = random01x - ThrowConstants.UnaimedRandScale;
            float jitterZ = random01z - ThrowConstants.UnaimedRandScale;

            Vec3 dir;
            dir.x = jitterX * ThrowConstants.UnaimedJitter - rok.Body.Pos.x;

            float side;
            if (targetGoalSlot == ObjectConstants.SlotSGoal)
            {
                side = -arena.GetRadiusY();
                dir.y = side - rok.Body.Pos.y;
            }
            else
            {
                side = arena.GetRadiusY();
                dir.y = side - rok.Body.Pos.y;
            }

            dir.z = jitterZ * ThrowConstants.UnaimedJitter - rok.Body.Pos.z - ThrowConstants.UnaimedDrop;

            float root = (float)Math.Sqrt(dir.x * dir.x + dir.y * dir.y + dir.z * dir.z);
            float flight = root / speedScale;

            float gravity = clock.DeltaSquared * ThrowConstants.UnaimedGravity;
            dir.z = gravity * flight * flight + dir.z;

            Vec3 normalized;
            if (MathOps.NormalizeTo(dir, out normalized))
            {
                dir = normalized;
            }

            rok.Body.Vel = Vec3.ScaleTo(dir, speedScale);

            return new UnaimedLaunch { Dir = dir, Speed = speedScale, ShootsOnGoal = true };
        }

        // planactionlaunch.cpp Launch's non-Rok branch: the projectile leaves with its existing
        // speed or half of it, plus ten units a second, raised along z, then scaled by the argument.
        public static void LaunchBounce(PhysicsBodyState body, EngineClock clock, float scale)
        {
            float speed = (float)Math.Sqrt(body.Vel.x * body.Vel.x + body.Vel.y * body.Vel.y + body.Vel.z * body.Vel.z);
            float bump = clock.GetDelta() * ThrowConstants.BounceBumpRate;

            if (bump <= speed)
            {
                bump = speed * ThrowConstants.BounceBumpScale;
            }

            body.Vel.z = bump + body.Vel.z;
            body.Vel = Vec3.ScaleTo(body.Vel, scale);
        }

        // planactionlaunch.cpp Launch's aimed branch (flRange >= 0). TODO(U3-M3): the full branch
        // needs the plan VM's shot word, the carrier's shot-power/offensive/long-shot ratings and
        // the goal plan side effects (MSG_SHOT_ON_GOAL). Its constants are transcribed in
        // ThrowConstants; this stub records the request instead of guessing the result.
        public static bool LaunchAimed(RokState rok, PlayerStats carrierStats, Vec3 carrierVelDir, Vec3 goalPos,
            int targetGoalSlot, float aimY, float aimX, EngineClock clock, CollisionEllipsoid arena, bool sessionActive)
        {
            return false;
        }
    }
}
