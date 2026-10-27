using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Simplified native counterpart to Plaat.Frontend/Components/TrackDetailsModal.razor.cs - the file
/// format/location stats and per-location "set as default" action, without the play-history chart or
/// lyrics panel (no charting library wired up in Plaat.Mobile, and neither has an obvious mobile-first
/// layout at this screen size).
/// </summary>
[QueryProperty(nameof(TrackId), "id")]
public partial class TrackDetailsViewModel(IPlaatClient plaatClient) : ViewModelBase
{
    [ObservableProperty]
    public partial uint TrackId { get; set; }

    [ObservableProperty]
    public partial TrackDetailsDto? Details { get; set; }

    [ObservableProperty]
    public partial uint? SettingDefaultLocationId { get; set; }

    public ObservableCollection<TrackLocationDto> Locations { get; } = [];

    public string DurationText
    {
        get
        {
            return Details is null ? string.Empty : $"{(int)Details.Duration.TotalMinutes:D2}:{Details.Duration.Seconds:D2}";
        }
    }

    public string SampleRateText
    {
        get
        {
            return Details is null ? string.Empty : $"{Details.SampleRate / 1000.0:0.#} kHz";
        }
    }

    public string FileSizeText
    {
        get
        {
            return Details is null ? string.Empty : FormatFileSize(Details.FileSizeBytes);
        }
    }

    public string LufsText
    {
        get
        {
            return Details?.IntegratedLoudnessLufs is { } lufs
        ? $"{lufs:0.#} LUFS (×{Details.AttenuationFactor:0.##})"
        : "Not analyzed";
        }
    }

    partial void OnTrackIdChanged(uint value)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Details = await plaatClient.GetTrackDetails(TrackId, CancellationToken.None);
            Title = Details?.Title ?? "Track details";

            Locations.Clear();
            if (Details is not null)
            {
                foreach (var location in Details.Locations)
                {
                    Locations.Add(location);
                }
            }

            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(SampleRateText));
            OnPropertyChanged(nameof(FileSizeText));
            OnPropertyChanged(nameof(LufsText));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load track: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetDefault(TrackLocationDto location)
    {
        SettingDefaultLocationId = location.Id;

        try
        {
            await plaatClient.SetDefaultTrackLocation(TrackId, location.Id, CancellationToken.None);
            await LoadAsync();
        }
        finally
        {
            SettingDefaultLocationId = null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.##} MB",
            >= 1024 => $"{bytes / 1024.0:0.##} KB",
            _ => $"{bytes} B"
        };
    }
}
