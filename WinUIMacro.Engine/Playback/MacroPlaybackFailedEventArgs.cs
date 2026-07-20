// 封装宏回放失败时的宏定义和异常信息。
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine.Playback;

/// <summary>报告宏回放失败。</summary>
internal sealed class MacroPlaybackFailedEventArgs(MacroDefinition macro, Exception exception)
    : EventArgs
{
    /// <summary>获取回放失败的宏。</summary>
    public MacroDefinition Macro { get; } = macro;

    /// <summary>获取回放异常。</summary>
    public Exception Exception { get; } = exception;
}
