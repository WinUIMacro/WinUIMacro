namespace WinUIMacro.Engine.Win32.Input;

/// <summary>
/// Represents one keyboard or mouse operation that can be recorded or injected.
/// </summary>
public abstract record InputOperation;

/// <summary>
/// Represents one scan-code-based keyboard operation.
/// </summary>
/// <param name="IsPress">Whether the key is pressed; otherwise it is released.</param>
/// <param name="ScanCode">The hardware scan code without an E0 prefix.</param>
/// <param name="IsExtended">Whether the scan code has an E0 prefix.</param>
public sealed record KeyboardOperation(bool IsPress, ushort ScanCode, bool IsExtended)
    : InputOperation;

/// <summary>
/// Represents one mouse button operation.
/// </summary>
/// <param name="IsPress">Whether the button is pressed; otherwise it is released.</param>
/// <param name="Button">The affected mouse button.</param>
public sealed record MouseButtonOperation(bool IsPress, MouseButton Button) : InputOperation;

/// <summary>
/// Represents one vertical mouse wheel operation.
/// </summary>
/// <param name="Direction">The logical wheel direction.</param>
public sealed record MouseWheelOperation(MouseWheelDirection Direction) : InputOperation;

/// <summary>
/// Identifies a standard mouse button supported by Raw Input and SendInput.
/// </summary>
public enum MouseButton : byte
{
    /// <summary>The primary mouse button.</summary>
    Left,

    /// <summary>The secondary mouse button.</summary>
    Right,

    /// <summary>The middle mouse button.</summary>
    Middle,

    /// <summary>The first extended mouse button.</summary>
    X1,

    /// <summary>The second extended mouse button.</summary>
    X2,
}

/// <summary>
/// Identifies a vertical mouse wheel direction.
/// </summary>
public enum MouseWheelDirection : sbyte
{
    /// <summary>The wheel is rotated toward the user.</summary>
    Down = -1,

    /// <summary>The wheel is rotated away from the user.</summary>
    Up = 1,
}
