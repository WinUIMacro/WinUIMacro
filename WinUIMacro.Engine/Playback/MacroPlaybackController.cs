using System.Globalization;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Power;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Win32.Input;
using WinUIMacro.Engine.Win32.Timing;

namespace WinUIMacro.Engine.Playback;

/// <summary>Runs at most one triggered macro in a cancellable worker task.</summary>
[SupportedOSPlatform("windows10.0.17134")]
public sealed class MacroPlaybackController : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<InputOperation> _inject;
    private IReadOnlyDictionary<string, MacroDefinition> _macrosByTrigger =
        new Dictionary<string, MacroDefinition>();
    private PlaybackRun? _current;
    private bool _disposed;

    /// <summary>Initializes a playback controller backed by SendInput.</summary>
    public MacroPlaybackController()
        : this(InputInjector.Send) { }

    internal MacroPlaybackController(Action<InputOperation> inject) => _inject = inject;

    /// <summary>Raised when a macro cannot be replayed.</summary>
    public event EventHandler<MacroPlaybackFailedEventArgs>? PlaybackFailed;

    /// <summary>Replaces the macro and binding snapshot used by future triggers.</summary>
    public void UpdateConfiguration(
        IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroTriggerBinding> bindings
    )
    {
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(bindings);

        var macrosById = macros.ToDictionary(macro => macro.Id);
        var macrosByTrigger = bindings.ToDictionary(
            binding => binding.Trigger,
            binding => macrosById[binding.MacroId],
            StringComparer.Ordinal
        );

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _macrosByTrigger = macrosByTrigger;
        }
    }

    /// <summary>Checks one physical input for a configured playback trigger.</summary>
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

            if (!isPress || !_macrosByTrigger.TryGetValue(trigger, out var macro))
                return;

            PlaybackPlan plan;
            try
            {
                plan = Compile(macro);
            }
            catch (Exception exception)
            {
                PlaybackFailed?.Invoke(this, new MacroPlaybackFailedEventArgs(macro, exception));
                return;
            }

            var run = new PlaybackRun(trigger, macro, plan, new CancellationTokenSource());
            _current = run;
            run.Task = Task.Run(() => Play(run));
        }
    }

    /// <summary>Requests cancellation of the currently running macro.</summary>
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

    /// <summary>Cancels playback and releases owned resources.</summary>
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
        internal static PressedInputIdentityComparer Instance { get; } = new();

        public bool Equals(InputOperation? x, InputOperation? y) =>
            (x, y) switch
            {
                (KeyboardOperation left, KeyboardOperation right) => left.ScanCode == right.ScanCode
                    && left.IsExtended == right.IsExtended,
                (MouseButtonOperation left, MouseButtonOperation right) => left.Button
                    == right.Button,
                _ => false,
            };

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
        public string Trigger { get; } = trigger;
        public MacroDefinition Macro { get; } = macro;
        public PlaybackPlan Plan { get; } = plan;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private sealed record PlaybackPlan(
        IReadOnlyList<PlaybackStep> Steps,
        bool RequiresLoopThrottle
    );

    private readonly record struct PlaybackStep(long? DelayMilliseconds, InputOperation? Operation)
    {
        internal static PlaybackStep Delay(long milliseconds) => new(milliseconds, null);

        internal static PlaybackStep Input(InputOperation operation) => new(null, operation);
    }
}
