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

    /// <summary>Returns the positions just after each consecutive base segment's closing <c>/</c>.</summary>
    private static List<int> SegmentEnds(string text, int start, int maxSegments)
    {
        var ends = new List<int>();
        var position = start;
        while (ends.Count < maxSegments)
        {
            while (position < text.Length && IsBaseCharacter(text[position]))
            {
                position++;
            }

            if (position == text.Length || text[position] != '/')
            {
                break;
            }

            position++;
            ends.Add(position);
        }

        return ends;
    }

    private SilverReferenceMatch? MatchLiteral(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            var scheme = SchemeLength(text, start);
            if (scheme > 0)
            {
                var ends = SegmentEnds(text, start + scheme, int.MaxValue);
                for (var index = ends.Count - 1; index >= 0; index--)
                {
                    if (TryTypeAndId(text, ends[index], out var baseType, out var baseId))
                    {
                        return new SilverReferenceMatch(text[start..ends[index]], baseType, baseId, null, null);
                    }
                }
            }

            if (TryTypeAndId(text, start, out var type, out var id))
            {
                return new SilverReferenceMatch(null, type, id, null, null);
            }
        }

        return null;
    }

    private SilverReferenceMatch? MatchLogical(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            var scheme = SchemeLength(text, start);
            if (scheme > 0)
            {
                var ends = SegmentEnds(text, start + scheme, 1);
                if (ends.Count == 1 && TryTypeAndQuery(text, ends[0], out var baseMatch))
                {
                    return baseMatch with { Base = text[start..ends[0]] };
                }
            }

            if (TryTypeAndQuery(text, start, out var match))
            {
                return match;
            }
        }

        return null;
    }

    private bool TryTypeAndId(string text, int start, out string type, out string id)
    {
        type = string.Empty;
        id = string.Empty;
        var slash = text.IndexOf('/', start);
        if (slash < 0 || !IsResourceType(text[start..slash]))
        {
            return false;
        }

        var end = slash + 1;
        while (end < text.Length && end - slash - 1 < MaxIdLength && IsIdCharacter(text[end]))
        {
            end++;
        }

        if (end == slash + 1)
        {
            return false;
        }

        type = text[start..slash];
        id = text[(slash + 1)..end];
        return true;
    }

    private bool TryTypeAndQuery(string text, int start, out SilverReferenceMatch match)
    {
        match = null!;
        var query = text.IndexOf('?', start);
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

    /// <summary>Whether the R4 schema defines <paramref name="candidate"/> as a concrete resource type (exact case).</summary>
    private bool IsResourceType(string candidate)
    {
        if (candidate.Length == 0 || !candidate.All(char.IsAsciiLetter))
        {
            return false;
        }

        var definition = _schema.GetTypeDefinition(candidate);
        return definition is not null &&
               definition.Info.Name == candidate &&
               definition.Info.IsResource &&
               !definition.Info.IsAbstract;
    }
}
