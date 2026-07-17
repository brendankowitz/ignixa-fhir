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
/// <b>Resource-type resolution</b> is out of scope for this stage beyond two narrow exceptions:
/// see <see cref="SymbolCollectingVisitor"/>'s remarks. <see cref="RunAsync"/> resolves
/// <c>ResourceTypeId</c> for any resource type collected via <see cref="ReferenceSearchValue"/>
/// leaves (task 8), and for the caller-supplied <c>targetResourceType</c> (if provided), which
/// represents the query's own target resource type (e.g., "Patient" for a Patient?... search).
/// Since the query's own target type does not appear anywhere on the <see cref="Expression"/> tree
/// itself, callers needing ResourceSource (task 5's :not, task 6's resource-column predicates)
/// must supply it explicitly. Resolve still does not resolve resource types touched only by
/// chain/compartment context (chain target types, etc.) that do not exist on this
/// <see cref="Expression"/> tree -- that generalization is Phase 5/6/8's job.
/// </para>
/// </remarks>
public static class Resolve
{
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        CancellationToken cancellationToken,
        string? targetResourceType = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);

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
        if (targetResourceType is not null)
        {
            resourceTypes.Add(targetResourceType);
        }

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
