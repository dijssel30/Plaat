using Plaat.Mobile.ViewModels;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.Tests;

public sealed class DiscogsSummaryTests
{
    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        // The API HTML-encodes the profile text, so an ampersand in a band name arrives encoded -
        // shown undecoded it reads as "Earth, Wind &amp; Fire".
        DiscogsSummary.ToPlainText("Earth, Wind &amp; Fire").ShouldBe("Earth, Wind & Fire");
    }

    [Fact]
    public void ToPlainText_StripsTheApisOwnMarkup()
    {
        DiscogsSummary.ToPlainText("<strong>Bold</strong> and <em>italic</em>")
            .ShouldBe("Bold  and  italic");
    }

    [Fact]
    public void ToPlainText_KeepsEncodedAngleBracketsThatWerePartOfTheText()
    {
        // Strip-then-decode: decoding first would turn this into a tag the stripper would eat.
        DiscogsSummary.ToPlainText("Known as &lt;dj&gt; locally").ShouldBe("Known as <dj> locally");
    }

    [Fact]
    public void ToPlainText_WithNull_ReturnsNull()
    {
        DiscogsSummary.ToPlainText(null).ShouldBeNull();
    }
}
