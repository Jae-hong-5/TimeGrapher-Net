using Avalonia.Controls;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Live controls of the Positions tab's ACTIVE hero: the current position's live
/// readout (rate / amplitude / beat error / beats) and collection progress, plus
/// the sequence reference KPIs (mean rate / mean amplitude over the measured
/// positions, positions-measured count and total beats). Cross-position
/// consistency is judged on the Health tab, so the Positions tab is a pure
/// measurement/data view; the active position itself is shown by the rail and the
/// hero watch diagram, so no position-name label is carried here.
/// </summary>
internal sealed record PositionSequenceDashboardControls(
    TextBlock LiveRate,
    TextBlock LiveAmplitude,
    TextBlock LiveBeatError,
    TextBlock LiveBeats,
    Grid CollectionBar,
    Border CollectionFill,
    TextBlock CollectionLabel,
    TextBlock SeqRate,
    TextBlock SeqAmplitude,
    TextBlock PositionsMeasured,
    TextBlock TotalBeats);
