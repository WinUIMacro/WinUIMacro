namespace WinUIMacro.Views;

public sealed partial class BindingsPage : Page, IDisposable
{
    private const double KeyboardColumnMinWidth = 50;
    private const double KeyboardColumnNarrowMinWidth = 44;
    private const double BindingSurfaceStackWidth = 900;
    private const double BindingSurfaceWideMinWidth = 1280;
    private Flyout? _flyout;
    private BindingKeyViewModel? _activeKey;
    private ListView? _candidateList;
    private AutoSuggestBox? _searchBox;
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
            foreach (var key in ViewModel.KeyboardRows[rowIndex].Keys)
            {
                var button = (Button)template.LoadContent();
                button.DataContext = key;
                button.Tag = key;
                Grid.SetRow(button, rowIndex);
                Grid.SetColumn(button, key.GridColumn);
                Grid.SetColumnSpan(button, key.GridColumnSpan);
                KeyboardGrid.Children.Add(button);
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
        var panel = new StackPanel
        {
            Width = 280,
            Spacing = 8,
            Padding = new Thickness(4),
        };
        if (key.IsBound)
        {
            var unbind = new Button
            {
                Content = "取消绑定",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            unbind.Click += Unbind_Click;
            panel.Children.Add(unbind);
        }
        _searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索宏",
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        _searchBox.TextChanged += SearchBox_TextChanged;
        panel.Children.Add(_searchBox);
        _candidateList = new ListView
        {
            MaxHeight = 320,
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            DisplayMemberPath = "Name",
        };
        _candidateList.ItemClick += CandidateList_ItemClick;
        panel.Children.Add(_candidateList);
        _flyout = new Flyout { Content = panel };
        _flyout.Closed += Flyout_Closed;
        RefreshCandidates();
        _flyout.ShowAt(element);
        _searchBox.Focus(FocusState.Programmatic);
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
        if (_candidateList is not null)
            _candidateList.ItemsSource = ViewModel.FindBindableMacros(_searchBox?.Text).ToArray();
    }

    private void Flyout_Closed(object? sender, object e) => ClearFlyout();

    private void CloseFlyout()
    {
        var flyout = _flyout;
        ClearFlyout();
        flyout?.Hide();
    }

    private void ClearFlyout()
    {
        if (_searchBox is not null)
            _searchBox.TextChanged -= SearchBox_TextChanged;
        if (_candidateList is not null)
        {
            _candidateList.ItemClick -= CandidateList_ItemClick;
            _candidateList.ItemsSource = null;
        }
        if (_flyout is not null)
        {
            _flyout.Closed -= Flyout_Closed;
            _flyout.Content = null;
        }
        _flyout = null;
        _activeKey = null;
        _searchBox = null;
        _candidateList = null;
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

        var stacked = width < BindingSurfaceStackWidth;
        foreach (var column in KeyboardGrid.ColumnDefinitions)
            column.MinWidth = stacked ? KeyboardColumnNarrowMinWidth : KeyboardColumnMinWidth;
        BindingSurface.ColumnSpacing = stacked ? 0 : 28;
        MouseColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(220);
        KeyboardColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(MousePanel, 0);
        Grid.SetColumn(MousePanel, 0);
        Grid.SetColumnSpan(MousePanel, stacked ? 2 : 1);
        Grid.SetRow(KeyboardGrid, stacked ? 1 : 0);
        Grid.SetColumn(KeyboardGrid, stacked ? 0 : 1);
        Grid.SetColumnSpan(KeyboardGrid, stacked ? 2 : 1);
        BindingSurface.MinWidth = stacked
            ? KeyboardLayout.ColumnCount * KeyboardColumnNarrowMinWidth
            : BindingSurfaceWideMinWidth;
        BindingSurface.Width = Math.Max(BindingSurface.MinWidth, width);
    }
}
