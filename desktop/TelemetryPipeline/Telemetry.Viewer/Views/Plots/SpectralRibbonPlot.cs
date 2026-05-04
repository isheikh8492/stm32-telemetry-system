using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Dialogs;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

internal static class SpectralRibbonPlot
{
    public static void Register(Worksheet.Worksheet worksheet, IDialogService dialogs)
    {
        PlotProcessorRegistry.Register(PlotType.SpectralRibbon, new SpectralRibbonPlotProcessor());

        worksheet.RegisterPlotType<SpectralRibbonSettings, SpectralRibbonPlotItem>(
            type:           PlotType.SpectralRibbon,
            label:          "Spectral Ribbon",
            defaultSize:    new Size(1040, 160),
            createSettings: () => new SpectralRibbonSettings(
                plotId:    Guid.NewGuid(),
                channelIds: SelectionStrategy.AllChannelIds(),
                param:     ParamType.PeakHeight,
                binCount:  BinCount.Bins128,
                minRange:  1,
                maxRange:  1_000_000,
                scale:     AxisScale.Logarithmic),
            createItem:     () => new SpectralRibbonPlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s, dialogs))
            });
    }

    private static void OpenProperties(SpectralRibbonSettings settings, IDialogService dialogs)
        => dialogs.ShowDialog(new SpectralRibbonPropertiesDialog(settings));
}
