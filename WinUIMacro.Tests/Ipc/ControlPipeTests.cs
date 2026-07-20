// 验证 Control Pipe 的单连接超时不会阻塞后续正常激活请求。
using System.IO.Pipes;
using FluentAssertions;

namespace WinUIMacro.Tests.Ipc;

[TestClass]
public sealed class ControlPipeTests
{
    [TestMethod]
    public async Task OpenUiAsync_PreviousClientSendsNothing_StillProcessesNextRequest()
    {
        var pipeName = $"WinUIMacro.Tests.Control.{Guid.NewGuid():N}";
        var openCount = 0;
        await using var server = new global::WinUIMacro.ControlPipeServer(
            pipeName,
            engineIsElevated: true,
            () =>
            {
                Interlocked.Increment(ref openCount);
            },
            TimeSpan.FromMilliseconds(100)
        );
        server.Start();
        await using var stalledClient = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stalledClient.ConnectAsync(timeout.Token);

        var response = await global::WinUIMacro.ControlPipeClient.OpenUiAsync(
            pipeName,
            timeout.Token
        );

        response.EngineIsElevated.Should().BeTrue();
        openCount.Should().Be(1);
    }
}
