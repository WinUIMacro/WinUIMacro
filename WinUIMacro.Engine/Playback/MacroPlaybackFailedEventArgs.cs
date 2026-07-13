using WinUIMacro.Engine.Models;

namespace WinUIMacro.Engine.Playback;

/// <summary>Reports a macro playback failure.</summary>
public sealed class MacroPlaybackFailedEventArgs(MacroDefinition macro, Exception exception)
    : EventArgs
{
    /// <summary>Gets the macro that failed.</summary>
    public MacroDefinition Macro { get; } = macro;

    /// <summary>Gets the playback exception.</summary>
    public Exception Exception { get; } = exception;
}
