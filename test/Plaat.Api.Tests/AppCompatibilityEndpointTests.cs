using System.Net;
using System.Net.Http.Json;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class AppCompatibilityEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetCompatibility_ReturnsOkWithoutAuthentication()
    {
        var response = await _client.GetAsync("/api/app/compatibility");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var compatibility = await response.Content.ReadFromJsonAsync<AppCompatibilityDto>();
        compatibility.ShouldNotBeNull();
        compatibility.MinimumAppVersion.ShouldNotBeNullOrWhiteSpace();
    }
}
