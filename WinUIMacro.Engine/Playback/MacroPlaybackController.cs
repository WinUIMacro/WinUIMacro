// 根据触发键编译并执行宏，同时保证取消时释放仍按下的输入。
using System.Globalization;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Power;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Win32.Input;
using WinUIMacro.Engine.Win32.Timing;

namespace WinUIMacro.Engine.Playback;

/// <summary>在可取消的工作任务中执行最多一个已触发的宏。</summary>
[SupportedOSPlatform("windows10.0.17134")]
internal sealed class MacroPlaybackController : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<InputOperation> _inject;
    private IReadOnlyDictionary<string, PreparedMacro> _macrosByTrigger =
        new Dictionary<string, PreparedMacro>();
    private PlaybackRun? _current;
    private bool _disposed;

    /// <summary>初始化由 SendInput 驱动的回放控制器。</summary>
    public MacroPlaybackController()
        : this(InputInjector.Send) { }

    /// <summary>使用可替换的输入注入委托创建回放控制器。</summary>
    internal MacroPlaybackController(Action<InputOperation> inject) => _inject = inject;

    /// <summary>当宏无法回放时引发。</summary>
    public event EventHandler<MacroPlaybackFailedEventArgs>? PlaybackFailed;

    /// <summary>替换后续触发操作使用的宏和绑定快照。</summary>
    public void UpdateConfiguration(
        IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroTriggerBinding> bindings
    )
    {
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(bindings);

        var macrosById = macros.ToDictionary(macro => macro.Id);
        Dictionary<Guid, PreparedMacro> preparedById = [];
        Dictionary<string, PreparedMacro> macrosByTrigger = new(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (!preparedById.TryGetValue(binding.MacroId, out var prepared))
            {
                var macro = macrosById[binding.MacroId];
                prepared = new PreparedMacro(macro, Compile(macro));
                preparedById.Add(macro.Id, prepared);
            }
            macrosByTrigger.Add(binding.Trigger, prepared);
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _macrosByTrigger = macrosByTrigger;
        }
    }

    /// <summary>检查一个物理输入是否匹配已配置的回放触发键。</summary>
    public void ProcessInput(InputOperation operation)
    {
        if (!MacroInputRegistry.TryGetTrigger(operation, out var trigger, out var isPress))
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_current is { } current)
            {
                // Hold 在释放触发键时停止，Toggle 在再次按下同一触发键时停止。
                if (
                    current.Macro.Mode == MacroPlaybackMode.Hold
                    && !isPress
                    && current.Trigger == trigger
                )
                    current.Cancellation.Cancel();
                else if (
                    current.Macro.Mode == MacroPlaybackMode.Toggle
                    && isPress
                    && current.Trigger == trigger
                )
                    current.Cancellation.Cancel();
                return;
            }

            if (!isPress || !_macrosByTrigger.TryGetValue(trigger, out var prepared))
                return;

            var run = new PlaybackRun(
                trigger,
                prepared.Macro,
                prepared.Plan,
                new CancellationTokenSource()
            );
            _current = run;
            run.Task = Task.Run(() => Play(run));
        }
    }

    /// <summary>请求取消当前正在运行的宏。</summary>
    public Task StopAsync()
    {
        PlaybackRun? run;
        lock (_sync)
        {
            run = _current;
            run?.Cancellation.Cancel();
        }
        return run?.Task ?? Task.CompletedTask;
    }

    /// <summary>取消回放并释放控制器持有的资源。</summary>
    public void Dispose()
    {
        PlaybackRun? run;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            run = _current;
            run?.Cancellation.Cancel();
        }
        try
        {
            run?.Task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
    }

    private void Play(PlaybackRun run)
    {
        HashSet<InputOperation> pressed = new(PressedInputIdentityComparer.Instance);
        // 播放期间请求系统保持唤醒，避免长宏被待机打断。
        var sleepBlocked =
            PInvoke.SetThreadExecutionState(
                EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED
            ) != 0;
        try
        {
            using var delay = new HighResolutionDelay();
            do
            {
                foreach (var step in run.Plan.Steps)
                {
                    run.Cancellation.Token.ThrowIfCancellationRequested();
                    if (step.DelayMilliseconds is { } milliseconds)
                    {
                        delay.Delay(milliseconds, run.Cancellation.Token);
                        continue;
                    }
                    var operation = step.Operation!;
                    _inject(operation);
                    TrackPressed(pressed, operation);
                }
                if (
                    // 没有正延时的循环宏需要主动让出少量时间，避免占满 CPU。
                    run.Plan.RequiresLoopThrottle
                    && run.Macro.Mode is MacroPlaybackMode.Toggle or MacroPlaybackMode.Hold
                )
                    delay.Delay(1, run.Cancellation.Token);
            } while (run.Macro.Mode is MacroPlaybackMode.Toggle or MacroPlaybackMode.Hold);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PlaybackFailed?.Invoke(this, new MacroPlaybackFailedEventArgs(run.Macro, exception));
        }
        finally
        {
            if (sleepBlocked)
                PInvoke.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            // 无论正常结束、取消还是异常，都释放仍处于按下状态的输入。
            ReleasePressed(pressed);
            lock (_sync)
            {
                if (ReferenceEquals(_current, run))
                    _current = null;
            }
            run.Cancellation.Dispose();
        }
    }

    private static PlaybackPlan Compile(MacroDefinition macro)
    {
        List<PlaybackStep> steps = [];
        var hasPositiveDelay = false;
        foreach (var node in macro.Nodes)
        {
            // 备注只供编辑器显示，不参与回放；延时节点单独解析为毫秒。
            if (node.Type == MacroNodeType.Note)
                continue;
            if (node.Type == MacroNodeType.Delay)
            {
                if (
                    !long.TryParse(
                        node.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var milliseconds
                    )
                    || milliseconds < 0
                )
                    throw new InvalidDataException($"Invalid delay '{node.Value}'.");
                hasPositiveDelay |= milliseconds > 0;
                steps.Add(PlaybackStep.Delay(milliseconds));
                continue;
            }
            if (!MacroInputRegistry.TryCreateOperation(node, out var operation))
                throw new InvalidDataException($"Unsupported macro node {node.Type}:{node.Value}.");
            steps.Add(PlaybackStep.Input(operation));
        }
        return new PlaybackPlan(steps, !hasPositiveDelay);
    }

    private static void TrackPressed(HashSet<InputOperation> pressed, InputOperation operation)
    {
        switch (operation)
        {
            case KeyboardOperation { IsPress: true } or MouseButtonOperation { IsPress: true }:
                _ = pressed.Add(operation);
                break;
            case KeyboardOperation { IsPress: false } or MouseButtonOperation { IsPress: false }:
                _ = pressed.Remove(operation);
                break;
        }
    }

    private void ReleasePressed(HashSet<InputOperation> pressed)
    {
        foreach (var operation in pressed)
        {
            InputOperation? release = operation switch
            {
                KeyboardOperation keyboard => keyboard with { IsPress = false },
                MouseButtonOperation mouse => mouse with { IsPress = false },
                _ => null,
            };
            if (release is not null)
            {
                try
                {
                    _inject(release);
                }
                catch { }
            }
        }
    }

    private sealed class PressedInputIdentityComparer : IEqualityComparer<InputOperation>
    {
        /// <summary>获取按输入物理身份比较的共享实例。</summary>
        internal static PressedInputIdentityComparer Instance { get; } = new();

        /// <summary>比较两个操作是否表示同一个键盘键或鼠标按钮。</summary>
        public bool Equals(InputOperation? x, InputOperation? y) =>
            (x, y) switch
            {
                (KeyboardOperation left, KeyboardOperation right) => left.ScanCode == right.ScanCode
                    && left.IsExtended == right.IsExtended,
                (MouseButtonOperation left, MouseButtonOperation right) => left.Button
                    == right.Button,
                _ => false,
            };

        /// <summary>获取与输入物理身份一致的哈希码。</summary>
        public int GetHashCode(InputOperation operation) =>
            operation switch
            {
                KeyboardOperation keyboard => HashCode.Combine(
                    nameof(KeyboardOperation),
                    keyboard.ScanCode,
                    keyboard.IsExtended
                ),
                MouseButtonOperation mouse => HashCode.Combine(
                    nameof(MouseButtonOperation),
                    mouse.Button
                ),
                _ => operation.GetHashCode(),
            };
    }

    private sealed class PlaybackRun(
        string trigger,
        MacroDefinition macro,
        PlaybackPlan plan,
        CancellationTokenSource cancellation
    )
    {
        /// <summary>获取启动本次回放的触发值。</summary>
        public string Trigger { get; } = trigger;

        /// <summary>获取本次回放使用的宏快照。</summary>
        public MacroDefinition Macro { get; } = macro;

        /// <summary>获取预先编译的回放计划。</summary>
        public PlaybackPlan Plan { get; } = plan;

        /// <summary>获取用于停止本次回放的取消源。</summary>
        public CancellationTokenSource Cancellation { get; } = cancellation;

        /// <summary>获取或设置正在执行的回放任务。</summary>
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private sealed record PreparedMacro(MacroDefinition Macro, PlaybackPlan Plan);

    private sealed record PlaybackPlan(
        IReadOnlyList<PlaybackStep> Steps,
        bool RequiresLoopThrottle
    );

    private readonly record struct PlaybackStep(long? DelayMilliseconds, InputOperation? Operation)
    {
        /// <summary>创建延时回放步骤。</summary>
        internal static PlaybackStep Delay(long milliseconds) => new(milliseconds, null);

        /// <summary>创建输入注入回放步骤。</summary>
        internal static PlaybackStep Input(InputOperation operation) => new(null, operation);
    }
}
