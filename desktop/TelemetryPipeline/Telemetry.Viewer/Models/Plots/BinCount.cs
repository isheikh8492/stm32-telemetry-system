namespace Telemetry.Viewer.Models.Plots;

// Allowed bin counts for any binned plot (Histogram, Pseudocolor,
// SpectralRibbon). Single source of truth — dialogs populate combo boxes
// from Enum.GetValues<BinCount>(), settings store one of these values,
// processing reads the int via (int)cast. Powers of two only.
public enum BinCount
{
    Bins64  = 64,
    Bins128 = 128,
    Bins256 = 256,
    Bins512 = 512,
}
