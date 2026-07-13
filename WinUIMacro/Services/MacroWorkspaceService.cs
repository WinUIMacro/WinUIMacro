using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Services;

/// <summary>Persists the macro workspace and publishes its current snapshot to the engine.</summary>
internal sealed class MacroWorkspaceService
{
    private readonly EngineThreadService _engine;
    private readonly MacroLibraryService _library;
    private readonly MacroTriggerBindingService _bindingStore;
    private IReadOnlyList<MacroTriggerBinding> _bindings = [];

    internal MacroWorkspaceService(EngineThreadService engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Paths = new MacroDataPaths();
        _library = new MacroLibraryService(Paths);
        _bindingStore = new MacroTriggerBindingService(Paths);
    }

    internal MacroDataPaths Paths { get; }
    internal IReadOnlyList<MacroTriggerBinding> Bindings => _bindings;

    internal async Task<(
        IReadOnlyList<MacroDefinition> Macros,
        IReadOnlyList<string> Errors,
        Exception? RuntimeError
    )> LoadAsync()
    {
        var library = await _library.LoadAsync();
        var bindingResult = await _bindingStore.LoadAsync(
            library.Macros.Select(macro => macro.Id).ToHashSet()
        );
        if (library.DuplicateIds.Count > 0)
            await _bindingStore.SaveAsync(bindingResult.Bindings);

        _bindings = bindingResult.Bindings;
        var runtimeError = await SynchronizeRuntimeAsync(library.Macros);
        return (
            library.Macros,
            library.Errors.Concat(bindingResult.Errors).ToArray(),
            runtimeError
        );
    }

    internal async Task<(MacroDefinition Macro, Exception? RuntimeError)> SaveMacroAsync(
        MacroDefinition macro,
        string? previousName,
        IReadOnlyList<MacroDefinition> runtimeMacros
    )
    {
        var saved = await _library.SaveAsync(macro, previousName);
        var snapshot = runtimeMacros.Any(item => item.Id == saved.Id)
            ? runtimeMacros.Select(item => item.Id == saved.Id ? saved : item)
            : runtimeMacros.Append(saved);
        return (saved, await SynchronizeRuntimeAsync(snapshot));
    }

    internal async Task<Exception?> SetBindingAsync(
        string trigger,
        Guid? macroId,
        IReadOnlyList<MacroDefinition> runtimeMacros
    )
    {
        var bindings = _bindings
            .Where(binding => !string.Equals(binding.Trigger, trigger, StringComparison.Ordinal))
            .ToList();
        if (macroId is { } id)
            bindings.Add(new MacroTriggerBinding(trigger, id));
        await _bindingStore.SaveAsync(bindings);
        _bindings = bindings;
        return await SynchronizeRuntimeAsync(runtimeMacros);
    }

    internal async Task<Exception?> DeleteMacroAsync(
        Guid macroId,
        string? savedName,
        IReadOnlyList<MacroDefinition> remainingRuntimeMacros
    )
    {
        var bindings = _bindings.Where(binding => binding.MacroId != macroId).ToArray();
        await _bindingStore.SaveAsync(bindings);
        _bindings = bindings;
        if (savedName is not null)
            _library.Delete(savedName);
        return await SynchronizeRuntimeAsync(remainingRuntimeMacros);
    }

    private async Task<Exception?> SynchronizeRuntimeAsync(IEnumerable<MacroDefinition> macros)
    {
        try
        {
            await _engine.UpdateConfigurationAsync(macros, _bindings);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
