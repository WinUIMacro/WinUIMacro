// 验证 Explorer COM 启动等待可被取消，且后台 STA 不阻塞宿主退出。
using FluentAssertions;
using WinUIMacro.Engine.Win32.Shell;

namespace WinUIMacro.Tests.Win32.Shell;

[TestClass]
public sealed class ExplorerShellLauncherTests
{
    [TestMethod]
    public async Task RunOnStaAsync_BlockingCall_CancellationReleasesCallerAndAllowsNextCall()
    {
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var apartment = new TaskCompletionSource<ApartmentState>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var cancellation = new CancellationTokenSource();
        var launch = ExplorerShellLauncher.RunOnStaAsync(
            () =>
            {
                try
                {
                    apartment.TrySetResult(Thread.CurrentThread.GetApartmentState());
                    release.Wait();
                }
                finally
                {
                    finished.Set();
                }
            },
            cancellation.Token
        );
        (await apartment.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(ApartmentState.STA);

        cancellation.Cancel();

        await launch.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        var retryThread = 0;
        await ExplorerShellLauncher.RunOnStaAsync(() =>
            retryThread = Environment.CurrentManagedThreadId
        );
        retryThread.Should().NotBe(0);
        release.Set();
        finished.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }
}
