// 表示物理像素单位的虚拟桌面坐标点，允许出现负坐标。
namespace WinUIMacro.Engine.Win32.Coordinates;

/// <summary>
/// 表示虚拟桌面中的物理像素坐标点。
/// 位于主显示器上方或左侧的显示器可能产生负坐标。
/// </summary>
/// <param name="X">桌面横坐标。</param>
/// <param name="Y">桌面纵坐标。</param>
internal readonly record struct DesktopPoint(int X, int Y);
