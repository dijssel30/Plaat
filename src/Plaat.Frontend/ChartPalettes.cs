using MudBlazor;

namespace Plaat.Frontend;

/// <summary>
/// Chart colours for the Aurora theme. MudBlazor's default palette is a set of
/// bright primaries that clash with the dark surface, so every chart passes one
/// of these.
/// </summary>
public static class ChartPalettes
{
    /// <summary>
    /// Single-series charts (play history) use the accent on its own.
    /// </summary>
    private static readonly string[] SingleSeries = ["#ff6a00"];

    public static ChartOptions Bar()
    {
        return new ChartOptions { ChartPalette = SingleSeries };
    }
}
