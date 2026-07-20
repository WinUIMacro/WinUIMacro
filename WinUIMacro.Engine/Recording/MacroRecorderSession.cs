// 将 Raw Input 操作和经过的时间转换为可持久化的宏节点。
using System.Diagnostics;
using System.Globalization;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Win32.Coordinates;

namespace WinUIMacro.Engine.Recording;

/// <summary>将接收的 Raw Input 操作和经过的时间转换为宏节点。</summary>
internal sealed class MacroRecorderSession
{
    private readonly Func<long> _getTimestamp;
    private bool _waitingForStopRelease;
    private long? _lastTimestamp;
    private DesktopRectangle _stopBounds;

    /// <summary>初始化录制会话。</summary>
    public MacroRecorderSession()
        : this(Stopwatch.GetTimestamp) { }

    internal MacroRecorderSession(Func<long> getTimestamp) => _getTimestamp = getTimestamp;

    /// <summary>获取输入当前是否被路由到录制会话。</summary>
    public bool IsRecording { get; private set; }

    /// <summary>获取正在录制的宏 UUID。</summary>
    public Guid TargetId { get; private set; }

    /// <summary>每生成一个延时节点或输入节点时引发。</summary>
    public event Action<MacroNode>? NodeRecorded;

    /// <summary>因录制按钮鼠标释放而停止录制时引发。</summary>
    public event Action<bool>? Stopped;

    /// <summary>开始向指定宏录制输入。</summary>
    public void Start(Guid targetId, DesktopRectangle stopBounds)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A recording target UUID is required.", nameof(targetId));
        if (IsRecording)
            throw new InvalidOperationException("Recording is already active.");
        TargetId = targetId;
        _stopBounds = stopBounds;
        _lastTimestamp = null;
        _waitingForStopRelease = false;
        IsRecording = true;
    }

    /// <summary>更新用于过滤停止点击的录制按钮边界。</summary>
    public void UpdateStopBounds(DesktopRectangle bounds) => _stopBounds = bounds;

    /// <summary>立即停止录制。</summary>
    public void Stop() => StopCore(stoppedByRecordButton: false);

    private void StopCore(bool stoppedByRecordButton)
    {
        if (!IsRecording)
            return;
        IsRecording = false;
        _waitingForStopRelease = false;
        _lastTimestamp = null;
        TargetId = Guid.Empty;
        Stopped?.Invoke(stoppedByRecordButton);
    }

    /// <summary>将一个输入操作交给活动的录制会话处理。</summary>
    /// <returns>录制会话拥有并消费该操作时返回 <see langword="true"/>。</returns>
    public bool ProcessInput(InputOperation operation, DesktopPoint? cursorPosition = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!IsRecording)
            return false;

        if (_waitingForStopRelease)
        {
            // 停止按钮的按下事件已被吞掉，只等待对应的释放事件完成停止。
            if (operation is MouseButtonOperation { Button: MouseButton.Left, IsPress: false })
                StopCore(stoppedByRecordButton: true);
            return true;
        }

        if (
            operation is MouseButtonOperation { Button: MouseButton.Left, IsPress: true }
            && cursorPosition is { } point
            && _stopBounds.Contains(point.X, point.Y)
        )
        {
            // 录制按钮点击用于结束录制，不应写入宏本身。
            _waitingForStopRelease = true;
            return true;
        }

        if (!MacroInputRegistry.TryCreateNode(operation, out var node))
            return true;
        var now = _getTimestamp();
        if (_lastTimestamp is { } previous)
        {
            // 用相邻输入的高精度时间戳生成毫秒延时节点。
            var milliseconds = (long)
                Math.Round(
                    (now - previous) * 1000d / Stopwatch.Frequency,
                    MidpointRounding.AwayFromZero
                );
            if (milliseconds > 0)
                NodeRecorded?.Invoke(
                    new MacroNode(
                        MacroNodeType.Delay,
                        milliseconds.ToString(CultureInfo.InvariantCulture)
                    )
                );
        }
        _lastTimestamp = now;
        NodeRecorded?.Invoke(node);
        return true;
    }
}
