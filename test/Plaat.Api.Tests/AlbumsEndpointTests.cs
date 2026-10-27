using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class AlbumsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAllAlbums_WhenEmpty_ReturnsOkWithEmptyList()
    {
        factory.AlbumRepository.GetAlbums(Arg.Any<CancellationToken>())
            .Returns([]);

        var response = await _client.GetAsync("/api/albums");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var albums = await response.Content.ReadFromJsonAsync<AlbumOnlyDto[]>();
        albums.ShouldNotBeNull();
        albums.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllAlbums_WhenAlbumsExist_ReturnsAll()
    {
        factory.AlbumRepository.GetAlbums(Arg.Any<CancellationToken>())
            .Returns([
                new AlbumOnlyDto { Id = 1, Title = "Album One", Artists = [new ArtistOnlyDto { Name = "Artist A" }] },
                new AlbumOnlyDto { Id = 2, Title = "Album Two", Artists = [new ArtistOnlyDto { Name = "Artist B" }] }
            ]);

        var response = await _client.GetAsync("/api/albums");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var albums = await response.Content.ReadFromJsonAsync<AlbumOnlyDto[]>();
        albums.ShouldNotBeNull();
        albums.Length.ShouldBe(2);
    }

    [Fact]
    public async Task GetAlbum_WhenAlbumExists_ReturnsOkWithAlbum()
    {
        factory.AlbumRepository.GetAlbum(42, Arg.Any<CancellationToken>())
            .Returns(new AlbumDetailsDto
            {
                Id = 42,
                Title = "Test Album",
                Artists = [new ArtistOnlyDto { Name = "Test Artist" }],
                Tracks = []
            });

        var response = await _client.GetAsync("/api/albums/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AlbumDetailsDto>();
        result.ShouldNotBeNull();
        result.Title.ShouldBe("Test Album");
        result.Id.ShouldBe(42u);
    }

    [Fact]
    public async Task GetAlbum_WhenNotFound_ReturnsNoContent()
    {
        factory.AlbumRepository.GetAlbum(Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns((AlbumDetailsDto?)null);

        var response = await _client.GetAsync("/api/albums/999");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UploadAlbumCover_WithValidImage_PersistsFileAndReturnsOk()
    {
        var imagesDir = Path.Join(Path.GetTempPath(), $"plaat-cover-{Guid.NewGuid():N}");

        try
        {
            using var client = CreateClientWithImagesDir(imagesDir);

            factory.AlbumRepository
                .SetCoverArt(42, Arg.Any<string>(), Arg.Any<string>(), "image/png", Arg.Any<CancellationToken>())
                .Returns(true);

            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

            using var content = BuildImageForm(pngBytes, "image/png");
            var response = await client.PostAsync("/api/albums/42/cover", content);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var written = Directory.GetFiles(imagesDir);
            written.ShouldHaveSingleItem();
            Path.GetExtension(written[0]).ShouldBe(".png");
            (await File.ReadAllBytesAsync(written[0])).ShouldBe(pngBytes);

            await factory.AlbumRepository.Received(1).SetCoverArt(
                42, Arg.Is<string>(p => p != null && p.EndsWith(".png")), Arg.Any<string>(), "image/png", Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(imagesDir))
            {
                Directory.Delete(imagesDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAlbumCover_WhenAlbumNotFound_ReturnsNotFound()
    {
        var imagesDir = Path.Join(Path.GetTempPath(), $"plaat-cover-{Guid.NewGuid():N}");

        try
        {
            using var client = CreateClientWithImagesDir(imagesDir);

            factory.AlbumRepository
                .SetCoverArt(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(false);

            using var content = BuildImageForm([0xFF, 0xD8, 0xFF, 0xE0, 1, 2], "image/jpeg");
            var response = await client.PostAsync("/api/albums/999/cover", content);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            if (Directory.Exists(imagesDir))
            {
                Directory.Delete(imagesDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAlbumCover_WithUnsupportedType_ReturnsBadRequest()
    {
        using var content = BuildImageForm([1, 2, 3, 4], "image/gif");
        var response = await _client.PostAsync("/api/albums/42/cover", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadAlbumCover_WithSpoofedContentType_ReturnsBadRequest()
    {
        using var content = BuildImageForm("<script>alert(1)</script>"u8.ToArray(), "image/png");
        var response = await _client.PostAsync("/api/albums/42/cover", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClientWithImagesDir(string imagesDir)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new PlaatOptions
                {
                    ImagesStoragePath = imagesDir,
                    TargetReconvertedPath = "/reconverted",
                    JwtOptions = new JwtOptions { Key = "test-key" },
                    DiscogsSettings = new DiscogsSettings { Enabled = true }
                });
            });
        }).CreateClient();
    }

    private static MultipartFormDataContent BuildImageForm(byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", "cover");
        return form;
    }
}
