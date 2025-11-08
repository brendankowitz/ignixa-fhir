using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Search.Infrastructure;
using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;
using ISourceNode = Ignixa.Abstractions.ISourceNode;
using ITypedElement = Ignixa.Abstractions.ITypedElement;

// Static using for extension methods
using static Ignixa.Serialization.SourceNodes.TypedElementExtensions;
using TypedElementExtensions = Ignixa.Serialization.SourceNodes.TypedElementExtensions;

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet handles cleanup via GlobalCleanup")]
public class NavigationBenchmarks
{
    private ResourceJsonNode _ignixaObservation = null!;
    private SdkISourceNode _firelySourceNode = null!;
    private Hl7.Fhir.Model.Observation _firelyPOCO = null!;
    private ITypedElement _ignixaTypedElement = null!;
    private SdkITypedElement _firelyTypedElement = null!;
    private IFhirSchemaProvider _ignixaSchemaProvider = null!;
    private FhirVersionContext _versionContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Load test data
        var assembly = Assembly.GetExecutingAssembly();
        var json = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.observation-medium.json");

        // Ignixa setup
        _ignixaObservation = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        _versionContext = new FhirVersionContext(NullLoggerFactory.Instance);
        _ignixaSchemaProvider = _versionContext.GetSchemaProvider(FhirSpecification.R4);
        var sourceNode = _ignixaObservation.ToSourceNode();
        _ignixaTypedElement = TypedElementExtensions.ToTypedElement(sourceNode, _ignixaSchemaProvider);

        // Firely setup
        _firelySourceNode = Hl7.Fhir.Serialization.FhirJsonNode.Parse(json);
        var deserializer = new FhirJsonDeserializer();
        _firelyPOCO = deserializer.Deserialize<Observation>(json);
        _firelyTypedElement = _firelySourceNode.ToTypedElement(ModelInfo.ModelInspector);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ========== SIMPLE PROPERTY ACCESS ==========

    [Benchmark(Description = "Ignixa: Access simple property (JsonNode direct)")]
    [BenchmarkCategory("Simple")]
    public string? IgnixaSimpleJsonNode()
    {
        return _ignixaObservation.MutableNode["status"]?.GetValue<string>();
    }

    [Benchmark(Description = "Ignixa: Access simple property (ITypedElement)")]
    [BenchmarkCategory("Simple")]
    public string? IgnixaSimpleTypedElement()
    {
        return _ignixaTypedElement.Children("status").FirstOrDefault()?.Value?.ToString();
    }

    [Benchmark(Description = "Firely: Access simple property (POCO)")]
    [BenchmarkCategory("Simple")]
    public string? FirelySimplePOCO()
    {
        return _firelyPOCO.Status?.ToString();
    }

    [Benchmark(Description = "Firely: Access simple property (ITypedElement)")]
    [BenchmarkCategory("Simple")]
    public string? FirelySimpleTypedElement()
    {
        return _firelyTypedElement.Children("status").FirstOrDefault()?.Value?.ToString();
    }

    // ========== NESTED OBJECT ACCESS ==========

    [Benchmark(Description = "Ignixa: Access nested object (JsonNode direct)")]
    [BenchmarkCategory("Nested")]
    public string? IgnixaNestedJsonNode()
    {
        return _ignixaObservation.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
    }

    [Benchmark(Description = "Ignixa: Access nested object (ITypedElement)")]
    [BenchmarkCategory("Nested")]
    public string? IgnixaNestedTypedElement()
    {
        return _ignixaTypedElement
            .Children("code").FirstOrDefault()?
            .Children("coding").FirstOrDefault()?
            .Children("code").FirstOrDefault()?
            .Value?.ToString();
    }

    [Benchmark(Description = "Firely: Access nested object (POCO)")]
    [BenchmarkCategory("Nested")]
    public string? FirelyNestedPOCO()
    {
        return _firelyPOCO.Code?.Coding?.FirstOrDefault()?.Code;
    }

    [Benchmark(Description = "Firely: Access nested object (ITypedElement)")]
    [BenchmarkCategory("Nested")]
    public string? FirelyNestedTypedElement()
    {
        return _firelyTypedElement
            .Children("code").FirstOrDefault()?
            .Children("coding").FirstOrDefault()?
            .Children("code").FirstOrDefault()?
            .Value?.ToString();
    }

    // ========== ARRAY ACCESS ==========

    [Benchmark(Description = "Ignixa: Access array element (JsonNode direct)")]
    [BenchmarkCategory("Array")]
    public decimal? IgnixaArrayJsonNode()
    {
        var components = _ignixaObservation.MutableNode["component"]?.AsArray();
        return components?[0]?["valueQuantity"]?["value"]?.GetValue<decimal>();
    }

    [Benchmark(Description = "Ignixa: Access array element (ITypedElement)")]
    [BenchmarkCategory("Array")]
    public string? IgnixaArrayTypedElement()
    {
        return _ignixaTypedElement
            .Children("component").FirstOrDefault()?
            .Children("valueQuantity").FirstOrDefault()?
            .Children("value").FirstOrDefault()?
            .Value?.ToString();
    }

    [Benchmark(Description = "Firely: Access array element (POCO)")]
    [BenchmarkCategory("Array")]
    public decimal? FirelyArrayPOCO()
    {
        var component = _firelyPOCO.Component?.FirstOrDefault();
        return (component?.Value as Hl7.Fhir.Model.Quantity)?.Value;
    }

    [Benchmark(Description = "Firely: Access array element (ITypedElement)")]
    [BenchmarkCategory("Array")]
    public string? FirelyArrayTypedElement()
    {
        return _firelyTypedElement
            .Children("component").FirstOrDefault()?
            .Children("valueQuantity").FirstOrDefault()?
            .Children("value").FirstOrDefault()?
            .Value?.ToString();
    }

    // ========== CONVERSION TO ISourceNode ==========

    [Benchmark(Description = "Ignixa: Convert to ISourceNode")]
    [BenchmarkCategory("Conversion")]
    public ISourceNode IgnixaToSourceNode()
    {
        return _ignixaObservation.ToSourceNode();
    }

    [Benchmark(Description = "Firely: Already ISourceNode (no-op)")]
    [BenchmarkCategory("Conversion")]
    public SdkISourceNode FirelyToSourceNode()
    {
        return _firelySourceNode;
    }

    // ========== CONVERSION TO ITypedElement ==========

    [Benchmark(Description = "Ignixa: Convert to ITypedElement")]
    [BenchmarkCategory("Conversion")]
    public ITypedElement IgnixaToTypedElement()
    {
        var sourceNode = _ignixaObservation.ToSourceNode();
        return TypedElementExtensions.ToTypedElement(sourceNode, _ignixaSchemaProvider);
    }

    [Benchmark(Description = "Firely: Convert to ITypedElement")]
    [BenchmarkCategory("Conversion")]
    public SdkITypedElement FirelyToTypedElement()
    {
        return _firelySourceNode.ToTypedElement(ModelInfo.ModelInspector);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _versionContext?.Dispose();
    }
}
