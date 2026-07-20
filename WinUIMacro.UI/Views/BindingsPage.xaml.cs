namespace WinUIMacro.UI.Views;

public sealed partial class BindingsPage : Page, IDisposable
{
    private const double KeyboardColumnMinWidth = 50;
    private const double KeyboardColumnNarrowMinWidth = 44;
    private const double BindingSurfaceWideMinWidth = 1160;
    private const double MouseColumnWidth = 160;
    private BindingKeyViewModel? _activeKey;
    private bool _built;

    public BindingsPage() => InitializeComponent();

    internal MacroWorkspaceViewModel ViewModel { get; private set; } = null!;

    internal void Activate(MacroWorkspaceViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
        if (!_built)
        {
            BuildKeyboard();
            _built = true;
        }
        UpdateBindingSurfaceLayout();
    }

    internal void Deactivate() => CloseFlyout();

    public void Dispose()
    {
        CloseFlyout();
        Bindings.StopTracking();
        MouseSideKeysList.ItemsSource = null;
        KeyboardGrid.Children.Clear();
        ViewModel = null!;
    }

    private void BuildKeyboard()
    {
        // 根据视图模型中的行列描述动态生成键盘，避免为每个按键维护重复 XAML。
        foreach (var _ in ViewModel.KeyboardRows)
            KeyboardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < KeyboardLayout.ColumnCount; index++)
            KeyboardGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = KeyboardColumnMinWidth,
                }
            );
        var template = (DataTemplate)Resources["BindingKeyTemplate"];
        for (var rowIndex = 0; rowIndex < ViewModel.KeyboardRows.Count; rowIndex++)
            foreach (var key in ViewModel.KeyboardRows[rowIndex])
            {
                var element = (FrameworkElement)template.LoadContent();
                element.DataContext = key;

                Grid.SetRow(element, rowIndex);
                Grid.SetColumn(element, key.GridColumn);
                Grid.SetColumnSpan(element, key.GridColumnSpan);

                KeyboardGrid.Children.Add(element);
            }
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (
            sender is not FrameworkElement element
            || (element.Tag ?? element.DataContext) is not BindingKeyViewModel key
        )
            return;
        CloseFlyout();
        _activeKey = key;
        UnbindButton.Visibility = key.IsBound ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Text = string.Empty;
        RefreshCandidates();
        BindingFlyout.ShowAt(element);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void Unbind_Click(object sender, RoutedEventArgs e)
    {
        if (_activeKey is { } key)
        {
            CloseFlyout();
            await ViewModel.UnbindTriggerAsync(key);
        }
    }

    private async void CandidateList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_activeKey is { } key && e.ClickedItem is MacroEditorViewModel macro)
        {
            CloseFlyout();
            await ViewModel.BindTriggerAsync(key, macro);
        }
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs e
    ) => RefreshCandidates();

    private void RefreshCandidates()
    {
        CandidateList.ItemsSource = ViewModel.FindBindableMacros(SearchBox.Text).ToArray();
    }

    private void BindingFlyout_Closed(object? sender, object e) => ClearFlyout();

    private void CloseFlyout()
    {
        ClearFlyout();
        BindingFlyout.Hide();
    }

    private void ClearFlyout()
    {
        CandidateList.ItemsSource = null;
        _activeKey = null;
    }

    private void BindingScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateBindingSurfaceLayout();

    private void UpdateBindingSurfaceLayout()
    {
        var width =
            BindingScroll.ViewportWidth > 0
                ? BindingScroll.ViewportWidth
                : BindingScroll.ActualWidth;

        if (width <= 0)
            return;

        var stacked = width < BindingSurfaceWideMinWidth;

        foreach (var column in KeyboardGrid.ColumnDefinitions)
            column.MinWidth = stacked ? KeyboardColumnNarrowMinWidth : KeyboardColumnMinWidth;

        BindingSurface.ColumnSpacing = stacked ? 0 : 20;

        KeyboardColumn.Width = new GridLength(1, GridUnitType.Star);
        MouseColumn.Width = stacked ? new GridLength(0) : new GridLength(MouseColumnWidth);

        // 宽窗口：键盘在左，鼠标在右。
        // 窄窗口：键盘在上，鼠标在下。
        Grid.SetRow(KeyboardGrid, 0);
        Grid.SetColumn(KeyboardGrid, 0);
        Grid.SetColumnSpan(KeyboardGrid, stacked ? 2 : 1);

        Grid.SetRow(MousePanel, stacked ? 1 : 0);
        Grid.SetColumn(MousePanel, stacked ? 0 : 1);
        Grid.SetColumnSpan(MousePanel, stacked ? 2 : 1);

        BindingSurface.MinWidth = stacked
            ? KeyboardLayout.ColumnCount * KeyboardColumnNarrowMinWidth
            : BindingSurfaceWideMinWidth;

        BindingSurface.Width = Math.Max(BindingSurface.MinWidth, width);
    }
}
