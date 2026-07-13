using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Models;

/// <summary>Maps Raw Input operations to stable persisted values and back.</summary>
public static class MacroInputRegistry
{
    private static readonly KeyboardDefinition[] KeyboardDefinitions = [.. CreateKeyboardEntries()];

    private static readonly FrozenDictionary<
        (ushort ScanCode, bool Extended),
        string
    > KeyboardValues = KeyboardDefinitions.ToFrozenDictionary(
        entry => (entry.ScanCode, entry.Extended),
        entry => entry.Value
    );

    private static readonly FrozenDictionary<string, (ushort ScanCode, bool Extended)> Keyboards =
        KeyboardDefinitions.ToFrozenDictionary(
            entry => entry.Value,
            entry => (entry.ScanCode, entry.Extended),
            StringComparer.Ordinal
        );

    private static readonly FrozenDictionary<string, string> DisplayTexts = KeyboardDefinitions
        .Select(entry => KeyValuePair.Create(entry.Value, entry.DisplayText))
        .Concat(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MouseLeft"] = "左键",
                ["MouseRight"] = "右键",
                ["MouseMiddle"] = "中键",
                ["MouseX1"] = "侧键1",
                ["MouseX2"] = "侧键2",
                ["WheelUp"] = "滚轮↑",
                ["WheelDown"] = "滚轮↓",
            }
        )
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Gets the keyboard values that may be used as trigger bindings.</summary>
    public static IReadOnlyList<string> KeyboardTriggerValues { get; } =
    [
        .. KeyboardDefinitions
            .Select(entry => entry.Value)
            .Where(value => value is not ("Menu" or "NumLk")),
    ];

    /// <summary>Gets the mouse values that may be used as trigger bindings.</summary>
    public static IReadOnlyList<string> MouseTriggerValues { get; } = ["MouseX1", "MouseX2"];

    /// <summary>Gets the canonical short display text for a supported persisted input value.</summary>
    public static string GetDisplayText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return DisplayTexts.TryGetValue(value, out var displayText)
            ? displayText
            : throw new ArgumentException(
                $"Unsupported macro input value '{value}'.",
                nameof(value)
            );
    }

    /// <summary>Converts an input operation to a persisted node.</summary>
    public static bool TryCreateNode(
        InputOperation operation,
        [NotNullWhen(true)] out MacroNode? node
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        switch (operation)
        {
            case KeyboardOperation keyboard
                when KeyboardValues.TryGetValue(
                    (keyboard.ScanCode, keyboard.IsExtended),
                    out var key
                ):
                node = new MacroNode(
                    keyboard.IsPress ? MacroNodeType.KeyDown : MacroNodeType.KeyUp,
                    key
                );
                return true;
            case MouseButtonOperation mouse:
                node = new MacroNode(
                    mouse.IsPress ? MacroNodeType.MouseDown : MacroNodeType.MouseUp,
                    ToMouseValue(mouse.Button)
                );
                return true;
            case MouseWheelOperation wheel:
                node = new MacroNode(
                    MacroNodeType.MouseWheel,
                    wheel.Direction == MouseWheelDirection.Up ? "WheelUp" : "WheelDown"
                );
                return true;
            default:
                node = null;
                return false;
        }
    }

    /// <summary>Converts a persisted node to an injectable operation.</summary>
    public static bool TryCreateOperation(
        MacroNode node,
        [NotNullWhen(true)] out InputOperation? operation
    )
    {
        ArgumentNullException.ThrowIfNull(node);
        if (
            node.Type is MacroNodeType.KeyDown or MacroNodeType.KeyUp
            && Keyboards.TryGetValue(node.Value, out var keyboard)
        )
        {
            operation = new KeyboardOperation(
                node.Type == MacroNodeType.KeyDown,
                keyboard.ScanCode,
                keyboard.Extended
            );
            return true;
        }

        if (
            node.Type is MacroNodeType.MouseDown or MacroNodeType.MouseUp
            && TryParseMouseValue(node.Value, out var button)
        )
        {
            operation = new MouseButtonOperation(node.Type == MacroNodeType.MouseDown, button);
            return true;
        }

        if (node.Type == MacroNodeType.MouseWheel && node.Value is "WheelUp" or "WheelDown")
        {
            operation = new MouseWheelOperation(
                node.Value == "WheelUp" ? MouseWheelDirection.Up : MouseWheelDirection.Down
            );
            return true;
        }

        operation = null;
        return false;
    }

    /// <summary>Gets the stable trigger value represented by a press or release operation.</summary>
    public static bool TryGetTrigger(
        InputOperation operation,
        [NotNullWhen(true)] out string? value,
        out bool isPress
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        switch (operation)
        {
            case KeyboardOperation keyboard
                when KeyboardValues.TryGetValue(
                    (keyboard.ScanCode, keyboard.IsExtended),
                    out value
                ):
                isPress = keyboard.IsPress;
                return true;
            case MouseButtonOperation { Button: MouseButton.X1 or MouseButton.X2 } mouse:
                value = ToMouseValue(mouse.Button);
                isPress = mouse.IsPress;
                return true;
            default:
                value = null;
                isPress = false;
                return false;
        }
    }

    private static string ToMouseValue(MouseButton button) =>
        button switch
        {
            MouseButton.Left => "MouseLeft",
            MouseButton.Right => "MouseRight",
            MouseButton.Middle => "MouseMiddle",
            MouseButton.X1 => "MouseX1",
            MouseButton.X2 => "MouseX2",
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

    private static bool TryParseMouseValue(string value, out MouseButton button)
    {
        button = value switch
        {
            "MouseLeft" => MouseButton.Left,
            "MouseRight" => MouseButton.Right,
            "MouseMiddle" => MouseButton.Middle,
            "MouseX1" => MouseButton.X1,
            "MouseX2" => MouseButton.X2,
            _ => (MouseButton)byte.MaxValue,
        };
        return button != (MouseButton)byte.MaxValue;
    }

    private static IEnumerable<KeyboardDefinition> CreateKeyboardEntries()
    {
        yield return Entry("Esc", 0x01);
        for (ushort index = 0; index < 10; index++)
        {
            var value = ((index + 1) % 10).ToString(CultureInfo.InvariantCulture);
            yield return Entry(value, (ushort)(0x02 + index));
        }
        string[] firstRow = ["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"];
        for (ushort index = 0; index < firstRow.Length; index++)
            yield return Entry(firstRow[index], (ushort)(0x10 + index));
        string[] secondRow = ["A", "S", "D", "F", "G", "H", "J", "K", "L"];
        for (ushort index = 0; index < secondRow.Length; index++)
            yield return Entry(secondRow[index], (ushort)(0x1E + index));
        string[] thirdRow = ["Z", "X", "C", "V", "B", "N", "M"];
        for (ushort index = 0; index < thirdRow.Length; index++)
            yield return Entry(thirdRow[index], (ushort)(0x2C + index));
        yield return Entry("F1", 0x3B);
        yield return Entry("F2", 0x3C);
        yield return Entry("F3", 0x3D);
        yield return Entry("F4", 0x3E);
        yield return Entry("F5", 0x3F);
        yield return Entry("F6", 0x40);
        yield return Entry("F7", 0x41);
        yield return Entry("F8", 0x42);
        yield return Entry("F9", 0x43);
        yield return Entry("F10", 0x44);
        yield return Entry("F11", 0x57);
        yield return Entry("F12", 0x58);
        yield return Entry("Tab", 0x0F);
        yield return Entry("Enter", 0x1C);
        yield return Entry("Space", 0x39);
        yield return Entry("Backspace", 0x0E, displayText: "Bksp");
        yield return Entry("LCtrl", 0x1D);
        yield return Entry("RCtrl", 0x1D, extended: true);
        yield return Entry("LShift", 0x2A);
        yield return Entry("RShift", 0x36);
        yield return Entry("LAlt", 0x38);
        yield return Entry("RAlt", 0x38, extended: true);
        yield return Entry("LWin", 0x5B, extended: true);
        yield return Entry("RWin", 0x5C, extended: true);
        yield return Entry("Insert", 0x52, extended: true, displayText: "Ins");
        yield return Entry("Delete", 0x53, extended: true, displayText: "Del");
        yield return Entry("Home", 0x47, extended: true);
        yield return Entry("End", 0x4F, extended: true);
        yield return Entry("PageUp", 0x49, extended: true, displayText: "PgUp");
        yield return Entry("PageDown", 0x51, extended: true, displayText: "PgDn");
        yield return Entry("↑", 0x48, extended: true);
        yield return Entry("↓", 0x50, extended: true);
        yield return Entry("←", 0x4B, extended: true);
        yield return Entry("→", 0x4D, extended: true);
        yield return Entry("PrtSc", 0x37, extended: true);
        yield return Entry("ScrLk", 0x46);
        yield return Entry("-", 0x0C);
        yield return Entry("=", 0x0D);
        yield return Entry("[", 0x1A);
        yield return Entry("]", 0x1B);
        yield return Entry("\\", 0x2B);
        yield return Entry(";", 0x27);
        yield return Entry("'", 0x28);
        yield return Entry(",", 0x33);
        yield return Entry(".", 0x34);
        yield return Entry("/", 0x35);
        yield return Entry("`", 0x29);
        yield return Entry("CapsLock", 0x3A, displayText: "Caps");
        yield return Entry("Menu", 0x5D, extended: true);
        yield return Entry("NumLk", 0x45);
        yield return Entry("Num/", 0x35, extended: true);
        yield return Entry("Num*", 0x37);
        yield return Entry("Num-", 0x4A);
        yield return Entry("Num+", 0x4E);
        yield return Entry("Num7", 0x47);
        yield return Entry("Num8", 0x48);
        yield return Entry("Num9", 0x49);
        yield return Entry("Num4", 0x4B);
        yield return Entry("Num5", 0x4C);
        yield return Entry("Num6", 0x4D);
        yield return Entry("Num1", 0x4F);
        yield return Entry("Num2", 0x50);
        yield return Entry("Num3", 0x51);
        yield return Entry("Num0", 0x52);
        yield return Entry("Num.", 0x53);
        yield return Entry("NumEnt", 0x1C, extended: true);
    }

    private static KeyboardDefinition Entry(
        string value,
        ushort scanCode,
        bool extended = false,
        string? displayText = null
    ) => new(value, displayText ?? value, scanCode, extended);

    private readonly record struct KeyboardDefinition(
        string Value,
        string DisplayText,
        ushort ScanCode,
        bool Extended
    );
}
