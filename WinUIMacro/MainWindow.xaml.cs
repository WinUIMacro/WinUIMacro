using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Helpers;

namespace WinUIMacro;

public sealed partial class MainWindow : Window, IDisposable
{
    private const double MinimumWindowWidth = 800;

    private readonly MacroListPage _macroListPage = new();
    private readonly BindingsPage _bindingsPage = new();
    private MacroDetailPage? _macroDetailPage;
    private DesktopRectangle? _lastRecordButtonBounds;
    private bool _disposed;
    private bool _allowClose;
    private bool _closeInProgress;

    internal MainWindow(MacroWorkspaceViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
        RootSelectorBar.SelectedItem = MacroListSelectorItem;
        ShowMacroList();
    }

    internal MacroWorkspaceViewModel ViewModel { get; }

    internal Task<bool> PrepareForExitAsync(bool forceExit = false) =>
        TryLeaveMacroDetailAsync(navigateToList: false, forceExit);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        AppWindow.Changed -= AppWindow_Changed;
        AppWindow.Closing -= AppWindow_Closing;
        Bindings.StopTracking();
        BackBar.DataContext = null;
        DisposeMacroDetailPage();
        ContentHost.Content = null;
        _macroListPage.Dispose();
        _bindingsPage.Dispose();
    }

    internal void NavigateToMacroDetail(MacroEditorViewModel macro)
    {
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
        await ViewModel.DeleteSelectedAsync();
        ShowMacroList();
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryConsumeRecordButtonClickSuppression())
            return;
        if (ViewModel.SelectedMacro is null || !TryGetRecordButtonBounds(out var bounds))
            return;
        _lastRecordButtonBounds = bounds;
        await ViewModel.ToggleRecordingAsync(bounds);
        if (ViewModel.IsRecording)
            UpdateRecordingBounds();
    }

    private void RecordButton_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateRecordingBounds();

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            var minimumWidth = (int)
                Math.Ceiling(MinimumWindowWidth * Content.XamlRoot.RasterizationScale);
            if (sender.Size.Width < minimumWidth)
                sender.Resize(new SizeInt32(minimumWidth, sender.Size.Height));
        }
        if (args.DidPositionChange || args.DidSizeChange)
            UpdateRecordingBounds();
    }

    private void UpdateRecordingBounds()
    {
        if (
            !ViewModel.IsRecording
            || !TryGetRecordButtonBounds(out var bounds)
            || _lastRecordButtonBounds == bounds
        )
            return;
        _lastRecordButtonBounds = bounds;
        _ = ViewModel.UpdateRecordingBoundsAsync(bounds);
    }

    private bool TryGetRecordButtonBounds(out DesktopRectangle bounds)
    {
        bounds = default;
        if (
            RecordButton.XamlRoot is null
            || RecordButton.ActualWidth <= 0
            || RecordButton.ActualHeight <= 0
        )
            return false;
        try
        {
            var topLeft = RecordButton.ToDesktopPoint(new Point(0, 0));
            var bottomRight = RecordButton.ToDesktopPoint(
                new Point(RecordButton.ActualWidth, RecordButton.ActualHeight)
            );
            bounds = new DesktopRectangle(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
            {
                return;
            }
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
        await ViewModel.StopRecordingIfActiveAsync();
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
