using System;
using System.Collections.Generic;
using System.Text;
using UBlade3.Assets;
using UBlade3.PlanVM;
using UBlade3.Sim;
using UnityEngine;

namespace UBlade3.Native
{
    // N16: forwards AnimAssm frame triggers into the PlanVM message path.
    //
    // AnimPlayer::UpdateFrame (HYPERX 0x00424880) treats a trigger whose message is MSG_SYNC_FRAME as
    // a ConditionalDispatch (handled by MatchAnimationPlayer's sync transition) and posts every other
    // trigger's message to the target object with PostEvent(message,param,sender). The C# trigger table
    // gives the action name and sync reference; this bridge maps id_MSG_* names through
    // PlanOpcodes.MessageNames and calls the target adapter's existing InjectSceneMessage path.
    // Sync triggers are counted but not injected (the player already followed them). Unmapped action
    // names are explicit telemetry, never guessed.
    public sealed class MatchAnimationMessageBridge
    {
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_MESSAGE";

        private readonly Dictionary<string, int> messageByName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // N17: DATA/ENUMIDS.H id_MSG_YerDown is id 56 (the decomp messageids.h omits the symbol,
        // but the original ENUMIDS list places it between Breakaway (55) and ExitBehavior (57)).
        private readonly Dictionary<string, int> messageAliases =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "YerDown", 56 },
            };

        // AnimAssm action names that are not id_MSG_ symbols. The original ParseScriptLine stores
        // MSG_NOMSG for a trigger name that does not start with the plan-symbol prefix; these are
        // animation action labels, not plan scene messages. They are counted and never injected.
        private static readonly string[] NonMessageTriggerNames = { "Contact", "Catch", "release" };

        public MatchAnimationConsumer Animation;

        public MatchPlanAiRoster Roster;

        public int AttachedPlayers;

        public int TriggerEvents;

        public int SyncTriggers;

        public int InjectedMessages;

        public int InjectFailures;

        public int UnmappedTriggers;

        public int NonMessageTriggers;

        public int YerDownInjected;

        public int MappedMessageTriggers;

        public readonly List<string> Trace = new List<string>();

        public MatchAnimationMessageBridge()
        {
            foreach (KeyValuePair<int, string> entry in PlanOpcodes.MessageNames)
            {
                if (!messageByName.ContainsKey(entry.Value))
                {
                    messageByName[entry.Value] = entry.Key;
                }
            }

            foreach (KeyValuePair<string, int> entry in messageAliases)
            {
                messageByName[entry.Key] = entry.Value;
            }
        }

        public void Attach(MatchAnimationConsumer animation, MatchPlanAiRoster roster)
        {
            Animation = animation;
            Roster = roster;
            AttachedPlayers = 0;

            if (Animation == null || Animation.Players == null)
            {
                return;
            }

            for (int slot = 0; slot < Animation.Players.Length; slot++)
            {
                MatchAnimationPlayer player = Animation.Players[slot];

                if (player == null)
                {
                    continue;
                }

                player.EventFired -= OnTrigger;
                player.EventFired += OnTrigger;
                AttachedPlayers++;
            }

            Trace.Add("attached players=" + AttachedPlayers + " roster=" + (roster == null
                ? "none" : roster.PlanSlots.Count.ToString()));
            Debug.Log(Marker + "_ATTACHED players=" + AttachedPlayers
                      + " planSlots=" + (roster == null ? 0 : roster.PlanSlots.Count));
        }

        private void OnTrigger(MatchAnimationPlayer player, AnimationAssemblyTrigger trigger, int frame)
        {
            TriggerEvents++;

            if (trigger.HasSync)
            {
                SyncTriggers++;
                Trace.Add("sync slot=" + player.Slot + " frame=" + trigger.Frame
                          + " target=" + trigger.SyncAssembly + ":" + trigger.SyncSegment + ":"
                          + trigger.SyncFrame);
                return;
            }

            MatchPlanAi adapter = Roster == null ? null : Roster.Adapter(player.Slot);
            int targetSlot = player.Slot;
            int param = 0;

            if (adapter != null && adapter.CurrentBehavior != null)
            {
                PlanBehavior behavior = adapter.CurrentBehavior;

                if (behavior.EventTarget >= 0 && behavior.EventTarget < ObjectConstants.PlayerObjectCount)
                {
                    targetSlot = behavior.EventTarget;
                }

                param = behavior.EventParam;
            }

            string action = trigger.ActionName ?? string.Empty;
            string messageName = action.StartsWith("id_MSG_", StringComparison.OrdinalIgnoreCase)
                ? action.Substring("id_MSG_".Length)
                : action;

            for (int index = 0; index < NonMessageTriggerNames.Length; index++)
            {
                if (string.Equals(messageName, NonMessageTriggerNames[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    NonMessageTriggers++;
                    Trace.Add("non-message slot=" + player.Slot + " frame=" + trigger.Frame
                              + " action=" + action
                              + " (AnimAssm action name is not an id_MSG_ symbol; source stores MSG_NOMSG)");
                    return;
                }
            }

            int messageId;

            if (!messageByName.TryGetValue(messageName, out messageId))
            {
                UnmappedTriggers++;
                Trace.Add("unmapped slot=" + player.Slot + " frame=" + trigger.Frame
                          + " action=" + action + " mappedName=" + messageName);
                return;
            }

            MappedMessageTriggers++;

            MatchPlanAi target = Roster == null ? null : Roster.Adapter(targetSlot);

            if (target == null)
            {
                target = adapter;
            }

            if (target == null)
            {
                InjectFailures++;
                Trace.Add("no-target slot=" + player.Slot + " target=" + targetSlot
                          + " message=" + messageName);
                return;
            }

            target.InjectSceneMessage(messageId, param, player.Slot);
            InjectedMessages++;

            if (string.Equals(messageName, "YerDown", StringComparison.OrdinalIgnoreCase))
            {
                YerDownInjected++;
            }
            Trace.Add("inject slot=" + player.Slot + " target=" + targetSlot + " message="
                      + messageName + "(" + messageId + ") param=" + param + " frame=" + trigger.Frame);
            Debug.Log(Marker + "_INJECT slot=" + player.Slot + " target=" + targetSlot
                      + " message=" + messageName + " id=" + messageId + " param=" + param
                      + " frame=" + trigger.Frame);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append("_STATE attached=").Append(AttachedPlayers)
                .Append(" triggers=").Append(TriggerEvents)
                .Append(" syncTriggers=").Append(SyncTriggers)
                .Append(" injected=").Append(InjectedMessages)
                .Append(" failures=").Append(InjectFailures)
                .Append(" unmapped=").Append(UnmappedTriggers)
                .Append(" nonMessage=").Append(NonMessageTriggers)
                .Append(" mapped=").Append(MappedMessageTriggers)
                .Append(" yerDownInjected=").Append(YerDownInjected);
            return text.ToString();
        }

        public string TraceText(int max)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < Trace.Count && i < max; i++)
            {
                text.Append(Marker).Append("_TRACE ").AppendLine(Trace[i]);
            }

            return text.ToString();
        }
    }

    // N16 interactive driver: installs the stat/bubble/animation-message consumers without editing
    // MatchScene/MatchPlay. Capture/probe paths keep it off so their frames/behaviour are unchanged.
    public sealed class MatchEffectsDriver : MonoBehaviour
    {
        public const string Marker = "UBLADE3_NATIVE_EFFECTS";

        public static bool ForceEnabled;

        private static readonly string[] CaptureArguments =
        {
            "--play-capture=", "--n7-capture=", "--hud-capture=", "--display-probe=",
            "--n13-probe=", "--n15-probe=", "--gameplay-capture=", "--match-capture=",
            "--n13-capture=",
        };

        public MatchStatAccumulator Stats { get; private set; }

        public MatchBubbleAdapter Bubbles { get; private set; }

        public MatchAnimationMessageBridge Messages { get; private set; }

        public MatchPlay Play;

        public MatchScene Scene;

        public static bool ShouldEnableByDefault()
        {
            if (ForceEnabled)
            {
                return true;
            }

            if (!Application.isPlaying || Application.isBatchMode)
            {
                return false;
            }

            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                for (int c = 0; c < CaptureArguments.Length; c++)
                {
                    if (arguments[i].StartsWith(CaptureArguments[c], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!ShouldEnableByDefault())
            {
                return;
            }

            GameObject host = new GameObject("UBlade3 Match Effects");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<MatchEffectsDriver>();
        }

        private void LateUpdate()
        {
            if (Stats == null)
            {
                Scene = UnityEngine.Object.FindAnyObjectByType<MatchScene>();
                Play = UnityEngine.Object.FindAnyObjectByType<MatchPlay>();

                if (Scene == null || Play == null || !Play.IsBuilt || Scene.Animation == null)
                {
                    return;
                }

                Stats = Play.Control != null && Play.Control.PlanRoster != null
                    ? Play.Control.PlanRoster.MatchStats : new MatchStatAccumulator();
                Bubbles = new MatchBubbleAdapter();
                Messages = new MatchAnimationMessageBridge();
                Messages.Attach(Scene.Animation, Play.Control == null ? null : Play.Control.PlanRoster);
                Debug.Log(Marker + "_READY animation=" + Scene.Animation.PlanSlots
                          + " planRoster=" + (Play.Control == null || Play.Control.PlanRoster == null
                              ? "none" : Play.Control.PlanRoster.PlanSlots.Count.ToString()));
            }

            if (Scene == null || Play == null || Play.Control == null)
            {
                return;
            }

            MatchPlanAiRoster roster = Play.Control.PlanRoster;

            if (roster == null)
            {
                return;
            }

            Stats.Tick(roster, Scene.Sim.Sim);
            Bubbles.Tick(roster, Scene.Sim.Sim);

            if (Messages.Animation != Scene.Animation)
            {
                Messages.Attach(Scene.Animation, roster);
            }
        }
    }
}
