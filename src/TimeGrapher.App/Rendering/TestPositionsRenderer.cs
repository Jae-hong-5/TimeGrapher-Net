using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Test Positions tab: one large button per NIHS 95-10 / ISO 3158 watch test
/// position. The active button carries the "active" style class (accent
/// background, white text; styles in App.axaml so the highlight re-themes via
/// DynamicResource). Clicking highlights immediately; while frames flow the
/// highlight follows the position Core actually stamps into the cumulative
/// metrics snapshot, so the display shows what the analysis is really tagging.
/// Re-renders are gated on the snapshot version, so coalesced or repeated
/// frames cost nothing.
/// </summary>
internal sealed class TestPositionsRenderer
{
    private const string ActiveClass = "active";

    private readonly Button[] _buttons; // indexed by WatchPosition ordinal
    private int _activeIndex = -1;
    private ulong _lastVersion;

    public TestPositionsRenderer(Button[] buttons, WatchPosition initialPosition)
    {
        _buttons = buttons;
        SetActivePosition(initialPosition);
    }

    public void SetActivePosition(WatchPosition position)
    {
        int index = (int)position;
        if (_activeIndex == index)
        {
            return;
        }

        _activeIndex = index;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == index)
            {
                if (!_buttons[i].Classes.Contains(ActiveClass))
                {
                    _buttons[i].Classes.Add(ActiveClass);
                }
            }
            else
            {
                _buttons[i].Classes.Remove(ActiveClass);
            }
        }
    }

    public void Reset()
    {
        // The highlight is selection state (the watch's physical orientation),
        // not run data; only the snapshot version gate restarts.
        _lastVersion = 0;
    }

    public void RenderFrame(AnalysisFrame frame)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history == null || history.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = history.Version;
        SetActivePosition(history.ActivePosition);
    }
}
