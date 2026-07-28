using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's Resolve stage: walks a typed predicate tree once (plus the includes, sort, and
/// compartments), collects every search parameter and resource type it references, and resolves them all
/// through <see cref="ISymbolResolver"/>. This is the compiler's only I/O, done up front, producing an
/// immutable <see cref="SymbolTable"/> that Lower and Emit consume synchronously, plus the list of
/// parameters the resolver could not find (see <see cref="ResolvedSymbols"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISymbolResolver.GetSearchParamIdAsync"/> takes the whole <see cref="SearchParameterInfo"/>,
/// not a bare URL, so the resolver can apply its own override-URL fallback. The resolved id is stored
/// under the requesting parameter's own <c>Url</c> — the key Lower looks it up by later — so how the id
/// was found stays invisible once resolved.
/// </para>
/// <para>
/// <c>targetResourceType</c> is the query's own resource type (e.g. "Patient" for a
/// Patient?... search); every ordinary leaf and composite predicate needs it to scope its ParamSource,
/// because a SearchParamId is assigned per parameter-definition URL, not per resource type. It is
/// nullable for the wildcard-compartment and system-level cases, where only the types collected from the
/// tree, includes, sort, compartments, and <c>additionalResourceTypes</c> are resolved. Compartment entries are expanded into
/// <see cref="SymbolTable.CompartmentMembership"/> via the two optional definition managers, which are
/// required only when a compartment search is actually present.
/// </para>
/// </remarks>
public static class Resolve
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

        // Access constraints are lowered as ordinary expressions by AccessConstraintApplier, so their
        // symbols have to be resolved here like any other. Omitting them made every constraint whose
        // predicate named a parameter the user's own query did not also name throw KeyNotFoundException
        // out of Lower -- an authorization control that worked only by coincidence.
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

            // A null Url is unresolvable, not a crash: SymbolTable is keyed by Url, so such a parameter
            // could never be looked up even with an id in hand -- SymbolTable.SearchParamId says exactly
            // that on the lookup side. Report it the same way as a resolver miss.
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

        // A system-level caller that already resolved _type before compiling passes its type list here
        // rather than in the expression, so nothing in the tree collects those names. Without this they
        // resolve to the unmatchable sentinel and the base set narrows to IN (-1, ...) -- a query that
        // compiles, emits, and matches nothing. The names still go through the resolver like any other,
        // so a genuinely unknown type keeps the sentinel it deserves.
        if (additionalResourceTypes is not null)
        {
            resourceTypes.UnionWith(additionalResourceTypes);
        }

        // A resource type the resolver cannot find is recorded as unmatchable rather than dropped: dropping
        // it turns into a KeyNotFoundException the moment Lower looks it up, so the very first search
        // against an empty catalog throws instead of returning an empty bundle. An unknown system already
        // lowers to a false predicate; an unknown resource type is the same structurally-unsatisfiable
        // query and now behaves the same way.
        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            resourceTypeIds[resourceType] = id ?? SymbolTable.UnmatchableResourceTypeId;
        }

        var allSystems = new HashSet<string>(collector.TokenSystems, StringComparer.Ordinal);
        allSystems.UnionWith(collector.QuantitySystems);
        // Re-keyed off the requested set rather than trusted verbatim: SymbolTable's three-state contract
        // needs an entry for every collected system, and a resolver overriding the batch method could
        // return fewer.
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
    /// Resolves each <c>_not-referenced=Type:path</c> pair to its reference search parameter and adds that
    /// parameter to the collector so it gets a SearchParamId like any other. A pair whose path the resolver
    /// cannot find, that carries no Url, or that is not a reference-type parameter is dropped rather than
    /// recorded: Lower then falls back to a path-agnostic (source-type-only) anti-join, which is the shipping
    /// engine's lenient behaviour for an unresolvable path. A missing definition manager is a different case
    /// -- a required dependency was not supplied -- and throws rather than silently degrading every path
    /// filter, mirroring <see cref="ResolveCompartmentMembership"/>. Returns null when there are no pairs, so
    /// the common case allocates nothing.
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

                        // Same null-Url reasoning as the leaf loop below: SymbolTable is keyed by Url, so a
                        // compartment member carrying none could never be looked up even once resolved. Skip
                        // it -- CompileAsync awaits this outside its try/catch, so a dereference here would
                        // escape as an NRE and destroy the whole trace.
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
