// 为外部控制请求提供独立的本机会话管道。
using System.IO.Pipes;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine.Win32.Pipes;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro;

internal sealed class ControlPipeServer(
    string pipeName,
    bool engineIsElevated,
    Action openUi,
    TimeSpan? requestTimeout = null
) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _runTask;

    internal void Start() => _runTask = RunAsync();

    internal Task Completion =>
        _runTask ?? throw new InvalidOperationException("Control Pipe 尚未启动。");

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException) { }
        }
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                // 控制管道一次只处理一个短请求，连接关闭后再创建下一实例。
                await using var pipe = NamedPipeServerFactory.Create(pipeName, 1);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                var client = ProcessSecurity.ValidateClient(pipe, requireAtLeastMedium: false);
                await using var connection = new PipeConnection(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellation.Token
                );
                timeout.CancelAfter(requestTimeout ?? Protocol.RequestTimeout);
                var request = await connection.ReadAsync(timeout.Token);
                if (
                    request
                    is not {
                        ProtocolVersion: Protocol.Version,
                        Kind: MessageKind.Request,
                        MessageType: IpcMessageType.Control,
                        RequestId: { },
                    }
                )
                    continue;

                _ = PipeConnection.ReadPayload<Unit>(request);
                // 管理员客户端不能把普通权限引擎拉起的 UI 提升到管理员权限。
                if (!(client.IsElevated && !engineIsElevated))
                    openUi();
                await connection.SendResponseAsync(
                    request,
                    new ControlResponse(engineIsElevated),
                    cancellationToken: timeout.Token
                );
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                ) { }
        }
    }
}

internal static class ControlPipeClient
{
    internal static async Task<ControlResponse> OpenUiAsync(
        string pipeName,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        await stream.ConnectAsync(cancellationToken);
        await using var connection = new PipeConnection(stream);
        var request = connection.SendRequestAsync<Unit, ControlResponse>(
            IpcMessageType.Control,
            new Unit(),
            cancellationToken: cancellationToken
        );
        try
        {
            var response = await connection.ReadAsync(cancellationToken);
            if (response is null)
                connection.FailPendingRequests(new EndOfStreamException("Control Pipe 已断开。"));
            else if (!connection.TryHandleResponse(response))
                connection.FailPendingRequests(
                    new InvalidDataException("Control Pipe 未返回有效响应。")
                );
            return await request;
        }
        catch (Exception exception)
        {
            connection.FailPendingRequests(exception);
            try
            {
                await request;
            }
            catch { }
            throw;
        }
    }
}
