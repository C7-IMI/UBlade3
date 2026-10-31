using System.Globalization;

namespace UBlade3.Assets
{
    // A plain three component float vector. The asset layer depends on nothing outside the BCL so
    // that it compiles and runs in the Unity editor, in a batch-mode player and in a plain C#
    // harness alike; Unity mesh creation converts these to UnityEngine.Vector3 later.
    public readonly struct AssetVector3
    {
        public AssetVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public override string ToString()
        {
            return "(" + F(X) + ", " + F(Y) + ", " + F(Z) + ")";
        }

        private static string F(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }
    }

    // A texture coordinate pair. src/graphics/texcoord.h defines TexCoord as two floats, tu and tv.
    public readonly struct AssetVector2
    {
        public AssetVector2(float tu, float tv)
        {
            Tu = tu;
            Tv = tv;
        }

        public float Tu { get; }

        public float Tv { get; }

        public override string ToString()
        {
            return "(" + Tu.ToString("G9", CultureInfo.InvariantCulture) + ", " + Tv.ToString("G9", CultureInfo.InvariantCulture) + ")";
        }
    }
}
