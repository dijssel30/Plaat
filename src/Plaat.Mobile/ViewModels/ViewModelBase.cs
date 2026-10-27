using CommunityToolkit.Mvvm.ComponentModel;

namespace Plaat.Mobile.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Set true once a load has completed successfully. Pages check this in OnAppearing so
    /// switching tabs back and forth doesn't re-fetch and rebuild the whole list every time.
    /// </summary>
    public bool HasLoadedOnce { get; protected set; }
}
