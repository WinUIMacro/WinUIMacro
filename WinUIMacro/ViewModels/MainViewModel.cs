using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Playback;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Services;

namespace WinUIMacro.ViewModels;

/// <summary>Coordinates editable macros, trigger bindings, recording, and runtime status.</summary>
public partial class MacroWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly EngineThreadService _engine;
    private readonly MacroWorkspaceService _workspace;
    private readonly DispatcherQueueTimer _messageTimer;
    private int _suppressRecordButtonClick;
    private bool _disposed;

    internal MacroWorkspaceViewModel(EngineThreadService engine, DispatcherQueue dispatcherQueue)
    {
        _engine = engine;
        _dispatcherQueue = dispatcherQueue;
        _workspace = new MacroWorkspaceService(engine);
        _messageTimer = dispatcherQueue.CreateTimer();
        _messageTimer.Interval = TimeSpan.FromSeconds(4);
        _messageTimer.IsRepeating = false;
        _messageTimer.Tick += MessageTimer_Tick;
        _engine.NodeRecorded += Engine_NodeRecorded;
        _engine.RecordingStateChanged += Engine_RecordingStateChanged;
        _engine.PlaybackFailed += Engine_PlaybackFailed;
        _engine.ProcessingFailed += Engine_ProcessingFailed;
        BuildBindingKeys();
    }

    public ObservableCollection<MacroEditorViewModel> Macros { get; } = [];
    public ObservableCollection<BindingKeyViewModel> BindingKeys { get; } = [];
    public ObservableCollection<BindingKeyViewModel> MouseSideKeys { get; } = [];
    public ObservableCollection<KeyboardRowViewModel> KeyboardRows { get; } = [];

    [ObservableProperty]
    public partial MacroEditorViewModel? SelectedMacro { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsMessageOpen { get; set; }

    [ObservableProperty]
    public partial RecordingState RecordingState { get; set; }

    public bool IsRecording => RecordingState == RecordingState.Recording;

    public bool CanEditSequence => RecordingState == RecordingState.Idle;

    public string RecordButtonLabel =>
        RecordingState switch
        {
            RecordingState.Starting => "取消录制",
            RecordingState.Recording => "停止录制",
            RecordingState.Stopping => "正在停止",
            _ => "开始录制",
        };
    public string RecordButtonGlyph => IsRecording ? "\uE71A" : "\uE768";

    partial void OnRecordingStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(RecordButtonLabel));
        OnPropertyChanged(nameof(RecordButtonGlyph));
        OnPropertyChanged(nameof(CanEditSequence));
    }

    internal async Task InitializeAsync()
    {
        await ReloadLibraryAsync();
    }

    [RelayCommand]
    private async Task RefreshMacrosAsync()
    {
        if (Macros.Any(macro => macro.IsDirty))
        {
            ShowWarning("请先保存或撤销未保存的宏，再刷新列表。");
            return;
        }

        try
        {
            await ReloadLibraryAsync();
        }
        catch (Exception exception)
        {
            ShowError($"刷新宏列表失败：{exception.Message}");
        }
    }

    private async Task ReloadLibraryAsync()
    {
        var selectedId = SelectedMacro?.Id;
        var loaded = await _workspace.LoadAsync();

        Macros.Clear();
        foreach (var macro in loaded.Macros)
            Macros.Add(new MacroEditorViewModel(macro, true));
        SelectedMacro =
            Macros.FirstOrDefault(macro => macro.Id == selectedId) ?? Macros.FirstOrDefault();
        RefreshBindingDisplay();
        var errors = loaded.RuntimeError is null
            ? loaded.Errors
            : [.. loaded.Errors, $"运行时配置更新失败：{loaded.RuntimeError.Message}"];
        if (errors.Count == 0)
            ShowSuccess($"已加载 {Macros.Count} 个宏。");
        else
            ShowWarning(string.Join(Environment.NewLine, errors));
    }

    [RelayCommand]
    private void AddMacro()
    {
        var name = CreateUniqueName("未命名宏");
        var macro = new MacroEditorViewModel(
            new MacroDefinition(Guid.NewGuid(), name, MacroPlaybackMode.Once, []),
            false
        );
        Macros.Add(macro);
        SelectedMacro = macro;
        ShowInfo("已创建宏草稿，保存后可绑定触发键。");
    }

    [RelayCommand]
    private async Task SaveMacroAsync()
    {
        if (SelectedMacro is not { } macro)
            return;
        try
        {
            EnsureUniqueName(macro);
            var result = await _workspace.SaveMacroAsync(
                macro.ToDefinition(),
                macro.SavedName,
                GetRuntimeMacros()
            );
            macro.MarkSaved(result.Macro);
            RefreshBindingDisplay();
            if (result.RuntimeError is null)
                ShowSuccess($"已保存“{result.Macro.Name}”。");
            else
                ShowWarning($"宏已保存，但运行时配置更新失败：{result.RuntimeError.Message}");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    internal async Task DeleteSelectedAsync()
    {
        if (SelectedMacro is not { } macro)
            return;
        try
        {
            var runtimeError = await _workspace.DeleteMacroAsync(
                macro.Id,
                macro.SavedName,
                GetRuntimeMacros(excludedMacro: macro)
            );
            var index = Macros.IndexOf(macro);
            Macros.Remove(macro);
            SelectedMacro =
                Macros.Count == 0 ? null : Macros[Math.Clamp(index, 0, Macros.Count - 1)];
            RefreshBindingDisplay();
            if (runtimeError is null)
                ShowSuccess($"已删除“{macro.Name}”。");
            else
                ShowWarning($"宏已删除，但运行时配置更新失败：{runtimeError.Message}");
        }
        catch (Exception exception)
        {
            RefreshBindingDisplay();
            ShowError(exception.Message);
        }
    }

    internal void DiscardChanges(MacroEditorViewModel macro)
    {
        if (macro.IsNew)
        {
            var index = Macros.IndexOf(macro);
            if (index >= 0)
                Macros.RemoveAt(index);
            SelectedMacro =
                Macros.Count == 0 ? null : Macros[Math.Clamp(index, 0, Macros.Count - 1)];
            return;
        }

        macro.RevertToSavedSnapshot();
        ShowInfo($"已撤销“{macro.DisplayName}”的未保存修改。");
    }

    [RelayCommand]
    private void AddDelay() => SelectedMacro?.AddNode(new MacroNode(MacroNodeType.Delay, "100"));

    [RelayCommand]
    private void AddNote() => SelectedMacro?.AddNode(new MacroNode(MacroNodeType.Note, "备注"));

    internal async Task ToggleRecordingAsync(DesktopRectangle stopBounds)
    {
        try
        {
            await (
                RecordingState switch
                {
                    RecordingState.Idle when SelectedMacro is { } macro =>
                        _engine.StartRecordingAsync(macro.Id, stopBounds),
                    RecordingState.Starting or RecordingState.Recording =>
                        _engine.StopRecordingAsync(),
                    _ => Task.CompletedTask,
                }
            );
        }
        catch (Exception exception)
        {
            ShowError($"录制操作失败：{exception.Message}");
        }
    }

    internal async Task UpdateRecordingBoundsAsync(DesktopRectangle bounds)
    {
        try
        {
            await _engine.UpdateRecordingBoundsAsync(bounds);
        }
        catch (Exception exception)
        {
            ShowError($"录制操作失败：{exception.Message}");
        }
    }

    internal bool TryConsumeRecordButtonClickSuppression() =>
        Interlocked.Exchange(ref _suppressRecordButtonClick, 0) != 0;

    internal async Task BindTriggerAsync(BindingKeyViewModel key, MacroEditorViewModel macro)
    {
        await SetBindingAsync(key, macro, $"{key.DisplayText} 已绑定到“{macro.Name}”。");
    }

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

    internal Task StopRecordingIfActiveAsync() => _engine.StopRecordingAsync();

    [RelayCommand]
    private void OpenMacroFolder()
    {
        Directory.CreateDirectory(_workspace.Paths.MacroDirectory);
        Process.Start(
            new ProcessStartInfo("explorer.exe", _workspace.Paths.MacroDirectory)
            {
                UseShellExecute = true,
            }
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _messageTimer.Stop();
        _messageTimer.Tick -= MessageTimer_Tick;
        _engine.NodeRecorded -= Engine_NodeRecorded;
        _engine.RecordingStateChanged -= Engine_RecordingStateChanged;
        _engine.PlaybackFailed -= Engine_PlaybackFailed;
        _engine.ProcessingFailed -= Engine_ProcessingFailed;
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<MacroDefinition> GetRuntimeMacros(
        MacroEditorViewModel? excludedMacro = null
    ) =>
        [
            .. Macros
                .Where(macro => macro.IsPersisted && !ReferenceEquals(macro, excludedMacro))
                .Select(macro => macro.ToDefinition()),
        ];

    private void RefreshBindingDisplay()
    {
        foreach (var key in BindingKeys)
        {
            var binding = _workspace.Bindings.FirstOrDefault(item => item.Trigger == key.Value);
            key.BoundMacro = binding is not null
                ? Macros.FirstOrDefault(macro => macro.Id == binding.MacroId)
                : null;
        }
        foreach (var macro in Macros)
        {
            var values = _workspace
                .Bindings.Where(binding => binding.MacroId == macro.Id)
                .Select(binding => MacroInputRegistry.GetDisplayText(binding.Trigger))
                .ToArray();
            macro.IsBound = values.Length > 0;
            macro.BindingSummary = values.Length == 0 ? "未绑定" : string.Join("、", values);
        }
        SortMacros();
    }

    private void SortMacros()
    {
        var sorted = Macros
            .OrderByDescending(macro => macro.IsBound)
            .ThenByDescending(macro => macro.LastEditedAt)
            .ThenBy(macro => macro.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        for (var targetIndex = 0; targetIndex < sorted.Length; targetIndex++)
        {
            var currentIndex = Macros.IndexOf(sorted[targetIndex]);
            if (currentIndex != targetIndex)
                Macros.Move(currentIndex, targetIndex);
        }
    }

    private async Task SetBindingAsync(
        BindingKeyViewModel key,
        MacroEditorViewModel? macro,
        string successMessage
    )
    {
        try
        {
            var runtimeError = await _workspace.SetBindingAsync(
                key.Value,
                macro?.Id,
                GetRuntimeMacros()
            );
            RefreshBindingDisplay();
            if (runtimeError is null)
                ShowSuccess(successMessage);
            else
                ShowWarning($"绑定已保存，但运行时配置更新失败：{runtimeError.Message}");
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
            var row = new KeyboardRowViewModel();
            foreach (var item in rowValues)
            {
                if (!keys.Remove(item.Value, out var key))
                    throw new InvalidOperationException(
                        $"KeyboardLayout contains an unknown or duplicate key '{item.Value}'."
                    );
                key.GridColumn = item.Column;
                key.GridColumnSpan = item.Span;
                row.Keys.Add(key);
                BindingKeys.Add(key);
            }
            KeyboardRows.Add(row);
        }
        foreach (var value in MacroInputRegistry.MouseTriggerValues.Reverse())
        {
            var key = new BindingKeyViewModel(value, MacroInputRegistry.GetDisplayText(value));
            MouseSideKeys.Add(key);
            BindingKeys.Add(key);
        }
    }

    private void EnsureUniqueName(MacroEditorViewModel target)
    {
        if (
            Macros.Any(macro =>
                !ReferenceEquals(macro, target)
                && string.Equals(
                    macro.Name.Trim(),
                    target.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            throw new InvalidOperationException($"宏名“{target.Name.Trim()}”已经存在。");
    }

    private string CreateUniqueName(string baseName)
    {
        var name = baseName;
        for (
            var index = 2;
            Macros.Any(macro =>
                string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            index++
        )
            name = $"{baseName} {index}";
        return name;
    }

    private void Engine_NodeRecorded(object? sender, MacroNodeRecordedEventArgs args) =>
        Dispatch(() =>
        {
            var macro = Macros.FirstOrDefault(item => item.Id == args.MacroId);
            macro?.AddNode(args.Node);
        });

    private void Engine_RecordingStateChanged(object? sender, RecordingStateChangedEventArgs args)
    {
        if (args.SuppressRecordButtonClick)
            Interlocked.Exchange(ref _suppressRecordButtonClick, 1);
        Dispatch(() =>
        {
            RecordingState = args.State;
            if (args.State == RecordingState.Starting && SelectedMacro is { } macro)
                ShowInfo($"开始录制“{macro.Name}”。");
            else if (args.State == RecordingState.Idle)
                ShowInfo("录制已停止，请保存宏。");
        });
    }

    private void Engine_PlaybackFailed(object? sender, MacroPlaybackFailedEventArgs args) =>
        Dispatch(() => ShowError($"回放“{args.Macro.Name}”失败：{args.Exception.Message}"));

    private void Engine_ProcessingFailed(Exception exception) =>
        Dispatch(() => ShowError($"输入处理失败：{exception.Message}"));

    private void ShowInfo(string message, bool autoClose = true) =>
        ShowMessage(message, InfoBarSeverity.Informational, autoClose);

    private void ShowSuccess(string message) =>
        ShowMessage(message, InfoBarSeverity.Success, autoClose: true);

    private void ShowWarning(string message) =>
        ShowMessage(message, InfoBarSeverity.Warning, autoClose: false);

    internal void ShowError(string message) =>
        ShowMessage(message, InfoBarSeverity.Error, autoClose: false);

    private void ShowMessage(string message, InfoBarSeverity severity, bool autoClose)
    {
        _messageTimer.Stop();
        Message = message;
        MessageSeverity = severity;
        IsMessageOpen = true;
        if (autoClose)
            _messageTimer.Start();
    }

    private void MessageTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        IsMessageOpen = false;
    }

    private void Dispatch(Action action)
    {
        if (_disposed)
            return;
        if (_dispatcherQueue.HasThreadAccess)
            action();
        else
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                    action();
            });
    }
}
