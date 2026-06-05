using TimeGrapher.Core.AudioIo;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class WavStreamWriterTests
{
    [Fact]
    public void WritePersistsReadableFloatWav()
    {
        string path = Path.Combine(Path.GetTempPath(), "timegrapher-wav-writer-" + Guid.NewGuid().ToString("N") + ".wav");
        float[] samples = { -0.5f, 0.0f, 0.25f, 0.75f };

        try
        {
            using (var writer = new WavStreamWriter())
            {
                Assert.True(writer.Open(path, 48000, 1));
                Assert.True(writer.Write(samples));
                Assert.True(writer.Close());
            }

            WavData read = WavFileReader.ReadMonoFloat(path);

            Assert.Equal(48000, read.SampleRate);
            Assert.Equal(samples.Length, read.Samples.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                Assert.Equal(samples[i], read.Samples[i], precision: 6);
            }
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
