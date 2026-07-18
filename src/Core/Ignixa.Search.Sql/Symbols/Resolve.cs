using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's Resolve stage: walks a typed predicate tree once, collects every search
/// parameter it references, and resolves them all via <see cref="ISymbolResolver"/> -- the
/// compiler's only I/O, done up front, producing an immutable <see cref="SymbolTable"/> that
/// Lower/Emit consume synchronously. See
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md, "Resolve".
/// </summary>
/// <remarks>
/// <para>
/// <b>OverridesUrl</b>: <see cref="ISymbolResolver.GetSearchParamIdAsync"/> takes the full
/// <see cref="Ignixa.Search.Models.SearchParameterInfo"/>, not a bare URL, specifically so an
/// implementation can apply the same override-URL fallback the existing data layer already does --
/// <c>SearchIndexReferenceDataCache.GetSearchParamIdAsync(SearchParameterInfo)</c>
/// (src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs)
/// tries the parameter's own <c>Url</c> first, then falls back to <c>OverridesUrl</c> if the
/// primary lookup misses. Resolve does not duplicate that fallback logic; it passes the parameter
/// through unchanged and lets the resolver implementation decide. The resulting id is stored under
/// the *requesting* parameter's own <c>Url</c> (not the override target's), because that is the key
/// Lower will look it up by later (<see cref="SymbolTable.SearchParamId"/> takes the predicate's own
/// <see cref="Ignixa.Search.Models.SearchParameterInfo"/>) -- the override is an implementation
/// detail of *how* the id was found, invisible once resolved.
/// </para>
/// <para>
/// <b>Resource-type resolution</b> is out of scope for this stage beyond the caller-supplied
/// <c>targetResourceType</c>, which is mandatory and always resolved: it represents the query's own
/// target resource type (e.g., "Patient" for a Patient?... search), and every ordinary leaf/composite
/// predicate needs it to constrain <see cref="Ignixa.Search.Sql.Ast.CteDefinition.ParamSource"/>'s
/// <c>ResourceTypeId</c> (a <c>SearchParamId</c> is assigned per search-parameter-definition URL, not
/// per resource type, so a shared definition could otherwise match rows of the wrong resource type).
/// Since the query's own target type does not appear anywhere on the <see cref="Expression"/> tree
/// itself, callers must always supply it explicitly. Beyond that mandatory case, resolution extends
/// to whatever <see cref="SymbolCollectingVisitor"/> collects into <c>ResourceTypes</c> -- see that
/// type's remarks for the full list, which now includes <see cref="ReferenceSearchValue"/> leaves, a
/// <c>_type</c> predicate's own value, and (as of Phase 6) a <see cref="ChainedExpression"/>'s
/// <c>ReferenceSearchParameter</c> and both its <c>ResourceTypes</c>/<c>TargetResourceTypes</c> arrays.
/// As of Phase 7, resolution also extends to every <see cref="IncludeExpression"/> passed via the
/// includes/revIncludes parameters -- see SymbolCollectingVisitor.CollectInclude's remarks for the
/// exact fields collected.
/// As of Phase 8, Resolve also expands every SymbolCollectingVisitor.Compartments entry via
/// ICompartmentDefinitionManager/ISearchParameterDefinitionManager (both optional, required only when
/// a compartment search is actually present) into SymbolTable.CompartmentMembership -- see that
/// method's remarks for the exact shape.
/// </para>
/// </remarks>
public static class Resolve
{
    public static async Task<SymbolTable> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
    {
        ArgumentNullException.ThrowIfNull(includes);
        ArgumentNullException.ThrowIfNull(revIncludes);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(targetResourceType);

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

        var compartmentMembership = ResolveCompartmentMembership(collector, compartmentDefinitionManager, searchParameterDefinitionManager);

        var searchParamIds = new Dictionary<string, short>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }
        }

        var resourceTypes = new HashSet<string>(collector.ResourceTypes);
        resourceTypes.Add(targetResourceType);

        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            if (id.HasValue)
            {
                resourceTypeIds[resourceType] = id.Value;
            }
        }

        return new SymbolTable(searchParamIds, resourceTypeIds, compartmentMembership);
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

                        var key = searchParam.Url.ToString();
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
