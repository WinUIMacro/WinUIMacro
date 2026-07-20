using System.IO.Pipes;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine;
using WinUIMacro.Engine.Win32.Pipes;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro;

internal sealed class EnginePipeServer(
    string pipeName,
    EngineRuntime engine,
    EngineWorkspace workspace
) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _sessionLock = new();
    private EngineUiSession? _session;
    private TaskCompletionSource _nextUiConnected = CreateCompletion();
    private TaskCompletionSource<int> _expectedClient = CreateClientExpectation();
    private Task? _acceptTask;

    internal void Start() => _acceptTask = AcceptLoopAsync();

    internal Task Completion =>
        _acceptTask ?? throw new InvalidOperationException("UI Pipe 尚未启动。");

    internal void RegisterExpectedClient(int processId)
    {
        lock (_sessionLock)
        {
            if (_session is not null)
                throw new InvalidOperationException("UI 会话已建立，不能更换预期客户端。");
            if (_expectedClient.Task.IsCompleted)
                _expectedClient = CreateClientExpectation();
            _expectedClient.TrySetResult(processId);
        }
    }

    internal Task WaitForUiConnectedAsync(CancellationToken cancellationToken)
    {
        lock (_sessionLock)
            return _session is not null
                ? Task.CompletedTask
                : _nextUiConnected.Task.WaitAsync(cancellationToken);
    }

    internal async Task<bool> TryActivateUiAsync()
    {
        var session = GetSession();
        if (session is null)
            return false;
        try
        {
            if (await session.TryActivateAsync())
                return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or ObjectDisposedException
                        or OperationCanceledException
                        or TimeoutException
                        or InvalidDataException
                        or InvalidOperationException
            ) { }

        RetireSession(session);
        session.Abort();
        return false;
    }

    internal async Task<bool> PrepareUiShutdownAsync()
    {
        var session = GetSession();
        if (session is null)
            return true;
        try
        {
            return await session.PrepareShutdownAsync();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        EngineUiSession? session;
        lock (_sessionLock)
        {
            session = _session;
            _nextUiConnected.TrySetCanceled(_cancellation.Token);
            _expectedClient.TrySetCanceled(_cancellation.Token);
        }
        if (session is not null)
            await session.DisposeAsync();
        if (_acceptTask is not null)
            await _acceptTask;
        _cancellation.Dispose();
    }

    private EngineUiSession? GetSession()
    {
        lock (_sessionLock)
            return _session;
    }

    private void RetireSession(EngineUiSession session)
    {
        lock (_sessionLock)
        {
            if (!ReferenceEquals(_session, session))
                return;
            _session = null;
            _nextUiConnected = CreateCompletion();
            _expectedClient = CreateClientExpectation();
            if (_cancellation.IsCancellationRequested)
            {
                _nextUiConnected.TrySetCanceled(_cancellation.Token);
                _expectedClient.TrySetCanceled(_cancellation.Token);
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                // UI 会话串行运行；当前会话断开后才接受下一次连接。
                await using var pipe = NamedPipeServerFactory.Create(pipeName, 1);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                await HandleConnectionAsync(pipe);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        await using var connection = new PipeConnection(pipe);
        try
        {
            // 握手前先校验会话、完整性级别及 Host 实际启动的 UI 进程身份。
            var client = ProcessSecurity.ValidateClient(pipe, requireAtLeastMedium: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token
            );
            timeout.CancelAfter(Protocol.RequestTimeout);
            Task<int> expectedClient;
            lock (_sessionLock)
                expectedClient = _expectedClient.Task;
            if (client.ProcessId != await expectedClient.WaitAsync(timeout.Token))
                throw new UnauthorizedAccessException("Pipe 客户端不是 Host 启动的 UI 进程。");

            var helloMessage = await connection.ReadAsync(timeout.Token);
            if (
                helloMessage
                is not {
                    Kind: MessageKind.Request,
                    MessageType: IpcMessageType.Hello,
                    RequestId: not null,
                }
            )
                throw new InvalidDataException("UI 未发送有效握手。");
            if (helloMessage.ProtocolVersion != Protocol.Version)
            {
                await connection.SendResponseAsync(
                    helloMessage,
                    new HelloResponse(false),
                    "IPC 协议版本不匹配。"
                );
                return;
            }

            _ = PipeConnection.ReadPayload<Unit>(helloMessage);

            var newSession = new EngineUiSession(connection, engine, workspace);
            TaskCompletionSource uiConnected;
            lock (_sessionLock)
            {
                _session = newSession;
                uiConnected = _nextUiConnected;
            }

            try
            {
                await connection.SendResponseAsync(helloMessage, new HelloResponse(true));
                uiConnected.TrySetResult();
                await newSession.RunAsync(_cancellation.Token);
            }
            finally
            {
                RetireSession(newSession);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (IOException) { }
        catch (Exception exception)
        {
            workspace.RecordError(exception.Message);
        }
    }

    private static TaskCompletionSource CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<int> CreateClientExpectation() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
