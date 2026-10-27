using Microsoft.Extensions.DependencyInjection;
using Plaat.Contract.Client;

namespace Plaat.Contract;

/// <summary>
/// Configuration for where the Plaat API is hosted.
/// </summary>
public sealed class PlaatBackendOptions
{
    // Settable a long-lived host - e.g. Plaat.Mobile's login screen - can point
    // the client at a different server at runtime without rebuilding: IPlaatClient/PlaatClient reads
    // this via IHttpClientFactory's per-call configure callback, so a mutation here on the shared
    // singleton takes effect on the very next request.
    public required Uri BaseUri { get; set; }
}

/// <summary>
/// DI registration helpers for wiring up <see cref="IPlaatClient"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static void AddPlaatClient(
        this IServiceCollection services,
        PlaatBackendOptions options,
        Action<IHttpClientBuilder>? configureClient = null)
    {
        var builder = services.AddHttpClient(nameof(PlaatClient), c =>
        {
            c.BaseAddress = options.BaseUri;
        });

        configureClient?.Invoke(builder);

        services.AddScoped<IPlaatClient, PlaatClient>();

        services.AddSingleton(options);
    }
}
