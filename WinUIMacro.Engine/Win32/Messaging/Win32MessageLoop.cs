using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.Messaging;

/// <summary>
/// Runs the message loop for one <see cref="MessageOnlyWindow"/> on its owner thread.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed unsafe class Win32MessageLoop(MessageOnlyWindow messageWindow)
{
    private const uint StopMessage = 0x8001;
    private readonly MessageOnlyWindow _messageWindow = messageWindow;

    public void Run()
    {
        _messageWindow.VerifyAccess();

        while (true)
        {
            var result = PInvoke.GetMessage(out var message, HWND.Null, 0, 0);
            if (result.Value == -1)
                throw CreateLastWin32Exception(nameof(PInvoke.GetMessage));
            if (result.Value == 0)
                return;
            if (message.message == StopMessage && (nint)message.hwnd.Value == _messageWindow.Handle)
                return;

            _ = PInvoke.DispatchMessage(in message);
        }
    }

    public void RequestStop()
    {
        var handle = _messageWindow.Handle;
        if (handle == 0)
            return;

        if (!PInvoke.PostMessage(new HWND(handle), StopMessage, default, default))
        {
            if (_messageWindow.Handle != 0)
                throw CreateLastWin32Exception(nameof(PInvoke.PostMessage));
        }
    }

    private static Win32Exception CreateLastWin32Exception(string apiName) =>
        new(Marshal.GetLastPInvokeError(), string.Concat(apiName, " failed."));
}
