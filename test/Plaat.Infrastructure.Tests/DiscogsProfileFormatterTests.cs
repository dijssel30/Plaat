using NSubstitute;
using Plaat.Infrastructure.Discogs;
using Shouldly;
using Xunit;

namespace Plaat.Infrastructure.Tests;

public sealed class DiscogsProfileFormatterTests
{
    [Fact]
    public async Task Format_NamedArtistReference_ReplacesWithNameWithoutCallingClient()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("Formed the band [a=Total Touch].", CancellationToken.None);

        result.ShouldBe("Formed the band Total Touch.");
        await client.DidNotReceive().ResolveReferenceName(Arg.Any<char>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Format_BareIdReference_ResolvesViaClient()
    {
        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('a', 3824954, Arg.Any<CancellationToken>()).Returns("3JS");
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("Founded [a3824954] in 2012.", CancellationToken.None);

        result.ShouldBe("Founded 3JS in 2012.");
    }

    [Fact]
    public async Task Format_MasterReleaseReferenceWithEquals_ResolvesTitle()
    {
        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('m', 844034, Arg.Any<CancellationToken>()).Returns("Walk Through Fire");
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("with the song \"[m=844034]\".", CancellationToken.None);

        result.ShouldBe("with the song &quot;Walk Through Fire&quot;.");
    }

    [Fact]
    public async Task Format_ResolutionFails_FallsBackToOriginalToken()
    {
        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('a', 123, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new HttpRequestException("boom")));
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("Members: [a123]", CancellationToken.None);

        result.ShouldBe("Members: [a123]");
    }

    [Fact]
    public async Task Format_ResolutionReturnsNull_FallsBackToOriginalToken()
    {
        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('a', 123, Arg.Any<CancellationToken>()).Returns((string?)null);
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("Members: [a123]", CancellationToken.None);

        result.ShouldBe("Members: [a123]");
    }

    [Fact]
    public async Task Format_UrlWithText_ReplacesWithAnchorText()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format(
            "Visit [url=https://example.com]their website[/url] for more.", CancellationToken.None);

        result.ShouldBe("Visit their website for more.");
    }

    [Fact]
    public async Task Format_BareUrl_ReplacesWithAddress()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("See [url]https://example.com[/url].", CancellationToken.None);

        result.ShouldBe("See https://example.com.");
    }

    [Fact]
    public async Task Format_RepeatedReference_OnlyResolvesOnce()
    {
        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('a', 123, Arg.Any<CancellationToken>()).Returns("Same Artist");
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("[a123] and [a123] again.", CancellationToken.None);

        result.ShouldBe("Same Artist and Same Artist again.");
        await client.Received(1).ResolveReferenceName('a', 123, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Format_FullProfileExample_ProducesReadableText()
    {
        const string profile =
            "Initiated the band [a=Total Touch] together with her brother, [a=Tjeerd P. Oosterhuis]. " +
            "Founded [a3824954] in 2012 together with [a403038], [a58349], [a411439] and [a493200], " +
            "but left the group in 2017. She represented the Netherlands in the Eurovision Song Contest " +
            "2015 in Vienna, Austria with the song \"[m=844034]\".";

        var client = Substitute.For<IDiscogsClient>();
        client.ResolveReferenceName('a', 3824954, Arg.Any<CancellationToken>()).Returns("3JS");
        client.ResolveReferenceName('a', 403038, Arg.Any<CancellationToken>()).Returns("Peter Slager");
        client.ResolveReferenceName('a', 58349, Arg.Any<CancellationToken>()).Returns("Ruud Bos");
        client.ResolveReferenceName('a', 411439, Arg.Any<CancellationToken>()).Returns("Herman Gerrits");
        client.ResolveReferenceName('a', 493200, Arg.Any<CancellationToken>()).Returns("Jaap Kwakman");
        client.ResolveReferenceName('m', 844034, Arg.Any<CancellationToken>()).Returns("Walk Through Fire");

        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format(profile, CancellationToken.None);

        result.ShouldBe(
            "Initiated the band Total Touch together with her brother, Tjeerd P. Oosterhuis. " +
            "Founded 3JS in 2012 together with Peter Slager, Ruud Bos, Herman Gerrits and Jaap Kwakman, " +
            "but left the group in 2017. She represented the Netherlands in the Eurovision Song Contest " +
            "2015 in Vienna, Austria with the song &quot;Walk Through Fire&quot;.");
    }

    [Fact]
    public async Task Format_BoldAndItalicTags_ConvertToRealHtmlTags()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("[b]Current members:[/b] [i]a great band[/i]", CancellationToken.None);

        result.ShouldBe("<strong>Current members:</strong> <em>a great band</em>");
    }

    [Fact]
    public async Task Format_UnderlineTag_ConvertsToRealHtmlTag()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("[u]underlined[/u]", CancellationToken.None);

        result.ShouldBe("<u>underlined</u>");
    }

    [Fact]
    public async Task Format_TextContainsHtmlLookingCharacters_IsEncoded()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format("Rock & Roll <script>alert(1)</script>", CancellationToken.None);

        result.ShouldBe("Rock &amp; Roll &lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task Format_AwardsListExample_ConvertsHeadersAndPreservesLines()
    {
        var client = Substitute.For<IDiscogsClient>();
        var formatter = new DiscogsProfileFormatter(client);

        var result = await formatter.Format(
            "[b]Current members:[/b]\nCasper Starreveld\n\n[i]2013[/i]\nMTV Europe Music Award",
            CancellationToken.None);

        result.ShouldBe("<strong>Current members:</strong>\nCasper Starreveld\n\n<em>2013</em>\nMTV Europe Music Award");
    }
}
