namespace Ignixa.Domain.Caching;

/// <summary>
/// Multi-tier cache for FHIR conformance resources.
/// Supports in-memory and distributed caching strategies.
/// </summary>
public interface IFhirConformanceCache
{
    /// <summary>
    /// Retrieves a cached conformance resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL of the resource</param>
    /// <param name="version">Resource version (optional for exact match)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached resource JSON, or null if not found</returns>
    ValueTask<string?> GetAsync(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches a conformance resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL of the resource</param>
    /// <param name="resourceJson">Resource JSON content</param>
    /// <param name="ttl">Time to live (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask SetAsync(
        string tenantId,
        string canonical,
        string resourceJson,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches multiple conformance resources in a single operation.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="resources">Dictionary of canonical URL to resource JSON</param>
    /// <param name="ttl">Time to live (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask SetManyAsync(
        string tenantId,
        IReadOnlyDictionary<string, string> resources,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a cached resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL of the resource</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached resources for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
