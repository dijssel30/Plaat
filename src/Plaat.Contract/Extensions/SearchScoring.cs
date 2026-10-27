namespace Plaat.Contract.Extensions;

/// <summary>
/// Shared free-text matching/ranking used everywhere a user types into a search box (the global
/// search page, and the tracks/albums/artists list filters). A query matches a record when every
/// one of its words appears somewhere across the given fields combined - the words don't need to
/// be adjacent or in the same field - and the returned score ranks a field that equals the query
/// exactly (or starts with it) above one that merely contains it as a substring. That's what
/// keeps an artist literally named "RAYE" ahead of one that just contains those letters, like
/// "Andrew Rayel", when searching "RAYE".
/// </summary>
public static class SearchScoring
{
    private const int ExactFieldMatch = 1000;
    private const int ExactWordMatch = 900;
    private const int FieldStartsWithQuery = 800;
    private const int WordStartsWithQuery = 700;
    private const int FieldContainsQuery = 500;
    private const int WordsScatteredAcrossFields = 100;

    /// <summary>
    /// Scores how well the given fields match a search query (trimmed and diacritic/case-folded
    /// automatically). Returns null when at least one query word is missing from every field
    /// combined - meaning the record should be excluded from results - otherwise a relevance
    /// score where higher is a better match.
    /// </summary>
    public static int? SearchScore(string query, params string[] fields)
    {
        var words = query
            .NormalizeForSearch()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return null;
        }

        var normalizedFields = Array.ConvertAll(fields, f => f.NormalizeForSearch());
        var combined = string.Join(' ', normalizedFields);

        if (words.Any(w => !combined.Contains(w, StringComparison.Ordinal)))
        {
            return null;
        }

        var fullQuery = string.Join(' ', words);

        return normalizedFields
            .Select(field => ScoreField(field, fullQuery, words))
            .Prepend(WordsScatteredAcrossFields)
            .Max();
    }

    private static int ScoreField(string field, string fullQuery, string[] words)
    {
        if (field.Length == 0)
        {
            return 0;
        }

        if (field == fullQuery)
        {
            return ExactFieldMatch;
        }

        var fieldWords = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 1 && Array.IndexOf(fieldWords, words[0]) >= 0)
        {
            return ExactWordMatch;
        }

        if (field.StartsWith(fullQuery, StringComparison.Ordinal))
        {
            return FieldStartsWithQuery;
        }

        if (words.Length == 1 && Array.Exists(fieldWords, w => w.StartsWith(words[0], StringComparison.Ordinal)))
        {
            return WordStartsWithQuery;
        }

        if (field.Contains(fullQuery, StringComparison.Ordinal))
        {
            return FieldContainsQuery;
        }

        return 0;
    }
}
