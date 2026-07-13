namespace WinUIMacro.Engine.Storage;

/// <summary>Provides the portable macro workspace paths.</summary>
public sealed class MacroDataPaths(string? baseDirectory = null)
{
    /// <summary>Gets the directory containing macro JSON files.</summary>
    public string MacroDirectory { get; } =
        Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Data", "Macro");

    /// <summary>Gets the trigger-binding TOML path.</summary>
    public string BindingFilePath => Path.Combine(MacroDirectory, "bindings.toml");
}
