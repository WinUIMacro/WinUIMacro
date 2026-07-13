namespace WinUIMacro.Views;

public sealed partial class MacroListPage : Page, IDisposable
{
    private MainWindow? _window;

    public MacroListPage() => InitializeComponent();

    internal MacroWorkspaceViewModel ViewModel { get; private set; } = null!;

    internal void Activate(MacroWorkspaceViewModel viewModel, MainWindow window)
    {
        ViewModel = viewModel;
        _window = window;
        Bindings.Update();
    }

    public void Dispose()
    {
        Bindings.StopTracking();
        MacroGrid.ItemsSource = null;
        _window = null;
        ViewModel = null!;
    }

    private void MacroGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MacroEditorViewModel macro)
            _window?.NavigateToMacroDetail(macro);
    }

    private void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddMacroCommand.Execute(null);
        if (ViewModel.SelectedMacro is { } macro)
            _window?.NavigateToMacroDetail(macro);
    }
}
