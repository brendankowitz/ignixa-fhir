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
/// Benchmarks for FHIR validation system (tier-aware).
/// Target: Fast tier less than 25ms, Spec tier less than 200ms.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
public class ValidationBenchmarks
{
    private ISourceNavigator _patientSourceNode = null!;
    private ISourceNavigator _observationSourceNode = null!;
    private ValidationSchema _patientSchema = null!;
    private ValidationSchema _observationSchema = null!;
    private ValidationSettings _fastSettings = null!;
    private ValidationSettings _specSettings = null!;
    private ValidationState _state = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Patient with typical complexity (name, identifier, telecom, address)
        var patientJson = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""identifier"": [{
                ""system"": ""http://hospital.example.org"",
                ""value"": ""12345""
            }],
            ""name"": [{
                ""family"": ""Doe"",
                ""given"": [""John"", ""Q""]
            }],
            ""telecom"": [{
                ""system"": ""phone"",
                ""value"": ""555-1234"",
                ""use"": ""home""
            }],
            ""gender"": ""male"",
            ""birthDate"": ""1990-01-15"",
            ""address"": [{
                ""line"": [""123 Main St""],
                ""city"": ""Springfield"",
                ""state"": ""IL"",
                ""postalCode"": ""62701"",
                ""country"": ""USA""
            }]
        }")!;
        _patientSourceNode = JsonNodeSourceNode.Create(patientJson);

        // Observation with typical complexity
        var observationJson = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example-bp"",
            ""status"": ""final"",
            ""category"": [{
                ""coding"": [{
                    ""system"": ""http://terminology.hl7.org/CodeSystem/observation-category"",
                    ""code"": ""vital-signs"",
                    ""display"": ""Vital Signs""
                }]
            }],
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""85354-9"",
                    ""display"": ""Blood pressure panel""
                }]
            },
            ""subject"": {
                ""reference"": ""Patient/example""
            },
            ""effectiveDateTime"": ""2024-10-20T10:30:00Z"",
            ""component"": [{
                ""code"": {
                    ""coding"": [{
                        ""system"": ""http://loinc.org"",
                        ""code"": ""8480-6"",
                        ""display"": ""Systolic blood pressure""
                    }]
                },
                ""valueQuantity"": {
                    ""value"": 120,
                    ""unit"": ""mmHg"",
                    ""system"": ""http://unitsofmeasure.org"",
                    ""code"": ""mm[Hg]""
                }
            }]
        }")!;
        _observationSourceNode = JsonNodeSourceNode.Create(observationJson);

        // Setup schema resolver with caching
        var schemaProvider = new R4CoreSchemaProvider();
        var innerResolver = new StructureDefinitionSchemaResolver(schemaProvider);
        var schemaResolver = new CachedValidationSchemaResolver(innerResolver);

        _patientSchema = schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")!;
        _observationSchema = schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Observation")!;

        _fastSettings = new ValidationSettings { Depth = ValidationDepth.Minimal };
        _specSettings = new ValidationSettings { Depth = ValidationDepth.Spec };
        _state = new ValidationState();
    }

    [Benchmark(Baseline = true, Description = "Validate Patient (Fast tier)")]
    public ValidationResult ValidatePatientFast()
    {
        return _patientSchema.Validate((IElement)_patientSourceNode, _fastSettings, _state);
    }

    [Benchmark(Description = "Validate Patient (Spec tier)")]
    public ValidationResult ValidatePatientSpec()
    {
        return _patientSchema.Validate((IElement)_patientSourceNode, _specSettings, _state);
    }

    [Benchmark(Description = "Validate Observation (Fast tier)")]
    public ValidationResult ValidateObservationFast()
    {
        return _observationSchema.Validate((IElement)_observationSourceNode, _fastSettings, _state);
    }

    [Benchmark(Description = "Validate Observation (Spec tier)")]
    public ValidationResult ValidateObservationSpec()
    {
        return _observationSchema.Validate((IElement)_observationSourceNode, _specSettings, _state);
    }
}
