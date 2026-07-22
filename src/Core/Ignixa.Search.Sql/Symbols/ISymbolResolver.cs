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
    /// Looks up an existing quantity-code surrogate ID. Returns null when the code is not present
    /// in the lookup table; callers lower a null result to a false predicate (empty match) rather
    /// than throwing.
    /// </summary>
    Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
}
