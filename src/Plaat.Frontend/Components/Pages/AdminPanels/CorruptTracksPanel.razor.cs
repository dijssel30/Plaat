using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages.AdminPanels;

public partial class CorruptTracksPanel : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private IReadOnlyCollection<PossiblyCorruptTrackDto> _tracks = [];
    private string? _message;
    private bool _isLoading;

    protected override async Task OnInitializedAsync()
    {
        await Refresh();
    }

    private async Task Refresh()
    {
        _message = null;
        _isLoading = true;

        try
        {
            _tracks = await PlaatClient.GetPossiblyCorruptTracks(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _message = $"Failed to load corrupt tracks: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
}
