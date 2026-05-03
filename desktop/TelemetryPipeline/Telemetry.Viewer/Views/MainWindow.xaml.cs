using System.Windows;
using Telemetry.Viewer.ViewModels;

namespace Telemetry.Viewer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Pure View — DI supplies the ViewModel; XAML bindings do everything else.
    /// The only code-behind is the post-Loaded handshake that hands the
    /// OscilloscopePlotView (a UI element, not DI-managed) to the VM so it can
    /// register the plot as a render target with the viewport.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += (_, _) => _viewModel.AttachOscilloscopePlot(oscilloscopePlotView);
        }
    }
}
