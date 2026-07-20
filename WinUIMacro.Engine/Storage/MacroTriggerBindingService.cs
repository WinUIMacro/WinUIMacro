// 负责触发键绑定的 TOML 加载、校验、清理和原子保存。
using Tomlyn;
using Tomlyn.Serialization;
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine.Storage;

/// <summary>使用 UUID 引用加载并原子保存触发键绑定。</summary>
internal sealed class MacroTriggerBindingService(MacroDataPaths paths)
{
    private readonly MacroDataPaths _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>加载有效触发键绑定，并报告无效项而不修改原文件。</summary>
    public async Task<MacroTriggerBindingLoadResult> LoadAsync(
        IReadOnlySet<Guid> macroIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(macroIds);
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
            // 逐条校验引用、触发值和重复项，只返回可安全应用的绑定。
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
            return new MacroTriggerBindingLoadResult(bindings, errors, true);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new MacroTriggerBindingLoadResult([], [], true);
        }
        catch (Exception exception)
            when (exception
                    is TomlException
                        or InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return new MacroTriggerBindingLoadResult(
                [],
                [$"bindings.toml：{exception.Message}"],
                false
            );
        }
    }

    /// <summary>原子保存全部触发键绑定。</summary>
    public async Task SaveAsync(
        IReadOnlyList<MacroTriggerBinding> bindings,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var unsupportedTrigger = bindings.FirstOrDefault(binding =>
            !IsSupportedTrigger(binding.Trigger)
        );
        if (unsupportedTrigger is not null)
            throw new InvalidDataException($"触发键 {unsupportedTrigger.Trigger} 不受支持。");
        if (
            bindings.Select(binding => binding.Trigger).Distinct(StringComparer.Ordinal).Count()
            != bindings.Count
        )
            throw new InvalidDataException("一个触发键只能绑定一个宏。");
        // 所有绑定通过校验后再创建目录和写入，避免无效 IPC 请求污染磁盘状态。
        Directory.CreateDirectory(_paths.MacroDirectory);
        var text = TomlSerializer.Serialize(
            new MacroTriggerBindingsDocument
            {
                Bindings = [.. bindings.Select(MacroTriggerBindingDocument.FromModel)],
            }
        );
        await AtomicFile
            .WriteAllTextAsync(_paths.BindingFilePath, text, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsSupportedTrigger(string value) =>
        MacroInputRegistry.KeyboardTriggerValues.Contains(value, StringComparer.Ordinal)
        || MacroInputRegistry.MouseTriggerValues.Contains(value, StringComparer.Ordinal);

    private sealed class MacroTriggerBindingsDocument
    {
        /// <summary>获取或设置 TOML 文档中的绑定表数组。</summary>
        [TomlPropertyName("bindings")]
        [TomlTableArrayStyle(TomlTableArrayStyle.Headers)]
        public List<MacroTriggerBindingDocument> Bindings { get; set; } = [];
    }

    private sealed class MacroTriggerBindingDocument
    {
        /// <summary>获取或设置持久化的触发值。</summary>
        [TomlPropertyName("trigger")]
        public string Trigger { get; set; } = string.Empty;

        /// <summary>获取或设置绑定目标宏的 UUID。</summary>
        [TomlPropertyName("id")]
        public Guid MacroId { get; set; }

        /// <summary>从领域绑定创建可序列化的 TOML 文档项。</summary>
        internal static MacroTriggerBindingDocument FromModel(MacroTriggerBinding binding) =>
            new() { Trigger = binding.Trigger, MacroId = binding.MacroId };
    }
}
