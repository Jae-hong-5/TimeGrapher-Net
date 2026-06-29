namespace TimeGrapher.App.Services;

internal interface IAiAnalysisDisplaySession
{
    Task ShowStatusAsync(string statusText);
    Task ShowResultAsync(AiAnalysisDisplay display);
    Task ShowFailureAsync(AiAnalysisFailureDisplay failure);
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
