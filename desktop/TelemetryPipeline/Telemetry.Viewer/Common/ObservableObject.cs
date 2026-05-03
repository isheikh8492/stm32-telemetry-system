using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Telemetry.Viewer.Common;

// Base class for ViewModels that participate in WPF data binding.
//
// Implements INotifyPropertyChanged — the contract WPF's binding system listens
// to. When a property's value changes, the binding system needs to know so it
// can refresh any control bound to that property. This is signalled by raising
// the PropertyChanged event with the property name.
//
// SetProperty captures the standard "compare → assign → raise" pattern in one
// call so each property setter shrinks from ~6 lines to one. [CallerMemberName]
// makes the compiler pass the calling property's name automatically — no
// stringly-typed property names, no typo bugs.
//
// Returns true when the value actually changed; useful for chaining
// dependent updates (e.g. raising another property's PropertyChanged or
// refreshing a command's CanExecute).
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    // Manual raise for cases SetProperty can't cover — e.g. computed properties
    // whose dependencies just changed, or batch updates where you want to fire
    // notifications explicitly.
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
