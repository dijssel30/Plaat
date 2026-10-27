using Plaat.Contract.Extensions;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class SearchScoringTests
{
    [Fact]
    public void SearchScore_ExactFieldMatch_OutscoresSubstringMatch()
    {
        var exact = SearchScoring.SearchScore("RAYE", "RAYE");
        var substring = SearchScoring.SearchScore("RAYE", "Andrew Rayel");

        exact.ShouldNotBeNull();
        substring.ShouldNotBeNull();
        exact.Value.ShouldBeGreaterThan(substring.Value);
    }

    [Fact]
    public void SearchScore_TrailingWhitespace_IsIgnored()
    {
        SearchScoring.SearchScore("raye ", "RAYE").ShouldBe(SearchScoring.SearchScore("raye", "RAYE"));
    }

    [Fact]
    public void SearchScore_LeadingAndInnerWhitespace_IsIgnored()
    {
        SearchScoring.SearchScore("  raye escapism", "Escapism", "RAYE")
            .ShouldBe(SearchScoring.SearchScore("raye escapism", "Escapism", "RAYE"));
    }

    [Fact]
    public void SearchScore_MultipleWords_MatchAcrossDifferentFields_RegardlessOfOrder()
    {
        // "Escapism" is the title, "RAYE" is the artist - the query names both, in the opposite order.
        var score = SearchScoring.SearchScore("escapism raye", "Escapism", "RAYE");

        score.ShouldNotBeNull();
    }

    [Fact]
    public void SearchScore_MissingWord_ReturnsNull()
    {
        SearchScoring.SearchScore("raye nowhere", "Escapism", "RAYE").ShouldBeNull();
    }

    [Fact]
    public void SearchScore_IsDiacriticAndCaseInsensitive()
    {
        SearchScoring.SearchScore("MOTORHEAD", "Motörhead").ShouldNotBeNull();
    }

    [Fact]
    public void SearchScore_WordDoesNotHaveToBeAdjacentInSameField()
    {
        // Words present in one field but not contiguous in that exact order still match.
        var score = SearchScoring.SearchScore("boys back", "Back Street Boys");

        score.ShouldNotBeNull();
    }

    [Fact]
    public void SearchScore_FieldStartingWithQuery_OutscoresPlainSubstringMatch()
    {
        var startsWith = SearchScoring.SearchScore("ray", "Rayel Brothers");
        var containsOnly = SearchScoring.SearchScore("ray", "Andrew Rayel");

        startsWith.ShouldNotBeNull();
        containsOnly.ShouldNotBeNull();
        startsWith.Value.ShouldBeGreaterThan(containsOnly.Value);
    }

    [Fact]
    public void SearchScore_BlankQuery_ReturnsNull()
    {
        SearchScoring.SearchScore("   ", "Anything").ShouldBeNull();
    }

    [Fact]
    public void SearchScore_EmptyFieldIsIgnored_AndExactWordInFieldScoresAsExactWord()
    {
        SearchScoring.SearchScore("raye", "", "raye live").ShouldBe(900);
    }

    [Fact]
    public void SearchScore_SingleWordPrefixOfLaterWord_ScoresAsWordStart()
    {
        SearchScoring.SearchScore("ray", "andrew rayel").ShouldBe(700);
    }

    [Fact]
    public void SearchScore_SingleWordInsideAWord_ScoresAsContains()
    {
        SearchScoring.SearchScore("aye", "raye").ShouldBe(500);
    }

    [Fact]
    public void SearchScore_MultiWordPhraseInsideField_ScoresAsContains()
    {
        SearchScoring.SearchScore("b c", "a b c d").ShouldBe(500);
    }

    [Fact]
    public void SearchScore_WordsOnlyMatchingAcrossFields_ScoresAsScattered()
    {
        SearchScoring.SearchScore("alpha beta", "alpha", "beta").ShouldBe(100);
    }
}
