using System;
using System.IO;
using TimeGrapher.Core.AudioIo;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Per-format decode coverage for <see cref="WavFileReader.ReadMonoFloat"/>. The
/// round-trip tests only exercise the IEEE-float-32 arm (the writer's output
/// format); these pin the PCM 16/24/32 sign-extension+scaling, IEEE float64, and
/// multi-channel channel-0 extraction arms against known sample values.
/// </summary>
public sealed class WavFileReaderDecodeTests
{
    private const ushort PcmFormat = 1;
    private const ushort FloatFormat = 3;

    private static string WriteWav(ushort audioFormat, ushort channels, ushort bitsPerSample, byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), "tg-decode-" + Guid.NewGuid().ToString("N") + ".wav");
        ushort blockAlign = (ushort)(channels * bitsPerSample / 8);
        uint byteRate = (uint)(48000 * blockAlign);
        using var w = new BinaryWriter(File.Create(path));
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        w.Write(4u + 8u + 16u + 8u + (uint)data.Length);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16u);
        w.Write(audioFormat);
        w.Write(channels);
        w.Write(48000u);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        w.Write((uint)data.Length);
        w.Write(data);
        return path;
    }

    private static byte[] Pcm24Bytes(params int[] values)
    {
        var bytes = new byte[values.Length * 3];
        for (int i = 0; i < values.Length; i++)
        {
            bytes[i * 3] = (byte)(values[i] & 0xFF);
            bytes[i * 3 + 1] = (byte)((values[i] >> 8) & 0xFF);
            bytes[i * 3 + 2] = (byte)((values[i] >> 16) & 0xFF);
        }
        return bytes;
    }

    [Fact]
    public void DecodesPcm16WithSignExtensionAndScaling()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((short)-32768);
        bw.Write((short)32767);
        bw.Write((short)0);
        string path = WriteWav(PcmFormat, 1, 16, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(48000, wav.SampleRate);
            Assert.Equal(-1.0f, wav.Samples[0], 6);
            Assert.Equal(32767.0f / 32768.0f, wav.Samples[1], 6);
            Assert.Equal(0.0f, wav.Samples[2], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DecodesPcm24WithSignExtensionAndScaling()
    {
        string path = WriteWav(PcmFormat, 1, 24, Pcm24Bytes(0x800000, 0x7FFFFF, 0x000000));
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(-1.0f, wav.Samples[0], 6);              // 0x800000 sign-extends to -8388608
            Assert.Equal(8388607.0f / 8388608.0f, wav.Samples[1], 6);
            Assert.Equal(0.0f, wav.Samples[2], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DecodesPcm32WithScaling()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(int.MinValue);
        bw.Write(int.MaxValue);
        bw.Write(0);
        string path = WriteWav(PcmFormat, 1, 32, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(-1.0f, wav.Samples[0], 6);
            Assert.Equal(int.MaxValue / 2147483648.0f, wav.Samples[1], 6);
            Assert.Equal(0.0f, wav.Samples[2], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DecodesIeeeFloat64()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(-1.0);
        bw.Write(0.5);
        bw.Write(0.0);
        string path = WriteWav(FloatFormat, 1, 64, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(-1.0f, wav.Samples[0], 6);
            Assert.Equal(0.5f, wav.Samples[1], 6);
            Assert.Equal(0.0f, wav.Samples[2], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractsChannelZeroFromStereoPcm16()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((short)16384); bw.Write((short)-16384); // frame 0: ch0=0.5, ch1 ignored
        bw.Write((short)-32768); bw.Write((short)0);      // frame 1: ch0=-1.0, ch1 ignored
        string path = WriteWav(PcmFormat, 2, 16, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(2, wav.Samples.Length);
            Assert.Equal(0.5f, wav.Samples[0], 6);
            Assert.Equal(-1.0f, wav.Samples[1], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FoldsNonFiniteIeeeFloat32SamplesToZero()
    {
        // A legal IEEE float32 WAV can carry NaN/Inf bit patterns; the decode
        // boundary must fold them to a finite value so they never latch into the
        // recursive HPF/envelope DSP state (regression guard mirroring the
        // PlaybackWorker non-finite fold). Finite samples must pass through unchanged.
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(0.5f);
        bw.Write(float.NaN);
        bw.Write(float.PositiveInfinity);
        bw.Write(float.NegativeInfinity);
        bw.Write(-0.25f);
        string path = WriteWav(FloatFormat, 1, 32, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(5, wav.Samples.Length);
            Assert.All(wav.Samples, s => Assert.True(float.IsFinite(s)));
            Assert.Equal(0.5f, wav.Samples[0], 6);
            Assert.Equal(0.0f, wav.Samples[1]);  // NaN folded
            Assert.Equal(0.0f, wav.Samples[2]);  // +Inf folded
            Assert.Equal(0.0f, wav.Samples[3]);  // -Inf folded
            Assert.Equal(-0.25f, wav.Samples[4], 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FoldsNonFiniteIeeeFloat64SamplesToZero()
    {
        // The float64 decode arm shares the same non-finite fold.
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(0.5);
        bw.Write(double.NaN);
        bw.Write(double.PositiveInfinity);
        bw.Write(double.NegativeInfinity);
        string path = WriteWav(FloatFormat, 1, 64, ms.ToArray());
        try
        {
            WavData wav = WavFileReader.ReadMonoFloat(path);
            Assert.Equal(4, wav.Samples.Length);
            Assert.All(wav.Samples, s => Assert.True(float.IsFinite(s)));
            Assert.Equal(0.5f, wav.Samples[0], 6);
            Assert.Equal(0.0f, wav.Samples[1]);
            Assert.Equal(0.0f, wav.Samples[2]);
            Assert.Equal(0.0f, wav.Samples[3]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThrowsOnProbeAcceptedButUndecodableFormat()
    {
        // PCM 8-bit passes the probe's basic field checks (BytesPerSample=1,
        // BlockAlign=1) but matches no decode arm -> the switch's default throws.
        string path = WriteWav(PcmFormat, 1, 8, new byte[] { 0x10, 0x20, 0x30, 0x40 });
        try
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => WavFileReader.ReadMonoFloat(path));
            Assert.Contains("unsupported format", ex.Message);
        }
        finally { File.Delete(path); }
    }
}
