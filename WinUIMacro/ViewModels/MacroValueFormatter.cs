namespace WinUIMacro.ViewModels;

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
