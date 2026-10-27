using System.Globalization;
using Microsoft.AspNetCore.Components;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components;

/// <summary>
/// A ranked list of named counts drawn as horizontal bars, each scaled against the largest count
/// in the list. Used wherever a "top N" breakdown would otherwise become a pie: a pie can only
/// carry a handful of slices before its legend wraps and its small slices collapse into slivers,
/// whereas a bar row keeps the name, the number and the relative size readable at any width.
/// </summary>
public partial class RankedBars : ComponentBase
{
    [Parameter]
    public IReadOnlyList<NamedCountDto> Items { get; set; } = [];

    /// <summary>
    /// Bar colour, as any CSS colour value. One colour for the whole list - the bar
    /// length already encodes the count, so colouring per row would say nothing extra.
    /// </summary>
    [Parameter]
    public string Accent { get; set; } = "var(--accent)";

    /// <summary>
    /// What a count counts ("plays", "tracks"), for the per-row tooltip. The same list shape is
    /// used for both, so the bare number beside a name is ambiguous without it.
    /// </summary>
    [Parameter]
    public string Unit { get; set; } = string.Empty;

    private int _max = 1;

    protected override void OnParametersSet()
    {
        _max = Items.Count == 0 ? 1 : Math.Max(1, Items.Max(i => i.Count));
    }

    private string Share(int count)
    {
        return (count * 100d / _max).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
