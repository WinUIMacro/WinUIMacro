using System.IO.Pipes;
using FluentAssertions;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Tests.Ipc;

[TestClass]
public sealed class EnginePipeServerTests
{
    [TestMethod]
    public Task GetWorkspaceAsync_AfterHello_ReturnsWorkspaceSnapshot() =>
        RunConnectedAsync(
            async (connection, cancellationToken, directory) =>
            {
                var response = await SendRequestAsync(
                    connection,
                    IpcMessageType.GetWorkspace,
                    new Unit(),
                    cancellationToken
                );

                response.Error.Should().BeNull();
                var snapshot = PipeConnection.ReadPayload<WorkspaceSnapshot>(response);
                snapshot.MacroDirectory.Should().Be(new MacroDataPaths(directory).MacroDirectory);
            }
        );

    [TestMethod]
    public Task StopRecordingAsync_WhenRecorderIsIdle_ReturnsUnitResponse() =>
        RunConnectedAsync(
            async (connection, cancellationToken, _) =>
            {
                var response = await SendRequestAsync(
                    connection,
                    IpcMessageType.StopRecording,
                    new StopRecordingRequest(null),
                    cancellationToken
                );

                response.Error.Should().BeNull();
                PipeConnection.ReadPayload<Unit>(response).Should().Be(new Unit());
            }
        );

    [TestMethod]
    public Task GetMacroAsync_WhenMacroDoesNotExist_NextRequestStillSucceeds() =>
        RunConnectedAsync(
            async (connection, cancellationToken, _) =>
            {
                var failed = await SendRequestAsync(
                    connection,
                    IpcMessageType.GetMacro,
                    new GetMacroRequest(Guid.NewGuid()),
                    cancellationToken
                );
                var next = await SendRequestAsync(
                    connection,
                    IpcMessageType.GetWorkspace,
                    new Unit(),
                    cancellationToken
                );

                failed.Error.Should().Contain("找不到宏");
                next.Error.Should().BeNull();
                PipeConnection.ReadPayload<WorkspaceSnapshot>(next).Should().NotBeNull();
            }
        );

    [TestMethod]
    public async Task ConnectAsync_ClientPidDoesNotMatchExpectedUi_RejectsBeforeHello()
    {
        var pipeName = $"WinUIMacro.Tests.Ui.{Guid.NewGuid():N}";
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var workspace = new global::WinUIMacro.EngineWorkspace(
                new MacroDataPaths(directory),
                (_, _) => Task.CompletedTask
            );
            await using var server = new global::WinUIMacro.EnginePipeServer(
                pipeName,
                new EngineRuntime(),
                workspace
            );
            server.RegisterExpectedClient(
                Environment.ProcessId == int.MaxValue ? int.MaxValue - 1 : Environment.ProcessId + 1
            );
            server.Start();
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(timeout.Token);
            await using var connection = new PipeConnection(client);
            await connection.SendAsync(
                new MessageEnvelope(
                    Protocol.Version,
                    MessageKind.Request,
                    IpcMessageType.Hello,
                    Guid.NewGuid(),
                    PipeConnection.ToPayload(new Unit())
                ),
                timeout.Token
            );

            var response = await connection.ReadAsync(timeout.Token);

            response.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunConnectedAsync(
        Func<PipeConnection, CancellationToken, string, Task> assertion
    )
    {
        var pipeName = $"WinUIMacro.Tests.Ui.{Guid.NewGuid():N}";
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var workspace = new global::WinUIMacro.EngineWorkspace(
                new MacroDataPaths(directory),
                (_, _) => Task.CompletedTask
            );
            await using var server = new global::WinUIMacro.EnginePipeServer(
                pipeName,
                new EngineRuntime(),
                workspace
            );
            server.RegisterExpectedClient(Environment.ProcessId);
            server.Start();
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(timeout.Token);
            await using var connection = new PipeConnection(client);

            var hello = await SendRequestAsync(
                connection,
                IpcMessageType.Hello,
                new Unit(),
                timeout.Token
            );
            hello.Error.Should().BeNull();
            PipeConnection.ReadPayload<HelloResponse>(hello).Accepted.Should().BeTrue();

            await assertion(connection, timeout.Token, directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<MessageEnvelope> SendRequestAsync<T>(
        PipeConnection connection,
        IpcMessageType messageType,
        T payload,
        CancellationToken cancellationToken
    )
    {
        var requestId = Guid.NewGuid();
        await connection.SendAsync(
            new MessageEnvelope(
                Protocol.Version,
                MessageKind.Request,
                messageType,
                requestId,
                PipeConnection.ToPayload(payload)
            ),
            cancellationToken
        );
        var response = await connection.ReadAsync(cancellationToken);
        response.Should().NotBeNull();
        response!.Kind.Should().Be(MessageKind.Response);
        response.RequestId.Should().Be(requestId);
        return response;
    }
}
