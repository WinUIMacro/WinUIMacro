using System.Globalization;
using WinUIMacro.Engine.Models;

namespace WinUIMacro.ViewModels;

public partial class MacroNodeViewModel(MacroNodeType type, string value) : ObservableObject
{
    public MacroNodeType Type { get; } = type;

    public bool IsEditable => Type is MacroNodeType.Delay or MacroNodeType.Note;
    public bool IsKeyboard => Type is MacroNodeType.KeyDown or MacroNodeType.KeyUp;
    public bool IsMouseButton => Type is MacroNodeType.MouseDown or MacroNodeType.MouseUp;
    public bool IsMouseWheel => Type == MacroNodeType.MouseWheel;
    public bool IsDelay => Type == MacroNodeType.Delay;
    public bool IsNote => Type == MacroNodeType.Note;
    public Visibility PressVisibility =>
        Type is MacroNodeType.KeyDown or MacroNodeType.MouseDown
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility ReleaseVisibility =>
        Type is MacroNodeType.KeyUp or MacroNodeType.MouseUp
            ? Visibility.Visible
            : Visibility.Collapsed;
    public string WheelArrow => Value == "WheelUp" ? "↑" : "↓";
    public long DelayMilliseconds =>
        Type == MacroNodeType.Delay
        && long.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var delay)
            ? delay
            : 0;
    public string DisplayText =>
        Type switch
        {
            MacroNodeType.Delay => MacroValueFormatter.FormatDuration(DelayMilliseconds),
            MacroNodeType.Note => string.IsNullOrWhiteSpace(Value) ? "注释" : Value,
            _ => MacroInputRegistry.GetDisplayText(Value),
        };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(WheelArrow))]
    [NotifyPropertyChangedFor(nameof(DelayMilliseconds))]
    public partial string Value { get; set; } = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditVisibility))]
    [NotifyPropertyChangedFor(nameof(DisplayVisibility))]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    public partial string EditableValue { get; set; } =
        type == MacroNodeType.Delay && string.IsNullOrWhiteSpace(value) ? "0" : value;

    public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseLeftVisibility =>
        Value == "MouseLeft" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseRightVisibility =>
        Value == "MouseRight" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseMiddleVisibility =>
        Value == "MouseMiddle" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseTopVisibility =>
        Value is "MouseX1" or "MouseX2" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MouseSideVisibility =>
        Value is "MouseX1" or "MouseX2" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseX1Visibility =>
        Value == "MouseX1" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseX2Visibility =>
        Value == "MouseX2" ? Visibility.Visible : Visibility.Collapsed;

    public void BeginEdit()
    {
        if (!IsEditable)
            return;
        EditableValue =
            Type == MacroNodeType.Delay
                ? DelayMilliseconds.ToString(CultureInfo.InvariantCulture)
                : Value;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (Type == MacroNodeType.Delay)
            Value = long.TryParse(
                EditableValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var delay
            )
                ? delay.ToString(CultureInfo.InvariantCulture)
                : "0";
        else if (Type == MacroNodeType.Note)
            Value = EditableValue;
        IsEditing = false;
    }

    public MacroNode ToModel() => new(Type, Value);
}
