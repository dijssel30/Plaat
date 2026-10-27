using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Mobile.Services;
using Plaat.Mobile.Services.Auth;
using Plaat.Mobile.Services.Player;

namespace Plaat.Mobile.ViewModels;

public partial class LoginViewModel(AuthService authService, PlaybackStateSyncService stateSync, PlaatBackendOptions backendOptions) : ViewModelBase
{
    [ObservableProperty]
    public partial string ServerAddress { get; set; } = backendOptions.BaseUri.ToString();

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    public event Action? LoginSucceeded;

    [RelayCommand]
    private async Task Login()
    {
        if (!Uri.TryCreate(ServerAddress.Trim(), UriKind.Absolute, out var serverUri))
        {
            ErrorMessage = "Enter a valid server address, e.g. http://192.168.1.10:5014";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter a username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            // Applied to the shared PlaatBackendOptions singleton before logging in, so a changed
            // address takes effect immediately for this login attempt - no rebuild or restart needed.
            backendOptions.BaseUri = serverUri;
            ServerAddressStore.Save(serverUri);

            var success = await authService.Login(Username.Trim(), Password, CancellationToken.None);

            if (!success)
            {
                ErrorMessage = "Invalid username or password.";
                return;
            }

            Password = string.Empty;

            await stateSync.RestoreAsync(CancellationToken.None);
            stateSync.StartSyncLoop();

            LoginSucceeded?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
