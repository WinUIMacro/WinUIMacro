namespace WinUIMacro.Engine.Win32.Coordinates;

/// <summary>
/// Represents a physical-pixel point in virtual desktop coordinates.
/// Coordinates may be negative on monitors above or to the left of the primary monitor.
/// </summary>
/// <param name="X">The horizontal desktop coordinate.</param>
/// <param name="Y">The vertical desktop coordinate.</param>
public readonly record struct DesktopPoint(int X, int Y);
