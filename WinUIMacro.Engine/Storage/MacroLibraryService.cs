using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinUIMacro.Engine.Models;

namespace WinUIMacro.Engine.Storage;

/// <summary>Loads and atomically saves name-owned macro JSON files.</summary>
public sealed class MacroLibraryService(MacroDataPaths paths)
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

    /// <summary>Loads all valid macro files without changing invalid files.</summary>
    public async Task<MacroLibraryLoadResult> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(_paths.MacroDirectory);
        List<MacroDefinition> macros = [];
        List<string> errors = [];
        Dictionary<Guid, (MacroDefinition Macro, string FileName)> macrosById = [];
        HashSet<Guid> duplicateIds = [];

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
                if (macrosById.TryGetValue(macro.Id, out var existing))
                {
                    macros.Remove(existing.Macro);
                    duplicateIds.Add(macro.Id);
                    throw new InvalidDataException(
                        $"{name} 与 {existing.FileName}的UUID {macro.Id} 重复。"
                    );
                }
                if (duplicateIds.Contains(macro.Id))
                    throw new InvalidDataException($"UUID {macro.Id} 与其他宏重复。");
                macrosById.Add(macro.Id, (macro, Path.GetFileName(path)));
                macros.Add(macro);
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

        return new MacroLibraryLoadResult(macros, errors, duplicateIds);
    }

    /// <summary>Saves a macro and removes its previous name-owned file after the new write succeeds.</summary>
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
        var document = new MacroDocument(
            SchemaVersion,
            saved.Id,
            saved.Mode,
            saved.Nodes,
            saved.LastEditedAt
        );
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await WriteAtomicAsync(targetPath, json, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(previousName))
        {
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

    /// <summary>Deletes the name-owned file for a macro.</summary>
    public void Delete(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(MacroNameValidator.Normalize(name));
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetPath(string name) =>
        Path.Combine(_paths.MacroDirectory, string.Concat(name, ".json"));

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

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken
    )
    {
        var temporaryPath = string.Concat(path, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record MacroDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] Guid Id,
        [property: JsonRequired] MacroPlaybackMode Mode,
        [property: JsonRequired] IReadOnlyList<MacroNode>? Nodes,
        [property: JsonRequired] DateTimeOffset LastEditedAt
    );
}
