namespace Telemetry.Viewer.Views.Worksheet;

// Generic single-item selection: registered items get OnSelect/OnDeselect
// callbacks. Used by Worksheet to (a) toggle each plot's resize thumbs and
// (b) bump z-index so the selected plot floats above the others.
internal sealed class SelectionManager<T> where T : class
{
    private readonly Dictionary<T, (Action onSelect, Action onDeselect)> _registrations = new();

    public T? Selected { get; private set; }
    public event Action<T?>? SelectionChanged;

    public void Register(T item, Action onSelect, Action onDeselect)
        => _registrations[item] = (onSelect, onDeselect);

    public void Unregister(T item) => _registrations.Remove(item);

    public void Select(T? item)
    {
        if (ReferenceEquals(Selected, item)) return;

        if (Selected is not null && _registrations.TryGetValue(Selected, out var prev))
            prev.onDeselect();

        Selected = item;

        if (item is not null && _registrations.TryGetValue(item, out var cur))
            cur.onSelect();

        SelectionChanged?.Invoke(item);
    }
}
