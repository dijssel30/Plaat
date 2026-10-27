using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Components.Pages.AdminPanels;

public partial class ManageUsersPanel : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required ILogger<ManageUsersPanel> Logger { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationStateTask { get; set; }

    private string? _currentUsername;

    private IReadOnlyCollection<UserSummaryDto> _users = [];

    private string _newUsername = "";
    private string _newUserPassword = "";
    private bool _newUserIsAdmin;
    private string? _adminMessage;
    private bool _adminError;
    private bool _isCreatingUser;

    private readonly Dictionary<string, string> _resetPasswordByUsername = new();
    private string? _busyUsername;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateTask;
        _currentUsername = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        _users = await PlaatClient.GetUsers(CancellationToken.None);
    }

    private async Task CreateUser()
    {
        _adminMessage = null;

        if (string.IsNullOrWhiteSpace(_newUsername) || string.IsNullOrWhiteSpace(_newUserPassword))
        {
            _adminMessage = "Username and password are required.";
            _adminError = true;
            return;
        }

        _isCreatingUser = true;

        try
        {
            await PlaatClient.CreateUser(
                new CreateUserRequest
                {
                    Username = _newUsername,
                    Password = _newUserPassword,
                    IsAdmin = _newUserIsAdmin
                },
                CancellationToken.None);

            _adminMessage = $"User '{_newUsername}' created successfully.";
            _adminError = false;
            _newUsername = "";
            _newUserPassword = "";
            _newUserIsAdmin = false;

            await LoadUsers();
        }
        catch (Exception ex)
        {
            LogCreateUserFailed(Logger, ex, _newUsername);
            _adminMessage = "Failed to create user.";
            _adminError = true;
        }
        finally
        {
            _isCreatingUser = false;
        }
    }

    private string GetResetPassword(string username)
    {
        return _resetPasswordByUsername.GetValueOrDefault(username, "");
    }

    private void SetResetPassword(string username, string value)
    {
        _resetPasswordByUsername[username] = value;
    }

    private async Task ResetPassword(string username)
    {
        var newPassword = GetResetPassword(username);

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return;
        }

        _busyUsername = username;

        try
        {
            await PlaatClient.SetUserPassword(
                username,
                new SetPasswordRequest(newPassword),
                CancellationToken.None);

            _adminMessage = $"Password for '{username}' updated.";
            _adminError = false;
            _resetPasswordByUsername[username] = "";
        }
        catch (Exception ex)
        {
            LogResetPasswordFailed(Logger, ex, username);
            _adminMessage = $"Failed to reset password for '{username}'.";
            _adminError = true;
        }
        finally
        {
            _busyUsername = null;
        }
    }

    private async Task ToggleAdmin(UserSummaryDto user)
    {
        _busyUsername = user.Username;

        try
        {
            await PlaatClient.SetUserAdmin(user.Username, !user.IsAdmin, CancellationToken.None);

            await LoadUsers();
        }
        catch (Exception ex)
        {
            LogUpdateAdminRoleFailed(Logger, ex, user.Username);
            _adminMessage = $"Failed to update admin role for '{user.Username}'.";
            _adminError = true;
        }
        finally
        {
            _busyUsername = null;
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to create user {Username}")]
    private static partial void LogCreateUserFailed(ILogger logger, Exception ex, string username);

    [LoggerMessage(LogLevel.Error, "Failed to reset password for {Username}")]
    private static partial void LogResetPasswordFailed(ILogger logger, Exception ex, string username);

    [LoggerMessage(LogLevel.Error, "Failed to update admin role for {Username}")]
    private static partial void LogUpdateAdminRoleFailed(ILogger logger, Exception ex, string username);
}
