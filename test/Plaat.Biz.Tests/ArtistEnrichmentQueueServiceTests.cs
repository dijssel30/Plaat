using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;
using Plaat.Db.Models;
using Plaat.Db.Repositories;
using Plaat.Infrastructure.Discogs;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class ArtistEnrichmentQueueServiceTests
{
    private static PlaatOptions BuildOptions(bool enabled)
    {
        return new()
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/reconverted",
            JwtOptions = new JwtOptions { Key = "test-key" },
            DiscogsSettings = new DiscogsSettings { Enabled = enabled }
        };
    }

    private static ArtistEnrichmentQueueService BuildService(
        IArtistRepository artistRepository,
        IDiscogsClient discogsClient,
        bool enabled = true)
    {
        var logger = Substitute.For<ILogger<ArtistEnrichmentQueueService>>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        return new ArtistEnrichmentQueueService(
            artistRepository, discogsClient, httpClientFactory, BuildOptions(enabled), logger);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task EnrichAndWait_FeatureDisabled_DoesNotClaim()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        var discogsClient = Substitute.For<IDiscogsClient>();
        var service = BuildService(artistRepository, discogsClient, enabled: false);

        var enriched = await service.EnrichAndWait(1, CancellationToken.None);

        enriched.ShouldBeFalse();
        await artistRepository.DidNotReceive().TryClaimForEnrichment(Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAndWait_AlreadyClaimedByAnotherRequest_ReturnsFalse()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.TryClaimForEnrichment(1, Arg.Any<CancellationToken>()).Returns(false);
        var discogsClient = Substitute.For<IDiscogsClient>();
        var service = BuildService(artistRepository, discogsClient);

        var enriched = await service.EnrichAndWait(1, CancellationToken.None);

        enriched.ShouldBeFalse();
    }

    [Fact]
    public async Task EnrichAndWait_ExactNameMatch_WaitsForSaveAndReturnsTrue()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.TryClaimForEnrichment(1, Arg.Any<CancellationToken>()).Returns(true);
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns("Radiohead");

        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
            .Returns(new DiscogsSearchArtist(100, "Radiohead"));
        discogsClient.GetArtistById(100, Arg.Any<CancellationToken>())
            .Returns(new DiscogsArtistDetails(100, "Radiohead", "A rock band.", null));

        var service = BuildService(artistRepository, discogsClient);

        var enriched = await service.EnrichAndWait(1, CancellationToken.None);

        enriched.ShouldBeTrue();
        await artistRepository.Received(1).SaveEnrichmentResult(
            1, 100, "A rock band.", null, ArtistDiscogsLookupStatus.Found, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAndWait_NoExactMatch_SavesNotFoundAndReturnsTrue()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.TryClaimForEnrichment(1, Arg.Any<CancellationToken>()).Returns(true);
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns("Some Unknown Band");

        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Some Unknown Band", Arg.Any<CancellationToken>())
            .Returns((DiscogsSearchArtist?)null);

        var service = BuildService(artistRepository, discogsClient);

        var enriched = await service.EnrichAndWait(1, CancellationToken.None);

        enriched.ShouldBeTrue();
        await artistRepository.Received(1).SaveEnrichmentResult(
            1, null, null, null, ArtistDiscogsLookupStatus.NotFound, Arg.Any<CancellationToken>());
        await discogsClient.DidNotReceive().GetArtistById(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueForced_SkipsNameMatchAndUsesGivenId()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns("Local Spelling");

        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.GetArtistById(999, Arg.Any<CancellationToken>())
            .Returns(new DiscogsArtistDetails(999, "Completely Different Name", "Bio", null));

        var service = BuildService(artistRepository, discogsClient);

        await service.EnqueueForced(1, 999, CancellationToken.None);

        var saved = await WaitUntil(() => artistRepository.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IArtistRepository.SaveEnrichmentResult)));

        saved.ShouldBeTrue();
        await discogsClient.DidNotReceive().SearchArtistByExactName(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await artistRepository.Received(1).SaveEnrichmentResult(
            1, 999, "Bio", null, ArtistDiscogsLookupStatus.Found, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueForced_UnknownDiscogsId_ResetsToNotAttempted()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns("Local Spelling");

        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.GetArtistById(999, Arg.Any<CancellationToken>())
            .Returns((DiscogsArtistDetails?)null);

        var service = BuildService(artistRepository, discogsClient);

        await service.EnqueueForced(1, 999, CancellationToken.None);

        var reset = await WaitUntil(() => artistRepository.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IArtistRepository.ResetToNotAttempted)) >= 2);

        reset.ShouldBeTrue();
        await artistRepository.DidNotReceive().SaveEnrichmentResult(
            Arg.Any<uint>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<ArtistDiscogsLookupStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAndWait_DiscogsClientThrows_ResetsToNotAttemptedAndReturnsFalse()
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.TryClaimForEnrichment(1, Arg.Any<CancellationToken>()).Returns(true);
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns("Radiohead");

        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DiscogsSearchArtist?>(new HttpRequestException("boom")));

        var service = BuildService(artistRepository, discogsClient);

        var enriched = await service.EnrichAndWait(1, CancellationToken.None);

        enriched.ShouldBeFalse();
        await artistRepository.Received(1).ResetToNotAttempted(1, Arg.Any<CancellationToken>());
        await artistRepository.DidNotReceive().SaveEnrichmentResult(
            Arg.Any<uint>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<ArtistDiscogsLookupStatus>(), Arg.Any<CancellationToken>());
    }

    private sealed class StubImageHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);

            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private static (ArtistEnrichmentQueueService service, StubImageHandler handler) BuildWithImages(
        IArtistRepository artistRepository,
        IDiscogsClient discogsClient,
        string imagesDir,
        HttpStatusCode imageStatus)
    {
        var handler = new StubImageHandler(imageStatus);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var options = new PlaatOptions
        {
            ImagesStoragePath = imagesDir,
            TargetReconvertedPath = "/reconverted",
            JwtOptions = new JwtOptions { Key = "test-key" },
            DiscogsSettings = new DiscogsSettings { Enabled = true }
        };

        return (new ArtistEnrichmentQueueService(
            artistRepository, discogsClient, httpClientFactory, options, NullLogger<ArtistEnrichmentQueueService>.Instance), handler);
    }

    private static IArtistRepository ClaimableArtist(string? name)
    {
        var artistRepository = Substitute.For<IArtistRepository>();
        artistRepository.TryClaimForEnrichment(1, Arg.Any<CancellationToken>()).Returns(true);
        artistRepository.GetArtistName(1, Arg.Any<CancellationToken>()).Returns(name);

        return artistRepository;
    }

    [Fact]
    public async Task EnrichAndWait_DiscogsNotConfigured_ReturnsFalse()
    {
        var options = new PlaatOptions
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/reconverted",
            JwtOptions = new JwtOptions { Key = "test-key" }
        };
        var artistRepository = Substitute.For<IArtistRepository>();
        var service = new ArtistEnrichmentQueueService(
            artistRepository, Substitute.For<IDiscogsClient>(), Substitute.For<IHttpClientFactory>(), options,
            NullLogger<ArtistEnrichmentQueueService>.Instance);

        (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeFalse();
        await artistRepository.DidNotReceiveWithAnyArgs().TryClaimForEnrichment(0, CancellationToken.None);
    }

    [Fact]
    public async Task EnrichAndWait_ArtistNoLongerExists_CompletesWithoutSaving()
    {
        var artistRepository = ClaimableArtist(null);
        var discogsClient = Substitute.For<IDiscogsClient>();
        var service = BuildService(artistRepository, discogsClient);

        (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeTrue();

        await discogsClient.DidNotReceiveWithAnyArgs().SearchArtistByExactName(null!, CancellationToken.None);
        await artistRepository.DidNotReceiveWithAnyArgs().SaveEnrichmentResult(0, null, null, null, default, CancellationToken.None);
    }

    [Fact]
    public async Task EnrichAndWait_MatchWhosePageVanished_ResetsToNotAttempted()
    {
        var artistRepository = ClaimableArtist("Radiohead");
        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
            .Returns(new DiscogsSearchArtist(100, "Radiohead"));
        discogsClient.GetArtistById(100, Arg.Any<CancellationToken>()).Returns((DiscogsArtistDetails?)null);
        var service = BuildService(artistRepository, discogsClient);

        (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeTrue();

        await artistRepository.Received(1).ResetToNotAttempted(1, Arg.Any<CancellationToken>());
        await artistRepository.DidNotReceiveWithAnyArgs().SaveEnrichmentResult(0, null, null, null, default, CancellationToken.None);
    }

    [Fact]
    public async Task EnrichAndWait_WhenResetAfterFailureAlsoFails_StillCompletesWithFalse()
    {
        var artistRepository = ClaimableArtist("Radiohead");
        artistRepository.ResetToNotAttempted(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));
        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DiscogsSearchArtist?>(new HttpRequestException("boom")));
        var service = BuildService(artistRepository, discogsClient);

        (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://img.discogs.test/a/photo.png", ".png")]
    [InlineData("https://img.discogs.test/a/photo", ".jpg")]
    public async Task EnrichAndWait_WithImage_DownloadsItIntoImageStorage(string imageUri, string expectedExtension)
    {
        var imagesDir = Directory.CreateTempSubdirectory("plaat-discogs-").FullName;
        try
        {
            var artistRepository = ClaimableArtist("Radiohead");
            var discogsClient = Substitute.For<IDiscogsClient>();
            discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
                .Returns(new DiscogsSearchArtist(100, "Radiohead"));
            discogsClient.GetArtistById(100, Arg.Any<CancellationToken>())
                .Returns(new DiscogsArtistDetails(100, "Radiohead", null, imageUri));
            var (service, handler) = BuildWithImages(artistRepository, discogsClient, imagesDir, HttpStatusCode.OK);

            (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeTrue();

            handler.Requested.ShouldHaveSingleItem().ShouldBe(new Uri(imageUri));
            var file = Directory.GetFiles(imagesDir).ShouldHaveSingleItem();
            Path.GetExtension(file).ShouldBe(expectedExtension);
            (await File.ReadAllBytesAsync(file)).ShouldBe(new byte[] { 1, 2, 3 });
            await artistRepository.Received(1).SaveEnrichmentResult(
                1, 100, null, Path.GetFileName(file), ArtistDiscogsLookupStatus.Found, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(imagesDir, recursive: true);
        }
    }

    [Fact]
    public async Task EnrichAndWait_ImageDownloadFails_SavesWithoutImage()
    {
        var imagesDir = Directory.CreateTempSubdirectory("plaat-discogs-").FullName;
        try
        {
            var artistRepository = ClaimableArtist("Radiohead");
            var discogsClient = Substitute.For<IDiscogsClient>();
            discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
                .Returns(new DiscogsSearchArtist(100, "Radiohead"));
            discogsClient.GetArtistById(100, Arg.Any<CancellationToken>())
                .Returns(new DiscogsArtistDetails(100, "Radiohead", "Band.", "https://img.discogs.test/x.jpg"));
            var (service, _) = BuildWithImages(artistRepository, discogsClient, imagesDir, HttpStatusCode.NotFound);

            (await service.EnrichAndWait(1, CancellationToken.None)).ShouldBeTrue();

            Directory.GetFiles(imagesDir).ShouldBeEmpty();
            await artistRepository.Received(1).SaveEnrichmentResult(
                1, 100, "Band.", null, ArtistDiscogsLookupStatus.Found, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(imagesDir, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueueForRefresh_ResetsClaimsAndRunsANameLookup()
    {
        var artistRepository = ClaimableArtist("Radiohead");
        var discogsClient = Substitute.For<IDiscogsClient>();
        discogsClient.SearchArtistByExactName("Radiohead", Arg.Any<CancellationToken>())
            .Returns((DiscogsSearchArtist?)null);
        var service = BuildService(artistRepository, discogsClient);

        await service.EnqueueForRefresh(1, CancellationToken.None);

        (await WaitUntil(() => artistRepository.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IArtistRepository.SaveEnrichmentResult))))
            .ShouldBeTrue();
        await artistRepository.Received(1).ResetToNotAttempted(1, Arg.Any<CancellationToken>());
        await artistRepository.Received(1).TryClaimForEnrichment(1, Arg.Any<CancellationToken>());
        service.Dispose();
    }
}
