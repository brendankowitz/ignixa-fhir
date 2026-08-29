using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's Resolve stage: walks the predicate tree once (plus includes, sort, and compartments),
/// collects every search parameter and resource type, and resolves them through <see cref="ISymbolResolver"/>.
/// This is the compiler's only I/O, done up front, producing an immutable <see cref="SymbolTable"/> that
/// Lower and Emit consume synchronously, plus the parameters the resolver could not find.
/// </summary>
internal static class Resolve
{
    internal static async Task<ResolvedSymbols> RunAsync(
        CompilationContext context,
        SymbolResolution deps,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deps);

        var expression = context.Expression;
        var includes = context.Includes;
        var revIncludes = context.RevIncludes;
        var sort = context.Sort;
        var resolver = deps.Resolver;
        var targetResourceType = context.TargetResourceType;
        var compartmentDefinitionManager = deps.CompartmentDefinitionManager;
        var searchParameterDefinitionManager = deps.SearchParameterDefinitionManager;
        var additionalResourceTypes = context.ResourceTypes;
        var accessConstraints = context.AccessConstraints;

        // Kept in step with the allow-list Lower enforces from the same context: each permitted type's id
        // must resolve here (an unknown one keeps the unmatchable sentinel rather than being dropped, which
        // would widen the allow-list into a fail-open bypass).
        var allowedResourceTypes = context.AllowedResourceTypes;

        var collector = new SymbolCollectingVisitor();
        if (expression is not null)
        {
            expression.AcceptVisitor(collector, context: null);
        }

        foreach (var include in includes)
        {
            collector.CollectInclude(include);
        }

        foreach (var revInclude in revIncludes)
        {
            collector.CollectInclude(revInclude);
        }

        foreach (var sortExpression in sort)
        {
            collector.CollectSort(sortExpression);
        }

        if (context.LastNOptions is { } lastN)
        {
            collector.Parameters.Add(lastN.CodeParameter);
            collector.Parameters.Add(lastN.EffectiveDateParameter);
        }

        // Access constraints are lowered as ordinary expressions by AccessConstraintApplier, so their symbols
        // must be resolved here. Omitting them made a constraint throw from Lower unless the user's query also
        // named the same parameter.
        foreach (var constraint in accessConstraints ?? [])
        {
            collector.CollectConstraint(constraint);
        }

        var compartmentMembership = ResolveCompartmentMembership(collector, compartmentDefinitionManager, searchParameterDefinitionManager);
        var notReferencedPaths = ResolveNotReferencedPaths(collector, searchParameterDefinitionManager);

        var searchParamIds = new Dictionary<string, short>();
        var unresolved = new List<SearchParameterInfo>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);

            // A null Url is unresolvable, not a crash: SymbolTable is keyed by Url, so report it as a miss.
            if (id.HasValue && parameter.Url is { } url)
            {
                searchParamIds[url.ToString()] = id.Value;
            }
            else
            {
                unresolved.Add(parameter);
            }
        }

        var resourceTypes = new HashSet<string>(collector.ResourceTypes);
        if (targetResourceType is not null)
        {
            resourceTypes.Add(targetResourceType);
        }

        // A system-level caller resolves _type before compiling and passes its type list here, not in the
        // tree. Without this those names resolve to the unmatchable sentinel and the query matches nothing;
        // genuinely unknown types still keep the sentinel.
        if (additionalResourceTypes is not null)
        {
            resourceTypes.UnionWith(additionalResourceTypes);
        }

        // Resolves the allow-list type names so their ids are available to Lower's enforcement. Unions into
        // the RESOLUTION set only — never the searched base set (Lower derives that from the context's
        // TargetResourceType/ResourceTypes) — so resolving a permitted name here cannot widen results. An
        // unresolvable allowed type keeps the sentinel rather than being dropped, which would fail open.
        if (allowedResourceTypes is not null)
        {
            resourceTypes.UnionWith(allowedResourceTypes);
        }

        // A resource type the resolver cannot find is recorded as unmatchable rather than dropped: dropping it
        // becomes a KeyNotFoundException when Lower looks it up, so the first search against an empty catalog
        // would throw instead of returning an empty bundle.
        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            resourceTypeIds[resourceType] = id ?? SymbolTable.UnmatchableResourceTypeId;
        }

        var allSystems = new HashSet<string>(collector.TokenSystems, StringComparer.Ordinal);
        allSystems.UnionWith(collector.QuantitySystems);
        // Re-keyed off the requested set: SymbolTable's three-state contract needs an entry for every collected
        // system, and a resolver overriding the batch method could return fewer.
        var resolvedSystems = await resolver.GetSystemIdsAsync(allSystems, cancellationToken);
        var systemIds = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var system in allSystems)
        {
            systemIds[system] = resolvedSystems.GetValueOrDefault(system);
        }

        // Resolve every distinct quantity code exactly once, storing null for known misses.
        var quantityCodeIds = new Dictionary<string, int?>();
        foreach (var code in collector.QuantityCodes)
        {
            quantityCodeIds[code] = await resolver.GetQuantityCodeIdAsync(code, cancellationToken);
        }

        return new ResolvedSymbols(new SymbolTable(searchParamIds, resourceTypeIds, compartmentMembership, systemIds, quantityCodeIds, notReferencedPaths), unresolved);
    }

    /// <summary>
    /// Resolves each <c>_not-referenced=Type:path</c> pair to its reference parameter and adds it to the
    /// collector. An unresolvable/non-reference/null-Url pair is dropped (Lower falls back to a path-agnostic
    /// anti-join); a missing definition manager throws instead. Returns null when there are no pairs.
    /// </summary>
    private static Dictionary<(string SourceResourceType, string ReferencePath), SearchParameterInfo>? ResolveNotReferencedPaths(
        SymbolCollectingVisitor collector,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager)
    {
        if (collector.NotReferencedPaths.Count == 0)
        {
            return null;
        }

        if (searchParameterDefinitionManager is null)
        {
            throw new InvalidOperationException(
                "Resolve encountered a _not-referenced path filter (Type:path) but no " +
                "ISearchParameterDefinitionManager was supplied -- it is required to resolve the reference " +
                "path. Silently omitting it would widen the anti-join to a path-agnostic one, returning more " +
                "resources than the query asked for.");
        }

        var resolved = new Dictionary<(string, string), SearchParameterInfo>();
        foreach (var (sourceType, path) in collector.NotReferencedPaths)
        {
            if (resolved.ContainsKey((sourceType, path)))
            {
                continue;
            }

            if (!searchParameterDefinitionManager.TryGetSearchParameter(sourceType, path, out var parameter)
                || parameter.Type != SearchParamType.Reference
                || parameter.Url is null)
            {
                continue;
            }

            resolved[(sourceType, path)] = parameter;
            collector.Parameters.Add(parameter);
        }

        return resolved.Count == 0 ? null : resolved;
    }

    private static Dictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? ResolveCompartmentMembership(
        SymbolCollectingVisitor collector,
        ICompartmentDefinitionManager? compartmentDefinitionManager,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager)
    {
        if (collector.Compartments.Count == 0)
        {
            return null;
        }

        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>();
        foreach (var (compartmentType, _) in collector.Compartments)
        {
            if (membership.ContainsKey(compartmentType))
            {
                continue;
            }

            if (compartmentDefinitionManager is null || searchParameterDefinitionManager is null)
            {
                throw new InvalidOperationException(
                    $"Resolve encountered a compartment search for '{compartmentType}' but no " +
                    "ICompartmentDefinitionManager/ISearchParameterDefinitionManager was supplied -- both are " +
                    "required to resolve compartment membership.");
            }

            if (!Enum.TryParse<CompartmentType>(compartmentType, out var compartmentTypeEnum))
            {
                throw new InvalidOperationException($"Invalid compartment type: {compartmentType}");
            }

            var groups = new Dictionary<string, (SearchParameterInfo Parameter, List<string> ResourceTypes)>();
            if (compartmentDefinitionManager.TryGetResourceTypes(compartmentTypeEnum, out var allResourceTypes))
            {
                foreach (var resourceType in allResourceTypes)
                {
                    if (!compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentTypeEnum, out var searchParamCodes))
                    {
                        continue;
                    }

                    foreach (var code in searchParamCodes)
                    {
                        if (!searchParameterDefinitionManager.TryGetSearchParameter(resourceType, code, out var searchParam)
                            || searchParam.Type != SearchParamType.Reference)
                        {
                            continue;
                        }

                        // Same null-Url reasoning as the leaf loop: a member carrying none could never be
                        // looked up, so skip it. Resolve.RunAsync awaits this outside its try/catch, so an NRE
                        // here would escape and destroy the whole compilation.
                        if (searchParam.Url is not { } url)
                        {
                            continue;
                        }

                        var key = url.ToString();
                        if (!groups.TryGetValue(key, out var group))
                        {
                            group = (searchParam, []);
                            groups[key] = group;
                        }

                        group.ResourceTypes.Add(resourceType);
                    }
                }
            }

            var groupList = groups.Values
                .Select(g => (g.Parameter, (IReadOnlyList<string>)g.ResourceTypes))
                .ToList();
            membership[compartmentType] = groupList;

            foreach (var (parameter, resourceTypes) in groupList)
            {
                collector.Parameters.Add(parameter);
                foreach (var resourceType in resourceTypes)
                {
                    collector.ResourceTypes.Add(resourceType);
                }
            }
        }

        return membership;
    }
}
