// 管理宏列表页面的激活、释放、选择和新建操作。
namespace WinUIMacro.UI.Views;

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

    private async void MacroGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel.CanChangeWorkspace && e.ClickedItem is MacroEditorViewModel macro)
            await (_window?.NavigateToMacroDetailAsync(macro) ?? Task.CompletedTask);
    }

    private async void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanChangeWorkspace)
            return;
        ViewModel.AddMacroCommand.Execute(null);
        if (ViewModel.SelectedMacro is { } macro)
            await (_window?.NavigateToMacroDetailAsync(macro) ?? Task.CompletedTask);
    }
}
