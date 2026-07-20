using Microsoft.UI.Windowing;

namespace WinUIMacro.UI;

public sealed partial class MainWindow
{
    private readonly MacroListPage _macroListPage = new();
    private readonly BindingsPage _bindingsPage = new();
    private MacroDetailPage? _macroDetailPage;
    private Task<bool>? _leaveTask;
    private bool _allowClose;
    private bool _closeInProgress;

    internal Task<bool> PrepareForExitAsync(bool forceExit = false) =>
        TryLeaveMacroDetailAsync(navigateToList: false, forceExit);

    internal void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    internal async Task NavigateToMacroDetailAsync(MacroEditorViewModel macro)
    {
        if (
            !ViewModel.CanChangeWorkspace
            || !await ViewModel.EnsureDetailsLoadedAsync(macro)
            || !ViewModel.CanChangeWorkspace
            || !ViewModel.Macros.Contains(macro)
        )
            return;

        // 旧详情页必须先退订节点集合，才能安全切换编辑目标。
        DisposeMacroDetailPage();
        ViewModel.SelectedMacro = macro;
        var detailPage = new MacroDetailPage();
        detailPage.Activate(ViewModel, macro);
        _macroDetailPage = detailPage;
        ContentHost.Content = detailPage;
        RootSelectorBar.Visibility = Visibility.Collapsed;
        BackBar.Visibility = Visibility.Visible;
    }

    private void RootSelectorBar_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args
    )
    {
        if (ReferenceEquals(sender.SelectedItem, BindingsSelectorItem))
            ShowBindings();
        else
            ShowMacroList();
    }

    private void ShowMacroList()
    {
        DisposeMacroDetailPage();
        _bindingsPage.Deactivate();
        _macroListPage.Activate(ViewModel, this);
        ContentHost.Content = _macroListPage;
        RootSelectorBar.Visibility = Visibility.Visible;
        BackBar.Visibility = Visibility.Collapsed;
    }

    private void ShowBindings()
    {
        DisposeMacroDetailPage();
        _bindingsPage.Activate(ViewModel);
        ContentHost.Content = _bindingsPage;
        RootSelectorBar.Visibility = Visibility.Visible;
        BackBar.Visibility = Visibility.Collapsed;
    }

    private void DisposeMacroDetailPage()
    {
        if (_macroDetailPage is not { } detailPage)
            return;

        if (ReferenceEquals(ContentHost.Content, detailPage))
            ContentHost.Content = null;
        detailPage.Dispose();
        _macroDetailPage = null;
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await TryLeaveMacroDetailAsync(navigateToList: true);
        }
        catch (Exception exception)
        {
            ViewModel.ShowError($"返回宏列表失败：{exception.Message}");
        }
    }

    private async void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMacro is not { } macro)
            return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "删除宏",
            Content = $"确定删除“{macro.Name}”吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        if (await ViewModel.DeleteSelectedAsync())
            ShowMacroList();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        if (_closeInProgress)
            return;
        _closeInProgress = true;
        _ = CompleteCloseAsync();
    }

    private async Task CompleteCloseAsync()
    {
        try
        {
            if (!await TryLeaveMacroDetailAsync(navigateToList: false))
                return;
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            _allowClose = false;
            ViewModel.ShowError($"关闭窗口失败：{exception.Message}");
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    private async Task<bool> TryLeaveMacroDetailAsync(bool navigateToList, bool forceExit = false)
    {
        // 返回、窗口关闭和 Host 退出必须共享同一个确认流程，避免并发显示 ContentDialog。
        var leaveTask = _leaveTask ??= LeaveMacroDetailCoreAsync(navigateToList, forceExit);
        try
        {
            return await leaveTask;
        }
        finally
        {
            if (ReferenceEquals(_leaveTask, leaveTask))
                _leaveTask = null;
        }
    }

    private async Task<bool> LeaveMacroDetailCoreAsync(bool navigateToList, bool forceExit)
    {
        await ViewModel.StopRecordingAsync();
        ViewModel.SelectedMacro?.CommitActiveEdits();
        if (ViewModel.SelectedMacro is { IsEmptyUnmodifiedDraft: true } emptyDraft)
        {
            ViewModel.DiscardChanges(emptyDraft);
            if (navigateToList)
            {
                RootSelectorBar.SelectedItem = MacroListSelectorItem;
                ShowMacroList();
            }
            return true;
        }
        if (ViewModel.SelectedMacro is not { IsDirty: true } macro)
        {
            if (navigateToList)
            {
                RootSelectorBar.SelectedItem = MacroListSelectorItem;
                ShowMacroList();
            }
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "保存更改？",
            Content = forceExit
                ? $"宏“{macro.DisplayName}”有未保存的修改。后台引擎已停止，请保存或放弃修改后退出。"
                : $"宏“{macro.DisplayName}”有未保存的修改。",
            PrimaryButtonText = "保存并退出",
            SecondaryButtonText = "不保存退出",
            CloseButtonText = forceExit ? string.Empty : "取消退出",
            DefaultButton = ContentDialogButton.Primary,
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.SaveMacroCommand.ExecuteAsync(null);
                if (macro.IsDirty)
                {
                    if (forceExit)
                        continue;
                    return false;
                }
                break;
            }
            if (result == ContentDialogResult.Secondary)
            {
                ViewModel.DiscardChanges(macro);
                if (macro.IsNew && ViewModel.Macros.Contains(macro))
                    return false;
                break;
            }
            if (!forceExit)
                return false;
        }

        if (navigateToList)
        {
            RootSelectorBar.SelectedItem = MacroListSelectorItem;
            ShowMacroList();
        }
        return true;
    }
}
