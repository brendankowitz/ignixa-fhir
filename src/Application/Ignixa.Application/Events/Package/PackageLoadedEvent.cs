namespace Ignixa.Application.Events.Package;

/// <summary>
/// Concrete implementation of IPackageLoaded event.
/// Published when a FHIR package is successfully loaded.
/// </summary>
public record PackageLoadedEvent(
    string PackageId,
    string PackageVersion,
    int TenantId,
    DateTimeOffset LoadedAt) : IPackageLoaded;
