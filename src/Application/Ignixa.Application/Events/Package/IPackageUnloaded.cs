using Medino;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Event published when a FHIR package (IG) is unloaded from the system.
/// Used for cache invalidation and multi-instance synchronization.
/// </summary>
public interface IPackageUnloaded : INotification
{
    /// <summary>
    /// The NPM package ID (e.g., "hl7.fhir.us.core")
    /// </summary>
    string PackageId { get; }

    /// <summary>
    /// Package version (e.g., "5.0.1")
    /// </summary>
    string PackageVersion { get; }

    /// <summary>
    /// Tenant ID where package was unloaded (0 = system, >0 = tenant-scoped)
    /// </summary>
    int TenantId { get; }

    /// <summary>
    /// When the package was unloaded
    /// </summary>
    DateTimeOffset UnloadedAt { get; }
}
