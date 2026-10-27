using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Plaat.Frontend.Components;

/// <summary>
/// A column chart over a fixed sequence of buckets: a y axis of round gridline values, one bar per
/// bucket flexing to share whatever width the panel has, and a sparse x axis underneath. Used for
/// every "counts over buckets" series on the stats page - plays per day, plays per hour of the day,
/// tracks in the library per month.
/// </summary>
public partial class BarChart : ComponentBase
{
    /// <summary>
    /// Bucket labels. Must line up one-to-one with <see cref="Counts"/>.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Labels { get; set; } = [];

    [Parameter]
    public IReadOnlyList<int> Counts { get; set; } = [];

    /// <summary>
    /// What a count counts, for the per-bar tooltip ("plays", "tracks").
    /// </summary>
    [Parameter]
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Bar colour, as any CSS colour value.
    /// </summary>
    [Parameter]
    public string Accent { get; set; } = "var(--accent)";

    /// <summary>
    /// Bar colour while hovered.
    /// </summary>
    [Parameter]
    public string HoverAccent { get; set; } = "var(--accent-2)";

    /// <summary>
    /// How many gaps the y axis is divided into.
    /// </summary>
    private const int AxisIntervals = 4;

    /// <summary>
    /// The most x-axis labels drawn. The x axis is deliberately sparse: one label per bar turns
    /// into an unreadable smear as soon as there are more than a week's worth, so only every n-th
    /// bucket is named.
    /// </summary>
    private const int MaxXAxisLabels = 8;

    private int[] _ticks = [0];
    private int _axisMax = 1;
    private int _labelStride = 1;

    protected override void OnParametersSet()
    {
        _ticks = BuildAxisTicks(Counts.Count == 0 ? 0 : Counts.Max());
        _axisMax = Math.Max(1, _ticks[^1]);
        _labelStride = Math.Max(1, (int)Math.Ceiling(Labels.Count / (double)MaxXAxisLabels));
    }

    /// <summary>
    /// Gridline values from 0 up to a round number at or above <paramref name="max"/>, stepping by
    /// a 1/2/5 x 10^n interval so the axis reads as whole numbers rather than as whatever the tallest
    /// bar happens to be.
    /// </summary>
    private static int[] BuildAxisTicks(int max)
    {
        if (max <= 0)
        {
            return [0];
        }

        var rough = max / (double)AxisIntervals;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalised = rough / magnitude;
        var step = Math.Max(1, (int)(magnitude * (normalised switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10
        })));
        var top = step * (int)Math.Ceiling(max / (double)step);

        return [.. Enumerable.Range(0, (top / step) + 1).Select(i => i * step)];
    }

    /// <summary>
    /// Labelled from the right, so the most recent bucket is always named.
    /// </summary>
    private bool ShowLabel(int index)
    {
        return (Labels.Count - 1 - index) % _labelStride == 0;
    }

    /// <summary>
    /// Every other label, counting back from the most recent - narrow screens drop these so what
    /// is left still has room to breathe (see the media query in BarChart.razor.css).
    /// </summary>
    private bool IsSecondaryLabel(int index)
    {
        return (Labels.Count - 1 - index) / _labelStride % 2 == 1;
    }

    private string Height(int count)
    {
        return (count * 100d / _axisMax).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
