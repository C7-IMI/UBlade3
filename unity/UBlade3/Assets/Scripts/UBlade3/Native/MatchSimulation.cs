using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Data;
using UBlade3.Sim;
using SimVec3 = UBlade3.Sim.Vec3;

namespace UBlade3.Native
{
    // The match's link to the exact UBlade3.Sim core: it binds the six original players and the drome
    // to a Simulation, places them from the original data, and steps the fixed 30 Hz tick with the
    // same input plumbing Simulation.InputForSlot documents. Nothing in UBlade3.Sim is modified by
    // this file; the scene only supplies the loader/placement hooks the engine's object construction
    // would have run.
    public sealed class MatchSimulation
    {
        public const int HumanSlot = ObjectConstants.SlotDromer0;

        // The AI is a labelled fallback, not the plan VM: every non-human outfield skater holds the
        // Forward binding and every goalie holds no input. The ADLIB plan VM (N5) is not ported, so
        // no original plan behaviour, interactor or collision bubble runs in this scene yet.
        public const string AiFallbackNote =
            "placeholder AI (plan VM not ported): non-human skaters hold Forward; goalies hold no input";

        // The goalie is placed at Game::InitMatchMarkers' goal-mouth marker instead of the HTM pos
        // behind the goal line; see MatchWorldData.InitialPosition.
        public const string GoaliePlacementNote =
            "goalies stand at the InitMatchMarkers goal-mouth markers; the plan-VM goalie agenda is not ported";

        public readonly MatchWorldData World;
        public readonly Simulation Sim;

        // Simulation.Players index -> the original definition/asset mapping.
        public readonly MatchPlayerSlot[] Slots = new MatchPlayerSlot[ObjectConstants.PlayerObjectCount];

        // The keyboard/script held mask for the human-controlled home skater (slot 0).
        public HeldBinding HumanInput;

        public bool AiFallbackActive = true;

        public int TickCount;

        public bool JumbotronRequested;

        public readonly List<GameEvent> Events = new List<GameEvent>();

        private SimVec3 humanStartPosition;

        public double MatchSeconds
        {
            get { return TickCount * MatchWorldData.FixedTickSeconds; }
        }

        public int ClockMilliseconds
        {
            get { return (int)Sim.Rules.Clock.TickMs; }
        }

        private MatchSimulation(MatchWorldData world)
        {
            World = world;

            MatchContext context = new MatchContext();
            context.PeriodLengthMs = ClockConstants.DefaultPeriodLengthMs;
            context.PlayerMode = MatchRules.PlayerModeExhibition;
            context.SimulatedSession = false;
            context.DragOff = false;
            context.Broadcast = gameEvent => Events.Add(gameEvent);
            context.OnJumbotronRequested = () => { JumbotronRequested = true; };

            Sim = new Simulation(context);
        }

        public static MatchSimulation Create(MatchWorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }

            MatchSimulation match = new MatchSimulation(world);
            match.Setup();
            return match;
        }

        private void Setup()
        {
            for (int slot = 0; slot < Slots.Length; slot++)
            {
                MatchPlayerSlot player = World.Players[slot];
                Slots[slot] = player;

                // PhysicsBody::BindToObject (HYPERX 0x00407780) through the simulation's loader hook.
                ObjectMotionParams motion = MatchWorldData.ToMotionParams(player.Definition);
                Sim.BindPlayer(slot, motion);

                // PlayerObject::PlayerObject (HYPERX 0x00474150): copy the difficulty rating table
                // into ObjectStats and apply the period-one ScaleTo.
                ApplyStats(slot, player);
            }

            if (World.SimDrome.RokDefinition != null)
            {
                Sim.BindRok(World.SimDrome.RokDefinition);
            }

            // GameEngine::SetArena + RestartMatch: the match begins playing.
            Sim.LoadDrome(World.SimDrome, true);

            PlaceInitialBodies();

            Sim.InputForSlot = slot => slot == HumanSlot ? HumanInput : AiInput(slot);
            humanStartPosition = Sim.Players[HumanSlot].Body.Pos;
        }

        private void ApplyStats(int slot, MatchPlayerSlot player)
        {
            PlayerState state = Sim.Players[slot];
            float[] ratings = player.Ratings;

            if (ratings == null)
            {
                return;
            }

            int count = player.IsGoalie ? Ratings.GoalieCount : Ratings.PlayerCount;

            for (int i = 0; i < count && i < ratings.Length; i++)
            {
                state.Stats.SetStat(i + ObjectConstants.RatingSlotOffset, ratings[i]);
            }

            // ObjectStats::ScaleTo as PlayerObject calls it at load (period 1, exhibition mode).
            state.Stats.ScaleTo(slot, Sim.Rules.Clock, false, 0.0f, false);
        }

        // The loader/placement hook: the engine's physics body starts at ObjectDef::m_vPos and the
        // glide behaviour snaps it to the arena shell on the first tick (PhysicsBody::InitOnSurface
        // / CGlideOnSurface). This scene does that snap once at build time with the exact
        // CollisionEllipsoid::GetZ and NormalAtPoint, preserving the ObjectDef azimuth as the
        // heading (ResetOnSurface would overwrite it with +x; InitOnSurface's projection is used).
        private void PlaceInitialBodies()
        {
            ArenaMarkerSet markers = Sim.Arena.Markers;
            CollisionEllipsoid arena = EllipsoidStatics.Arena;

            for (int slot = 0; slot < Slots.Length; slot++)
            {
                MatchPlayerSlot player = Slots[slot];
                PhysicsBodyState body = Sim.Players[slot].Body;

                SimVec3 spawn = MatchWorldData.InitialPosition(player, markers);
                float z = arena.GetZ(spawn.x, spawn.y);

                if (z > 0.0f)
                {
                    // Outside the ellipse: the shipped definition positions the goalie behind the
                    // goal line. Pull the goalie's marker a fraction inside so the shell answer is
                    // real; the marker already sits at radiusY - 3, so this only guards bad data.
                    spawn.y *= 0.98f;
                    z = arena.GetZ(spawn.x, spawn.y);

                    if (z > 0.0f)
                    {
                        z = 0.0f;
                    }
                }

                SimVec3 position = new SimVec3(spawn.x, spawn.y, z);
                body.Pos = position;
                body.PrevPos = position;
                body.Vel = SimVec3.Zero;
                body.SmoothVel = SimVec3.Zero;
                body.Axis = SimVec3.Zero;
                body.Speed = 0.0f;

                SimVec3 normal;
                if (arena.NormalAtPoint(position, out normal))
                {
                    body.SurfNormal = normal;
                }
                else
                {
                    body.SurfNormal = new SimVec3(0.0f, 0.0f, 1.0f);
                }

                // PhysicsBody::InitOnSurface projects velDir into the surface plane.
                float dot = body.SurfNormal.x * body.VelDir.x + body.SurfNormal.y * body.VelDir.y
                            + body.SurfNormal.z * body.VelDir.z;
                body.VelDir.x -= body.SurfNormal.x * dot;
                body.VelDir.y -= body.SurfNormal.y * dot;
                body.VelDir.z -= body.SurfNormal.z * dot;

                SimVec3 normalized;
                if (MathOps.NormalizeTo(body.VelDir, out normalized))
                {
                    body.VelDir = normalized;
                }

                body.VelDirLatched = body.VelDir;

                PlayerState state = Sim.Players[slot];
                state.PlanBehaviorKind = ObjectConstants.BehaviorGlide;
                state.AimPosition = HeadOf(position);
            }
        }

        private static SimVec3 HeadOf(SimVec3 position)
        {
            return new SimVec3(position.x, position.y, position.z + MatchWorldData.HeadHeight);
        }

        private HeldBinding AiInput(int slot)
        {
            if (!AiFallbackActive)
            {
                return HeldBinding.None;
            }

            MatchPlayerSlot player = Slots[slot];

            // The goalie plan agenda (CMonitorKeys MON_GOALIE_CTRL) is not ported; a goalie with no
            // input stands on its goal-mouth placement.
            if (player.IsGoalie)
            {
                return HeldBinding.None;
            }

            return HeldBinding.Forward;
        }

        // One fixed 30 Hz simulation tick. The original's GameEngine::PerFrameUpdate order is inside
        // Simulation.Tick; this wrapper only supplies the input mask and moves the camera target to
        // the head height the animation port cannot supply yet.
        public void Step()
        {
            Sim.Tick();
            TickCount++;

            for (int slot = 0; slot < Slots.Length; slot++)
            {
                PlayerState state = Sim.Players[slot];
                state.AimPosition = HeadOf(state.Body.Pos);
            }
        }

        // Steps while a deterministic input script drives the human slot. The script receives the
        // tick index before that tick runs; null means no human input for the whole run.
        public void RunTicks(int count, Func<int, HeldBinding> script)
        {
            for (int i = 0; i < count; i++)
            {
                HumanInput = script == null ? HeldBinding.None : script(TickCount);
                Step();
            }
        }

        public float HumanDisplacement()
        {
            SimVec3 now = Sim.Players[HumanSlot].Body.Pos;

            float x = now.x - humanStartPosition.x;
            float y = now.y - humanStartPosition.y;
            float z = now.z - humanStartPosition.z;

            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        public SimVec3 HumanPosition
        {
            get { return Sim.Players[HumanSlot].Body.Pos; }
        }

        public string DescribeSimulation()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_SIM ticks=").Append(TickCount.ToString(CultureInfo.InvariantCulture));
            text.Append(" seconds=").Append(MatchSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            text.Append(" clockMs=").Append(ClockMilliseconds.ToString(CultureInfo.InvariantCulture));
            text.Append(" period=").Append(Sim.Rules.Clock.Period.ToString(CultureInfo.InvariantCulture));
            text.Append(" runState=").Append(Sim.Rules.RunState.ToString(CultureInfo.InvariantCulture));
            text.Append(" delta=").Append(F(Sim.Rules.Clock.Delta));
            text.Append(" scoreHome=").Append(Sim.Rules.ScoreHome.ToString(CultureInfo.InvariantCulture));
            text.Append(" scoreAway=").Append(Sim.Rules.ScoreAway.ToString(CultureInfo.InvariantCulture));
            text.Append(" events=").Append(Events.Count.ToString(CultureInfo.InvariantCulture));
            text.Append(" viewedSlot=").Append(Sim.ViewedSlot.ToString(CultureInfo.InvariantCulture));
            text.Append(" humanDisplacement=").Append(F(HumanDisplacement()));
            text.AppendLine();
            text.Append("UBLADE3_NATIVE_MATCH_AI_FALLBACK active=").Append(AiFallbackActive ? "True" : "False")
                .Append(" note=\"").Append(AiFallbackNote).AppendLine("\"");
            text.Append("UBLADE3_NATIVE_MATCH_GOALIE_PLACEMENT note=\"").AppendLine(GoaliePlacementNote + "\"");
            text.Append(DescribeCamera());
            text.Append(DescribeStates());

            return text.ToString().TrimEnd();
        }

        public string DescribeCamera()
        {
            CameraState camera = Sim.MainCamera;

            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_NATIVE_MATCH_CAMERA pos=(").Append(F(camera.Body.Pos.x)).Append(',')
                .Append(F(camera.Body.Pos.y)).Append(',').Append(F(camera.Body.Pos.z));
            text.Append(") dir=(").Append(F(camera.Body.VelDir.x)).Append(',')
                .Append(F(camera.Body.VelDir.y)).Append(',').Append(F(camera.Body.VelDir.z));
            text.Append(") bungee=").Append(F(camera.BungeeLength));
            text.Append(" countdown=").Append(camera.Countdown.ToString(CultureInfo.InvariantCulture));
            text.Append(" fovDegrees=").Append(F(camera.HalfFov * 2.0f * 57.29577951308232f));
            text.Append(" tracked=").Append(camera.PrimaryTracked.ToString(CultureInfo.InvariantCulture));

            return text.ToString() + Environment.NewLine;
        }

        public string DescribeStates()
        {
            StringBuilder text = new StringBuilder();

            for (int slot = 0; slot < Slots.Length; slot++)
            {
                MatchPlayerSlot player = Slots[slot];
                PlayerState state = Sim.Players[slot];
                SimVec3 position = state.Body.Pos;

                text.Append("UBLADE3_NATIVE_MATCH_STATE slot=").Append(slot.ToString(CultureInfo.InvariantCulture));
                text.Append(" role=").Append(player.Role);
                text.Append(" model=").Append(player.ModelName);
                text.Append(" pos=(").Append(F(position.x)).Append(',').Append(F(position.y))
                    .Append(',').Append(F(position.z)).Append(')');
                text.Append(" speed=").Append(F(state.Body.Speed));
                text.Append(" velDir=(").Append(F(state.Body.VelDir.x)).Append(',')
                    .Append(F(state.Body.VelDir.y)).Append(',').Append(F(state.Body.VelDir.z)).Append(')');
                text.Append(" facing=").Append(F(state.Body.Facing));
                text.Append(" aimPos=(").Append(F(state.AimPosition.x)).Append(',')
                    .Append(F(state.AimPosition.y)).Append(',').Append(F(state.AimPosition.z)).Append(')');
                text.AppendLine();
            }

            return text.ToString().TrimEnd();
        }

        internal static string F(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
