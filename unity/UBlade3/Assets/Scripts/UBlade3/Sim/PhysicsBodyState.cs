// UBlade3 - exact HyperBlade (1996) simulation core.
// PhysicsBodyState: src/physics/physicsbody.h (0x150-byte body) and src/physics/physicsbody.cpp
// BindToObject 0x00407780, ResetSpeeds 0x00407890, ResetDynamics 0x00407920, PreUpdateTick
// 0x00407960, UpdateMovementAxes 0x00407c40, ApplySteeringForce 0x00408080, InitOnSurface
// 0x00408190, IntegrateMotion 0x004082c0, LaunchDiagonal 0x00408540, IntegrateGravity
// 0x004085d0, StepWithGravity 0x004086a0, UpdateVelocityDirection 0x00408760,
// ApplyBankForce 0x00408880, UpdateBanking 0x00408940, ResetOnSurface 0x00407ab0,
// TakeSurfaceNormal 0x00407a60.
// The plan-driven pieces (ApplyAnimDrivenForce, SetFlatOnTerrain, movement-axis helpers owned by
// behaviours) are hook points here and are listed in docs/SIM-SLICE.md as TODO.

using System;

namespace UBlade3.Sim
{
    // physicsbody.h AttachPak: the Rok's and the mine's carry attachment.
    public sealed class AttachPak
    {
        public short Slot;       // nSlot, 0x00
        public short LinkId;     // nLinkId, 0x02
        public Vec3 Offset;      // vOffset, 0x04
        public int Unused;       // nUnused, 0x10
        public Transform4 Transform; // pTransform, 0x14
        public short Index;      // nIndex, 0x18
    }

    // physicsbody.h struct BodyMove.
    public struct BodyMove
    {
        public Vec3 Dir;    // 0x00
        public Vec3 Target; // 0x0c
    }

    // src/app/config.cpp globals the physics reads (defaults read from Data\DftKeybd.cfg; all one
    // in the shipped configuration unless the file overrides them).
    public sealed class PhysicsTuning
    {
        public float NetTracking = PhysicsConstants.DefaultNetTracking;
        public float Bandwidth = PhysicsConstants.DefaultBandwidth;
        public float ForceRelaxation = PhysicsConstants.DefaultForceRelaxation;
        public float MaxSpeed = PhysicsConstants.DefaultMaxSpeed;
        public float MaxAcceleration = PhysicsConstants.DefaultMaxAcceleration;
        public float SpeedBurstFactor = PhysicsConstants.DefaultSpeedBurstFactor;
        public float StopForceFactor = PhysicsConstants.DefaultStopForceFactor;
        public float ForceIncrement = PhysicsConstants.DefaultForceIncrement;

        public static readonly PhysicsTuning Shipped = new PhysicsTuning();
    }

    // physicsbody.h PhysicsBody. One instance per simulation object (players, Rok, cameras).
    public sealed class PhysicsBodyState
    {
        public EngineObjectKind OwnerKind = EngineObjectKind.Unknown;
        public int Slot = -1;

        // The fields, in the original record's order.
        public Vec3 SmoothVel;      // 0x014
        public Vec3 Vel;            // 0x020
        public Vec3 VelDir;         // 0x02c
        public Vec3 VelDirLatched;  // 0x038
        public Vec3 Pos;            // 0x044
        public Vec3 PrevPos;        // 0x050
        public AttachPak Attach;    // 0x05c, pAttach
        public CollisionEllipsoid Shell; // 0x060, pShell (the arena's CollisionShape in this slice)
        public Vec3 SurfNormal;     // 0x064
        public float GravDeltaVel;  // 0x070
        public Vec3 BankForce;      // 0x074
        public float Elasticity;    // 0x080
        public Vec3 Step;           // 0x084
        public readonly Vec3[] RingVelHistory = new Vec3[PhysicsConstants.VelocityRingLength]; // 0x090
        public readonly Vec3[] RingVelSmooth = new Vec3[PhysicsConstants.VelocityRingLength];  // 0x0c0
        public int RingIndex;       // 0x0f0
        public Vec3 NetTarget;      // 0x0f4
        public Vec3 Axis;           // 0x100
        public HeldBinding HeldBindings;     // 0x10c (short in the original)
        public HeldBinding PrevHeldBindings; // 0x10e (short in the original)
        public float GravAccel;     // 0x110
        public float Facing;        // 0x114
        public float SteerScale;    // 0x118
        public float AccelDt;       // 0x11c
        public float Accel;         // 0x120
        public float Friction;      // 0x124
        public float Speed;         // 0x128
        public bool NetTargetPending; // 0x12c
        public uint PrevTick;       // 0x130
        public BodyMove Move;       // 0x138

        // The four values BindToObject derives from the frame clock; kept for inspection.
        public float BaseForwardSpeed; // flBaseForwardSpeed, 0x010
        public float ForwardSpeedDt;   // flForwardSpeedDt, 0x00c
        public float ForwardAccel;     // 0x008

        // The engine's gravity delta (GameEngine::m_flGravityDt = 32 * deltaSquared).
        public float EngineGravityDt;

        public PhysicsTuning Tuning = PhysicsTuning.Shipped;

        public PhysicsBodyState()
        {
            ResetFields();
        }

        // physicsbody.cpp PhysicsBody::PhysicsBody (0x00407520): everything zero except the rings'
        // storage, which is also zero.
        private void ResetFields()
        {
            SmoothVel = Vec3.Zero;
            Vel = Vec3.Zero;
            VelDir = Vec3.Zero;
            VelDirLatched = Vec3.Zero;
            Pos = Vec3.Zero;
            PrevPos = Vec3.Zero;
            Attach = null;
            Shell = null;
            SurfNormal = Vec3.Zero;
            GravDeltaVel = 0.0f;
            BankForce = Vec3.Zero;
            Elasticity = 0.0f;
            Step = Vec3.Zero;
            for (int i = 0; i < PhysicsConstants.VelocityRingLength; i++)
            {
                RingVelHistory[i] = Vec3.Zero;
                RingVelSmooth[i] = Vec3.Zero;
            }
            RingIndex = 0;
            NetTarget = Vec3.Zero;
            Axis = Vec3.Zero;
            HeldBindings = HeldBinding.None;
            PrevHeldBindings = HeldBinding.None;
            GravAccel = 0.0f;
            Facing = 0.0f;
            SteerScale = 0.0f;
            AccelDt = 0.0f;
            Accel = 0.0f;
            Friction = 0.0f;
            Speed = 0.0f;
            NetTargetPending = false;
            PrevTick = 0;
            Move = new BodyMove { Dir = Vec3.Zero, Target = Vec3.Zero };
            BaseForwardSpeed = 0.0f;
            ForwardSpeedDt = 0.0f;
            ForwardAccel = 0.0f;
            EngineGravityDt = 0.0f;
        }

        public Vec3 GetVel() { return Vel; }
        public float GetSpeed() { return Speed; }
        public float GetForwardSpeedDt() { return ForwardSpeedDt; }
        public float GetAxisX() { return Axis.x; }

        // physicsbody.cpp BindToObject (0x00407780): speed and acceleration are premultiplied by
        // the frame delta here; the heading is in degrees.
        public void BindToObject(int slot, EngineObjectKind kind, ObjectMotionParams def, EngineClock clock, float engineGravityDt)
        {
            Slot = slot;
            OwnerKind = kind;

            PrevPos = def.Position;
            Pos = def.Position;

            Speed = 0.0f;
            Shell = EllipsoidStatics.Arena;

            Friction = def.Friction;
            Elasticity = def.Elasticity;
            ForwardAccel = 0.0f;
            BaseForwardSpeed = def.Speed;
            Accel = def.Acceleration;

            ForwardSpeedDt = clock.Delta * def.Speed;
            AccelDt = clock.DeltaSquared * def.Acceleration;

            SteerScale = 1.0f;
            GravAccel = PhysicsConstants.Gravity;
            EngineGravityDt = engineGravityDt;
            GravDeltaVel = engineGravityDt;

            float azimuth = def.AzimuthDegrees;
            if (azimuth < 0.0f)
            {
                azimuth = MathOps.DegreesPerTurn + azimuth;
            }

            VelDir.x = (float)Math.Cos(azimuth * 0.0174532925);
            VelDir.y = (float)-Math.Sin(azimuth * 0.0174532925);
            Facing = (float)(azimuth * 0.0174532925);
        }

        // physicsbody.cpp ResetSpeeds (0x00407890): the main camera keeps six fields.
        public void ResetSpeeds(EngineClock clock)
        {
            ResetDynamics(clock);
            if (Slot != ObjectConstants.CameraMainSlot)
            {
                VelDir = new Vec3(0.0f, 1.0f, 0.0f);
                Vel = Vec3.Zero;
            }
            SmoothVel = Vec3.Zero;
            Axis = Vec3.Zero;
        }

        // physicsbody.cpp ResetDynamics (0x00407920): seeded from the frame's own delta.
        public void ResetDynamics(EngineClock clock)
        {
            Speed = 0.0f;
            ForwardAccel = 0.0f;
            ForwardSpeedDt = clock.Delta * BaseForwardSpeed;
            AccelDt = clock.DeltaSquared * Accel;
        }

        // physicsbody.cpp PreUpdateTick (0x00407960): rescales the previous frame's accumulators
        // to this frame's delta.
        public void PreUpdateTick(EngineClock clock)
        {
            float step = clock.Delta * clock.PrevInvDelta;
            Speed = step * Speed;
            Vel = Vec3.ScaleTo(Vel, step);
            ForwardAccel = clock.DeltaSquared * clock.PrevInvDeltaSquared * ForwardAccel;
            GravDeltaVel = clock.DeltaSquared * GravAccel;
            ForwardSpeedDt = clock.Delta * BaseForwardSpeed;
            AccelDt = clock.DeltaSquared * Accel;
        }

        // physicsbody.cpp TakeSurfaceNormal (0x00407a60).
        public bool TakeSurfaceNormal(Vec3 point)
        {
            Vec3 normal;
            if (!Shell.NormalAtPoint(point, out normal))
            {
                return false;
            }
            SurfNormal = normal;
            return true;
        }

        // physicsbody.cpp ResetOnSurface (0x00407ab0): snaps the body to the shell's floor and
        // builds a surface-plane velocity direction from the initial x axis.
        public void ResetOnSurface()
        {
            // The original seeds this from g_pGameEngine's clock; the caller can pass one with
            // ResetOnSurface(clock). The zeroed clock only affects the two premultiplied fields,
            // which this routine zeroes anyway.
            ResetDynamics(new EngineClock());

            float height = Shell.GetZ(Pos.x, Pos.y);
            PrevPos.z = height;
            Pos.z = height;

            Vec3 normal;
            if (!Shell.NormalAtPoint(Pos, out normal))
            {
                return;
            }
            SurfNormal = normal;

            VelDir.x = 1.0f;
            VelDir.y = 0.0f;

            // CollisionObj(&surfNormal, &origin(0,0,0), 0, 1).GetZ(velDir.x, velDir.y):
            // slopeX = -n.x/n.z, slopeY = -n.y/n.z, offset = 0, so z = x*slopeX + y*slopeY.
            float z = normal.z != 0.0f
                ? VelDir.x * -(normal.x / normal.z) + VelDir.y * -(normal.y / normal.z)
                : 0.0f;

            VelDirLatched.x = VelDir.x;
            VelDirLatched.y = VelDir.y;
            VelDir.z = z;
            VelDirLatched.z = z;

            Vec3 normalized;
            if (!MathOps.NormalizeTo(VelDir, out normalized))
            {
                return;
            }
            VelDir = normalized;

            Vel.x = GetSpeed() * VelDir.x;
            Vel.y = Speed * VelDir.y;
            Vel.z = GetSpeed() * VelDir.z;

            SmoothVel = Vec3.Zero;
            Axis = Vec3.Zero;
            Facing = 0.0f;
        }

        // physicsbody.cpp UpdateMovementAxes (0x00407c40). The shoot drive lasts one second past
        // the release; the stop binding publishes g_bMoveAxesStopHeld for the two steering blocks.
        public void UpdateMovementAxes(PlayerStats stats, EngineClock clock, PhysicsTuning tuning)
        {
            float push;
            float pull;
            float cap;
            float dot;
            float cross;
            float limit;

            push = (stats.GetWeightedStat(ObjectConstants.RatingBalance) * 0.14f + 0.5f) * AccelDt * tuning.ForceIncrement;
            float clockSquared = clock.DeltaSquared;
            pull = (stats.GetWeightedStat(ObjectConstants.RatingBalance) * 0.12f + 0.4f) * Speed * clockSquared;
            cap = (stats.GetWeightedStat(ObjectConstants.RatingBalance) * 0.06f + 0.2f) * AccelDt * tuning.MaxAcceleration;

            if ((HeldBindings & HeldBinding.Shoot) != 0)
            {
                Axis.x = push + Axis.x;
                MoveAxesShootHeld = true;
            }
            else if (MoveAxesShootHeld)
            {
                MoveAxesShootReleaseTick = clock.TickMs;
                MoveAxesShootHeld = false;
            }

            if (MoveAxesShootReleaseTick != 0)
            {
                Axis.x = push + Axis.x;
                if (clock.TickMs - MoveAxesShootReleaseTick > PhysicsConstants.ShootDriveTailMs)
                {
                    MoveAxesShootReleaseTick = 0;
                }
            }

            HeldBinding held = HeldBindings;

            if (held == HeldBinding.None && PrevHeldBindings != HeldBinding.None)
            {
                Axis.y = 0.0f;
                Axis.x = 0.0f;
                PrevHeldBindings = HeldBinding.None;
                return;
            }

            if (HeldBindings == HeldBinding.None && PrevHeldBindings == HeldBinding.None)
            {
                Axis.x = Math.Abs(Axis.x) > PhysicsConstants.AxisFloorIdle ? PhysicsConstants.MovementAxisDecay * Axis.x : 0.0f;
                Axis.y = Math.Abs(Axis.y) > PhysicsConstants.AxisFloorIdle ? PhysicsConstants.MovementAxisDecay * Axis.y : 0.0f;
                return;
            }

            if ((held & (HeldBinding.Throttle | HeldBinding.Shoot)) == 0)
            {
                Axis.x = Math.Abs(Axis.x) > 0.01f ? PhysicsConstants.MovementAxisDecay * Axis.x : 0.0f;
            }

            if ((held & HeldBinding.Rotate) == 0)
            {
                Axis.y = Math.Abs(Axis.y) > 0.01f ? PhysicsConstants.MovementAxisDecay * Axis.y : 0.0f;
            }

            HeldBinding movement = held & HeldBinding.Movement;
            if (movement != HeldBinding.None)
            {
                if ((movement & HeldBinding.Movement) == HeldBinding.Forward)
                {
                    Vec3 rowA = VelDir;
                    Vec3 rowB = VelDirLatched;

                    dot = 1.0f - (rowB.x * VelDir.x + (rowB.y * rowA.y + rowB.z * rowA.z));

                    if ((cross = rowB.y * rowA.x - rowB.x * rowA.y) < -0.001f)
                    {
                        Axis.y = Math.Abs(dot) * push + Axis.y;
                    }
                    else if (cross > 0.0001f)
                    {
                        Axis.y -= Math.Abs(dot) * push;
                    }
                }

                if ((movement & HeldBinding.Forward) != 0)
                {
                    Axis.x = push + Axis.x;
                }

                MoveAxesStopHeld = ((movement & HeldBinding.Stop) != 0);

                float turn = tuning.NetTracking * Speed * PhysicsConstants.TurnScale;

                if ((movement & HeldBinding.RotateLeft) != 0)
                {
                    if (MoveAxesStopHeld)
                    {
                        Axis.y = turn * -4.0f;
                    }
                    else if (-turn < Axis.y)
                    {
                        Axis.y = -turn;
                    }

                    MoveAxesStopHeld = false;

                    float downY = Axis.y - pull;
                    Axis.y = downY;

                    limit = Speed * -PhysicsConstants.TurnLimit;
                    if (downY < limit)
                    {
                        Axis.y = limit;
                    }
                }

                if ((movement & HeldBinding.RotateRight) != 0)
                {
                    if (MoveAxesStopHeld)
                    {
                        Axis.y = turn * 4.0f;
                    }
                    else if (Axis.y < turn)
                    {
                        Axis.y = turn;
                    }

                    MoveAxesStopHeld = false;

                    float upY = pull + Axis.y;
                    Axis.y = upY;

                    limit = Speed * PhysicsConstants.TurnLimit;
                    if (upY > limit)
                    {
                        Axis.y = limit;
                    }
                }
            }

            double abs = Math.Abs((double)Axis.x);

            if (abs < cap)
            {
                Axis.x = GetAxisX();
            }
            else
            {
                Axis.x = (float)(Axis.x / abs) * cap;
            }
        }

        // physicsbody.cpp ApplySteeringForce (0x00408080): the cross product and normalise run even
        // for a second player that skips the force.
        public void ApplySteeringForce(bool twoPlayers)
        {
            Vec3 lateral = new Vec3(
                SurfNormal.z * VelDir.y - SurfNormal.y * VelDir.z,
                VelDir.z * SurfNormal.x - SurfNormal.z * VelDir.x,
                SurfNormal.y * VelDir.x - SurfNormal.x * VelDir.y);

            Vec3 normalized;
            if (MathOps.NormalizeTo(lateral, out normalized))
            {
                lateral = normalized;
            }

            if (!twoPlayers)
            {
                if ((double)SteerScale > PhysicsConstants.SteerScaleThreshold)
                {
                    SmoothVel.x = (lateral.x * Axis.y + VelDir.x) * SteerScale + SmoothVel.x;
                    SmoothVel.y = (lateral.y * Axis.y + VelDir.y) * SteerScale + SmoothVel.y;
                    SmoothVel.z = (lateral.z * Axis.y + VelDir.z) * SteerScale + SmoothVel.z;
                }
                else
                {
                    SmoothVel.x = lateral.x * Axis.y + VelDir.x * Axis.x + SmoothVel.x;
                    SmoothVel.y = lateral.y * Axis.y + VelDir.y * Axis.x + SmoothVel.y;
                    SmoothVel.z = VelDir.z * Axis.x + lateral.z * Axis.y + SmoothVel.z;
                }
            }
        }

        // physicsbody.cpp InitOnSurface (0x00408190): projects along the previous call's normal,
        // so the result depends on the last call.
        public void InitOnSurface(int unused)
        {
            Vec3 nextPos;

            PrevPos = Pos;

            nextPos.x = PrevPos.x + Vel.x;
            nextPos.y = PrevPos.y + Vel.y;
            nextPos.z = PrevPos.z + Vel.z;

            Vec3 hit;
            Shell.RayIntersection(SurfNormal, nextPos, out hit);
            Pos = hit;

            Vec3 normal;
            if (!Shell.NormalAtPoint(Pos, out normal))
            {
                return;
            }
            SurfNormal = normal;

            float length = (float)Math.Sqrt(Vel.x * Vel.x + Vel.y * Vel.y + Vel.z * Vel.z);
            Speed = length;

            if (length > PhysicsConstants.MinVelocityDirectionSpeed)
            {
                float scale = 1.0f / length;
                VelDir.x = scale * Vel.x;
                VelDir.y = scale * Vel.y;
                VelDir.z = scale * Vel.z;
            }

            VelDirLatched = VelDir;

            SmoothVel = Vec3.Zero;
            BankForce = Vec3.Zero;
        }

        // physicsbody.cpp IntegrateMotion (0x004082c0). A step over fifty snaps; the heading sum
        // starts with y at one, so it never divides by zero.
        public void IntegrateMotion(EngineClock clock, float bandwidth)
        {
            Vec3 smooth = Vec3.Zero;
            Vec3 dir = new Vec3(0.0f, 1.0f, 0.0f);

            Move.Dir = new Vec3(Move.Target.x - Pos.x, Move.Target.y - Pos.y, Move.Target.z - Pos.z);

            float root = (float)Math.Sqrt(Move.Dir.x * Move.Dir.x + Move.Dir.y * Move.Dir.y + Move.Dir.z * Move.Dir.z);

            Vec3 normalized;
            if (MathOps.NormalizeTo(Move.Dir, out normalized))
            {
                Move.Dir = normalized;
            }

            if (root > 50.0)
            {
                Pos = Move.Target;
            }

            float step = clock.GetDeltaSquared() * bandwidth;
            AccelDt = step;
            step *= root;

            RingVelSmooth[RingIndex].x = step * Move.Dir.x;
            RingVelSmooth[RingIndex].y = step * Move.Dir.y;
            RingVelSmooth[RingIndex].z = step * Move.Dir.z;

            for (int i = 0; i < PhysicsConstants.VelocityRingLength; i++)
            {
                smooth.x += RingVelSmooth[i].x;
                smooth.y += RingVelSmooth[i].y;
                smooth.z += RingVelSmooth[i].z;
            }

            SmoothVel = Vec3.ScaleTo(smooth, 0.25f);

            ForwardSpeedDt = (float)Math.Sqrt(Step.x * Step.x + Step.y * Step.y + Step.z * Step.z)
                             * clock.Delta * PhysicsConstants.ForwardSpeedDtGain;

            Vel.x = Vel.x + SmoothVel.x;
            Vel.y = SmoothVel.y + Vel.y;
            Vel.z = SmoothVel.z + Vel.z;

            InitOnSurface(0);

            if (Speed > PhysicsConstants.MinVelocityDirectionSpeed)
            {
                RingVelHistory[RingIndex] = Vel;
            }

            for (int i = 0; i < PhysicsConstants.VelocityRingLength; i++)
            {
                dir.x += RingVelHistory[i].x;
                dir.y += RingVelHistory[i].y;
                dir.z += RingVelHistory[i].z;
            }

            VelDir = dir;
            Vec3 normalizedDir;
            if (MathOps.NormalizeTo(VelDir, out normalizedDir))
            {
                VelDir = normalizedDir;
            }

            RingIndex = (RingIndex + 1) % PhysicsConstants.VelocityRingLength;
        }

        // physicsbody.cpp LaunchDiagonal (0x00408540): pins the direction to the 45-degree diagonal.
        public void LaunchDiagonal(EngineClock clock)
        {
            ResetDynamics(clock);

            float diagonal = Speed * 0.707f;

            VelDir.x = 0.707f;
            VelDir.y = 0.0f;
            VelDir.z = 0.707f;

            Vel.x = diagonal;
            Vel.y = GetSpeed() * VelDir.y;
            Vel.z = diagonal;

            SmoothVel = Vec3.Zero;
            Axis = Vec3.Zero;
            Facing = 0.0f;
        }

        // physicsbody.cpp IntegrateGravity (0x004085d0).
        public bool IntegrateGravity(EngineClock clock)
        {
            PrevPos = Pos;

            Pos.x = Vel.x + Pos.x;
            Pos.y = Vel.y + Pos.y;
            Pos.z = (Vel.z + Pos.z) - 0.5f * GravDeltaVel;

            if (GravDeltaVel < 0.01f)
            {
                GravAccel = PhysicsConstants.Gravity;
                GravDeltaVel = clock.GetDeltaSquared() * PhysicsConstants.Gravity;
            }

            Vel.z -= GravDeltaVel;

            float length = Vel.Magnitude();
            Speed = length;

            if (length > PhysicsConstants.MinVelocityDirectionSpeed)
            {
                float invSpeed = 1.0f / length;
                VelDir.x = invSpeed * Vel.x;
                VelDir.y = invSpeed * Vel.y;
                VelDir.z = Vel.z * invSpeed;
                return true;
            }

            return false;
        }

        // physicsbody.cpp StepWithGravity (0x004086a0).
        public bool StepWithGravity(float fraction)
        {
            PrevPos = Pos;

            Pos.x = fraction * Vel.x + Pos.x;
            Pos.y = fraction * Vel.y + Pos.y;
            Pos.z = (fraction * Vel.z + Pos.z) - fraction * (0.5f * GravDeltaVel);

            Vel.z -= fraction * GravDeltaVel;

            float length = Vel.Magnitude();
            Speed = length;

            if (length > PhysicsConstants.MinVelocityDirectionSpeed)
            {
                float invSpeed = 1.0f / length;
                VelDir.x = invSpeed * Vel.x;
                VelDir.y = invSpeed * Vel.y;
                VelDir.z = Vel.z * invSpeed;
                return true;
            }

            return false;
        }

        // physicsbody.cpp UpdateVelocityDirection (0x00408760): projects the velocity off the
        // surface normal; below the minimum speed it answers false.
        public bool UpdateVelocityDirection()
        {
            float drag = 1.0f - Friction;
            float blend = drag - Shell.Friction;
            float weight = Shell.Elasticity * Elasticity;

            float dot = Vel.z * SurfNormal.z + Vel.y * SurfNormal.y + Vel.x * SurfNormal.x;

            float projY = -(dot * SurfNormal.y);
            float projZ = -(dot * SurfNormal.z);
            float projX = -(dot * SurfNormal.x);

            float newX = (projX + Vel.x) * blend + projX * weight;
            Vel.x = newX;
            float newY = (projY + Vel.y) * blend + projY * weight;
            Vel.y = newY;
            float newZ = (projZ + Vel.z) * blend + projZ * weight;
            Vel.z = newZ;

            float length = (float)Math.Sqrt(Vel.LengthSquared());
            Speed = length;

            if (length > PhysicsConstants.MinVelocityDirectionSpeed)
            {
                double recip = 1.0 / length;
                VelDir.x = (float)(newX * recip);
                VelDir.y = (float)(newY * recip);
                VelDir.z = (float)(newZ * recip);
                return true;
            }

            return false;
        }

        // physicsbody.cpp ApplyBankForce (0x00408880). The right vector is (normal x dir).
        public void ApplyBankForce(int direction, float force)
        {
            Vec3 right = new Vec3(
                SurfNormal.z * VelDir.y - SurfNormal.y * VelDir.z,
                SurfNormal.x * VelDir.z - SurfNormal.z * VelDir.x,
                VelDir.x * SurfNormal.y - SurfNormal.x * VelDir.y);

            Vec3 normalized;
            if (MathOps.NormalizeTo(right, out normalized))
            {
                right = normalized;
            }

            if (direction == BankDirectionRight)
            {
                BankForce.x = right.x * force + BankForce.x;
                BankForce.y = right.y * force + BankForce.y;
                BankForce.z = right.z * force + BankForce.z;
                return;
            }

            BankForce.x -= right.x * force;
            BankForce.y -= right.y * force;
            BankForce.z -= right.z * force;
        }

        // physicsbody.cpp UpdateBanking (0x00408940): the camera repositioning at the end only runs
        // while the main camera is not in the track-trajectory behaviour; the camera is optional.
        public void UpdateBanking(EngineClock clock, CameraState mainCamera)
        {
            if ((HeldBindings & HeldBinding.Rotate) == 0)
            {
                return;
            }

            float angle;
            float cos;
            float sin;

            if ((HeldBindings & HeldBinding.RotateLeft) != 0)
            {
                angle = PhysicsConstants.BankingAngleRadians;
                cos = (float)Math.Cos(angle);
                sin = (float)Math.Sin(angle);
            }
            else
            {
                angle = -PhysicsConstants.BankingAngleRadians;
                cos = (float)Math.Cos(angle);
                sin = (float)Math.Sin(angle);
            }

            float dirX = VelDir.x;
            VelDir.x = dirX * cos - sin * VelDir.y;
            VelDir.y = dirX * sin + cos * VelDir.y;

            if (mainCamera == null)
            {
                return;
            }

            // The original reads bone 9's world row and the player's transformB column 1.
            // TODO(U3-M2): bone transforms are owned by the render/animation port; the camera
            // snap is exposed through CameraState.SnapToBone instead of being faked here.
            mainCamera.SnapToBone(this, angle);
        }

        // The globals physicsbody.cpp UpdateMovementAxes keeps between frames
        // (g_bMoveAxesShootHeld, g_nMoveAxesShootReleaseTick, g_bMoveAxesStopHeld).
        public static bool MoveAxesShootHeld;
        public static uint MoveAxesShootReleaseTick;
        public static bool MoveAxesStopHeld;

        // The bank direction constant from src/physics/banking.h; only the right/left sign matters.
        public const int BankDirectionRight = 1;
    }

    // src/engine/objectclasses.h object kinds the simulation distinguishes.
    public enum EngineObjectKind
    {
        Unknown = -1,
        Dromer = 0,
        Rok = 1,
        Camera = 2,
        Drome = 3,
        Goal = 4,
        Multiplier = 5,
        Device = 6,
    }
}
