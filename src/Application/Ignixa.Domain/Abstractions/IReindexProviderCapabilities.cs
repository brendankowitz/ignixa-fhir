using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Reports whether a configured storage provider supports server-wide reindexing.
/// </summary>
public interface IReindexProviderCapabilities
{
    /// <summary>
    /// Gets whether the provider configured for <paramref name="tenantConfiguration"/> supports reindexing.
    /// </summary>
    bool SupportsReindex(TenantConfiguration tenantConfiguration);
}
