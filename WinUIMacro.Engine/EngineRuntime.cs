using System.Diagnostics;
using System.Runtime.Versioning;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Playback;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Engine.Win32.Messaging;
using WinUIMacro.Engine.Win32.RawInput;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed partial class EngineRuntime
{
    private readonly Lock _stateLock = new();
    private Thread? _thread;
    private Win32MessageLoop? _messageLoop;
    private Win32ThreadDispatcher? _dispatcher;
    private MacroPlaybackController? _playback;
    private Task _startup = Task.CompletedTask;
    private Task _completion = Task.CompletedTask;

    public event EventHandler<MacroPlaybackFailedEventArgs>? PlaybackFailed;
    public event Action<Exception>? ProcessingFailed;
    public event Action? OpenUiRequested;
    public event Action? ExitRequested;

    public Task Completion
    {
        get
        {
            lock (_stateLock)
                return _completion;
        }
    }

    public Task StartAsync()
    {
        lock (_stateLock)
        {
            // Win32 资源由同一消息线程成组创建和销毁；重复启动复用首次任务。
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

    public Task UpdateConfigurationAsync(
        IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroTriggerBinding> bindings
    )
    {
        var macroSnapshot = macros.ToArray();
        var bindingSnapshot = bindings.ToArray();
        return GetDispatcher()
            .InvokeAsync(() => _playback!.UpdateConfiguration(macroSnapshot, bindingSnapshot));
    }

    public async Task StopAsync()
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
            // 这些对象具有消息线程亲和性，必须按声明顺序在此线程释放。
            using var messageWindow = new NativeHostWindow();
            using var tray = new NativeTrayService(messageWindow);
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
            tray.OpenUiRequested += OnOpenUiRequested;
            tray.ExitRequested += OnExitRequested;

            lock (_stateLock)
            {
                _dispatcher = dispatcher;
                _messageLoop = messageLoop;
            }
            tray.Create();
            startup.TrySetResult();
            messageLoop.Run();

            recorder.Stop();
            recorder.NodeRecorded -= Recorder_NodeRecorded;
            recorder.Stopped -= Recorder_Stopped;
            playback.PlaybackFailed -= Playback_PlaybackFailed;
            messageWindow.MessageDispatchFailed -= OnProcessingFailed;
            rawInput.InputReceived -= RawInput_InputReceived;
            tray.OpenUiRequested -= OnOpenUiRequested;
            tray.ExitRequested -= OnExitRequested;
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

    private void Playback_PlaybackFailed(object? sender, MacroPlaybackFailedEventArgs args) =>
        PlaybackFailed?.Invoke(this, args);

    private void OnProcessingFailed(Exception exception) => ProcessingFailed?.Invoke(exception);

    private void OnOpenUiRequested() => OpenUiRequested?.Invoke();

    private void OnExitRequested() => ExitRequested?.Invoke();
}
