using System.Diagnostics;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Playback;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Engine.Win32.Coordinates;
using WinUIMacro.Engine.Win32.Input;
using WinUIMacro.Engine.Win32.Messaging;
using WinUIMacro.Engine.Win32.RawInput;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Services;

internal sealed class EngineThreadService
{
    private readonly Lock _stateLock = new();
    private readonly Lock _recordingLock = new();
    private CancellationTokenSource? _recordingStartCancellation;
    private RecordingState _recordingState;
    private Thread? _thread;
    private Win32MessageLoop? _messageLoop;
    private Win32ThreadDispatcher? _dispatcher;
    private MacroRecorderSession? _recorder;
    private MacroPlaybackController? _playback;
    private Task _startup = Task.CompletedTask;
    private Task _completion = Task.CompletedTask;

    internal event EventHandler<MacroNodeRecordedEventArgs>? NodeRecorded;
    internal event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
    internal event EventHandler<MacroPlaybackFailedEventArgs>? PlaybackFailed;
    internal event Action<Exception>? ProcessingFailed;

    internal Task Completion
    {
        get
        {
            lock (_stateLock)
                return _completion;
        }
    }

    internal Task StartAsync()
    {
        lock (_stateLock)
        {
            if (_thread is not null)
                return _startup;
            var startup = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _startup = startup.Task;
            _completion = completion.Task;
            _thread = new Thread(() => Run(startup, completion))
            {
                IsBackground = true,
                Name = "WinUIMacro engine",
            };
            _thread.Start();
            return _startup;
        }
    }

    internal Task UpdateConfigurationAsync(
        IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroTriggerBinding> bindings
    )
    {
        var macroSnapshot = macros.ToArray();
        var bindingSnapshot = bindings.ToArray();
        return GetDispatcher()
            .InvokeAsync(() => _playback!.UpdateConfiguration(macroSnapshot, bindingSnapshot));
    }

    internal async Task StartRecordingAsync(Guid macroId, DesktopRectangle stopBounds)
    {
        CancellationTokenSource cancellation;

        lock (_recordingLock)
        {
            if (_recordingState != RecordingState.Idle)
                return;

            _recordingState = RecordingState.Starting;
            cancellation = new CancellationTokenSource();
            _recordingStartCancellation = cancellation;
        }

        RecordingStateChanged?.Invoke(
            this,
            new RecordingStateChangedEventArgs(RecordingState.Starting)
        );

        try
        {
            var dispatcher = GetDispatcher();
            Task playbackStopped = Task.CompletedTask;

            await dispatcher.InvokeAsync(() =>
            {
                playbackStopped = _playback!.StopAsync();
            });

            await playbackStopped.WaitAsync(cancellation.Token);

            await dispatcher.InvokeAsync(() =>
            {
                lock (_recordingLock)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    _recorder!.Start(macroId, stopBounds);
                    _recordingState = RecordingState.Recording;
                    RecordingStateChanged?.Invoke(
                        this,
                        new RecordingStateChangedEventArgs(RecordingState.Recording)
                    );
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (_recordingLock)
                _recordingState = RecordingState.Idle;

            RecordingStateChanged?.Invoke(
                this,
                new RecordingStateChangedEventArgs(RecordingState.Idle)
            );
        }
        catch
        {
            lock (_recordingLock)
                _recordingState = RecordingState.Idle;
            RecordingStateChanged?.Invoke(
                this,
                new RecordingStateChangedEventArgs(RecordingState.Idle)
            );
            throw;
        }
        finally
        {
            lock (_recordingLock)
            {
                if (ReferenceEquals(_recordingStartCancellation, cancellation))
                    _recordingStartCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    internal Task UpdateRecordingBoundsAsync(DesktopRectangle stopBounds) =>
        GetDispatcher()
            .InvokeAsync(() =>
            {
                if (_recorder!.IsRecording)
                    _recorder.UpdateStopBounds(stopBounds);
            });

    internal async Task StopRecordingAsync()
    {
        RecordingState state;

        lock (_recordingLock)
        {
            state = _recordingState;

            if (state == RecordingState.Idle)
                return;

            _recordingState = RecordingState.Stopping;
            _recordingStartCancellation?.Cancel();
        }

        RecordingStateChanged?.Invoke(
            this,
            new RecordingStateChangedEventArgs(RecordingState.Stopping)
        );

        if (state == RecordingState.Recording)
            await GetDispatcher().InvokeAsync(() => _recorder!.Stop());
    }

    internal async Task StopAsync()
    {
        await StopRecordingAsync();
        Win32MessageLoop? messageLoop;
        Task completion;
        lock (_stateLock)
        {
            if (_thread is null)
                return;
            messageLoop = _messageLoop;
            completion = _completion;
        }

        try
        {
            messageLoop?.RequestStop();
            await completion;
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_completion, completion))
                {
                    _dispatcher = null;
                    _messageLoop = null;
                    _thread = null;
                }
            }
        }
    }

    private Win32ThreadDispatcher GetDispatcher()
    {
        lock (_stateLock)
            return _dispatcher
                ?? throw new InvalidOperationException("The engine thread is not running.");
    }

    private void Run(TaskCompletionSource startup, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            using var messageWindow = new MessageOnlyWindow();
            using var dispatcher = new Win32ThreadDispatcher(messageWindow);
            using var rawInput = new RawInputService(messageWindow);
            using var playback = new MacroPlaybackController();
            var recorder = new MacroRecorderSession();
            var messageLoop = new Win32MessageLoop(messageWindow);

            _recorder = recorder;
            _playback = playback;
            recorder.NodeRecorded += Recorder_NodeRecorded;
            recorder.Stopped += Recorder_Stopped;
            playback.PlaybackFailed += Playback_PlaybackFailed;
            messageWindow.MessageDispatchFailed += OnProcessingFailed;
            rawInput.InputReceived += RawInput_InputReceived;

            lock (_stateLock)
            {
                _dispatcher = dispatcher;
                _messageLoop = messageLoop;
            }
            startup.TrySetResult();
            messageLoop.Run();

            recorder.Stop();
            recorder.NodeRecorded -= Recorder_NodeRecorded;
            recorder.Stopped -= Recorder_Stopped;
            playback.PlaybackFailed -= Playback_PlaybackFailed;
            messageWindow.MessageDispatchFailed -= OnProcessingFailed;
            rawInput.InputReceived -= RawInput_InputReceived;
        }
        catch (Exception exception)
        {
            failure = exception;
            startup.TrySetException(exception);
        }
        finally
        {
            lock (_recordingLock)
            {
                _recordingState = RecordingState.Idle;
                _recordingStartCancellation?.Cancel();
            }
            lock (_stateLock)
            {
                _dispatcher = null;
                _messageLoop = null;
                _recorder = null;
                _playback = null;
            }
            if (!startup.Task.IsCompleted)
            {
                failure ??= new InvalidOperationException(
                    "The engine thread exited before startup completed."
                );
                startup.TrySetException(failure);
            }
            if (failure is null)
                completion.TrySetResult();
            else
            {
                Debug.WriteLine(failure);
                completion.TrySetException(failure);
            }
        }
    }

    private void RawInput_InputReceived(InputOperation operation)
    {
        if (_recordingState is RecordingState.Starting or RecordingState.Stopping)
            return;

        DesktopPoint? cursor = operation
            is MouseButtonOperation { Button: MouseButton.Left, IsPress: true }
            ? DesktopCoordinates.GetCursorPosition()
            : null;
        if (_recorder!.ProcessInput(operation, cursor))
            return;
        _playback!.ProcessInput(operation);
    }

    private void Recorder_NodeRecorded(MacroNode node) =>
        NodeRecorded?.Invoke(this, new MacroNodeRecordedEventArgs(_recorder!.TargetId, node));

    private void Recorder_Stopped(bool stoppedByRecordButton)
    {
        lock (_recordingLock)
            _recordingState = RecordingState.Idle;

        RecordingStateChanged?.Invoke(
            this,
            new RecordingStateChangedEventArgs(RecordingState.Idle, stoppedByRecordButton)
        );
    }

    private void Playback_PlaybackFailed(object? sender, MacroPlaybackFailedEventArgs args) =>
        PlaybackFailed?.Invoke(this, args);

    private void OnProcessingFailed(Exception exception) => ProcessingFailed?.Invoke(exception);
}
