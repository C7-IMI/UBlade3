// UBlade3 - exact HyperBlade (1996) simulation core.
// Camera: src/engine/cameraobject.cpp (0x00421980 constructor, 0x00421d80 UpdateSmoothing,
// 0x00421ef0 UpdateDirSmoothing, 0x00422490/0x004225a0 the history-size setters, 0x00422750
// InitViewTransforms), src/engine/cameraprojectionstate.cpp (0x004303b0 CreateProjectionState,
// 0x004304a0 Configure, 0x00430620 SetEyePoint, 0x00430690 BuildMatrix), src/engine/ctrack.cpp
// CTrack::Process (0x00408fd0), src/planvm/cheadtrack.cpp CHeadTrack (0x00409fc0/0x0040a100),
// src/planvm/ctracktraj.cpp CTrackTraj (0x0040a7b0).
// Camera offset modes come from src/engine/game.cpp (g_vBirdsEyeTrackOffset and the four other
// ID_OFS vectors) and planactiontracking.cpp SetTrackingOffset (0x004019a0).

using System;

namespace UBlade3.Sim
{
    // What a chasing camera needs from the object it watches. Implemented by PlayerState, RokState
    // and CameraState; the render/animation port supplies AimPosition (bone 9's world position).
    public interface ITrackedEntity
    {
        int Slot { get; }
        PhysicsBodyState Body { get; }
        bool IsPlayer { get; }
        int PlanBehaviorKind { get; }
        int KnockedDownState { get; }
        bool HasShootHeld { get; }
        int TargetGoalSlot { get; }
        Vec3 AimPosition { get; }
    }

    // src/engine/gameengine.h LayoutState, reduced to the fields the projection reads.
    public struct LayoutState
    {
        public int PrevWidth;
        public int PrevHeight;
        public float PrevRight;
        public float PrevBottom;
    }

    // src/engine/cameraprojectionstate.h CameraProjectionState: the perspective path the game's
    // cameras use (cameraobject.cpp constructs both states with cFlag = 1, so m_bHasRow3 is set).
    public sealed class CameraProjectionState
    {
        public float CmPerPixel;
        public float PixelsPerCm;
        public float HalfWidthPxA;
        public float HalfHeightPxA;
        public float HalfWidthPxB;
        public float HalfHeightPxB;
        public float HalfWidthPx;
        public float HalfHeightPx;
        public float HalfHeightCm;
        public float HalfWidthCm;
        public float OffsetXPx;
        public float OffsetYPx;
        public float HalfFov;
        public double TanHalfFov;
        public double CosHalfFov;
        public double SinHalfFov;
        public float TanHalfFovFloat;
        public float FarClip;
        public float NearClip;
        public Vec3 Eye;
        public Vec3 Translate;
        public float CotX;
        public float CotY;
        public float TanX;
        public float TanY;
        public float TanXSquared;
        public float TanYSquared;
        public bool HasRow3;
        public readonly Transform4 Matrix = new Transform4();

        // Constants from src/common/staticinitglobals.cpp.
        public const float DotsPerInchProjection = 72.0f;
        public const float DotPitchProjection = 0.75f;
        public const float CmPerInchProjection = 2.54f;

        public CameraProjectionState()
        {
            Matrix.Reset();
        }

        // cameraprojectionstate.cpp CreateProjectionState (0x004303b0): zero the numbers, then
        // Configure against the viewport with a 1000-unit far clip.
        public void CreateProjectionState(bool hasRow3, LayoutState view, float fov)
        {
            CmPerPixel = DotPitchProjection;
            PixelsPerCm = 0.0f;
            HalfWidthCm = 0.0f;
            HalfHeightCm = 0.0f;
            HalfHeightPxB = 0.0f;
            HalfWidthPxB = 0.0f;
            HalfHeightPxA = 0.0f;
            HalfWidthPxA = 0.0f;
            TanHalfFovFloat = 0.0f;
            HalfFov = 0.0f;
            OffsetYPx = 0.0f;
            OffsetXPx = 0.0f;
            NearClip = 0.0f;
            FarClip = 0.0f;
            Eye = Vec3.Zero;
            HasRow3 = hasRow3;

            Matrix.Reset();
            Configure(view, fov, 1000.0f);
        }

        // cameraprojectionstate.cpp Configure (0x004304a0): assumes a 72-dot-per-inch screen for
        // the centimetre conversion.
        public void Configure(LayoutState view, float halfFov, float farClip)
        {
            HalfWidthPx = HalfWidthPxB = HalfWidthPxA = view.PrevWidth * 0.5f;
            HalfHeightPx = HalfHeightPxB = HalfHeightPxA = view.PrevHeight * 0.5f;

            CmPerPixel = 1.0f / (DotsPerInchProjection / CmPerInchProjection);
            PixelsPerCm = 1.0f / CmPerPixel;

            HalfHeightCm = CmPerPixel * HalfHeightPxA;
            HalfWidthCm = CmPerPixel * HalfWidthPxA;

            OffsetXPx = view.PrevWidth * 0.5f;
            OffsetYPx = view.PrevHeight * 0.5f;

            HalfFov = halfFov;
            FarClip = farClip;

            double tan = Math.Tan(halfFov);
            TanHalfFov = tan;

            double sin = Math.Sin(halfFov);
            SinHalfFov = sin;

            double cos = Math.Cos(halfFov);
            CosHalfFov = cos;

            if (HasRow3)
            {
                TanHalfFovFloat = (float)tan;
                NearClip = 1.0f;
                HalfWidthPxA = view.PrevWidth * 0.5f;
            }
            else
            {
                NearClip = (float)(HalfWidthCm * cos / sin);
            }

            BuildMatrix();
        }

        // cameraprojectionstate.cpp BuildMatrix (0x00430690), the perspective arm plus the aspect
        // compose. The transform starts as an identity (CreateProjectionState resets it), so the
        // direct-write arm below is the one the game takes.
        public void BuildMatrix()
        {
            if (HasRow3)
            {
                float depth = FarClip - NearClip;
                float reach = FarClip * TanHalfFovFloat;
                float depthScale = reach / (depth * NearClip);
                float depthBias = -(reach / depth);
                float wPerZ = TanHalfFovFloat / NearClip;

                Matrix.Reset();
                Matrix.Identity = false;
                Matrix[0, 0] = HalfWidthPx;
                Matrix[1, 1] = HalfHeightPx;
                Matrix[0, 2] = wPerZ * OffsetXPx;
                Matrix[2, 2] = depthScale;
                Matrix[2, 3] = depthBias;
                Matrix[3, 2] = wPerZ;
                Matrix[1, 2] = wPerZ * OffsetYPx;
                Matrix[3, 3] = 0.0f;

                // The original builds an identity transform with scale (1, halfWidthCm / halfHeightCm, 1)
                // and composes it after the projection; that column-scales row one.
                Transform4 aspect = new Transform4(false);
                aspect.ApplyScale(1.0f, HalfWidthCm / HalfHeightCm, 1.0f);
                Matrix.Compose(aspect);
            }
            else
            {
                float scaleX = NearClip / HalfWidthCm;
                CotX = scaleX;

                float scaleY = NearClip / HalfHeightCm;
                CotY = scaleY;

                TanX = 1.0f / scaleX;
                TanY = 1.0f / scaleY;
                TanXSquared = TanX * TanX;
                TanYSquared = TanY * TanY;

                Matrix.Reset();
                Matrix.Identity = false;
                Matrix[0, 0] = scaleX;
                Matrix[1, 1] = scaleY;
            }
        }

        // cameraprojectionstate.cpp SetEyePoint (0x00430620): the eye and its negated translation.
        public void SetEyePoint(Vec3 eye)
        {
            Eye = eye;
            Translate = new Vec3(-eye.x, -eye.y, -eye.z);
            Matrix.SetTranslation(Translate.x, Translate.y, Translate.z);
        }

        // cameraprojectionstate.cpp SubtractTranslation (0x00430cc0).
        public void SubtractTranslation(float x, float y, float z)
        {
            float newX = Translate.x - x;
            Translate.x = newX;
            Matrix[0, 3] = newX;

            float newY = Translate.y - y;
            Translate.y = newY;
            Matrix[1, 3] = newY;

            float newZ = Translate.z - z;
            Translate.z = newZ;
            Matrix[2, 3] = newZ;

            Eye = new Vec3(-newX, -newY, -newZ);
        }
    }

    // The camera's two history windows (cameraobject.h CAMERA_HISTORY_CAPACITY = 25).
    public sealed class CameraHistoryWindow
    {
        public readonly Vec3[] Ring = new Vec3[CameraConstants.HistoryCapacity];
        public Vec3 Output;
        public short Count;
        public short Index;
        public float Weight;
    }

    // src/engine/cameraobject.h CameraObject, the simulation half (no textures, queue or renderer).
    public sealed class CameraState : ITrackedEntity
    {
        public readonly PhysicsBodyState Body = new PhysicsBodyState();

        public int PrimaryTracked = -1;   // m_nPrimaryTracked, 0x0d14
        public int SecondaryTracked = -1; // m_nSecondaryTracked, 0x0d18
        public int LastTracked = -1;      // m_nLastTracked, 0x0d1c
        public Vec3 AxisScale;            // m_vAxisScale, 0x0d20; m_pSettings points at it
        public float BungeeLength = CameraConstants.BungeeLength; // 0x0d30
        public readonly CameraHistoryWindow PosHistory = new CameraHistoryWindow();
        public readonly CameraHistoryWindow DirHistory = new CameraHistoryWindow();

        public float HalfFov = CameraConstants.HalfFieldOfView; // 0x1010
        public float ViewFracX = 0.5f;  // 0x0fb4
        public float ViewFracY = 0.5f;  // 0x0fb8
        public int ReticleState = CameraConstants.ReticleOff; // 0x0fbc
        public int ShotPowerLevel = 100;   // 0x0fc0
        public bool ShotPowerShown;        // 0x0fc4
        public bool RearView;              // 0x102c
        public int ViewportId;             // 0x0fcc

        public CameraProjectionState Projection = new CameraProjectionState();
        public CameraProjectionState View = new CameraProjectionState();
        public readonly Transform4 RotA = new Transform4(true);
        public readonly Transform4 RotB = new Transform4(true);

        // CTrack's state (src/engine/ctrack.h).
        public short Countdown;        // m_wCountdown
        public bool SwingBack;         // m_bSwingBack
        public bool SwingChosen;       // m_bSwingChosen
        public bool OutOfView;         // m_bOutOfView
        public Vec3 LastDir;           // m_vLastDir

        // The plan behavior kind the camera is running (for CTrack's own wState reads it is the
        // tracked object's kind; cameras also carry one for completeness).
        public int PlanBehaviorKind = ObjectConstants.BehaviorTrackTrajectory;

        public Func<int, ITrackedEntity> SlotLookup;

        public CameraState()
        {
            Body.Slot = ObjectConstants.CameraMainSlot;
            Body.OwnerKind = EngineObjectKind.Camera;
            ResetToInitialState(new Vec3(0.0f, 0.0f, 0.0f));
        }

        // cameraobject.cpp CameraObject::CameraObject (0x00421980): both rings are seeded with the
        // entity's position and a look a hundred behind the origin; the first camera body pos is
        // the object definition's, so the caller may pass it before calling this.
        public void ResetToInitialState(Vec3 bodyPos)
        {
            Body.Pos = bodyPos;
            Body.PrevPos = bodyPos;

            Vec3 dir = new Vec3(0.0f - bodyPos.x,
                                0.0f - bodyPos.y,
                                CameraConstants.CameraStartLookZ - bodyPos.z);
            Vec3 normalized;
            if (MathOps.NormalizeTo(dir, out normalized))
            {
                dir = normalized;
            }

            for (int i = 0; i < CameraConstants.HistoryCapacity; i++)
            {
                PosHistory.Ring[i] = bodyPos;
                DirHistory.Ring[i] = dir;
            }

            PosHistory.Output = bodyPos;
            DirHistory.Output = dir;
            Body.VelDir = dir;

            PosHistory.Count = CameraConstants.InitialPosHistory;
            PosHistory.Index = 0;
            DirHistory.Count = CameraConstants.InitialDirHistory;
            DirHistory.Index = 0;
            PosHistory.Weight = CameraConstants.InitialPosWeight;
            DirHistory.Weight = CameraConstants.InitialDirWeight;

            PrimaryTracked = -1;
            SecondaryTracked = -1;
            LastTracked = -1;

            AxisScale = Vec3.Zero;
            BungeeLength = CameraConstants.BungeeLength;

            ViewFracX = 0.5f;
            ViewFracY = 0.5f;
            ReticleState = CameraConstants.ReticleOff;
            ShotPowerShown = false;
            ShotPowerLevel = 100;
            HalfFov = CameraConstants.HalfFieldOfView;
            RearView = false;
            ViewportId = 0;
            Countdown = 0;
            SwingBack = false;
            SwingChosen = false;
            OutOfView = false;
            LastDir = Vec3.Zero;
        }

        // cameraobject.cpp InitViewTransforms (0x00422750): reset the projection and view for the
        // layout, then build the 90-degree tilt into the projection. The scratch transforms the
        // original constructs on the stack are constructed and destroyed unread.
        public void InitViewTransforms(LayoutState layout)
        {
            Projection = new CameraProjectionState();
            Projection.CreateProjectionState(true, layout, HalfFov);

            View = new CameraProjectionState();
            View.CreateProjectionState(true, layout, HalfFov);

            RotA.Reset();
            RotB.Reset();

            // The camera's pi/2 tilt about x, written into RotA (RotA is identity).
            float tilt = CameraConstants.PiCamera / 2.0f;
            RotA.Identity = false;
            RotA[1, 1] = (float)Math.Cos(tilt);
            RotA[1, 2] = (float)-Math.Sin(tilt);
            RotA[2, 1] = (float)Math.Sin(tilt);
            RotA[2, 2] = (float)Math.Cos(tilt);

            Projection.Matrix.Compose(RotA);
        }

        // cameraobject.cpp UpdateSmoothing (0x00421d80): both boxcars advance one slot per frame.
        public void UpdateSmoothing()
        {
            int posIndex = PosHistory.Index;

            Vec3 position = Body.Pos;

            PosHistory.Output.x += (position.x - PosHistory.Ring[posIndex].x) * PosHistory.Weight;
            PosHistory.Output.y += (position.y - PosHistory.Ring[posIndex].y) * PosHistory.Weight;
            PosHistory.Output.z += (position.z - PosHistory.Ring[posIndex].z) * PosHistory.Weight;

            int lookIndex = DirHistory.Index;

            DirHistory.Output.x += (Body.VelDir.x - DirHistory.Ring[lookIndex].x) * DirHistory.Weight;
            DirHistory.Output.y += (Body.VelDir.y - DirHistory.Ring[lookIndex].y) * DirHistory.Weight;
            DirHistory.Output.z += (Body.VelDir.z - DirHistory.Ring[lookIndex].z) * DirHistory.Weight;

            PosHistory.Ring[posIndex] = Body.Pos;
            DirHistory.Ring[DirHistory.Index] = Body.VelDir;

            PosHistory.Index = (short)((PosHistory.Index + 1) % PosHistory.Count);
            DirHistory.Index = (short)((DirHistory.Index + 1) % DirHistory.Count);
        }

        // cameraobject.cpp UpdateDirSmoothing (0x00421ef0). The shipped body reads the y component
        // through m_wDirIndex rather than the local index; same value until the ring wraps.
        public void UpdateDirSmoothing()
        {
            int index = DirHistory.Index;
            Vec3 lookAt = Body.VelDir;

            DirHistory.Output.x += (lookAt.x - DirHistory.Ring[index].x) * DirHistory.Weight;
            DirHistory.Output.y += (lookAt.y - DirHistory.Ring[DirHistory.Index].y) * DirHistory.Weight;
            DirHistory.Output.z += (lookAt.z - DirHistory.Ring[index].z) * DirHistory.Weight;

            DirHistory.Ring[index] = lookAt;

            int next = DirHistory.Index + 1;
            DirHistory.Index = (short)(next % DirHistory.Count);
        }

        // cameraobject.cpp SetPosHistorySize (0x00422490). A count of zero divides by zero; only the
        // callers' clamp to [1, 25] prevents it, so this port rejects it instead.
        public bool SetPosHistorySize(short count)
        {
            if (count < 1 || count > CameraConstants.HistoryCapacity)
            {
                return false;
            }

            short old = PosHistory.Count;
            PosHistory.Count = count;
            PosHistory.Weight = 1.0f / count;

            if (PosHistory.Index >= count)
            {
                PosHistory.Index = 0;
            }

            while (old < PosHistory.Count)
            {
                PosHistory.Ring[old] = PosHistory.Output;
                old++;
            }

            Vec3 sum = Vec3.Zero;
            for (short i = 0; i < PosHistory.Count; i++)
            {
                sum.x += PosHistory.Ring[i].x;
                sum.y += PosHistory.Ring[i].y;
                sum.z += PosHistory.Ring[i].z;
            }

            sum.x *= PosHistory.Weight;
            sum.y *= PosHistory.Weight;
            sum.z *= PosHistory.Weight;
            PosHistory.Output = sum;
            return true;
        }

        // cameraobject.cpp SetDirHistorySize (0x004225a0).
        public bool SetDirHistorySize(short count)
        {
            if (count < 1 || count > CameraConstants.HistoryCapacity)
            {
                return false;
            }

            short old = DirHistory.Count;
            DirHistory.Count = count;
            DirHistory.Weight = 1.0f / count;

            if (DirHistory.Index >= count)
            {
                DirHistory.Index = 0;
            }

            while (old < DirHistory.Count)
            {
                DirHistory.Ring[old] = DirHistory.Output;
                old++;
            }

            Vec3 sum = Vec3.Zero;
            for (short i = 0; i < DirHistory.Count; i++)
            {
                sum.x += DirHistory.Ring[i].x;
                sum.y += DirHistory.Ring[i].y;
                sum.z += DirHistory.Ring[i].z;
            }

            sum.x *= DirHistory.Weight;
            sum.y *= DirHistory.Weight;
            sum.z *= DirHistory.Weight;
            DirHistory.Output = sum;
            return true;
        }

        public ITrackedEntity GetTrackedObject()
        {
            if (PrimaryTracked == -1 || SlotLookup == null)
            {
                return null;
            }
            return SlotLookup(PrimaryTracked);
        }

        public ITrackedEntity GetSecondaryTrackedObject()
        {
            if (SecondaryTracked == -1 || SlotLookup == null)
            {
                return null;
            }
            return SlotLookup(SecondaryTracked);
        }

        public ITrackedEntity GetLastTrackedObject()
        {
            if (LastTracked == -1 || SlotLookup == null)
            {
                return null;
            }
            return SlotLookup(LastTracked);
        }

        public void SetTrackedObject(ITrackedEntity entity)
        {
            PrimaryTracked = entity == null ? -1 : entity.Slot;
        }

        public void SetSecondaryTrackedObject(ITrackedEntity entity)
        {
            SecondaryTracked = entity == null ? -1 : entity.Slot;
        }

        public void SetLastTrackedObject(ITrackedEntity entity)
        {
            LastTracked = entity == null ? -1 : entity.Slot;
        }

        // cameraobject.cpp SetReticleState (0x00422450).
        public void SetReticleState(int state)
        {
            ReticleState = state;
            if (state == CameraConstants.ReticleAim)
            {
                ShotPowerLevel = 0;
            }
        }

        // src/planvm/planactiontracking.cpp ResetCameraBufferSize (0x00401ee0): the windows are
        // sized from the frame rate and the steady-cam buffer, clamped to [1, 25].
        public void ResetCameraBufferSize(EngineClock clock, float steadyCamBuffer)
        {
            float rate = clock.GetInvDelta();

            short pos = (short)(rate * steadyCamBuffer * CameraConstants.PosWindowSeconds);
            if (pos > CameraConstants.HistoryCapacity)
            {
                pos = CameraConstants.HistoryCapacity;
            }
            else if (pos < 1)
            {
                pos = 1;
            }
            SetPosHistorySize(pos);

            rate = clock.GetInvDelta();
            short dir = (short)(rate * steadyCamBuffer * CameraConstants.DirWindowSeconds);
            if (dir > CameraConstants.HistoryCapacity)
            {
                dir = CameraConstants.HistoryCapacity;
            }
            else if (dir < 1)
            {
                dir = 1;
            }
            SetDirHistorySize(dir);
        }

        // physicsbody.cpp UpdateBanking's camera snap needs bone 9's world row and the player's
        // transform; the animation/render port owns both. TODO(U3-M3): replace the no-op with the
        // real bone read (physicsbody.cpp 0x00408940 tail).
        public void SnapToBone(PhysicsBodyState playerBody, float angle)
        {
            // Intentionally empty until the bone transforms are ported.
        }

        // --- ITrackedEntity: a camera can itself be the tracked object of another camera. ---
        public int Slot { get { return Body.Slot; } }
        PhysicsBodyState ITrackedEntity.Body { get { return Body; } }
        public bool IsPlayer { get { return false; } }
        int ITrackedEntity.PlanBehaviorKind { get { return PlanBehaviorKind; } }
        public int KnockedDownState { get { return 0; } }
        public bool HasShootHeld { get { return false; } }
        public int TargetGoalSlot { get { return -1; } }
        public Vec3 AimPosition { get { return Body.Pos; } }
    }

    // src/engine/ctrack.cpp CTrack::Process (0x00408fd0): the chase camera's settle countdown,
    // bungee spring, "smart cam" switch, offset-mode basis and look swing.
    public static class CameraTrack
    {
        public const int SwingSteps = 6;
        // src/globals.cpp: g_bSmartCamEnabled, the animation peek dialog's checkbox.
        public static bool SmartCamEnabled;
        // src/globals.cpp: g_bCamTrackLookEnabled, the debug dialog's watch-rok checkbox.
        public static bool LookEnabled = true;

        // Returns false when the camera has no tracked object (the original calls g_gameApp.Fatal
        // and continues; a headless slice reports the fault instead).
        public static bool Process(CameraState camera, EngineClock clock, Func<int, ITrackedEntity> slotLookup,
            Func<AttachPak> rokAttach, int viewedSlot)
        {
            if (camera.SlotLookup == null)
            {
                camera.SlotLookup = slotLookup;
            }

            float flStep = clock.GetDelta() * 50.0f;

            Vec3 offsetScales = camera.AxisScale;

            ITrackedEntity tracked = camera.GetTrackedObject();
            if (tracked == null)
            {
                return false;
            }

            ITrackedEntity lastTracked = camera.GetLastTrackedObject();
            PhysicsBodyState body = tracked.Body;

            if (lastTracked == null || lastTracked != tracked)
            {
                float frames;
                if (lastTracked != null)
                {
                    frames = clock.InvDelta * 0.75f;
                    if (10.0f > frames)
                    {
                        frames = 10.0f;
                    }
                }
                else
                {
                    frames = clock.InvDelta * 1.5f;
                }

                camera.Countdown = (short)frames;
                camera.SwingChosen = false;
                camera.OutOfView = true;
            }

            camera.SetLastTrackedObject(tracked);

            Vec3 aTarget;

            if (tracked.Slot < ObjectConstants.PlayerObjectCount)
            {
                aTarget = tracked.AimPosition;

                if (tracked.HasShootHeld)
                {
                    AttachPak holder = rokAttach != null ? rokAttach() : null;

                    if (holder != null)
                    {
                        int carrier = holder.Slot;
                        if (carrier < ObjectConstants.PlayerObjectCount && carrier == tracked.Slot)
                        {
                            ITrackedEntity goal = slotLookup != null ? slotLookup(tracked.TargetGoalSlot) : null;
                            if (goal != null)
                            {
                                // Worked out and then never read in the original.
                                Vec3 scaled = goal.Body.Pos.Minus(tracked.Body.Pos);
                                Vec3 normalized;
                                MathOps.NormalizeTo(scaled, out normalized);
                            }
                        }
                    }
                }
            }
            else
            {
                int viewer = viewedSlot; // g_pGameEngine->m_nViewedSlot
                AttachPak holder = body.Attach;

                if (holder != null && holder.Slot < ObjectConstants.PlayerObjectCount)
                {
                    camera.PrimaryTracked = viewer;
                    camera.SetSecondaryTrackedObject(null);
                }
                else if (tracked.PlanBehaviorKind != ObjectConstants.BehaviorFlyToward)
                {
                    camera.PrimaryTracked = viewer;
                    camera.SetSecondaryTrackedObject(null);
                }

                aTarget = body.Pos;
            }

            Vec3 vTrack;
            Vec3 vLook;
            vTrack.x = vLook.x = aTarget.x - camera.Body.Pos.x;
            vTrack.y = vLook.y = aTarget.y - camera.Body.Pos.y;

            float drop = aTarget.z - camera.Body.Pos.z;

            vTrack.z = drop + CameraConstants.TrackingHeight;
            vLook.z = drop + CameraConstants.LookHeight;

            Vec3 normalizedLook;
            if (MathOps.NormalizeTo(vLook, out normalizedLook))
            {
                vLook = normalizedLook;
            }

            float pull = ((float)Math.Sqrt(vTrack.x * vTrack.x + (vTrack.y * vTrack.y + vTrack.z * vTrack.z))
                          - camera.GetBungeeLength()) * 1.5f;

            if (pull <= 0.0f)
            {
                pull *= CameraConstants.TrackVelocityGain;
            }

            Vec3 normalizedTrack;
            if (MathOps.NormalizeTo(vTrack, out normalizedTrack))
            {
                vTrack = normalizedTrack;
            }

            camera.Body.SmoothVel = Vec3.ScaleTo(vTrack, pull);

            Vec3 facing = body.VelDir;

            if (Math.Abs(body.Facing) > 0.1f)
            {
                float cos = (float)Math.Cos(body.Facing);
                float sin = (float)Math.Sin(body.Facing);

                float rawX = facing.z * 0.0f - facing.y * 1.0f;
                float rawY = facing.x * 1.0f - facing.z * 0.0f;

                facing.x = rawX * sin + facing.x * cos;
                facing.y = rawY * sin + facing.y * cos;
            }

            int state = tracked.PlanBehaviorKind;

            if (state == ObjectConstants.BehaviorFly
                || state == ObjectConstants.BehaviorRailSlide
                || camera.Countdown != 0)
            {
                camera.Body.Vel.x = (camera.Body.SmoothVel.x - camera.Body.Vel.x * 0.6f) + camera.Body.Vel.x;
                camera.Body.Vel.y = (camera.Body.SmoothVel.y - camera.Body.Vel.y * 0.6f) + camera.Body.Vel.y;
                camera.Body.Vel.z = (camera.Body.SmoothVel.z - camera.Body.Vel.z * 0.6f) + camera.Body.Vel.z;

                float speed = (float)Math.Sqrt(camera.Body.Vel.x * camera.Body.Vel.x
                                               + camera.Body.Vel.y * camera.Body.Vel.y
                                               + camera.Body.Vel.z * camera.Body.Vel.z);
                if (speed > flStep)
                {
                    camera.Body.Vel = Vec3.ScaleTo(camera.Body.Vel, flStep / speed);
                }

                camera.Body.Pos.x = camera.Body.Vel.x + camera.Body.Pos.x;
                camera.Body.Pos.y = camera.Body.Vel.y + camera.Body.Pos.y;
                camera.Body.Pos.z = camera.Body.Vel.z + camera.Body.Pos.z;

                if (camera.Body.Pos.x * camera.Body.Pos.x * EllipsoidStatics.CamTrack.GetInvXX()
                    + (camera.Body.Pos.z * camera.Body.Pos.z * EllipsoidStatics.CamTrack.GetInvZZ()
                       + camera.Body.Pos.y * camera.Body.Pos.y * EllipsoidStatics.CamTrack.GetInvYY()) > 1.0f)
                {
                    Vec3 farRay;
                    Vec3 nearRay;
                    EllipsoidStatics.CamTrack.IntersectSegment(camera.Body.Pos, Vec3.Zero, out farRay, out nearRay);
                    camera.Body.Pos = Vec3.FindClosest(camera.Body.Pos, farRay, nearRay);
                }

                if (camera.Countdown != 0)
                {
                    camera.OutOfView = true;
                    camera.Countdown--;
                }
            }
            else
            {
                Vec3 back = Vec3.ScaleTo(facing, -camera.GetBungeeLength());

                Vec3 wish = new Vec3(back.x + aTarget.x, back.y + aTarget.y, back.z + aTarget.z);

                if (wish.x * wish.x * EllipsoidStatics.CamTrack.GetInvXX()
                    + (wish.y * wish.y * EllipsoidStatics.CamTrack.GetInvYY()
                       + wish.z * wish.z * EllipsoidStatics.CamTrack.GetInvZZ()) > 1.0f)
                {
                    Vec3 nearSegment;
                    Vec3 raw;
                    EllipsoidStatics.CamTrack.IntersectSegment(wish, Vec3.Zero, out nearSegment, out raw);
                    wish = Vec3.FindClosest(wish, nearSegment, raw);
                }

                Vec3 pullVector = new Vec3(wish.x - camera.Body.Pos.x, wish.y - camera.Body.Pos.y, wish.z - camera.Body.Pos.z);

                if (SmartCamEnabled)
                {
                    camera.Body.Vel.x = (camera.Body.SmoothVel.x - camera.Body.Vel.x * 0.6f) + camera.Body.Vel.x;
                    camera.Body.Vel.y = (camera.Body.SmoothVel.y - camera.Body.Vel.y * 0.6f) + camera.Body.Vel.y;
                    camera.Body.Vel.z = (camera.Body.SmoothVel.z - camera.Body.Vel.z * 0.6f) + camera.Body.Vel.z;

                    float speed = (float)Math.Sqrt(camera.Body.Vel.x * camera.Body.Vel.x
                                                   + camera.Body.Vel.y * camera.Body.Vel.y
                                                   + camera.Body.Vel.z * camera.Body.Vel.z);
                    if (speed > flStep)
                    {
                        camera.Body.Vel = Vec3.ScaleTo(camera.Body.Vel, flStep / speed);
                    }

                    camera.Body.Vel.x = pullVector.x * CameraConstants.SolarWindFactor + camera.Body.Vel.x;
                    camera.Body.Vel.y = pullVector.y * CameraConstants.SolarWindFactor + camera.Body.Vel.y;
                    camera.Body.Vel.z = pullVector.z * CameraConstants.SolarWindFactor + camera.Body.Vel.z;

                    speed = (float)Math.Sqrt(camera.Body.Vel.x * camera.Body.Vel.x
                                              + camera.Body.Vel.y * camera.Body.Vel.y
                                              + camera.Body.Vel.z * camera.Body.Vel.z);
                    if (speed > flStep * 2.5f)
                    {
                        camera.Body.Vel = Vec3.ScaleTo(camera.Body.Vel, flStep * 2.5f / speed);
                    }
                }
                else
                {
                    camera.Body.Vel.x = pullVector.x * CameraConstants.SolarWindFactor
                                        + (camera.Body.SmoothVel.x - camera.Body.Vel.x * 0.6f) + camera.Body.Vel.x;
                    camera.Body.Vel.y = pullVector.y * CameraConstants.SolarWindFactor
                                        + (camera.Body.SmoothVel.y - camera.Body.Vel.y * 0.6f) + camera.Body.Vel.y;
                    camera.Body.Vel.z = pullVector.z * CameraConstants.SolarWindFactor
                                        + (camera.Body.SmoothVel.z - camera.Body.Vel.z * 0.6f) + camera.Body.Vel.z;

                    float speed = (float)Math.Sqrt(camera.Body.Vel.x * camera.Body.Vel.x
                                                   + camera.Body.Vel.y * camera.Body.Vel.y
                                                   + camera.Body.Vel.z * camera.Body.Vel.z);
                    if (speed > flStep)
                    {
                        camera.Body.Vel = Vec3.ScaleTo(camera.Body.Vel, flStep / speed);
                    }
                }

                camera.Body.Pos.x = camera.Body.Vel.x + camera.Body.Pos.x;
                camera.Body.Pos.y = camera.Body.Vel.y + camera.Body.Pos.y;
                camera.Body.Pos.z = camera.Body.Vel.z + camera.Body.Pos.z;

                if (tracked.Slot < ObjectConstants.PlayerObjectCount)
                {
                    if (tracked.KnockedDownState != 0)
                    {
                        camera.OutOfView = true;
                    }

                    float deltaY = wish.y - aTarget.y;
                    float absY = Math.Abs(deltaY);

                    pullVector = aTarget;

                    float deltaX = wish.x - aTarget.x;
                    float absX = Math.Abs(deltaX);

                    float hypot = (float)Math.Sqrt(absX * absX + absY * absY);

                    float offset;
                    if (absX < 1e-06f)
                    {
                        offset = Math.Abs(camera.Body.Pos.x - wish.x);
                    }
                    else
                    {
                        float slope = deltaY / deltaX;
                        float onLine = (aTarget.y - slope * aTarget.x) + slope * camera.Body.Pos.x;
                        offset = (float)(Math.Abs(onLine - camera.Body.Pos.y) * Math.Cos(Math.Asin(absY / hypot)));
                    }

                    if (offset > 2.0f)
                    {
                        if (!camera.OutOfView)
                        {
                            Vec3 gap = new Vec3(wish.x - camera.Body.Pos.x,
                                                wish.y - camera.Body.Pos.y,
                                                wish.z - camera.Body.Pos.z);

                            Vec3 halfGap = Vec3.ScaleTo(gap, 0.5f);

                            camera.Body.Pos.x = halfGap.x + camera.Body.Pos.x;
                            camera.Body.Pos.y = halfGap.y + camera.Body.Pos.y;
                            camera.Body.Pos.z = halfGap.z + camera.Body.Pos.z;

                            Vec3 raw = facing;
                            Vec3 chaseDir = Vec3.ScaleTo(raw, body.Speed);

                            pullVector.x = chaseDir.x + aTarget.x;
                            pullVector.y = chaseDir.y + pullVector.y;
                            pullVector.z += chaseDir.z;

                            Vec3 backGap = Vec3.ScaleTo(facing, -camera.GetBungeeLength());

                            Vec3 chasePos = new Vec3(backGap.x + pullVector.x,
                                                     backGap.y + pullVector.y,
                                                     pullVector.z + backGap.z);

                            gap = new Vec3(chasePos.x - camera.Body.Pos.x,
                                           chasePos.y - camera.Body.Pos.y,
                                           chasePos.z - camera.Body.Pos.z);

                            camera.Body.Vel = camera.Body.Vel + Vec3.ScaleTo(gap, 0.25f);
                        }
                    }
                    else
                    {
                        camera.OutOfView = false;
                    }
                }

                float scaleX = offsetScales.x;

                Vec3 side = Vec3.Zero;
                Vec3 dir = Vec3.Zero;
                Vec3 up = new Vec3(0.0f, 0.0f, CameraConstants.FracFull);

                if (scaleX != 0.0f || offsetScales.y != 0.0f || offsetScales.z != 0.0f)
                {
                    dir = camera.Body.VelDir;

                    side = new Vec3(
                        dir.y * CameraConstants.FracFull - dir.z * 0.0f,
                        dir.z * 0.0f - dir.x * CameraConstants.FracFull,
                        dir.x * 0.0f - dir.y * 0.0f);

                    side = Vec3.ScaleTo(side, scaleX);
                    dir = Vec3.ScaleTo(dir, offsetScales.y);
                    up = Vec3.ScaleTo(up, offsetScales.z);
                }

                Vec3 sum = new Vec3(
                    dir.x + side.x + camera.Body.Pos.x,
                    dir.y + camera.Body.Pos.y + side.y,
                    up.z + camera.Body.Pos.z + dir.z + side.z);

                Vec3 scaled = Vec3.ScaleTo(body.SurfNormal, CameraConstants.MinHeight);

                Vec3 nearRay;
                Vec3 farRay;
                EllipsoidStatics.CamTrack.IntersectRay(scaled, sum, out nearRay, out farRay);

                sum = Vec3.FindClosest(sum, nearRay, farRay);

                Vec3 final = new Vec3(
                    aTarget.x - (scaled.x + sum.x),
                    aTarget.y - (scaled.y + sum.y),
                    aTarget.z - (scaled.z + sum.z));

                Vec3 normalizedFinal;
                if (MathOps.NormalizeTo(final, out normalizedFinal))
                {
                    final = normalizedFinal;
                }

                double follow = camera.GetBungeeLength();
                final = Vec3.ScaleTo(final, (float)follow);

                camera.Body.Pos = new Vec3(aTarget.x - final.x, aTarget.y - final.y, aTarget.z - final.z);
            }

            ITrackedEntity secondary = camera.GetSecondaryTrackedObject();

            if (secondary != null)
            {
                if (secondary.Slot == ObjectConstants.SlotRok)
                {
                    AttachPak holder = rokAttach != null ? rokAttach() : null;
                    if (holder != null && holder.Slot < ObjectConstants.PlayerObjectCount)
                    {
                        secondary = slotLookup(holder.Slot);
                    }
                }

                if (secondary != null)
                {
                    Vec3 secondaryPos = secondary.Body.Pos;

                    Vec3 side = new Vec3(secondaryPos.x - camera.Body.Pos.x,
                                         secondaryPos.y - camera.Body.Pos.y,
                                         secondaryPos.z - camera.Body.Pos.z);

                    Vec3 normalizedSide;
                    if (MathOps.NormalizeTo(side, out normalizedSide))
                    {
                        side = normalizedSide;
                    }

                    Vec3 dir = new Vec3(side.x + vTrack.x, side.y + vTrack.y, side.z + vTrack.z);
                    Vec3 normalizedDir;
                    if (MathOps.NormalizeTo(dir, out normalizedDir))
                    {
                        dir = normalizedDir;
                    }
                    camera.Body.VelDir = dir;
                }
            }
            else if (camera.Countdown != 0)
            {
                if (camera.SwingChosen)
                {
                    camera.Body.VelDir = camera.LastDir;
                }

                Vec3 gap = camera.Body.VelDir;
                gap.z = 0.0f;

                Vec3 normalizedGap;
                if (MathOps.NormalizeTo(gap, out normalizedGap))
                {
                    gap = normalizedGap;
                }

                Vec3 wish = vLook;
                wish.z = 0.0f;

                Vec3 normalizedWish;
                if (MathOps.NormalizeTo(wish, out normalizedWish))
                {
                    wish = normalizedWish;
                }

                float angle = (float)Math.Acos(gap.x * wish.x + (wish.y * gap.y + wish.z * gap.z));

                int steps = Math.Min(SwingSteps, (int)camera.Countdown);
                float count = steps;

                float stepAngle = angle / count;

                float dirAngle = (float)Math.Acos(gap.x * CameraConstants.FracFull + (gap.y + gap.z) * 0.0f);

                if (gap.y < 0.0f)
                {
                    dirAngle = (MathOps.Pi + MathOps.Pi) - dirAngle;
                }

                float lookAngle = (float)Math.Acos(wish.x * CameraConstants.FracFull + (wish.y + wish.z) * 0.0f);

                if (wish.y < 0.0f)
                {
                    lookAngle = (MathOps.Pi + MathOps.Pi) - lookAngle;
                }

                if (!camera.SwingChosen)
                {
                    if (dirAngle > lookAngle)
                    {
                        angle = dirAngle - stepAngle;
                        camera.SwingBack = true;
                    }
                    else
                    {
                        angle = dirAngle + stepAngle;
                        camera.SwingBack = false;
                    }

                    camera.SwingChosen = true;
                }
                else if (dirAngle > lookAngle)
                {
                    angle = dirAngle - stepAngle;
                }
                else
                {
                    angle = dirAngle + stepAngle;
                }

                Vec3 final = new Vec3((float)Math.Cos(angle), (float)Math.Sin(angle), 0.0f);

                Vec3 normalizedFinal;
                if (MathOps.NormalizeTo(final, out normalizedFinal))
                {
                    final = normalizedFinal;
                }

                gap = camera.Body.VelDir;
                Vec3 normalizedGap2;
                if (MathOps.NormalizeTo(gap, out normalizedGap2))
                {
                    gap = normalizedGap2;
                }

                wish = vLook;
                Vec3 normalizedWish2;
                if (MathOps.NormalizeTo(wish, out normalizedWish2))
                {
                    wish = normalizedWish2;
                }

                final.z = (wish.z - gap.z) / count + gap.z;

                camera.Body.VelDir = final;
                Vec3 normalizedDir3;
                if (MathOps.NormalizeTo(camera.Body.VelDir, out normalizedDir3))
                {
                    camera.Body.VelDir = normalizedDir3;
                }

                camera.LastDir = camera.Body.VelDir;
            }
            else
            {
                camera.Body.VelDir = vLook;
            }

            camera.UpdateSmoothing();
            return true;
        }
    }

    // src/planvm/cupdateshotpower.cpp CUpdateShotPower::Process (0x004124a0): the helmet camera's
    // meter rises at (power rating + 12) * 0.004 per millisecond and caps at 100; Bind sets it to 1.
    public static class HeadCamShotPower
    {
        public static int Amount(uint startedMs, uint nowMs, float shotPowerRating)
        {
            float amount = (float)(nowMs - startedMs) * (shotPowerRating + CameraConstants.ShotPowerStatBias)
                           * CameraConstants.ShotPowerRate;
            int value = (int)amount;
            if (value > CameraConstants.ShotPowerMax)
            {
                value = CameraConstants.ShotPowerMax;
            }
            return value;
        }
    }

    public static class CameraStateExtensions
    {
        public static float GetBungeeLength(this CameraState camera)
        {
            return camera.BungeeLength;
        }
    }

    // The five camera tracking offsets, from src/engine/game.cpp (g_vBirdsEyeTrackOffset,
    // g_vFirstPersonTrackOffset, g_vOverShoulderTrackOffset, g_vOverShoulderRokTrackOffset,
    // g_vFallTrackOffset) and src/globals.h g_apTrackOffsets. The values are loaded from
    // Data/DftKeybd.cfg by src/app/config.cpp; the loader hook accepts them as plain data.
    public enum CameraOffsetMode
    {
        BirdsEye = 0,
        FirstPerson = 1,
        OverShoulder = 2,
        OverShoulderRok = 3,
        Fall = 4,
        Count = 5,
    }

    public static class CameraOffsetModes
    {
        public static readonly Vec3[] Offsets = new Vec3[(int)CameraOffsetMode.Count];

        // planactiontracking.cpp SetTrackingOffset (0x004019a0): a marker index copies its three
        // floats; otherwise the three arguments are whole units.
        public static bool SetTrackingOffset(CameraState camera, CameraOffsetMode mode)
        {
            Vec3 offset = Offsets[(int)mode];
            camera.AxisScale = offset;
            return true;
        }

        // planactiontracking.cpp IncrementTrackingOffset (0x00401a40): the marker path adds whole
        // floats, the numeric path adds tenths.
        public static void IncrementTrackingOffset(CameraState camera, CameraOffsetMode mode)
        {
            Vec3 offset = Offsets[(int)mode];
            camera.AxisScale = new Vec3(camera.AxisScale.x + offset.x,
                                        camera.AxisScale.y + offset.y,
                                        camera.AxisScale.z + offset.z);
        }

        public static void IncrementTrackingOffsetTenths(CameraState camera, float x, float y, float z)
        {
            camera.AxisScale = new Vec3(camera.AxisScale.x + x * 0.1f,
                                        camera.AxisScale.y + y * 0.1f,
                                        camera.AxisScale.z + z * 0.1f);
        }

        // planactiontracking.cpp SetTrackingOffset's whole-unit branch.
        public static void SetTrackingOffsetUnits(CameraState camera, float x, float y, float z)
        {
            camera.AxisScale = new Vec3(x, y, z);
        }

        // planactiontracking.cpp InitRearViewPos (0x00402070): leads the tracked object by three
        // frames of its velocity.
        public static Vec3 RearViewTarget(PhysicsBodyState trackedBody, Vec3 trackedPos)
        {
            return new Vec3(CameraConstants.RearViewLeadFrames * trackedBody.Vel.x + trackedPos.x,
                            CameraConstants.RearViewLeadFrames * trackedBody.Vel.y + trackedPos.y,
                            CameraConstants.RearViewLeadFrames * trackedBody.Vel.z + trackedPos.z);
        }

        // planactiontracking.cpp SetBungeeLength (0x00401c40): whole feet in tenths.
        public static void SetBungeeLength(CameraState camera, int tenths)
        {
            camera.BungeeLength = tenths * 0.1f;
        }

        public static void IncrementBungeeLength(CameraState camera, int tenths)
        {
            camera.BungeeLength = tenths * 0.1f + camera.BungeeLength;
        }
    }
}
