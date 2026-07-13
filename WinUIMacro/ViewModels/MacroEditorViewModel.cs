using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using WinUIMacro.Engine.Models;

namespace WinUIMacro.ViewModels;

public partial class MacroEditorViewModel : ObservableObject
{
    private readonly string _initialName;
    private readonly Dictionary<MacroNodeViewModel, long> _nodeDelays = [];
    private MacroDefinition? _savedSnapshot;
    private long _totalDelayMilliseconds;

    public MacroEditorViewModel(MacroDefinition definition, bool isPersisted)
    {
        Id = definition.Id;
        _initialName = definition.Name;
        Name = definition.Name;
        ModeIndex = (int)definition.Mode;
        LastEditedAt = definition.LastEditedAt;
        _savedSnapshot = isPersisted ? Clone(definition) : null;
        Nodes.CollectionChanged += Nodes_CollectionChanged;
        foreach (var node in definition.Nodes)
            Nodes.Add(new MacroNodeViewModel(node.Type, node.Value));
        IsDirty = !isPersisted;
    }

    public Guid Id { get; }
    public ObservableCollection<MacroNodeViewModel> Nodes { get; } = [];
    public DateTimeOffset LastEditedAt { get; private set; }
    public string? SavedName => _savedSnapshot?.Name;
    public bool IsPersisted => _savedSnapshot is not null;
    public bool IsNew => _savedSnapshot is null;
    public bool IsEmptyUnmodifiedDraft =>
        IsNew
        && Nodes.Count == 0
        && string.Equals(Name, _initialName, StringComparison.Ordinal)
        && ModeIndex == (int)MacroPlaybackMode.Once;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名宏" : Name;
    public string TotalDurationDisplay =>
        MacroValueFormatter.FormatDuration(_totalDelayMilliseconds);
    public bool IsOnceMode
    {
        get => ModeIndex == 0;
        set
        {
            if (value)
                ModeIndex = 0;
        }
    }
    public bool IsToggleMode
    {
        get => ModeIndex == 1;
        set
        {
            if (value)
                ModeIndex = 1;
        }
    }
    public bool IsHoldMode
    {
        get => ModeIndex == 2;
        set
        {
            if (value)
                ModeIndex = 2;
        }
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string BindingSummary { get; set; } = "未绑定";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoundBackgroundOpacity))]
    public partial bool IsBound { get; set; }

    public double BoundBackgroundOpacity => IsBound ? 0.18 : 0;

    partial void OnNameChanged(string value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnModeIndexChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsOnceMode));
        OnPropertyChanged(nameof(IsToggleMode));
        OnPropertyChanged(nameof(IsHoldMode));
    }

    public MacroDefinition ToDefinition()
    {
        foreach (var node in Nodes.Where(node => node.IsEditing))
            node.CommitEdit();
        return new MacroDefinition(
            Id,
            Name,
            (MacroPlaybackMode)Math.Clamp(ModeIndex, 0, 2),
            [.. Nodes.Select(node => node.ToModel())],
            LastEditedAt
        );
    }

    public void MarkSaved(MacroDefinition definition)
    {
        _savedSnapshot = Clone(definition);
        LastEditedAt = definition.LastEditedAt;
        if (!string.Equals(Name, definition.Name, StringComparison.Ordinal))
            Name = definition.Name;
        IsDirty = false;
        OnPropertyChanged(nameof(SavedName));
        OnPropertyChanged(nameof(IsPersisted));
    }

    public void AddNode(MacroNode node) => Nodes.Add(new MacroNodeViewModel(node.Type, node.Value));

    public void RevertToSavedSnapshot()
    {
        if (_savedSnapshot is null)
            return;
        Name = _savedSnapshot.Name;
        ModeIndex = (int)_savedSnapshot.Mode;
        Nodes.Clear();
        foreach (var node in _savedSnapshot.Nodes)
            Nodes.Add(new MacroNodeViewModel(node.Type, node.Value));
        IsDirty = false;
    }

    private void Nodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var previousTotal = _totalDelayMilliseconds;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var node in _nodeDelays.Keys)
                node.PropertyChanged -= Node_PropertyChanged;
            _nodeDelays.Clear();
            _totalDelayMilliseconds = 0;
        }
        else
        {
            if (args.OldItems is not null)
                foreach (MacroNodeViewModel node in args.OldItems)
                {
                    node.PropertyChanged -= Node_PropertyChanged;
                    _nodeDelays.Remove(node, out var delay);
                    _totalDelayMilliseconds -= delay;
                }
            if (args.NewItems is not null)
                foreach (MacroNodeViewModel node in args.NewItems)
                {
                    node.PropertyChanged += Node_PropertyChanged;
                    var delay = node.DelayMilliseconds;
                    _nodeDelays[node] = delay;
                    _totalDelayMilliseconds += delay;
                }
        }
        IsDirty = true;
        if (_totalDelayMilliseconds != previousTotal)
            OnPropertyChanged(nameof(TotalDurationDisplay));
    }

    private void Node_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName != nameof(MacroNodeViewModel.Value)
            || sender is not MacroNodeViewModel node
        )
            return;

        IsDirty = true;
        var previousDelay = _nodeDelays[node];
        var currentDelay = node.DelayMilliseconds;
        if (currentDelay != previousDelay)
        {
            _nodeDelays[node] = currentDelay;
            _totalDelayMilliseconds += currentDelay - previousDelay;
            OnPropertyChanged(nameof(TotalDurationDisplay));
        }
    }

    private static MacroDefinition Clone(MacroDefinition definition) =>
        definition with
        {
            Nodes = [.. definition.Nodes.Select(node => new MacroNode(node.Type, node.Value))],
        };
}
