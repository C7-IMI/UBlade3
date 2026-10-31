using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace UBlade3.Audio
{
    // One RIFF chunk as the wave readers walk it. The 14 byte BITMAPFILEHEADER-like wrapper the
    // game never uses is not part of WAV; a RIFF/WAVE file opens with its 12 byte form header.
    public sealed class WavChunk
    {
        internal WavChunk(string id, long offset, int size)
        {
            Id = id;
            Offset = offset;
            Size = size;
        }

        // Four characters, for example "fmt ", "fact" or "data".
        public string Id { get; }

        // Byte offset of the chunk's 8 byte header in the file.
        public long Offset { get; }

        // The chunk's declared data size, not counting this header or the even-alignment pad byte.
        public int Size { get; }
    }

    // One shipped HyperBlade wave. Every file on the disc is a RIFF/WAVE file holding one PCM
    // format chunk and one data chunk; the audio manager reads exactly the PCM header and the
    // declared data bytes.
    //
    // Sources of truth:
    //   third_party/hyperblade-decomp/src/sound/wave.h/.cpp
    //     Wave::Open            HYPERX 0x00493430
    //     Wave::FindDataChunk   HYPERX 0x004935f0
    //     Wave::ReadChunk       HYPERX 0x00493640
    //     Wave::Close           HYPERX 0x00493710
    //   third_party/hyperblade-20XX/src/sound/wave.cpp  (clean RIFF reader used as the oracle)
    //   third_party/hyperblade-20XX/tests/wave_check.cpp (every shipped wave parses, reads back)
    //
    // The original accepts a file when the RIFF form is WAVE, the fmt chunk is at least 16 bytes,
    // the format tag is WAVE_FORMAT_PCM (1) and its cbSize is zero, and the data chunk can be
    // read. The 295 shipped files are all PCM, mono or stereo, 8 or 16 bits, at 11025, 22050 or
    // 44100 Hz. This reader validates those three format properties and the block alignment;
    // anything else fails loudly with AudioFormatException.
    //
    // Acceptance details taken from the two readers, verified against all 295 shipped files:
    //   * chunks are dword aligned; an odd chunk size carries one pad byte
    //     (20XX Wave::ReadInfo: "Chunks start on even offsets").
    //   * the fmt and data chunks may appear in either order; the walk stops once both are found,
    //     so bytes or chunks after data are never read by the original.
    //   * Wave::Open reads only the first 16 bytes of a PCM fmt chunk, so a 18 byte chunk (four
    //     shipped files) is accepted: the trailing cbSize word is skipped by mmioAscend.
    //   * the RIFF size field is never compared with the file length; 40 shipped files declare a
    //     size one byte short (the pad byte), and several carry bytes after the data chunk.
    //   * AudioMgr::LoadSound reads exactly m_nDataBytes bytes, so bytes past the data chunk are
    //     ignored even when they are sample data (two shipped files carry 12 such bytes).
    //   * a PCM wave needs no conversion: DirectSound takes 8 bit unsigned and 16 bit signed
    //     little-endian samples, mono or stereo, at the wave's own rate.
    public sealed class WavFile
    {
        // RIFF form header: "RIFF", dword size, "WAVE".
        public const int FormHeaderBytes = 12;

        // One RIFF chunk header is 8 bytes.
        public const int ChunkHeaderBytes = 8;

        // WAVE_FORMAT_PCM, the only format tag the shipped disc uses and the only one
        // AudioMgr::LoadSound plays: pFormat->wFormatTag must equal WAVE_FORMAT_PCM.
        public const int PcmFormatTag = 1;

        // The shipped disc's rates. The reader accepts any positive rate; these three are the
        // measured set and the probe checks them.
        public const int ShippedRateLow = 11025;
        public const int ShippedRateMid = 22050;
        public const int ShippedRateHigh = 44100;

        // 20XX's kMaxChunks: far more chunks than any shipped file, so a damaged file cannot loop.
        public const int MaxChunks = 64;

        private WavFile(
            string sourceName,
            byte[] fileBytes,
            int formatTag,
            int formatChunkSize,
            int channels,
            int sampleRate,
            int byteRate,
            int blockAlign,
            int bitsPerSample,
            int cbSize,
            int rawCbSize,
            long dataOffset,
            int dataBytes,
            WavChunk[] chunks,
            int declaredRiffSize,
            byte[] trailingData)
        {
            SourceName = sourceName;
            FormatTag = formatTag;
            FormatChunkSize = formatChunkSize;
            Channels = channels;
            SampleRate = sampleRate;
            ByteRate = byteRate;
            BlockAlign = blockAlign;
            BitsPerSample = bitsPerSample;
            CbSize = cbSize;
            RawCbSize = rawCbSize;
            DataOffset = dataOffset;
            DataBytes = dataBytes;
            Chunks = chunks;
            DeclaredRiffSize = declaredRiffSize;
            FileLength = fileBytes.Length;
            RawData = new byte[dataBytes];
            Array.Copy(fileBytes, dataOffset, RawData, 0, dataBytes);
            TrailingData = trailingData;
        }

        public string SourceName { get; }

        // WAVE_FORMAT_PCM; the reader rejects any other tag.
        public int FormatTag { get; }

        // The fmt chunk's declared size: 16 on 291 shipped files, 18 on 4.
        public int FormatChunkSize { get; }

        public int Channels { get; }

        public int SampleRate { get; }

        // nAvgBytesPerSec from the fmt chunk.
        public int ByteRate { get; }

        // nBlockAlign from the fmt chunk: bytes per sample frame.
        public int BlockAlign { get; }

        public int BitsPerSample { get; }

        // WAVEFORMATEX.cbSize as Wave::Open leaves it: the loader reads a 16 byte PCMWAVEFORMAT
        // and sets cbExtra to zero for a PCM tag, so AudioMgr::LoadSound always sees zero on the
        // shipped PCM waves. Zero here by construction.
        public int CbSize { get; }

        // The word at fmt offset 16 when the fmt chunk is 18 bytes or longer, exposed for the
        // record only. Wave::Open never reads it for PCM (one shipped file, sxstmo1.wav, has 20
        // there as unused padding) so it does not affect acceptance.
        public int RawCbSize { get; }

        // The data chunk's payload offset and declared byte count.
        public long DataOffset { get; }

        public int DataBytes { get; }

        // The chunks the reader walked to find fmt and data, in file order.
        public WavChunk[] Chunks { get; }

        // The RIFF form's size field. It is not compared with the file length: 40 shipped files
        // declare one byte short, and the original never checks it.
        public int DeclaredRiffSize { get; }

        public int FileLength { get; }

        // The original's data chunk read: exactly DataBytes bytes starting at DataOffset.
        public byte[] RawData { get; }

        public bool RiffSizeMatchesFile
        {
            get { return DeclaredRiffSize == FileLength - 8; }
        }

        // Whole sample frames in the data chunk (a partial trailing frame is a format error here).
        public int FrameCount
        {
            get { return DataBytes / BlockAlign; }
        }

        public double DurationSeconds
        {
            get { return (double)FrameCount / SampleRate; }
        }

        public long DurationMilliseconds
        {
            get { return (long)Math.Round((double)FrameCount * 1000.0 / SampleRate); }
        }

        // The bytes after the declared data chunk, copied for classification. The original never
        // reads them. They are mostly a final pad byte, a few zero bytes or a trailing LIST
        // metadata chunk, but two shipped files carry 12 real sample bytes beyond the data chunk.
        public byte[] TrailingData { get; }

        public int TrailingBytes
        {
            get { return TrailingData.Length; }
        }

        // A copy of the bytes after the data chunk: zero on all but the pad/metadata cases.
        public bool TrailingAllZero
        {
            get
            {
                for (int i = 0; i < TrailingData.Length; i++)
                {
                    if (TrailingData[i] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        // The four character chunk id of a well formed chunk after data ("LIST" on one shipped
        // file), or null. Used only for the probe report; the loader never reads that far.
        public string TrailingChunkId
        {
            get
            {
                if (TrailingData.Length < ChunkHeaderBytes)
                {
                    return null;
                }

                char[] chars = new char[4];

                for (int i = 0; i < 4; i++)
                {
                    char c = (char)TrailingData[i];

                    if (c < 0x20 || c > 0x7e)
                    {
                        return null;
                    }

                    chars[i] = c;
                }

                return new string(chars);
            }
        }

        public bool HasFactChunk
        {
            get
            {
                for (int i = 0; i < Chunks.Length; i++)
                {
                    if (string.Equals(Chunks[i].Id, "fact", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // Converts the PCM data to the interleaved float samples AudioClip.SetData wants.
        // 8 bit PCM is unsigned with 128 as silence; 16 bit PCM is signed little endian.
        //   DirectSound accepts both layouts directly (SoundChannel::StartPlayback builds the
        //   WAVEFORMATEX from the SoundEntry fields), so this is the only conversion the original
        //   data needs on the way into a Unity AudioClip.
        public float[] DecodeToFloat()
        {
            float[] samples = new float[FrameCount * Channels];
            int at = 0;

            if (BitsPerSample == 8)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = (RawData[i] - 128) / 128.0f;
                }

                return samples;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                short value = unchecked((short)(RawData[at] | (RawData[at + 1] << 8)));

                samples[i] = value / 32768.0f;
                at += 2;
            }

            return samples;
        }

        public static WavFile LoadFile(string path)
        {
            return Load(File.ReadAllBytes(path), path);
        }

        // Parses one RIFF/WAVE file. Malformed or unsupported input is an AudioFormatException.
        public static WavFile Load(byte[] fileBytes, string sourceName)
        {
            if (fileBytes == null)
            {
                throw new ArgumentNullException(nameof(fileBytes));
            }

            if (fileBytes.Length < FormHeaderBytes)
            {
                throw AudioFormatException.At(sourceName, 0, "file is shorter than a RIFF/WAVE form header");
            }

            if (fileBytes[0] != (byte)'R' || fileBytes[1] != (byte)'I' || fileBytes[2] != (byte)'F' || fileBytes[3] != (byte)'F')
            {
                throw AudioFormatException.At(sourceName, 0, "missing RIFF form id");
            }

            if (fileBytes[8] != (byte)'W' || fileBytes[9] != (byte)'A' || fileBytes[10] != (byte)'V' || fileBytes[11] != (byte)'E')
            {
                throw AudioFormatException.At(sourceName, 8, "RIFF form is not WAVE");
            }

            int declaredRiffSize = (int)ReadU32(fileBytes, 4);
            long at = FormHeaderBytes;
            bool haveFormat = false;
            bool haveData = false;
            int formatTag = 0;
            int formatChunkSize = 0;
            int channels = 0;
            int sampleRate = 0;
            int byteRate = 0;
            int blockAlign = 0;
            int bitsPerSample = 0;
            int cbSize = 0;
            int rawCbSize = 0;
            long dataOffset = 0;
            int dataBytes = 0;
            List<WavChunk> chunks = new List<WavChunk>();

            // 20XX Wave::ReadInfo's loop: up to 64 chunks, stop once fmt and data have both been
            // seen, chunks dword aligned. The original seeks back to the form start to find data
            // after fmt, so either order is accepted here too.
            for (int chunk = 0; chunk < MaxChunks && !(haveFormat && haveData); chunk++)
            {
                if (at + ChunkHeaderBytes > fileBytes.Length)
                {
                    break;
                }

                string id = ReadFourCc(fileBytes, at);
                long size = ReadU32(fileBytes, at + 4);

                if (size > int.MaxValue)
                {
                    throw AudioFormatException.At(sourceName, at, "chunk \"" + id + "\" declares an impossible size " + size);
                }

                if (at + ChunkHeaderBytes + size > fileBytes.Length)
                {
                    // A truncated chunk before data means the file is not a usable wave.
                    throw AudioFormatException.At(
                        sourceName,
                        at,
                        "chunk \"" + id + "\" of " + size + " byte(s) runs past the end of the " + fileBytes.Length + " byte file");
                }

                chunks.Add(new WavChunk(id, at, (int)size));

                if (string.Equals(id, "fmt ", StringComparison.Ordinal) && !haveFormat)
                {
                    if (size < 16)
                    {
                        // Wave::Open: "if (chunk.cksize < sizeof(PCMWAVEFORMAT))" -> WAVE_ERR_FORMAT.
                        throw AudioFormatException.At(sourceName, at, "fmt chunk is " + size + " byte(s), the loader needs at least 16");
                    }

                    formatChunkSize = (int)size;
                    formatTag = ReadU16(fileBytes, at + 8);
                    channels = ReadU16(fileBytes, at + 10);
                    sampleRate = (int)ReadU32(fileBytes, at + 12);
                    byteRate = (int)ReadU32(fileBytes, at + 16);
                    blockAlign = ReadU16(fileBytes, at + 20);
                    bitsPerSample = ReadU16(fileBytes, at + 22);
                    rawCbSize = size >= 18 ? ReadU16(fileBytes, at + 24) : 0;

                    // Wave::Open: "if (pcm.wf.wFormatTag == WAVE_FORMAT_PCM) cbExtra = 0;" and the
                    // extra cbSize bytes are only read for a non-PCM tag, which LoadSound rejects.
                    cbSize = formatTag == PcmFormatTag ? 0 : rawCbSize;
                    haveFormat = true;
                }
                else if (string.Equals(id, "data", StringComparison.Ordinal) && !haveData)
                {
                    dataOffset = at + ChunkHeaderBytes;
                    dataBytes = (int)size;
                    haveData = true;
                }

                at += ChunkHeaderBytes + size + (size & 1);
            }

            if (!haveFormat)
            {
                throw AudioFormatException.At(sourceName, 0, "no fmt chunk");
            }

            if (!haveData)
            {
                throw AudioFormatException.At(sourceName, 0, "no data chunk");
            }

            // AudioMgr::LoadSound: wFormatTag must be WAVE_FORMAT_PCM and cbSize must be zero.
            if (formatTag != PcmFormatTag)
            {
                throw AudioFormatException.At(
                    sourceName,
                    0,
                    "unsupported wave format tag " + formatTag + "; the shipped data and AudioMgr::LoadSound accept only PCM (1)");
            }

            if (channels != 1 && channels != 2)
            {
                throw AudioFormatException.At(sourceName, 0, "unsupported channel count " + channels + "; UBlade3.Audio implements the shipped mono/stereo waves");
            }

            if (bitsPerSample != 8 && bitsPerSample != 16)
            {
                throw AudioFormatException.At(sourceName, 0, "unsupported bits per sample " + bitsPerSample + "; the shipped waves are 8 or 16 bit PCM");
            }

            if (sampleRate <= 0)
            {
                throw AudioFormatException.At(sourceName, 0, "sample rate " + sampleRate + " is not positive");
            }

            int expectedBlockAlign = channels * (bitsPerSample / 8);

            if (blockAlign != expectedBlockAlign)
            {
                throw AudioFormatException.At(
                    sourceName,
                    0,
                    "block alignment " + blockAlign + " does not match " + channels + " channel(s) of " + bitsPerSample + " bit samples (" + expectedBlockAlign + ")");
            }

            if (byteRate != sampleRate * blockAlign)
            {
                throw AudioFormatException.At(
                    sourceName,
                    0,
                    "average bytes per second " + byteRate + " does not match " + sampleRate + " Hz * " + blockAlign);
            }

            if (dataBytes % blockAlign != 0)
            {
                throw AudioFormatException.At(sourceName, dataOffset, "data chunk is not a whole number of sample frames");
            }

            if (dataOffset + dataBytes > fileBytes.Length)
            {
                throw AudioFormatException.At(sourceName, dataOffset, "data chunk runs past the end of the file");
            }

            int trailingCount = fileBytes.Length - (int)(dataOffset + dataBytes);
            byte[] trailingData = new byte[trailingCount];

            if (trailingCount > 0)
            {
                Array.Copy(fileBytes, dataOffset + dataBytes, trailingData, 0, trailingCount);
            }

            return new WavFile(
                sourceName,
                fileBytes,
                formatTag,
                formatChunkSize,
                channels,
                sampleRate,
                byteRate,
                blockAlign,
                bitsPerSample,
                cbSize,
                rawCbSize,
                dataOffset,
                dataBytes,
                chunks.ToArray(),
                declaredRiffSize,
                trailingData);
        }

        private static string ReadFourCc(byte[] bytes, long at)
        {
            char[] chars = new char[4];

            for (int i = 0; i < 4; i++)
            {
                chars[i] = (char)bytes[at + i];
            }

            return new string(chars);
        }

        private static int ReadU16(byte[] bytes, long at)
        {
            return bytes[at] | (bytes[at + 1] << 8);
        }

        private static uint ReadU32(byte[] bytes, long at)
        {
            return (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
        }

        public override string ToString()
        {
            return "WavFile(" + Channels + "ch " + BitsPerSample + "bit " + SampleRate + "Hz " + DurationMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms)";
        }
    }
}
