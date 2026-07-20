// 接受 Explorer 启动的 UI 注册，并读取操作系统确认的客户端 PID。
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine.Win32.Pipes;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro;

internal static class UiRegistrationServer
{
    internal static async Task<int> AcceptAsync(
        string pipeName,
        string expectedToken,
        string expectedExecutablePath,
        CancellationToken cancellationToken,
        bool rejectElevatedClient = true
    )
    {
        var expectedPath = Path.GetFullPath(expectedExecutablePath);
        while (true)
        {
            await using var pipe = NamedPipeServerFactory.Create(pipeName, 1);
            await pipe.WaitForConnectionAsync(cancellationToken);
            try
            {
                var processId = await HandleCandidateAsync(
                    pipe,
                    expectedToken,
                    expectedPath,
                    cancellationToken,
                    rejectElevatedClient
                );
                if (processId is { } acceptedProcessId)
                    return acceptedProcessId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or System.Text.Json.JsonException
                ) { }
        }
    }

    private static async Task<int?> HandleCandidateAsync(
        NamedPipeServerStream pipe,
        string expectedToken,
        string expectedExecutablePath,
        CancellationToken cancellationToken,
        bool rejectElevatedClient
    )
    {
        var client = ProcessSecurity.ValidateClient(pipe, requireAtLeastMedium: true);
        if (rejectElevatedClient && client.IsElevated)
            throw new UnauthorizedAccessException("Explorer 启动的 UI 进程不能处于提升权限状态。");
        var actualPath = Path.GetFullPath(ProcessSecurity.GetProcessImagePath(client.ProcessId));
        if (!string.Equals(actualPath, expectedExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("注册客户端不是预期的 WinUIMacro.UI.exe。");

        await using var connection = new PipeConnection(pipe);
        var request = await connection.ReadAsync(cancellationToken);
        if (
            request
            is not {
                Kind: MessageKind.Request,
                MessageType: IpcMessageType.UiRegistration,
                RequestId: not null,
            }
        )
            throw new InvalidDataException("UI 未发送有效的启动注册请求。");
        if (request.ProtocolVersion != Protocol.Version)
        {
            await connection.SendResponseAsync(
                request,
                new UiRegistrationResponse(false),
                "UI 启动注册协议版本不匹配。",
                cancellationToken
            );
            return null;
        }

        var registration = PipeConnection.ReadPayload<UiRegistrationRequest>(request);
        if (!TokensEqual(expectedToken, registration.Token))
        {
            await connection.SendResponseAsync(
                request,
                new UiRegistrationResponse(false),
                "UI 启动注册令牌无效。",
                cancellationToken
            );
            return null;
        }

        await connection.SendResponseAsync(
            request,
            new UiRegistrationResponse(true),
            cancellationToken: cancellationToken
        );
        return client.ProcessId;
    }

    private static bool TokensEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
