using System.Runtime.CompilerServices;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Models;

// Mutable settings record for a single worksheet plot. Two-way bindable —
// editing a property fires PropertyChanged so the UI refreshes, and bumps
// Version so ProcessingEngine reprocesses on the next tick.
public abstract class PlotSettings : ObservableObject
{
    public Guid PlotId { get; }

    // Monotonic counter — any property change bumps it. ProcessingEngine
    // fingerprints on (PlotId, Version, EventId) to detect when this plot
    // needs reprocessing.
    public uint Version { get; private set; }

    // Stable plot-kind identifier. Drives processing dispatch + (future)
    // serialization. Each subclass returns a constant.
    public abstract PlotType Type { get; }

    // Human-readable label for tabs / tooltips / debug. Subclasses compute
    // this from their own properties.
    public abstract string DisplayName { get; }

    protected PlotSettings(Guid plotId)
    {
        PlotId = plotId;
    }

    // Shadow ObservableObject.SetProperty so every settings mutation also
    // bumps Version. Subclasses should call this from their setters.
    protected new bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (!base.SetProperty(ref field, value, propertyName))
            return false;
        Version++;
        return true;
    }
}
