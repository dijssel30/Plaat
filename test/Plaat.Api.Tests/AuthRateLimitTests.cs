using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plaat.Biz.Options;
using Plaat.Contract.Models.Authorization;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class AuthRateLimitTests : IClassFixture<PlaatApiWebApplicationFactory>
{
    private const int LoginLimit = 3;
    private const int RefreshLimit = 5;

    private readonly WebApplicationFactory<Program> _factory;

    public AuthRateLimitTests(PlaatApiWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new PlaatOptions
                {
                    ImagesStoragePath = "/images",
                    TargetReconvertedPath = "/reconverted",
                    JwtOptions = new JwtOptions { Key = "test-key" },
                    RateLimitSettings = new RateLimitSettings
                    {
                        LoginPermitLimit = LoginLimit,
                        RefreshPermitLimit = RefreshLimit,
                        TrustedProxies = ["127.0.0.1"]
                    }
                });
            });
        });
    }

    [Fact]
    public async Task Login_ExceedingLimit_ReturnsTooManyRequests()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < LoginLimit; i++)
        {
            var response = await PostLogin(client);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await PostLogin(client);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refresh_HasSeparateHigherLimit()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < RefreshLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("nope"));
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("nope"));

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static Task<HttpResponseMessage> PostLogin(HttpClient client)
    {
        return client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "wrong"));
    }

    [Fact]
    public async Task Login_FromTrustedProxy_CountsEachForwardedClientSeparately()
    {
        var client = CreateClientFromConnectionIp("127.0.0.1");

        for (var i = 0; i < LoginLimit; i++)
        {
            (await PostLogin(client, "10.0.0.1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await PostLogin(client, "10.0.0.1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await PostLogin(client, "10.0.0.2")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_FromUntrustedCaller_IgnoresForwardedHeader()
    {
        var client = CreateClientFromConnectionIp("203.0.113.7");

        for (var i = 0; i < LoginLimit; i++)
        {
            (await PostLogin(client, $"10.0.0.{i}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // A fresh spoofed address doesn't buy a fresh allowance.
        (await PostLogin(client, "10.0.0.99")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private HttpClient CreateClientFromConnectionIp(string connectionIp)
    {
        // The test server has no real socket, so stamp a connection IP ahead of the pipeline
        // (in particular ahead of the forwarded-headers middleware).
        return _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter>(new ConnectionIpStartupFilter(connectionIp)));
            }))
            .CreateClient();
    }

    private static Task<HttpResponseMessage> PostLogin(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", "wrong"))
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private sealed class ConnectionIpStartupFilter(string connectionIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(connectionIp);

                    return nextMiddleware();
                });

                next(app);
            };
        }
    }
}
