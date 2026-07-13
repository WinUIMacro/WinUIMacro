using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Win32.RawInput;

internal sealed class RawInputTranslator
{
    internal const ushort KeyBreak = 0x0001;
    internal const ushort KeyE0 = 0x0002;
    internal const ushort KeyE1 = 0x0004;

    internal const ushort MouseLeftDown = 0x0001;
    internal const ushort MouseLeftUp = 0x0002;
    internal const ushort MouseRightDown = 0x0004;
    internal const ushort MouseRightUp = 0x0008;
    internal const ushort MouseMiddleDown = 0x0010;
    internal const ushort MouseMiddleUp = 0x0020;
    internal const ushort MouseX1Down = 0x0040;
    internal const ushort MouseX1Up = 0x0080;
    internal const ushort MouseX2Down = 0x0100;
    internal const ushort MouseX2Up = 0x0200;
    internal const ushort MouseWheel = 0x0400;

    private const int WheelDelta = 120;

    private readonly HashSet<KeyboardStateKey> _pressedKeys = [];
    private readonly Dictionary<nint, int> _wheelRemainders = [];

    internal bool TryTranslateKeyboard(
        nint device,
        ushort makeCode,
        ushort flags,
        out KeyboardOperation? operation
    )
    {
        operation = null;
        if (makeCode == 0 || (flags & KeyE1) != 0)
            return false;

        var isPress = (flags & KeyBreak) == 0;
        var key = new KeyboardStateKey(device, makeCode, (flags & KeyE0) != 0);

        if (isPress)
        {
            if (!_pressedKeys.Add(key))
                return false;
        }
        else if (!_pressedKeys.Remove(key))
        {
            return false;
        }

        operation = new KeyboardOperation(isPress, makeCode, key.IsExtended);
        return true;
    }

    internal void TranslateMouse(
        nint device,
        ushort buttonFlags,
        ushort buttonData,
        Action<InputOperation> publish
    )
    {
        var wheelSteps = UpdateWheelState(device, buttonFlags, buttonData);

        PublishMouseButton(buttonFlags, MouseLeftDown, true, MouseButton.Left, publish);
        PublishMouseButton(buttonFlags, MouseLeftUp, false, MouseButton.Left, publish);
        PublishMouseButton(buttonFlags, MouseRightDown, true, MouseButton.Right, publish);
        PublishMouseButton(buttonFlags, MouseRightUp, false, MouseButton.Right, publish);
        PublishMouseButton(buttonFlags, MouseMiddleDown, true, MouseButton.Middle, publish);
        PublishMouseButton(buttonFlags, MouseMiddleUp, false, MouseButton.Middle, publish);
        PublishMouseButton(buttonFlags, MouseX1Down, true, MouseButton.X1, publish);
        PublishMouseButton(buttonFlags, MouseX1Up, false, MouseButton.X1, publish);
        PublishMouseButton(buttonFlags, MouseX2Down, true, MouseButton.X2, publish);
        PublishMouseButton(buttonFlags, MouseX2Up, false, MouseButton.X2, publish);

        var direction = wheelSteps > 0 ? MouseWheelDirection.Up : MouseWheelDirection.Down;
        for (var count = Math.Abs(wheelSteps); count > 0; count--)
            publish(new MouseWheelOperation(direction));
    }

    internal void Reset()
    {
        _pressedKeys.Clear();
        _wheelRemainders.Clear();
    }

    private int UpdateWheelState(nint device, ushort buttonFlags, ushort buttonData)
    {
        if ((buttonFlags & MouseWheel) == 0)
            return 0;

        var delta = unchecked((short)buttonData);
        if (delta == 0)
            return 0;

        _wheelRemainders.TryGetValue(device, out var remainder);
        remainder += delta;
        var steps = remainder / WheelDelta;
        remainder %= WheelDelta;

        if (remainder == 0)
            _wheelRemainders.Remove(device);
        else
            _wheelRemainders[device] = remainder;

        return steps;
    }

    private static void PublishMouseButton(
        ushort buttonFlags,
        ushort expectedFlag,
        bool isPress,
        MouseButton button,
        Action<InputOperation> publish
    )
    {
        if ((buttonFlags & expectedFlag) != 0)
            publish(new MouseButtonOperation(isPress, button));
    }

    private readonly record struct KeyboardStateKey(nint Device, ushort ScanCode, bool IsExtended);
}
