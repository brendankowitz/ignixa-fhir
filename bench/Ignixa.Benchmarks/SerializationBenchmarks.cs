using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
public class SerializationBenchmarks
{
    private string _patientSmallJson = null!;
    private string _observationMediumJson = null!;
    private string _bundleLargeJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Load embedded test data
        var assembly = Assembly.GetExecutingAssembly();
        _patientSmallJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.patient-small.json");
        _observationMediumJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.observation-medium.json");
        _bundleLargeJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.bundle-large.json");
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ========== SMALL RESOURCE (Patient ~500 bytes) ==========

    [Benchmark(Description = "Ignixa: Parse small Patient (JsonSerializer)")]
    [BenchmarkCategory("Parse", "Small")]
    public ResourceJsonNode IgnixaParseSmall()
    {
        return JsonSerializer.Deserialize<ResourceJsonNode>(_patientSmallJson)!;
    }

    [Benchmark(Description = "Firely: Parse small Patient (FhirJsonNode)")]
    [BenchmarkCategory("Parse", "Small")]
    public SdkISourceNode FirelyParseSmall()
    {
        return FhirJsonNode.Parse(_patientSmallJson);
    }

    [Benchmark(Description = "Firely: Parse small Patient (POCO)")]
    [BenchmarkCategory("Parse", "Small")]
    public Patient FirelyParsePOCOSmall()
    {
        var deserializer = new FhirJsonDeserializer();
        return deserializer.Deserialize<Patient>(_patientSmallJson);
    }

    // ========== MEDIUM RESOURCE (Observation ~2KB) ==========

    [Benchmark(Description = "Ignixa: Parse medium Observation (JsonSerializer)")]
    [BenchmarkCategory("Parse", "Medium")]
    public ResourceJsonNode IgnixaParseMedium()
    {
        return JsonSerializer.Deserialize<ResourceJsonNode>(_observationMediumJson)!;
    }

    [Benchmark(Description = "Firely: Parse medium Observation (FhirJsonNode)")]
    [BenchmarkCategory("Parse", "Medium")]
    public SdkISourceNode FirelyParseMedium()
    {
        return FhirJsonNode.Parse(_observationMediumJson);
    }

    [Benchmark(Description = "Firely: Parse medium Observation (POCO)")]
    [BenchmarkCategory("Parse", "Medium")]
    public Observation FirelyParsePOCOMedium()
    {
        var deserializer = new FhirJsonDeserializer();
        return deserializer.Deserialize<Observation>(_observationMediumJson);
    }

    // ========== LARGE RESOURCE (Bundle ~100KB, 53 entries) ==========

    [Benchmark(Description = "Ignixa: Parse large Bundle (JsonSerializer)")]
    [BenchmarkCategory("Parse", "Large")]
    public ResourceJsonNode IgnixaParseLarge()
    {
        return JsonSerializer.Deserialize<ResourceJsonNode>(_bundleLargeJson)!;
    }

    [Benchmark(Description = "Firely: Parse large Bundle (FhirJsonNode)")]
    [BenchmarkCategory("Parse", "Large")]
    public SdkISourceNode FirelyParseLarge()
    {
        return FhirJsonNode.Parse(_bundleLargeJson);
    }

    [Benchmark(Description = "Firely: Parse large Bundle (POCO)")]
    [BenchmarkCategory("Parse", "Large")]
    public Bundle FirelyParsePOCOLarge()
    {
        var deserializer = new FhirJsonDeserializer();
        return deserializer.Deserialize<Bundle>(_bundleLargeJson);
    }

    // ========== SERIALIZATION (Write back to JSON) ==========

    [Benchmark(Description = "Ignixa: Serialize small Patient")]
    [BenchmarkCategory("Serialize", "Small")]
    public string IgnixaSerializeSmall()
    {
        var resource = JsonSerializer.Deserialize<ResourceJsonNode>(_patientSmallJson)!;
        return resource.SerializeToString();
    }

    [Benchmark(Description = "Firely: Serialize small Patient (POCO)")]
    [BenchmarkCategory("Serialize", "Small")]
    public string FirelySerializeSmall()
    {
        var deserializer = new FhirJsonDeserializer();
        var resource = deserializer.Deserialize<Patient>(_patientSmallJson);
        var serializer = new FhirJsonSerializer();
        return serializer.SerializeToString(resource);
    }

    [Benchmark(Description = "Ignixa: Serialize large Bundle")]
    [BenchmarkCategory("Serialize", "Large")]
    public string IgnixaSerializeLarge()
    {
        var resource = JsonSerializer.Deserialize<ResourceJsonNode>(_bundleLargeJson)!;
        return resource.SerializeToString();
    }

    [Benchmark(Description = "Firely: Serialize large Bundle (POCO)")]
    [BenchmarkCategory("Serialize", "Large")]
    public string FirelySerializeLarge()
    {
        var deserializer = new FhirJsonDeserializer();
        var resource = deserializer.Deserialize<Bundle>(_bundleLargeJson);
        var serializer = new FhirJsonSerializer();
        return serializer.SerializeToString(resource);
    }
}
