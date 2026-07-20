// 校验既作为显示名称又作为 Windows 文件名的宏名称。
namespace WinUIMacro.Engine.Storage;

/// <summary>校验同时作为 Windows 文件名的宏名称。</summary>
internal static class MacroNameValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    };

    /// <summary>校验并去除宏名称首尾空白。</summary>
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 120)
            throw new ArgumentException("宏名不能超过 120 个字符。", nameof(name));
        if (normalized.EndsWith('.') || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("宏名包含 Windows 文件名不支持的字符。", nameof(name));
        var deviceName = normalized.Split('.')[0];
        if (ReservedNames.Contains(deviceName))
            throw new ArgumentException("宏名不能使用 Windows 保留设备名。", nameof(name));
        return normalized;
    }
}
