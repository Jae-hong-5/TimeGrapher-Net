using System;

namespace TimeGrapher.App.Services;

/// <summary>
/// The selection-coordinator operations the <see cref="AudioDeviceController"/> drives while it
/// repopulates the device/rate combos: suppress the coordinator's reactions during a bulk update,
/// and set the selected device/rate index (which re-runs the coordinator's selection logic).
/// Implemented by <see cref="MainWindowSelectionCoordinator"/>; depending on the interface (and
/// attaching it after construction) breaks the coordinator&lt;-&gt;controller construction cycle
/// and lets the controller be tested with a fake gate.
/// </summary>
internal interface ISelectionEventGate
{
    IDisposable SuppressEvents();

    void SetSelectedInputDeviceIndex(int index, bool forceChanged = false);

    void SetSelectedSampleRateIndex(int index, bool forceChanged = false);
}
