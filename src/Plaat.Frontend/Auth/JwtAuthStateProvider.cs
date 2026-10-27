using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Auth;

public sealed class JwtAuthStateProvider(TokenStorageService tokenStorage) : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var state = tokenStorage.IsAuthenticated
            ? BuildAuthState(tokenStorage.AccessToken!)
            : AnonymousState;

        return Task.FromResult(state);
    }

    private static AuthenticationState BuildAuthState(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        return new AuthenticationState(
            new ClaimsPrincipal(
                new ClaimsIdentity(jwt.Claims, "jwt")));
    }

    public void NotifyUserAuthenticated()
    {
        var accessToken = tokenStorage.AccessToken ??
                          throw new UnauthorizedAccessException(
                              "Tried to notify user authenticated, but did not find an access token");

        var authState = BuildAuthState(accessToken);

        NotifyAuthenticationStateChanged(Task.FromResult(authState));
    }

    public void NotifyUserLoggedOut()
    {
        var authState = AnonymousState;

        NotifyAuthenticationStateChanged(Task.FromResult(authState));
    }
}
