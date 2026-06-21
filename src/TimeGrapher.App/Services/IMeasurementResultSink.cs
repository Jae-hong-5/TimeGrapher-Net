using System;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// The measurement-log sink the <see cref="MeasurementLogController"/> writes displayed frames
/// to. Implemented by <see cref="MeasurementResultLogger"/>; an interface so the controller can
/// be unit-tested with a fake sink that does not open a CSV file.
/// </summary>
internal interface IMeasurementResultSink : IDisposable
{
    void ObserveDisplayed(AnalysisFrame frame);
}
