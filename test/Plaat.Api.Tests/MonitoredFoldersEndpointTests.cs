using System.Data;
using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class MonitoredFoldersEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetFolders_ReturnsAllFolders()
    {
        factory.MonitoredFolderRepository.GetAll(Arg.Any<CancellationToken>())
            .Returns([new MonitoredFolderDto(1, "/music/a", null), new MonitoredFolderDto(2, "/music/b", null)]);

        var response = await _client.GetAsync("/api/monitored-folders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var folders = await response.Content.ReadFromJsonAsync<MonitoredFolderDto[]>();
        folders.ShouldNotBeNull();
        folders.Length.ShouldBe(2);
    }

    [Fact]
    public async Task AddFolder_WhenPathNotYetMonitored_ReturnsOk()
    {
        factory.MonitoredFolderRepository.AddFolder("/music/new", Arg.Any<CancellationToken>())
            .Returns(new MonitoredFolderDto(1, "/music/new", null));

        var response = await _client.PostAsJsonAsync(
            "/api/monitored-folders", new AddMonitoredFolderRequest("/music/new"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddFolder_WhenPathAlreadyMonitored_ReturnsConflict()
    {
        factory.MonitoredFolderRepository.AddFolder("/music/existing", Arg.Any<CancellationToken>())
            .Returns<MonitoredFolderDto>(_ => throw new DuplicateNameException("Folder is already monitored"));

        var response = await _client.PostAsJsonAsync(
            "/api/monitored-folders", new AddMonitoredFolderRequest("/music/existing"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddFolder_WhenPathIsBlank_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/monitored-folders", new AddMonitoredFolderRequest("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveFolder_CallsRepository()
    {
        var response = await _client.DeleteAsync("/api/monitored-folders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.MonitoredFolderRepository.Received(1).RemoveFolder(1, Arg.Any<CancellationToken>());
    }
}
