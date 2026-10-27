using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Components.Pages;

public partial class Settings : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required ILogger<Settings> Logger { get; init; }

    private string _currentPassword = "";
    private string _newPassword = "";
    private string _confirmPassword = "";
    private string? _passwordMessage;
    private bool _passwordError;
    private bool _isChangingPassword;

    private bool _loudnessNormalizationEnabled = true;
    private bool _isSavingLoudnessNormalization;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var currentUser = await PlaatClient.GetCurrentUser(CancellationToken.None);

            if (currentUser is not null)
            {
                _loudnessNormalizationEnabled = currentUser.LoudnessNormalizationEnabled;
            }
        }
        catch (Exception ex)
        {
            LogLoadSettingsFailed(Logger, ex);
        }
    }

    private async Task OnLoudnessNormalizationChanged(bool enabled)
    {
        var previous = _loudnessNormalizationEnabled;
        _loudnessNormalizationEnabled = enabled;
        _isSavingLoudnessNormalization = true;

        try
        {
            await PlaatClient.SetLoudnessNormalization(enabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogUpdateLoudnessFailed(Logger, ex);
            _loudnessNormalizationEnabled = previous;
        }
        finally
        {
            _isSavingLoudnessNormalization = false;
        }
    }

    private async Task ChangePassword()
    {
        _passwordMessage = null;

        if (string.IsNullOrWhiteSpace(_currentPassword) || string.IsNullOrWhiteSpace(_newPassword))
        {
            _passwordMessage = "Current and new password are required.";
            _passwordError = true;
            return;
        }

        if (_newPassword != _confirmPassword)
        {
            _passwordMessage = "New password and confirmation do not match.";
            _passwordError = true;
            return;
        }

        _isChangingPassword = true;

        try
        {
            var success = await PlaatClient.ChangeOwnPassword(
                new ChangePasswordRequest(_currentPassword, _newPassword),
                CancellationToken.None);

            if (success)
            {
                _passwordMessage = "Password updated successfully.";
                _passwordError = false;
                _currentPassword = "";
                _newPassword = "";
                _confirmPassword = "";
            }
            else
            {
                _passwordMessage = "Current password is incorrect.";
                _passwordError = true;
            }
        }
        catch (Exception ex)
        {
            LogUpdatePasswordFailed(Logger, ex);
            _passwordMessage = "Failed to update password.";
            _passwordError = true;
        }
        finally
        {
            _isChangingPassword = false;
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to load current user settings")]
    private static partial void LogLoadSettingsFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Failed to update loudness normalization setting")]
    private static partial void LogUpdateLoudnessFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Failed to update own password")]
    private static partial void LogUpdatePasswordFailed(ILogger logger, Exception ex);
}
