// 管理与创建线程绑定的隐藏原生窗口，并转发窗口消息。
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinUIMacro.Engine.Win32.Window;

/// <summary>
/// 在创建线程上拥有进程级隐藏原生窗口。
/// 进程中只能存在一个活动实例，并且必须在同一线程上释放。
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed unsafe class NativeHostWindow : IDisposable
{
    private const uint WindowMessageNcDestroy = 0x0082;
    private const string WindowClassName = "WinUIMacro.NativeHostWindow";

    private readonly WNDPROC _windowProcedure;
    private readonly Thread _ownerThread;
    private nint _handle;
    private HINSTANCE _instanceHandle;
    private bool _disposed;

    /// <summary>在当前线程注册窗口类并创建隐藏原生窗口。</summary>
    public NativeHostWindow()
    {
        _windowProcedure = WindowProcedure;
        _ownerThread = Thread.CurrentThread;
        _instanceHandle = RegisterWindowClass();

        try
        {
            // 窗口过程委托必须由实例持有，避免原生窗口仍在使用时被 GC 回收。
            Volatile.Write(ref _handle, (nint)CreateNativeWindow().Value);
        }
        catch
        {
            UnregisterWindowClass();
            throw;
        }
    }

    /// <summary>获取当前原生窗口句柄；窗口销毁后返回零。</summary>
    public nint Handle => Volatile.Read(ref _handle);

    /// <summary>窗口消息订阅者抛出异常时引发。</summary>
    public event Action<Exception>? MessageDispatchFailed;

    /// <summary>原生窗口收到消息时引发。</summary>
    internal event WindowMessageReceivedHandler? MessageReceived;

    /// <summary>销毁原生窗口并注销窗口类。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        VerifyAccess();
        var handle = Handle;
        if (handle != 0 && !PInvoke.DestroyWindow(new HWND(handle)))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.DestroyWindow));

        UnregisterWindowClass();
        _disposed = true;
    }

    /// <summary>验证当前调用线程是否为窗口创建线程。</summary>
    internal void VerifyAccess()
    {
        if (!ReferenceEquals(Thread.CurrentThread, _ownerThread))
            throw new InvalidOperationException(
                "The native host window must be accessed from the thread that created it."
            );
    }

    private HINSTANCE RegisterWindowClass()
    {
        var moduleHandle = PInvoke.GetModuleHandle(default(PCWSTR));
        if (moduleHandle.IsNull)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetModuleHandle));

        var instanceHandle = new HINSTANCE(moduleHandle.Value);
        fixed (char* className = WindowClassName)
        {
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = _windowProcedure,
                hInstance = instanceHandle,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClass(in windowClass) == 0)
                throw Win32ExceptionFactory.Create(nameof(PInvoke.RegisterClass));
        }

        return instanceHandle;
    }

    private HWND CreateNativeWindow()
    {
        fixed (char* className = WindowClassName)
        {
            var window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                className,
                default,
                0,
                0,
                0,
                0,
                HWND.Null,
                HMENU.Null,
                _instanceHandle,
                null
            );

            if (window.IsNull)
                throw Win32ExceptionFactory.Create(nameof(PInvoke.CreateWindowEx));

            return window;
        }
    }

    private void UnregisterWindowClass()
    {
        fixed (char* className = WindowClassName)
        {
            if (!PInvoke.UnregisterClass(className, _instanceHandle))
                throw Win32ExceptionFactory.Create(nameof(PInvoke.UnregisterClass));
        }

        _instanceHandle = HINSTANCE.Null;
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            MessageReceived?.Invoke(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            try
            {
                MessageDispatchFailed?.Invoke(exception);
            }
            catch
            {
                // 托管异常不能穿过原生窗口过程，否则会破坏 Win32 消息分发。
            }
        }

        var result = PInvoke.DefWindowProc(window, message, wParam, lParam);
        if (message == WindowMessageNcDestroy)
            // WM_NCDESTROY 表示原生句柄已失效，后续访问应立即看到 0。
            Volatile.Write(ref _handle, 0);
        return result;
    }
}

/// <summary>处理隐藏原生窗口接收的 Win32 消息。</summary>
internal delegate void WindowMessageReceivedHandler(
    HWND window,
    uint message,
    WPARAM wParam,
    LPARAM lParam
);
