using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBlade3.PlanVM
{
    public sealed class PlanVmSelfCheckResult
    {
        public int Passed;

        public int Total;

        public readonly List<string> Failures = new List<string>();

        public bool Ok
        {
            get { return Total == Passed; }
        }
    }

    /// <summary>
    /// Synthetic checks for the plan VM core: the operand model, the PBN decoder, the loader, the
    /// compile/instantiate state machine, the decision log and the ExecuteScript dispatch. No game
    /// data and no scene are needed, so the behavioural part of the port stays verified even when the
    /// retail install is not present.
    /// </summary>
    public static class PlanVmSelfCheck
    {
        public static PlanVmSelfCheckResult Run()
        {
            PlanVmSelfCheckResult result = new PlanVmSelfCheckResult();
            Checker checker = new Checker(result);

            CheckDecisionLog(checker);
            CheckOpcodes(checker);
            CheckWordModel(checker);
            CheckArgResolver(checker);
            CheckParser(checker);
            CheckLoader(checker);
            CheckState(checker);
            CheckBehaviorSetup(checker);
            CheckScriptDispatch(checker);
            CheckDecideReplay(checker);
            CheckUnsupportedActions(checker);

            return result;
        }

        // ---- synthetic plan construction -------------------------------------------------------

        private static int[] L(int value)
        {
            return new int[] { value };
        }

        private static int[] S(string text)
        {
            int[] words = new int[text.Length + 2];
            words[0] = PlanWord.MarkerString;

            for (int index = 0; index < text.Length; index++)
            {
                words[index + 1] = text[index];
            }

            words[words.Length - 1] = 0;

            return words;
        }

        private static int[] N(int opcode, params int[][] children)
        {
            List<int> words = new List<int>();
            words.Add(PlanWord.MarkerNode);
            words.Add(opcode);
            words.Add(children.Length);

            for (int index = 0; index < children.Length; index++)
            {
                words.AddRange(children[index]);
            }

            return words.ToArray();
        }

        private static int[] End()
        {
            return new int[] { PlanWord.MarkerNode, PlanWord.EndPlanOpcode };
        }

        private static int[] Cat(params int[][] parts)
        {
            List<int> words = new List<int>();

            for (int index = 0; index < parts.Length; index++)
            {
                words.AddRange(parts[index]);
            }

            return words.ToArray();
        }

        private static PlanFileImage Image(string name, int[] words)
        {
            PlanFileImage image = new PlanFileImage();
            image.Name = name;
            image.Path = name;
            image.DeclaredWordCount = words.Length;
            image.Words = Array.ConvertAll(words, delegate(int value) { return new PlanWord(value); });
            image.ByteLength = (words.Length + 1) * 4;

            return image;
        }

        private static PlanVmRecordingHost Host(int slot)
        {
            FixedPlanNameResolver names = new FixedPlanNameResolver();
            names.Add("Sideln", 3);

            return new PlanVmRecordingHost((short)slot, PlanArgResolver.ForSlot(slot), names);
        }

        private static PlanFileImage ThreeBehaviorPlan()
        {
            return Image("THREE",
                Cat(
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")),
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("B")),
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("C")),
                    End()));
        }

        private sealed class Checker
        {
            private readonly PlanVmSelfCheckResult result;

            public Checker(PlanVmSelfCheckResult result)
            {
                this.result = result;
            }

            public void Check(string name, bool condition)
            {
                result.Total++;

                if (condition)
                {
                    result.Passed++;
                }
                else
                {
                    result.Failures.Add(name);
                }
            }

            public void Throws<T>(string name, Action action) where T : Exception
            {
                result.Total++;

                try
                {
                    action();
                    result.Failures.Add(name + " (no exception)");
                }
                catch (T)
                {
                    result.Passed++;
                }
                catch (Exception exception)
                {
                    result.Failures.Add(name + " (threw " + exception.GetType().Name + ": " + exception.Message + ")");
                }
            }
        }

        // ---- decision log ---------------------------------------------------------------------

        private static void CheckDecisionLog(Checker checker)
        {
            short word = DecisionLog.Fresh;
            DecisionLog.Push(ref word, 5);
            short one = word;
            short masked = unchecked((short)(one & 0x3FFF));
            short branch = 0;
            DecisionLog.Pop(ref masked, ref branch);
            checker.Check("decisionlog.one.branch", branch == 5);
            checker.Check("decisionlog.one.left", masked == 0);

            short two = DecisionLog.Fresh;
            DecisionLog.Push(ref two, 3);
            DecisionLog.Push(ref two, 31);
            short rest = unchecked((short)(two & 0x3FFF));
            short first = 0;
            DecisionLog.Pop(ref rest, ref first);
            short second = 0;
            DecisionLog.Pop(ref rest, ref second);
            checker.Check("decisionlog.two.order", first == 3 && second == 31 && rest == 0);

            short spill = DecisionLog.Fresh;
            DecisionLog.Push(ref spill, 3);
            DecisionLog.Push(ref spill, 32);
            checker.Check("decisionlog.two.spill", (spill & 0x3000) != 0);

            short three = DecisionLog.Fresh;
            DecisionLog.Push(ref three, 1);
            DecisionLog.Push(ref three, 2);
            DecisionLog.Push(ref three, 3);
            short at = unchecked((short)(three & 0x3FFF));
            short b1 = 0;
            short b2 = 0;
            short b3 = -1;
            DecisionLog.Pop(ref at, ref b1);
            DecisionLog.Pop(ref at, ref b2);
            DecisionLog.Pop(ref at, ref b3);
            checker.Check("decisionlog.three.fault", b1 == 1 && b2 == 2 && b3 == 0);

            short wide = DecisionLog.Fresh;
            DecisionLog.Push(ref wide, 37);
            short narrow = unchecked((short)(wide & 0xFF));
            short wideBranch = 0;
            DecisionLog.Pop(ref narrow, ref wideBranch);
            checker.Check("decisionlog.collision.eightbits", wideBranch == 5);

            checker.Check("decisionlog.fresh.records", DecisionLog.IsRecording(DecisionLog.Fresh));
            short recording = DecisionLog.Fresh;
            DecisionLog.Push(ref recording, 0);
            checker.Check("decisionlog.recording", DecisionLog.IsRecording(recording));
        }

        // ---- opcode vocabulary ----------------------------------------------------------------

        private static void CheckOpcodes(Checker checker)
        {
            checker.Check("opcodes.end.keyword", PlanOpcodes.KeywordOf(PlanWord.EndPlanOpcode) == "END_PLAN");
            checker.Check("opcodes.declare.keyword", PlanOpcodes.KeywordOf(PlanOpcodes.OpcodeDeclareBehavior) == "DECLARE_BEHAVIOR");
            checker.Check("opcodes.unknown.keyword", PlanOpcodes.KeywordOf(0x12345678) == "op_1234_5678");
            checker.Check("opcodes.known.count", PlanOpcodes.IsKnownOpcode(PlanOpcodes.OpcodeEnable) && !PlanOpcodes.IsKnownOpcode(0x12345678));
            checker.Check("opcodes.action.supported", PlanOpcodes.IsSupportedActionId(5) && PlanOpcodes.IsSupportedActionId(93) && PlanOpcodes.IsSupportedActionId(139));
            checker.Check("opcodes.action.missing", !PlanOpcodes.IsSupportedActionId(2) && !PlanOpcodes.IsSupportedActionId(14) && !PlanOpcodes.IsSupportedActionId(15) && !PlanOpcodes.IsSupportedActionId(44) && !PlanOpcodes.IsSupportedActionId(121));
            checker.Check("opcodes.action.range", !PlanOpcodes.IsSupportedActionId(-1) && !PlanOpcodes.IsSupportedActionId(140));
            checker.Check("opcodes.decision.supported", PlanOpcodes.IsSupportedDecisionId(0) && PlanOpcodes.IsSupportedDecisionId(138) && !PlanOpcodes.IsSupportedDecisionId(139));
            checker.Check("opcodes.behparam.supported", PlanOpcodes.IsSupportedBehaviorParamId(0) && PlanOpcodes.IsSupportedBehaviorParamId(16) && !PlanOpcodes.IsSupportedBehaviorParamId(17));
            checker.Check("opcodes.interactorparam.supported", PlanOpcodes.IsSupportedInteractorParamId(0) && PlanOpcodes.IsSupportedInteractorParamId(1) && !PlanOpcodes.IsSupportedInteractorParamId(2));
            checker.Check("opcodes.behaviourkind.supported", PlanOpcodes.IsSupportedBehaviorKind(0) && PlanOpcodes.IsSupportedBehaviorKind(24) && !PlanOpcodes.IsSupportedBehaviorKind(25));
            checker.Check("opcodes.teammate.slot0", PlanOpcodes.TeammateSlot(0) == 2);
            checker.Check("opcodes.teammate.slot1", PlanOpcodes.TeammateSlot(1) == 3);
            checker.Check("opcodes.teammate.slot2", PlanOpcodes.TeammateSlot(2) == 0);
            checker.Check("opcodes.teammate.slot3", PlanOpcodes.TeammateSlot(3) == 1);
            checker.Check("opcodes.teammate.goalieA", PlanOpcodes.TeammateSlot(4) == 0);
            checker.Check("opcodes.teammate.goalieB", PlanOpcodes.TeammateSlot(5) == 1);
            checker.Check("opcodes.category.names", PlanOpcodes.CategoryName(PlanOpcodes.OpcodeEnable) == "enable" && PlanOpcodes.CategoryName(0x12345678) == "unknown");
            checker.Check("opcodes.argument.enum", PlanOpcodes.ArgumentEnumFor(PlanOpcodes.OpcodeDoAction, 0) == "ActionIds" && PlanOpcodes.ArgumentEnumFor(PlanOpcodes.OpcodeSetTimeoutMsg, 1) == "MessageIds");
            checker.Check("opcodes.action.names", PlanOpcodes.ActionName(5) == "SendMessage" && PlanOpcodes.ActionName(14) == "ACF_14_UNMAPPED");
        }

        // ---- operand model --------------------------------------------------------------------

        private static void CheckWordModel(Checker checker)
        {
            PlanWord pronoun = new PlanWord(PlanWord.Pack(PlanWord.PronounTag, 7));
            checker.Check("word.pronoun.pack", pronoun.IsPronoun && pronoun.High == PlanWord.PronounTag && pronoun.Low == 7);
            PlanWord local = new PlanWord(PlanWord.Pack(PlanWord.LocalRealTag, 2));
            checker.Check("word.local.pack", local.IsLocalReal && local.Low == 2);
            PlanWord name = new PlanWord(PlanWord.Pack(PlanWord.InteractorNameTag, 1));
            checker.Check("word.interactorname.pack", name.IsInteractorName && name.Low == 1);
            checker.Check("word.float.bits", Math.Abs(new PlanWord(unchecked((int)0x3F800000)).AsFloat - 1.0f) < 0.000001f);
            checker.Check("word.tokens", new PlanWord(PlanWord.TokenNoBehaviourChange).IsNoBehaviourChange && new PlanWord(PlanWord.TokenPreviousBehaviour).IsPreviousBehaviour);
            checker.Check("word.equality", new PlanWord(7).Equals(new PlanWord(7)) && new PlanWord(7).ToString() == "0x00000007");
        }

        // ---- compile-time argument resolution -------------------------------------------------

        private static void CheckArgResolver(Checker checker)
        {
            int rawLead = PlanWord.Pack(PlanWord.PronounTag, 2);
            int rawOther = PlanWord.Pack(PlanWord.PronounTag, 3);
            int rawGoal = PlanWord.Pack(PlanWord.PronounTag, 4);
            int rawGoalSide = PlanWord.Pack(PlanWord.PronounTag, 5);
            int rawGoalZone = PlanWord.Pack(PlanWord.PronounTag, 6);
            int rawGoaliesGoal = PlanWord.Pack(PlanWord.PronounTag, 7);
            int rawViewOwner = PlanWord.Pack(PlanWord.PronounTag, 15);
            int rawBody = PlanWord.Pack(PlanWord.PronounTag, 32);
            int rawGoalie = PlanWord.Pack(PlanWord.PronounTag, 35);
            int rawRamp = PlanWord.Pack(PlanWord.PronounTag, 50);
            int rawMateCob = PlanWord.Pack(PlanWord.PronounTag, 51);

            PlanArgResolver slot0 = PlanArgResolver.ForSlot(0);
            checker.Check("arg.slot0.lead", slot0.ResolvePronoun(2, new PlanWord(rawLead)) == 1);
            checker.Check("arg.slot0.other", slot0.ResolvePronoun(3, new PlanWord(rawOther)) == 3);
            checker.Check("arg.slot0.goal", slot0.ResolvePronoun(4, new PlanWord(rawGoal)) == PlanOpcodes.SlotNgoal);
            checker.Check("arg.slot0.goalside", slot0.ResolvePronoun(5, new PlanWord(rawGoalSide)) == PlanOpcodes.CollisionNgoalSide);
            checker.Check("arg.slot0.goalzone", slot0.ResolvePronoun(6, new PlanWord(rawGoalZone)) == PlanOpcodes.CollisionNgoalZone);
            checker.Check("arg.slot0.oppgoal", slot0.ResolvePronoun(7, new PlanWord(rawGoaliesGoal)) == PlanOpcodes.SlotSgoal);

            PlanArgResolver slot1 = PlanArgResolver.ForSlot(1);
            checker.Check("arg.slot1.goal", slot1.ResolvePronoun(4, new PlanWord(rawGoal)) == PlanOpcodes.SlotSgoal);
            checker.Check("arg.slot1.other", slot1.ResolvePronoun(3, new PlanWord(rawOther)) == 2);
            checker.Check("arg.slot1.viewowner", slot1.ResolvePronoun(15, new PlanWord(rawViewOwner)) == rawViewOwner);
            checker.Check("arg.slot1.body", slot1.ResolvePronoun(32, new PlanWord(rawBody)) == 1 * PlanOpcodes.CollisionPerDromer + PlanOpcodes.CollisionDromerBody0);
            checker.Check("arg.slot1.goalie", slot1.ResolvePronoun(35, new PlanWord(rawGoalie)) == PlanOpcodes.GoalieSlotB);
            checker.Check("arg.slot1.ramp", slot1.ResolvePronoun(50, new PlanWord(rawRamp)) == 1 + (PlanOpcodes.CollisionRamp - PlanOpcodes.SlotRamp));
            checker.Check("arg.slot1.matecob", slot1.ResolvePronoun(51, new PlanWord(rawMateCob)) == PlanOpcodes.TeammateSlot(1) * PlanOpcodes.CollisionPerDromer);

            PlanArgResolver goalieA = PlanArgResolver.ForSlot(4);
            checker.Check("arg.goalieA.mate", goalieA.ResolvePronoun(1, new PlanWord(PlanWord.Pack(PlanWord.PronounTag, 1))) == 0);
            checker.Check("arg.goalieA.lead", goalieA.ResolvePronoun(2, new PlanWord(rawLead)) == 1);

            bool allResolve = true;

            for (int slot = 0; slot < 6; slot++)
            {
                PlanArgResolver resolver = PlanArgResolver.ForSlot(slot);

                for (int pronoun = 0; pronoun < 57; pronoun++)
                {
                    try
                    {
                        resolver.ResolvePronoun(pronoun, new PlanWord(PlanWord.Pack(PlanWord.PronounTag, pronoun)));
                    }
                    catch (PlanVmException)
                    {
                        allResolve = false;
                    }
                }
            }

            checker.Check("arg.all57.resolve", allResolve);
        }

        // ---- PBN stream decoder ---------------------------------------------------------------

        private static void CheckParser(Checker checker)
        {
            PlanProgram reinit = PlanStreamParser.Parse(Image("REINIT", PlanState.ReinitializeImage), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.reinit.nodes", reinit.TopLevelNodes.Length == 2);
            checker.Check("parser.reinit.first", reinit.TopLevelNodes[0].Opcode == PlanOpcodes.OpcodeDeclareBehavior && reinit.TopLevelNodes[0].ArgCount == 2);
            checker.Check("parser.reinit.kind", reinit.TopLevelNodes[0].Args[0].Kind == PlanOperandKind.Leaf && reinit.TopLevelNodes[0].Args[0].IntValue == 6);
            checker.Check("parser.reinit.string", reinit.TopLevelNodes[0].Args[1].Kind == PlanOperandKind.String && reinit.TopLevelNodes[0].Args[1].Text == "x");
            checker.Check("parser.reinit.end", reinit.TopLevelNodes[1].IsEndPlan);
            checker.Check("parser.reinit.consumed", reinit.ConsumedWords == PlanState.ReinitializeImage.Length && reinit.BehaviorDeclarationCount == 1);
            checker.Check("parser.reinit.maxstring", reinit.MaxStringLength == 1 && reinit.StringsRead == 1);

            PlanProgram endOnly = PlanStreamParser.Parse(Image("ENDONLY", End()), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.endonly.words", endOnly.ConsumedWords == 2 && endOnly.TopLevelNodes.Length == 1 && endOnly.TopLevelNodes[0].IsEndPlan);

            int[] masked = new int[] { PlanWord.MarkerNode, PlanOpcodes.OpcodeBlock, 0x8002, 1, 2, PlanWord.MarkerNode, PlanWord.EndPlanOpcode };
            PlanProgram maskedProgram = PlanStreamParser.Parse(Image("MASK", masked), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.argcount.mask", maskedProgram.TopLevelNodes[0].ArgCount == unchecked((short)0x8002) && maskedProgram.TopLevelNodes[0].ChildCount == 2);

            PlanFileImage stringImage = Image("STR",
                Cat(
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("Ab")),
                    End()));
            PlanProgram stringProgram = PlanStreamParser.Parse(stringImage, PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.string.read", stringProgram.MaxStringLength == 2 && stringProgram.StringsRead == 1 && stringProgram.TopLevelNodes[0].Args[1].Text == "Ab");
            checker.Check("parser.string.raw", stringProgram.TopLevelNodes[0].Args[1].StringWords.Length == 2 && stringProgram.TopLevelNodes[0].Args[1].StringWords[0].Value == 'A');

            checker.Throws<PlanVmException>("parser.missing.end", delegate
            {
                PlanStreamParser.Parse(Image("NOEND", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")))), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            });

            checker.Throws<PlanVmException>("parser.truncated.node", delegate
            {
                PlanStreamParser.Parse(Image("TRUNC", new int[] { PlanWord.MarkerNode }), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            });

            checker.Throws<PlanVmException>("parser.truncated.string", delegate
            {
                PlanStreamParser.Parse(Image("TRUNCSTR", new int[] { PlanWord.MarkerNode, PlanOpcodes.OpcodeDeclareBehavior, 1, PlanWord.MarkerString, 'A' }), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            });

            StringBuilder longName = new StringBuilder();
            for (int index = 0; index < 32; index++)
            {
                longName.Append('a');
            }

            checker.Throws<PlanVmException>("parser.string.too.long", delegate
            {
                PlanStreamParser.Parse(Image("LONG", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S(longName.ToString())), End())), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            });

            checker.Throws<PlanVmException>("parser.top.level.leaf", delegate
            {
                PlanStreamParser.Parse(Image("TOPLEAF", new int[] { 5, PlanWord.MarkerNode, PlanWord.EndPlanOpcode }), PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            });

            PlanFileImage unknownImage = Image("UNKNOWN", Cat(N(0x12345678, L(1)), End()));
            PlanProgram unknownProgram = PlanStreamParser.Parse(unknownImage, PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.unknown.opcode", !PlanOpcodes.IsKnownOpcode(unknownProgram.TopLevelNodes[0].Opcode) && PlanOpcodes.KeywordOf(unknownProgram.TopLevelNodes[0].Opcode) == "op_1234_5678");

            int pronounRaw = PlanWord.Pack(PlanWord.PronounTag, 2);
            PlanFileImage pronounImage = Image("PRONOUN", Cat(N(PlanOpcodes.OpcodeBlock, L(pronounRaw)), End()));
            PlanProgram pronounProgram = PlanStreamParser.Parse(pronounImage, PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.pronoun.raw", pronounProgram.TopLevelNodes[0].Args[0].Raw.Value == pronounRaw);
            checker.Check("parser.pronoun.resolved", pronounProgram.TopLevelNodes[0].Args[0].IntValue == 1);

            FixedPlanNameResolver names = new FixedPlanNameResolver();
            names.Add("Sideln", 7);
            PlanFileImage planRefImage = Image("PLANREF", Cat(N(PlanOpcodes.OpcodeChangeOfPlan, S("Sideln"), L(1)), End()));
            PlanProgram planRefProgram = PlanStreamParser.Parse(planRefImage, PlanArgResolver.ForSlot(0), names);
            checker.Check("parser.planref.resolved", planRefProgram.TopLevelNodes[0].Args[0].IsPlanReference && planRefProgram.TopLevelNodes[0].Args[0].IntValue == 7);

            PlanProgram missingRef = PlanStreamParser.Parse(planRefImage, PlanArgResolver.ForSlot(0), new NullPlanNameResolver());
            checker.Check("parser.planref.missing", missingRef.TopLevelNodes[0].Args[0].IntValue == -1);

            PlanStatistics statistics = PlanAnalyzer.Analyze(Image("ANALYZE", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")), End())), PlanStreamParser.Parse(Image("ANALYZE", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")), End())), PlanArgResolver.ForSlot(0), new NullPlanNameResolver()));
            checker.Check("analyze.counts", statistics.Nodes == 2 && statistics.Leaves == 1 && statistics.Strings == 1 && statistics.Behaviors == 1 && statistics.MaxDepth == 0);
            checker.Check("analyze.digest.stable", PlanAnalyzer.Analyze(Image("ANALYZE", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")), End())), PlanStreamParser.Parse(Image("ANALYZE", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")), End())), PlanArgResolver.ForSlot(0), new NullPlanNameResolver())).Digest == statistics.Digest);
            checker.Check("analyze.leafkinds", PlanAnalyzer.ClassifyLeaf(PlanWord.Pack(PlanWord.PronounTag, 1)) == "pronoun" && PlanAnalyzer.ClassifyLeaf(PlanWord.TokenMessageData) == "sentinel" && PlanAnalyzer.ClassifyLeaf(3) == "int");
        }

        // ---- loader ---------------------------------------------------------------------------

        private static void CheckLoader(Checker checker)
        {
            string path = Path.Combine(Path.GetTempPath(), "ublade3-planvm-check-" + Guid.NewGuid().ToString("N") + ".pbn");

            try
            {
                byte[] words = new byte[4 + 8];
                words[0] = 2;
                words[4] = 0xFF;
                words[5] = 0xEF;
                words[6] = 0xCD;
                words[7] = 0xAB;

                File.WriteAllBytes(path, words);

                PlanFileImage image = PlanFileLoader.Load(path, "TEMP");
                checker.Check("loader.count", image.DeclaredWordCount == 2 && image.WordCount == 2);
                checker.Check("loader.word", unchecked((uint)image.Words[0].Value) == 0xABCDEFFF);

                byte[] truncated = new byte[8];
                truncated[0] = 4;
                File.WriteAllBytes(path, truncated);

                checker.Throws<PlanFileException>("loader.truncated", delegate { PlanFileLoader.Load(path, "TEMP"); });

                byte[] trailing = new byte[12];
                trailing[0] = 1;
                File.WriteAllBytes(path, trailing);

                checker.Throws<PlanFileException>("loader.trailing", delegate { PlanFileLoader.Load(path, "TEMP"); });
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        // ---- plan state -----------------------------------------------------------------------

        private static void CheckState(Checker checker)
        {
            PlanVmRecordingHost reinitHost = Host(0);
            PlanState reinitState = new PlanState(reinitHost, "reinit");
            reinitState.Reinitialize();
            checker.Check("state.reinit.count", reinitState.BehaviorNodeCount == 2);
            checker.Check("state.reinit.instantiate", reinitState.Instantiate());
            checker.Check("state.reinit.head", reinitState.Head != null && reinitState.Head.Name == "x" && reinitState.Head.Kind == 6);
            checker.Check("state.reinit.current", ReferenceEquals(reinitState.Current, reinitState.Head) && reinitState.Head.Standby == null);
            checker.Check("state.reinit.rearm", reinitState.CollisionRearmTicks == PlanState.CollisionRearmTicksInitial && reinitState.Index == 0 && reinitState.Previous == 0);

            PlanVmRecordingHost host = Host(0);
            PlanState state = new PlanState(host, "three");
            checker.Check("state.three.compile", state.SetAndInit(ThreeBehaviorPlan()));
            checker.Check("state.three.count", state.BehaviorNodeCount == 4 && state.Program.BehaviorDeclarationCount == 3);
            checker.Check("state.three.instantiate", state.Instantiate());
            checker.Check("state.three.chain", state.Head.Name == "A" && state.Head.Standby.Name == "B" && state.Head.Standby.Standby.Name == "C" && state.Head.Standby.Standby.Standby == null);
            checker.Check("state.three.sync", state.Head.SyncValue == 0 && state.Head.Standby.SyncValue == 1 && state.Head.Standby.Standby.SyncValue == 2);
            checker.Check("state.three.interactors", state.InteractorCount == 0 && state.CollInteractors == null && state.MsgInteractors == null);

            host.ResetTelemetry();
            state.ActivateBehavior(0);
            checker.Check("state.activate.passon", state.Current.Name == "C" && state.Index == 0 && state.Previous == 0);
            checker.Check("state.activate.ticks", host.TickAndSortCount == 3 && host.TimedMessageCount == 3 && state.Current.PollCountdown == state.Current.Latency);
            checker.Check("state.activate.cursor", state.CursorIndex == 2);

            state.ActivateBehavior(1);
            checker.Check("state.activate.index", state.Index == 1 && state.Previous == 0 && state.Current.Name == "C");
            checker.Check("state.activate.previous", state.GetBehaviorByIndex(unchecked((uint)PlanWord.TokenPreviousBehaviour)).Name == "A");
            checker.Check("state.get.behavior", state.GetBehaviorByIndex(2).Name == "C");
            checker.Check("state.get.null", state.GetBehaviorByIndex(3) == null);
            checker.Throws<PlanVmException>("state.get.offchain", delegate { state.GetBehaviorByIndex(4); });

            PlanFileImage endOnly = Image("ENDONLY", End());
            PlanVmRecordingHost endHost = Host(0);
            PlanState endState = new PlanState(endHost, "end");
            endState.SetAndInit(endOnly);
            checker.Throws<PlanVmException>("state.end.before.behavior", delegate { endState.Instantiate(); });

            PlanFileImage badKind = Image("BADKIND", Cat(N(PlanOpcodes.OpcodeDeclareBehavior, L(25), S("X")), End()));
            PlanVmRecordingHost badHost = Host(0);
            PlanState badState = new PlanState(badHost, "bad");
            badState.SetAndInit(badKind);
            checker.Throws<PlanVmException>("state.kind.range", delegate { badState.Instantiate(); });

            PlanVmRecordingHost teardownHost = Host(0);
            PlanState teardownState = new PlanState(teardownHost, "teardown");
            teardownState.SetAndInit(ThreeBehaviorPlan());
            teardownState.Instantiate();
            teardownState.Teardown();
            checker.Check("state.teardown", teardownState.Head == null && teardownState.Current == null && teardownHost.ClearObserverCount == 1 && teardownHost.ResetCount == 3);
        }

        private static void CheckBehaviorSetup(Checker checker)
        {
            PlanFileImage invalid = Image("BADBEHAVIOUR",
                Cat(
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A"), N(PlanOpcodes.OpcodeDecideByAmong, L(5), N(PlanOpcodes.OpcodeBlock, L(1)))),
                    End()));
            PlanVmRecordingHost host = Host(0);
            PlanState state = new PlanState(host, "badbehaviour");
            state.SetAndInit(invalid);

            checker.Throws<PlanVmException>("behaviour.setup.invalid", delegate { state.Instantiate(); });

            PlanFileImage globalInvalid = Image("BADGLOBAL",
                Cat(
                    N(PlanOpcodes.OpcodeGlobalInteractors, L(1)),
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")),
                    End()));
            PlanVmRecordingHost globalHost = Host(0);
            PlanState globalState = new PlanState(globalHost, "badglobal");
            globalState.SetAndInit(globalInvalid);

            checker.Throws<PlanVmException>("behaviour.global.invalid", delegate { globalState.Instantiate(); });
        }

        // ---- interactor scripts ---------------------------------------------------------------

        private static PlanFileImage ScriptedPlan(int enableOpcode, params int[][] enableChildren)
        {
            return Image("SCRIPTED",
                Cat(
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(0), S("A"),
                        N(PlanOpcodes.OpcodeSetMessageInteractor, L(76), N(PlanOpcodes.OpcodeEnable, enableChildren))),
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("B")),
                    End()));
        }

        private static PlanMessageInteractor FirstMessage(PlanState state)
        {
            return (PlanMessageInteractor)state.Head.MsgInteractors;
        }

        private static void CheckScriptDispatch(Checker checker)
        {
            PlanFileImage plan = ScriptedPlan(
                PlanOpcodes.OpcodeEnable,
                N(PlanOpcodes.OpcodeDoAction, L(5), L(800)),
                N(PlanOpcodes.OpcodeBlock, N(PlanOpcodes.OpcodeDoAction, L(7), L(9))),
                N(PlanOpcodes.OpcodeChangeToBehavior, L(1)),
                N(PlanOpcodes.OpcodeChangeOfPlan, S("Sideln"), L(1)));
            PlanVmRecordingHost host = Host(0);
            PlanState state = new PlanState(host, "script");
            state.SetAndInit(plan);
            checker.Check("script.instantiate", state.Instantiate());
            PlanMessageInteractor message = FirstMessage(state);
            checker.Check("script.setup", message != null && message.EnableNode != null && state.Head.MsgInteractors == message);

            host.ResetTelemetry();
            short decisionState = DecisionLog.Fresh;
            message.Dispatch(42, ref decisionState, host);
            checker.Check("script.actions", host.Actions.Count == 2 && host.Actions[0] == 5 && host.Actions[1] == 7);
            checker.Check("script.behaviour.change", state.Current.Name == "B" && state.Index == 1 && host.ExitBehaviorCount == 2);
            checker.Check("script.plan.change", host.Changes.Count == 1 && host.Changes[0] == "3:1");
            checker.Check("script.rearm", state.CollisionRearmTicks == PlanState.CollisionRearmTicksAfterRun);
            checker.Check("script.no.decisions", host.Decisions.Count == 0 && decisionState == DecisionLog.Fresh);

            PlanVmRecordingHost replayHost = Host(0);
            PlanState replayState = new PlanState(replayHost, "replay");
            replayState.SetAndInit(plan);
            replayState.Instantiate();
            replayHost.ResetTelemetry();
            short replayWord = DecisionLog.Fresh;
            FirstMessage(replayState).Dispatch(42, ref replayWord, replayHost);
            checker.Check("script.trace.equal", replayHost.Actions.Count == 2 && replayHost.Actions[0] == 5 && replayHost.Actions[1] == 7);

            PlanFileImage localAction = ScriptedPlan(PlanOpcodes.OpcodeEnable, N(PlanOpcodes.OpcodeDoLocalAction, L(6)));
            PlanVmRecordingHost localHost = Host(0);
            PlanState localState = new PlanState(localHost, "local");
            localState.SetAndInit(localAction);
            localState.Instantiate();
            short localDecision = DecisionLog.Fresh;
            FirstMessage(localState).Dispatch(0, ref localDecision, localHost);
            checker.Check("script.local.action", localHost.Actions.Count == 1 && localHost.Actions[0] == 6);

            PlanVmRecordingHost networkHost = Host(0);
            networkHost.Phase = PlanPhase.Network;
            PlanState networkState = new PlanState(networkHost, "network");
            networkState.SetAndInit(localAction);
            networkState.Instantiate();
            short networkDecision = DecisionLog.Fresh;
            FirstMessage(networkState).Dispatch(0, ref networkDecision, networkHost);
            checker.Check("script.local.skipped.on.network", networkHost.Actions.Count == 0);

            PlanFileImage netAction = ScriptedPlan(PlanOpcodes.OpcodeEnable, N(PlanOpcodes.OpcodeDoNetAction, L(6)));
            PlanVmRecordingHost netHost = Host(0);
            netHost.Phase = PlanPhase.Network;
            PlanState netState = new PlanState(netHost, "net");
            netState.SetAndInit(netAction);
            netState.Instantiate();
            short netDecision = DecisionLog.Fresh;
            FirstMessage(netState).Dispatch(0, ref netDecision, netHost);
            checker.Check("script.net.action", netHost.Actions.Count == 1 && netHost.Actions[0] == 6);

            checker.Throws<PlanVmException>("script.leaf.instruction", delegate
            {
                PlanFileImage leafScript = ScriptedPlan(PlanOpcodes.OpcodeEnable, L(5));
                PlanVmRecordingHost leafHost = Host(0);
                PlanState leafState = new PlanState(leafHost, "leaf");
                leafState.SetAndInit(leafScript);
                leafState.Instantiate();
                short word = DecisionLog.Fresh;
                FirstMessage(leafState).Dispatch(0, ref word, leafHost);
            });
        }

        private static PlanFileImage DecidePlan(int opcode, params int[][] arms)
        {
            int[][] children = new int[1 + arms.Length][];
            children[0] = L(5);

            for (int index = 0; index < arms.Length; index++)
            {
                children[1 + index] = arms[index];
            }

            return Image("DECIDE",
                Cat(
                    N(PlanOpcodes.OpcodeGlobalInteractors,
                        N(PlanOpcodes.OpcodeSetMessageInteractor, L(76), N(PlanOpcodes.OpcodeEnable, N(opcode, children)))),
                    N(PlanOpcodes.OpcodeDeclareBehavior, L(6), S("A")),
                    End()));
        }

        private static PlanMessageInteractor GlobalMessage(PlanState state)
        {
            return (PlanMessageInteractor)state.MsgInteractors;
        }

        private static void CheckDecideReplay(Checker checker)
        {
            PlanFileImage recordPlan = DecidePlan(
                PlanOpcodes.OpcodeDecideByAmong,
                N(PlanOpcodes.OpcodeDoAction, L(5), L(1)),
                N(PlanOpcodes.OpcodeDoAction, L(93), L(2)));

            PlanVmRecordingHost recordHost = Host(0);
            recordHost.DecisionPolicy = delegate(int id, PlanNode node) { return (short)1; };
            PlanState recordState = new PlanState(recordHost, "record");
            recordState.SetAndInit(recordPlan);
            recordState.Instantiate();
            recordHost.ResetTelemetry();
            short log = DecisionLog.Fresh;
            GlobalMessage(recordState).Dispatch(7, ref log, recordHost);
            checker.Check("decide.record.branch1", recordHost.Actions.Count == 1 && recordHost.Actions[0] == 93 && recordHost.Decisions.Count == 1 && recordHost.Decisions[0] == 5);
            checker.Check("decide.record.log", DecisionLog.IsRecording(log) && log == unchecked((short)0x9001));

            PlanVmRecordingHost replayHost = Host(0);
            PlanState replayState = new PlanState(replayHost, "replay");
            replayState.SetAndInit(recordPlan);
            replayState.Instantiate();
            replayHost.ResetTelemetry();
            short replayLog = unchecked((short)(log & 0x3FFF));
            GlobalMessage(replayState).Dispatch(7, ref replayLog, replayHost);
            checker.Check("decide.replay.branch1", replayHost.Actions.Count == 1 && replayHost.Actions[0] == 93 && replayHost.Decisions.Count == 0);
            checker.Check("decide.replay.log.empty", replayLog == 0);

            PlanVmRecordingHost recordZeroHost = Host(0);
            recordZeroHost.DecisionPolicy = delegate(int id, PlanNode node) { return (short)0; };
            PlanState recordZeroState = new PlanState(recordZeroHost, "recordzero");
            recordZeroState.SetAndInit(recordPlan);
            recordZeroState.Instantiate();
            recordZeroHost.ResetTelemetry();
            short logZero = DecisionLog.Fresh;
            GlobalMessage(recordZeroState).Dispatch(7, ref logZero, recordZeroHost);
            checker.Check("decide.record.branch0", recordZeroHost.Actions.Count == 1 && recordZeroHost.Actions[0] == 5);

            PlanFileImage withAmong = DecidePlan(
                PlanOpcodes.OpcodeDecideByWithAmong,
                N(PlanOpcodes.OpcodeDataBlock, L(5)),
                N(PlanOpcodes.OpcodeDoAction, L(5), L(1)),
                N(PlanOpcodes.OpcodeDoAction, L(93), L(2)));
            PlanVmRecordingHost withHost = Host(0);
            PlanState withState = new PlanState(withHost, "withamong");
            withState.SetAndInit(withAmong);
            withState.Instantiate();
            withHost.ResetTelemetry();
            short withLog = DecisionLog.Fresh;
            GlobalMessage(withState).Dispatch(0, ref withLog, withHost);
            checker.Check("decide.withamong.default", withHost.Decisions.Count == 1 && withHost.Actions.Count == 1 && withHost.Actions[0] == 5);

            checker.Throws<PlanVmException>("decide.withamong.datablock.throws", delegate
            {
                PlanVmRecordingHost blockHost = Host(0);
                PlanState blockState = new PlanState(blockHost, "block");
                blockState.SetAndInit(withAmong);
                blockState.Instantiate();
                short blockLog = DecisionLog.Fresh;
                DecisionLog.Push(ref blockLog, 0);
                blockLog = unchecked((short)(blockLog & 0x3FFF));
                GlobalMessage(blockState).Dispatch(0, ref blockLog, blockHost);
            });
        }

        private static void CheckUnsupportedActions(Checker checker)
        {
            PlanFileImage plan = ScriptedPlan(PlanOpcodes.OpcodeEnable, N(PlanOpcodes.OpcodeDoAction, L(14)));
            PlanVmRecordingHost host = Host(0);
            PlanState state = new PlanState(host, "unsupported");
            state.SetAndInit(plan);
            state.Instantiate();

            checker.Throws<PlanVmUnsupportedHandlerException>("unsupported.action14", delegate
            {
                short word = DecisionLog.Fresh;
                FirstMessage(state).Dispatch(0, ref word, host);
            });

            PlanFileImage badParam = ScriptedPlan(PlanOpcodes.OpcodeEnable, N(PlanOpcodes.OpcodeSetBehaviorParam, L(17)));
            PlanVmRecordingHost paramHost = Host(0);
            PlanState paramState = new PlanState(paramHost, "badparam");
            paramState.SetAndInit(badParam);
            paramState.Instantiate();

            checker.Throws<PlanVmUnsupportedHandlerException>("unsupported.behparam17", delegate
            {
                short word = DecisionLog.Fresh;
                FirstMessage(paramState).Dispatch(0, ref word, paramHost);
            });
        }
    }
}
