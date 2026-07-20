// 集中计算宏 JSON、绑定 TOML 和临时文件所在的工作区路径。
namespace WinUIMacro.Engine.Storage;

/// <summary>提供便携式宏工作区所需的路径。</summary>
internal sealed class MacroDataPaths(string? baseDirectory = null)
{
    /// <summary>获取宏 JSON 文件所在目录。</summary>
    public string MacroDirectory { get; } =
        Path.Combine(
            baseDirectory
                ?? Path.GetDirectoryName(Environment.ProcessPath)
                ?? AppContext.BaseDirectory,
            "Data",
            "Macro"
        );

    /// <summary>获取触发键绑定 TOML 文件路径。</summary>
    public string BindingFilePath => Path.Combine(MacroDirectory, "bindings.toml");
}
