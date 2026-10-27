using Microsoft.Extensions.DependencyInjection;

namespace Plaat.Infrastructure.Discogs;

public static class ServiceCollectionExtensions
{
    public static void AddDiscogsClient(this IServiceCollection services)
    {
        services.AddHttpClient<IDiscogsClient, DiscogsClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<DiscogsOptions>();

            client.BaseAddress = new Uri("https://api.discogs.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Plaat/1.0");

            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Discogs token={options.Token}");
            }
        });
    }
}
