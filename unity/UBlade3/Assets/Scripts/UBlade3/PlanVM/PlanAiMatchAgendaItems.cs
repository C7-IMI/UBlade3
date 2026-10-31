using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N13 agenda items 8 CAvoidObjects (src/planvm/cavoidobjects.cpp, Process HYPERX 0x0040f9c0)
    /// and 15 CRelaxToPosition (src/planvm/crelaxtoposition.cpp, Process HYPERX 0x00411e60).
    /// The exact documented accessors/constants are used; their writes land on the plan mirror and
    /// MatchPlanAi applies the position, heading and smoothVel accumulation to the real Simulation
    /// body. The original Release phase (netTarget) is not ticked by this mock's one-pass agenda, and
    /// that is reported as a gap rather than simulated.
    /// </summary>
    public static class PlanAiMatchAgendaItems
    {
        /// <summary>Off for pinned legacy regression suites; enabled by the N13/default roster.</summary>
        public static bool Enabled;

        public static int[] ImplementedIds()
        {
            return new int[] { 8, 15 };
        }

        public static bool TryAdd(PlanAiHost host, int agendaItemId, PlanNode node, PlanBehavior behavior, int nArg)
        {
            if (!Enabled || host == null)
            {
                return false;
            }

            if (agendaItemId == 8)
            {
                PlanAiAvoidObjects item = new PlanAiAvoidObjects();
                item.BindWithNode(host, behavior, node, nArg);
                host.World.Log("agenda-add CAvoidObjects valid=" + (item.Valid ? "True" : "False")
                               + " swerve=" + item.SwerveSlot + " draw=" + item.DrawSlot
                               + " range=" + item.Range.ToString("0.###") + " strength=" + item.Strength.ToString("0.###"));
                if (item.Valid)
                {
                    item.Activate();
                }

                return true;
            }

            if (agendaItemId == 15)
            {
                PlanAiRelaxToPosition item = new PlanAiRelaxToPosition();
                item.BindWithNode(host, behavior, node, nArg);
                host.World.Log("agenda-add CRelaxToPosition valid=" + (item.Valid ? "True" : "False")
                               + " dest=(" + item.Dest.X.ToString("0.###") + "," + item.Dest.Y.ToString("0.###")
                               + "," + item.Dest.Z.ToString("0.###") + ")");
                if (item.Valid)
                {
                    item.Activate();
                }

                return true;
            }

            return false;
        }

        public static bool TryRemove(PlanAiHost host, int agendaItemId)
        {
            if (!Enabled || host == null || host.Actor == null)
            {
                return false;
            }

            if (agendaItemId != 8 && agendaItemId != 15)
            {
                return false;
            }

            int removed = 0;
            PlanAiAgendaItem item = host.Actor.Observers;

            while (item != null)
            {
                PlanAiAgendaItem next = item.Next;

                if (item.Kind == agendaItemId)
                {
                    item.Detach();
                    removed++;
                }

                item = next;
            }

            host.World.Log("agenda-remove handled id=" + agendaItemId + " removed=" + removed);

            return true;
        }
    }

    /// <summary>AgendaItem 8, CAvoidObjects. A bounded exact port of the documented steering branch.</summary>
    public sealed class PlanAiAvoidObjects : PlanAiAgendaItem
    {
        private const float DefaultRange = 80.0f;

        private const float DefaultStrength = -6.5f;

        private const int WrongSideLimit = 0x14;

        public int SwerveSlot = -1;

        public int DrawSlot = -1;

        public bool ReleaseOnRange;

        public float Range = DefaultRange;

        public float Strength = DefaultStrength;

        public bool Valid;

        private PlanAiHost host;

        public override void BindWithNode(PlanAiHost host, PlanBehavior behavior, PlanNode node, int nArg)
        {
            base.Bind(host, behavior);
            this.host = host;
            Kind = 8;
            Persistent = true;
            Ready = true;
            SwerveSlot = -1;
            DrawSlot = -1;
            ReleaseOnRange = false;
            Range = DefaultRange;
            Strength = DefaultStrength;

            if (node == null)
            {
                Valid = false;

                return;
            }

            if (node.ArgCount == 3)
            {
                SwerveSlot = Slot(host, node, 1, -1);
                DrawSlot = Slot(host, node, 2, -1);
            }
            else if (node.ArgCount == 5)
            {
                SwerveSlot = node.Args[1].Word.Value == PlanWord.TokenMessageData
                    ? nArg
                    : Slot(host, node, 1, -1);

                if (node.Args[2].Word.Value != PlanWord.TokenMessageData)
                {
                    ReleaseOnRange = node.Args[2].IntValue != 0;
                }

                Range = node.Args[3].Word.AsFloat;
                Strength = node.Args[4].Word.AsFloat;
            }

            Valid = Owner != null
                    && (SwerveSlot >= 0 || DrawSlot >= 0)
                    && (host.World.GetObject(SwerveSlot) != null || host.World.GetObject(DrawSlot) != null);
        }

        public override void Process(int nArg)
        {
            if (!Valid || Owner == null || Owner.Body == null)
            {
                return;
            }

            PlanAiAgendaState state = PlanAiAgendaStateStore.Get(Owner);
            PlanAiObject swerve = SwerveSlot >= 0 ? host.World.GetObject(SwerveSlot) : null;
            PlanAiObject draw = DrawSlot >= 0 ? host.World.GetObject(DrawSlot) : null;
            PlanAiPhysicsBody body = Owner.Body;
            float invDelta = PlanAiMatchContext.EngineInvDelta;
            float speed = (float)Math.Sqrt(PlanAiVec3.Dot(body.Velocity, body.Velocity)) * invDelta;
            bool push = body.Friction < 0.8f && speed >= 2.0f;
            int goal = PlanAiMatchContext.GetTargetGoal(Owner.Slot);
            float sign = goal == PlanOpcodes.SlotNgoal ? 1.0f : -1.0f;
            float deltaX = 0.0f;
            float deltaY = 0.0f;
            float length = 0.0f;
            PlanAiVec3 axis = new PlanAiVec3(0.0f, 0.0f, 1.0f);

            if (swerve != null && Owner.GetDistIfFacing(swerve, 0.0f) != 0.0f)
            {
                float targetX = swerve.Body.Position.X - Owner.Body.Position.X;
                float targetY = swerve.Body.Position.Y - Owner.Body.Position.Y;

                deltaX = targetX;
                deltaY = targetY;

                if ((float)(Math.Abs((int)deltaY) + Math.Abs((int)deltaX)) < Range)
                {
                    PlanAiVec3 gap = new PlanAiVec3(
                        targetX, targetY, swerve.Body.Position.Z - Owner.Body.Position.Z);
                    length = (float)Math.Sqrt(PlanAiVec3.Dot(gap, gap));
                    PlanAiVec3 direction = length > 1e-7f ? gap * (1.0f / length) : new PlanAiVec3(0.0f, 0.0f, 0.0f);
                    PlanAiVec3 heading = body.VelocityDirection;
                    PlanAiVec3 side;

                    if (direction.Y * heading.X - direction.X * heading.Y > 0.0f)
                    {
                        side = new PlanAiVec3(
                            axis.Y * heading.Z - axis.Z * heading.Y,
                            axis.Z * heading.X - axis.X * heading.Z,
                            axis.X * heading.Y - axis.Y * heading.X);
                    }
                    else
                    {
                        side = new PlanAiVec3(
                            axis.Z * heading.Y - axis.Y * heading.Z,
                            axis.X * heading.Z - axis.Z * heading.X,
                            axis.Y * heading.X - axis.X * heading.Y);
                    }

                    float scale = length > 0.5f ? Strength / length : 2.0f * Strength;
                    scale = PlanAiMatchContext.EngineDeltaSquared * scale * 60.0f;

                    if (push)
                    {
                        state.SmoothVelAccum += (side + direction) * scale;
                        state.AvoidAdvanceEvents++;
                    }
                }
            }

            bool wrongSide = sign * deltaY < 0.0f;

            if (draw != null && Owner.GetDistIfFacing(draw, 0.0f) != 0.0f)
            {
                float targetX = draw.Body.Position.X - Owner.Body.Position.X;
                float targetY = draw.Body.Position.Y - Owner.Body.Position.Y;

                if ((float)(Math.Abs((int)targetY) + Math.Abs((int)targetX)) < Range)
                {
                    PlanAiVec3 gap = new PlanAiVec3(
                        targetX, targetY, draw.Body.Position.Z - Owner.Body.Position.Z);
                    length = (float)Math.Sqrt(PlanAiVec3.Dot(gap, gap));
                    PlanAiVec3 direction = length > 1e-7f ? gap * (1.0f / length) : new PlanAiVec3(0.0f, 0.0f, 0.0f);
                    float scale = length > 0.5f ? Strength / length : 2.0f * Strength;
                    scale = scale * PlanAiMatchContext.EngineDeltaSquared * 30.0f;

                    if (push)
                    {
                        state.SmoothVelAccum += direction * scale;
                    }
                }
            }

            if (wrongSide && sign * deltaY < 0.0f)
            {
                state.WrongSideFrames++;

                if (state.WrongSideFrames > WrongSideLimit)
                {
                    host.World.PostSceneEvent(Owner.Slot, 55, 0);
                    state.WrongSideFrames = 0;
                    state.BreakawayEvents++;
                }
            }

            if (PlanAiVec3.Dot(state.SmoothVelAccum, body.VelocityDirection) < 0.3f)
            {
                host.World.PostSceneEvent(Owner.Slot, 54, 0);
                state.BlockedEvents++;
            }

            if (ReleaseOnRange && length > Range)
            {
                Detach();
            }

            if (Next != null)
            {
                Next.Process(nArg);
            }
        }

        private static int Slot(PlanAiHost host, PlanNode node, int index, int fallback)
        {
            PlanWord word = node.Args[index].Word;

            if (word.IsPronoun)
            {
                return host.ArgResolver.ResolvePronoun(word.Low, word);
            }

            return node.Args[index].IntValue;
        }
    }

    /// <summary>AgendaItem 15, CRelaxToPosition. Exact rate table and Process clamp; camera bias is skipped.</summary>
    public sealed class PlanAiRelaxToPosition : PlanAiAgendaItem
    {
        private static readonly float[] RelaxRateByFrameRate =
        {
            0.75f, 0.29f, 0.159f, 0.1091f, 0.0829f, 0.067f, 0.056f, 0.0483f,
            0.0424f, 0.0377f, 0.035f, 0.031f, 0.0285f, 0.0263f, 0.024f,
        };

        public PlanAiVec3 Dest;

        public int TargetSlot = -1;

        public bool CameraBias;

        public bool Valid;

        private PlanAiHost host;

        public override void BindWithNode(PlanAiHost host, PlanBehavior behavior, PlanNode node, int nArg)
        {
            base.Bind(host, behavior);
            this.host = host;
            Kind = 15;
            Persistent = true;
            Ready = true;
            TargetSlot = -1;
            Dest = new PlanAiVec3(0.0f, 0.0f, 0.0f);

            if (node == null)
            {
                Valid = false;

                return;
            }

            CameraBias = node.Args[1].IntValue != 0;

            if (node.ArgCount == 4)
            {
                PlanWord markerWord = node.Args[2].Word;
                int marker = markerWord.IsPronoun
                    ? host.ArgResolver.ResolvePronoun(markerWord.Low, markerWord)
                    : node.Args[2].IntValue;
                int target = Slot(host, node, 3, -1);

                if (PlanAiMatchContext.MarkerPosition == null || !PlanAiMatchContext.MarkerResolver(marker))
                {
                    Valid = false;

                    return;
                }

                Dest = PlanAiMatchContext.MarkerPosition(marker);
                TargetSlot = target;
                Valid = true;

                return;
            }

            if (node.ArgCount == 5)
            {
                float x = (float)node.Args[2].IntValue;
                float y = (float)node.Args[3].IntValue;
                float z = 0.0f;
                PlanAiMatchContext.TryTerrainZ(x, y, out z);
                Dest = new PlanAiVec3(x, y, z);
                TargetSlot = Slot(host, node, 4, -1);
                Valid = true;

                return;
            }

            Valid = false;
        }

        public override void Process(int nArg)
        {
            if (!Valid || Owner == null || Owner.Body == null)
            {
                return;
            }

            PlanAiAgendaState state = PlanAiAgendaStateStore.Get(Owner);
            int bucket = (int)PlanAiMatchContext.EngineInvDelta;
            int clamped = Math.Min(0x34, bucket);
            clamped >>= 2;

            if (clamped < 0)
            {
                clamped = 0;
            }

            if (clamped >= RelaxRateByFrameRate.Length)
            {
                clamped = RelaxRateByFrameRate.Length - 1;
            }

            float rate = RelaxRateByFrameRate[clamped];

            if (CameraBias)
            {
                // The original applies the head camera's view fraction and the Checking rating here.
                // This mock has no head camera, so the camera-bias term is omitted and recorded.
                host.World.Log("agenda.CRelaxToPosition cameraBias omitted (no head camera)");
            }

            PlanAiVec3 gap = Dest - Owner.Body.Position;
            PlanAiVec3 step = gap * rate;
            float forward = PlanAiMatchContext.GetForwardSpeedDt(Owner.Slot);
            float stepLength = (float)Math.Sqrt(PlanAiVec3.Dot(step, step));

            if (forward > 0.0f && forward < stepLength)
            {
                step = step * (forward / stepLength);
            }

            Owner.Body.Position += step;
            state.Position = Owner.Body.Position;
            state.PositionDirty = true;
            state.RelaxAdvanceEvents++;

            if (TargetSlot >= 0)
            {
                PlanAiObject target = host.World.GetObject(TargetSlot);

                if (target != null)
                {
                    PlanAiVec3 heading = target.Body.Position - Owner.Body.Position;
                    float headingLength = (float)Math.Sqrt(PlanAiVec3.Dot(heading, heading));

                    if (headingLength > 1e-7f)
                    {
                        state.Heading = heading * (1.0f / headingLength);
                        state.HeadingDirty = true;
                    }
                }
            }

            if (Next != null)
            {
                Next.Process(nArg);
            }
        }

        private static int Slot(PlanAiHost host, PlanNode node, int index, int fallback)
        {
            PlanWord word = node.Args[index].Word;

            if (word.IsPronoun)
            {
                return host.ArgResolver.ResolvePronoun(word.Low, word);
            }

            return node.Args[index].IntValue;
        }
    }
}
