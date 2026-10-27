using CommunityToolkit.Mvvm.Input;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;

namespace Plaat.Mobile.ViewModels;

public partial class PlaylistsViewModel(IPlaatClient plaatClient) : ViewModelBase
{
    private List<PlaylistOnlyDto> _all = [];

    public BulkObservableCollection<PlaylistOnlyDto> Playlists { get; } = [];

    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilter();
            }
        }
    } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var playlists = await plaatClient.GetPlaylists(CancellationToken.None);
            List<PlaylistOnlyDto> sorted =
            [
                .. playlists
                    .OrderByDescending(p => p.IsPinned)
                    .ThenBy(p => p.PlaylistName, StringComparer.OrdinalIgnoreCase)
            ];

            // The page reloads on every appearance (to pick up playlists created and tracks added
            // elsewhere) - skip the list Reset, and the scroll jump it causes, when nothing changed.
            if (!HasLoadedOnce || !sorted.Select(Summarize).SequenceEqual(_all.Select(Summarize)))
            {
                _all = sorted;
                ApplyFilter();
            }

            HasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load playlists: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static (uint Id, string Name, bool IsSmart, bool IsPinned, int TrackCount) Summarize(PlaylistOnlyDto playlist)
    {
        return (playlist.Id, playlist.PlaylistName, playlist.IsSmart, playlist.IsPinned, playlist.TrackCount);
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(Search)
            ? _all
            : [.. _all
                .Select(p => (Item: p, Score: SearchScoring.SearchScore(Search, p.PlaylistName)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];

        Playlists.ReplaceAll(filtered);
    }
}
