// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// See LegacyExpressionParser.cs for why this exists and how to use it as a rollback lever.
//
// NOTE: some of these extension methods (SplitByTokenSeparator, JoinByOrSeparator,
// EscapeSearchParameterValue, UnescapeSearchParameterValue) have the same name and signature as
// methods still present on Ignixa.Search.Indexing.StringExtensions (the production, trimmed-down
// version - it dropped SplitByOrSeparator/SplitByCompositeSeparator since the current parser doesn't
// need them). Any file that imports BOTH Ignixa.Search.Indexing and Ignixa.Search.Expressions.
// Parsers.Legacy and then calls one of those four names will get a compile-time ambiguous-call error
// (CS0121), not a silent misresolution, since both extend the identical `string` receiver type with
// identical signatures. Neither LegacyExpressionParser.cs nor LegacySearchParameterExpressionParser.cs
// import both namespaces while calling an overlapping name (verified: only SplitByOrSeparator and
// SplitByCompositeSeparator are actually called here, and neither exists on the production type
// anymore). If you add code here that needs one of the four overlapping methods, fully qualify it as
// Ignixa.Search.Expressions.Parsers.Legacy.LegacyStringExtensions.MethodName(...) rather than relying
// on extension-method syntax.

using EnsureThat;

namespace Ignixa.Search.Expressions.Parsers.Legacy;

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
