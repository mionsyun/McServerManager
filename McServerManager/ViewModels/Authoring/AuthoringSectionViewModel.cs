using System.Runtime.CompilerServices;
using McServerManager.Utilities;

namespace McServerManager.ViewModels.Authoring;

/// <summary>Editable presentation fields; the service remains the validation and edition boundary.</summary>
public abstract class AuthoringSectionViewModel(Func<bool> canEdit, Action changed) : ObservableObject
{
    private bool _attached = true;
    protected void SetInput<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (_attached && canEdit() && SetProperty(ref field, value, propertyName)) changed();
    }
    internal void Detach() => _attached = false;
}
