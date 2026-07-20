// 在创建原生窗口的线程上运行 Win32 消息循环。
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.Messaging;

/// <summary>
/// 在 <see cref="NativeHostWindow"/> 所属线程上运行对应的消息循环。
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed unsafe class Win32MessageLoop(NativeHostWindow messageWindow)
{
    private const uint StopMessage = 0x8001;
    private readonly NativeHostWindow _messageWindow = messageWindow;

    public void Run()
    {
        _messageWindow.VerifyAccess();

        while (true)
        {
            var result = PInvoke.GetMessage(out var message, HWND.Null, 0, 0);
            if (result.Value == -1)
                throw Win32ExceptionFactory.Create(nameof(PInvoke.GetMessage));
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
                throw Win32ExceptionFactory.Create(nameof(PInvoke.PostMessage));
        }
    }
}
