using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Store for tenant configuration and settings.
/// </summary>
public interface ITenantConfigurationStore
{
    /// <summary>
    /// Gets the system-wide tenant mode (Isolated or Distributed).
    /// </summary>
    TenantMode Mode { get; }

    /// <summary>
    /// Gets configuration for a specific tenant by tenant ID.
    /// Returns null if tenant doesn't exist or is inactive.
    /// </summary>
    ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(
        int tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active tenant configurations.
    /// </summary>
    ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the active tenant registered for <paramref name="host"/> (case-insensitive), or null if no
    /// tenant claims it. Hostnames are unique across tenants; a host claimed by more than one is a
    /// configuration error and throws.
    /// </summary>
    ValueTask<TenantConfiguration?> ResolveByHostAsync(
        string host,
        CancellationToken cancellationToken = default);
}
