using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Plaat.Frontend.Auth;

namespace Plaat.Frontend.Components.Pages;

public partial class Login : ComponentBase, IDisposable
{
    [Inject]
    public required NavigationManager Navigation { get; init; }

    [Inject]
    public required AuthService AuthService { get; init; }

    [Inject]
    public required AuthenticationStateProvider AuthenticationStateProvider { get; init; }

    [Inject]
    public required IHttpContextAccessor HttpContextAccessor { get; init; }

    private readonly CancellationTokenSource _cts = new();
    private string? _username;
    private string? _password;
    private string? _errorMessage;
    private bool _isLoading;

    private string? _clientIp;

    protected override void OnInitialized()
    {
        _clientIp = HttpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }

    private async Task HandleLogin()
    {
        _isLoading = true;
        _errorMessage = null;

        if (_username is null || _password is null)
        {
            _errorMessage = "Invalid username or password";
            _isLoading = false;
            return;
        }

        var result = await AuthService.Login(
            _username,
            _password,
            _clientIp,
            _cts.Token);

        if (result is LoginResult.Success)
        {
            // Soft navigation - see RedirectToLogin. The auth state has already been updated
            // (TokenSessionStore.PersistRefreshedToken), and the tokens live in circuit-scoped
            // services, so there is nothing a full page load would pick up.
            Navigation.NavigateTo(GetReturnUrl());
        }
        else
        {
            _errorMessage = result is LoginResult.TooManyAttempts
                ? "Too many login attempts. Please wait a minute and try again."
                : "Invalid username or password";
            _isLoading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        if (state.User.Identity is { IsAuthenticated: true })
        {
            Navigation.NavigateTo(GetReturnUrl());
        }
    }

    // Only ever return a local path - never trust returnUrl enough to redirect off-site.
    private string GetReturnUrl()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);

        if (!query.TryGetValue("returnUrl", out var returnUrl) ||
            string.IsNullOrWhiteSpace(returnUrl) ||
            !IsLocalUrl(returnUrl!))
        {
            return "/";
        }

        return returnUrl!;
    }

    private static bool IsLocalUrl(string url)
    {
        return url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
