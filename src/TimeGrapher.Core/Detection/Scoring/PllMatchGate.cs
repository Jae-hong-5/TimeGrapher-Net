namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// Classical reference implementation of <see cref="IBeatEventGate"/>: drop
/// A (onset) events that failed the sync PLL's phase match while a lock was
/// held. Previously every detector event reached the metrics regardless of
/// the PLL verdict, so a single accepted noise impulse could flip tic/toc
/// parity, inject ~half-beat spikes into the displayed beat error, and pair
/// an orphaned C into a bogus amplitude. The veto is applied to A events
/// only: the PLL C-phase tolerance is seeded from the A-only beat history and
/// can latch on a low-amplitude watch whose A-to-C span exceeds it, which
/// would wrongly veto every valid C event; an A rejected here already vetoes
/// its companion C through the host's pair veto, so gating C independently is
/// both redundant and harmful. Zero window, zero latency, stateless.
/// </summary>
public sealed class PllMatchGate : IBeatEventGate
{
    public string Name => "pll";
    public double WindowPreMs => 0.0;
    public double WindowPostMs => 0.0;

    public bool Accept(ReadOnlySpan<float> envelopeWindow, int eventOffsetInWindow,
                       double sampleRate, in BeatCandidate candidate)
        => candidate.Event.Type != TgEventType.A || candidate.PllMatched;

    public void Reset()
    {
    }
}
