using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace WinUIMacro.Engine.Win32.Input;

/// <summary>Injects keyboard and mouse operations through SendInput.</summary>
[SupportedOSPlatform("windows6.0.6000")]
public static unsafe class InputInjector
{
    private const int WheelDelta = 120;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    public static void Send(InputOperation operation)
    {
        var nativeInput = CreateNativeInput(operation);
        var sent = PInvoke.SendInput(1, &nativeInput, sizeof(INPUT));
        if (sent != 1)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, CreateFailureMessage(error, operation));
        }
    }

    private static string CreateFailureMessage(int error, InputOperation operation) =>
        $"{DateTimeOffset.Now:O} | {Describe(operation)} | Win32 error: {error}";

    private static string Describe(InputOperation operation) =>
        operation switch
        {
            KeyboardOperation keyboard =>
                $"Keyboard ScanCode=0x{keyboard.ScanCode:X4}, IsExtended={keyboard.IsExtended}, IsPress={keyboard.IsPress}",
            MouseButtonOperation mouse =>
                $"MouseButton Button={mouse.Button}, IsPress={mouse.IsPress}",
            MouseWheelOperation wheel => $"MouseWheel Direction={wheel.Direction}",
            _ => operation.GetType().Name,
        };

    internal static INPUT CreateNativeInput(InputOperation operation) =>
        operation switch
        {
            KeyboardOperation keyboard => CreateKeyboardInput(keyboard),
            MouseButtonOperation mouseButton => CreateMouseButtonInput(mouseButton),
            MouseWheelOperation mouseWheel => CreateMouseWheelInput(mouseWheel),
            _ => throw new ArgumentException(
                "The input operation type is not supported.",
                nameof(operation)
            ),
        };

    private static INPUT CreateKeyboardInput(KeyboardOperation operation)
    {
        if (operation.ScanCode == 0)
            throw new ArgumentException(
                "A keyboard operation must contain a non-zero scan code.",
                nameof(operation)
            );

        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE;
        if (operation.IsExtended)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        if (!operation.IsPress)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT { wScan = operation.ScanCode, dwFlags = flags },
            },
        };
    }

    private static INPUT CreateMouseButtonInput(MouseButtonOperation operation)
    {
        var flags = operation.Button switch
        {
            MouseButton.Left => operation.IsPress
                ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN
                : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
            MouseButton.Right => operation.IsPress
                ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN
                : MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => operation.IsPress
                ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN
                : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
            MouseButton.X1 or MouseButton.X2 => operation.IsPress
                ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN
                : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Button,
                "The mouse button is not supported."
            ),
        };

        var mouseData = operation.Button switch
        {
            MouseButton.X1 => XButton1,
            MouseButton.X2 => XButton2,
            _ => 0U,
        };

        return CreateMouseInput(flags, mouseData);
    }

    private static INPUT CreateMouseWheelInput(MouseWheelOperation operation)
    {
        var mouseData = operation.Direction switch
        {
            MouseWheelDirection.Up => (uint)WheelDelta,
            MouseWheelDirection.Down => unchecked((uint)-WheelDelta),
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Direction,
                "The mouse wheel direction is not supported."
            ),
        };

        return CreateMouseInput(MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, mouseData);
    }

    private static INPUT CreateMouseInput(MOUSE_EVENT_FLAGS flags, uint mouseData) =>
        new()
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT { mouseData = mouseData, dwFlags = flags },
            },
        };
}
