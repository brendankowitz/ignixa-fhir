using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's only I/O seam. Resolves search-parameter and resource-type identity to the integer
/// surrogate keys the search-index schema stores. Implemented by the data layer; this project has no EF
/// or ASP.NET reference and does no I/O of its own.
/// </summary>
public interface ISymbolResolver
{
    /// <summary>
    /// Resolves a search parameter's SearchParamId, or null when it has no catalog row (e.g. an unindexed
    /// override URL). Does not throw for "not found"; callers decide error vs empty-result.
    /// </summary>
    Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken);

    /// <summary>Resolves a FHIR resource type name (e.g. "Patient") to its ResourceTypeId.</summary>
    Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a token-system surrogate ID, or null when absent; callers lower null to a false predicate
    /// (empty match) rather than throwing.
    /// </summary>
    Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken);

    /// <summary>
    /// Batches <see cref="GetSystemIdAsync"/> so a query naming many systems costs one round trip. Every
    /// requested system appears, mapped to null when it has no row. The default implementation resolves
    /// sequentially; a store-backed implementation should override with a single set-based query.
    /// </summary>
    async Task<IReadOnlyDictionary<string, int?>> GetSystemIdsAsync(IReadOnlyCollection<string> systems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systems);

        var results = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var system in systems)
        {
            if (!results.ContainsKey(system))
            {
                results[system] = await GetSystemIdAsync(system, cancellationToken);
            }
        }

        return results;
    }

    /// <summary>
    /// Looks up a quantity-code surrogate ID, or null when absent; callers lower null to a false predicate
    /// (empty match) rather than throwing.
    /// </summary>
    Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
}
