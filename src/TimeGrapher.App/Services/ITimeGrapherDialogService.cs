namespace TimeGrapher.App.Services;

internal interface ITimeGrapherDialogService
{
    Task<RecordSessionChoice> AskRecordSessionAsync();

    Task<string?> PickOpenWavAsync(string currentDirectory);
    Task<string?> PickOpenMeasurementLogAsync();


    Task<string?> PickSaveWavAsync();

    Task ShowErrorAsync(string title, string message);
    Task<AiExplanationDialogResult?> AskAiExplanationAsync(AiExplanationDialogRequest request);

    Task ShowAiExplanationAsync(AiExplanationDisplay display);
}
