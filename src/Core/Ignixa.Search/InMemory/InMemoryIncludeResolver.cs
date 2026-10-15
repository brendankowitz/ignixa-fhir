// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Resolves <c>_include</c> and <c>_revinclude</c> for a page of matches from their index entries.
/// </summary>
/// <remarks>
/// <para>
/// <c>_include=Source:param[:Target]</c> adds the resources that the page's <c>Source</c> resources reference through
/// <c>param</c> (any reference parameter for <c>Source:*</c>, and of any resource for <c>*</c>), limited to <c>Target</c> when given.
/// <c>_revinclude=Source:param[:Target]</c> adds the <c>Source</c> resources that reference a page resource through
/// <c>param</c>. A reference to another server, or to a resource the source does not hold, includes nothing.
/// </para>
/// <para>
/// <c>:iterate</c> includes are applied again to what the previous round added, until a round adds nothing. A
/// resource is included at most once and never when it is itself a match. The result is in discovery order: round by
/// round, forward includes before reverse ones, each in the order the page, its entries and the source enumerate.
/// Resolution stops once the caller's maximum is reached, so <c>:iterate</c> cannot expand without bound.
/// </para>
/// </remarks>
internal static class InMemoryIncludeResolver
{
    /// <summary>The source type of <c>_include=*</c>, which follows every reference of every resource it is applied to.</summary>
    private const string AnySource = "*";

    public static IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Resolve(
        IInMemorySearchSource source,
        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> matches,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int maxIncluded)
    {
        if (revIncludes.FirstOrDefault(revInclude => revInclude.WildCard) is { } wildcard)
        {
            throw new SearchOperationNotSupportedException($"{wildcard} is not supported in in-memory search: a reverse include must name its search parameter.");
        }

        HashSet<(string ResourceType, string Id)> seen = [.. matches.Select(match => Key(match.Location))];
        List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> included = [];

        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> round = matches;
        IReadOnlyList<IncludeExpression> forward = includes;
        IReadOnlyList<IncludeExpression> reverse = revIncludes;
        while (round.Count > 0 && (forward.Count > 0 || reverse.Count > 0) && included.Count < maxIncluded)
        {
            List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> added = [];
            foreach ((ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) resource in
                Forward(source, round, forward).Concat(Reverse(source, round, reverse)))
            {
                if (included.Count + added.Count == maxIncluded)
                {
                    break;
                }

                if (seen.Add(Key(resource.Location)))
                {
                    added.Add(resource);
                }
            }

            included.AddRange(added);
            round = added;
            forward = [.. includes.Where(include => include.Iterate)];
            reverse = [.. revIncludes.Where(include => include.Iterate)];
        }

        return included;
    }

    private static IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Forward(
        IInMemorySearchSource source,
        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> round,
        IReadOnlyList<IncludeExpression> includes) =>
        round
            .SelectMany(resource => includes
                .Where(include => include.SourceResourceType == AnySource || include.SourceResourceType == resource.Location.ResourceType)
                .SelectMany(include => SearchQueryInterpreter
                    .LocalReferences(resource.Index, include.WildCard ? null : include.ReferenceSearchParameter)
                    .Where(reference => include.TargetResourceType is null || include.TargetResourceType == reference.ResourceType)))
            .Select(reference => source.Resolve(reference.ResourceType, reference.Id))
            .Where(target => target.HasValue)
            .Select(target => target!.Value);

    private static IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Reverse(
        IInMemorySearchSource source,
        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> round,
        IReadOnlyList<IncludeExpression> revIncludes)
    {
        if (revIncludes.Count == 0)
        {
            return [];
        }

        HashSet<(string ResourceType, string Id)> targets = [.. round.Select(resource => Key(resource.Location))];
        return revIncludes.SelectMany(revInclude => source
            .Enumerate(revInclude.SourceResourceType)
            .Where(candidate => SearchQueryInterpreter.LocalReferences(candidate.Index, revInclude.ReferenceSearchParameter)
                .Any(reference => (revInclude.TargetResourceType is null || revInclude.TargetResourceType == reference.ResourceType) && targets.Contains(reference))));
    }

    private static (string ResourceType, string Id) Key(ResourceKey location) => (location.ResourceType, location.Id);
}
