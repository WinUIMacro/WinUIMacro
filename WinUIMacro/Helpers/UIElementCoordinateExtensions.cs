using Windows.Foundation;
using WinUIMacro.Engine.Win32.Coordinates;

namespace WinUIMacro.Helpers;

/// <summary>
/// Converts WinUI element-local coordinates to physical virtual desktop coordinates.
/// </summary>
public static class UIElementCoordinateExtensions
{
    /// <summary>
    /// Converts a point inside a connected WinUI element to physical desktop coordinates.
    /// </summary>
    /// <param name="element">The element that owns the local coordinate space.</param>
    /// <param name="localPoint">The point relative to the element.</param>
    /// <returns>The corresponding physical-pixel desktop point.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element" /> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the element is not connected to a XAML root.</exception>
    public static DesktopPoint ToDesktopPoint(this UIElement element, Point localPoint)
    {
        ArgumentNullException.ThrowIfNull(element);

        var xamlRoot =
            element.XamlRoot
            ?? throw new InvalidOperationException(
                "The element must be connected to a XAML root before coordinates are converted."
            );
        var rootPoint = element.TransformToVisual(null).TransformPoint(localPoint);
        var screenPoint = xamlRoot.CoordinateConverter.ConvertLocalToScreen(rootPoint);
        return new DesktopPoint(screenPoint.X, screenPoint.Y);
    }
}
