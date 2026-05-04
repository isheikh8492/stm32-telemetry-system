using System.Windows;
using System.Windows.Input;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Worksheet;

public sealed record PlotTypeOption(string Label, ICommand AddCommand);

// Catalog of every plot type the app knows about. Each `<Type>Plot.Register`
// adds: factories for settings/view, menu shape, default size. Looked up by
// PlotItemHost (factory + menu) and Worksheet (default size on placement).
//
// Pure data — no behavior beyond Register/lookup. Toolbar's Add-X buttons
// and click-to-drop state live on Worksheet.
public sealed class PlotTypeRegistry
{
    private readonly Dictionary<PlotType, Func<PlotItem>> _itemFactories = new();
    private readonly Dictionary<PlotType, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();
    private readonly Dictionary<PlotType, Size> _defaultSizes = new();

    public void Register<TSettings, TItem>(
        PlotType type,
        Size defaultSize,
        Func<TSettings> createSettings,
        Func<TItem> createItem,
        Func<TSettings, IReadOnlyList<ContextMenuProvider>> menuBuilder)
        where TSettings : PlotSettings
        where TItem : PlotItem
    {
        _itemFactories[type] = () => createItem();
        _menus[type]         = s => menuBuilder((TSettings)s);
        _defaultSizes[type]  = defaultSize;
    }

    public PlotItem? CreateItem(PlotType type)
        => _itemFactories.TryGetValue(type, out var f) ? f() : null;

    public Size DefaultSize(PlotType type)
        => _defaultSizes.TryGetValue(type, out var s) ? s : new Size(400, 200);

    public IReadOnlyList<ContextMenuProvider> MenuFor(PlotSettings settings)
        => _menus.TryGetValue(settings.Type, out var b) ? b(settings) : Array.Empty<ContextMenuProvider>();
}
