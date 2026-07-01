using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Platform.LinuxAudio;

public sealed class LinuxLiveAudioWorker : ILiveAudioWorker
{
    private const int ReplacementStopTimeoutMs = 2000;
    private const int StartupFailureProbeTimeoutMs = 250;
    // Device enumeration (wpctl status / arecord -l) runs synchronously on the UI thread
    // via LoadAudioDevices on the capture-ended, device-dropdown and reset paths. A 2 s
    // ceiling per probe stacked into a multi-second UI stall while PipeWire was re-settling
    // a just-plugged USB mic; 800 ms keeps a healthy probe well within budget while capping
    // the worst-case UI block. A genuinely slower wpctl may then miss the window and the
    // device list falls back to empty for that refresh (recoverable by reopening the list).
    private static readonly TimeSpan CommandProbeTimeout = TimeSpan.FromMilliseconds(800);
    private const int Channels = MasterAudioBuffer.Channels;
    private const int AlsaDeviceNumberBase = 1_000_000;
    private const int AlsaDeviceNumberStride = 1_000;
    private static readonly HashSet<string> GenericAudioNameTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "alsa",
        "hw",
        "usb",
        "audio",
        "device",
        "mono",
        "stereo",
        "capture",
        "source",
        "input",
        "analog",
        "digital",
        "fallback",
    };

    private static readonly Regex SourceLineRegex = new(
        @"(?:^|\s)(?:\*\s*)?(?<id>\d+)\.\s+(?<name>.+?)(?:\s+\[|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AlsaCaptureDeviceRegex = new(
        @"^card\s+(?<card>\d+):\s+(?<cardId>[^\[]+)\[(?<cardName>[^\]]+)\],\s+device\s+(?<device>\d+):\s+(?<deviceName>.+?)(?:\s+\[.*\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PipeWirePropertyRegex = new(
        @"^(?:\*\s*)?(?<name>[A-Za-z0-9_.-]+)\s*=\s*(?:""(?<quoted>[^""]*)""|(?<bare>\S+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HwParamsRateRegex = new(
        @"(?:^|\n)\s*RATE:\s+(?<rate>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IntegerRegex = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly object PipeWireRateProbeLock = new();
    private static IReadOnlyDictionary<int, int> _pipeWireAlsaRateProbeDevices = new Dictionary<int, int>();
    private static IReadOnlySet<int> _pipeWireRateProbeSourceNumbers = new HashSet<int>();

    private readonly MasterAudioBuffer _rawAudio;
    private readonly Stopwatch _timer = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _stderrLock = new();

    private Process? _process;
    private Thread? _stdoutThread;
    private Thread? _stderrThread;
    private bool _timerStarted;
    private double _lastTime;
    private ulong _frameCount;
    private ulong _sampleCount;
    // Written on the UI thread (Start/SetVolume), read on the capture reader
    // thread (WriteSamples): volatile so a gain change is published promptly
    // and never read from a stale register/cache.
    private volatile float _volume = 1.0f;
    private string _processErrorPrefix = "pw-record";
    private Func<Process, TimeSpan, bool>? _waitForExitOverride;
    private volatile bool _paused;
    private volatile bool _stopRequested;
    // Ensures CaptureEnded fires at most once per capture session, even when both the
    // reader's end-of-stream path and the post-probe liveness re-check observe the
    // same death. Reset to 0 at the start of each StartProcess.
    private int _captureEndedFired;

    public LinuxLiveAudioWorker(MasterAudioBuffer buffer)
    {
        _rawAudio = buffer;
        _rawAudio.Reset();
    }

    public event Action? DataReady;

    public event Action? CaptureEnded;

    public bool IsPaused => _paused;

    public static IReadOnlyList<LiveAudioDevice> EnumerateInputDevices()
    {
        // Enumeration runs synchronously on the UI thread, so the whole refresh - not
        // just each probe - must stay within one CommandProbeTimeout window. Track the
        // elapsed budget so the ALSA fallback only runs with the time wpctl left over.
        var budget = Stopwatch.StartNew();
        string status = RunCommand("wpctl", "status");
        IReadOnlyList<LiveAudioDevice> devices = ParseWpctlSources(status);
        if (devices.Count > 0)
        {
            return devices;
        }

        // wpctl found no sources. Fall back to the ALSA probe only if budget remains: if
        // wpctl itself consumed the whole window (a stalled PipeWire status rather than a
        // genuinely source-less system), a second full-timeout probe would just double the
        // UI stall, so end the refresh empty (recoverable by reopening the list) instead.
        TimeSpan remaining = CommandProbeTimeout - budget.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return Array.Empty<LiveAudioDevice>();
        }

        string arecordList = RunCommand("arecord", remaining, "-l");
        return ParseAlsaCaptureDevices(arecordList);
    }

    public static void SetPipeWireSourceVolume(IReadOnlyList<string> sourceNameFragments, int volumePercent)
    {
        string status = RunCommand("wpctl", "status");
        IReadOnlyList<LiveAudioDevice> sources = ParseWpctlSources(status);
        string volume = FormatPipeWireVolumePercent(volumePercent);
        SetMatchingPipeWireSourceVolumes(
            sources,
            sourceNameFragments,
            volume,
            static (sourceNumber, volumeArgument) =>
            {
                RunCommand(
                    "wpctl",
                    "set-volume",
                    sourceNumber.ToString(CultureInfo.InvariantCulture),
                    volumeArgument);
            });
    }

    internal static IReadOnlyList<LiveAudioDevice> ParseWpctlSources(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Array.Empty<LiveAudioDevice>();
        }

        var devices = new List<LiveAudioDevice>();
        bool inSources = false;
        foreach (string rawLine in status.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line.Contains("Sources:", StringComparison.Ordinal))
            {
                inSources = true;
                continue;
            }

            if (!inSources)
            {
                continue;
            }

            // A source line carries an id (e.g. "65. USB Video Capture [vol: 1.00]")
            // and must be added even when its NAME contains a section keyword such
            // as "Video": the break below only ends the Sources block on a real
            // section header, which never matches the id-prefixed source pattern.
            Match match = SourceLineRegex.Match(line);
            if (match.Success &&
                int.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                string name = match.Groups["name"].Value.Trim();
                if (name.Length != 0)
                {
                    devices.Add(new LiveAudioDevice(id, name));
                }

                continue;
            }

            if (line.Contains("Filters:", StringComparison.Ordinal) ||
                line.Contains("Streams:", StringComparison.Ordinal) ||
                line.Contains("Video", StringComparison.Ordinal) ||
                line.Contains("Settings", StringComparison.Ordinal))
            {
                break;
            }
        }

        return devices;
    }

    internal static IReadOnlyList<LiveAudioDevice> ParseAlsaCaptureDevices(string arecordList)
    {
        if (string.IsNullOrWhiteSpace(arecordList))
        {
            return Array.Empty<LiveAudioDevice>();
        }

        var devices = new List<LiveAudioDevice>();
        foreach (string rawLine in arecordList.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            Match match = AlsaCaptureDeviceRegex.Match(line);
            if (!match.Success ||
                !int.TryParse(match.Groups["card"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int card) ||
                !int.TryParse(match.Groups["device"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int device))
            {
                continue;
            }

            string cardName = match.Groups["cardName"].Value.Trim();
            string deviceName = match.Groups["deviceName"].Value.Trim();
            string displayName = "ALSA hw:" + card.ToString(CultureInfo.InvariantCulture) +
                "," + device.ToString(CultureInfo.InvariantCulture) +
                " " + cardName;
            if (deviceName.Length > 0 && !displayName.Contains(deviceName, StringComparison.Ordinal))
            {
                displayName += " - " + deviceName;
            }

            devices.Add(new LiveAudioDevice(EncodeAlsaDeviceNumber(card, device), displayName));
        }

        return devices;
    }

    internal static IReadOnlyDictionary<int, int> BuildPipeWireAlsaRateProbeMap(
        IReadOnlyList<LiveAudioDevice> pipeWireSources,
        IReadOnlyList<LiveAudioDevice> alsaDevices)
    {
        return BuildPipeWireAlsaRateProbeMap(pipeWireSources, alsaDevices, inspectPipeWireSource: null);
    }

    internal static IReadOnlyDictionary<int, int> BuildPipeWireAlsaRateProbeMap(
        IReadOnlyList<LiveAudioDevice> pipeWireSources,
        IReadOnlyList<LiveAudioDevice> alsaDevices,
        Func<int, string>? inspectPipeWireSource)
    {
        var map = new Dictionary<int, int>();
        foreach (LiveAudioDevice source in pipeWireSources)
        {
            if (TryResolveInspectedAlsaRateProbeDevice(source.Number, alsaDevices, inspectPipeWireSource, out int inspectedDeviceNumber))
            {
                map[source.Number] = inspectedDeviceNumber;
                continue;
            }

            int matchedDeviceNumber = 0;
            bool matched = false;
            bool ambiguous = false;
            foreach (LiveAudioDevice alsaDevice in alsaDevices)
            {
                if (!TryDecodeAlsaDeviceNumber(alsaDevice.Number, out _, out _) ||
                    !AudioDeviceNamesLikelyMatch(source.Name, alsaDevice.Name))
                {
                    continue;
                }

                if (matched)
                {
                    ambiguous = true;
                    break;
                }

                matched = true;
                matchedDeviceNumber = alsaDevice.Number;
            }

            if (matched && !ambiguous)
            {
                map[source.Number] = matchedDeviceNumber;
            }
        }

        return map;
    }

    private static IReadOnlySet<int> BuildPipeWireSourceNumberSet(IReadOnlyList<LiveAudioDevice> pipeWireSources)
    {
        var numbers = new HashSet<int>();
        foreach (LiveAudioDevice source in pipeWireSources)
        {
            numbers.Add(source.Number);
        }

        return numbers;
    }

    internal static int ResolveRateProbeDeviceNumber(
        int deviceNumber,
        IReadOnlyDictionary<int, int> pipeWireAlsaRateProbeDevices)
    {
        return pipeWireAlsaRateProbeDevices.TryGetValue(deviceNumber, out int alsaDeviceNumber)
            ? alsaDeviceNumber
            : deviceNumber;
    }

    internal static bool TryResolveRateProbeDeviceNumber(
        int deviceNumber,
        IReadOnlyDictionary<int, int> pipeWireAlsaRateProbeDevices,
        IReadOnlySet<int> pipeWireSourceNumbers,
        out int probeDeviceNumber)
    {
        if (pipeWireAlsaRateProbeDevices.TryGetValue(deviceNumber, out int alsaDeviceNumber))
        {
            probeDeviceNumber = alsaDeviceNumber;
            return true;
        }

        if (pipeWireSourceNumbers.Contains(deviceNumber))
        {
            probeDeviceNumber = 0;
            return false;
        }

        probeDeviceNumber = deviceNumber;
        return true;
    }

    /// <summary>
    /// Live-capture candidate sample rates for the rate menu. Intentionally returns the
    /// full standard set regardless of <paramref name="deviceNumber"/>: an earlier
    /// revision probed ALSA/PipeWire hardware to hide unverified rates, but that also hid
    /// rates that actually worked, so commit 3075ace ("keep standard live rates
    /// selectable") reverted to offering all standard rates and validating the chosen
    /// rate at capture start instead. The internal probe overload below and the
    /// hw-params helpers are retained (and unit tested) for a possible future opt-in, but
    /// are deliberately NOT wired into this production entry point.
    /// </summary>
    public static IReadOnlyList<int> GetCandidateSampleRates(int deviceNumber)
    {
        _ = deviceNumber;
        return AudioSampleRates.Standard;
    }

    internal static IReadOnlyList<int> GetCandidateSampleRates(Func<int, bool> supportsSampleRate)
    {
        IReadOnlyList<int> standardRates = AudioSampleRates.Standard;
        var supportedRates = new List<int>(standardRates.Count);
        foreach (int rate in standardRates)
        {
            if (supportsSampleRate(rate))
            {
                supportedRates.Add(rate);
            }
        }

        return supportedRates;
    }

    internal static bool IsConservativePipeWireFallbackRate(int sampleRate)
    {
        return sampleRate == AudioSampleRates.Standard[0];
    }

    public void Start(int deviceNumber, int sampleRate, float volume, int bufferMilliseconds)
    {
        _volume = volume;
        _paused = false;
        if (_process != null)
        {
            if (!TryStop(TimeSpan.FromMilliseconds(ReplacementStopTimeoutMs)))
            {
                throw new InvalidOperationException("Existing audio capture process did not stop.");
            }
        }

        if (TryDecodeAlsaDeviceNumber(deviceNumber, out int card, out int device))
        {
            StartAlsaCapture(card, device, sampleRate, bufferMilliseconds);
            return;
        }

        StartPipeWireCapture(deviceNumber, sampleRate, bufferMilliseconds);
    }

    private static bool CanOpenDeviceAtSampleRate(int deviceNumber, int sampleRate)
    {
        try
        {
            if (!TryResolveRateProbeDeviceNumber(deviceNumber, out int probeDeviceNumber))
            {
                if (!IsConservativePipeWireFallbackRate(sampleRate))
                {
                    return false;
                }

                return ProbeStartInfoForSampleRate(
                    BuildPipeWireProbeStartInfo(deviceNumber, sampleRate),
                    startupProbeTimeoutMs: StartupFailureProbeTimeoutMs,
                    cleanupTimeoutMs: ReplacementStopTimeoutMs);
            }
            bool isAlsaProbe = TryDecodeAlsaDeviceNumber(probeDeviceNumber, out int card, out int device);
            ProcessStartInfo startInfo = isAlsaProbe
                ? BuildAlsaProbeStartInfo(card, device, sampleRate)
                : BuildPipeWireProbeStartInfo(deviceNumber, sampleRate);
            return ProbeStartInfoForSampleRate(
                startInfo,
                startupProbeTimeoutMs: StartupFailureProbeTimeoutMs,
                cleanupTimeoutMs: ReplacementStopTimeoutMs,
                requestedSampleRate: isAlsaProbe ? sampleRate : null);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveRateProbeDeviceNumber(int deviceNumber, out int probeDeviceNumber)
    {
        IReadOnlyDictionary<int, int> map;
        IReadOnlySet<int> pipeWireSourceNumbers;
        lock (PipeWireRateProbeLock)
        {
            map = _pipeWireAlsaRateProbeDevices;
            pipeWireSourceNumbers = _pipeWireRateProbeSourceNumbers;
        }

        return TryResolveRateProbeDeviceNumber(deviceNumber, map, pipeWireSourceNumbers, out probeDeviceNumber);
    }

    private static void SetPipeWireAlsaRateProbeDevices(
        IReadOnlyDictionary<int, int> map,
        IReadOnlySet<int> pipeWireSourceNumbers)
    {
        lock (PipeWireRateProbeLock)
        {
            _pipeWireAlsaRateProbeDevices = map;
            _pipeWireRateProbeSourceNumbers = pipeWireSourceNumbers;
        }
    }

    private static string InspectPipeWireSource(int sourceNumber)
    {
        return RunCommand(
            "wpctl",
            "inspect",
            sourceNumber.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryResolveInspectedAlsaRateProbeDevice(
        int sourceNumber,
        IReadOnlyList<LiveAudioDevice> alsaDevices,
        Func<int, string>? inspectPipeWireSource,
        out int deviceNumber)
    {
        deviceNumber = 0;
        if (inspectPipeWireSource == null)
        {
            return false;
        }

        string inspectOutput = inspectPipeWireSource(sourceNumber);
        if (!TryParsePipeWireAlsaHardwareAddress(inspectOutput, out int card, out int device))
        {
            return false;
        }

        int encodedDeviceNumber = EncodeAlsaDeviceNumber(card, device);
        foreach (LiveAudioDevice alsaDevice in alsaDevices)
        {
            if (alsaDevice.Number == encodedDeviceNumber)
            {
                deviceNumber = encodedDeviceNumber;
                return true;
            }
        }

        return false;
    }

    internal static bool TryParsePipeWireAlsaHardwareAddress(string inspectOutput, out int card, out int device)
    {
        card = 0;
        device = 0;
        int? parsedCard = ReadPipeWireIntProperty(
            inspectOutput,
            "api.alsa.pcm.card",
            "api.alsa.card",
            "alsa.card");
        int? parsedDevice = ReadPipeWireIntProperty(
            inspectOutput,
            "api.alsa.pcm.device",
            "api.alsa.device",
            "alsa.device");

        if (TryReadPipeWireHwPath(inspectOutput, out int pathCard, out int pathDevice))
        {
            parsedCard ??= pathCard;
            parsedDevice ??= pathDevice;
        }

        if (parsedCard is not { } resolvedCard || parsedDevice is not { } resolvedDevice)
        {
            return false;
        }

        card = resolvedCard;
        device = resolvedDevice;
        return card >= 0 && device >= 0;
    }

    private static int? ReadPipeWireIntProperty(string inspectOutput, params string[] propertyNames)
    {
        string? value = ReadPipeWireProperty(inspectOutput, propertyNames);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static bool TryReadPipeWireHwPath(string inspectOutput, out int card, out int device)
    {
        card = 0;
        device = 0;
        string? path = ReadPipeWireProperty(inspectOutput, "api.alsa.path");
        if (!string.IsNullOrWhiteSpace(path) && TryParsePipeWireHwPath(path, out card, out device))
        {
            return true;
        }

        string? objectPath = ReadPipeWireProperty(inspectOutput, "object.path", "node.name");
        return !string.IsNullOrWhiteSpace(objectPath) &&
            TryParsePipeWireObjectPath(objectPath, out card, out device);
    }

    private static bool TryParsePipeWireHwPath(string path, out int card, out int device)
    {
        card = 0;
        device = 0;
        if (!path.StartsWith("hw:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> rest = path.AsSpan("hw:".Length);
        int comma = rest.IndexOf(',');
        ReadOnlySpan<char> cardText = comma >= 0 ? rest[..comma] : rest;
        ReadOnlySpan<char> deviceText = comma >= 0 ? rest[(comma + 1)..] : "0".AsSpan();

        return int.TryParse(cardText, NumberStyles.Integer, CultureInfo.InvariantCulture, out card) &&
            int.TryParse(deviceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out device);
    }

    private static bool TryParsePipeWireObjectPath(string path, out int card, out int device)
    {
        card = 0;
        device = 0;
        string[] parts = path.Split(':');
        if (parts.Length < 5 ||
            !parts[0].Equals("alsa", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("pcm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out card) &&
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out device);
    }

    private static string? ReadPipeWireProperty(string inspectOutput, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(inspectOutput))
        {
            return null;
        }

        foreach (string rawLine in inspectOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            Match match = PipeWirePropertyRegex.Match(rawLine.Trim());
            if (!match.Success)
            {
                continue;
            }

            string propertyName = match.Groups["name"].Value;
            foreach (string expectedName in propertyNames)
            {
                if (!propertyName.Equals(expectedName, StringComparison.Ordinal))
                {
                    continue;
                }

                Group quoted = match.Groups["quoted"];
                return quoted.Success ? quoted.Value : match.Groups["bare"].Value;
            }
        }

        return null;
    }

    private static bool AudioDeviceNamesLikelyMatch(string pipeWireName, string alsaName)
    {
        HashSet<string> pipeWireTokens = SignificantAudioNameTokens(pipeWireName);
        HashSet<string> alsaTokens = SignificantAudioNameTokens(alsaName);
        if (pipeWireTokens.Count == 0 || alsaTokens.Count == 0)
        {
            return false;
        }

        return IsSubset(pipeWireTokens, alsaTokens) || IsSubset(alsaTokens, pipeWireTokens);
    }

    private static bool IsSubset(HashSet<string> left, HashSet<string> right)
    {
        foreach (string token in left)
        {
            if (!right.Contains(token))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> SignificantAudioNameTokens(string name)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Span<char> buffer = stackalloc char[64];
        int length = 0;
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (length < buffer.Length)
                {
                    buffer[length++] = char.ToLowerInvariant(ch);
                }

                continue;
            }

            AddSignificantAudioNameToken(tokens, buffer.Slice(0, length));
            length = 0;
        }

        AddSignificantAudioNameToken(tokens, buffer.Slice(0, length));
        return tokens;
    }

    private static void AddSignificantAudioNameToken(HashSet<string> tokens, ReadOnlySpan<char> token)
    {
        if (token.Length == 0)
        {
            return;
        }

        bool allDigits = true;
        for (int i = 0; i < token.Length; i++)
        {
            if (!char.IsDigit(token[i]))
            {
                allDigits = false;
                break;
            }
        }

        if (allDigits)
        {
            return;
        }

        string text = token.ToString();
        if (!GenericAudioNameTokens.Contains(text))
        {
            tokens.Add(text);
        }
    }

    internal static bool ProbeStartInfoForSampleRate(
        ProcessStartInfo startInfo,
        int startupProbeTimeoutMs,
        int cleanupTimeoutMs,
        int? requestedSampleRate = null)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(startupProbeTimeoutMs))
            {
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                string combinedOutput = output + "\n" + error;
                return process.ExitCode == 0 &&
                    !ContainsInaccurateRateWarning(combinedOutput) &&
                    DumpedHwParamsAllowRequestedRate(combinedOutput, requestedSampleRate);
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            _ = outputTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
            _ = errorTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
            return process.WaitForExit(cleanupTimeoutMs);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsInaccurateRateWarning(string text)
    {
        return text.Contains("rate is not accurate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DumpedHwParamsAllowRequestedRate(string text, int? requestedSampleRate)
    {
        if (requestedSampleRate == null)
        {
            return true;
        }

        Match match = HwParamsRateRegex.Match(text);
        if (!match.Success)
        {
            return true;
        }

        string rateSpec = match.Groups["rate"].Value;
        if (rateSpec.Contains("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        MatchCollection numbers = IntegerRegex.Matches(rateSpec);
        if (numbers.Count == 1 &&
            int.TryParse(numbers[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exactRate))
        {
            return requestedSampleRate.Value == exactRate;
        }

        if (numbers.Count >= 2 &&
            int.TryParse(numbers[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minRate) &&
            int.TryParse(numbers[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxRate))
        {
            return requestedSampleRate.Value >= minRate && requestedSampleRate.Value <= maxRate;
        }

        return true;
    }

    private void StartPipeWireCapture(int deviceNumber, int sampleRate, int bufferMilliseconds)
    {
        StartProcess(BuildPipeWireStartInfo(deviceNumber, sampleRate, bufferMilliseconds), PcmSampleFormat.Float32LittleEndian, "pw-record");
    }

    private void StartAlsaCapture(int card, int device, int sampleRate, int bufferMilliseconds)
    {
        StartProcess(BuildAlsaStartInfo(card, device, sampleRate, bufferMilliseconds), PcmSampleFormat.Int16LittleEndian, "arecord");
    }

    internal static ProcessStartInfo BuildPipeWireProbeStartInfo(int deviceNumber, int sampleRate)
    {
        ProcessStartInfo startInfo = BuildPipeWireStartInfo(deviceNumber, sampleRate);
        startInfo.ArgumentList[^1] = "/dev/null";
        return startInfo;
    }

    internal static ProcessStartInfo BuildPipeWireStartInfo(int deviceNumber, int sampleRate, int bufferMilliseconds = LiveAudioDefaults.BufferMilliseconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pw-record",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--media-category");
        startInfo.ArgumentList.Add("Capture");
        startInfo.ArgumentList.Add("--rate");
        startInfo.ArgumentList.Add(sampleRate.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--channels");
        startInfo.ArgumentList.Add(Channels.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("f32");
        startInfo.ArgumentList.Add("--raw");
        if (deviceNumber > 0)
        {
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(deviceNumber.ToString(CultureInfo.InvariantCulture));
        }
        // A non-default buffer requests an explicit capture latency; the default omits the
        // flag so pw-record keeps its native buffering (current behaviour). The trailing
        // "-" stays last so the probe builder can swap it for /dev/null.
        if (bufferMilliseconds != LiveAudioDefaults.BufferMilliseconds)
        {
            startInfo.ArgumentList.Add("--latency");
            startInfo.ArgumentList.Add(bufferMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
        }
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    internal static ProcessStartInfo BuildAlsaProbeStartInfo(int card, int device, int sampleRate)
    {
        ProcessStartInfo startInfo = BuildAlsaStartInfo(card, device, sampleRate);
        int targetIndex = startInfo.ArgumentList.Count - 1;
        startInfo.ArgumentList.Insert(targetIndex++, "--dump-hw-params");
        startInfo.ArgumentList.Insert(targetIndex++, "--samples");
        startInfo.ArgumentList.Insert(targetIndex, "1");
        startInfo.ArgumentList[^1] = "/dev/null";
        return startInfo;
    }

    internal static ProcessStartInfo BuildAlsaStartInfo(int card, int device, int sampleRate, int bufferMilliseconds = LiveAudioDefaults.BufferMilliseconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "arecord",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-D");
        startInfo.ArgumentList.Add(
            "hw:" + card.ToString(CultureInfo.InvariantCulture) + "," + device.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("raw");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("S16_LE");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(Channels.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(sampleRate.ToString(CultureInfo.InvariantCulture));
        // arecord takes buffer time in microseconds; omit at the default so ALSA keeps its
        // negotiated buffer (current behaviour). The trailing "-" stays last for the probe swap.
        if (bufferMilliseconds != LiveAudioDefaults.BufferMilliseconds)
        {
            startInfo.ArgumentList.Add("--buffer-time");
            startInfo.ArgumentList.Add((bufferMilliseconds * 1000).ToString(CultureInfo.InvariantCulture));
        }
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    /// <summary>Test hook: drives the real StartProcess path with an arbitrary child process.</summary>
    internal void StartCaptureProcessForTests(ProcessStartInfo startInfo, int startupProbeTimeoutMs = StartupFailureProbeTimeoutMs)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        StartProcess(startInfo, PcmSampleFormat.Int16LittleEndian, startInfo.FileName, startupProbeTimeoutMs);
    }

    internal void InstallWaitForExitForTests(Func<Process, TimeSpan, bool> waitForExit)
    {
        _waitForExitOverride = waitForExit;
    }

    private void StartProcess(
        ProcessStartInfo startInfo,
        PcmSampleFormat sampleFormat,
        string processName,
        int startupProbeTimeoutMs = StartupFailureProbeTimeoutMs)
    {
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start " + processName + ".");

        _process = process;
        Interlocked.Exchange(ref _captureEndedFired, 0);
        // Suppress the reader's unexpected-death CaptureEnded until the startup
        // probe confirms the capture is live: if the process dies during the
        // probe, that failure is reported by the throw below, so CaptureEnded
        // must not also fire (which would report the same failure twice). Set
        // before the reader threads start so the end-of-stream check below sees
        // it without a race.
        _stopRequested = true;
        lock (_stderrLock)
        {
            _stderr.Clear();
        }

        _processErrorPrefix = processName;
        _stdoutThread = new Thread(() => ReadPcm(process, sampleFormat))
        {
            Name = processName + "AudioCaptureRead",
            IsBackground = true,
        };
        _stderrThread = new Thread(() => ReadStderr(process))
        {
            Name = processName + "AudioCaptureErr",
            IsBackground = true,
        };
        _stdoutThread.Start();
        _stderrThread.Start();

        if (process.WaitForExit(startupProbeTimeoutMs))
        {
            // Join the readers before touching _stderr so the stderr thread is done appending.
            _stdoutThread?.Join(TimeSpan.FromMilliseconds(250));
            _stderrThread?.Join(TimeSpan.FromMilliseconds(250));
            _stdoutThread = null;
            _stderrThread = null;
            string error;
            lock (_stderrLock)
            {
                error = _stderr.ToString().Trim();
            }

            process.Dispose();
            _process = null;
            throw new InvalidOperationException(
                error.Length == 0 ? processName + " exited before capture started." : processName + " exited: " + error);
        }

        // Startup probe passed: the capture is live, so from here an unexpected
        // stream end is a real capture death that must raise CaptureEnded.
        _stopRequested = false;

        // If the child died in the tiny window between the probe returning "still
        // running" and clearing _stopRequested above, the reader already reached
        // end-of-stream and suppressed CaptureEnded (it saw _stopRequested still
        // true). Re-check liveness and raise it here so a capture that dies right
        // after the probe is reported instead of leaving the UI stuck in Running.
        if (process.HasExited && ReferenceEquals(Volatile.Read(ref _process), process))
        {
            RaiseCaptureEndedOnce();
        }
    }

    public void SetVolume(float volume)
    {
        _volume = volume;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
    }

    public bool TryStop(TimeSpan timeout)
    {
        _stopRequested = true;
        _paused = false;
        Process? process = Interlocked.Exchange(ref _process, null);
        if (process == null)
        {
            return true;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            process.WaitForExit();
        }
        else if (!WaitForExit(process, timeout))
        {
            // Keep the process and reader threads so a later stop attempt can
            // re-wait and finish teardown instead of disposing a live process.
            if (Interlocked.CompareExchange(ref _process, process, null) != null)
            {
                process.Dispose();
            }

            return false;
        }

        _stdoutThread?.Join(TimeSpan.FromMilliseconds(250));
        _stderrThread?.Join(TimeSpan.FromMilliseconds(250));
        _stdoutThread = null;
        _stderrThread = null;
        process.Dispose();
        return true;
    }

    private bool WaitForExit(Process process, TimeSpan timeout)
    {
        Func<Process, TimeSpan, bool>? waitOverride = _waitForExitOverride;
        return waitOverride?.Invoke(process, timeout) ?? process.WaitForExit(timeout);
    }

    public void Dispose()
    {
        TryStop(Timeout.InfiniteTimeSpan);
    }

    // The sample format is bound at thread start: a reader that outlives the
    // bounded TryStop join must never decode with a format the NEXT capture
    // installed into the shared field.
    private void ReadPcm(Process process, PcmSampleFormat sampleFormat)
    {
        var pending = new byte[sizeof(float)];
        int pendingCount = 0;
        var readBuffer = new byte[8192];

        try
        {
            Stream stream = process.StandardOutput.BaseStream;
            int bytesPerSample = BytesPerSample(sampleFormat);
            while (true)
            {
                int read = stream.Read(readBuffer, 0, readBuffer.Length);
                if (read <= 0)
                {
                    break;
                }

                // An orphaned reader (its capture torn down or replaced) must
                // not ring-write stale samples into the next session's buffer.
                if (!ReferenceEquals(Volatile.Read(ref _process), process))
                {
                    return;
                }

                int offset = 0;
                if (pendingCount > 0)
                {
                    int bytesNeeded = bytesPerSample - pendingCount;
                    int bytesToCopy = Math.Min(bytesNeeded, read);
                    Array.Copy(readBuffer, 0, pending, pendingCount, bytesToCopy);
                    pendingCount += bytesToCopy;
                    offset += bytesToCopy;

                    if (pendingCount < bytesPerSample)
                    {
                        continue;
                    }

                    WriteSamples(pending.AsSpan(0, bytesPerSample), sampleFormat);
                    pendingCount = 0;
                }

                int remaining = read - offset;
                int usableBytes = remaining - (remaining % bytesPerSample);
                if (usableBytes > 0)
                {
                    WriteSamples(readBuffer.AsSpan(offset, usableBytes), sampleFormat);
                    offset += usableBytes;
                }

                int leftoverBytes = read - offset;
                if (leftoverBytes > 0)
                {
                    Array.Copy(readBuffer, offset, pending, 0, leftoverBytes);
                    pendingCount = leftoverBytes;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }

        // The capture stream ended. If no stop was requested and this is still the
        // active process (not a torn-down or replaced one), the capture died on us.
        if (!_stopRequested && ReferenceEquals(Volatile.Read(ref _process), process))
        {
            RaiseCaptureEndedOnce();
        }
    }

    private void RaiseCaptureEndedOnce()
    {
        if (Interlocked.Exchange(ref _captureEndedFired, 1) == 0)
        {
            CaptureEnded?.Invoke();
        }
    }

    private void ReadStderr(Process process)
    {
        try
        {
            while (!process.StandardError.EndOfStream)
            {
                string? line = process.StandardError.ReadLine();
                if (line == null)
                {
                    return;
                }

                lock (_stderrLock)
                {
                    if (_stderr.Length < 4096)
                    {
                        _stderr.AppendLine(line);
                    }
                }

                Console.Error.WriteLine(_processErrorPrefix + ": " + line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void WriteSamples(ReadOnlySpan<byte> bytes, PcmSampleFormat sampleFormat)
    {
        if (_paused)
        {
            return;
        }

        int bytesPerSample = BytesPerSample(sampleFormat);
        int sampleCount = bytes.Length / bytesPerSample;
        if (sampleCount <= 0)
        {
            DataReady?.Invoke();
            return;
        }

        float volume = _volume;
        Span<float> block = sampleCount <= 4096
            ? stackalloc float[sampleCount]
            : new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * bytesPerSample;
            if (sampleFormat == PcmSampleFormat.Float32LittleEndian)
            {
                float s = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(float)))) * volume;
                // A live float capture stream can deliver NaN/Inf samples. Folding
                // them to 0 here keeps a non-finite value from latching into the
                // recursive HPF/envelope state (which would silently and permanently
                // kill detection), mirroring the fold-to-safe guard PlaybackWorker
                // applies on the playback decode boundary. The S16 path below cannot
                // produce a non-finite value, so it is left unchanged.
                block[i] = float.IsFinite(s) ? s : 0f;
            }
            else
            {
                block[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, sizeof(short))) / 32768.0f * volume;
            }
        }

        _rawAudio.WriteSamples(block);
        UpdateStats((ulong)sampleCount);
        DataReady?.Invoke();
    }

    private void UpdateStats(ulong sampleCount)
    {
        if (!_timerStarted)
        {
            _timerStarted = true;
            _timer.Start();
        }

        ++_frameCount;
        _sampleCount += sampleCount;
        double currentTime = _timer.ElapsedMilliseconds / 1000.0;
        if (currentTime - _lastTime > 2)
        {
            double delta = currentTime - _lastTime;
            double fps = _frameCount / delta;
            double sps = _sampleCount / delta;
            // Original: SampleCount/FrameCount with both uint64_t -> integer division.
            double spf = _sampleCount / _frameCount;
            _rawAudio.SetStats(fps, spf, sps);
            _lastTime = currentTime;
            _frameCount = 0;
            _sampleCount = 0;
        }
    }

    private static string RunCommand(string fileName, params string[] arguments)
    {
        return RunCommand(fileName, CommandProbeTimeout, arguments);
    }

    internal static string FormatPipeWireVolumePercent(int volumePercent)
    {
        int clamped = Math.Clamp(volumePercent, 0, 100);
        return clamped.ToString(CultureInfo.InvariantCulture) + "%";
    }

    internal static int SetMatchingPipeWireSourceVolumes(
        IReadOnlyList<LiveAudioDevice> sources,
        IReadOnlyList<string> sourceNameFragments,
        string volumeArgument,
        Action<int, string> setVolume)
    {
        var configured = new HashSet<int>();
        foreach (LiveAudioDevice source in sources)
        {
            foreach (string fragment in sourceNameFragments)
            {
                if (string.IsNullOrWhiteSpace(fragment) ||
                    !source.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                    !configured.Add(source.Number))
                {
                    continue;
                }

                setVolume(source.Number, volumeArgument);
                break;
            }
        }

        return configured.Count;
    }

    internal static string RunCommand(string fileName, TimeSpan timeout, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            // Pin the child locale so the English tokens the parsers expect
            // ("Sources:", "Filters:"/"Streams:", ALSA "card .., device ..") are
            // emitted regardless of the Pi's configured locale; the app itself
            // runs invariant-culture, so a localized wpctl/arecord must not make
            // device enumeration return zero devices.
            process.StartInfo.EnvironmentVariables["LC_ALL"] = "C";
            process.StartInfo.EnvironmentVariables["LANG"] = "C";
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return "";
            }

            // One deadline for the whole probe (process exit AND pipe drain) so it is bounded
            // end-to-end to a single timeout window, not one window per stage.
            var elapsed = Stopwatch.StartNew();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                AbandonProbe(process, outputTask, errorTask);
                return "";
            }

            // WaitForExit(timeout) returns as soon as the process itself exits, but its
            // stdout/stderr pipes can outlive it when the child spawned a grandchild that
            // inherited the write handles; ReadToEndAsync would then never complete and
            // GetResult() would block with no bound. Drain within the budget REMAINING after
            // exit so the total stays inside one window; normal probes (pipes closed on exit)
            // complete this instantly.
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (!Task.WaitAll(new Task[] { outputTask, errorTask }, remaining))
            {
                AbandonProbe(process, outputTask, errorTask);
                return "";
            }

            string output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : "";
        }
        catch
        {
            return "";
        }
    }

    // Kills a probe that outran its budget (process still running, or its pipes still
    // open after exit) and observes the faulted stdout/stderr reads so they do not
    // surface as unobserved task exceptions on the finalizer. If the root process has
    // already exited but a reparented descendant kept the inherited pipe handles open,
    // Kill(entireProcessTree) cannot reach that descendant; the caller's `using` then
    // disposes the Process, closing our read ends so the observed reads fault promptly
    // and the UI stays unblocked. That rare descendant is left to exit on its own.
    private static void AbandonProbe(Process process, Task outputTask, Task errorTask)
    {
        TryKillProcessTree(process);
        _ = outputTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
        _ = errorTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Device probing is best-effort; callers fall back to no devices.
        }
    }

    private static int EncodeAlsaDeviceNumber(int card, int device)
    {
        return AlsaDeviceNumberBase + (card * AlsaDeviceNumberStride) + device;
    }

    internal static bool TryDecodeAlsaDeviceNumber(int deviceNumber, out int card, out int device)
    {
        if (deviceNumber < AlsaDeviceNumberBase)
        {
            card = 0;
            device = 0;
            return false;
        }

        int encoded = deviceNumber - AlsaDeviceNumberBase;
        card = encoded / AlsaDeviceNumberStride;
        device = encoded % AlsaDeviceNumberStride;
        return true;
    }

    private static int BytesPerSample(PcmSampleFormat sampleFormat)
    {
        return sampleFormat == PcmSampleFormat.Float32LittleEndian ? sizeof(float) : sizeof(short);
    }

    private enum PcmSampleFormat
    {
        Float32LittleEndian,
        Int16LittleEndian,
    }
}
