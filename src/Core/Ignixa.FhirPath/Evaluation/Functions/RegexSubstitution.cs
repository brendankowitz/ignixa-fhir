// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// What a .NET regex replacement pattern refers to, enough to bound the length of its expansion without
/// expanding it.
/// </summary>
/// <remarks>
/// <para>
/// <c>replaceMatches()</c> hands its substitution to <see cref="System.Text.RegularExpressions.Regex.Replace(string, string)"/>,
/// so the substitution uses .NET's replacement syntax: <c>$$</c> is a literal dollar; <c>$&amp;</c> and
/// <c>$0</c> the match; <c>$`</c> and <c>$'</c> the text before and after it; <c>$_</c> the whole input;
/// <c>$+</c>, <c>$n</c> and <c>${name}</c> a capture group. Anything else is literal text.
/// </para>
/// <para>
/// The parse is deliberately an over-approximation where .NET's own rules depend on the pattern: a <c>$12</c>
/// is group 12, or group 1 followed by <c>2</c>, or literal text, depending on which groups exist. Counting it
/// as a group reference that may also expand to its own text gives an upper bound that holds in every case,
/// which is all a length check needs.
/// </para>
/// </remarks>
internal sealed class RegexSubstitution
{
    private RegexSubstitution(int literalLength, int matchReferences, int prefixReferences, int suffixReferences, int inputReferences, int groupReferences)
    {
        LiteralLength = literalLength;
        MatchReferences = matchReferences;
        PrefixReferences = prefixReferences;
        SuffixReferences = suffixReferences;
        InputReferences = inputReferences;
        GroupReferences = groupReferences;
    }

    /// <summary>Characters the substitution contributes on its own, counting each group reference's own text.</summary>
    public int LiteralLength { get; }

    /// <summary>References to the whole match: <c>$&amp;</c> and <c>$0</c>.</summary>
    public int MatchReferences { get; }

    /// <summary>References to the text before the match: <c>$`</c>.</summary>
    public int PrefixReferences { get; }

    /// <summary>References to the text after the match: <c>$'</c>.</summary>
    public int SuffixReferences { get; }

    /// <summary>References to the whole input: <c>$_</c>.</summary>
    public int InputReferences { get; }

    /// <summary>References to a capture group: <c>$+</c>, <c>$n</c> with n &gt; 0, and <c>${name}</c>.</summary>
    public int GroupReferences { get; }

    /// <summary>
    /// Whether the substitution refers to anything at all, which makes the linear engine compute full match data.
    /// Measured over the same 65,536-character input: <c>$0</c>, <c>$&amp;</c>, <c>$1</c>, <c>$`</c>, <c>$'</c>
    /// and <c>$_</c> each cost about two seconds, a literal or <c>$$</c> about half a second.
    /// </summary>
    public bool NeedsCaptures =>
        GroupReferences + MatchReferences + PrefixReferences + SuffixReferences + InputReferences > 0;

    /// <summary>
    /// Parses a .NET replacement pattern.
    /// </summary>
    public static RegexSubstitution Parse(string substitution)
    {
        ArgumentNullException.ThrowIfNull(substitution);

        int literal = 0, match = 0, prefix = 0, suffix = 0, input = 0, group = 0;
        var i = 0;

        while (i < substitution.Length)
        {
            if (substitution[i] != '$' || i + 1 >= substitution.Length)
            {
                literal++;
                i++;
                continue;
            }

            var next = substitution[i + 1];
            switch (next)
            {
                case '$':
                    literal++;
                    i += 2;
                    continue;
                case '&':
                    match++;
                    i += 2;
                    continue;
                case '`':
                    prefix++;
                    i += 2;
                    continue;
                case '\'':
                    suffix++;
                    i += 2;
                    continue;
                case '_':
                    input++;
                    i += 2;
                    continue;
                case '+':
                    group++;
                    literal += 2;
                    i += 2;
                    continue;
            }

            if (char.IsAsciiDigit(next))
            {
                var end = i + 1;
                while (end < substitution.Length && char.IsAsciiDigit(substitution[end]))
                {
                    end++;
                }

                if (end == i + 2 && next == '0')
                {
                    match++;
                }
                else
                {
                    group++;
                    literal += end - i;
                }

                i = end;
                continue;
            }

            if (next == '{')
            {
                var close = substitution.IndexOf('}', i + 2);
                if (close > 0)
                {
                    group++;
                    literal += close - i + 1;
                    i = close + 1;
                    continue;
                }
            }

            literal++;
            i++;
        }

        return new RegexSubstitution(literal, match, prefix, suffix, input, group);
    }

    /// <summary>
    /// The longest text this substitution can expand to for one match.
    /// </summary>
    /// <param name="matchIndex">Where the match starts.</param>
    /// <param name="matchLength">The match's length.</param>
    /// <param name="inputLength">The input's length.</param>
    /// <param name="groupLengthBound">The longest a capture group can be.</param>
    public long MaxExpansionLength(int matchIndex, int matchLength, int inputLength, int groupLengthBound) =>
        LiteralLength
        + ((long)MatchReferences * matchLength)
        + ((long)PrefixReferences * matchIndex)
        + ((long)SuffixReferences * (inputLength - matchIndex - matchLength))
        + ((long)InputReferences * inputLength)
        + ((long)GroupReferences * groupLengthBound);
}
