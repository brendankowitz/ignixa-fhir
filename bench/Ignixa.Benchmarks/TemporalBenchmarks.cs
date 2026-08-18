using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

#pragma warning disable CS0618

namespace Ignixa.Benchmarks;

/// <summary>
/// Benchmarks for element navigation and FHIRPath evaluation over temporal-heavy resources.
/// Covers the paths that changed when IElement.Value began returning a parsed temporal value for
/// date/dateTime/instant/time primitives: per-navigation value materialisation, temporal comparison,
/// and a whole-tree walk that touches every primitive value once.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net10_0)]
[RankColumn]
[MarkdownExporter]
public class TemporalBenchmarks
{
    private IElement _patientElement = null!;
    private IElement _observationElement = null!;

    private const string BirthDateExpression = "Patient.birthDate";
    private const string DeepTemporalExpression = "Patient.contact.period.start";
    private const string TemporalComparisonExpression = "Patient.birthDate > @1970-01-01";
    private const string TemporalRangeExpression =
        "Observation.effectivePeriod.start <= @2024-10-20T12:00:00Z and Observation.effectivePeriod.end >= @2024-10-20T09:00:00Z";
    private const string NonTemporalControlExpression = "Patient.name.family";

    [GlobalSetup]
    public void Setup()
    {
        // Patient carrying temporal values at several depths: a top-level date, a deceased dateTime,
        // meta.lastUpdated (instant), and repeated contact periods.
        var patientJson = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""temporal-example"",
            ""meta"": {
                ""versionId"": ""3"",
                ""lastUpdated"": ""2024-10-20T10:30:00.123+00:00""
            },
            ""identifier"": [{
                ""system"": ""http://hospital.example.org"",
                ""value"": ""12345"",
                ""period"": { ""start"": ""2010-01-01"", ""end"": ""2030-01-01"" }
            }],
            ""name"": [{
                ""family"": ""Doe"",
                ""given"": [""John"", ""Q""],
                ""period"": { ""start"": ""1990-01-15"" }
            }],
            ""telecom"": [{
                ""system"": ""phone"",
                ""value"": ""555-1234"",
                ""period"": { ""start"": ""2015-06-01"", ""end"": ""2020-06-01"" }
            }],
            ""gender"": ""male"",
            ""birthDate"": ""1990-01-15"",
            ""deceasedDateTime"": ""2024-03-04T08:15:00-05:00"",
            ""address"": [{
                ""line"": [""123 Main St""],
                ""city"": ""Springfield"",
                ""period"": { ""start"": ""2001-02-03"", ""end"": ""2011-02-03"" }
            }],
            ""contact"": [
                {
                    ""name"": { ""family"": ""Doe"", ""given"": [""Jane""] },
                    ""period"": { ""start"": ""2000-01-01"", ""end"": ""2010-01-01"" }
                },
                {
                    ""name"": { ""family"": ""Roe"", ""given"": [""Richard""] },
                    ""period"": { ""start"": ""2010-01-02"", ""end"": ""2020-01-02"" }
                }
            ]
        }")!;

        var observationJson = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""temporal-bp"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""85354-9""
                }]
            },
            ""subject"": { ""reference"": ""Patient/temporal-example"" },
            ""effectivePeriod"": {
                ""start"": ""2024-10-20T10:00:00Z"",
                ""end"": ""2024-10-20T11:00:00Z""
            },
            ""issued"": ""2024-10-20T11:05:00.000Z"",
            ""component"": [{
                ""code"": { ""coding"": [{ ""system"": ""http://loinc.org"", ""code"": ""8480-6"" }] },
                ""valueQuantity"": { ""value"": 120, ""unit"": ""mmHg"" }
            }]
        }")!;

        var schemaProvider = new R4CoreSchemaProvider();

        _patientElement = JsonNodeSourceNode.Create(patientJson).ToElement(schemaProvider);
        _observationElement = JsonNodeSourceNode.Create(observationJson).ToElement(schemaProvider);

        // Warm the FHIRPath AST / delegate caches so the benchmarks measure evaluation only.
        _ = _patientElement.Select(BirthDateExpression).ToArray();
        _ = _patientElement.Select(DeepTemporalExpression).ToArray();
        _ = _patientElement.Select(TemporalComparisonExpression).ToArray();
        _ = _patientElement.Select(NonTemporalControlExpression).ToArray();
        _ = _observationElement.Select(TemporalRangeExpression).ToArray();
        _ = WalkValues(_patientElement);
    }

    [Benchmark(Baseline = true, Description = "Control: non-temporal navigation (Patient.name.family)")]
    [BenchmarkCategory("Temporal")]
    public IElement[] NonTemporalControl()
    {
        return _patientElement.Select(NonTemporalControlExpression).ToArray();
    }

    [Benchmark(Description = "Temporal: scalar date navigation (Patient.birthDate)")]
    [BenchmarkCategory("Temporal")]
    public object? TemporalScalar()
    {
        return _patientElement.Select(BirthDateExpression).FirstOrDefault()?.Value;
    }

    [Benchmark(Description = "Temporal: nested date navigation (Patient.contact.period.start)")]
    [BenchmarkCategory("Temporal")]
    public IElement[] TemporalNested()
    {
        return _patientElement.Select(DeepTemporalExpression).ToArray();
    }

    [Benchmark(Description = "Temporal: date comparison against a literal")]
    [BenchmarkCategory("Temporal")]
    public IElement[] TemporalComparison()
    {
        return _patientElement.Select(TemporalComparisonExpression).ToArray();
    }

    [Benchmark(Description = "Temporal: dateTime range comparison (Observation.effectivePeriod)")]
    [BenchmarkCategory("Temporal")]
    public IElement[] TemporalRangeComparison()
    {
        return _observationElement.Select(TemporalRangeExpression).ToArray();
    }

    [Benchmark(Description = "Temporal: whole-resource walk touching every primitive Value")]
    [BenchmarkCategory("Temporal")]
    public int WalkAllValuesPatient()
    {
        return WalkValues(_patientElement);
    }

    [Benchmark(Description = "Temporal: whole-resource walk (Observation)")]
    [BenchmarkCategory("Temporal")]
    public int WalkAllValuesObservation()
    {
        return WalkValues(_observationElement);
    }

    /// <summary>
    /// Walks the element tree depth-first, materialising every child element and reading its Value.
    /// This is the shape of work that validation, search indexing and serialization all perform, and
    /// it is the path where a per-navigation value parse would show up.
    /// </summary>
    private static int WalkValues(IElement element)
    {
        var count = 0;

        if (element.Value is not null)
        {
            count++;
        }

        foreach (var child in element.Children(null))
        {
            count += WalkValues(child);
        }

        return count;
    }
}
