using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// The small deterministic math surface the plan AI slice needs: Vec3 and the original's
    /// SquirrelNoise5 random. Both are ports, not stand-ins: Vec3 mirrors src/math/vec3.cpp and
    /// src/math/math.cpp (Vec3Normalize, HYPERX 0x00430110), the random mirrors
    /// src/math/squirrel5.h (Squirrel Eiserloh's stateless hash, the image's Math::Rand).
    /// </summary>
    public struct PlanAiVec3
    {
        public float X;

        public float Y;

        public float Z;

        public PlanAiVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static PlanAiVec3 operator +(PlanAiVec3 left, PlanAiVec3 right)
        {
            return new PlanAiVec3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static PlanAiVec3 operator -(PlanAiVec3 left, PlanAiVec3 right)
        {
            return new PlanAiVec3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static PlanAiVec3 operator *(PlanAiVec3 value, float scale)
        {
            return new PlanAiVec3(value.X * scale, value.Y * scale, value.Z * scale);
        }

        /// <summary>Vec3::DotInline.</summary>
        public static float Dot(PlanAiVec3 left, PlanAiVec3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        /// <summary>Vec3::Length; the original squares with floats and takes a double sqrt.</summary>
        public float Length
        {
            get { return (float)Math.Sqrt(Dot(this, this)); }
        }

        /// <summary>Math::Vec3Normalize: a double length, floats divided by it, zero when under 1e-7.</summary>
        public PlanAiVec3 NormalizedCopy()
        {
            PlanAiVec3 copy = this;
            copy.NormalizeInPlace();

            return copy;
        }

        public bool NormalizeInPlace()
        {
            double length = Math.Sqrt((double)Dot(this, this));

            if (length > PlanAiConstants.NormalizeEpsilon)
            {
                X = (float)(X / length);
                Y = (float)(Y / length);
                Z = (float)(Z / length);

                return true;
            }

            X = 0.0f;
            Y = 0.0f;
            Z = 0.0f;

            return false;
        }

        public override string ToString()
        {
            return "(" + X.ToString("R") + "," + Y.ToString("R") + "," + Z.ToString("R") + ")";
        }
    }

    /// <summary>src/math/squirrel5.h: SeedRand/Rand, the image's deterministic random.</summary>
    public static class PlanAiRandom
    {
        public const int RandMax = 0x7FFFFFFF;

        private static uint seed;

        private static uint position;

        public static void Seed(uint value)
        {
            seed = value;
            position = 0;
        }

        public static uint Squirrel5(uint atPosition, uint withSeed)
        {
            unchecked
            {
                const uint BitNoise1 = 0xD2A80A23;
                const uint BitNoise2 = 0xA884F197;
                const uint BitNoise3 = 0x6C736F4B;
                const uint BitNoise4 = 0xB79F3ABB;
                const uint BitNoise5 = 0x1B56C4F5;

                uint mangled = atPosition * BitNoise1;
                mangled += withSeed;
                mangled ^= mangled >> 9;
                mangled += BitNoise2;
                mangled ^= mangled >> 11;
                mangled *= BitNoise3;
                mangled ^= mangled >> 13;
                mangled += BitNoise4;
                mangled ^= mangled << 15;
                mangled *= BitNoise5;
                mangled ^= mangled >> 17;

                return mangled;
            }
        }

        /// <summary>Math::Rand: a uniform integer in [0, kRandMax].</summary>
        public static int Rand()
        {
            unchecked
            {
                return (int)(Squirrel5(position++, seed) >> 1);
            }
        }

        public static float RandSigned()
        {
            float unit = (float)Rand() * (1.0f / (float)RandMax);

            return unit * 2.0f - 1.0f;
        }

        public static float RandMagnitude()
        {
            float roll = RandSigned();

            return roll <= 0.0f ? -roll : roll;
        }
    }

    /// <summary>The constants the plans and the ported handlers read, cited from the decompilation.</summary>
    public static class PlanAiConstants
    {
        /// <summary>GLOBAL HYPERX 0x004cfa.. g_flPiPlan, globals.cpp: 3.1415927410125732f.</summary>
        public const float PiPlan = 3.1415927410125732f;

        /// <summary>globals.cpp g_flThreePlan.</summary>
        public const float ThreePlan = 3.0f;

        public const float Zero = 0.0f;

        public const float One = 1.0f;

        /// <summary>src/common/staticinitglobals.cpp g_flHalfRokObject.</summary>
        public const float HalfRokObject = 0.5f;

        public const float NormalizeEpsilon = 1e-07f;

        /// <summary>src/engine/modelids.h kModelIDNone.</summary>
        public const int ModelIdNone = 0x66;

        /// <summary>src/engine/engineobject.h kSceneMessageCount.</summary>
        public const int SceneMessageCount = 0x8E;

        /// <summary>src/engine/engineobject.h kSceneHandlerSlots.</summary>
        public const int SceneHandlerSlots = 50;

        /// <summary>src/engine/messageids.h kMsgInitiate.</summary>
        public const int MessageInitiate = 0x4C;

        /// <summary>src/engine/messageids.h kMsgExitBehavior.</summary>
        public const int MessageExitBehavior = 0x39;

        /// <summary>src/engine/messageids.h kMsgNomsg.</summary>
        public const int MessageNoMessage = 0x8E;

        // src/engine/statids.h ids the handling bodies read.
        public const int RatingLongPassEnable = 0x0B;

        public const int RatingRokHandling = 0x0D;

        public const int RatingDamage = 0x0F;

        public const int RatingCarryRokThrowDist = 0x36;

        // PlanCond::RecvrInRangeNView and PlanUtil constants (plancond.cpp).
        public const float PassRangeSquared = 27500.0f;

        public const float ScoreRange = 250.0f;

        public const float ScoreFalloff = 0.004f;

        // PlanCond::ShootSituation's authored range window.
        public const int ShootRangeMin = 10;

        public const int ShootRangeMax = 350;

        public const int ShootRangeDefault = 150;

        // EngineObject::GetDistIfFacing's default threshold.
        public const float HalfRokObjectFacing = HalfRokObject;

        public const int PlayerObjectCount = 6;

        public const int SlotRok = 6;

        public const int SlotNgoal = 7;

        public const int SlotSgoal = 8;
    }

    /// <summary>DifficultyLevel and DamageGate, src/planvm/damagegate.h.</summary>
    public enum PlanAiDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
    }

    /// <summary>A fatal condition the original reports through g_gameApp.Fatal (and the port names explicitly).</summary>
    public sealed class PlanAiFatalException : PlanVmException
    {
        public PlanAiFatalException(string message)
        : base("PLAN AI FATAL: " + message)
        {
        }
    }

    /// <summary>
    /// A handler id the original table has but this bounded slice does not port yet. It is thrown
    /// rather than guessed; the Probe reports the id and the step that reached it.
    /// </summary>
    public sealed class PlanAiUnportedHandlerException : PlanVmException
    {
        public readonly string HandlerKind;

        public readonly int HandlerId;

        public PlanAiUnportedHandlerException(string kind, int id)
        : base("Plan AI handler not ported: " + kind + " id " + id)
        {
            HandlerKind = kind;
            HandlerId = id;
        }
    }
}
