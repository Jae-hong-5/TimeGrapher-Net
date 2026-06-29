using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TimeGrapher.App.Services;

internal interface IAiCredentialStore
{
    Task<bool> ProbeAsync(CancellationToken cancellationToken);

    Task<AiBackendCredentials?> ReadAsync(CancellationToken cancellationToken);

    Task<bool> SaveAsync(AiBackendCredentials credentials, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(CancellationToken cancellationToken);
}

internal static class AiCredentialStore
{
    public static IAiCredentialStore CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsAiCredentialStore();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSecretToolAiCredentialStore();
        }

        return NullAiCredentialStore.Instance;
    }
}

internal sealed class NullAiCredentialStore : IAiCredentialStore
{
    public static readonly NullAiCredentialStore Instance = new();

    private NullAiCredentialStore()
    {
    }

    public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<AiBackendCredentials?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<AiBackendCredentials?>(null);

    public Task<bool> SaveAsync(AiBackendCredentials credentials, CancellationToken cancellationToken) => Task.FromResult(false);

    // Fail-closed: an unsupported OS has no credential store, so a removal cannot be
    // confirmed as having succeeded.
    public Task<bool> DeleteAsync(CancellationToken cancellationToken) => Task.FromResult(false);
}

internal abstract class JsonAiCredentialStore : IAiCredentialStore
{
    private const string ProbeUsername = "probe";
    private const string ProbePassword = "probe-password";

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        var probe = new AiBackendCredentials(ProbeUsername, ProbePassword);
        bool saved = await SaveJsonAsync(ProbeTargetName, Serialize(probe), cancellationToken);
        if (!saved)
        {
            return false;
        }

        string? read = await ReadJsonAsync(ProbeTargetName, cancellationToken);
        await DeleteJsonAsync(ProbeTargetName, cancellationToken);
        AiBackendCredentials? credentials = read == null ? null : Deserialize(read);
        return credentials == probe;
    }

    public async Task<AiBackendCredentials?> ReadAsync(CancellationToken cancellationToken)
    {
        string? json = await ReadJsonAsync(TargetName, cancellationToken);
        return json == null ? null : Deserialize(json);
    }

    public Task<bool> SaveAsync(AiBackendCredentials credentials, CancellationToken cancellationToken) =>
        SaveJsonAsync(TargetName, Serialize(credentials), cancellationToken);

    public Task<bool> DeleteAsync(CancellationToken cancellationToken) => DeleteJsonAsync(TargetName, cancellationToken);

    protected abstract string TargetName { get; }

    protected abstract string ProbeTargetName { get; }

    protected abstract Task<bool> SaveJsonAsync(string targetName, string json, CancellationToken cancellationToken);

    protected abstract Task<string?> ReadJsonAsync(string targetName, CancellationToken cancellationToken);

    protected abstract Task<bool> DeleteJsonAsync(string targetName, CancellationToken cancellationToken);

    private static string Serialize(AiBackendCredentials credentials) => JsonSerializer.Serialize(credentials);

    private static AiBackendCredentials? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AiBackendCredentials>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class WindowsAiCredentialStore : JsonAiCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    protected override string TargetName => "TimeGrapher/GeminiBackend";

    protected override string ProbeTargetName => "TimeGrapher/GeminiBackendProbe";

    protected override Task<bool> SaveJsonAsync(string targetName, string json, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "TimeGrapher"
            };

            return Task.FromResult(CredWrite(ref credential, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    protected override Task<string?> ReadJsonAsync(string targetName, CancellationToken cancellationToken)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out IntPtr credentialPtr))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return Task.FromResult<string?>(null);
            }

            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, credential.CredentialBlobSize);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    protected override Task<bool> DeleteJsonAsync(string targetName, CancellationToken cancellationToken)
    {
        return Task.FromResult(CredDelete(targetName, CredentialTypeGeneric, 0));
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }
}

internal sealed class LinuxSecretToolAiCredentialStore : JsonAiCredentialStore
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    protected override string TargetName => "gemini-backend";

    protected override string ProbeTargetName => "gemini-backend-probe";

    protected override async Task<bool> SaveJsonAsync(string targetName, string json, CancellationToken cancellationToken)
    {
        SecretToolResult result = await RunSecretToolAsync(
            new[] { "store", "--label", "TimeGrapher AI Login", "app", "timegrapher", "service", targetName },
            json,
            cancellationToken);
        return result.ExitCode == 0;
    }

    protected override async Task<string?> ReadJsonAsync(string targetName, CancellationToken cancellationToken)
    {
        SecretToolResult result = await RunSecretToolAsync(
            new[] { "lookup", "app", "timegrapher", "service", targetName },
            null,
            cancellationToken);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim()
            : null;
    }

    protected override async Task<bool> DeleteJsonAsync(string targetName, CancellationToken cancellationToken)
    {
        SecretToolResult result = await RunSecretToolAsync(
            new[] { "clear", "app", "timegrapher", "service", targetName },
            null,
            cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task<SecretToolResult> RunSecretToolAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = standardInput != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                return new SecretToolResult(-1, string.Empty);
            }

            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CommandTimeout);
            await process.WaitForExitAsync(timeoutCts.Token);
            string stdout = await stdoutTask;
            _ = await stderrTask;
            return new SecretToolResult(process.ExitCode, stdout);
        }
        catch (Win32Exception)
        {
            return new SecretToolResult(-1, string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            return new SecretToolResult(-1, string.Empty);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private readonly record struct SecretToolResult(int ExitCode, string StandardOutput);
}
