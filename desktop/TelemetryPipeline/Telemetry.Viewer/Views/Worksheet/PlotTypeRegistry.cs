using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Worksheet;

public sealed record PlotTypeOption(string Label, ICommand AddCommand);

// Catalog of every plot type the app knows about. Each `<Type>Plot.Register`
// call hits this once at startup with: a toolbar label, a default size, the
// settings/view factories, and the menu shape. Lookups (factory, default
// size, menu builder) and the toolbar's bindable PlotTypes list both come
// from here.
//
// Single-responsibility: this is the only place that holds per-type
// dictionaries. Worksheet/PlotPlacementController/PlotItemHost read it via
// `For(...)`; they don't carry per-type maps of their own.
public sealed class PlotTypeRegistry
{
    public ObservableCollection<PlotTypeOption> PlotTypes { get; } = new();

    private readonly Dictionary<PlotType, Func<PlotSettings>> _settingsFactories = new();
    private readonly Dictionary<PlotType, Func<PlotItem>> _itemFactories = new();
    private readonly Dictionary<PlotType, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();
    private readonly Dictionary<PlotType, Size> _defaultSizes = new();

    public void Register<TSettings, TItem>(
        PlotType type,
        string label,
        Size defaultSize,
        Func<TSettings> createSettings,
        Func<TItem> createItem,
        Func<TSettings, IReadOnlyList<ContextMenuProvider>> menuBuilder,
        Action<Func<PlotSettings>> onAddCommand)
        where TSettings : PlotSettings
        where TItem : PlotItem
    {
        _settingsFactories[type] = () => createSettings();
        _itemFactories[type]     = () => createItem();
        _menus[type]             = s => menuBuilder((TSettings)s);
        _defaultSizes[type]      = defaultSize;

        PlotTypes.Add(new PlotTypeOption(
            Label: label,
            AddCommand: new RelayCommand(() => onAddCommand(() => createSettings()))));
    }

    public PlotItem? CreateItem(PlotType type)
        => _itemFactories.TryGetValue(type, out var f) ? f() : null;

    public Size DefaultSize(PlotType type)
        => _defaultSizes.TryGetValue(type, out var s) ? s : new Size(400, 200);

    public IReadOnlyList<ContextMenuProvider> MenuFor(PlotSettings settings)
        => _menus.TryGetValue(settings.Type, out var b) ? b(settings) : Array.Empty<ContextMenuProvider>();
}
