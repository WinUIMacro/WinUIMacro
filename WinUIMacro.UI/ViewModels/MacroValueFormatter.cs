// 将宏节点的内部值转换为面向用户的简短显示文本。
namespace WinUIMacro.UI.ViewModels;

internal static class MacroValueFormatter
{
    internal static string FormatDuration(long milliseconds) =>
        milliseconds switch
        {
            < 1000 => $"{milliseconds} ms",
            < 60_000 => $"{milliseconds / 1000d:0.##} s",
            _ => $"{milliseconds / 60_000} min {milliseconds % 60_000 / 1000} s",
        };
}
