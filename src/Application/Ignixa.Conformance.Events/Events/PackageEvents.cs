using Ignixa.Conformance.Events.Models;

namespace Ignixa.Conformance.Events.Events;

public record PackageUploaded(
    string PackageId,
    string Version,
    string FhirVersion,
    IReadOnlyList<ResourceManifest> Resources);

public record PackageActivated(
    string PackageId,
    string Version,
    IReadOnlyList<ActivatedResource> Resources);

public record PackageDeactivated(
    string PackageId,
    string Version,
    string Reason);
