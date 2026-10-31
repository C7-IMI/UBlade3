using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// N13 behaviour-parameter bodies (src/planvm/behparam.cpp): 0 ModsTweenNFacing (0x0041c780),
    /// 2 InteractorLatency (0x0041c8a0), 3 Target (0x0041c920), 9 PersistentAnimation (0x0041cb20),
    /// 10 TargetGoal (0x0041cdb0), 11 Animation (0x0041cce0), 12 AnimationEvent (0x0041cd40),
    /// 15 Digressive (0x0041c8e0). The exact field/argument handling is ported; the animation registry
    /// and clip playback are not in this slice, so the animation ids/rates are recorded in plan state
    /// and are reported as exact telemetry rather than rendering something else.
    /// </summary>
    public static class PlanAiMatchBehaviorParams
    {
        /// <summary>Off for the pinned N9/N10/N11/N12 regression suites; enabled by the N13/default roster.</summary>
        public static bool Enabled;

        public const int IdModsTweenNFacing = 0;

        public const int IdInteractorLatency = 2;

        public const int IdTarget = 3;

        public const int IdPersistentAnimation = 9;

        public const int IdTargetGoal = 10;

        public const int IdAnimation = 11;

        public const int IdAnimationEvent = 12;

        public const int IdDigressive = 15;

        public static int[] ImplementedIds()
        {
            return new int[]
            {
                IdModsTweenNFacing, IdInteractorLatency, IdTarget, IdPersistentAnimation,
                IdTargetGoal, IdAnimation, IdAnimationEvent, IdDigressive,
            };
        }

        public static bool TryApply(PlanAiHost host, int behaviorParamId, PlanNode node, PlanBehavior behavior)
        {
            if (!Enabled || behavior == null || node == null)
            {
                return false;
            }

            PlanAiBehaviorState state = PlanAiBehaviorStateStore.Get(behavior);
            state.Applied++;

            switch (behaviorParamId)
            {
                case IdModsTweenNFacing:
                {
                    if (node.ArgCount != 5)
                    {
                        throw new PlanAiFatalException(
                            "Unexpected number of args in Behavior parameterization functions.");
                    }

                    int objectA = ResolveSlot(host, node, 1);
                    int objectB = ResolveSlot(host, node, 2);
                    int aim = ResolveSlot(host, node, 3);

                    if (objectA < 0 || objectB < 0 || aim < 0)
                    {
                        host.World.Log("behparam.ModsTweenNFacing negative target, no state (source returns)");

                        return true;
                    }

                    state.ObjectA = objectA;
                    state.ObjectB = objectB;
                    state.AimObject = aim;
                    int period = node.Args[4].IntValue;
                    state.BlendStep = period != 0 ? 1.0f / (float)period : 0.0f;
                    host.World.Log("behparam.ModsTweenNFacing objectA=" + objectA + " objectB=" + objectB
                                   + " aim=" + aim + " blendStep=" + state.BlendStep.ToString("0.#####"));
                    return true;
                }

                case IdInteractorLatency:
                {
                    short value = (short)node.Args[1].IntValue;
                    behavior.Latency = (short)(PlanAiMatchContext.EngineInvDelta * (value * 0.01f));
                    host.World.Log("behparam.InteractorLatency value=" + value + " latency=" + behavior.Latency);
                    return true;
                }

                case IdTarget:
                {
                    state.TargetSlot = ResolveSlot(host, node, 1);
                    host.World.Log("behparam.Target slot=" + state.TargetSlot);
                    return true;
                }

                case IdPersistentAnimation:
                {
                    int animation = node.Args[1].IntValue;
                    state.PersistentAnimationIndex = animation;
                    behavior.PersistentRate = node.ArgCount == 4 ? node.Args[2].Word.AsFloat : -1.0f;
                    behavior.PersistentFlags = node.ArgCount == 4 ? node.Args[3].IntValue : 1;
                    host.World.Log("behparam.PersistentAnimation anim=" + animation
                                   + " rate=" + behavior.PersistentRate.ToString("0.#####")
                                   + " flags=" + behavior.PersistentFlags + " (animation registry absent; state)");
                    return true;
                }

                case IdTargetGoal:
                {
                    state.TargetGoal = node.Args[1].IntValue;
                    state.TargetGoalDirty = true;
                    host.World.Log("behparam.TargetGoal slot=" + state.TargetGoal);
                    return true;
                }

                case IdAnimation:
                {
                    int animation = node.Args[1].IntValue;
                    state.AnimationIndex = animation;
                    behavior.AnimationRate = node.ArgCount == 4 ? node.Args[2].Word.AsFloat : -1.0f;
                    behavior.AnimationFlags = node.ArgCount == 4 ? node.Args[3].IntValue : -1;
                    host.World.Log("behparam.Animation anim=" + animation
                                   + " rate=" + behavior.AnimationRate.ToString("0.#####")
                                   + " flags=" + behavior.AnimationFlags + " (animation registry absent; state)");
                    return true;
                }

                case IdAnimationEvent:
                {
                    behavior.ChannelSlot = node.Args[1].IntValue;
                    behavior.EventTarget = node.Args[2].IntValue;
                    behavior.EventKind = node.Args[3].IntValue;
                    behavior.EventParam = node.ArgCount == 5 ? node.Args[4].IntValue : 0;
                    state.AnimationEvents++;
                    state.LastAnimationEventKind = behavior.EventKind;
                    host.World.Log("behparam.AnimationEvent channel=" + behavior.ChannelSlot
                                   + " target=" + behavior.EventTarget + " kind=" + behavior.EventKind
                                   + " param=" + behavior.EventParam);
                    return true;
                }

                case IdDigressive:
                {
                    behavior.Digressive = node.Args[1].IntValue != 0;
                    host.World.Log("behparam.Digressive value=" + (behavior.Digressive ? 1 : 0));
                    return true;
                }

                default:
                    return false;
            }
        }

        private static int ResolveSlot(PlanAiHost host, PlanNode node, int index)
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
