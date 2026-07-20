// 负责宏 JSON 文件的扫描、校验、原子保存和删除。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine.Storage;

/// <summary>加载并原子保存以名称拥有的宏 JSON 文件。</summary>
internal sealed class MacroLibraryService(MacroDataPaths paths)
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly MacroDataPaths _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>加载所有有效宏文件，并为重复 UUID 的宏重新生成标识。</summary>
    public async Task<MacroLibraryLoadResult> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(_paths.MacroDirectory);
        List<LoadedMacro> loadedMacros = [];
        List<string> errors = [];
        HashSet<Guid> duplicateIds = [];

        // 单个文件出错只记录到结果中，避免一份损坏宏阻止其他宏加载。
        foreach (
            var path in Directory
                .EnumerateFiles(_paths.MacroDirectory, "*.json")
                .Order(StringComparer.OrdinalIgnoreCase)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var name = MacroNameValidator.Normalize(Path.GetFileNameWithoutExtension(path));
                var json = await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                var document =
                    JsonSerializer.Deserialize<MacroDocument>(json, JsonOptions)
                    ?? throw new InvalidDataException("JSON 中没有宏定义。");
                if (document.SchemaVersion != SchemaVersion)
                    throw new InvalidDataException(
                        $"不支持 schemaVersion {document.SchemaVersion}。"
                    );
                if (document.Nodes is not { } nodes)
                    throw new InvalidDataException("nodes 是必需字段。");
                var macro = new MacroDefinition(
                    document.Id,
                    name,
                    document.Mode,
                    nodes,
                    document.LastEditedAt
                );
                ValidateMacro(macro);
                loadedMacros.Add(new LoadedMacro(macro, path, json));
            }
            catch (Exception exception)
                when (exception
                        is JsonException
                            or InvalidDataException
                            or ArgumentException
                            or IOException
                            or UnauthorizedAccessException
                )
            {
                errors.Add($"{Path.GetFileName(path)}：{exception.Message}");
            }
        }

        var usedIds = loadedMacros.Select(item => item.Macro.Id).ToHashSet();
        foreach (
            var group in loadedMacros
                .GroupBy(item => item.Macro.Id)
                .Where(group => group.Count() > 1)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            duplicateIds.Add(group.Key);
            var duplicates = group.ToArray();
            try
            {
                foreach (var duplicate in duplicates)
                {
                    Guid replacementId;
                    do replacementId = Guid.NewGuid();
                    while (!usedIds.Add(replacementId));

                    duplicate.Macro = duplicate.Macro with { Id = replacementId };
                    await WriteMacroAsync(duplicate.Path, duplicate.Macro, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                foreach (var duplicate in duplicates)
                {
                    duplicate.IsValid = false;
                    try
                    {
                        await AtomicFile
                            .WriteAllTextAsync(
                                duplicate.Path,
                                duplicate.OriginalJson,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                        when (rollbackException is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(
                            $"{duplicate.Macro.Name}.json 恢复原 UUID 失败：{rollbackException.Message}"
                        );
                    }
                }

                errors.Add($"重复 ID 自动修复失败：{exception.Message}");
                continue;
            }

            var names = duplicates.Select(item => item.Macro.Name).ToArray();
            errors.Add(
                $"{FormatMacroNames(names)} 的 ID 重复，已为这些宏重新生成 UUID；"
                    + "如果之前这个 ID 绑定了触发键，请重新绑定。"
            );
        }

        var macros = loadedMacros.Where(item => item.IsValid).Select(item => item.Macro).ToArray();
        return new MacroLibraryLoadResult(macros, errors, duplicateIds);
    }

    /// <summary>保存宏；新文件写入成功后再删除旧名称对应的文件。</summary>
    public async Task<MacroDefinition> SaveAsync(
        MacroDefinition macro,
        string? previousName = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(macro);
        var name = MacroNameValidator.Normalize(macro.Name);
        Directory.CreateDirectory(_paths.MacroDirectory);
        var targetPath = GetPath(name);
        EnsureNameAvailable(targetPath, previousName);

        var saved = macro with { Name = name, LastEditedAt = DateTimeOffset.UtcNow };
        ValidateMacro(saved);
        // 先写临时文件再替换目标文件，避免程序中断时留下半份 JSON。
        await WriteMacroAsync(targetPath, saved, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(previousName))
        {
            // 改名时新文件已经落盘，旧文件只在确认路径不同后删除。
            var previousPath = GetPath(MacroNameValidator.Normalize(previousName));
            if (
                !string.Equals(previousPath, targetPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(previousPath)
            )
            {
                try
                {
                    File.Delete(previousPath);
                }
                catch
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch { }
                    throw;
                }
            }
        }

        return saved;
    }

    /// <summary>删除宏名称对应的文件。</summary>
    public void Delete(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(MacroNameValidator.Normalize(name));
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetPath(string name) =>
        Path.Combine(_paths.MacroDirectory, string.Concat(name, ".json"));

    private static async Task WriteMacroAsync(
        string path,
        MacroDefinition macro,
        CancellationToken cancellationToken
    )
    {
        var document = new MacroDocument(
            SchemaVersion,
            macro.Id,
            macro.Mode,
            macro.Nodes,
            macro.LastEditedAt
        );
        await AtomicFile
            .WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(document, JsonOptions),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static string FormatMacroNames(IReadOnlyList<string> names) =>
        names.Count == 2 ? $"{names[0]} 与 {names[1]}" : string.Join("、", names);

    private void EnsureNameAvailable(string targetPath, string? previousName)
    {
        var previousPath = string.IsNullOrWhiteSpace(previousName)
            ? null
            : GetPath(MacroNameValidator.Normalize(previousName));
        var conflict = Directory
            .EnumerateFiles(_paths.MacroDirectory, "*.json")
            .FirstOrDefault(path =>
                string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase)
            );
        if (conflict is not null)
            throw new IOException(
                $"宏名“{Path.GetFileNameWithoutExtension(targetPath)}”已经存在。"
            );
    }

    private static void ValidateMacro(MacroDefinition macro)
    {
        if (macro.Id == Guid.Empty)
            throw new InvalidDataException("id 必须是有效 UUID。");
        if (!Enum.IsDefined(macro.Mode))
            throw new InvalidDataException($"不支持的回放模式 {macro.Mode}。");
        foreach (var node in macro.Nodes)
            ValidateNode(node);
    }

    private static void ValidateNode(MacroNode? node)
    {
        if (node is null)
            throw new InvalidDataException("宏节点无效。");
        if (!Enum.IsDefined(node.Type))
            throw new InvalidDataException($"不支持的节点类型 {node.Type}。");
        if (node.Type == MacroNodeType.Note)
            return;
        if (string.IsNullOrWhiteSpace(node.Value))
            throw new InvalidDataException("宏节点值不能为空。");
        if (node.Type == MacroNodeType.Delay)
        {
            // 延时使用不受区域设置影响的非负整数毫秒保存。
            if (
                !long.TryParse(
                    node.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var milliseconds
                )
                || milliseconds < 0
            )
                throw new InvalidDataException($"延迟“{node.Value}”不是有效的非负毫秒数。");
            return;
        }
        if (!MacroInputRegistry.TryCreateOperation(node, out _))
            throw new InvalidDataException($"节点 {node.Type}:{node.Value} 不受支持。");
    }

    private sealed record MacroDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] Guid Id,
        [property: JsonRequired] MacroPlaybackMode Mode,
        [property: JsonRequired] IReadOnlyList<MacroNode>? Nodes,
        [property: JsonRequired] DateTimeOffset LastEditedAt
    );

    private sealed class LoadedMacro(MacroDefinition macro, string path, string originalJson)
    {
        internal MacroDefinition Macro { get; set; } = macro;
        internal string Path { get; } = path;
        internal string OriginalJson { get; } = originalJson;
        internal bool IsValid { get; set; } = true;
    }
}
