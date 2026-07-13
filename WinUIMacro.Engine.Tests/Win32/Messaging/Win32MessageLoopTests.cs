using FluentAssertions;
using WinUIMacro.Engine.Win32.Messaging;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Tests.Win32.Messaging;

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
                using var window = new MessageOnlyWindow();
                messageLoop = new Win32MessageLoop(window);
                ready.Set();
                messageLoop.Run();
            }
            catch (Exception exception)
            {
                threadException = exception;
                ready.Set();
            }
        });

        messageThread.Start();
        ready.Wait();
        messageLoop.Should().NotBeNull();
        messageLoop!.RequestStop();

        messageThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        threadException.Should().BeNull();
    }
}
