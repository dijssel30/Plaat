using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Selectors;

/// <summary>
/// Picks the row template for the Search page's single flat, virtualized results list. The results
/// used to be three separate CollectionViews stacked in a ScrollView with hand-computed
/// HeightRequests - that disables virtualization (every row realized at once), which janks the
/// scrolling and, on a large library where a common query fills every section, can exhaust memory.
/// One CollectionView over a heterogeneous list keeps virtualization; this selector maps each item
/// to its template.
/// </summary>
public sealed class SearchItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate HeaderTemplate { get; set; } = null!;

    public DataTemplate ArtistTemplate { get; set; } = null!;

    public DataTemplate AlbumTemplate { get; set; } = null!;

    public DataTemplate TrackTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            SearchHeader => HeaderTemplate,
            ArtistOnlyDto => ArtistTemplate,
            AlbumOnlyDto => AlbumTemplate,
            _ => TrackTemplate
        };
    }
}
