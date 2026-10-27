// UBlade3 - exact HyperBlade (1996) simulation core.
// ArenaState: src/engine/arenastate.h (0x20-byte record) with the ellipsoid, the slot ids, the
// Rok's definition and the loader hooks. Match markers: src/engine/game.cpp InitMatchMarkers
// (0x0047d1c0). Goal frame geometry: src/physics/collisioncheck.cpp 0x004521d0 and
// src/engine/object.cpp ComputePoleGateNormal (0x00404670).
// Original drome/team data is read by the data port; this file only accepts plain data structs.

using System;
using System.Collections.Generic;

namespace UBlade3.Sim
{
    // src/globals.h GameObjectType, as DATA/ENUMIDS.H numbers object types.
    public enum GameObjectType
    {
        Null = 0,
        Dromer = 1,
        Rok = 2,
        Drome = 3,
        Ramp = 4,
        NGoal = 5,
        SGoal = 6,
        MainCamera = 7,
        HeadCam = 8,
        StartGun = 9,
        Turnstile = 10,
        Multiplier = 11,
        LaserHurdle = 12,
        GreenRandomizer = 13,
        RedRandomizer = 14,
        God = 15,
        SoundMan = 16,
        LimboPole = 17,
        Hurdle = 18,
        FastLane = 19,
        Explosion = 20,
        Armory = 21,
        KeyBoard = 22,
        GamePad = 23,
        Joystick = 24,
        Mouse = 25,
        SlowLane = 26,
        Mine = 27,
        Swarf = 28,
        Coach = 29,
        BodyPart = 30,
        Count = 31,
    }

    // src/engine/objectdef.h ObjectDef, reduced to the fields the simulation reads. The data port
    // fills these from DATA/GAME.CFG; no file parsing happens here.
    public sealed class ObjectMotionParams
    {
        public string ModelName = string.Empty;
        public int Type;
        public int TypeId;
        public string MaterialName = string.Empty;
        public int RatingsMode;      // RATINGS_FIELD 1 / RATINGS_GOALIE 2 / RATINGS_SPECIAL_GOALIE 3
        public Vec3 Position = Vec3.Zero;
        public float Acceleration;   // m_flAccel
        public float Speed;          // m_flSpeed
        public float AzimuthDegrees; // m_flAzimuth
        public float ElevationDegrees; // m_flElevation
        public float Friction;       // m_flFriction
        public float Elasticity;     // m_flElasticity
        public string[] PlanNames = new string[0];
        public float[] Ratings = new float[0]; // the selected difficulty's table
    }

    // One object a drome places: the loader hook for DATA/GAME.CFG's DefineObject/Cmd_SetMotionParams.
    public struct DromeObjectPlacement
    {
        public int ModelId;
        public GameObjectType Type;
        public Vec3 Position;
        public float AzimuthDegrees;
        public float ElevationDegrees;
        public float Speed;
        public float Acceleration;
        public float Friction;
        public float Elasticity;
        public string ModelName;
    }

    // A collision bubble the loader hands over, from Cmd_SetCollisionObject / BubbleCmd
    // (src/physics/bubblecmd.h). Components: -1 the object's own transform, -2 a world centre,
    // 0 and up a bone.
    public struct CollisionBubbleData
    {
        public short Slot;
        public short Shape;        // BUBBLE_SHAPE_SPHERE 1, ATTACHED 4, CAGED 5
        public short Component;
        public float Radius;
        public Vec3 Centre;
        public int AttachSlot;
        public Vec3 AttachOffset;
        public float AttachTransformScale;
    }

    // Everything a drome load produces. The data port converts GAME.CFG/HTM records into this.
    public sealed class DromeDefinition
    {
        public string Name = string.Empty;
        public int ArenaIndex;
        public readonly List<DromeObjectPlacement> Placements = new List<DromeObjectPlacement>();
        public readonly List<CollisionBubbleData> Bubbles = new List<CollisionBubbleData>();
        public readonly List<int> SpawnSlots = new List<int>();
        public ObjectMotionParams RokDefinition;
        public GoalDefinition NorthGoal = GoalDefinition.CreateNorth();
        public GoalDefinition SouthGoal = GoalDefinition.CreateSouth();
        public readonly List<MultiplierPlacement> Multipliers = new List<MultiplierPlacement>();
    }

    // The goal's plan/data pair: the score region is the COB_*GOAL_PORT bubble from the loader, and
    // the frame geometry below is the shared collisioncheck.cpp box set.
    public sealed class GoalDefinition
    {
        public int Slot;          // SLOT_NGOAL 7 or SLOT_SGOAL 8
        public float SideSign;    // +1 north (y positive), -1 south
        public string GoalPlan;

        public static GoalDefinition CreateNorth()
        {
            return new GoalDefinition { Slot = ObjectConstants.SlotNGoal, SideSign = 1.0f };
        }

        public static GoalDefinition CreateSouth()
        {
            return new GoalDefinition { Slot = ObjectConstants.SlotSGoal, SideSign = -1.0f };
        }
    }

    // id_MOD_Multiplier's placement; the trigger itself is CollisionCheck::DromerMultiplierCollision.
    public struct MultiplierPlacement
    {
        public int Slot;
        public Vec3 Position;
        public float AzimuthDegrees;
    }

    // src/engine/arenastate.h. The original's record is 0x20 bytes; the counts and slot ids are kept
    // as the loader wrote them (m_wCount at 0x10, m_pnSlotIds at 0x14).
    public sealed class ArenaState
    {
        public int[] Unused = new int[4];       // m_anUnused[4]
        public short Count;                      // m_wCount, 0x10
        public int[] SlotIds = new int[0];       // m_pnSlotIds, 0x14
        public CollisionEllipsoid Ellipsoid;     // m_pEllipsoid, 0x18
        public ObjectMotionParams RokDefinition; // m_pRokDef, 0x1c
        public DromeDefinition Drome;

        // arenastate.h ArenaState::ArenaState(): the ellipsoid is the only member the memset leaves.
        public ArenaState()
        {
            Count = 0;
            SlotIds = new int[0];
            Ellipsoid = EllipsoidStatics.Arena;
            RokDefinition = null;
            Drome = null;
        }

        // Loader hook. The data port calls it once per drome; no file access happens here.
        public void ApplyDromeDefinition(DromeDefinition definition)
        {
            Drome = definition;
            Count = (short)definition.Placements.Count;

            int[] ids = new int[definition.Placements.Count];
            for (int i = 0; i < definition.Placements.Count; i++)
            {
                ids[i] = definition.Placements[i].ModelId;
            }
            SlotIds = ids;
            RokDefinition = definition.RokDefinition;
        }

        public void Clear()
        {
            Count = 0;
            SlotIds = new int[0];
            Drome = null;
            RokDefinition = null;
        }
    }

    // The eight markers Game::InitMatchMarkers writes. The south surface marker asks the ellipsoid
    // for its height at y = +6, not -6 (shipped bug, kept).
    public struct ArenaMarkerSet
    {
        public Vec3 GoalMouthSouth;
        public Vec3 GoalMouthNorth;
        public Vec3 SurfaceCentre;
        public Vec3 SurfaceYMinus;
        public Vec3 SurfaceYPlus;
        public Vec3 GoalSouth;
        public Vec3 GoalNorth;
        public Vec3 RimEast;
    }

    public static class ArenaMarkers
    {
        // src/engine/game.cpp Game::InitMatchMarkers (0x0047d1c0).
        public static ArenaMarkerSet Compute(CollisionEllipsoid arena)
        {
            ArenaMarkerSet markers = new ArenaMarkerSet();
            float radiusX = arena.GetRadiusX();
            float radiusY = arena.GetRadiusY();

            markers.RimEast.x = radiusX + GoalConstants.MarkerRimEastXOffset;

            markers.GoalNorth.y = radiusY * GoalConstants.MarkerGoalNorthYFactor;

            markers.RimEast.y = 0.0f;
            markers.RimEast.z = GoalConstants.MarkerRimEastZ;
            markers.GoalNorth.z = 0.0f;
            markers.GoalNorth.x = 0.0f;
            markers.GoalSouth.z = 0.0f;
            markers.GoalSouth.x = 0.0f;

            markers.GoalSouth.y = radiusY * GoalConstants.MarkerGoalSouthYFactor;

            markers.SurfaceYPlus.y = GoalConstants.MarkerSurfaceY;
            markers.SurfaceYPlus.x = radiusX - GoalConstants.MarkerSurfaceXInset;
            markers.SurfaceYPlus.z = arena.GetZ(radiusX - GoalConstants.MarkerSurfaceXInset, GoalConstants.MarkerSurfaceY);

            markers.SurfaceYMinus.x = radiusX - GoalConstants.MarkerSurfaceXInset;
            markers.SurfaceYMinus.y = -GoalConstants.MarkerSurfaceY;
            // The shipped call passes +6 here; -6 would give a different z.
            markers.SurfaceYMinus.z = arena.GetZ(radiusX - GoalConstants.MarkerSurfaceXInset, GoalConstants.MarkerSurfaceY);

            markers.SurfaceCentre.x = radiusX - GoalConstants.MarkerSurfaceCentreInset;
            markers.SurfaceCentre.y = 0.0f;
            markers.SurfaceCentre.z = arena.GetZ(radiusX - GoalConstants.MarkerSurfaceCentreInset, 0.0f);

            markers.GoalMouthNorth.y = radiusY - GoalConstants.MarkerGoalMouthYInset;
            markers.GoalMouthSouth.z = GoalConstants.MarkerGoalMouthZ;
            markers.GoalMouthSouth.x = 0.0f;
            markers.GoalMouthNorth.x = 0.0f;
            markers.GoalMouthNorth.z = GoalConstants.MarkerGoalMouthZ;
            markers.GoalMouthSouth.y = GoalConstants.MarkerGoalMouthYInset - radiusY;

            return markers;
        }
    }

    // An axis-aligned box in the goal's own frame (x across the mouth, y up, z through the mouth).
    public struct GoalBox
    {
        public Vec3 Min;
        public Vec3 Max;
        public bool Valid;

        public static GoalBox Create(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            return new GoalBox
            {
                Min = new Vec3(minX, minY, minZ),
                Max = new Vec3(maxX, maxY, maxZ),
                Valid = true,
            };
        }

        public bool Contains(Vec3 point)
        {
            return Valid
                && Min.x <= point.x && point.x <= Max.x
                && Min.y <= point.y && point.y <= Max.y
                && Min.z <= point.z && point.z <= Max.z;
        }
    }

    // The goal frame the collision checks build. The origin is the goal mouth centre on the ice;
    // y is world z (up) and z is the depth axis, as in Object::ComputePoleGateNormal's local frame.
    public sealed class GoalFrame
    {
        public GoalDefinition Definition;
        public GoalBox PostA;
        public GoalBox PostB;
        public GoalBox Mouth;
        public GoalBox UpperBar;
        public Vec3 MouthCentre;
        public ArenaMarkerSet Markers;

        // collisioncheck.cpp 0x004521d0: flInner = (span - frame) * 0.5, post boxes are
        // [flInner - frame/2, flInner + frame/2] with depth +-frame/2; the mouth box spans
        // +-flOuter; the upper bar is y [zone - frame, zone + 5].
        public static GoalFrame Create(GoalDefinition definition, ArenaMarkerSet markers)
        {
            GoalFrame frame = new GoalFrame();
            frame.Definition = definition;
            frame.Markers = markers;

            float frameWidth = GoalConstants.FrameWidth;
            float zoneHeight = GoalConstants.ZoneHeight;
            float inner = GoalConstants.PostInner;
            float postHalfFrame = frameWidth * 0.5f;
            float outer = GoalConstants.PostOuter;

            frame.PostA = GoalBox.Create(inner - postHalfFrame, 0.0f, -postHalfFrame,
                                         postHalfFrame + inner, zoneHeight, postHalfFrame);
            frame.PostB = GoalBox.Create(-inner - postHalfFrame, 0.0f, -postHalfFrame,
                                         postHalfFrame - inner, zoneHeight, postHalfFrame);
            frame.Mouth = GoalBox.Create(-outer, 0.0f, -postHalfFrame,
                                         outer, zoneHeight, postHalfFrame);
            frame.UpperBar = GoalBox.Create(-GoalConstants.SpanWidth * 0.5f,
                                            zoneHeight - frameWidth,
                                            -postHalfFrame,
                                            GoalConstants.SpanWidth * 0.5f,
                                            zoneHeight - frameWidth + GoalConstants.UpperBarExtraHeight,
                                            postHalfFrame);

            float mouthY = definition.SideSign > 0.0f ? markers.GoalMouthNorth.y : markers.GoalMouthSouth.y;
            frame.MouthCentre = new Vec3(0.0f, mouthY, 0.0f);
            frame.MouthCentre.z = GoalConstants.MarkerGoalMouthZ;
            return frame;
        }
    }

    // The drome load result plus the shared arena state the simulation reads.
    public sealed class ArenaBuild
    {
        public ArenaState State = new ArenaState();
        public ArenaMarkerSet Markers;
        public GoalFrame NorthGoal;
        public GoalFrame SouthGoal;

        // Loader hook. Idempotent: loading a drome replaces the record, as LoadArena did.
        public void Load(DromeDefinition definition, CollisionEllipsoid arenaEllipsoid)
        {
            State.ApplyDromeDefinition(definition);
            State.Ellipsoid = arenaEllipsoid != null ? arenaEllipsoid : EllipsoidStatics.Arena;
            Markers = ArenaMarkers.Compute(State.Ellipsoid);
            NorthGoal = GoalFrame.Create(definition.NorthGoal, Markers);
            SouthGoal = GoalFrame.Create(definition.SouthGoal, Markers);
        }

        // The goal a team shoots at: PlayerObject::m_wTargetGoalSlot is SLOT_NGOAL or SLOT_SGOAL;
        // src/engine/playerobject.cpp sets it from the side (three below it is that goal's keeper).
        public GoalFrame GoalForSlot(int targetGoalSlot)
        {
            return targetGoalSlot == ObjectConstants.SlotNGoal ? NorthGoal : SouthGoal;
        }
    }

    internal static class ArenaMarkerExtensions
    {
        // Kept tiny and internal: ArenaMarkers.Compute always fills every field.
        public static bool MarkersEmpty(this ArenaMarkerSet markers)
        {
            return markers.GoalMouthNorth.y == 0.0f && markers.GoalMouthSouth.y == 0.0f;
        }
    }
}
