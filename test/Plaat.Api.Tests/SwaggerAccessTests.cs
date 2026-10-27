using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

/// <summary>
/// Swagger is opt-in, and when Swagger:AllowedNetworks is set only clients inside those networks
/// may reach it (everyone else gets a 404, as if it didn't exist).
/// </summary>
public sealed class SwaggerAccessTests(PlaatApiWebApplicationFactory factory) : IClassFixture<PlaatApiWebApplicationFactory>
{
    private sealed class RemoteIpStartupFilter(IPAddress? address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (HttpContext context, Func<Task> nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = address;
                    await nextMiddleware();
                });
                next(app);
            };
        }
    }

    private HttpClient Client(bool enabled, string? allowedNetwork = null, IPAddress? remote = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Swagger:Enabled", enabled ? "true" : "false");
            if (allowedNetwork is not null)
            {
                builder.UseSetting("Swagger:AllowedNetworks:0", allowedNetwork);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(remote)));
        }).CreateClient();
    }

    [Fact]
    public async Task Disabled_SwaggerIsNotServed()
    {
        (await Client(enabled: false).GetAsync("/swagger/v1/swagger.json")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnabledWithoutNetworkRestriction_IsOpenAndHasNoContentSecurityPolicy()
    {
        var response = await Client(enabled: true).GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("::ffff:10.1.2.3")]
    public async Task EnabledForANetwork_AllowsClientsInsideIt(string remote)
    {
        var response = await Client(enabled: true, "10.0.0.0/8", IPAddress.Parse(remote)).GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData(null)]
    public async Task EnabledForANetwork_HidesItFromEveryoneElse(string? remote)
    {
        var client = Client(enabled: true, "10.0.0.0/8", remote is null ? null : IPAddress.Parse(remote));

        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NetworkRestriction_DoesNotAffectTheApi()
    {
        var response = await Client(enabled: true, "10.0.0.0/8", IPAddress.Parse("192.168.1.5")).GetAsync("/api/app/compatibility");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("default-src 'none'");
    }
}
