using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Audio;

internal static class AudioSmokeRunner
{
    private const int DefaultDeviceIndex = 0;
    private const int DefaultRate = 48000;
    private const int DefaultDurationMs = 1500;

    public static int Run(string[] args, bool capture)
    {
        Console.WriteLine("audio_smoke=begin");
        Console.WriteLine("os=" + Environment.OSVersion);
        Console.WriteLine("can_capture=" + LiveAudioBackend.CanCapture.ToString(CultureInfo.InvariantCulture));

        if (!LiveAudioBackend.CanCapture)
        {
            Console.Error.WriteLine("Live audio capture is not supported on this platform.");
            return 2;
        }

        IReadOnlyList<LiveAudioDevice> devices;
        try
        {
            devices = LiveAudioBackend.EnumerateInputDevices();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Audio device enumeration failed: " + ex.Message);
            return 2;
        }

        Console.WriteLine("source_count=" + devices.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < devices.Count; i++)
        {
            LiveAudioDevice device = devices[i];
            Console.WriteLine(
                "source[" + i.ToString(CultureInfo.InvariantCulture) + "]=" +
                device.Number.ToString(CultureInfo.InvariantCulture) + ":" +
                device.Name);
        }

        if (!capture)
        {
            Console.WriteLine("audio_smoke=end");
            return 0;
        }

        if (devices.Count == 0)
        {
            Console.Error.WriteLine("No live audio capture source was found.");
            return 2;
        }

        int deviceIndex = ParsePositiveOption(args, "--device-index", DefaultDeviceIndex);
        if (deviceIndex >= devices.Count)
        {
            Console.Error.WriteLine(
                "Requested device index " + deviceIndex.ToString(CultureInfo.InvariantCulture) +
                " but only " + devices.Count.ToString(CultureInfo.InvariantCulture) + " source(s) are available.");
            return 2;
        }

        // An absent option keeps its default; a present-but-invalid value (missing,
        // non-numeric, or non-positive) is a usage error rather than a silent
        // fallback that would run the smoke at an unintended rate/duration.
        if (TryParsePositiveOption(args, "--rate", DefaultRate, out int sampleRate) == OptionParse.Invalid)
        {
            Console.Error.WriteLine("Invalid --rate value: expected a positive integer.");
            return 2;
        }

        if (TryParsePositiveOption(args, "--duration-ms", DefaultDurationMs, out int durationMs) == OptionParse.Invalid)
        {
            Console.Error.WriteLine("Invalid --duration-ms value: expected a positive integer.");
            return 2;
        }

        LiveAudioDevice selected = devices[deviceIndex];
        Console.WriteLine("capture_source_index=" + deviceIndex.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("capture_source_number=" + selected.Number.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("capture_source_name=" + selected.Name);
        Console.WriteLine("capture_rate=" + sampleRate.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("capture_duration_ms=" + durationMs.ToString(CultureInfo.InvariantCulture));

        var buffer = new MasterAudioBuffer(sampleRate);
        using ILiveAudioWorker worker = LiveAudioBackend.CreateWorker(buffer);
        int dataReadyCount = 0;
        worker.DataReady += () => Interlocked.Increment(ref dataReadyCount);

        try
        {
            worker.Start(selected.Number, sampleRate, volume: 1.0f, bufferMilliseconds: LiveAudioDefaults.BufferMilliseconds);
            Thread.Sleep(durationMs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Capture smoke failed to start: " + ex.Message);
            return 3;
        }
        finally
        {
            _ = worker.TryStop(TimeSpan.FromSeconds(2));
        }

        MasterAudioBufferSnapshot snapshot = buffer.GetSnapshot();
        Console.WriteLine("data_ready_count=" + dataReadyCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("samples_written=" + snapshot.TotalSamplesWritten.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("capture_fps=" + snapshot.Fps.ToString("F1", CultureInfo.InvariantCulture));
        Console.WriteLine("capture_sps=" + snapshot.Sps.ToString("F0", CultureInfo.InvariantCulture));
        Console.WriteLine("capture_spf=" + snapshot.Spf.ToString("F0", CultureInfo.InvariantCulture));

        if (snapshot.TotalSamplesWritten == 0)
        {
            Console.Error.WriteLine("Capture smoke received no samples.");
            return 3;
        }

        Console.WriteLine("audio_smoke=end");
        return 0;
    }

    /// <summary>Outcome of parsing one positive-integer option.</summary>
    internal enum OptionParse
    {
        /// <summary>The option was not present; the default applies.</summary>
        Absent,

        /// <summary>The option was present with a valid positive integer.</summary>
        Valid,

        /// <summary>The option was present but its value was missing, non-numeric, or non-positive.</summary>
        Invalid,
    }

    /// <summary>
    /// Parses a positive-integer option, distinguishing an absent option (keep the
    /// default) from a present-but-invalid one (a usage error). <paramref name="value"/>
    /// is the default unless a valid positive value was supplied.
    /// </summary>
    internal static OptionParse TryParsePositiveOption(string[] args, string name, int defaultValue, out int value)
    {
        value = defaultValue;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals(name, StringComparison.Ordinal))
            {
                if (i + 1 < args.Length &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                    parsed > 0)
                {
                    value = parsed;
                    return OptionParse.Valid;
                }

                // Present but missing/non-numeric/non-positive value.
                return OptionParse.Invalid;
            }

            string prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (int.TryParse(arg[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inlineValue) &&
                    inlineValue > 0)
                {
                    value = inlineValue;
                    return OptionParse.Valid;
                }

                return OptionParse.Invalid;
            }
        }

        return OptionParse.Absent;
    }

    /// <summary>
    /// Fallback-on-anything-else variant: returns the parsed positive value when
    /// present and valid, otherwise the default. Kept for callers that treat an
    /// absent and an invalid option the same (the benchmark runner's optional
    /// limits and the device-index lookup), which validate downstream.
    /// </summary>
    internal static int ParsePositiveOption(string[] args, string name, int defaultValue)
    {
        _ = TryParsePositiveOption(args, name, defaultValue, out int value);
        return value;
    }
}
