using FluentAssertions;
using WinUIMacro.Engine.Win32.Messaging;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Tests.Win32.Messaging;

[TestClass]
public sealed class Win32ThreadDispatcherTests
{
    [TestMethod]
    public async Task InvokeAsync_ExecutesInOrderOnMessageThread()
    {
        using var host = new DispatcherHost();
        List<(int Value, int ThreadId)> calls = [];

        var first = host.Dispatcher.InvokeAsync(() =>
            calls.Add((1, Environment.CurrentManagedThreadId))
        );
        var second = host.Dispatcher.InvokeAsync(() =>
            calls.Add((2, Environment.CurrentManagedThreadId))
        );
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        calls.Select(call => call.Value).Should().Equal(1, 2);
        calls.Should().OnlyContain(call => call.ThreadId == host.ThreadId);
    }

    [TestMethod]
    public async Task InvokeAsync_PropagatesActionFailureAndContinuesDispatching()
    {
        using var host = new DispatcherHost();
        var failing = host.Dispatcher.InvokeAsync(() =>
            throw new InvalidOperationException("failed")
        );
        var continued = false;
        var next = host.Dispatcher.InvokeAsync(() => continued = true);

        await failing.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>();
        await next.WaitAsync(TimeSpan.FromSeconds(5));

        continued.Should().BeTrue();
    }

    private sealed class DispatcherHost : IDisposable
    {
        private readonly Thread _thread;
        private Win32MessageLoop? _messageLoop;
        private Exception? _failure;

        internal DispatcherHost()
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                try
                {
                    using var window = new MessageOnlyWindow();
                    using var dispatcher = new Win32ThreadDispatcher(window);
                    Dispatcher = dispatcher;
                    _messageLoop = new Win32MessageLoop(window);
                    ThreadId = Environment.CurrentManagedThreadId;
                    ready.Set();
                    _messageLoop.Run();
                }
                catch (Exception exception)
                {
                    _failure = exception;
                    ready.Set();
                }
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait();
            if (_failure is not null)
                throw new InvalidOperationException("Dispatcher host failed to start.", _failure);
        }

        internal Win32ThreadDispatcher Dispatcher { get; private set; } = null!;
        internal int ThreadId { get; private set; }

        public void Dispose()
        {
            _messageLoop?.RequestStop();
            _thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            _failure.Should().BeNull();
        }
    }
}
