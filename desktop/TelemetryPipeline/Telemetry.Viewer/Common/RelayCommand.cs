using System.Windows.Input;

namespace Telemetry.Viewer.Common;

// Generic ICommand implementation for ViewModels.
//
// ICommand is the contract WPF buttons (and other input controls) bind to via
// Command="{Binding ...}". Three members:
//   - Execute(parameter)        : the action to perform
//   - CanExecute(parameter)     : whether the action is currently runnable
//                                  (WPF auto-greys-out the button when this is false)
//   - CanExecuteChanged event   : VM raises this when something changes that
//                                  affects CanExecute(); WPF re-queries it.
//
// Implementing ICommand for every action would mean a class per command.
// RelayCommand collapses that to one line: pass execute (and optionally
// canExecute) as delegates, done.
//
// The two constructors cover the two common cases:
//   - parameter-less commands (most buttons)
//   - parameterised commands (CommandParameter passed from XAML)
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) =>
        _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) =>
        _execute(parameter);

    public event EventHandler? CanExecuteChanged;

    // VM calls this after changing state that affects CanExecute() so the bound
    // button re-evaluates and updates its enabled state.
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
