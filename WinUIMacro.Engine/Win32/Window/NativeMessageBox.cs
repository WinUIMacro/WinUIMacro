// 在 UI 窗口尚未创建时显示进程级原生错误提示。
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinUIMacro.Engine.Win32.Window;

/// <summary>在 UI 窗口可用前显示进程级原生错误消息。</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static class NativeMessageBox
{
    /// <summary>显示没有所有者窗口的错误消息框。</summary>
    /// <param name="message">消息文本。</param>
    /// <param name="title">消息框标题。</param>
    public static void ShowError(string message, string title) =>
        _ = PInvoke.MessageBox(HWND.Null, message, title, MESSAGEBOX_STYLE.MB_ICONERROR);
}
