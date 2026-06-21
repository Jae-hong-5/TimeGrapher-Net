using System;

using Avalonia.Threading;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

/// <summary>Posts onto Avalonia's UI thread; the view-side <see cref="IUiDispatcher"/> the
/// AudioDeviceController uses to marshal its off-thread rate probe back to the UI thread.</summary>
internal sealed class UiThreadDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
