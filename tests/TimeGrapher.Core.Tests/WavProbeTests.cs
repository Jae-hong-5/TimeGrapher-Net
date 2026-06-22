using TimeGrapher.Core.AudioIo;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class WavProbeTests
{
    [Fact]
    public void TryReadFormatReadsFloatMonoWav()
    {
        using TempWavFile file = TempWavFile.Create(WavProbe.WaveFormatIeeeFloat, channels: 1, sampleRate: 48000, bitsPerSample: 32, dataBytes: 16);

        Assert.True(WavProbe.TryReadFormat(file.Path, out WavFormatInfo info, out string error));
        Assert.Equal("", error);
        Assert.True(info.IsIeeeFloat32Mono);
        Assert.True(WavProbe.IsStandardRateFloatMono(info));
    }

    [Fact]
    public void StandardRateCheckRejectsStereoAndNonStandardRates()
    {
        var stereo = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 2, 48000, 384000, 8, 32, 44, 16);
        var nonStandardRate = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 1, 44100, 176400, 4, 32, 44, 16);

        Assert.False(WavProbe.IsStandardRateFloatMono(stereo));
        Assert.False(WavProbe.IsStandardRateFloatMono(nonStandardRate));
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    public void PlaybackAcceptanceProfileAcceptsStandardRates(int sampleRate)
    {
        var info = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 1, sampleRate, (uint)(sampleRate * 4), 4, 32, 44, 16);

        Assert.True(WavProbe.IsAccepted(info, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));
    }

    [Fact]
    public void PlaybackAcceptanceProfileRejectsInconsistentLayout()
    {
        var badBlockAlign = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 1, 48000, 192000, 8, 32, 44, 16);
        var badByteRate = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 1, 48000, 123, 4, 32, 44, 16);
        var unalignedData = new WavFormatInfo(WavProbe.WaveFormatIeeeFloat, 1, 48000, 192000, 4, 32, 44, 18);

        Assert.False(WavProbe.IsAccepted(badBlockAlign, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));
        Assert.False(WavProbe.IsAccepted(badByteRate, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));
        Assert.False(WavProbe.IsAccepted(unalignedData, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));
    }

    [Fact]
    public void ReadMonoFloatRejectsBlockAlignSmallerThanSample()
    {
        // Malformed header: 64-bit float samples but BlockAlign=1. Without an
        // acceptance profile the probe does not cross-check this; the per-frame
        // decode would step 1 byte at a time and read past the data chunk. The
        // reader must reject it cleanly instead of throwing IndexOutOfRangeException.
        using TempWavFile file = TempWavFile.Create(
            WavProbe.WaveFormatIeeeFloat, channels: 1, sampleRate: 48000,
            bitsPerSample: 64, dataBytes: 16, blockAlignOverride: 1);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => WavFileReader.ReadMonoFloat(file.Path));
        Assert.Contains("frame stride", ex.Message);
    }

    [Fact]
    public void TryReadFormatReadsExtensibleFloatMonoWav()
    {
        using TempWavFile file = TempWavFile.Create(
            WavProbe.WaveFormatIeeeFloat,
            channels: 1,
            sampleRate: 96000,
            bitsPerSample: 32,
            dataBytes: 16,
            extensible: true);

        Assert.True(WavProbe.TryReadFormat(file.Path, out WavFormatInfo info, out string error));
        Assert.Equal("", error);
        Assert.Equal(WavProbe.WaveFormatIeeeFloat, info.AudioFormat);
        Assert.True(WavProbe.IsAccepted(info, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));
    }

    [Fact]
    public void TryReadFormatRejectsInvalidExtensibleGuid()
    {
        using TempWavFile file = TempWavFile.Create(
            WavProbe.WaveFormatIeeeFloat,
            channels: 1,
            sampleRate: 96000,
            bitsPerSample: 32,
            dataBytes: 16,
            extensible: true,
            validExtensibleGuid: false);

        Assert.False(WavProbe.TryReadFormat(file.Path, out _, out string error));
        Assert.Equal("Invalid WAVE_FORMAT_EXTENSIBLE SubFormat GUID.", error);
    }

    [Fact]
    public void WavFileReaderAcceptanceProfileRejectsNonStandardPlaybackRate()
    {
        using TempWavFile file = TempWavFile.Create(WavProbe.WaveFormatIeeeFloat, channels: 1, sampleRate: 44100, bitsPerSample: 32, dataBytes: 16);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            WavFileReader.ReadMonoFloat(file.Path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates));

        Assert.Equal("WavFileReader: WAV format rejected by acceptance profile", ex.Message);
    }

    [Fact]
    public void TryReadFormatRejectsMissingDataChunk()
    {
        using TempWavFile file = TempWavFile.Create(WavProbe.WaveFormatIeeeFloat, channels: 1, sampleRate: 48000, bitsPerSample: 32, dataBytes: null);

        Assert.False(WavProbe.TryReadFormat(file.Path, out _, out string error));
        Assert.Equal("No data chunk found.", error);
    }

    [Fact]
    public void TryReadFormatRejectsTruncatedDataChunk()
    {
        using TempWavFile file = TempWavFile.Create(
            WavProbe.WaveFormatIeeeFloat,
            channels: 1,
            sampleRate: 48000,
            bitsPerSample: 32,
            dataBytes: 16,
            actualDataBytes: 8);

        Assert.False(WavProbe.TryReadFormat(file.Path, out _, out string error));
        Assert.Equal("Invalid data chunk.", error);
    }

    [Fact]
    public void TryReadFormat_SkipsUnknownChunksAndOddSizePaddingBeforeFmtAndData()
    {
        // RIFF readers must skip unknown chunks and honor even-alignment padding
        // (chunkStart + chunkSize + (chunkSize & 1)). DAWs routinely insert odd-sized
        // JUNK and LIST/INFO metadata around fmt/data; a regression in the skip or
        // padding math would mis-locate the data chunk on otherwise-valid WAVs.
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "tg-wav-junklist-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var fmt = new byte[16];
            BitConverter.GetBytes(WavProbe.WaveFormatIeeeFloat).CopyTo(fmt, 0); // ushort audio format
            BitConverter.GetBytes((ushort)1).CopyTo(fmt, 2);                    // channels
            BitConverter.GetBytes((uint)48000).CopyTo(fmt, 4);                  // sample rate
            BitConverter.GetBytes((uint)192000).CopyTo(fmt, 8);                 // byte rate
            BitConverter.GetBytes((ushort)4).CopyTo(fmt, 12);                   // block align
            BitConverter.GetBytes((ushort)32).CopyTo(fmt, 14);                  // bits per sample

            long dataPayloadOffset;
            using (FileStream stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(0u); // RIFF size, patched after the body is written
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

                WriteChunk(writer, "JUNK", new byte[5]);  // odd-sized: 5 bytes + 1 pad
                WriteChunk(writer, "fmt ", fmt);
                WriteChunk(writer, "LIST", new[] { (byte)'I', (byte)'N', (byte)'F', (byte)'O', (byte)1, (byte)2, (byte)3, (byte)4 });

                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(4u); // one 32-bit float sample
                writer.Flush();
                dataPayloadOffset = stream.Position;
                writer.Write(0.5f);

                writer.Flush();
                long end = stream.Position;
                stream.Seek(4, SeekOrigin.Begin);
                writer.Write((uint)(end - 8));
            }

            Assert.True(WavProbe.TryReadFormat(path, out WavFormatInfo info, out string error), error);
            Assert.Equal("", error);
            Assert.True(info.IsIeeeFloat32Mono);
            Assert.Equal(48000, info.SampleRate);
            Assert.Equal(dataPayloadOffset, info.DataOffset);
            Assert.Equal(4u, info.DataSize);

            WavData wav = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
            float sample = Assert.Single(wav.Samples);
            Assert.Equal(0.5f, sample, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteChunk(BinaryWriter writer, string tag, byte[] body)
    {
        foreach (char c in tag)
        {
            writer.Write((byte)c);
        }

        writer.Write((uint)body.Length);
        writer.Write(body);
        if ((body.Length & 1) == 1)
        {
            writer.Write((byte)0); // RIFF chunks pad to even size
        }
    }

    private sealed class TempWavFile : IDisposable
    {
        private TempWavFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWavFile Create(
            ushort format,
            ushort channels,
            int sampleRate,
            ushort bitsPerSample,
            int? dataBytes,
            bool extensible = false,
            bool validExtensibleGuid = true,
            int? actualDataBytes = null,
            ushort? blockAlignOverride = null)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timegrapher-wav-probe-" + Guid.NewGuid().ToString("N") + ".wav");
            ushort blockAlign = blockAlignOverride ?? (ushort)(channels * bitsPerSample / 8);
            uint byteRate = (uint)(sampleRate * blockAlign);
            uint fmtSize = extensible ? 40u : 16u;
            uint riffSize = 4 + 8 + fmtSize + (dataBytes.HasValue ? 8u + (uint)dataBytes.Value : 0u);

            using (FileStream stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(riffSize);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(fmtSize);
                writer.Write(extensible ? WavProbe.WaveFormatExtensible : format);
                writer.Write(channels);
                writer.Write((uint)sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                if (extensible)
                {
                    writer.Write((ushort)22); // cbSize
                    writer.Write(bitsPerSample);
                    writer.Write((uint)0); // channel mask
                    writer.Write(format); // SubFormat GUID first two bytes
                    writer.Write(validExtensibleGuid
                        ? new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 }
                        : new byte[14]);
                }

                if (dataBytes.HasValue)
                {
                    writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    writer.Write((uint)dataBytes.Value);
                    writer.Write(new byte[actualDataBytes ?? dataBytes.Value]);
                }
            }

            return new TempWavFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
