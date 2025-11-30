namespace Ignixa.Application.Events.Package;

/// <summary>
/// Concrete implementation of IPackageUnloaded event.
/// Published when a FHIR package is successfully unloaded.
/// </summary>
public record PackageUnloadedEvent(
    string PackageId,
    string PackageVersion,
    int TenantId,
    DateTimeOffset UnloadedAt) : IPackageUnloaded;
