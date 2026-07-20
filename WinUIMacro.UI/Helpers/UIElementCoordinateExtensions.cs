// 将 WinUI 元素坐标换算为物理像素虚拟桌面坐标。
using Windows.Foundation;

namespace WinUIMacro.UI.Helpers;

/// <summary>将 WinUI 元素局部坐标转换为物理像素虚拟桌面坐标。</summary>
internal static class UIElementCoordinateExtensions
{
    /// <summary>将已连接到 XAML 根的元素内部点转换为物理桌面坐标。</summary>
    /// <param name="element">拥有局部坐标系的元素。</param>
    /// <param name="localPoint">相对于元素的坐标点。</param>
    /// <returns>对应的物理像素桌面坐标点。</returns>
    /// <exception cref="ArgumentNullException">元素为 <see langword="null"/> 时引发。</exception>
    /// <exception cref="InvalidOperationException">元素尚未连接到 XAML 根时引发。</exception>
    public static Point ToDesktopPoint(this UIElement element, Point localPoint)
    {
        ArgumentNullException.ThrowIfNull(element);

        var xamlRoot =
            element.XamlRoot
            ?? throw new InvalidOperationException(
                "The element must be connected to a XAML root before coordinates are converted."
            );
        var rootPoint = element.TransformToVisual(null).TransformPoint(localPoint);
        var screenPoint = xamlRoot.CoordinateConverter.ConvertLocalToScreen(rootPoint);
        return new Point(screenPoint.X, screenPoint.Y);
    }
}
