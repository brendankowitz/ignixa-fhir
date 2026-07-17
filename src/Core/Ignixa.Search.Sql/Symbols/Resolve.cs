using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;

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
/// Resolve still does not resolve resource types touched only by compartment context that does not
/// exist anywhere on this <see cref="Expression"/> tree -- that generalization is Phase 8's job.
/// </para>
/// </remarks>
public static class Resolve
{
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(targetResourceType);

        var collector = new SymbolCollectingVisitor();
        expression.AcceptVisitor(collector, context: null);

        var searchParamIds = new Dictionary<string, short>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }

            // A null result (unresolvable parameter) is not an error here -- Lower/Emit will throw
            // if something downstream actually needs it. Resolve's job is to look up what it can,
            // not to validate the tree is fully resolvable.
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

            // Same non-error stance as the search-param loop above -- an unresolvable resource
            // type is simply absent from the table until something downstream needs it.
        }

        return new SymbolTable(searchParamIds, resourceTypeIds);
    }
}
