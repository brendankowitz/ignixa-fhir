// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
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
internal static partial class ReferenceFollowChecks
{
    public const string UnresolvedCode = "references-unresolved";

    public const string NotApplicableCode = "include-not-applicable";

    /// <summary>
    /// One <c>information</c> issue per reference parameter a chain, <c>_has</c> or include followed, over resources that
    /// hold references the engine cannot follow: indexed ones that are not a local <c>Type/id</c> (an absolute URL, a bare
    /// id), and <c>urn:</c> references (Bundle <c>urn:uuid</c> fullUrls), which the indexer drops so no index entry shows
    /// them; those are found by scanning the resources' JSON. The resources read are every resource of a chain's
    /// referencing type, the page and its included resources for a forward include, and every resource of a reverse
    /// include's source type.
    /// </summary>
    public static IEnumerable<IssueItem> UnresolvedReferences(string rootPath, FolderSearchSource source, SearchOptions options, IReadOnlyCollection<IndexedResource> pageAndIncluded)
    {
        List<(string ResourceType, SearchParameterInfo? Parameter, IReadOnlyList<IndexedResource> Resources)> scopes = [];
        foreach (ChainedExpression chain in Chains(options.Expression))
        {
            scopes.AddRange(chain.ResourceTypes.Select(type => (type, (SearchParameterInfo?)chain.ReferenceSearchParameter, source.Resources(type))));
        }

        foreach (IncludeExpression include in options.Include)
        {
            IReadOnlyList<IndexedResource> read = [.. pageAndIncluded.Where(resource => resource.Key.ResourceType == include.SourceResourceType)];
            scopes.Add((include.SourceResourceType, include.WildCard ? null : include.ReferenceSearchParameter, read));
        }

        foreach (IncludeExpression revInclude in options.RevInclude)
        {
            scopes.Add((revInclude.SourceResourceType, revInclude.ReferenceSearchParameter, source.Resources(revInclude.SourceResourceType)));
        }

        (HashSet<CatalogEntry> holdingUrn, HashSet<CatalogEntry> unreadable) = ScanForUrnReferences(rootPath, scopes.SelectMany(scope => scope.Resources).Select(resource => resource.Entry));
        return scopes
            .GroupBy(scope => (scope.ResourceType, Parameter: scope.Parameter?.Code ?? "*"))
            .Select(group => (
                group.Key,
                NotLocal: group.Max(scope => NotLocal(scope.Resources, scope.Parameter)),
                Urn: group.Max(scope => scope.Resources.Count(resource => holdingUrn.Contains(resource.Entry))),
                Unreadable: group.Max(scope => scope.Resources.Count(resource => unreadable.Contains(resource.Entry)))))
            .Where(count => count.NotLocal + count.Urn + count.Unreadable > 0)
            .Select(count => new IssueItem(
                "information",
                UnresolvedCode,
                $"References through {count.Key.ResourceType}:{count.Key.Parameter} that are not local 'Type/id' references were not followed, so the chain or include may be missing resources: "
                    + $"{count.NotLocal} absolute or untyped references, and {count.Urn} {count.Key.ResourceType} resources holding urn: references (such as Bundle urn:uuid fullUrls)"
                    + (count.Unreadable > 0 ? $"; {count.Unreadable} resources could not be read to check." : "."),
                Location: null));
    }

    /// <summary>
    /// The entries whose JSON holds a <c>"reference": "urn:...</c>, reading each file once; entries whose file can no
    /// longer be read are returned separately, unchecked.
    /// </summary>
    private static (HashSet<CatalogEntry> HoldingUrn, HashSet<CatalogEntry> Unreadable) ScanForUrnReferences(string rootPath, IEnumerable<CatalogEntry> entries)
    {
        HashSet<CatalogEntry> holding = [];
        HashSet<CatalogEntry> unreadable = [];
        foreach (IGrouping<string, CatalogEntry> file in entries.Distinct().GroupBy(entry => entry.FilePath, StringComparer.Ordinal))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(Path.Combine(rootPath, file.Key));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable.UnionWith(file);
                continue;
            }

            foreach (CatalogEntry entry in file)
            {
                if (entry.Offset + entry.Length > bytes.Length)
                {
                    unreadable.Add(entry);
                }
                else if (UrnReference().IsMatch(Encoding.UTF8.GetString(bytes, (int)entry.Offset, entry.Length)))
                {
                    holding.Add(entry);
                }
            }
        }

        return (holding, unreadable);
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
        foreach (IncludeExpression include in options.Include.Where(include => include.SourceResourceType == searched))
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
            reached.UnionWith(options.Include.Where(include => include.Iterate && reached.Contains(include.SourceResourceType)).SelectMany(include => include.Produces ?? []));
            reached.UnionWith(options.RevInclude.Where(revInclude => revInclude.Iterate && RevTargets(revInclude).Overlaps(reached)).Select(revInclude => revInclude.SourceResourceType));
        }
        while (reached.Count != before);

        foreach (IncludeExpression include in options.Include)
        {
            if (include.SourceResourceType != searched && !(include.Iterate && reached.Contains(include.SourceResourceType)))
            {
                yield return NotApplicable(include, $"its source type {include.SourceResourceType} is neither the searched type {searched} nor reached by an :iterate include");
            }
        }

        foreach (IncludeExpression revInclude in options.RevInclude)
        {
            HashSet<string> targets = RevTargets(revInclude);
            if (!targets.Contains(searched) && !(revInclude.Iterate && targets.Overlaps(reached)))
            {
                yield return NotApplicable(revInclude, $"{revInclude.SourceResourceType}:{revInclude.ReferenceSearchParameter.Code} references {string.Join(", ", revInclude.ReferenceSearchParameter.TargetResourceTypes)}, never {revInclude.TargetResourceType ?? searched}");
            }
        }
    }

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
            && entry.Value is ReferenceSearchValue reference
            && (reference.BaseUri is not null || string.IsNullOrEmpty(reference.ResourceType) || string.IsNullOrEmpty(reference.ResourceId))));

    [GeneratedRegex("\"reference\"\\s*:\\s*\"urn:", RegexOptions.CultureInvariant)]
    private static partial Regex UrnReference();

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
