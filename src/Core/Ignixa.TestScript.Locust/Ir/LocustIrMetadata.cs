namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// Descriptive metadata about the TestScript definition a <see cref="LocustIrDocument"/> was compiled from.
/// </summary>
/// <param name="Name">The human-readable TestScript name.</param>
/// <param name="Source">The relative path to the originating TestScript definition.</param>
/// <param name="FhirVersion">The FHIR version the TestScript targets, if known.</param>
public sealed record LocustIrMetadata(string Name, string Source, string? FhirVersion);
