using WinUIMacro.Engine.Win32.Coordinates;

namespace WinUIMacro.Engine.Recording;

/// <summary>Represents a physical-pixel rectangle on the virtual desktop.</summary>
public readonly record struct DesktopRectangle(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Returns whether a desktop point is inside the rectangle.</summary>
    public bool Contains(DesktopPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}
