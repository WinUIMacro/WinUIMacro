using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;

namespace WinUIMacro.Engine.Win32.Coordinates;

/// <summary>
/// Provides physical-pixel virtual desktop coordinate helpers.
/// </summary>
[SupportedOSPlatform("windows5.0")]
public static class DesktopCoordinates
{
    /// <summary>
    /// Gets the current mouse cursor position in virtual desktop coordinates.
    /// </summary>
    /// <returns>The current physical-pixel cursor position.</returns>
    /// <exception cref="Win32Exception">Thrown when Windows cannot read the cursor position.</exception>
    public static DesktopPoint GetCursorPosition()
    {
        if (!PInvoke.GetCursorPos(out var point))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, "GetCursorPos failed.");
        }

        return new DesktopPoint(point.X, point.Y);
    }
}
