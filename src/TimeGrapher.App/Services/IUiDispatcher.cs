using System;

namespace TimeGrapher.App.Services;

/// <summary>
/// Posts an action back onto the UI thread. A narrow seam over Avalonia's
/// Dispatcher.UIThread so the <see cref="AudioDeviceController"/> can marshal its off-thread
/// rate probe back to the UI thread without depending on Avalonia, and can be tested with a
/// synchronous fake.
/// </summary>
internal interface IUiDispatcher
{
    void Post(Action action);
}
