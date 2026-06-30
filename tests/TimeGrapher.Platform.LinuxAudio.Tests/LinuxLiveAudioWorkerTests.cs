using System.Diagnostics;
using TimeGrapher.Core.Shared;
using TimeGrapher.Platform.LinuxAudio;
using Xunit;

namespace TimeGrapher.Platform.LinuxAudio.Tests;

public sealed class LinuxLiveAudioWorkerTests
{
    [Fact]
    public void ParseWpctlSources_IncludesSourceWhoseNameContainsVideo()
    {
        // A source line whose NAME contains "Video" (a real USB capture device with
        // an audio source) must not terminate enumeration: the parser breaks the
        // Sources block only on a real section header, never on a source-name
        // substring, so that source and every source after it stay selectable.
        const string status = """
Audio
 Sources:
    *   65. USB Video Capture Mono [vol: 1.00]
        66. Cubilux CA7 Mono [vol: 0.80]
 Filters:
 Streams:
""";

        IReadOnlyList<LiveAudioDevice> devices = LinuxLiveAudioWorker.ParseWpctlSources(status);

        Assert.Collection(
            devices,
            first =>
            {
                Assert.Equal(65, first.Number);
                Assert.Equal("USB Video Capture Mono", first.Name);
            },
            second =>
            {
                Assert.Equal(66, second.Number);
                Assert.Equal("Cubilux CA7 Mono", second.Name);
            });
    }

    [Fact]
    public void ParseWpctlSources_ReturnsSourceNodesOnly()
    {
        const string status = """
Audio
 Devices:
        48. Built-in Audio [alsa]
 Sinks:
    *   56. Built-in Audio Digital Stereo (HDMI) [vol: 0.40]
 Sources:
    *   65. USB PnP Sound Device Mono [vol: 1.00]
        66. Cubilux CA7 Mono [vol: 0.80]
 Filters:
 Streams:
""";

        IReadOnlyList<LiveAudioDevice> devices = LinuxLiveAudioWorker.ParseWpctlSources(status);

        Assert.Collection(
            devices,
            first =>
            {
                Assert.Equal(65, first.Number);
                Assert.Equal("USB PnP Sound Device Mono", first.Name);
            },
            second =>
            {
                Assert.Equal(66, second.Number);
                Assert.Equal("Cubilux CA7 Mono", second.Name);
            });
    }

    [Fact]
    public void ParseWpctlSources_ReturnsEmptyWhenNoSources()
    {
        const string status = """
Audio
 Devices:
        48. Built-in Audio [alsa]
 Sinks:
    *   56. Built-in Audio Digital Stereo (HDMI) [vol: 0.40]
 Sources:
 Filters:
 Streams:
""";

        IReadOnlyList<LiveAudioDevice> devices = LinuxLiveAudioWorker.ParseWpctlSources(status);

        Assert.Empty(devices);
    }

    [Fact]
    public void ParseAlsaCaptureDevices_ReturnsHardwareDevices()
    {
        const string arecordList = """
**** List of CAPTURE Hardware Devices ****
card 3: Device [USB PnP Sound Device], device 0: USB Audio [USB Audio]
  Subdevices: 1/1
  Subdevice #0: subdevice #0
card 4: CA7 [Cubilux CA7], device 0: USB Audio [USB Audio]
  Subdevices: 1/1
  Subdevice #0: subdevice #0
""";

        IReadOnlyList<LiveAudioDevice> devices = LinuxLiveAudioWorker.ParseAlsaCaptureDevices(arecordList);

        Assert.Collection(
            devices,
            first =>
            {
                Assert.True(LinuxLiveAudioWorker.TryDecodeAlsaDeviceNumber(first.Number, out int card, out int device));
                Assert.Equal(3, card);
                Assert.Equal(0, device);
                Assert.Equal("ALSA hw:3,0 USB PnP Sound Device - USB Audio", first.Name);
            },
            second =>
            {
                Assert.True(LinuxLiveAudioWorker.TryDecodeAlsaDeviceNumber(second.Number, out int card, out int device));
                Assert.Equal(4, card);
                Assert.Equal(0, device);
                Assert.Equal("ALSA hw:4,0 Cubilux CA7 - USB Audio", second.Name);
            });
    }

    [Fact]
    public void ParseAlsaCaptureDevices_ReturnsEmptyWhenNoHardwareDevices()
    {
        const string arecordList = """
**** List of CAPTURE Hardware Devices ****
""";

        IReadOnlyList<LiveAudioDevice> devices = LinuxLiveAudioWorker.ParseAlsaCaptureDevices(arecordList);

        Assert.Empty(devices);
    }

    [Fact]
    public void GetCandidateSampleRates_ReturnsOnlyRatesAcceptedByDeviceProbe()
    {
        IReadOnlyList<int> rates = LinuxLiveAudioWorker.GetCandidateSampleRates(
            rate => rate is 48000 or 192000);

        Assert.Equal(new[] { 48000, 192000 }, rates);
    }

    [Fact]
    public void FormatPipeWireVolumePercent_ClampsToEndpointVolumeRange()
    {
        Assert.Equal("0%", LinuxLiveAudioWorker.FormatPipeWireVolumePercent(-1));
        Assert.Equal("50%", LinuxLiveAudioWorker.FormatPipeWireVolumePercent(50));
        Assert.Equal("100%", LinuxLiveAudioWorker.FormatPipeWireVolumePercent(101));
    }

    [Fact]
    public void SetMatchingPipeWireSourceVolumes_ConfiguresMatchingSourcesOnce()
    {
        var sources = new[]
        {
            new LiveAudioDevice(65, "USB PnP Sound Device Mono"),
            new LiveAudioDevice(65, "USB PnP Sound Device Mono"),
            new LiveAudioDevice(66, "Built-in Audio Mono"),
            new LiveAudioDevice(67, "CM108 Audio Controller Mono"),
        };
        var configured = new List<(int SourceNumber, string Volume)>();

        int count = LinuxLiveAudioWorker.SetMatchingPipeWireSourceVolumes(
            sources,
            new[] { "usb pnp sound device", "CM108 Audio Controller Mono" },
            "50%",
            (sourceNumber, volume) => configured.Add((sourceNumber, volume)));

        Assert.Equal(2, count);
        Assert.Equal(new[] { (65, "50%"), (67, "50%") }, configured);
    }

    [Fact]
    public void ProbeStartInfoForSampleRate_KillsLongRunningProbeBeforeAcceptingRate()
    {
        const int startupProbeTimeoutMs = 100;
        const int cleanupTimeoutMs = 5000;
        Stopwatch stopwatch = Stopwatch.StartNew();
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul"
            : "sleep 30");

        bool supported = LinuxLiveAudioWorker.ProbeStartInfoForSampleRate(
            BuildStartInfo(fileName, args),
            startupProbeTimeoutMs: startupProbeTimeoutMs,
            cleanupTimeoutMs: cleanupTimeoutMs);

        Assert.True(supported);
        // Bound derived from the timeouts the probe is given (probe wait + cleanup wait)
        // plus a documented CI grace, rather than a magic 10 s: the kill must land well
        // inside this, and far under the ~30 s child that would run if the kill failed.
        const int ciGraceMs = 5000;
        Assert.True(stopwatch.Elapsed <
            TimeSpan.FromMilliseconds(startupProbeTimeoutMs + cleanupTimeoutMs + ciGraceMs));
    }

    [Fact]
    public void ProbeStartInfoForSampleRate_RejectsEarlyFailure()
    {
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "exit 7"
            : "exit 7");

        Assert.False(LinuxLiveAudioWorker.ProbeStartInfoForSampleRate(
            BuildStartInfo(fileName, args),
            startupProbeTimeoutMs: 5000,
            cleanupTimeoutMs: 5000));
    }

    [Fact]
    public void RunCommand_ReturnsOutputForSuccessfulProcess()
    {
        (string fileName, string[] args) = ShellCommand("echo ok");

        string output = LinuxLiveAudioWorker.RunCommand(fileName, TimeSpan.FromSeconds(2), args);

        Assert.Equal("ok", output.Trim());
    }

    [Fact]
    public void RunCommand_PinsChildLocaleToC()
    {
        // RunCommand forces LC_ALL=C/LANG=C so wpctl/arecord emit the English tokens
        // the parsers require regardless of the host locale; assert the child sees it.
        (string fileName, string[] args) = ShellCommand(
            OperatingSystem.IsWindows() ? "echo %LC_ALL%" : "echo $LC_ALL");

        string output = LinuxLiveAudioWorker.RunCommand(fileName, TimeSpan.FromSeconds(2), args);

        Assert.Equal("C", output.Trim());
    }

    [Fact]
    public void RunCommand_ReturnsEmptyWhenProcessExceedsTimeout()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        // A clearly long-lived child (~30 s) against a 200 ms timeout: RunCommand must
        // give up and return empty far inside the 5 s ceiling. (The old ~2 s child raced
        // the 2 s assertion, so a slow-to-start child could finish before the bound.)
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul & echo done"
            : "sleep 30; echo done");

        string output = LinuxLiveAudioWorker.RunCommand(fileName, TimeSpan.FromMilliseconds(200), args);

        Assert.Equal("", output);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryStop_TimeoutLeavesWorkerRestoppable()
    {
        var worker = new LinuxLiveAudioWorker(new MasterAudioBuffer(48000));
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul"
            : "sleep 30");
        worker.StartCaptureProcessForTests(BuildStartInfo(fileName, args));
        int waitCalls = 0;
        worker.InstallWaitForExitForTests((process, timeout) =>
        {
            if (Interlocked.Increment(ref waitCalls) == 1)
            {
                return false;
            }

            return process.WaitForExit(timeout);
        });

        Assert.False(
            worker.TryStop(TimeSpan.Zero),
            "The zero-length wait should exercise the timed-out stop path instead of silently passing.");

        Assert.True(worker.TryStop(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, waitCalls);
    }

    [Fact]
    public void CaptureEnded_RaisedWhenProcessExitsAfterStartupProbe()
    {
        var worker = new LinuxLiveAudioWorker(new MasterAudioBuffer(48000));
        using var captureEnded = new ManualResetEventSlim(initialState: false);
        worker.CaptureEnded += captureEnded.Set;

        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 3 > nul"
            : "sleep 2");
        worker.StartCaptureProcessForTests(BuildStartInfo(fileName, args));

        Assert.True(captureEnded.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void CaptureEnded_NotRaisedForRequestedStop()
    {
        var worker = new LinuxLiveAudioWorker(new MasterAudioBuffer(48000));
        bool raised = false;
        worker.CaptureEnded += () => raised = true;

        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul"
            : "sleep 30");
        worker.StartCaptureProcessForTests(BuildStartInfo(fileName, args));

        Assert.True(worker.TryStop(TimeSpan.FromSeconds(5)));
        Assert.False(raised);
    }

    [Fact]
    public void StartProcess_EarlyExitReportsStderrInFailure()
    {
        var worker = new LinuxLiveAudioWorker(new MasterAudioBuffer(48000));
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "echo boom 1>&2 & exit 1"
            : "echo boom 1>&2; exit 1");

        // A generous probe window keeps this deterministic on loaded CI runners,
        // where child-process startup can exceed the production 250 ms probe.
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => worker.StartCaptureProcessForTests(BuildStartInfo(fileName, args), startupProbeTimeoutMs: 5000));

        Assert.Equal(fileName + " exited: boom", ex.Message);
    }

    [Fact]
    public void StartProcess_EarlyExitDoesNotAlsoRaiseCaptureEnded()
    {
        // A startup failure is reported by the thrown exception; the reader's
        // unexpected-death CaptureEnded must stay suppressed so the same failure
        // is not reported twice (the UI would otherwise double-handle it).
        var worker = new LinuxLiveAudioWorker(new MasterAudioBuffer(48000));
        bool raised = false;
        worker.CaptureEnded += () => raised = true;

        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "echo boom 1>&2 & exit 1"
            : "echo boom 1>&2; exit 1");

        Assert.Throws<InvalidOperationException>(
            () => worker.StartCaptureProcessForTests(BuildStartInfo(fileName, args), startupProbeTimeoutMs: 5000));

        Assert.False(raised, "a startup failure must not also raise CaptureEnded");
    }

    [Fact]
    public void BuildPipeWireStartInfo_DefaultBuffer_OmitsLatencyFlag()
    {
        // At the default buffer pw-record keeps its native latency, so no --latency flag
        // is emitted and the capture target "-" stays last for the probe builder's swap.
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildPipeWireStartInfo(deviceNumber: 0, sampleRate: 48000);

        Assert.DoesNotContain("--latency", info.ArgumentList);
        Assert.Equal("-", info.ArgumentList[^1]);
    }

    [Fact]
    public void BuildPipeWireStartInfo_NonDefaultBuffer_AddsLatencyInMilliseconds()
    {
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildPipeWireStartInfo(deviceNumber: 0, sampleRate: 48000, bufferMilliseconds: 50);

        int index = info.ArgumentList.IndexOf("--latency");
        Assert.True(index >= 0);
        Assert.Equal("50ms", info.ArgumentList[index + 1]);
        Assert.Equal("-", info.ArgumentList[^1]);
    }

    [Fact]
    public void BuildAlsaStartInfo_DefaultBuffer_OmitsBufferTimeFlag()
    {
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildAlsaStartInfo(card: 3, device: 0, sampleRate: 48000);

        Assert.DoesNotContain("--buffer-time", info.ArgumentList);
        Assert.Equal("-", info.ArgumentList[^1]);
    }

    [Fact]
    public void BuildAlsaStartInfo_NonDefaultBuffer_AddsBufferTimeInMicroseconds()
    {
        // arecord takes buffer time in microseconds, so 50 ms maps to 50000.
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildAlsaStartInfo(card: 3, device: 0, sampleRate: 48000, bufferMilliseconds: 50);

        int index = info.ArgumentList.IndexOf("--buffer-time");
        Assert.True(index >= 0);
        Assert.Equal("50000", info.ArgumentList[index + 1]);
        Assert.Equal("-", info.ArgumentList[^1]);
    }

    [Fact]
    public void BuildPipeWireProbeStartInfo_TargetsDevNull_WithNoLatencyFlag()
    {
        // The probe swaps the trailing capture target for /dev/null; it must call the
        // builder at the default buffer so no --latency flag is appended before the swap.
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildPipeWireProbeStartInfo(deviceNumber: 0, sampleRate: 48000);

        Assert.Equal("/dev/null", info.ArgumentList[^1]);
        Assert.DoesNotContain("--latency", info.ArgumentList);
    }

    [Fact]
    public void BuildAlsaProbeStartInfo_TargetsDevNull_WithNoBufferTimeFlag()
    {
        ProcessStartInfo info = LinuxLiveAudioWorker.BuildAlsaProbeStartInfo(card: 3, device: 0, sampleRate: 48000);

        Assert.Equal("/dev/null", info.ArgumentList[^1]);
        Assert.DoesNotContain("--buffer-time", info.ArgumentList);
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string[] arguments)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static (string FileName, string[] Arguments) ShellCommand(string command)
    {
        return OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", command })
            : ("/bin/sh", new[] { "-c", command });
    }
}
