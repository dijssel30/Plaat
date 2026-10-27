using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;
using Plaat.Mobile.Services.Player;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Home is now "recently played + recently added" - two bounded lists - instead of the whole
/// track library, so opening the app no longer downloads ~all tracks. Browsing everything moved
/// to the dedicated, server-paged <see cref="TracksViewModel"/>.
/// </summary>
public partial class HomeViewModel(IPlaatClient plaatClient, PlayerQueueService queue) : ViewModelBase
{
    public BulkObservableCollection<TrackDto> RecentlyPlayed { get; } = [];

    public BulkObservableCollection<AlbumOnlyDto> RecentlyAddedAlbums { get; } = [];

    public BulkObservableCollection<TrackDto> RecentlyAddedTracks { get; } = [];

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
            var playedTask = plaatClient.GetRecentlyPlayed(CancellationToken.None);
            var addedTask = plaatClient.GetRecentlyAdded(CancellationToken.None);

            await Task.WhenAll(playedTask, addedTask);

            RecentlyPlayed.ReplaceAll(await playedTask);

            var added = await addedTask;
            RecentlyAddedAlbums.ReplaceAll(added.Albums);
            RecentlyAddedTracks.ReplaceAll(added.Tracks);

            HasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load home: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void PlayFromRecentlyPlayed(TrackDto track)
    {
        PlayFrom(RecentlyPlayed, track);
    }

    public void PlayFromRecentlyAdded(TrackDto track)
    {
        PlayFrom(RecentlyAddedTracks, track);
    }

    private void PlayFrom(BulkObservableCollection<TrackDto> list, TrackDto track)
    {
        var index = list.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        queue.SetPlayList(list);
        queue.Play(index);
    }
}
