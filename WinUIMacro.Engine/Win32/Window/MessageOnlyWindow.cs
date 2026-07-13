using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinUIMacro.Engine.Win32.Window;

/// <summary>
/// Owns the process-wide message-only window on the constructing thread. Only one active instance
/// may exist in the process, and it must be disposed on that same thread.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed unsafe class MessageOnlyWindow : IDisposable
{
    private const uint WindowMessageNcDestroy = 0x0082;
    private const string WindowClassName = "WinUIMacro.MessageOnlyWindow";

    private readonly WNDPROC _windowProcedure;
    private readonly Thread _ownerThread;
    private nint _handle;
    private HINSTANCE _instanceHandle;
    private bool _disposed;

    public MessageOnlyWindow()
    {
        _windowProcedure = WindowProcedure;
        _ownerThread = Thread.CurrentThread;
        _instanceHandle = RegisterWindowClass();

        try
        {
            Volatile.Write(ref _handle, (nint)CreateNativeWindow().Value);
        }
        catch
        {
            UnregisterWindowClass();
            throw;
        }
    }

    public nint Handle => Volatile.Read(ref _handle);

    public event Action<Exception>? MessageDispatchFailed;

    internal event WindowMessageReceivedHandler? MessageReceived;

    public void Dispose()
    {
        if (_disposed)
            return;

        VerifyAccess();
        var handle = Handle;
        if (handle != 0 && !PInvoke.DestroyWindow(new HWND(handle)))
            throw CreateLastWin32Exception(nameof(PInvoke.DestroyWindow));

        UnregisterWindowClass();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal void VerifyAccess()
    {
        if (!ReferenceEquals(Thread.CurrentThread, _ownerThread))
            throw new InvalidOperationException(
                "The message-only window must be accessed from the thread that created it."
            );
    }

    private HINSTANCE RegisterWindowClass()
    {
        var moduleHandle = PInvoke.GetModuleHandle(default(PCWSTR));
        if (moduleHandle.IsNull)
            throw CreateLastWin32Exception(nameof(PInvoke.GetModuleHandle));

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
                throw CreateLastWin32Exception(nameof(PInvoke.RegisterClass));
        }

        return instanceHandle;
    }

    private HWND CreateNativeWindow()
    {
        fixed (char* className = WindowClassName)
        {
            var window = PInvoke.CreateWindowEx(
                default,
                className,
                className,
                default,
                0,
                0,
                0,
                0,
                HWND.HWND_MESSAGE,
                HMENU.Null,
                _instanceHandle,
                null
            );

            if (window.IsNull)
                throw CreateLastWin32Exception(nameof(PInvoke.CreateWindowEx));

            return window;
        }
    }

    private void UnregisterWindowClass()
    {
        fixed (char* className = WindowClassName)
        {
            if (!PInvoke.UnregisterClass(className, _instanceHandle))
                throw CreateLastWin32Exception(nameof(PInvoke.UnregisterClass));
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
                // Managed exceptions must not cross the native window procedure.
            }
        }

        var result = PInvoke.DefWindowProc(window, message, wParam, lParam);
        if (message == WindowMessageNcDestroy)
            Volatile.Write(ref _handle, 0);
        return result;
    }

    private static Win32Exception CreateLastWin32Exception(string apiName) =>
        new(Marshal.GetLastPInvokeError(), string.Concat(apiName, " failed."));
}

internal delegate void WindowMessageReceivedHandler(
    HWND window,
    uint message,
    WPARAM wParam,
    LPARAM lParam
);
