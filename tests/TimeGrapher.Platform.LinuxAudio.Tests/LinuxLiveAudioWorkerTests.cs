using System.Diagnostics;
using TimeGrapher.Core.Shared;
using TimeGrapher.Platform.LinuxAudio;
using Xunit;

namespace TimeGrapher.Platform.LinuxAudio.Tests;

public sealed class LinuxLiveAudioWorkerTests
{
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
    public void ProbeStartInfoForSampleRate_KillsLongRunningProbeBeforeAcceptingRate()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul"
            : "sleep 30");

        bool supported = LinuxLiveAudioWorker.ProbeStartInfoForSampleRate(
            BuildStartInfo(fileName, args),
            startupProbeTimeoutMs: 100,
            cleanupTimeoutMs: 5000);

        Assert.True(supported);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
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

        (string fileName, string[] args) = ShellCommand(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 6 > nul & echo done"
            : "sleep 2; echo done");

        string output = LinuxLiveAudioWorker.RunCommand(fileName, TimeSpan.FromMilliseconds(200), args);

        Assert.Equal("", output);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
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
