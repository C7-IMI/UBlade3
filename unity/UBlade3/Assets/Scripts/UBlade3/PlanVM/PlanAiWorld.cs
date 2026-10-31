using System;
using System.Collections.Generic;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The rating scalars a plan handler reads from a PlayerObject (src/engine/objectstats.h
    /// GetScalar). The bounded slice uses a plain table so the decision bodies can be exercised
    /// without the team/rating data layer; values come from the Probe fixture and are documented.
    /// </summary>
    public sealed class PlanAiStats
    {
        private readonly Dictionary<int, float> scalars = new Dictionary<int, float>();

        public void Set(int ratingId, float value)
        {
            scalars[ratingId] = value;
        }

        public float GetScalar(int ratingId)
        {
            float value;

            return scalars.TryGetValue(ratingId, out value) ? value : 0.0f;
        }
    }

    /// <summary>
    /// The fields of the original PhysicsBody the ported handlers and decisions touch: position,
    /// velocity, heading, the ground friction pair and (for the Rok only) the attach target slot.
    /// </summary>
    public sealed class PlanAiPhysicsBody
    {
        public PlanAiVec3 Position;

        public PlanAiVec3 Velocity;

        /// <summary>PhysicsBody::velDir, the heading GetDistIfFacing uses.</summary>
        public PlanAiVec3 VelocityDirection;

        public float Friction = 1.0f;

        public float SteerScale = 1.0f;

        /// <summary>PhysicsBody::pAttach-&gt;nSlot: the player slot the Rok is attached to, or -1.</summary>
        public int AttachTargetSlot = -1;
    }

    /// <summary>
    /// One mock plan object: the subset of EngineObject / PlayerObject the ported action and
    /// decision bodies read and write. The scene message handler table, the message queue and the
    /// agenda observer list are ports of src/engine/engineobject.{h,cpp} and src/planvm/agendaitem.cpp.
    /// </summary>
    public sealed class PlanAiObject
    {
        public int Slot;

        public string Name;

        /// <summary>The owner's plan table name (the string EngineObject::FindPlanByName compares).</summary>
        public string PlanName;

        public PlanPhase Phase = PlanPhase.Local;

        public PlanAiPhysicsBody Body = new PlanAiPhysicsBody();

        public PlanAiStats Stats = new PlanAiStats();

        /// <summary>The plan instance on the object; null for the mock objects that only receive messages.</summary>
        public PlanState PlanState;

        public int FlyingSemaphore;

        public int AnimationSemaphore;

        public int ClockStoppedSemaphore;

        public int KnockedDownState;

        public int DesiredSubstitute = -1;

        public PlanAiAgendaItem Observers;

        public readonly int[] SceneHandlerSlot = new int[PlanAiConstants.SceneMessageCount];

        public readonly PlanMessageInteractor[] SceneHandlers = new PlanMessageInteractor[PlanAiConstants.SceneHandlerSlots];

        public int SceneHandlerCount = 1;

        public bool HasPlayer
        {
            get { return Slot >= 0 && Slot < PlanAiConstants.PlayerObjectCount; }
        }

        /// <summary>EngineObject::ResetSceneMessageHandlers (0x004721d0).</summary>
        public void ResetSceneMessageHandlers()
        {
            for (int index = 0; index < SceneHandlerSlot.Length; index++)
            {
                SceneHandlerSlot[index] = 0;
            }

            for (int index = 0; index < SceneHandlers.Length; index++)
            {
                SceneHandlers[index] = null;
            }

            SceneHandlerCount = 1;
        }

        /// <summary>
        /// EngineObject::RegisterSceneMessageHandler (0x00472200): one handler per message id, in
        /// registration order; a 51st handler is fatal in the original.
        /// </summary>
        public void RegisterSceneMessageHandler(PlanMessageInteractor handler)
        {
            if (SceneHandlerCount >= PlanAiConstants.SceneHandlerSlots)
            {
                throw new PlanAiFatalException("Too many scene message handlers on one object!");
            }

            int id = handler.GetNamedSlot();
            if (id < 0 || id >= SceneHandlerSlot.Length)
            {
                throw new PlanAiFatalException("Scene message id " + id + " is outside the object's handler table.");
            }

            SceneHandlerSlot[id] = SceneHandlerCount;
            SceneHandlers[SceneHandlerCount] = handler;
            SceneHandlerCount++;
        }

        /// <summary>EngineObject::GetDistIfFacing (0x00474590).</summary>
        public float GetDistIfFacing(PlanAiObject other, float minDot)
        {
            PlanAiVec3 delta = other.Body.Position - Body.Position;
            float distance = delta.Length;

            delta.NormalizeInPlace();

            if (PlanAiVec3.Dot(delta, Body.VelocityDirection) > minDot)
            {
                return distance;
            }

            return 0.0f;
        }

        public PlanAiVec3 Position
        {
            get { return Body.Position; }
        }
    }

    public struct PlanAiPendingMessage
    {
        public int TargetSlot;

        public int Message;

        public int Arg;

        public int Sender;

        public int FromInput;
    }

    /// <summary>
    /// The mock match world: the fixed object slots, the pending scene-message queue
    /// (GameEngine::PostSceneMessage 0x004870b0 and ProcessInputCommandQueue 0x00487140), the
    /// position markers, the difficulty gate and the trace log. Only the pieces the selected
    /// CARRYROK flow reaches are simulated; everything else is an explicit telemetry line.
    /// </summary>
    public sealed class PlanAiWorld
    {
        public readonly Dictionary<int, PlanAiObject> Objects = new Dictionary<int, PlanAiObject>();

        public readonly List<PlanAiPendingMessage> Pending = new List<PlanAiPendingMessage>();

        public readonly List<string> Events = new List<string>();

        public int ClockMs;

        /// <summary>GameEngine::m_nTeammateMode: 0 is single-player (the human team mate).</summary>
        public int TeammateMode = 1;

        public PlanAiDifficulty Difficulty = PlanAiDifficulty.Medium;

        /// <summary>The plan table EngineObject::FindPlanByName walks for this object; used for CHANGE_OF_PLAN telemetry.</summary>
        public readonly List<string> PlanTable = new List<string>();

        /// <summary>Set by PlanAiHost so world-initiated dispatch can call the interactor.</summary>
        public PlanAiHost Host;

        public float DamageGate
        {
            get
            {
                if (Difficulty == PlanAiDifficulty.Easy)
                {
                    return 3.0f;
                }

                if (Difficulty == PlanAiDifficulty.Medium)
                {
                    return 6.0f;
                }

                return 9.0f;
            }
        }

        public PlanAiObject Rok
        {
            get
            {
                PlanAiObject value;

                return Objects.TryGetValue(PlanAiConstants.SlotRok, out value) ? value : null;
            }
        }

        public PlanAiObject AddObject(int slot, string name, string planName)
        {
            PlanAiObject created = new PlanAiObject();
            created.Slot = slot;
            created.Name = name;
            created.PlanName = planName;
            Objects[slot] = created;

            return created;
        }

        public PlanAiObject GetObject(int slot)
        {
            PlanAiObject value;

            return Objects.TryGetValue(slot, out value) ? value : null;
        }

        public PlanAiObject GetPlayer(int slot)
        {
            if (slot < 0 || slot >= PlanAiConstants.PlayerObjectCount)
            {
                return null;
            }

            return GetObject(slot);
        }

        public void Log(string line)
        {
            Events.Add("t=" + ClockMs.ToString() + " " + line);
        }

        /// <summary>GameEngine::BroadcastEvent: the original posts to the sender kind's objects; the mock posts to every plan object.</summary>
        public void BroadcastEvent(int message, int arg, int senderSlot)
        {
            Log("broadcast message=" + DescribeMessage(message) + " arg=" + arg + " sender=" + senderSlot);

            foreach (KeyValuePair<int, PlanAiObject> entry in Objects)
            {
                if (entry.Value.PlanState != null)
                {
                    PostSceneMessage(entry.Key, message, arg, senderSlot, 0);
                }
            }
        }

        /// <summary>EngineObject::PostSceneEvent (0x00422f50): no explicit sender.</summary>
        public void PostSceneEvent(int targetSlot, int message, int arg)
        {
            PostSceneMessage(targetSlot, message, arg, PlanAiConstants.ModelIdNone, 0);
        }

        /// <summary>
        /// EngineObject::PostSceneMessage (0x00472090): a message is only queued when the target has
        /// a registered handler for that id, matching the original's m_aSceneHandlerSlot test.
        /// </summary>
        public void PostSceneMessage(int targetSlot, int message, int arg, int sender, int fromInput)
        {
            PlanAiObject target = GetObject(targetSlot);

            if (target == null)
            {
                Log("post.dropped target=" + targetSlot + " message=" + DescribeMessage(message) + " reason=no-object");

                return;
            }

            if (message < 0 || message >= PlanAiConstants.SceneMessageCount)
            {
                Log("post.dropped target=" + targetSlot + " message=" + message + " reason=out-of-range");

                return;
            }

            if (target.SceneHandlerSlot[message] == 0)
            {
                Log("post.dropped target=" + targetSlot + " message=" + DescribeMessage(message) + " reason=no-handler");

                return;
            }

            PlanAiPendingMessage pending;
            pending.TargetSlot = targetSlot;
            pending.Message = message;
            pending.Arg = arg;
            pending.Sender = sender;
            pending.FromInput = fromInput;
            Pending.Add(pending);
            Log("post target=" + targetSlot + " message=" + DescribeMessage(message) + " arg=" + arg + " sender=" + sender);
        }

        /// <summary>
        /// EngineObject::DispatchSceneMessage (0x00472110). Runs the one registered handler with a
        /// fresh decision-log word, records the log word the handler leaves behind, and reports the
        /// network pack enqueue the original does after a local player message.
        /// </summary>
        public short DispatchSceneMessage(int targetSlot, int message, int arg, short decisionLog, int sender, int fromInput)
        {
            PlanAiObject target = GetObject(targetSlot);

            if (target == null)
            {
                Log("dispatch.dropped target=" + targetSlot + " message=" + DescribeMessage(message) + " reason=no-object");

                return decisionLog;
            }

            if (message < 0 || message >= PlanAiConstants.SceneMessageCount || target.SceneHandlerSlot[message] == 0)
            {
                Log("dispatch.dropped target=" + targetSlot + " message=" + DescribeMessage(message) + " reason=no-handler");

                return decisionLog;
            }

            PlanMessageInteractor handler = target.SceneHandlers[target.SceneHandlerSlot[message]];
            short logWord = decisionLog;

            if (fromInput != 0 || target.Phase != PlanPhase.Network)
            {
                if (handler.GetActiveState() != -1)
                {
                    handler.SetSenderAndDispatch(arg, ref logWord, unchecked((short)sender), Host);
                }
                else
                {
                    Log("dispatch.skipped target=" + targetSlot + " message=" + DescribeMessage(message) + " reason=inactive-handler");
                }
            }

            Log("dispatch target=" + targetSlot + " message=" + DescribeMessage(message) + " arg=" + arg + " sender=" + sender +
                " log=0x" + unchecked((ushort)logWord).ToString("x4"));

            if (fromInput == 0 && target.HasPlayer && target.Phase == PlanPhase.Local)
            {
                Log("scene.pack-enqueue target=" + targetSlot + " message=" + DescribeMessage(message) + " (network copy not simulated)");
            }

            return logWord;
        }

        /// <summary>GameEngine::ProcessInputCommandQueue's pending-list pass: drain FIFO, including messages posted while draining.</summary>
        public int DrainPendingMessages(int budget)
        {
            int index = 0;
            int dispatched = 0;

            while (index < Pending.Count && dispatched < budget)
            {
                PlanAiPendingMessage message = Pending[index];
                index++;
                DispatchSceneMessage(message.TargetSlot, message.Message, message.Arg, DecisionLog.Fresh, message.Sender, message.FromInput);
                dispatched++;
            }

            if (index > 0)
            {
                Pending.RemoveRange(0, index);
            }

            return dispatched;
        }

        /// <summary>PhysicsBody's agenda pass (physicsbody.cpp: pTarget-&gt;Process(0)); the chain recurses itself.</summary>
        public void TickAgenda()
        {
            foreach (KeyValuePair<int, PlanAiObject> entry in Objects)
            {
                PlanAiAgendaItem item = entry.Value.Observers;

                if (item != null)
                {
                    item.Process(0);
                }
            }
        }

        public static string DescribeMessage(int message)
        {
            string name = PlanOpcodes.MessageName(message);

            return message.ToString() + "(" + name + ")";
        }
    }
}
