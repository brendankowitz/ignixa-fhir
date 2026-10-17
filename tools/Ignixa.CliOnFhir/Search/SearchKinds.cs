// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Record kinds in search (<see cref="FolderKinds"/>): a note when the matches mix kinds of their type, and the short name of a
/// profile accepted as a <c>_profile</c> value.
/// </summary>
internal static class SearchKinds
{
    public const string MixedCode = "results-mix-kinds";

    /// <summary>Most kinds named, with their filters, in the mixed-kinds note; the rest are counted.</summary>
    public const int ListedKinds = 4;

    private const string ProfileParameter = "_profile";

    /// <summary>
    /// A note naming the kinds <paramref name="matches"/> mix, with the count and the filter of each, when there is more than one;
    /// null otherwise. Counted over every match, not the page.
    /// </summary>
    public static IssueItem? Mixed(FolderSession session, string resourceType, IReadOnlyList<IndexedResource> matches)
    {
        if (matches.Count < 2 || !session.Kinds.IsMixed(resourceType))
        {
            return null;
        }

        int kinds = matches.Select(match => session.Kinds.KindOf(match.Entry)).Distinct().Count();
        if (kinds < 2)
        {
            return null;
        }

        (IReadOnlyList<(FolderKinds.KindCount Kind, int Count)> top, int rest) = session.Kinds.Count(matches.Select(match => match.Entry), ListedKinds);
        IEnumerable<string> listed = top.Select(kind => kind.Kind.Filter is { } filter ? $"{kind.Kind.ShortName} {kind.Count} [{filter}]" : $"{kind.Kind.ShortName} {kind.Count}");
        string more = rest > 0 ? $", {kinds - top.Count} more kinds {rest}" : string.Empty;
        return new IssueItem(
            "information",
            MixedCode,
            $"The {matches.Count} matches mix {kinds} kinds of {resourceType}: {string.Join(", ", listed)}{more}. Add a kind's filter to the query to count or list only it; a column {{\"kind\":\"{ColumnSearch.KindPath}\"}} shows each row's kind.",
            Location: null);
    }

    /// <summary>
    /// <paramref name="query"/> with each <c>_profile</c> value that is a profile's short name (no <c>/</c> or <c>:</c>) replaced by
    /// the one canonical of <paramref name="resourceType"/>'s kinds it names, and a warning per replacement; unchanged otherwise.
    /// </summary>
    public static (string? Query, IReadOnlyList<IssueItem> Adjustments) ExpandProfileNames(FolderSession session, string resourceType, string? query)
    {
        if (string.IsNullOrEmpty(query) || !query.Contains(ProfileParameter + "=", StringComparison.Ordinal))
        {
            return (query, []);
        }

        List<IssueItem> adjustments = [];
        string[] parts = query.Split('&');
        for (int index = 0; index < parts.Length; index++)
        {
            int equals = parts[index].IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || !string.Equals(parts[index][..equals], ProfileParameter, StringComparison.Ordinal))
            {
                continue;
            }

            string[] values = parts[index][(equals + 1)..].Split(',');
            for (int value = 0; value < values.Length; value++)
            {
                string name = values[value];
                if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal)
                    || session.Kinds.ProfileNamed(resourceType, name) is not { } canonical)
                {
                    continue;
                }

                values[value] = canonical;
                adjustments.Add(new IssueItem(
                    "warning",
                    LenientArguments.Code,
                    $"_profile={name} names no canonical; it was read as _profile={canonical}, the one {resourceType} profile in the folder that name (its last segment, or its kind's short name) stands for.",
                    Location: null));
            }

            parts[index] = $"{ProfileParameter}={string.Join(',', values)}";
        }

        return (string.Join('&', parts), adjustments);
    }
}
