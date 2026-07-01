using System;
using System.IO;
using TimeGrapher.Core.AudioIo;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Lifecycle and bounded back-pressure contract of the asynchronous recording
/// writer: re-open rejection, safe write-after-close, and drop accounting (the
/// back-pressure tactic the class exists for).
/// </summary>
public sealed class QueuedWavStreamWriterTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "tg-qwsw-" + Guid.NewGuid().ToString("N") + ".wav");

    [Fact]
    public void SecondOpenReturnsFalseWhileAlreadyOpen()
    {
        string path = TempPath();
        var w = new QueuedWavStreamWriter();
        try
        {
            Assert.True(w.Open(path, 48000, 1));
            Assert.False(w.Open(path, 48000, 1));
        }
        finally { w.Close(); File.Delete(path); }
    }

    [Fact]
    public void WriteAfterCloseReturnsFalseAndDoesNotThrow()
    {
        string path = TempPath();
        var w = new QueuedWavStreamWriter();
        Assert.True(w.Open(path, 48000, 1));
        w.Write(new float[] { 0.1f, 0.2f });
        Assert.True(w.Close());

        bool result = w.Write(new float[] { 0.3f, 0.4f });
        Assert.False(result);
        File.Delete(path);
    }

    [Fact]
    public void DroppedBlocksCountsEveryBackPressureRejectionExactlyOnce()
    {
        string path = TempPath();
        var w = new QueuedWavStreamWriter(queueCapacity: 1);
        Assert.True(w.Open(path, 48000, 1));

        var sample = new float[256];
        int rejected = 0;
        for (int i = 0; i < 4000; i++)
        {
            if (!w.Write(sample)) rejected++;
        }
        w.Close();

        // Every full-queue rejection (the writer cannot keep up) is counted exactly
        // once - nothing is silently lost or double-counted. (The count itself is
        // timing-dependent, so we assert the invariant, not a specific number.)
        Assert.Equal((ulong)rejected, w.DroppedBlocks);
        File.Delete(path);
    }

    [Fact]
    public void WritesFloatMonoWavAndClosesWithRoundTrip()
    {
        string path = TempPath();
        try
        {
            using var writer = new QueuedWavStreamWriter(queueCapacity: 2);
            Assert.True(writer.Open(path, sampleRate: 48000, channels: 1));
            Assert.True(writer.Write(new float[] { 0.1f, -0.2f, 0.3f, -0.4f }));
            Assert.True(writer.Close());

            WavData data = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);

            Assert.Equal(48000, data.SampleRate);
            Assert.Equal(new[] { 0.1f, -0.2f, 0.3f, -0.4f }, data.Samples);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void DisposeFinalizesHeaderForAllQueuedBlocksWithoutExplicitClose()
    {
        string path = TempPath();
        try
        {
            var writer = new QueuedWavStreamWriter(queueCapacity: 8);
            Assert.True(writer.Open(path, sampleRate: 48000, channels: 1));
            Assert.True(writer.Write(new float[] { 0.1f, 0.2f }));
            Assert.True(writer.Write(new float[] { 0.3f, 0.4f }));
            Assert.True(writer.Write(new float[] { 0.5f, 0.6f }));

            // No explicit Close(): finalization now lives in the writer thread, so Dispose
            // alone must drain every queued block and patch the RIFF/data sizes. The file is
            // then a valid, correctly-sized recording - not one left with placeholder sizes
            // that WavFileReader would reject as empty. (This is the same code path that
            // finalizes in the background when a slow-disk Close() times out on the join.)
            writer.Dispose();

            WavData data = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f }, data.Samples);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CloseIsIdempotentAndSafeToCallRepeatedly()
    {
        string path = TempPath();
        try
        {
            var writer = new QueuedWavStreamWriter();
            Assert.True(writer.Open(path, sampleRate: 48000, channels: 1));
            Assert.True(writer.Write(new float[] { 0.1f, 0.2f }));

            // First Close finalizes and disposes the queue. A second Close (e.g. the retry path
            // after a slow-disk timeout, or a later Dispose) must not throw ObjectDisposedException
            // on the already-disposed queue, must report closed, and Dispose must stay safe.
            Assert.True(writer.Close());
            Assert.False(writer.IsOpen);
            Assert.True(writer.Close());
            writer.Dispose();

            WavData data = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
            Assert.Equal(new[] { 0.1f, 0.2f }, data.Samples);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
