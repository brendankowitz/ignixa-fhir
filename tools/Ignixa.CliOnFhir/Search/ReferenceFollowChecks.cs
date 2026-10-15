// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Issues that keep a chain, <c>_has</c> or include from looking more complete than it is: references it could not follow,
/// and includes that can never add anything to this search.
/// </summary>
internal static class ReferenceFollowChecks
{
    public const string UnresolvedCode = "references-unresolved";

    public const string NotApplicableCode = "include-not-applicable";

    /// <summary>
    /// One <c>information</c> issue per reference parameter a chain, <c>_has</c> or include followed whose index holds
    /// references the engine cannot follow: not a local <c>Type/id</c> (an absolute URL, a bare id, or a <c>urn:</c> such as
    /// a Bundle's <c>urn:uuid</c> fullUrl, which Ignixa's reference converter keeps in the index), or a canonical, which a
    /// parameter that also selects references (R5 <c>RequestOrchestration-participant</c>) or <c>_include=*</c> skips. Read
    /// from the index entries of the parameter followed, over every resource of a chain's referencing type, the page and
    /// its included resources for a forward include, and every resource of a reverse include's source type.
    /// </summary>
    public static IEnumerable<IssueItem> UnresolvedReferences(FolderSearchSource source, SearchOptions options, IReadOnlyCollection<IndexedResource> pageAndIncluded)
    {
        List<(string ResourceType, SearchParameterInfo? Parameter, IReadOnlyList<IndexedResource> Resources)> scopes = [];
        foreach (ChainedExpression chain in Chains(options.Expression))
        {
            scopes.AddRange(chain.ResourceTypes.Select(type => (type, (SearchParameterInfo?)chain.ReferenceSearchParameter, source.Resources(type))));
        }

        foreach (IncludeExpression include in options.Include)
        {
            IReadOnlyList<IndexedResource> read = [.. pageAndIncluded.Where(resource => AppliesTo(include, resource.Key.ResourceType))];
            scopes.Add((include.SourceResourceType, include.WildCard ? null : include.ReferenceSearchParameter, read));
        }

        foreach (IncludeExpression revInclude in options.RevInclude)
        {
            scopes.Add((revInclude.SourceResourceType, revInclude.ReferenceSearchParameter, source.Resources(revInclude.SourceResourceType)));
        }

        return scopes
            .GroupBy(scope => (scope.ResourceType, Parameter: scope.Parameter?.Code ?? "*"))
            .Select(group => (group.Key, Count: group.Max(scope => NotLocal(scope.Resources, scope.Parameter))))
            .Where(count => count.Count > 0)
            .Select(count => new IssueItem(
                "information",
                UnresolvedCode,
                $"{count.Count} references through {count.Key.ResourceType}:{count.Key.Parameter} are not local 'Type/id' references (canonicals, urn:uuid, absolute URLs or bare ids) and were not followed, so the chain or include may be missing resources.",
                Location: null));
    }

    /// <summary>
    /// A <c>warning</c> for each include that can add nothing: an <c>_include</c> whose source type is neither the searched
    /// type nor reached through an <c>:iterate</c> include, a <c>_revinclude</c> whose parameter never references the
    /// searched type or a reached one.
    /// </summary>
    public static IEnumerable<IssueItem> InapplicableIncludes(SearchOptions options)
    {
        string searched = options.ResourceType;
        HashSet<string> reached = new(StringComparer.Ordinal);
        foreach (IncludeExpression include in options.Include.Where(include => AppliesTo(include, searched)))
        {
            reached.UnionWith(include.Produces ?? []);
        }

        foreach (IncludeExpression revInclude in options.RevInclude.Where(revInclude => RevTargets(revInclude).Contains(searched)))
        {
            reached.Add(revInclude.SourceResourceType);
        }

        int before;
        do
        {
            before = reached.Count;
            reached.UnionWith(options.Include.Where(include => include.Iterate && reached.Any(type => AppliesTo(include, type))).SelectMany(include => include.Produces ?? []));
            reached.UnionWith(options.RevInclude.Where(revInclude => revInclude.Iterate && RevTargets(revInclude).Overlaps(reached)).Select(revInclude => revInclude.SourceResourceType));
        }
        while (reached.Count != before);

        foreach (IncludeExpression include in options.Include)
        {
            if (!AppliesTo(include, searched) && !(include.Iterate && reached.Contains(include.SourceResourceType)))
            {
                yield return NotApplicable(include, $"its source type {include.SourceResourceType} is neither the searched type {searched} nor reached by an :iterate include");
            }
        }

        foreach (IncludeExpression revInclude in options.RevInclude)
        {
            HashSet<string> targets = RevTargets(revInclude);
            if (!targets.Contains(searched) && !(revInclude.Iterate && targets.Overlaps(reached)))
            {
                string reachable = revInclude.Iterate ? " or a type an :iterate include reaches" : string.Empty;
                string why = revInclude.TargetResourceType is { } target && target != searched
                    ? $"it includes {revInclude.SourceResourceType} resources that reference {target}, which is not the searched type {searched}{reachable}"
                    : $"{revInclude.SourceResourceType}:{revInclude.ReferenceSearchParameter.Code} references {string.Join(", ", revInclude.ReferenceSearchParameter.TargetResourceTypes)}, never the searched type {searched}{reachable}";
                yield return NotApplicable(revInclude, why);
            }
        }
    }

    /// <summary>Whether <paramref name="include"/> is applied to resources of <paramref name="resourceType"/>; <c>_include=*</c> applies to all.</summary>
    private static bool AppliesTo(IncludeExpression include, string resourceType) =>
        include.SourceResourceType is "*" || include.SourceResourceType == resourceType;

    private static HashSet<string> RevTargets(IncludeExpression revInclude)
    {
        IEnumerable<string> declared = revInclude.ReferenceSearchParameter.TargetResourceTypes;
        return revInclude.TargetResourceType is { } target
            ? [.. declared.Where(type => type == target)]
            : [.. declared];
    }

    private static IssueItem NotApplicable(IncludeExpression include, string why)
    {
        string name = (include.Reversed ? "_revinclude" : "_include") + (include.Iterate ? ":iterate" : string.Empty);
        string value = $"{include.SourceResourceType}:{(include.WildCard ? "*" : include.ReferenceSearchParameter.Code)}{(include.TargetResourceType is { } target && !include.Reversed ? ":" + target : string.Empty)}";
        return new IssueItem("warning", NotApplicableCode, $"'{name}={value}' adds nothing to this search: {why}.", Location: null);
    }

    private static int NotLocal(IEnumerable<IndexedResource> resources, SearchParameterInfo? parameter) =>
        resources.Sum(resource => resource.Index.Count(entry =>
            (parameter is null ? entry.SearchParameter.Type == SearchParamType.Reference : entry.SearchParameter.Name == parameter.Name)
            && entry.Value switch
            {
                UriSearchValue => true,
                ReferenceSearchValue reference => reference.BaseUri is not null
                    || string.IsNullOrEmpty(reference.ResourceType)
                    || string.IsNullOrEmpty(reference.ResourceId)
                    || reference.ResourceId.StartsWith("urn:", StringComparison.Ordinal),
                _ => false,
            }));

    private static IEnumerable<ChainedExpression> Chains(Expression? expression) =>
        expression switch
        {
            ChainedExpression chain => [chain, .. Chains(chain.Expression)],
            MultiaryExpression multiary => multiary.Expressions.SelectMany(Chains),
            UnionExpression union => union.Expressions.SelectMany(Chains),
            NotExpression not => Chains(not.Expression),
            _ => [],
        };
}
