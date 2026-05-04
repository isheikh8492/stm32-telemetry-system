using System.Windows;

namespace Telemetry.Viewer.Services.Dialogs;

// Abstracts WPF dialogs so VMs and per-plot wiring don't need to reach
// `Application.Current.MainWindow` (which is brittle in tests and during
// shutdown). Easy to fake in tests; easy to swap for a non-modal toast
// system later without touching every caller.
public interface IDialogService
{
    void ShowError(string message, string title);

    // Shows a modal Window owned by the app's main window. Returns the
    // dialog's DialogResult.
    bool? ShowDialog(Window dialog);
}

public sealed class WpfDialogService : IDialogService
{
    public void ShowError(string message, string title) =>
        MessageBox.Show(
            message, title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    // Single point in the codebase that reaches `Application.Current.MainWindow`.
    public bool? ShowDialog(Window dialog)
    {
        dialog.Owner = Application.Current?.MainWindow;
        return dialog.ShowDialog();
    }
}
