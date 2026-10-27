// UBlade3 - exact HyperBlade (1996) simulation core.
// Plain-float vector math mirroring src/math/vec3.cpp and src/math/vector.h of the decomp.
// No UnityEngine types: this file must compile and run headless.

using System;

namespace UBlade3.Sim
{
    // src/math/vec3.h. The decomp keeps floats in the struct and solves the collision quadratic in
    // doubles; the same split is preserved here so the intersection results match.
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0.0f, 0.0f, 0.0f);
        public static readonly Vec3 Up = new Vec3(0.0f, 0.0f, 1.0f);

        public float GetX() { return x; }
        public float GetY() { return y; }
        public float GetZ() { return z; }

        public float LengthSquared()
        {
            return z * z + x * x + y * y;
        }

        public float Magnitude()
        {
            return (float)Math.Sqrt(Dot(this, this));
        }

        public Vec3 Scaled(float scale)
        {
            return new Vec3(scale * x, scale * y, scale * z);
        }

        public Vec3 Minus(Vec3 from)
        {
            return new Vec3(x - from.x, y - from.y, z - from.z);
        }

        public Vec3 Plus(Vec3 rhs)
        {
            return new Vec3(x + rhs.x, y + rhs.y, z + rhs.z);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.x * s, a.y * s, a.z * s); }

        public static float Dot(Vec3 a, Vec3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public static float DotInline(Vec3 a, Vec3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static Vec3 ScaleTo(Vec3 input, float scale)
        {
            return new Vec3(scale * input.x, scale * input.y, scale * input.z);
        }

        // src/math/vec3.cpp FindClosest (0x00430340): the second point wins only on a strict
        // improvement of the squared distance.
        public static Vec3 FindClosest(Vec3 from, Vec3 first, Vec3 second)
        {
            float dx = first.x - from.x;
            float dy = first.y - from.y;
            float dz = first.z - from.z;
            float flToFirst = dx * dx + dy * dy + dz * dz;

            dx = second.x - from.x;
            dy = second.y - from.y;
            dz = second.z - from.z;
            float flToSecond = dx * dx + dy * dy + dz * dz;

            if (flToFirst <= flToSecond)
            {
                second = first;
            }

            return second;
        }

        public override string ToString()
        {
            return "(" + x.ToString("R") + ", " + y.ToString("R") + ", " + z.ToString("R") + ")";
        }
    }

    // src/math/vec3.cpp and src/math/mathops.h.
    public static class MathOps
    {
        // src/globals.cpp: g_dNormalizeEpsilon = 1e-07 (a double); the same epsilon converts to cm.
        public const double NormalizeEpsilon = 1e-07;
        // src/globals.cpp: g_flZero = 0, g_flOne = 1.
        public const float Zero = 0.0f;
        public const float One = 1.0f;
        // src/globals.cpp: g_flPi = 3.14159274f; g_flDegToRad = 0.017453292f.
        public const float Pi = 3.14159274f;
        public const float HalfPi = 1.5707964f;
        public const float DegToRad = 0.017453292f;
        public const float DegreesPerTurn = 360.0f;

        // src/math/vec3.cpp NormalizeTo (0x00430180): normalises in double, zeroes below the epsilon.
        public static bool NormalizeTo(Vec3 value, out Vec3 result)
        {
            double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z);

            if (length > NormalizeEpsilon)
            {
                result = new Vec3(
                    (float)(value.x / length),
                    (float)(value.y / length),
                    (float)(value.z / length));
                return true;
            }

            result = Vec3.Zero;
            return false;
        }

        // src/math/vec3.cpp NormalizeScattered (0x004301f0): the argument order is x, z, y.
        public static bool NormalizeScattered(ref float x, ref float y, ref float z)
        {
            double length = Math.Sqrt((double)x * x + (double)z * z + (double)y * y);

            if (length > NormalizeEpsilon)
            {
                x = (float)(x / length);
                y = (float)(y / length);
                z = (float)(z / length);
                return true;
            }

            x = 0.0f;
            y = 0.0f;
            z = 0.0f;
            return false;
        }

        // mathops.cpp Math::Vec3Normalize (0x0044b050's neighbour): returns 1/0.
        public static bool Vec3Normalize(ref Vec3 value)
        {
            return NormalizeTo(value, out value);
        }

        public static float Vec3Distance(Vec3 a, Vec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // src/math/mathops.cpp Math::SphericalToCartesian.
        public static Vec3 SphericalToCartesian(Vec3 input)
        {
            double dHorizontal = Math.Cos(input.y);
            return new Vec3(
                (float)(dHorizontal * Math.Cos(input.x)),
                (float)(dHorizontal * Math.Sin(input.x)),
                (float)Math.Sin(input.y));
        }

        // src/math/mathops.cpp Math::RandInt: inclusive both ends through g_flRandRangeBias 0.4999.
        public static int RandInt(int min, int max)
        {
            if (min > max)
            {
                int swap = min;
                min = max;
                max = swap;
            }

            return min + (int)((max - min + 1) * NextFloat());
        }

        private static readonly Random s_random = new Random(1);

        // Headless stand-in for the C runtime rand(); the simulation core never requires a seed to
        // match the original (the original's rand() sequence is not reproduced by any C# RNG).
        public static float NextFloat()
        {
            return (float)s_random.NextDouble();
        }
    }

    // The subset of src/math/transformobject.* the camera projection needs. Row-major 4x4; the
    // decomp's four shaped multiplies all reduce to this full multiply for the camera's flag-1
    // transforms (cameraobject.cpp constructs both projection states with cFlag = 1).
    public sealed class Transform4
    {
        public readonly float[] A = new float[16];

        public Transform4()
        {
            Reset();
        }

        public Transform4(bool hasRow3)
        {
            HasRow3 = hasRow3;
            Reset();
        }

        public bool HasRow3;
        public bool Identity = true;

        public float this[int row, int column]
        {
            get { return A[row * 4 + column]; }
            set { A[row * 4 + column] = value; }
        }

        public void Reset()
        {
            Array.Clear(A, 0, A.Length);
            A[0] = 1.0f;
            A[5] = 1.0f;
            A[10] = 1.0f;
            A[15] = 1.0f;
            Identity = true;
        }

        public void SetTranslation(float x, float y, float z)
        {
            this[0, 3] = x;
            this[1, 3] = y;
            this[2, 3] = z;
        }

        // transformobject.cpp Multiply (0x00427400), full four-by-four arm.
        public static void Multiply(Transform4 lhs, Transform4 rhs, Transform4 result)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float sum = 0.0f;
                    for (int k = 0; k < 4; k++)
                    {
                        sum += lhs.A[row * 4 + k] * rhs.A[k * 4 + column];
                    }
                    result.A[row * 4 + column] = sum;
                }
            }

            result.HasRow3 = lhs.HasRow3 || rhs.HasRow3;
            result.Identity = false;
        }

        // transformobject.cpp Compose (0x00426f40): this = this * other.
        public void Compose(Transform4 other)
        {
            Transform4 result = new Transform4();
            Multiply(this, other, result);
            Array.Copy(result.A, A, A.Length);
            HasRow3 = result.HasRow3;
            Identity = false;
        }

        // transformobject.cpp ApplyScale (0x00426b20).
        public void ApplyScale(float scaleX, float scaleY, float scaleZ)
        {
            if (Identity)
            {
                this[0, 0] = scaleX;
                this[1, 1] = scaleY;
                this[2, 2] = scaleZ;
                Identity = scaleX == 1.0f && scaleY == 1.0f && scaleZ == 1.0f;
                return;
            }

            for (int row = 0; row < 3; row++)
            {
                this[row, 0] *= scaleX;
                this[row, 1] *= scaleY;
                this[row, 2] *= scaleZ;
            }

            if (HasRow3)
            {
                this[3, 0] *= scaleX;
                this[3, 1] *= scaleY;
                this[3, 2] *= scaleZ;
            }

            Identity = false;
        }

        // transformobject.cpp TransformPoint (0x00403110).
        public Vec3 TransformPoint(Vec3 point)
        {
            return new Vec3(
                this[0, 0] * point.x + this[0, 1] * point.y + this[0, 2] * point.z + this[0, 3],
                this[1, 0] * point.x + this[1, 1] * point.y + this[1, 2] * point.z + this[1, 3],
                this[2, 0] * point.x + this[2, 1] * point.y + this[2, 2] * point.z + this[2, 3]);
        }

        public void CopyFrom(Transform4 source)
        {
            Array.Copy(source.A, A, A.Length);
            HasRow3 = source.HasRow3;
            Identity = source.Identity;
        }
    }
}
