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
    /// Resolves a search parameter's SearchParamId. Returns null if the parameter has no catalog
    /// row (e.g. an override URL that hasn't been indexed) -- callers decide whether that's an
    /// error or an empty-result case, this method does not throw for "not found."
    /// </summary>
    Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a FHIR resource type name (e.g. "Patient") to its ResourceTypeId.
    /// </summary>
    Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up an existing token-system surrogate ID. Returns null when the system URI is not
    /// present in the lookup table; callers lower a null result to a false predicate (empty match)
    /// rather than throwing.
    /// </summary>
    Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up several token/quantity systems at once, so a query naming many systems costs one round
    /// trip rather than one per system. Every requested system appears in the result, mapped to null
    /// when it has no row -- the same "not found is data, not an error" contract as
    /// <see cref="GetSystemIdAsync"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation resolves sequentially through <see cref="GetSystemIdAsync"/>, so an
    /// implementation with no batching story stays correct without writing anything; implementations
    /// backed by a real store should override it with a single set-based query.
    /// </remarks>
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
    /// Looks up an existing quantity-code surrogate ID. Returns null when the code is not present
    /// in the lookup table; callers lower a null result to a false predicate (empty match) rather
    /// than throwing.
    /// </summary>
    Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
}
