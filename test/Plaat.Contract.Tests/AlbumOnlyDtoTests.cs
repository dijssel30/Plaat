using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class AlbumOnlyDtoTests
{
    [Fact]
    public void AllArtists_WithSingleArtist_ReturnsSingleName()
    {
        var dto = new AlbumOnlyDto { Title = "Test", Artists = [new ArtistOnlyDto { Name = "Artist One" }] };

        dto.AllArtists.ShouldBe("Artist One");
    }

    [Fact]
    public void AllArtists_WithMultipleArtists_ReturnsCommaSeparated()
    {
        var dto = new AlbumOnlyDto { Title = "Test", Artists = [new ArtistOnlyDto { Name = "Artist One" }, new ArtistOnlyDto { Name = "Artist Two" }, new ArtistOnlyDto { Name = "Artist Three" }] };

        dto.AllArtists.ShouldBe("Artist One, Artist Two, Artist Three");
    }

    [Fact]
    public void AllArtists_WithNoArtists_ReturnsEmptyString()
    {
        var dto = new AlbumOnlyDto { Title = "Test", Artists = [] };

        dto.AllArtists.ShouldBeEmpty();
    }
}
