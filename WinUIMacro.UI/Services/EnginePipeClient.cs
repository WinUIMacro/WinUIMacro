using System.IO.Pipes;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.UI.Services;

internal sealed partial class EnginePipeClient(string pipeName) : IAsyncDisposable
{
    private readonly string _pipeName = pipeName;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private PipeConnection? _connection;
    private Task? _readTask;
    private int _disconnected;
    private bool _disposed;

    internal event Func<bool>? ActivateUiRequested;
    internal event Action<NodeRecordedBatchEvent>? NodesRecorded;
    internal event Action<RecordingStateChangedEvent>? RecordingStateChanged;
    internal event Action<RuntimeFailureEvent>? PlaybackFailed;
    internal event Action<RuntimeFailureEvent>? ProcessingFailed;
    internal event Action<Exception?>? Disconnected;
    internal event Action? HostShutdownApproved;
    internal event Func<CancellationToken, Task<bool>>? PrepareUiShutdownRequested;

    internal async Task<HelloResponse> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is not null)
            throw new InvalidOperationException("The engine pipe client has already connected.");

        var stream = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );

        try
        {
            await stream.ConnectAsync(cancellationToken);
            _connection = new PipeConnection(stream);
            // 必须先启动读循环，握手响应和后续事件才有接收方。
            _readTask = ReadLoopAsync();
            return await _connection.SendRequestAsync<Unit, HelloResponse>(
                IpcMessageType.Hello,
                new Unit(),
                cancellationToken: cancellationToken
            );
        }
        catch
        {
            if (_connection is null)
                await stream.DisposeAsync();
            else
                await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Exchange(ref _disconnected, 1);
        _disposeCancellation.Cancel();

        if (_connection is not null)
            await _connection.DisposeAsync();

        if (_readTask is not null)
        {
            try
            {
                await _readTask;
            }
            catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
            { }
        }

        _disposeCancellation.Dispose();
    }

    private PipeConnection GetConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _disconnected) != 0)
            throw new IOException("The engine pipe is disconnected.");
        return _connection
            ?? throw new InvalidOperationException("The engine pipe client is not connected.");
    }

    private void SignalDisconnected(Exception? failure)
    {
        // 读循环、异常和释放路径都可能发现断开，通知只能触发一次。
        if (Interlocked.Exchange(ref _disconnected, 1) != 0)
            return;

        _connection?.FailPendingRequests(
            failure ?? new EndOfStreamException("The engine pipe was closed.")
        );
        Disconnected?.Invoke(failure);
    }
}
