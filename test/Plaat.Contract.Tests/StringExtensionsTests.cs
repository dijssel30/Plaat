using Plaat.Contract.Extensions;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class StringExtensionsTests
{
    [Fact]
    public void NormalizeForSearch_WithLowercase_ReturnsSameString()
    {
        "hello".NormalizeForSearch().ShouldBe("hello");
    }

    [Fact]
    public void NormalizeForSearch_WithUppercase_LowercasesAll()
    {
        "HELLO".NormalizeForSearch().ShouldBe("hello");
    }

    [Fact]
    public void NormalizeForSearch_WithAccentedE_RemovesDiacritic()
    {
        "été".NormalizeForSearch().ShouldBe("ete");
    }

    [Fact]
    public void NormalizeForSearch_WithAccentedA_RemovesDiacritic()
    {
        "André".NormalizeForSearch().ShouldBe("andre");
    }

    [Fact]
    public void NormalizeForSearch_WithOSlash_MapsToO()
    {
        "Nøkken".NormalizeForSearch().ShouldBe("nokken");
    }

    [Fact]
    public void NormalizeForSearch_WithCapitalOSlash_MapsToO()
    {
        "Øystein".NormalizeForSearch().ShouldBe("oystein");
    }

    [Fact]
    public void NormalizeForSearch_WithEthD_MapsToD()
    {
        "Eiður".NormalizeForSearch().ShouldBe("eidur");
    }

    [Fact]
    public void NormalizeForSearch_WithThorn_MapsToT()
    {
        "þing".NormalizeForSearch().ShouldBe("ting");
    }

    [Fact]
    public void NormalizeForSearch_WithEszett_MapsToS()
    {
        "Straße".NormalizeForSearch().ShouldBe("strase");
    }

    [Fact]
    public void NormalizeForSearch_WithEmptyString_ReturnsEmptyString()
    {
        "".NormalizeForSearch().ShouldBeEmpty();
    }

    [Fact]
    public void NormalizeForSearch_WithMixedCase_NormalizesAll()
    {
        "Björk".NormalizeForSearch().ShouldBe("bjork");
    }

    [Theory]
    [InlineData("ð", "d")]
    [InlineData("Ð", "d")]
    [InlineData("þ", "t")]
    [InlineData("Þ", "t")]
    [InlineData("ø", "o")]
    [InlineData("Ø", "o")]
    [InlineData("ß", "s")]
    public void NormalizeForSearch_WithNonDecomposingLetters_MapsToAsciiEquivalent(string input, string expected)
    {
        input.NormalizeForSearch().ShouldBe(expected);
    }
}
