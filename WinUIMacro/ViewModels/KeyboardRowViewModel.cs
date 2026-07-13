using System.Collections.ObjectModel;

namespace WinUIMacro.ViewModels;

public sealed class KeyboardRowViewModel
{
    public ObservableCollection<BindingKeyViewModel> Keys { get; } = [];
}
