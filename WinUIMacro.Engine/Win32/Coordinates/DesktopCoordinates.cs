// 提供物理像素虚拟桌面坐标与当前鼠标位置的 Win32 访问。
using System.Runtime.Versioning;
using Windows.Win32;

namespace WinUIMacro.Engine.Win32.Coordinates;

/// <summary>
/// 提供物理像素虚拟桌面坐标辅助方法。
/// </summary>
[SupportedOSPlatform("windows5.0")]
internal static class DesktopCoordinates
{
    /// <summary>获取当前鼠标指针在虚拟桌面中的坐标。</summary>
    /// <returns>当前物理像素鼠标坐标。</returns>
    /// <exception cref="Win32Exception">Windows 无法读取鼠标位置时引发。</exception>
    public static DesktopPoint GetCursorPosition()
    {
        if (!PInvoke.GetCursorPos(out var point))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetCursorPos));

        return new DesktopPoint(point.X, point.Y);
    }
}
