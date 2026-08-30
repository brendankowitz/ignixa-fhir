using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Ignixa.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

#pragma warning disable CS0618

namespace Ignixa.Benchmarks;

/// <summary>
/// Benchmarks the scope-seeding path that <see cref="ValidationSchema.Validate"/> takes when the caller
/// omits the state - which is what the resource-write path does. <see cref="ValidationBenchmarks"/>
/// passes a bare <see cref="ValidationState"/> and therefore never seeds a scope, so it cannot see the
/// cost of the reference index at all.
/// </summary>
/// <remarks>
/// Three shapes, because they exercise opposite sides of the laziness trade: Patient/Observation have no
/// <c>contained</c> and no invariant that resolves, so the index is built and never read; the contained
/// Observation and the Bundle are the cases where <c>resolve()</c> genuinely needs it.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net10_0)]
[MarkdownExporter]
public class ValidationScopeSeedingBenchmarks
{
    private IElement _patientElement = null!;
    private IElement _observationElement = null!;
    private IElement _containedObservationElement = null!;
    private IElement _bundleElement = null!;
    private ValidationSchema _patientSchema = null!;
    private ValidationSchema _observationSchema = null!;
    private ValidationSchema _bundleSchema = null!;
    private ValidationSettings _minimalSettings = null!;
    private ValidationSettings _specSettings = null!;
    private ValidationSettings _fullSettings = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string PatientJson = """
        {
            "resourceType": "Patient",
            "id": "example",
            "identifier": [{ "system": "http://hospital.example.org", "value": "12345" }],
            "name": [{ "family": "Doe", "given": ["John", "Q"] }],
            "telecom": [{ "system": "phone", "value": "555-1234", "use": "home" }],
            "gender": "male",
            "birthDate": "1990-01-15",
            "address": [{
                "line": ["123 Main St"],
                "city": "Springfield",
                "state": "IL",
                "postalCode": "62701",
                "country": "USA"
            }]
        }
        """;

        const string ObservationJson = """
        {
            "resourceType": "Observation",
            "id": "example-bp",
            "status": "final",
            "category": [{ "coding": [{
                "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                "code": "vital-signs",
                "display": "Vital Signs"
            }] }],
            "code": { "coding": [{
                "system": "http://loinc.org",
                "code": "85354-9",
                "display": "Blood pressure panel"
            }] },
            "subject": { "reference": "Patient/example" },
            "effectiveDateTime": "2024-10-20T10:30:00Z",
            "component": [{
                "code": { "coding": [{
                    "system": "http://loinc.org",
                    "code": "8480-6",
                    "display": "Systolic blood pressure"
                }] },
                "valueQuantity": {
                    "value": 120,
                    "unit": "mmHg",
                    "system": "http://unitsofmeasure.org",
                    "code": "mm[Hg]"
                }
            }]
        }
        """;

        const string ContainedObservationJson = """
        {
            "resourceType": "Observation",
            "id": "example-contained",
            "status": "final",
            "contained": [{
                "resourceType": "Patient",
                "id": "p1",
                "name": [{ "family": "Doe", "given": ["John"] }],
                "gender": "male"
            }],
            "code": { "coding": [{
                "system": "http://loinc.org",
                "code": "85354-9",
                "display": "Blood pressure panel"
            }] },
            "subject": { "reference": "#p1" },
            "effectiveDateTime": "2024-10-20T10:30:00Z"
        }
        """;

        const string BundleJson = """
        {
            "resourceType": "Bundle",
            "id": "example-bundle",
            "type": "collection",
            "entry": [
                {
                    "fullUrl": "urn:uuid:11111111-1111-1111-1111-111111111111",
                    "resource": {
                        "resourceType": "Patient",
                        "id": "p1",
                        "name": [{ "family": "Doe", "given": ["John"] }],
                        "gender": "male"
                    }
                },
                {
                    "fullUrl": "urn:uuid:22222222-2222-2222-2222-222222222222",
                    "resource": {
                        "resourceType": "Observation",
                        "id": "o1",
                        "status": "final",
                        "code": { "coding": [{ "system": "http://loinc.org", "code": "85354-9" }] },
                        "subject": { "reference": "Patient/p1" }
                    }
                }
            ]
        }
        """;

        var schemaProvider = new R4CoreSchemaProvider();

        _patientElement = ToElement(PatientJson, schemaProvider);
        _observationElement = ToElement(ObservationJson, schemaProvider);
        _containedObservationElement = ToElement(ContainedObservationJson, schemaProvider);
        _bundleElement = ToElement(BundleJson, schemaProvider);

        var schemaResolver = new CachedValidationSchemaResolver(
            new StructureDefinitionSchemaResolver(schemaProvider));

        _patientSchema = schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")!;
        _observationSchema = schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Observation")!;
        _bundleSchema = schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Bundle")!;

        _minimalSettings = new ValidationSettings { Depth = ValidationDepth.Minimal };
        _specSettings = new ValidationSettings { Depth = ValidationDepth.Spec };
        _fullSettings = new ValidationSettings { Depth = ValidationDepth.Full };
    }

    private static IElement ToElement(string json, ISchema schemaProvider)
        => JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(schemaProvider);

    [Benchmark(Description = "Seeded: Patient (Minimal)")]
    public ValidationResult PatientMinimal() => _patientSchema.Validate(_patientElement, _minimalSettings);

    [Benchmark(Description = "Seeded: Patient (Spec)")]
    public ValidationResult PatientSpec() => _patientSchema.Validate(_patientElement, _specSettings);

    [Benchmark(Description = "Seeded: Patient (Full)")]
    public ValidationResult PatientFull() => _patientSchema.Validate(_patientElement, _fullSettings);

    [Benchmark(Description = "Seeded: Observation (Minimal)")]
    public ValidationResult ObservationMinimal() => _observationSchema.Validate(_observationElement, _minimalSettings);

    [Benchmark(Description = "Seeded: Observation (Spec)")]
    public ValidationResult ObservationSpec() => _observationSchema.Validate(_observationElement, _specSettings);

    [Benchmark(Description = "Seeded: Observation (Full)")]
    public ValidationResult ObservationFull() => _observationSchema.Validate(_observationElement, _fullSettings);

    [Benchmark(Description = "Seeded: Observation with contained (Full)")]
    public ValidationResult ContainedObservationFull()
        => _observationSchema.Validate(_containedObservationElement, _fullSettings);

    [Benchmark(Description = "Seeded: Bundle (Full)")]
    public ValidationResult BundleFull() => _bundleSchema.Validate(_bundleElement, _fullSettings);
}
