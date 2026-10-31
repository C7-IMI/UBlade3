using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The decision log: src/planvm/decisionlog.h. A local object records the branch each DECIDE
    /// takes; its network copy replays the branches oldest first. The packing is exact, including
    /// the third-branch masking fault the original has (Interactor::ExecuteScript, HYPERX 0x0045d860).
    /// </summary>
    public static class DecisionLog
    {
        public const short Recording = unchecked((short)0x8000);

        public const short CountMask = 0x3000;

        public const short One = 0x1000;

        public const short Two = 0x2000;

        public const short Fresh = -1;

        public static bool IsRecording(short word)
        {
            return (word & Recording) != 0;
        }

        /// <summary>Pushes one branch; the word is the short ExecutionScript carries as *pState.</summary>
        public static void Push(ref short word, short branch)
        {
            if (word == Fresh)
            {
                word = 0;
            }

            int nWord = word;
            int nBranch = (ushort)branch;

            if ((nWord & CountMask) == 0)
            {
                word = unchecked((short)((nWord & 0xFFF) | nBranch | Recording | One));
            }
            else if ((nWord & CountMask) == One)
            {
                word = unchecked((short)((nBranch << 7) | (nWord & 0x7F) | Recording | Two));
            }
            else
            {
                // The third branch is masked where it should be shifted, so its replay takes bits 9-11 (0x0045d963).
                word = unchecked((short)(((nWord >> 2) & 0x1E0) | (nWord & 0x1F) | (nBranch & 0xE00) | Recording | CountMask));
            }
        }

        /// <summary>Pops the oldest branch; the word is the short ExecutionScript carries as *pState.</summary>
        public static void Pop(ref short word, ref short branch)
        {
            int nWord = word;

            if ((nWord & CountMask) == One)
            {
                branch = unchecked((short)(nWord & 0xFFF));
                word = 0;
            }
            else if ((nWord & CountMask) == Two)
            {
                branch = unchecked((short)(nWord & 0x7F));
                word = unchecked((short)(((nWord >> 7) & ~0x1E0) | One));
            }
            else
            {
                branch = unchecked((short)(nWord & 0x1F));
                word = unchecked((short)((unchecked((short)(((nWord >> 3) & 0x3C) | (nWord & 0xE00))) >> 2) | Two));
            }
        }
    }
}
