using System.Collections.Specialized;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace WinUIMacro.Views;

public sealed partial class MacroDetailPage : Page, IDisposable
{
    public MacroDetailPage() => InitializeComponent();

    internal MacroWorkspaceViewModel ViewModel { get; private set; } = null!;
    internal MacroEditorViewModel Macro { get; private set; } = null!;

    internal void Activate(MacroWorkspaceViewModel viewModel, MacroEditorViewModel macro)
    {
        if (Macro is not null)
            Macro.Nodes.CollectionChanged -= Nodes_CollectionChanged;
        ViewModel = viewModel;
        Macro = macro;
        Macro.Nodes.CollectionChanged += Nodes_CollectionChanged;
        Bindings.Update();
    }

    public void Dispose()
    {
        if (Macro is not null)
            Macro.Nodes.CollectionChanged -= Nodes_CollectionChanged;
        Bindings.StopTracking();
        NodeGrid.ItemsSource = null;
        ViewModel = null!;
        Macro = null!;
    }

    private void NodeGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (
            !ViewModel.CanEditSequence
            || e.Key is not (VirtualKey.Delete or VirtualKey.Back)
            || e.OriginalSource is TextBox
            || NodeGrid.SelectedItem is not MacroNodeViewModel node
        )
            return;
        var removedIndex = Macro.Nodes.IndexOf(node);
        Macro.Nodes.Remove(node);
        SelectNodeAt(Math.Max(0, removedIndex - 1));
        e.Handled = true;
    }

    private void NodeGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsRecording)
            e.Handled = true;
    }

    private void Nodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ViewModel.IsRecording || e.Action != NotifyCollectionChangedAction.Add)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (Macro.Nodes.LastOrDefault() is { } lastNode)
                NodeGrid.ScrollIntoView(lastNode, ScrollIntoViewAlignment.Default);
        });
    }

    private void SelectNodeAt(int index)
    {
        if (index < 0 || index >= Macro.Nodes.Count)
        {
            NodeGrid.SelectedItem = null;
            return;
        }

        var node = Macro.Nodes[index];
        NodeGrid.SelectedItem = node;
        NodeGrid.ScrollIntoView(node);
        NodeGrid.Focus(FocusState.Programmatic);
    }

    private void EditableNodeDisplay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (
            !ViewModel.CanEditSequence
            || (sender as FrameworkElement)?.Tag is not MacroNodeViewModel node
        )
            return;
        node.BeginEdit();
        NodeGrid.SelectedItem = node;
        e.Handled = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (NodeGrid.ContainerFromItem(node) is not DependencyObject container)
                return;
            var textBox = FindDescendant<TextBox>(container, node);
            if (textBox is null)
                return;
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        });
    }

    private void EditableNodeTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || sender is not TextBox textBox)
            return;
        CommitEdit(textBox);
        NodeGrid.Focus(FocusState.Programmatic);
        e.Handled = true;
    }

    private void EditableNodeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            CommitEdit(textBox);
    }

    private static void CommitEdit(TextBox textBox)
    {
        if (textBox.Tag is MacroNodeViewModel node)
            node.CommitEdit();
    }

    private void Delay_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs e)
    {
        if (
            sender.Tag is MacroNodeViewModel { IsDelay: true }
            && e.NewText.Any(character => !char.IsAsciiDigit(character))
        )
            e.Cancel = true;
    }

    private static T? FindDescendant<T>(DependencyObject root, MacroNodeViewModel node)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (
                child is T match
                && ReferenceEquals(match.Tag, node)
                && match.Visibility == Visibility.Visible
            )
                return match;
            var descendant = FindDescendant<T>(child, node);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }
}
