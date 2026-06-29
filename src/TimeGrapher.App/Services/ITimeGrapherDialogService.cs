namespace TimeGrapher.App.Services;

internal interface IAiAnalysisDisplaySession
{
    Task ShowStatusAsync(string statusText);
    Task ShowResultAsync(AiAnalysisDisplay display);
    Task ShowFailureAsync(AiAnalysisFailureDisplay failure);

    /// <summary>
    /// Registers a callback invoked when the (non-modal) progress window closes, so the
    /// controller can cancel the in-flight request whose result the window would display.
    /// </summary>
    void OnClosed(Action callback);
}

internal interface ITimeGrapherDialogService
{
    Task<RecordSessionChoice> AskRecordSessionAsync();

    Task<string?> PickOpenWavAsync(string currentDirectory);
    Task<string?> PickOpenMeasurementLogAsync();


    Task<string?> PickSaveWavAsync();

    Task ShowErrorAsync(string title, string message);
    Task<AiAnalysisDialogResult?> AskAiAnalysisAsync(AiAnalysisDialogRequest request);

    Task<IAiAnalysisDisplaySession> ShowAiAnalysisProgressAsync(AiAnalysisProgressDisplay display);
}
