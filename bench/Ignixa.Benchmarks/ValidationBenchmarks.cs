using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation;

namespace Ignixa.Benchmarks;

/// <summary>
/// Benchmarks for FHIR validation system (Tier 1 - Fast).
/// Target: Validate typical Patient resource in less than 25ms.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
public class ValidationBenchmarks
{
    private ISourceNode _patientSourceNode = null!;
    private ISourceNode _observationSourceNode = null!;
    private FastValidator _validator = null!;

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

        _validator = new FastValidator();
    }

    [Benchmark(Baseline = true, Description = "Validate Patient (Tier 1)")]
    public ValidationResult ValidatePatient()
    {
        return _validator.Validate(_patientSourceNode);
    }

    [Benchmark(Description = "Validate Observation (Tier 1)")]
    public ValidationResult ValidateObservation()
    {
        return _validator.Validate(_observationSourceNode);
    }

    [Benchmark(Description = "Validate Invalid Patient (with errors)")]
    public ValidationResult ValidateInvalidPatient()
    {
        // Missing resourceType (should fail JsonStructureCheck)
        var invalidJson = JsonNode.Parse(@"{
            ""id"": ""example"",
            ""name"": [{""family"": ""Doe""}]
        }")!;
        var sourceNode = JsonNodeSourceNode.Create(invalidJson);
        return _validator.Validate(sourceNode);
    }
}
