// 将回调排队投递到引擎拥有的 Win32 消息线程。
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.Messaging;

/// <summary>将回调投递到引擎消息线程。</summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed class Win32ThreadDispatcher : IDisposable
{
    private const uint DispatchMessage = 0x8002;

    private readonly LinkedList<WorkItem> _queue = new();
    private readonly Lock _sync = new();
    private readonly NativeHostWindow _window;
    private bool _disposed;

    /// <summary>创建向指定宿主窗口线程投递工作的调度器。</summary>
    public Win32ThreadDispatcher(NativeHostWindow window)
    {
        _window = window;
        window.MessageReceived += OnMessageReceived;
    }

    /// <summary>将操作投递到宿主窗口线程并异步等待执行结果。</summary>
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

    /// <summary>停止接收工作并使尚未执行的调用失败。</summary>
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
            throw Win32ExceptionFactory.Create(nameof(PInvoke.PostMessage));
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
        /// <summary>获取调用完成、失败或取消时更新的任务源。</summary>
        internal TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
