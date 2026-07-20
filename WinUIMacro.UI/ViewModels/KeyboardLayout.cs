// 描述绑定页面使用的键盘网格行、列和跨度。
namespace WinUIMacro.UI.ViewModels;

internal static class KeyboardLayout
{
    internal const int ColumnCount = 18;

    internal readonly record struct Key(string Value, int Column, int Span = 1);

    internal static IReadOnlyList<IReadOnlyList<Key>> Rows { get; } =
    [
        Create(
            "Esc",
            "F1",
            "F2",
            "F3",
            "F4",
            "F5",
            "F6",
            "F7",
            "F8",
            "F9",
            "F10",
            "F11",
            "F12",
            "Delete",
            "Home",
            "End",
            "PageUp",
            "PageDown"
        ),
        Create(
            "`",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "0",
            "-",
            "=",
            "Backspace",
            "Num7",
            "Num8",
            "Num9",
            "Num/"
        ),
        Create(
            "Tab",
            "Q",
            "W",
            "E",
            "R",
            "T",
            "Y",
            "U",
            "I",
            "O",
            "P",
            "[",
            "]",
            "\\",
            "Num4",
            "Num5",
            "Num6",
            "Num*"
        ),
        Create(
            "CapsLock",
            "A",
            "S",
            "D",
            "F",
            "G",
            "H",
            "J",
            "K",
            "L",
            ";",
            "'",
            "Enter",
            "Enter",
            "Num1",
            "Num2",
            "Num3",
            "Num-"
        ),
        Create(
            "LShift",
            "LShift",
            "Z",
            "X",
            "C",
            "V",
            "B",
            "N",
            "M",
            ",",
            ".",
            "/",
            "RShift",
            "↑",
            "Num0",
            "Num.",
            "NumEnt",
            "Num+"
        ),
        Create(
            "LCtrl",
            "LWin",
            "LAlt",
            "Space",
            "Space",
            "Space",
            "Space",
            "Space",
            "Space",
            "RAlt",
            "RWin",
            "RCtrl",
            "←",
            "↓",
            "→",
            "PrtSc",
            "ScrLk",
            "Insert"
        ),
    ];

    private static List<Key> Create(params string[] values)
    {
        if (values.Length != ColumnCount)
            throw new InvalidOperationException(
                $"Keyboard rows must contain exactly {ColumnCount} cells."
            );

        var result = new List<Key>();
        for (var column = 0; column < values.Length; column++)
        {
            // 连续重复值表示一个跨列按键，例如 Backspace、Enter 或 Space。
            var span = 1;
            while (column + span < values.Length && values[column + span] == values[column])
                span++;
            result.Add(new Key(values[column], column, span));
            column += span - 1;
        }
        return result;
    }
}
