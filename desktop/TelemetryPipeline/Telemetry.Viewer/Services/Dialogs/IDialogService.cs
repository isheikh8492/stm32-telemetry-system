namespace Telemetry.Viewer.Services.Dialogs;

// Abstracts WPF MessageBox so VMs can report errors without referencing
// System.Windows. Easy to fake in tests; easy to swap for a non-modal toast
// system later without touching every caller.
public interface IDialogService
{
    void ShowError(string message, string title);
}

public sealed class WpfDialogService : IDialogService
{
    public void ShowError(string message, string title) =>
        System.Windows.MessageBox.Show(
            message, title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
}
