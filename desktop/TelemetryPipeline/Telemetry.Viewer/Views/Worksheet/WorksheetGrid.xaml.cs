using System.Windows;
using System.Windows.Controls;

namespace Telemetry.Viewer.Views.Worksheet;

// UserControl that hosts the worksheet's plot canvas. On Loaded, hands the
// canvas to the bound Worksheet so it can add plot containers to it
// programmatically (drag/resize/z-order all happen on Canvas children).
public partial class WorksheetGrid : UserControl
{
    public WorksheetGrid()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is Worksheet worksheet)
            worksheet.AttachCanvas(WorksheetCanvas);
    }
}
