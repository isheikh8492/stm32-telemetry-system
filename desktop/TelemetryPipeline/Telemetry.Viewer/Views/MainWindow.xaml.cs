using System.Windows;
using Telemetry.Viewer.ViewModels;

namespace Telemetry.Viewer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Pure View — DI supplies the ViewModel; XAML bindings do everything else.
    /// Plot views inside the worksheet self-register with the VM on Loaded
    /// (they walk up the visual tree to find this window's DataContext).
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += (_, _) => viewModel.Initialize();
        }
    }
}
