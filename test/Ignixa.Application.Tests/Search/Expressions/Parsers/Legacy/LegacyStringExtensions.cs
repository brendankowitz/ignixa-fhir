// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// Frozen snapshot of Ignixa.Search.Indexing.StringExtensions as it existed on `main` before PR #332
// (the handwritten-scanner search parser rewrite), for the sole purpose of running the pre-rewrite
// parser (see LegacyExpressionParser.cs) side-by-side with the current parser in
// SearchParserOldVsNewParityTests. Not shipped, not referenced by production code. Delete this
// Legacy/ folder once the parity suite is no longer needed (e.g. the old parser's behavior is fully
// characterized elsewhere and this differential harness has served its purpose).

using EnsureThat;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers.Legacy;

internal static class LegacyStringExtensions
{
    private const char EscapingCharacter = '\\';
    private const char TokenSeparator = '|';
    private const char CompositeSeparator = '$';
    private const char OrSeparator = ',';

    private static readonly string EscapedEscapingCharacter = $"{EscapingCharacter}{EscapingCharacter}";
    private static readonly string EscapedTokenSeparator = $"{EscapingCharacter}{TokenSeparator}";
    private static readonly string EscapedCompositeSeparator = $"{EscapingCharacter}{CompositeSeparator}";
    private static readonly string EscapedOrSeparator = $"{EscapingCharacter}{OrSeparator}";

    public static IReadOnlyList<string> SplitByTokenSeparator(this string s)
    {
        EnsureArg.IsNotNull(s, nameof(s));
        return Split(s, TokenSeparator);
    }

    public static IReadOnlyList<string> SplitByCompositeSeparator(this string s)
    {
        EnsureArg.IsNotNull(s, nameof(s));
        return Split(s, CompositeSeparator);
    }

    public static IReadOnlyList<string> SplitByOrSeparator(this string s)
    {
        EnsureArg.IsNotNull(s, nameof(s));
        return Split(s, OrSeparator);
    }

    public static string JoinByOrSeparator(this IEnumerable<string> strings)
    {
        EnsureArg.IsNotNull(strings, nameof(strings));
        return string.Join(OrSeparator, strings);
    }

    public static string EscapeSearchParameterValue(this string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        s = s.Replace($"{EscapingCharacter}", EscapedEscapingCharacter, StringComparison.Ordinal);
        s = s.Replace($"{TokenSeparator}", EscapedTokenSeparator, StringComparison.Ordinal);
        s = s.Replace($"{CompositeSeparator}", EscapedCompositeSeparator, StringComparison.Ordinal);
        s = s.Replace($"{OrSeparator}", EscapedOrSeparator, StringComparison.Ordinal);

        return s;
    }

    public static string UnescapeSearchParameterValue(this string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        s = s.Replace(EscapedTokenSeparator, $"{TokenSeparator}", StringComparison.Ordinal);
        s = s.Replace(EscapedCompositeSeparator, $"{CompositeSeparator}", StringComparison.Ordinal);
        s = s.Replace(EscapedOrSeparator, $"{OrSeparator}", StringComparison.Ordinal);
        s = s.Replace(EscapedEscapingCharacter, $"{EscapingCharacter}", StringComparison.Ordinal);

        return s;
    }

    private static IReadOnlyList<string> Split(string s, char separator)
    {
        EnsureArg.IsNotNull(s, nameof(s));

        var results = new List<string>();
        bool isEscaping = false;
        int currentSubstringStartingIndex = 0;

        for (int index = 0; index < s.Length; index++)
            if (isEscaping)
            {
                isEscaping = false;
            }
            else if (s[index] == EscapingCharacter)
            {
                isEscaping = true;
            }
            else if (s[index] == separator)
            {
                results.Add(s.Substring(currentSubstringStartingIndex, index - currentSubstringStartingIndex));
                currentSubstringStartingIndex = index + 1;
            }

        results.Add(s.Substring(currentSubstringStartingIndex, s.Length - currentSubstringStartingIndex));

        return results;
    }
}
