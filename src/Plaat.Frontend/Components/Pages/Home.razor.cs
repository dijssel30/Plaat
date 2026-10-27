using Microsoft.AspNetCore.Components;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private IReadOnlyList<TrackDto> _recentlyPlayed = [];
    private RecentlyAddedDto _recentlyAdded = new();
    private bool _loaded;

    protected override async Task OnInitializedAsync()
    {
        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        var playedTask = PlaatClient.GetRecentlyPlayed(tokenSource.Token);
        var addedTask = PlaatClient.GetRecentlyAdded(tokenSource.Token);

        await Task.WhenAll(playedTask, addedTask);

        _recentlyPlayed = await playedTask;
        _recentlyAdded = await addedTask;
        _loaded = true;
    }

    private static string GetImg(ulong albumId)
    {
        return $"/proxy/cover/album/{albumId}";
    }
}
