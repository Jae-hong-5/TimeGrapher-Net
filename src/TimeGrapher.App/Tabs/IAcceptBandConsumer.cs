namespace TimeGrapher.App.Tabs;

/// <summary>
/// Frame consumers whose plots draw or judge against the acceptable ("normal")
/// bands must refresh when the user edits the limits. GraphFrameRenderer fans
/// ApplyAcceptBands out to every consumer implementing this — mirroring
/// <see cref="IThemedFrameConsumer"/> — so a new banded tab participates by
/// implementing the interface instead of being special-cased by concrete type.
/// Unlike a run reset, this keeps the accumulated history and only re-reads the
/// band limits, so the change shows immediately even while playback is stopped.
/// </summary>
internal interface IAcceptBandConsumer
{
    void ApplyAcceptBands();
}
