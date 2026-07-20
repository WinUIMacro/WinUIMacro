// 验证隐藏窗口消息循环的启动、停止和异常传播。
using FluentAssertions;
using WinUIMacro.Engine.Win32.Messaging;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Tests.Win32.Messaging;

[TestClass]
public sealed class Win32MessageLoopTests
{
    [TestMethod]
    public void RequestStop_FromAnotherThread_StopsMessageLoop()
    {
        using var ready = new ManualResetEventSlim();
        Win32MessageLoop? messageLoop = null;
        Exception? threadException = null;
        var messageThread = new Thread(() =>
        {
            try
            {
                using var window = new NativeHostWindow();
                messageLoop = new Win32MessageLoop(window);
                ready.Set();
                messageLoop.Run();
            }
            catch (Exception exception)
            {
                threadException = exception;
                ready.Set();
            }
        })
        {
            IsBackground = true,
        };

        messageThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        messageLoop.Should().NotBeNull();
        messageLoop!.RequestStop();

        messageThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        threadException.Should().BeNull();
    }
}
