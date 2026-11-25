// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Recognises the two reference forms Silver normalizes, with the same search behaviour as the source pipeline:
/// the pattern may start anywhere in the string, earlier starts win, and at each start a service base is preferred
/// over none.
/// </summary>
/// <remarks>
/// <para>
/// A literal reference is an optional base (<c>http://</c> or <c>https://</c> followed by one or more segments, each
/// ending in <c>/</c> and possibly empty), a resource type, <c>/</c>, and an id of 1 to 64 letters, digits, hyphens or
/// dots. The id takes as many characters as it can; anything after it (a version suffix, for example) is ignored.
/// When several base lengths fit, the longest wins.
/// </para>
/// <para>
/// A logical reference is an optional base with exactly one segment, a resource type, <c>?identifier=</c>, and the
/// rest of the line, which splits at its last <c>|</c> into system and value.
/// </para>
/// <para>
/// A type counts only when the R4 schema defines it as a concrete resource; the source pipeline uses its own list of
/// names, so a name outside R4 behaves differently (unverified impact).
/// </para>
/// </remarks>
internal sealed class SilverReferencePattern
{
    private const string LogicalQuery = "?identifier=";
    private const int MaxIdLength = 64;

    // Longer than any R4 resource type name, so capping the type scan here changes no result; it bounds the work done
    // at each start position, which keeps matching linear in the reference length.
    private const int MaxTypeLength = 64;

    private readonly ISchema _schema;

    public SilverReferencePattern(ISchema schema)
    {
        _schema = schema;
    }

    /// <summary>Applies the literal form and, only when it does not match, the logical form.</summary>
    public SilverReferenceMatch? Match(string reference) => MatchLiteral(reference) ?? MatchLogical(reference);

    private static bool IsBaseCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '\\' or '.' or ':' or '%' or '$';

    private static bool IsIdCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '.';

    private static int SchemeLength(string text, int start)
    {
        if (string.CompareOrdinal(text, start, "https://", 0, "https://".Length) == 0)
        {
            return "https://".Length;
        }

        return string.CompareOrdinal(text, start, "http://", 0, "http://".Length) == 0 ? "http://".Length : 0;
    }

    /// <summary>The end of the 1 to <see cref="MaxTypeLength"/> ASCII letters at <paramref name="start"/>, or -1.</summary>
    private static int TypeEnd(string text, int start)
    {
        var end = start;
        while (end < text.Length && end - start <= MaxTypeLength && char.IsAsciiLetter(text[end]))
        {
            end++;
        }

        return end == start || end - start > MaxTypeLength ? -1 : end;
    }

    /// <summary>The position just after the single base segment starting at <paramref name="start"/>, or -1.</summary>
    private static int SegmentEnd(string text, int start)
    {
        var position = start;
        while (position < text.Length && IsBaseCharacter(text[position]))
        {
            position++;
        }

        return position < text.Length && text[position] == '/' ? position + 1 : -1;
    }

    private SilverReferenceMatch? MatchLiteral(string text)
    {
        int[]? furthestTypeAfterBase = null;
        for (var start = 0; start < text.Length; start++)
        {
            var scheme = SchemeLength(text, start);
            if (scheme > 0)
            {
                furthestTypeAfterBase ??= FurthestTypeAfterBase(text);
                var typeStart = furthestTypeAfterBase[start + scheme];
                if (typeStart >= 0)
                {
                    TryTypeAndId(text, typeStart, out var baseSlash, out var baseIdEnd);
                    return new SilverReferenceMatch(
                        text[start..typeStart],
                        text[typeStart..baseSlash],
                        text[(baseSlash + 1)..baseIdEnd],
                        null,
                        null);
                }
            }

            if (TryTypeAndId(text, start, out var slash, out var idEnd))
            {
                return new SilverReferenceMatch(null, text[start..slash], text[(slash + 1)..idEnd], null, null);
            }
        }

        return null;
    }

    /// <summary>
    /// For each position, the furthest position reached through one or more base segments (each a run of base
    /// characters closed by <c>/</c>) at which a literal type and id begin, or -1. That is the longest base a scheme
    /// ending at the position can take.
    /// </summary>
    /// <remarks>
    /// A segment always ends at the first <c>/</c> after its base characters, so the segments from any position form
    /// one fixed chain and chains from later positions are suffixes of it. One right-to-left pass therefore answers
    /// every scheme in the text, instead of rescanning the chain at each repeated scheme.
    /// </remarks>
    private int[] FurthestTypeAfterBase(string text)
    {
        var furthest = new int[text.Length + 1];
        furthest[text.Length] = -1;
        var runEnd = text.Length;
        for (var position = text.Length - 1; position >= 0; position--)
        {
            if (!IsBaseCharacter(text[position]))
            {
                runEnd = position;
            }

            if (runEnd == text.Length || text[runEnd] != '/')
            {
                furthest[position] = -1;
                continue;
            }

            if (position < runEnd)
            {
                // Same run, same closing slash, same answer.
                furthest[position] = furthest[position + 1];
                continue;
            }

            var segmentEnd = position + 1;
            if (furthest[segmentEnd] >= 0)
            {
                furthest[position] = furthest[segmentEnd];
            }
            else
            {
                furthest[position] = TryTypeAndId(text, segmentEnd, out _, out _) ? segmentEnd : -1;
            }
        }

        return furthest;
    }

    private SilverReferenceMatch? MatchLogical(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            // Segments scanned from different schemes never overlap, because a scheme's "//" closes any run before
            // it, so these scans stay linear in total.
            var scheme = SchemeLength(text, start);
            if (scheme > 0)
            {
                var segmentEnd = SegmentEnd(text, start + scheme);
                if (segmentEnd >= 0 && TryTypeAndQuery(text, segmentEnd, out var baseMatch))
                {
                    return baseMatch with { Base = text[start..segmentEnd] };
                }
            }

            if (TryTypeAndQuery(text, start, out var match))
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Whether a type, <c>/</c> and an id of 1 to <see cref="MaxIdLength"/> characters begin at <paramref name="start"/>.</summary>
    private bool TryTypeAndId(string text, int start, out int slash, out int idEnd)
    {
        slash = TypeEnd(text, start);
        idEnd = -1;
        if (slash < 0 || slash == text.Length || text[slash] != '/')
        {
            return false;
        }

        var end = slash + 1;
        while (end < text.Length && end - slash - 1 < MaxIdLength && IsIdCharacter(text[end]))
        {
            end++;
        }

        if (end == slash + 1 || !IsResourceType(text[start..slash]))
        {
            return false;
        }

        idEnd = end;
        return true;
    }

    private bool TryTypeAndQuery(string text, int start, out SilverReferenceMatch match)
    {
        match = null!;
        var query = TypeEnd(text, start);
        if (query < 0 ||
            string.CompareOrdinal(text, query, LogicalQuery, 0, LogicalQuery.Length) != 0 ||
            !IsResourceType(text[start..query]))
        {
            return false;
        }

        var valueStart = query + LogicalQuery.Length;
        var lineEnd = text.IndexOf('\n', valueStart);
        var identifier = lineEnd < 0 ? text[valueStart..] : text[valueStart..lineEnd];
        var bar = identifier.LastIndexOf('|');
        match = bar < 0
            ? new SilverReferenceMatch(null, text[start..query], null, null, identifier)
            : new SilverReferenceMatch(null, text[start..query], null, identifier[..bar], identifier[(bar + 1)..]);
        return true;
    }

    /// <summary>Whether the R4 schema defines <paramref name="candidate"/> (ASCII letters only) as a concrete resource type.</summary>
    private bool IsResourceType(string candidate)
    {
        var definition = _schema.GetTypeDefinition(candidate);
        return definition is not null &&
               definition.Info.Name == candidate &&
               definition.Info.IsResource &&
               !definition.Info.IsAbstract;
    }
}