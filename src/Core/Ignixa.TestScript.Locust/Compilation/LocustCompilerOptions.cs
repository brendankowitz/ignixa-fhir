using Ignixa.Abstractions;

namespace Ignixa.TestScript.Locust.Compilation;

/// <summary>
/// Options controlling how a <see cref="LocustIrCompiler"/> lowers a TestScript definition into the
/// versioned Locust intermediate representation.
/// </summary>
/// <param name="Source">The relative path to the originating TestScript definition.</param>
/// <param name="FhirVersion">The FHIR version string the TestScript targets, if known.</param>
/// <param name="Schema">
/// The FHIR schema provider used to resolve resource shapes. Consumed by fixture and parameter
/// expansion introduced in a later task; required now so the options contract is stable.
/// </param>
/// <param name="FixtureVariants">
/// The number of fixture resource variants to generate. Consumed by fixture expansion introduced
/// in a later task; required now so the options contract is stable.
/// </param>
public sealed record LocustCompilerOptions(
    string Source,
    string? FhirVersion,
    IFhirSchemaProvider Schema,
    int FixtureVariants);
