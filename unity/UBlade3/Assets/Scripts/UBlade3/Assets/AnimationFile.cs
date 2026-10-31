using System;

namespace UBlade3.Assets
{
    // The 0x0e byte animation set header: src/anim/animclip.h class AnimHeader (#pragma pack(push, 1),
    // STATIC_ASSERT(sizeof(AnimHeader) == 0x0e)), read by AnimClipSet::Load (HYPERX 0x004172d0).
    public sealed class AnimationHeader
    {
        public const int Size = 0x0e;

        internal AnimationHeader(short clipCount, short frameCount, short frameRate, float duration, short unused, short endFrame)
        {
            ClipCount = clipCount;
            FrameCount = frameCount;
            FrameRate = frameRate;
            Duration = duration;
            Unused = unused;
            EndFrame = endFrame;
        }

        // One clip per bone; the shipped skater sets carry sixteen.
        public short ClipCount { get; }

        public short FrameCount { get; }

        // Copied by AnimClipSet::Load and read by nothing in the shipped code.
        public short FrameRate { get; }

        // The frame count over 120, whatever the frame rate says (every shipped set agrees).
        public float Duration { get; }

        // Copied into AnimClipSet::m_wUnused, which nothing reads.
        public short Unused { get; }

        // The bound the animation binder clamps a node's end frame to; every shipped set has
        // frame count minus one.
        public short EndFrame { get; }
    }

    // One clip of an animation set: the per frame transform stream of one bone.
    // src/anim/animclip.h: AnimClipHeader (0x12 bytes, #pragma pack(push, 1)) followed by
    // nDataSize bytes of float data, loaded by AnimClip::Load (HYPERX 0x00416510).
    //
    // Format numbers and what AnimClip::Load/GetBoneTransform do with them:
    //   1  translations only, one Vec3 per frame
    //   2  one Vec3 translation, then three Euler angles per frame
    //   3  one Vec3 translation per frame, then three Euler angles per frame
    //   4  like 2 (the loader groups 2 and 4)
    //   5  one Vec3 translation; the loader allocates the quaternion array and never fills it
    //   6  one Vec3 translation per frame; the quaternion array is allocated and never filled
    //   7  one translation and a 3x3 matrix per frame; the loader raises
    //      "ANIM SETUP ERROR: ONE_TRAN_3x3 format not yet support!"
    //   8  like 7 with per frame translations; "ALL_TRAN_3x3 format not yet support!"
    // Formats 5 to 8 are recorded here so the reader can expose the raw stream, but no shipped
    // animation file uses them (the disc has formats 1, 2 and 3 only).
    public sealed class AnimationClip
    {
        public const int HeaderSize = 0x12;

        public const int FormatTranslations = 1;
        public const int FormatSingleTranslationEuler = 2;
        public const int FormatTranslationsEuler = 3;
        public const int FormatSingleTranslationEuler4 = 4;
        public const int FormatSingleTranslationQuaternion = 5;
        public const int FormatTranslationsQuaternion = 6;
        public const int FormatSingleTranslationMatrix = 7;
        public const int FormatTranslationsMatrix = 8;

        internal AnimationClip(
            int index,
            ushort segment,
            ushort frame,
            short dataSize,
            string axisOrder,
            int format,
            float[] rawData,
            AssetVector3[] translations,
            float[] eulerDegrees,
            bool hasUsableTransforms,
            byte[] unusedBytes)
        {
            Index = index;
            Segment = segment;
            Frame = frame;
            DataSize = dataSize;
            AxisOrder = axisOrder;
            Format = format;
            RawData = rawData;
            Translations = translations;
            EulerDegrees = eulerDegrees;
            HasUsableTransforms = hasUsableTransforms;
            UnusedBytes = unusedBytes;
        }

        // The clip's position in the set's array, which is also the bone index the animation
        // player indexes it by (AnimPlayer::UpdateFrame, src/anim/animplayer.cpp line 299).
        public int Index { get; }

        // AnimClipHeader.wSegment. Loaded but never read by the shipped player; in every shipped
        // set it equals the clip index and hence the bone index.
        public ushort Segment { get; }

        // AnimClipHeader.wFrame. Loaded but never read by the shipped player; shipped values are
        // zero to five, a frame offset the assembly's start frame supplies.
        public ushort Frame { get; }

        public short DataSize { get; }

        // AnimClipHeader.acAxisOrder: the Euler axis letters, applied last to first by
        // Anim::EulerToMatrix (HYPERX 0x00416ba0). Format 1 clips still carry "zyx".
        public string AxisOrder { get; }

        public int Format { get; }

        // AnimClipHeader.acUnused, five bytes that nothing reads.
        public byte[] UnusedBytes { get; }

        // The clip's data interpreted as floats exactly as AnimClip::Load reads it.
        public float[] RawData { get; }

        // Per frame translations, or a single entry for formats 2, 4, 5 and 7 (GetBoneTransform
        // reads m_pTranslation->x for 5 and 7 whatever frame is asked).
        public AssetVector3[] Translations { get; }

        // FrameCount * 3 Euler angles in DEGREES for formats 2, 3 and 4 (EulerToMatrix multiplies
        // by 0.017453292 before the trigonometry). Empty for the other formats.
        public float[] EulerDegrees { get; }

        // True for formats 1 to 4, the ones the shipped loader turns into a bone transform. For
        // the rest the original either leaves arrays uninitialised or raises, so the raw stream is
        // the only faithful reading.
        public bool HasUsableTransforms { get; }

        public bool IsSingleTranslation
        {
            get
            {
                return Format == FormatSingleTranslationEuler
                       || Format == FormatSingleTranslationEuler4
                       || Format == FormatSingleTranslationQuaternion
                       || Format == FormatSingleTranslationMatrix;
            }
        }

        public float EulerAt(int frame, int axis)
        {
            return EulerDegrees[(frame * 3) + axis];
        }
    }

    // One .wba animation set. AnimFile::Open (HYPERX 0x00493cb0) validates the tag and version,
    // then AnimSet::LoadIntoRegistry (HYPERX 0x00417220) builds AnimClipSet, whose Load
    // (HYPERX 0x004172d0) reads the 0x0e byte header and one AnimClip per bone.
    public sealed class AnimationClipSet
    {
        internal AnimationClipSet(string sourceName, FileHeader header, AnimationHeader animationHeader, AnimationClip[] clips, int trailingBytes, string trailingAscii)
        {
            SourceName = sourceName;
            Header = header;
            AnimationHeader = animationHeader;
            Clips = clips;
            TrailingBytes = trailingBytes;
            TrailingAscii = trailingAscii;
        }

        public string SourceName { get; }

        // The 0x30 byte file header; the shipped tag is 4 (AnimationFileType) and the version is
        // 1.1.0.0.0.
        public FileHeader Header { get; }

        public AnimationHeader AnimationHeader { get; }

        public AnimationClip[] Clips { get; }

        // Bytes the loader leaves unread after the last clip. 128 shipped files carry 85 bytes of
        // repeated "DONE" tokens here; AnimFile and AnimClipSet never look at them.
        public int TrailingBytes { get; }

        public string TrailingAscii { get; }

        public override string ToString()
        {
            return "AnimationClipSet(" + Header.Name + ", clips=" + Clips.Length + ", frames=" + AnimationHeader.FrameCount + ")";
        }
    }

    // Reads a .wba file. The layout is src/anim/animclipset.cpp AnimClipSet::Load,
    // src/anim/animclip.cpp AnimClip::Load (HYPERX 0x00416510) and src/anim/animfile.cpp.
    public static class WbaReader
    {
        public static AnimationClipSet LoadFile(string path)
        {
            return Load(System.IO.File.ReadAllBytes(path), path);
        }

        public static AnimationClipSet Load(byte[] data, string sourceName)
        {
            AssetBytes bytes = new AssetBytes(data, sourceName);
            FileHeader header = FileHeader.Read(bytes, 0);

            // AnimFile::Open: "if (nType == ANIM_FILE_TYPE)" then a version check; anything else
            // reports the expected and encountered type names.
            if (header.Tag != FileHeader.AnimationFileTag)
            {
                throw AssetFormatException.At(sourceName, 0, "file tag " + header.Tag + " is not the animation tag " + FileHeader.AnimationFileTag);
            }

            if (!SameVersion(header.Version, FileVersion.ExpectedAnimationVersion))
            {
                throw AssetFormatException.At(
                    sourceName,
                    0x24,
                    "animation file version is " + header.Version + ", the loader expects " + FileVersion.ExpectedAnimationVersion);
            }

            long at = FileHeader.Size;
            short clipCount = bytes.I16(at);
            short frameCount = bytes.I16(at + 2);
            short frameRate = bytes.I16(at + 4);
            float duration = bytes.F32(at + 6);
            short unused = bytes.I16(at + 0x0a);
            short endFrame = bytes.I16(at + 0x0c);

            at += AnimationHeader.Size;

            if (clipCount < 0)
            {
                throw AssetFormatException.At(sourceName, FileHeader.Size, "negative animation clip count " + clipCount);
            }

            if (frameCount < 0)
            {
                throw AssetFormatException.At(sourceName, FileHeader.Size + 2, "negative animation frame count " + frameCount);
            }

            AnimationClip[] clips = new AnimationClip[clipCount];

            for (int index = 0; index < clipCount; index++)
            {
                clips[index] = ReadClip(bytes, ref at, frameCount, index);
            }

            int trailing = data.Length - (int)at;
            string trailingAscii = trailing <= 0 ? string.Empty : bytes.FixedAscii(at, trailing);

            // The loader never reads past the last clip, so bytes here are left unread on purpose.
            return new AnimationClipSet(sourceName, header, new AnimationHeader(clipCount, frameCount, frameRate, duration, unused, endFrame), clips, trailing, trailingAscii);
        }

        private static AnimationClip ReadClip(AssetBytes bytes, ref long at, short frameCount, int index)
        {
            long headerAt = at;
            ushort segment = bytes.U16(at);
            ushort frame = bytes.U16(at + 2);
            short dataSize = bytes.I16(at + 4);
            string axisOrder = bytes.FixedAscii(at + 6, 3);
            byte[] unused = bytes.Slice(at + 9, 5);
            int format = bytes.I32(at + 0x0e);

            at += AnimationClip.HeaderSize;

            if (dataSize < 0)
            {
                throw AssetFormatException.At(bytes.SourceName, headerAt + 4, "clip " + index + " has a negative data size " + dataSize);
            }

            if ((dataSize & 3) != 0)
            {
                // AnimClip::Load allocates dataSize >> 2 floats and then reads dataSize bytes into
                // them, so a non multiple of four overruns its own buffer.
                throw AssetFormatException.At(bytes.SourceName, headerAt + 4, "clip " + index + " data size " + dataSize + " is not a multiple of four");
            }

            if (at + dataSize > bytes.Length)
            {
                throw AssetFormatException.At(bytes.SourceName, headerAt, "clip " + index + " data of " + dataSize + " byte(s) runs past the file");
            }

            float[] rawData = new float[dataSize / 4];

            for (int i = 0; i < rawData.Length; i++)
            {
                rawData[i] = bytes.F32(at + (i * 4));
            }

            AssetVector3[] translations = new AssetVector3[0];
            float[] eulerDegrees = new float[0];
            bool usable = false;

            switch (format)
            {
                case AnimationClip.FormatTranslations:
                    RequireSize(bytes, headerAt, dataSize, frameCount * 12, index, "format 1", "frame count * 12");
                    translations = new AssetVector3[frameCount];

                    for (int f = 0; f < frameCount; f++)
                    {
                        translations[f] = ReadVector(rawData, f * 3);
                    }

                    usable = true;
                    break;

                case AnimationClip.FormatSingleTranslationEuler:
                case AnimationClip.FormatSingleTranslationEuler4:
                    RequireSize(bytes, headerAt, dataSize, 12 + (frameCount * 12), index, "format 2/4", "12 + frame count * 12");
                    translations = new AssetVector3[1];
                    translations[0] = ReadVector(rawData, 0);
                    eulerDegrees = CopyFloats(rawData, 3, frameCount * 3);
                    usable = true;
                    break;

                case AnimationClip.FormatTranslationsEuler:
                    RequireSize(bytes, headerAt, dataSize, frameCount * 24, index, "format 3", "frame count * 24");
                    translations = new AssetVector3[frameCount];

                    for (int f = 0; f < frameCount; f++)
                    {
                        translations[f] = ReadVector(rawData, f * 3);
                    }

                    eulerDegrees = CopyFloats(rawData, frameCount * 3, frameCount * 3);
                    usable = true;
                    break;

                case AnimationClip.FormatSingleTranslationQuaternion:
                case AnimationClip.FormatTranslationsQuaternion:
                case AnimationClip.FormatSingleTranslationMatrix:
                case AnimationClip.FormatTranslationsMatrix:
                    // AnimClip::Load leaves the quaternion array uninitialised (5, 6) or raises
                    // (7, 8). Only the translation copy is faithful, and only when it fits.
                    if (dataSize >= 12)
                    {
                        translations = new AssetVector3[1];
                        translations[0] = ReadVector(rawData, 0);
                    }

                    break;

                default:
                    // AnimClip::Load's switch has no default arm; an unknown format leaves every
                    // pointer wherever the constructor put it.
                    break;
            }

            at += dataSize;

            return new AnimationClip(index, segment, frame, dataSize, axisOrder, format, rawData, translations, eulerDegrees, usable, unused);
        }

        private static void RequireSize(AssetBytes bytes, long headerAt, int dataSize, int expected, int index, string formatName, string formula)
        {
            if (dataSize != expected)
            {
                throw AssetFormatException.At(
                    bytes.SourceName,
                    headerAt + 4,
                    "clip " + index + " in " + formatName + " has " + dataSize + " data byte(s); the loader reads " + formula + " = " + expected);
            }
        }

        private static AssetVector3 ReadVector(float[] raw, int floatIndex)
        {
            return new AssetVector3(raw[floatIndex], raw[floatIndex + 1], raw[floatIndex + 2]);
        }

        private static float[] CopyFloats(float[] source, int first, int count)
        {
            float[] copy = new float[count];

            Array.Copy(source, first, copy, 0, count);

            return copy;
        }

        private static bool SameVersion(FileVersion left, FileVersion right)
        {
            return left.Major == right.Major
                   && left.Minor == right.Minor
                   && left.Revision == right.Revision
                   && left.Unused == right.Unused
                   && left.Build == right.Build;
        }
    }
}
