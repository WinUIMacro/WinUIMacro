using WinUIMacro.Contracts;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Engine.Win32.Coordinates;

namespace WinUIMacro.Engine;

internal sealed partial class EngineRuntime
{
    private readonly Lock _recordingLock = new();
    private CancellationTokenSource? _recordingStartCancellation;
    private TaskCompletionSource? _recordingStartCompletion;
    private volatile RecordingState _recordingState;
    private Guid? _recordingSessionId;
    private MacroRecorderSession? _recorder;

    public event EventHandler<EngineNodeRecordedEventArgs>? NodeRecorded;
    public event EventHandler<EngineRecordingStateChangedEventArgs>? RecordingStateChanged;

    public async Task<Guid> StartRecordingAsync(Guid macroId, DesktopRectangle stopBounds)
    {
        CancellationTokenSource cancellation;
        TaskCompletionSource startCompletion;
        Guid sessionId;

        lock (_recordingLock)
        {
            if (_recordingState != RecordingState.Idle)
                throw new InvalidOperationException("A recording session is already active.");

            _recordingState = RecordingState.Starting;
            sessionId = Guid.NewGuid();
            _recordingSessionId = sessionId;
            cancellation = new CancellationTokenSource();
            startCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _recordingStartCancellation = cancellation;
            _recordingStartCompletion = startCompletion;
        }

        RecordingStateChanged?.Invoke(
            this,
            new EngineRecordingStateChangedEventArgs(sessionId, RecordingState.Starting)
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
                        new EngineRecordingStateChangedEventArgs(
                            sessionId,
                            RecordingState.Recording
                        )
                    );
                }
            });
        }
        catch
        {
            lock (_recordingLock)
            {
                _recordingState = RecordingState.Idle;
                _recordingSessionId = null;
            }
            RecordingStateChanged?.Invoke(
                this,
                new EngineRecordingStateChangedEventArgs(sessionId, RecordingState.Idle)
            );
            throw;
        }
        finally
        {
            lock (_recordingLock)
            {
                if (ReferenceEquals(_recordingStartCancellation, cancellation))
                {
                    _recordingStartCancellation = null;
                    _recordingStartCompletion = null;
                }
            }

            startCompletion.TrySetResult();
            cancellation.Dispose();
        }

        return sessionId;
    }

    public Task UpdateRecordingBoundsAsync(Guid recordingSessionId, DesktopRectangle stopBounds) =>
        GetDispatcher()
            .InvokeAsync(() =>
            {
                lock (_recordingLock)
                {
                    if (_recordingSessionId == recordingSessionId && _recorder!.IsRecording)
                        _recorder.UpdateStopBounds(stopBounds);
                }
            });

    public async Task StopRecordingAsync(Guid? recordingSessionId = null)
    {
        RecordingState state;
        Guid? sessionId;
        Task? recordingStart;
        bool stateChanged;

        lock (_recordingLock)
        {
            state = _recordingState;
            sessionId = _recordingSessionId;
            if (
                state == RecordingState.Idle
                || recordingSessionId is { } requestedSession && requestedSession != sessionId
            )
                return;

            stateChanged = state != RecordingState.Stopping;
            if (stateChanged)
            {
                _recordingState = RecordingState.Stopping;
                _recordingStartCancellation?.Cancel();
            }
            recordingStart = _recordingStartCompletion?.Task;
        }

        if (stateChanged)
            RecordingStateChanged?.Invoke(
                this,
                new EngineRecordingStateChangedEventArgs(sessionId, RecordingState.Stopping)
            );

        if (state == RecordingState.Recording)
            await GetDispatcher().InvokeAsync(() => _recorder!.Stop());
        else if (recordingStart is not null)
            await recordingStart;
    }

    private void RawInput_InputReceived(InputOperation operation)
    {
        // 开始或停止转换期间不能让输入进入录制或回放。
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

    private void Recorder_NodeRecorded(MacroNode node)
    {
        Guid sessionId;
        lock (_recordingLock)
            sessionId = _recordingSessionId ?? Guid.Empty;
        NodeRecorded?.Invoke(
            this,
            new EngineNodeRecordedEventArgs(sessionId, _recorder!.TargetId, node)
        );
    }

    private void Recorder_Stopped(bool stoppedByRecordButton)
    {
        Guid? sessionId;
        lock (_recordingLock)
        {
            _recordingState = RecordingState.Idle;
            sessionId = _recordingSessionId;
            _recordingSessionId = null;
        }

        RecordingStateChanged?.Invoke(
            this,
            new EngineRecordingStateChangedEventArgs(
                sessionId,
                RecordingState.Idle,
                stoppedByRecordButton
            )
        );
    }
}
