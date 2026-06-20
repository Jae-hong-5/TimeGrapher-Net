namespace TimeGrapher.App.Tabs;

internal sealed class GraphViewResetCoordinator
{
    private readonly List<Action> _resetActions = new();

    public int Count => _resetActions.Count;

    public void Register(Action resetAction)
    {
        _resetActions.Add(resetAction);
    }

    public void ResetAll()
    {
        foreach (Action resetAction in _resetActions)
        {
            resetAction();
        }
    }
}
