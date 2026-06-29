namespace TimeGrapher.App.Services;

internal interface IAiExplanationDisplaySession
{
    Task ShowStatusAsync(string statusText);
    Task ShowResultAsync(AiExplanationDisplay display);
    Task ShowFailureAsync(AiExplanationFailureDisplay failure);
}

internal interface ITimeGrapherDialogService
{
    Task<RecordSessionChoice> AskRecordSessionAsync();

    Task<string?> PickOpenWavAsync(string currentDirectory);
    Task<string?> PickOpenMeasurementLogAsync();


    Task<string?> PickSaveWavAsync();

    Task ShowErrorAsync(string title, string message);
    Task<AiExplanationDialogResult?> AskAiExplanationAsync(AiExplanationDialogRequest request);

    Task<IAiExplanationDisplaySession> ShowAiExplanationProgressAsync(AiExplanationProgressDisplay display);
}
