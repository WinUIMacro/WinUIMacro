namespace WinUIMacro.UI;

public sealed partial class MainWindow : Window, IDisposable
{
    private bool _disposed;

    internal MainWindow(MacroWorkspaceViewModel viewModel, bool isElevated)
    {
        ViewModel = viewModel;
        IsElevated = isElevated;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
        RootSelectorBar.SelectedItem = MacroListSelectorItem;
        ShowMacroList();
    }

    internal MacroWorkspaceViewModel ViewModel { get; }

    internal bool IsElevated { get; }

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
}
