using Plaat.Contract;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// One disc's worth of tracks, for a grouped CollectionView. MAUI's grouping wants the group
/// itself to be the item collection, hence deriving from List&lt;TrackDto&gt; rather than holding one.
/// An empty <see cref="Name"/> means "don't show a header" (a single-disc album).
/// </summary>
public sealed class TrackDiscGroup(string name, IEnumerable<TrackDto> tracks) : List<TrackDto>(tracks)
{
    public string Name { get; } = name;
}
