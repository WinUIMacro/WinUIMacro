// 组合宏存储、绑定存储与运行时配置，形成引擎工作区门面。
using WinUIMacro.Contracts;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro;

/// <summary>组合宏存储、绑定存储与运行时配置，形成引擎工作区门面。</summary>
internal sealed class EngineWorkspace
{
    private readonly Func<
        IEnumerable<MacroDefinition>,
        IEnumerable<MacroTriggerBinding>,
        Task
    > _updateConfigurationAsync;
    private readonly MacroDataPaths _paths;
    private readonly MacroLibraryService _library;
    private readonly MacroTriggerBindingService _bindingStore;
    private IReadOnlyList<MacroDefinition> _macros = [];
    private IReadOnlyList<MacroTriggerBinding> _bindings = [];
    private IReadOnlyList<string> _loadErrors = [];
    private string? _recentError;

    /// <summary>使用默认数据路径和指定引擎创建工作区。</summary>
    internal EngineWorkspace(EngineRuntime engine)
        : this(
            new MacroDataPaths(),
            (engine ?? throw new ArgumentNullException(nameof(engine))).UpdateConfigurationAsync
        ) { }

    /// <summary>使用指定数据路径和运行时配置回调创建工作区。</summary>
    internal EngineWorkspace(
        MacroDataPaths paths,
        Func<
            IEnumerable<MacroDefinition>,
            IEnumerable<MacroTriggerBinding>,
            Task
        > updateConfigurationAsync
    )
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _updateConfigurationAsync =
            updateConfigurationAsync
            ?? throw new ArgumentNullException(nameof(updateConfigurationAsync));
        _library = new MacroLibraryService(_paths);
        _bindingStore = new MacroTriggerBindingService(_paths);
    }

    /// <summary>从磁盘加载宏与绑定，并将有效配置应用到运行时。</summary>
    internal async Task<WorkspaceSnapshot> LoadAsync()
    {
        // 先读取宏，再用有效宏 UUID 校验绑定，避免返回悬空引用。
        var library = await _library.LoadAsync();
        var bindingResult = await _bindingStore.LoadAsync(
            library.Macros.Select(macro => macro.Id).ToHashSet()
        );
        List<string> loadErrors = [.. library.Errors, .. bindingResult.Errors];
        if (library.DuplicateIds.Count > 0 && bindingResult.ParsedSuccessfully)
        {
            // 宏已获得新 UUID，原 UUID 的绑定含义不确定，只把其余有效绑定写回磁盘。
            try
            {
                await _bindingStore.SaveAsync(bindingResult.Bindings);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                loadErrors.Add($"bindings.toml 自动清理失败：{exception.Message}");
            }
        }

        _macros = library.Macros;
        _bindings = bindingResult.Bindings;
        _loadErrors = loadErrors;
        var runtimeError = await UpdateRuntimeAsync();
        // 磁盘数据已经成功加载时，运行时更新失败只作为状态错误返回给 UI。
        if (runtimeError is not null)
            RecordError($"运行时配置更新失败：{runtimeError}");
        var errors = _recentError is null ? _loadErrors : [.. _loadErrors, _recentError];
        return new WorkspaceSnapshot(
            [.. _macros.Select(ToSummary)],
            _bindings,
            errors,
            _paths.MacroDirectory
        );
    }

    /// <summary>从当前工作区按 UUID 获取完整宏。</summary>
    internal MacroDefinition GetMacro(Guid macroId) =>
        _macros.FirstOrDefault(macro => macro.Id == macroId)
        ?? throw new InvalidOperationException($"找不到宏 {macroId}。");

    /// <summary>保存宏、更新工作区快照并刷新运行时配置。</summary>
    internal async Task<SaveMacroResponse> SaveAsync(MacroDefinition macro)
    {
        var previousName = _macros.FirstOrDefault(item => item.Id == macro.Id)?.Name;
        var saved = await _library.SaveAsync(macro, previousName);
        _macros = _macros.Any(item => item.Id == saved.Id)
            ? _macros.Select(item => item.Id == saved.Id ? saved : item).ToArray()
            : [.. _macros, saved];
        var runtimeError = await UpdateRuntimeAsync();
        return new SaveMacroResponse(saved, runtimeError);
    }

    /// <summary>以绑定回滚保护删除指定宏并更新运行时配置。</summary>
    internal async Task<RuntimeUpdateResponse> DeleteAsync(Guid macroId)
    {
        var macro = GetMacro(macroId);
        var previousBindings = _bindings;
        var bindings = _bindings.Where(binding => binding.MacroId != macroId).ToArray();
        await _bindingStore.SaveAsync(bindings);
        _bindings = bindings;

        // 先让目标宏不可再被触发；此步失败时保留宏文件，避免磁盘删除后 Runtime 仍持有绑定。
        var unbindError = await UpdateRuntimeAsync();
        if (unbindError is not null)
        {
            try
            {
                await _bindingStore.SaveAsync(previousBindings);
                _bindings = previousBindings;
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"运行时解除绑定失败：{unbindError}；恢复绑定文件失败：{rollbackException.Message}",
                    rollbackException
                );
            }
            throw new InvalidOperationException($"运行时解除绑定失败：{unbindError}");
        }

        _library.Delete(macro.Name);
        _macros = _macros.Where(item => item.Id != macroId).ToArray();
        return new RuntimeUpdateResponse(await UpdateRuntimeAsync());
    }

    /// <summary>设置或取消触发键绑定，并刷新运行时配置。</summary>
    internal async Task<RuntimeUpdateResponse> SetBindingAsync(string trigger, Guid? macroId)
    {
        if (macroId is { } targetId)
            _ = GetMacro(targetId);
        var bindings = _bindings
            .Where(binding => !string.Equals(binding.Trigger, trigger, StringComparison.Ordinal))
            .ToList();
        if (macroId is { } id)
            bindings.Add(new MacroTriggerBinding(trigger, id));
        await _bindingStore.SaveAsync(bindings);
        _bindings = bindings;
        return new RuntimeUpdateResponse(await UpdateRuntimeAsync());
    }

    /// <summary>记录需要随下一次工作区快照返回的基础设施错误。</summary>
    internal void RecordError(string message) => _recentError = message;

    private async Task<string?> UpdateRuntimeAsync()
    {
        try
        {
            // 引擎在自己的消息线程应用不可变快照，宿主线程只负责传递当前状态。
            await _updateConfigurationAsync(_macros, _bindings);
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static MacroSummary ToSummary(MacroDefinition macro) =>
        new(macro.Id, macro.Name, macro.LastEditedAt);
}
