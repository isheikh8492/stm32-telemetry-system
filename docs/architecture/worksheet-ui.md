# Worksheet UI

The worksheet is the canvas where plots live: place by clicking, drag to move, drag corner thumbs to resize, optional grid + snap-to-grid. Single source of truth is [`Worksheet`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Worksheet/Worksheet.cs) (an MVVM view-model), rendered by `WorksheetGrid` + per-plot `PlotItemHost` containers. **It builds no visual tree itself** — the ItemsControl + DataTemplate machinery does that.

## Object graph

```
MainWindowViewModel
 └── Worksheet (VM)
      ├── ObservableCollection<PlotViewModel>   ← bound to ItemsControl
      ├── ObservableCollection<PlotTypeOption>  ← toolbar's Add-X buttons
      ├── PlotTypeRegistry                       ← per-type factories + menus
      └── ViewportSession (when connected)       ← receives plot lifecycle events

PlotViewModel
 ├── PlotSettings          ← shape / range / scale / channel ids
 ├── X, Y, Width, Height   ← canvas position
 ├── ZIndex                ← paint order (bumps on selection)
 └── IsSelected            ← drives thumb visibility

PlotItemHost (one per PlotViewModel; created by ItemsControl)
 ├── PlotItem              ← type-specific axis / labels / WpfPlot
 ├── DynamicBitmap         ← layer that receives blits from RenderingEngine
 ├── DragLayer             ← transparent Border for drag/click capture
 └── 4× Thumb              ← corner-resize handles
```

## Plot lifecycle

1. **Toolbar Add button** clicked → `Worksheet.Arm(factory)` stores a pending settings factory; `IsPlacing = true` flips the cursor to a crosshair.
2. **Empty-canvas click** (caught by `WorksheetGrid.OnCanvasPreviewMouseLeftButtonDown`) → `Worksheet.OnCanvasClick(pos)` builds a `PlotViewModel(settings, x, y, w, h, z)` and adds it to `Plots`.
3. ItemsControl notices the new item → applies the `ItemTemplate` → instantiates a `PlotItemHost`.
4. `PlotItemHost.OnLoaded`:
   - Resolves the type-specific `PlotItem` via `worksheet.Registry.CreateItem(type)`.
   - Wires `DragHandler`, `ThumbManager`, context menu, `PropertyChanged` → thumb visibility.
   - Subscribes to `PlotItem.DataAreaChanged` so the bitmap layer follows the plot's data rect.
   - Calls `worksheet.OnPlotItemReady(viewModel, plotItem)` → `ViewportSession.AddPlot` if a session is bound.
5. **Click-snap loop** (`AlignToGrid`): for the first 6 `DataAreaChanged` events after placement, adjust the host's X/Y/W/H so the **data rect's TL** lands on the grid intersection (not the host's TL). Plot reflow on resize iterates a few rounds; later passes are no-ops once chrome is stable.
6. Plot is removed → `RemovePlot(plotId)` → `Plots.Remove(vm)` → ItemsControl tears down the host → `OnUnloaded` → `worksheet.OnPlotItemReleased(vm)` → `ViewportSession.RemovePlot`.

## Snap-to-grid

`Worksheet.SnapSize` is `40` when `IsSnapEnabled` is true, `0` when off. Every snap consumer (`DragHandler`, `ThumbManager`, `AlignToGrid`, `OnCanvasClick`) checks `s > 0` and skips rounding when off — the toggle "just works".

The snap target is the **data rect's edge**, not the host's. That's why drag/resize use `LastDataArea` (cached from `DataAreaChanged`) and back out to host coords by adding the chrome offset. Without this, a plot's axes would land on a grid line at placement but the plot bitmap inside would be off by ~30 pixels.

## Drag (move)

[`DragHandler.cs`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Worksheet/DragHandler.cs) wires the host's transparent `DragLayer`:

- **MouseLeftButtonDown**: select the plot, capture mouse, record `dragOffset` (cursor → host TL).
- **MouseMove**: cursor in canvas coords → snap the data-rect TL to grid → back out to host X/Y → assign to `ViewModel.X/Y` (clamped non-negative). Bindings translate to `Canvas.Left/Top`.
- **MouseLeftButtonUp**: release capture.

## Resize (corner thumbs)

[`ThumbManager.cs`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Worksheet/ThumbManager.cs) adds 4 `Thumb` controls (TL/TR/BL/BR) into the host's grid at Z=100. Visible only when `IsSelected`.

On `DragStarted`: cache `_initialL/T/R/B` of the host plus chrome offsets `_leftChrome/_topChrome/_rightChrome/_bottomChrome` (host edge minus data rect edge). On `DragDelta`:

```
For the moving edges (TL: left+top, TR: right+top, BL: left+bottom, BR: right+bottom):
  initialDataEdge = initialHostEdge ± chrome
  snappedDataEdge = SnapTo(initialDataEdge + delta, snap)
  newHostEdge     = snappedDataEdge ± chrome
```

Snapping the **data rect** (not the host) keeps the corner thumbs glued to grid intersections through every resize. `MinSize = 50` clamps both new W and H.

## Default layout

`Worksheet.PopulateDefaultLayout()` (toolbar "Default Layout" button) clears the worksheet and drops:

1. **1 oscilloscope** at the top — channel 0, full-width (1040 px).
2. **4 spectral ribbons** (one per param) stacked below — full-width, 256 bins, log scale.
3. **240 histograms** in a 7-column grid: 60 channels × 4 params, channels flow left-to-right, each param fills its own contiguous block of rows.
4. **16 pseudocolors** in a 4×4 grid — PeakHeight × Area, channel `i` for `i ∈ [0, 16)`, 256 bins, log/log.

Hand-tuned sizes (`histW=160 / histH=120 / pcSize=240`) keep the whole thing legible without scrolling on a 1080p+ display.

## Selection & ZIndex

`Selected = vm` raises the previous selection's `IsSelected = false` and the new one's `IsSelected = true`, plus bumps `ZIndex = _nextZIndex++` so the freshly-clicked plot floats on top. ItemsControl's `ItemContainerStyle` binds `Panel.ZIndex` to `ZIndex`.

## Click-to-place state machine

Clicking on a `PlotItemHost` is intercepted by its `DragLayer` and never reaches `WorksheetGrid` (the layer marks `e.Handled`). Empty-canvas clicks bubble up because nothing on top consumes them. `WorksheetGrid` uses **PreviewMouseLeftButtonDown** so it sees the click before any normal handler can mark it handled, walks up the visual tree, and only forwards to `Worksheet.OnCanvasClick` if no ancestor is a `PlotItemHost`.

## Toolbar registration

Each plot type's `<Type>Plot.cs` calls `worksheet.RegisterPlotType<TSettings, TItem>(type, label, defaultSize, createSettings, createItem, menuBuilder)`. The worksheet adds a `PlotTypeOption(label, AddCommand)` entry — the toolbar `ItemsControl` materializes one button per entry. **Adding a new plot type adds a button automatically.**

## Stats panel

[`PipelineStatsViewModel`](../../desktop/TelemetryPipeline/Telemetry.Viewer/ViewModels/PipelineStatsViewModel.cs) ticks once a second on a `DispatcherTimer`. Reads `Buffer.TotalAppended` for the running event count + rate, and `Viewport.GetProcessingTimes()` / `GetRenderingTimes()` for per-PlotType average ms. The "Clear Memory" button calls `Stats.Reset()` which zeros the displayed counters and re-baselines the rate calc so deltas come out as 0.

## Channel catalog

[`ChannelCatalog`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Channels/ChannelCatalog.cs) is the app-lifetime singleton mapping channel id → name + color. Loaded from `channels.json` next to the EXE if present; otherwise seeded with 60 ADC defaults (golden-angle hue distribution) and a starter file written. Plot views, processors, and dialogs all read it without DI plumbing.
