// 验证命名管道服务端的名称、并发实例和安全配置。
using System.IO.Pipes;
using FluentAssertions;
using WinUIMacro.Engine.Win32.Pipes;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro.Tests.Win32.Pipes;

[TestClass]
public sealed class NamedPipeServerFactoryTests
{
    [TestMethod]
    public async Task Create_AllowsAtLeastMediumIntegrityCurrentSessionClient()
    {
        var pipeName = $"WinUIMacro.Tests.{Guid.NewGuid():N}";
        await using var server = NamedPipeServerFactory.Create(pipeName, 1);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var accept = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await accept;

        var clientInfo = ProcessSecurity.ValidateClient(server, requireAtLeastMedium: true);

        clientInfo.ProcessId.Should().Be(Environment.ProcessId);
        clientInfo.IsElevated.Should().Be(ProcessSecurity.IsCurrentProcessElevated());
    }
}
