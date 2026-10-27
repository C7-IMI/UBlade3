// UBlade3 - exact HyperBlade (1996) simulation core.
// The frame structure: src/engine/gameengine.cpp GameEngine::PerFrameUpdate (0x00486b20),
// GameEngine::SetRunState (0x00486de0), RestartMatch (0x00486ff0) and GameEngine::TickClock
// (0x00481130). The 30 Hz fixed step is 20XX src/engine/framepacing.h kTickMs and
// src/app/simulator.cpp PlayMatch; the decomp itself steps once per host frame.
// This class wires the ported pieces together. The ADLIB plan VM is not ported yet; the direct
// behaviours the plans invoke are called here in the same order the agenda items run.

using System;

namespace UBlade3.Sim
{
    public sealed class Simulation
    {
        public readonly FixedStepClock StepClock = new FixedStepClock();
        public readonly MatchRules Rules;
        public readonly ArenaBuild Arena = new ArenaBuild();
        public readonly CameraState MainCamera = new CameraState();
        public readonly RokState Rok = new RokState();
        public readonly PlayerState[] Players = new PlayerState[ObjectConstants.PlayerObjectCount];

        // g_bSessionActive and the two-player switch the steering reads.
        public bool SessionActive;
        public bool TwoPlayers;
        public bool SmartCam;

        // g_nViewedSlot, the slot the main camera follows and the throw gate reads.
        public int ViewedSlot;

        // The input device's held mask for a slot (the keyboard device's m_nHeldMask in the original).
        public Func<int, HeldBinding> InputForSlot = slot => HeldBinding.None;

        // Scene messages the ported behaviours post: (targetSlot, message, param).
        public Action<int, int, int> PostSceneMessage;

        // The viewport the camera projection is built for (cameraobject.cpp SetViewTransform output).
        public LayoutState Viewport = new LayoutState
        {
            PrevWidth = 1280,
            PrevHeight = 720,
            PrevRight = 1280.0f,
            PrevBottom = 720.0f,
        };

        // The engine's g_bEnergyDraining, one flag for every skater.
        private bool energyDraining = true;

        public Simulation(MatchContext context)
        {
            Rules = new MatchRules(context);
            StepClock.InitMatchTimers();

            for (int slot = 0; slot < Players.Length; slot++)
            {
                Players[slot] = new PlayerState(slot);
                Players[slot].Body.Tuning = PhysicsTuning.Shipped;
            }

            MainCamera.SlotLookup = slot =>
            {
                if (slot >= 0 && slot < Players.Length)
                {
                    return Players[slot];
                }
                if (slot == ObjectConstants.SlotRok)
                {
                    return Rok;
                }
                return null;
            };

            Rules.Clock.InitMatchTimers(0);
            MainCamera.InitViewTransforms(Viewport);
        }

        // The loader hook: bind an object's ObjectDef motion values and place it, as
        // PhysicsBody::BindToObject (0x00407780) does when the engine creates the object.
        public void BindPlayer(int slot, ObjectMotionParams definition)
        {
            PlayerState player = Players[slot];
            player.Body.BindToObject(slot, EngineObjectKind.Dromer, definition, Rules.Clock,
                PhysicsConstants.Gravity * Rules.Clock.DeltaSquared);
            player.AimPosition = player.Body.Pos;
            player.TargetGoalSlot = slot <= ObjectConstants.SlotDromer3
                ? (slot == 0 || slot == 2 ? (short)ObjectConstants.SlotNGoal : (short)ObjectConstants.SlotSGoal)
                : (slot == ObjectConstants.GoalieSlotA ? (short)ObjectConstants.SlotNGoal : (short)ObjectConstants.SlotSGoal);
            player.PlanBehaviorKind = ObjectConstants.BehaviorGlide;
        }

        public void BindRok(ObjectMotionParams definition)
        {
            Rok.Body.BindToObject(ObjectConstants.SlotRok, EngineObjectKind.Rok, definition, Rules.Clock,
                PhysicsConstants.Gravity * Rules.Clock.DeltaSquared);
            Rok.ResetToSpawnState();
        }

        // The drome load (GameEngine::LoadArena 0x00484e80 / SetArena 0x004850f0) hands its plain
        // data to ArenaState; match start calls BeginMatchPlay as RestartMatch does.
        public void LoadDrome(DromeDefinition definition, bool beginMatch)
        {
            Arena.Load(definition, EllipsoidStatics.Arena);
            if (beginMatch)
            {
                BeginMatch();
            }
        }

        // Game::UpdateState's ESTATE_MATCH_START tail (game.cpp 0x00491de0): SetTeams, SetArena,
        // RestartMatch, then the drag broadcast.
        public void BeginMatch()
        {
            Rules.RunState = ClockConstants.RunPlaying;
            Rules.Clock.InitMatchTimers(StepClock.Clock.TickMs);
            Rules.RestartMatch();
            Rules.SetRunState(1, Rules.Clock.TickMs);
            ViewedSlot = ObjectConstants.SlotDromer0;
            MainCamera.SetTrackedObject(Players[ObjectConstants.SlotDromer0]);
            MainCamera.Body.Pos = new Vec3(0.0f, 0.0f, -80.0f);
            MainCamera.ResetToInitialState(MainCamera.Body.Pos);
        }

        // One fixed simulation tick: the 30 Hz step. Game::RunFrame -> PerFrameUpdate -> TickClock.
        public void Tick()
        {
            uint now = StepClock.Advance();

            switch (Rules.RunState)
            {
                case ClockConstants.RunRestart:
                    // PerFrameUpdate case ENGINE_RUN_RESTART: restart then toggle pause back to play.
                    Rules.RestartMatch();
                    Rules.SetRunState(1, now);
                    return;

                case ClockConstants.RunPaused:
                    // The original only draws the main camera's view while paused; nothing simulates.
                    return;

                case ClockConstants.RunPlaying:
                    Rules.TickClock(now, false, true);
                    UpdateGravity();

                    // PerFrameUpdate walks m_ppObjects in sorted order and calls
                    // PhysicsBody::PerFrameTick: PreUpdateTick, then the plan behaviour's agenda
                    // chain, then PostUpdateCleanup, then the dromer's stats update.
                    for (int i = 0; i < ObjectConstants.PlayerObjectCount; i++)
                    {
                        TickPlayer(Players[i], now);
                    }
                    TickCamera(now);
                    UpdateCarriedRok();

                    // BroadcastCollisionEvents / ProcessInputCommandQueue / ClearPendingEvents:
                    // the collision bubble and plan-interactor ports are not in this slice, so the
                    // three steps are hook points on the shared task board.
                    return;
            }
        }

        // gameengine.cpp PerFrameUpdate: m_flGravityDt = g_flGravityGameEngine * flDeltaSquared.
        private void UpdateGravity()
        {
            float gravityDt = PhysicsConstants.Gravity * Rules.Clock.DeltaSquared;
            for (int i = 0; i < Players.Length; i++)
            {
                Players[i].Body.EngineGravityDt = gravityDt;
            }
            Rok.Body.EngineGravityDt = gravityDt;
        }

        // CSpeedBurst (agenda) + CMonitorKeys + CGlideOnSurface for one skater. The order follows
        // the behaviour's agenda chain: speed burst, key monitor (movement axes + steering force),
        // then the glide terrain step. If a plan is in the fly behaviour the owner's physics is
        // integrated by CProjectAndBounce instead; that behaviour is a hook point.
        private void TickPlayer(PlayerState player, uint now)
        {
            player.Body.PreUpdateTick(Rules.Clock);

            if (player.Kind == EngineObjectKind.Dromer)
            {
                EngineClock clock = Rules.Clock;

                HeldBinding deviceMask = InputForSlot(player.Slot);
                PlayerSteering.MonitorKeysTick(player, clock, deviceMask, OnPlayerEvent, TwoPlayers);
                player.Body.PreUpdateTick(clock);

                if (player.PlanBehaviorKind == ObjectConstants.BehaviorGlide)
                {
                    GlideOnSurface.UpdateTerrainPhysics(player, player.Body, clock, EllipsoidStatics.Arena,
                        Rok.AttachedSlot, player.Body.Tuning, OnPlayerEvent, false, true);
                }

                player.Stats.UpdateMomentumAndEnergy(clock, true, ref energyDraining);
            }
        }

        private void OnPlayerEvent(int message, int param)
        {
            if (PostSceneMessage != null)
            {
                PostSceneMessage(ObjectConstants.SlotSoundMan, message, param);
            }
        }

        // CBeAttached::Process (0x0040dc80): a carried body follows the attach pak's transform and
        // derives its velocity from the frame displacement. When the animation port supplies no
        // transform the carrier body's aim position plus the pak offset stands in (documented TODO).
        private void UpdateCarriedRok()
        {
            AttachPak pak = Rok.Body.Attach;
            if (pak == null)
            {
                return;
            }

            Rok.Body.PrevPos = Rok.Body.Pos;

            if (pak.Transform != null)
            {
                Rok.Body.Pos = pak.Transform.TransformPoint(pak.Offset);
            }
            else if (pak.Slot >= 0 && pak.Slot < Players.Length)
            {
                Vec3 carrier = Players[pak.Slot].AimPosition;
                Rok.Body.Pos = new Vec3(carrier.x + pak.Offset.x, carrier.y + pak.Offset.y, carrier.z + pak.Offset.z);
            }

            Vec3 vel = new Vec3(
                Rok.Body.Pos.x - Rok.Body.PrevPos.x,
                Rok.Body.Pos.y - Rok.Body.PrevPos.y,
                Rok.Body.Pos.z - Rok.Body.PrevPos.z);

            Rok.Body.Vel = vel;
            float speed = (float)Math.Sqrt(vel.z * vel.z + vel.y * vel.y + vel.x * vel.x);
            Rok.Body.Speed = speed;

            if (speed > 0.001f)
            {
                Vec3 normalized;
                if (MathOps.NormalizeTo(vel, out normalized))
                {
                    Rok.Body.VelDir = normalized;
                }
            }

            Rok.Body.Step = Vec3.ScaleTo(vel, Rules.Clock.GetInvDelta());
        }

        // cameraobject.cpp ViewIt / planvm CTrack: the main camera's chase behaviour runs at the end
        // of the object pass in the original; here it runs after the players it follows.
        private void TickCamera(uint now)
        {
            CameraTrack.SmartCamEnabled = SmartCam;
            CameraTrack.LookEnabled = true;

            if (MainCamera.GetTrackedObject() == null && ViewedSlot >= 0 && ViewedSlot < Players.Length)
            {
                MainCamera.SetTrackedObject(Players[ViewedSlot]);
            }

            CameraTrack.Process(MainCamera, Rules.Clock, MainCamera.SlotLookup, () => Rok.Body.Attach, ViewedSlot);
        }

        // The debug/pause entry points (GameEngine::TogglePause 0x0047acf0).
        public void TogglePause()
        {
            Rules.SetRunState(Rules.RunState == ClockConstants.RunPlaying ? 0 : 1, Rules.Clock.TickMs);
        }

        // GameEngine::SetRunState(3) path: the next frame restarts the match.
        public void RequestRestart()
        {
            Rules.RunState = ClockConstants.RunRestart;
        }

        // Posts a scene event through the engine's broadcast clock effects when it is one of the
        // three events that drive the period clock.
        public void BroadcastSceneEvent(int message, int param, int senderSlot)
        {
            if (message == MessageIds.Shoot || message == MessageIds.GoalScored || message == MessageIds.StartDrag)
            {
                Rules.SceneEvent(message, param, senderSlot, Rules.Clock.TickMs);
            }
        }
    }
}
