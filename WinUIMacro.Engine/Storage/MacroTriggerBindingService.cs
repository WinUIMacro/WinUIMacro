using Tomlyn;
using Tomlyn.Serialization;
using WinUIMacro.Engine.Models;

namespace WinUIMacro.Engine.Storage;

/// <summary>Loads and atomically saves trigger bindings using UUID references.</summary>
public sealed class MacroTriggerBindingService(MacroDataPaths paths)
{
    private readonly MacroDataPaths _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>Loads valid trigger bindings and reports invalid entries without changing the file.</summary>
    public async Task<(
        IReadOnlyList<MacroTriggerBinding> Bindings,
        IReadOnlyList<string> Errors
    )> LoadAsync(IReadOnlySet<Guid> macroIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macroIds);
        if (!File.Exists(_paths.BindingFilePath))
            return ([], []);
        try
        {
            var text = await File.ReadAllTextAsync(_paths.BindingFilePath, cancellationToken)
                .ConfigureAwait(false);
            var config =
                TomlSerializer.Deserialize<MacroTriggerBindingsDocument>(text)
                ?? throw new InvalidDataException("bindings.toml 中没有绑定数据。");
            List<MacroTriggerBinding> bindings = [];
            List<string> errors = [];
            HashSet<string> triggers = new(StringComparer.Ordinal);
            foreach (var document in config.Bindings)
            {
                var binding = new MacroTriggerBinding(document.Trigger, document.MacroId);
                if (!macroIds.Contains(binding.MacroId))
                    errors.Add($"触发键 {binding.Trigger} 引用了不存在的宏 {binding.MacroId}。");
                else if (!IsSupportedTrigger(binding.Trigger))
                    errors.Add($"触发键 {binding.Trigger} 不受支持。");
                else if (!triggers.Add(binding.Trigger))
                    errors.Add($"触发键 {binding.Trigger} 重复绑定。");
                else
                    bindings.Add(binding);
            }
            return (bindings, errors);
        }
        catch (Exception exception) when (exception is TomlException or InvalidDataException)
        {
            return ([], [$"bindings.toml：{exception.Message}"]);
        }
    }

    /// <summary>Saves all trigger bindings atomically.</summary>
    public async Task SaveAsync(
        IReadOnlyList<MacroTriggerBinding> bindings,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Directory.CreateDirectory(_paths.MacroDirectory);
        if (
            bindings.Select(binding => binding.Trigger).Distinct(StringComparer.Ordinal).Count()
            != bindings.Count
        )
            throw new InvalidDataException("一个触发键只能绑定一个宏。");
        var text = TomlSerializer.Serialize(
            new MacroTriggerBindingsDocument
            {
                Bindings = [.. bindings.Select(MacroTriggerBindingDocument.FromModel)],
            }
        );
        var temporaryPath = string.Concat(
            _paths.BindingFilePath,
            ".",
            Guid.NewGuid().ToString("N"),
            ".tmp"
        );
        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _paths.BindingFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsSupportedTrigger(string value) =>
        MacroInputRegistry.KeyboardTriggerValues.Contains(value, StringComparer.Ordinal)
        || MacroInputRegistry.MouseTriggerValues.Contains(value, StringComparer.Ordinal);

    private sealed class MacroTriggerBindingsDocument
    {
        [TomlPropertyName("bindings")]
        [TomlTableArrayStyle(TomlTableArrayStyle.Headers)]
        public List<MacroTriggerBindingDocument> Bindings { get; set; } = [];
    }

    private sealed class MacroTriggerBindingDocument
    {
        [TomlPropertyName("trigger")]
        public string Trigger { get; set; } = string.Empty;

        [TomlPropertyName("id")]
        public Guid MacroId { get; set; }

        internal static MacroTriggerBindingDocument FromModel(MacroTriggerBinding binding) =>
            new() { Trigger = binding.Trigger, MacroId = binding.MacroId };
    }
}
