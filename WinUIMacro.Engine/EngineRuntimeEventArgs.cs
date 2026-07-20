// 定义引擎向宿主和 UI 报告的录制、节点及运行状态事件参数。
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine;

/// <summary>携带录制会话、目标宏和新生成节点。</summary>
internal sealed class EngineNodeRecordedEventArgs(
    Guid recordingSessionId,
    Guid macroId,
    MacroNode node
) : EventArgs
{
    public Guid RecordingSessionId { get; } = recordingSessionId;
    public Guid MacroId { get; } = macroId;
    public MacroNode Node { get; } = node;
}

/// <summary>携带录制会话状态及是否应抑制录制按钮点击。</summary>
internal sealed class EngineRecordingStateChangedEventArgs(
    Guid? recordingSessionId,
    RecordingState state,
    bool suppressRecordButtonClick = false
) : EventArgs
{
    public Guid? RecordingSessionId { get; } = recordingSessionId;
    public RecordingState State { get; } = state;
    public bool SuppressRecordButtonClick { get; } = suppressRecordButtonClick;
}
