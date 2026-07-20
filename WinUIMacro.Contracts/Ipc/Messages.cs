// 定义引擎与 UI 之间共享的 IPC 消息、协议版本和数据契约。
using System.Text.Json;

namespace WinUIMacro.Contracts.Ipc;

/// <summary>封装当前用户会话使用的互斥体和命名管道名称。</summary>
public sealed record IpcEndpointNames(string Mutex, string UiPipe, string ControlPipe)
{
    /// <summary>根据用户 SID 和 Windows 会话 ID 生成隔离的 IPC 名称。</summary>
    public static IpcEndpointNames Create(string userSid, int sessionId)
    {
        var suffix = $"{userSid}.{sessionId}";
        return new IpcEndpointNames(
            $"Local\\WinUIMacro.Engine.{suffix}",
            $"WinUIMacro.Ui.{suffix}",
            $"WinUIMacro.Control.{suffix}"
        );
    }
}

/// <summary>集中定义 IPC 协议版本、消息限制和超时。</summary>
public static class Protocol
{
    /// <summary>获取当前 IPC 协议版本。</summary>
    public const int Version = 1;

    /// <summary>获取向 UI 传递引擎管道名称的命令行参数。</summary>
    public const string EnginePipeArgument = "--engine-pipe";

    /// <summary>获取向 UI 传递注册管道名称的命令行参数。</summary>
    public const string UiRegistrationPipeArgument = "--ui-registration-pipe";

    /// <summary>获取向 UI 传递一次性注册令牌的命令行参数。</summary>
    public const string UiRegistrationTokenArgument = "--ui-registration-token";

    /// <summary>获取单条 IPC 消息允许的最大字节数。</summary>
    internal const int MaximumMessageLength = 16 * 1024 * 1024;

    /// <summary>获取单个连接允许排队的写入数量。</summary>
    internal const int WriteQueueCapacity = 64;

    /// <summary>获取普通 IPC 请求的默认超时时间。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>获取 UI 建立引擎连接的超时时间。</summary>
    public static readonly TimeSpan UiConnectionTimeout = TimeSpan.FromSeconds(5);
}

/// <summary>区分请求、响应和异步事件三类 IPC 消息。</summary>
public enum MessageKind
{
    /// <summary>需要接收端处理并返回响应的请求。</summary>
    Request,

    /// <summary>与既有请求关联的响应。</summary>
    Response,

    /// <summary>不要求响应的异步通知。</summary>
    Event,
}

/// <summary>标识 IPC 请求、响应和事件承载的业务消息。</summary>
public enum IpcMessageType
{
    /// <summary>UI 使用一次性令牌向 Host 注册。</summary>
    UiRegistration,

    /// <summary>UI 与引擎建立连接后的协议握手。</summary>
    Hello,

    /// <summary>读取宏列表、绑定和加载错误。</summary>
    GetWorkspace,

    /// <summary>按 UUID 读取完整宏详情。</summary>
    GetMacro,

    /// <summary>保存新建或已编辑的宏。</summary>
    SaveMacro,

    /// <summary>删除指定宏及其绑定。</summary>
    DeleteMacro,

    /// <summary>设置或取消触发键绑定。</summary>
    SetBinding,

    /// <summary>启动宏录制会话。</summary>
    StartRecording,

    /// <summary>更新录制停止按钮的屏幕边界。</summary>
    UpdateRecordingBounds,

    /// <summary>停止当前录制会话。</summary>
    StopRecording,

    /// <summary>通知 UI 已录制一批新的宏节点。</summary>
    NodeRecordedBatch,

    /// <summary>通知 UI 录制状态已经变化。</summary>
    RecordingStateChanged,

    /// <summary>通知 UI 宏回放失败。</summary>
    PlaybackFailed,

    /// <summary>通知 UI 引擎处理输入或命令失败。</summary>
    ProcessingFailed,

    /// <summary>请求 UI 激活主窗口。</summary>
    ActivateUi,

    /// <summary>请求 UI 完成关闭前准备。</summary>
    PrepareUiShutdown,

    /// <summary>控制端请求 Host 打开 UI 或返回状态。</summary>
    Control,
}

/// <summary>向 Host 证明 UI 持有本次 Shell 启动的一次性注册令牌。</summary>
public sealed record UiRegistrationRequest(string Token);

/// <summary>返回 Host 是否已登记当前 UI 进程。</summary>
public sealed record UiRegistrationResponse(bool Accepted);

/// <summary>IPC 传输层使用的统一消息封装。</summary>
public sealed record MessageEnvelope(
    int ProtocolVersion,
    MessageKind Kind,
    IpcMessageType MessageType,
    Guid? RequestId,
    JsonElement Payload,
    string? Error = null
);

/// <summary>返回 UI 握手是否被当前引擎会话接受。</summary>
public sealed record HelloResponse(bool Accepted);

/// <summary>向 UI 提供宏、绑定、加载错误和宏目录的快照。</summary>
public sealed record WorkspaceSnapshot(
    IReadOnlyList<MacroSummary> Macros,
    IReadOnlyList<MacroTriggerBinding> Bindings,
    IReadOnlyList<string> Errors,
    string MacroDirectory
);

/// <summary>请求加载指定 UUID 的完整宏详情。</summary>
public sealed record GetMacroRequest(Guid MacroId);

/// <summary>请求保存一个宏。</summary>
public sealed record SaveMacroRequest(MacroDefinition Macro);

/// <summary>返回已保存宏以及运行时更新错误。</summary>
public sealed record SaveMacroResponse(MacroDefinition Macro, string? RuntimeError);

/// <summary>请求删除指定 UUID 的宏。</summary>
public sealed record DeleteMacroRequest(Guid MacroId);

/// <summary>请求设置或取消一个触发键绑定。</summary>
public sealed record SetBindingRequest(string Trigger, Guid? MacroId);

/// <summary>返回工作区变更后的运行时配置错误。</summary>
public sealed record RuntimeUpdateResponse(string? RuntimeError);

/// <summary>请求开始录制指定宏，并提供停止按钮屏幕边界。</summary>
public sealed record StartRecordingRequest(Guid MacroId, DesktopRectangle StopBounds);

/// <summary>返回新建录制会话的 UUID。</summary>
public sealed record StartRecordingResponse(Guid RecordingSessionId);

/// <summary>更新录制会话使用的停止按钮边界。</summary>
public sealed record UpdateRecordingBoundsRequest(
    Guid RecordingSessionId,
    DesktopRectangle StopBounds
);

/// <summary>请求停止指定或当前录制会话。</summary>
public sealed record StopRecordingRequest(Guid? RecordingSessionId);

/// <summary>表示录制会话生成的一个宏节点。</summary>
public sealed record NodeRecordedEvent(Guid RecordingSessionId, Guid MacroId, MacroNode Node);

/// <summary>通知 UI 录制会话生成了一批宏节点。</summary>
public sealed record NodeRecordedBatchEvent(IReadOnlyList<NodeRecordedEvent> Nodes);

/// <summary>通知 UI 录制状态发生变化。</summary>
public sealed record RecordingStateChangedEvent(
    Guid? RecordingSessionId,
    RecordingState State,
    bool SuppressRecordButtonClick = false
);

/// <summary>通知 UI 引擎处理过程中发生的可展示错误。</summary>
public sealed record RuntimeFailureEvent(string Message, string? MacroName = null);

/// <summary>返回 UI 是否已接受窗口激活请求。</summary>
public sealed record ActivateUiResponse(bool Activated);

/// <summary>返回 UI 是否已经完成关闭前准备。</summary>
public sealed record PrepareUiShutdownResponse(bool CanShutdown);

/// <summary>返回控制端查询到的引擎提权状态。</summary>
public sealed record ControlResponse(bool EngineIsElevated);

/// <summary>表示没有业务负载的请求、响应或事件。</summary>
public readonly record struct Unit;
