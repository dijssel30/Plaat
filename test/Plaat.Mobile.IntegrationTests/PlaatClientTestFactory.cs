using Microsoft.Extensions.DependencyInjection;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Mobile.IntegrationTests;

/// <summary>
/// Mirrors the real Login()/PersistRefreshedToken() session mechanics without any MAUI SecureStorage.
/// </summary>
internal sealed class InMemoryTokenSessionStore(TokenStorageService tokenStorage) : ITokenSessionStore
{
    public Task PersistRefreshedToken(TokenResponse tokenResponse, CancellationToken cancellationToken)
    {
        tokenStorage.AccessToken = tokenResponse.AccessToken;
        tokenStorage.RefreshToken = tokenResponse.RefreshToken;
        tokenStorage.ExpiresAt = tokenResponse.ExpiresAt;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Builds a real <see cref="IPlaatClient"/> (Plaat.Contract's <c>PlaatClient</c>) wired up the same
/// way Plaat.Mobile/Plaat.Frontend do, pointed at a real running <see cref="Plaat.TestSupport.PlaatApiTestServer"/>.
/// </summary>
internal sealed class PlaatClientTestFactory : IDisposable
{
    private readonly ServiceProvider _provider;

    public IPlaatClient Client { get; }
    public TokenStorageService TokenStorage { get; }

    public PlaatClientTestFactory(Uri backendBaseUri)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TokenStorageService>();
        services.AddSingleton<ITokenSessionStore, InMemoryTokenSessionStore>();
        services.AddPlaatClient(new PlaatBackendOptions { BaseUri = backendBaseUri });

        _provider = services.BuildServiceProvider();
        Client = _provider.GetRequiredService<IPlaatClient>();
        TokenStorage = _provider.GetRequiredService<TokenStorageService>();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
