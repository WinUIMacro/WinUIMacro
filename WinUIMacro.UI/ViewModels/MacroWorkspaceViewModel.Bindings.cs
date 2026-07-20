using System.Diagnostics;
using WinUIMacro.Contracts;

namespace WinUIMacro.UI.ViewModels;

internal partial class MacroWorkspaceViewModel
{
    private IReadOnlyList<MacroTriggerBinding> _bindings = [];
    private readonly List<BindingKeyViewModel> _mouseSideKeys = [];
    private List<BindingKeyViewModel> BindingKeys { get; } = [];

    public IReadOnlyList<BindingKeyViewModel> MouseSideKeys => _mouseSideKeys;

    internal List<IReadOnlyList<BindingKeyViewModel>> KeyboardRows { get; } = [];

    internal Task BindTriggerAsync(BindingKeyViewModel key, MacroEditorViewModel macro) =>
        SetBindingAsync(key, macro, $"{key.DisplayText} 已绑定到“{macro.Name}”。");

    internal Task UnbindTriggerAsync(BindingKeyViewModel key) =>
        SetBindingAsync(key, null, $"已取消 {key.DisplayText} 的绑定。");

    internal IEnumerable<MacroEditorViewModel> FindBindableMacros(string? query)
    {
        var candidates = Macros.Where(macro => macro.IsPersisted);
        if (!string.IsNullOrWhiteSpace(query))
            candidates = candidates.Where(macro =>
                macro.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
            );
        return candidates.OrderBy(macro => macro.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    [RelayCommand]
    private void OpenMacroFolder()
    {
        Directory.CreateDirectory(_macroDirectory);
        Process.Start(
            new ProcessStartInfo("explorer.exe", _macroDirectory) { UseShellExecute = true }
        );
    }

    private void RefreshBindingDisplay()
    {
        foreach (var key in BindingKeys)
        {
            var binding = _bindings.FirstOrDefault(item => item.Trigger == key.Value);
            key.BoundMacro = binding is not null
                ? Macros.FirstOrDefault(macro => macro.Id == binding.MacroId)
                : null;
        }
        foreach (var macro in Macros)
        {
            var values = _bindings
                .Where(binding => binding.MacroId == macro.Id)
                .Select(binding => MacroInputRegistry.GetDisplayText(binding.Trigger))
                .ToArray();
            macro.IsBound = values.Length > 0;
            macro.BindingSummary = values.Length == 0 ? "未绑定" : string.Join("、", values);
        }
        SortMacros();
    }

    private async Task SetBindingAsync(
        BindingKeyViewModel key,
        MacroEditorViewModel? macro,
        string successMessage
    )
    {
        try
        {
            var result = await _client.SetBindingAsync(key.Value, macro?.Id);
            _bindings = _bindings
                .Where(binding =>
                    !string.Equals(binding.Trigger, key.Value, StringComparison.Ordinal)
                )
                .Concat(macro is null ? [] : [new MacroTriggerBinding(key.Value, macro.Id)])
                .ToArray();
            RefreshBindingDisplay();
            ShowRuntimeUpdateResult(
                successMessage,
                "绑定已保存，但运行时配置更新失败",
                result.RuntimeError
            );
        }
        catch (Exception exception)
        {
            RefreshBindingDisplay();
            ShowError(exception.Message);
        }
    }

    private void BuildBindingKeys()
    {
        var keys = MacroInputRegistry.KeyboardTriggerValues.ToDictionary(
            value => value,
            value => new BindingKeyViewModel(value, MacroInputRegistry.GetDisplayText(value)),
            StringComparer.Ordinal
        );
        foreach (var rowValues in KeyboardLayout.Rows)
        {
            List<BindingKeyViewModel> row = [];
            foreach (var item in rowValues)
            {
                if (!keys.Remove(item.Value, out var key))
                    throw new InvalidOperationException(
                        $"KeyboardLayout contains an unknown or duplicate key '{item.Value}'."
                    );
                key.GridColumn = item.Column;
                key.GridColumnSpan = item.Span;
                row.Add(key);
                BindingKeys.Add(key);
            }
            KeyboardRows.Add(row);
        }
        foreach (var value in MacroInputRegistry.MouseTriggerValues.Reverse())
        {
            var key = new BindingKeyViewModel(value, MacroInputRegistry.GetDisplayText(value));
            _mouseSideKeys.Add(key);
            BindingKeys.Add(key);
        }
    }
}
