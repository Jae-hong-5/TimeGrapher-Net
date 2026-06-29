using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The platform-agnostic JsonAiCredentialStore probe contract (the OS-keystore
/// stores derive from it): the probe round-trips a sentinel credential, must
/// clean up the probe key afterwards, and must fail closed whenever a save fails
/// or the read does not round-trip. The NullAiCredentialStore (unsupported OS)
/// must also fail closed.
/// </summary>
public sealed class AiCredentialStoreTests
{
    private sealed class InMemoryJsonStore : JsonAiCredentialStore
    {
        private readonly Dictionary<string, string> _backing = new();

        public bool SaveSucceeds { get; set; } = true;
        public string? OverrideReadBlob { get; set; }

        public IReadOnlyDictionary<string, string> Backing => _backing;

        protected override string TargetName => "test-target";

        protected override string ProbeTargetName => "test-target-probe";

        protected override Task<bool> SaveJsonAsync(string targetName, string json, CancellationToken cancellationToken)
        {
            if (!SaveSucceeds)
            {
                return Task.FromResult(false);
            }

            _backing[targetName] = json;
            return Task.FromResult(true);
        }

        protected override Task<string?> ReadJsonAsync(string targetName, CancellationToken cancellationToken)
        {
            if (OverrideReadBlob != null)
            {
                return Task.FromResult<string?>(OverrideReadBlob);
            }

            return Task.FromResult(_backing.TryGetValue(targetName, out string? json) ? json : null);
        }

        protected override Task DeleteJsonAsync(string targetName, CancellationToken cancellationToken)
        {
            _backing.Remove(targetName);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProbeAsync_ReturnsTrue_AndCleansUpTheProbeKey_OnWorkingStore()
    {
        var store = new InMemoryJsonStore();

        Assert.True(await store.ProbeAsync(CancellationToken.None));

        // The probe must not leave its sentinel credential behind in the keystore.
        Assert.Empty(store.Backing);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFalse_WhenSaveFails()
    {
        var store = new InMemoryJsonStore { SaveSucceeds = false };

        Assert.False(await store.ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFalse_WhenReadDoesNotRoundTrip()
    {
        // A garbled / non-matching blob means the keystore did not faithfully store the
        // sentinel; the probe must report the store unusable rather than trust it.
        var store = new InMemoryJsonStore
        {
            OverrideReadBlob = JsonSerializer.Serialize(new AiBackendCredentials("someone-else", "wrong")),
        };

        Assert.False(await store.ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NullStore_FailsClosed()
    {
        IAiCredentialStore store = NullAiCredentialStore.Instance;

        Assert.False(await store.ProbeAsync(CancellationToken.None));
        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }
}
