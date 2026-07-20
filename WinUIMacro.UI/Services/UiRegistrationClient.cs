// 在连接 Engine 前向 Host 注册当前 Explorer 启动的 UI 进程。
using System.IO.Pipes;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.UI.Services;

internal static class UiRegistrationClient
{
    internal static async Task RegisterAsync(
        string pipeName,
        string token,
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
        var registration = connection.SendRequestAsync<
            UiRegistrationRequest,
            UiRegistrationResponse
        >(
            IpcMessageType.UiRegistration,
            new UiRegistrationRequest(token),
            cancellationToken: cancellationToken
        );
        try
        {
            var response = await connection.ReadAsync(cancellationToken);
            if (response is null)
                connection.FailPendingRequests(new EndOfStreamException("UI 注册管道已断开。"));
            else if (!connection.TryHandleResponse(response))
                connection.FailPendingRequests(
                    new InvalidDataException("UI 注册管道未返回有效响应。")
                );

            if (!(await registration).Accepted)
                throw new UnauthorizedAccessException("Host 拒绝了 UI 启动注册。");
        }
        catch (Exception exception)
        {
            connection.FailPendingRequests(exception);
            try
            {
                await registration;
            }
            catch { }
            throw;
        }
    }
}
