namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// Classical reference implementation of <see cref="IBeatEventGate"/>: drop
/// events that failed the sync PLL's phase match while a lock was held.
/// Previously every detector event reached the metrics regardless of the
/// PLL verdict, so a single accepted noise impulse could flip tic/toc
/// parity, inject ~half-beat spikes into the displayed beat error, and pair
/// an orphaned C into a bogus amplitude. Zero window, zero latency,
/// stateless.
/// </summary>
public sealed class PllMatchGate : IBeatEventGate
{
    public string Name => "pll";
    public double WindowPreMs => 0.0;
    public double WindowPostMs => 0.0;

    public bool Accept(ReadOnlySpan<float> envelopeWindow, int eventOffsetInWindow,
                       double sampleRate, in BeatCandidate candidate)
        => candidate.PllMatched;

    public void Reset()
    {
    }
}
