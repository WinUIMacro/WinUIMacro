// 定义宏、录制节点、输入操作和运行状态等跨进程共享模型。
namespace WinUIMacro.Contracts;

/// <summary>表示一个可编辑、可持久化并可回放的宏。</summary>
public sealed record MacroDefinition(
    Guid Id,
    string Name,
    MacroPlaybackMode Mode,
    IReadOnlyList<MacroNode> Nodes,
    DateTimeOffset LastEditedAt = default
);

/// <summary>表示工作区列表所需的不含节点的宏摘要。</summary>
public sealed record MacroSummary(Guid Id, string Name, DateTimeOffset LastEditedAt);

/// <summary>定义宏被触发后的回放方式。</summary>
public enum MacroPlaybackMode
{
    /// <summary>每次触发后完整执行一次宏。</summary>
    Once,

    /// <summary>首次触发开始循环，再次触发停止循环。</summary>
    Toggle,

    /// <summary>按住触发键时循环执行，释放后停止。</summary>
    Hold,
}

/// <summary>表示宏序列中的一个输入、延时或备注节点。</summary>
public sealed record MacroNode(MacroNodeType Type, string Value);

/// <summary>定义宏节点的具体类型。</summary>
public enum MacroNodeType
{
    /// <summary>按下键盘按键。</summary>
    KeyDown,

    /// <summary>释放键盘按键。</summary>
    KeyUp,

    /// <summary>等待指定的毫秒数。</summary>
    Delay,

    /// <summary>按下鼠标按钮。</summary>
    MouseDown,

    /// <summary>释放鼠标按钮。</summary>
    MouseUp,

    /// <summary>滚动鼠标滚轮。</summary>
    MouseWheel,

    /// <summary>保存不参与回放的备注文本。</summary>
    Note,
}

/// <summary>表示一个触发值到宏 UUID 的绑定关系。</summary>
public sealed record MacroTriggerBinding(string Trigger, Guid MacroId);

/// <summary>表示物理像素虚拟桌面中的矩形区域。</summary>
public readonly record struct DesktopRectangle(double Left, double Top, double Right, double Bottom)
{
    /// <summary>确定指定坐标是否位于矩形边界内。</summary>
    /// <param name="x">待检测点的水平物理像素坐标。</param>
    /// <param name="y">待检测点的垂直物理像素坐标。</param>
    /// <returns>坐标位于矩形内或边界上时返回 <see langword="true"/>。</returns>
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>表示录制会话的生命周期状态。</summary>
public enum RecordingState
{
    /// <summary>当前没有录制会话。</summary>
    Idle,

    /// <summary>正在向引擎请求启动录制。</summary>
    Starting,

    /// <summary>引擎正在接收并记录输入。</summary>
    Recording,

    /// <summary>正在向引擎请求停止录制。</summary>
    Stopping,
}

/// <summary>表示尚未转换为 Win32 结构的领域输入操作。</summary>
public abstract record InputOperation;

/// <summary>表示键盘扫描码的按下或释放操作。</summary>
public sealed record KeyboardOperation(bool IsPress, ushort ScanCode, bool IsExtended)
    : InputOperation;

/// <summary>表示鼠标按钮的按下或释放操作。</summary>
public sealed record MouseButtonOperation(bool IsPress, MouseButton Button) : InputOperation;

/// <summary>表示鼠标滚轮滚动操作。</summary>
public sealed record MouseWheelOperation(MouseWheelDirection Direction) : InputOperation;

/// <summary>定义宏支持的鼠标按钮。</summary>
public enum MouseButton : byte
{
    /// <summary>鼠标左键。</summary>
    Left,

    /// <summary>鼠标右键。</summary>
    Right,

    /// <summary>鼠标中键。</summary>
    Middle,

    /// <summary>第一个鼠标侧键。</summary>
    X1,

    /// <summary>第二个鼠标侧键。</summary>
    X2,
}

/// <summary>定义鼠标滚轮滚动方向。</summary>
public enum MouseWheelDirection : sbyte
{
    /// <summary>向下滚动。</summary>
    Down = -1,

    /// <summary>向上滚动。</summary>
    Up = 1,
}
