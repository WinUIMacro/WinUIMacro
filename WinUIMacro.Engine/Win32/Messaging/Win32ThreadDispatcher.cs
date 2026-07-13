using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.Messaging;

/// <summary>Posts callbacks to the engine message thread.</summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class Win32ThreadDispatcher : IDisposable
{
    private const uint DispatchMessage = 0x8002;

    private readonly LinkedList<WorkItem> _queue = new();
    private readonly Lock _sync = new();
    private readonly MessageOnlyWindow _window;
    private bool _disposed;

    public Win32ThreadDispatcher(MessageOnlyWindow window)
    {
        _window = window;
        window.MessageReceived += OnMessageReceived;
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var item = new WorkItem(action);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var node = _queue.AddLast(item);
            try
            {
                PostDispatchMessage();
            }
            catch (Exception exception)
            {
                _queue.Remove(node);
                item.Completion.SetException(exception);
            }
        }
        return item.Completion.Task;
    }

    public void Dispose()
    {
        _window.VerifyAccess();
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _window.MessageReceived -= OnMessageReceived;
            var exception = new ObjectDisposedException(nameof(Win32ThreadDispatcher));
            foreach (var item in _queue)
                item.Completion.SetException(exception);
            _queue.Clear();
        }
    }

    private void PostDispatchMessage()
    {
        var handle = _window.Handle;
        if (
            handle == 0
            || !PInvoke.PostMessage(new HWND(handle), DispatchMessage, default, default)
        )
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "PostMessage failed while dispatching engine work."
            );
    }

    private void OnMessageReceived(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message != DispatchMessage)
            return;

        WorkItem? item = null;
        lock (_sync)
        {
            if (_queue.First is { } node)
            {
                _queue.RemoveFirst();
                item = node.Value;
            }
        }

        if (item is null)
            return;

        try
        {
            item.Action();
            item.Completion.SetResult();
        }
        catch (Exception exception)
        {
            item.Completion.SetException(exception);
        }
    }

    private sealed record WorkItem(Action Action)
    {
        internal TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
