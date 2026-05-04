using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Telemetry.Viewer.Services.Channels;

// App-lifetime catalog of channel descriptors. Static singleton so anything
// — UI thread (plot views, dialogs) or worker thread (plot processors) —
// can read it without DI plumbing. Configure once at App.OnStartup; all
// reads after that are lock-free against the frozen list shape (entries
// themselves are still mutable + observable for renames/recolors).
public static class ChannelCatalog
{
    private static IReadOnlyList<ChannelDescriptor> _channels = Array.Empty<ChannelDescriptor>();

    public static IReadOnlyList<ChannelDescriptor> All => _channels;
    public static int Count => _channels.Count;
    public static ChannelDescriptor Get(int id) => _channels[id];

    // Seed with `count` defaults: ADC0..ADC{count-1}, spectrum colors red→violet.
    public static void Configure(int count)
    {
        _channels = Enumerable.Range(0, count)
            .Select(i => new ChannelDescriptor(i, $"ADC{i}", SpectrumColor(i, count)))
            .ToList();
    }

    // Load from JSON if present; otherwise fall back to defaults AND write a
    // starter file at the given path so the user has something to edit.
    // Schema: { "channels": [ { "name": "ADC0", "color": "#FF0000" }, ... ] }
    public static void LoadFrom(string path, int fallbackCount)
    {
        if (!File.Exists(path))
        {
            Configure(fallbackCount);
            TryWriteDefaults(path);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<ChannelsFile>(json);
            var entries = doc?.Channels;
            if (entries is null || entries.Length == 0)
            {
                Configure(fallbackCount);
                return;
            }

            _channels = entries.Select((c, i) => new ChannelDescriptor(
                id:    i,
                name:  string.IsNullOrWhiteSpace(c.Name) ? $"ADC{i}" : c.Name!,
                color: ParseColor(c.Color) ?? SpectrumColor(i, entries.Length))).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChannelCatalog] Failed to load {path}: {ex.Message}; using defaults.");
            Configure(fallbackCount);
        }
    }

    // ---- color helpers ----

    // Golden-angle hue distribution (≈137.508° per id step) — neighbouring
    // ids land far apart in hue space, so two adjacent channels never look
    // similar. Over many ids the hues spread evenly around the wheel without
    // ever clustering. Saturation/Value pulled in slightly from full so the
    // colours read as distinct ink-on-paper rather than blown-out neon.
    private const double GoldenAngleDegrees = 137.5077640500378;
    private static Color SpectrumColor(int id, int _)
    {
        var hue = (id * GoldenAngleDegrees) % 360.0;
        return HsvToRgb(hue, 0.78, 0.92);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var hh = h / 60.0;
        var x = c * (1 - Math.Abs((hh % 2) - 1));
        double r = 0, g = 0, b = 0;
        switch ((int)Math.Floor(hh))
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            case 5: r = c; b = x; break;
        }
        var m = v - c;
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }

    // ---- starter file ----

    private static void TryWriteDefaults(string path)
    {
        try
        {
            var entries = _channels
                .Select(c => new ChannelEntry(c.Name, $"#{c.Color.R:X2}{c.Color.G:X2}{c.Color.B:X2}"))
                .ToArray();
            var doc = new ChannelsFile(entries);
            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChannelCatalog] Failed to write starter {path}: {ex.Message}");
        }
    }

    // ---- JSON schema ----

    private sealed record ChannelsFile([property: JsonPropertyName("channels")] ChannelEntry[] Channels);
    private sealed record ChannelEntry(
        [property: JsonPropertyName("name")]  string? Name,
        [property: JsonPropertyName("color")] string? Color);
}
