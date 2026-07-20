// 表示绑定页面中的一个可触发键及其当前绑定宏。
using System.ComponentModel;

namespace WinUIMacro.UI.ViewModels;

internal partial class BindingKeyViewModel(string value, string displayText) : ObservableObject
{
    public string Value { get; } = value;
    public string DisplayText { get; } = displayText;

    public int GridColumn { get; internal set; }
    public int GridColumnSpan { get; internal set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoundMacroName))]
    [NotifyPropertyChangedFor(nameof(IsBound))]
    [NotifyPropertyChangedFor(nameof(BoundBackgroundOpacity))]
    public partial MacroEditorViewModel? BoundMacro { get; set; }

    public string BoundMacroName => BoundMacro?.Name ?? "未绑定";
    public bool IsBound => BoundMacro is not null;
    public double BoundBackgroundOpacity => IsBound ? 0.18 : 0;

    partial void OnBoundMacroChanging(MacroEditorViewModel? value)
    {
        if (BoundMacro is not null)
            BoundMacro.PropertyChanged -= BoundMacro_PropertyChanged;
    }

    partial void OnBoundMacroChanged(MacroEditorViewModel? value)
    {
        if (value is not null)
            value.PropertyChanged += BoundMacro_PropertyChanged;
    }

    private void BoundMacro_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MacroEditorViewModel.Name) or null or "")
            OnPropertyChanged(nameof(BoundMacroName));
    }
}
