// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Search;

/// <summary>Builds the hint for a parameter name the search pipeline does not recognise.</summary>
internal static class ParameterSuggestions
{
    /// <summary>Codes are listed in full when the type has at most this many.</summary>
    public const int ListLimit = 80;

    /// <summary>Most names offered as "did you mean".</summary>
    private const int MaxSuggestions = 3;

    /// <summary>Largest edit distance still offered as a likely typo.</summary>
    private const int MaxDistance = 2;

    /// <summary>
    /// The hint for <paramref name="baseName"/>: the offset argument for paging parameters, otherwise the codes that match
    /// it ignoring case or within <see cref="MaxDistance"/> edits, and the type's codes when there are few enough to list.
    /// </summary>
    /// <param name="baseName">The unrecognised name without its modifier or chain.</param>
    /// <param name="codes">Every name the type accepts, in ordinal order.</param>
    public static string For(string baseName, string resourceType, IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(codes);

        if (baseName is "_offset" or "_page")
        {
            return "Page with the 'offset' argument (--offset on the CLI) instead.";
        }

        List<string> parts = [];
        string[] suggestions = Suggest(baseName, codes);
        if (suggestions.Length > 0)
        {
            parts.Add($"Did you mean {string.Join(" or ", suggestions.Select(code => $"'{code}'"))}?");
        }

        parts.Add(codes.Count <= ListLimit
            ? $"Search parameters of {resourceType}: {string.Join(", ", codes)}."
            : $"{resourceType} has {codes.Count} search parameters; names are case-sensitive.");
        return string.Join(' ', parts);
    }

    private static string[] Suggest(string baseName, IReadOnlyList<string> codes)
    {
        string[] sameIgnoringCase = [.. codes.Where(code => string.Equals(code, baseName, StringComparison.OrdinalIgnoreCase))];
        if (sameIgnoringCase.Length > 0)
        {
            return sameIgnoringCase;
        }

        return [.. codes
            .Select(code => (Code: code, Distance: Distance(baseName.ToUpperInvariant(), code.ToUpperInvariant())))
            .Where(candidate => candidate.Distance <= MaxDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(candidate => candidate.Code)];
    }

    /// <summary>Levenshtein distance between <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static int Distance(string a, string b)
    {
        int[] previous = [.. Enumerable.Range(0, b.Length + 1)];
        int[] current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
