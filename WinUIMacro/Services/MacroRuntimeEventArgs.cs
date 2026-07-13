using WinUIMacro.Engine.Models;

namespace WinUIMacro.Services;

public enum RecordingState
{
    Idle,
    Starting,
    Recording,
    Stopping,
}

internal sealed class MacroNodeRecordedEventArgs(Guid macroId, MacroNode node) : EventArgs
{
    internal Guid MacroId { get; } = macroId;
    internal MacroNode Node { get; } = node;
}

internal sealed class RecordingStateChangedEventArgs(
    RecordingState state,
    bool suppressRecordButtonClick = false
) : EventArgs
{
    internal RecordingState State { get; } = state;
    internal bool SuppressRecordButtonClick { get; } = suppressRecordButtonClick;
}
