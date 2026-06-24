using System;
using System.IO;
using System.Linq;
using TimeGrapher.Verify;
using Xunit;

namespace TimeGrapher.Verify.Tests;

/// <summary>
/// Pins the refiner-training exporter's contract: it emits a CSV with the
/// expected schema and the scenario mix is weighted toward weak-A (the more
/// serious B->A failure), so weak-A rows are the majority.
/// </summary>
public sealed class TrainingDataExporterTests
{
    [Fact]
    public void Export_WritesWeakAWeightedCsv()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tg-train-" + Guid.NewGuid().ToString("N"));
        try
        {
            int code = TrainingDataExporter.Export(dir);
            Assert.Equal(0, code);

            string csv = Path.Combine(dir, "landmark_training.csv");
            Assert.True(File.Exists(csv), "expected landmark_training.csv");

            string[] lines = File.ReadAllLines(csv);
            Assert.True(lines.Length > 1, "expected a header plus data rows");

            string header = lines[0];
            Assert.StartsWith("scenario,bph,", header);
            Assert.Contains("det_a_off,det_c_off,true_a_off,true_c_off", header);
            Assert.Contains("env_0", header);

            // Every data row has the same column count as the header.
            int headerCols = header.Split(',').Length;
            foreach (string line in lines.Skip(1))
            {
                Assert.Equal(headerCols, line.Split(',').Length);
            }

            // weak-A scenarios are the majority of the rows.
            int total = lines.Length - 1;
            int weakA = lines.Skip(1).Count(l => l.StartsWith("weak-a", StringComparison.Ordinal));
            Assert.True(weakA * 2 > total, $"weak-A rows {weakA}/{total} should be the majority");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
