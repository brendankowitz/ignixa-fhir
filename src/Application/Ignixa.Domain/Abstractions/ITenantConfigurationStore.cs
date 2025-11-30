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
}
