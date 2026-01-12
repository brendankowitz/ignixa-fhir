using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;
using ISourceNavigator = Ignixa.Abstractions.ISourceNavigator;
using IElement = Ignixa.Abstractions.IElement;

// Static using for extension methods
using static Ignixa.Serialization.SourceNodes.SchemaAwareElementExtensions;
using SchemaAwareElementExtensions = Ignixa.Serialization.SourceNodes.SchemaAwareElementExtensions;

#pragma warning disable CS0618

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
    private IElement _ignixaTypedElement = null!;
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
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        _versionContext = new FhirVersionContext(NullLoggerFactory.Instance, searchParamOptions);
        _ignixaSchemaProvider = _versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        var sourceNode = _ignixaObservation.ToSourceNavigator();
        _ignixaTypedElement = (IElement)SchemaAwareElementExtensions.ToElement(sourceNode, _ignixaSchemaProvider);

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

    [Benchmark(Description = "Ignixa: Access simple property (IElement)")]
    [BenchmarkCategory("Simple")]
    public string? IgnixaSimpleTypedElement()
    {
        return _ignixaTypedElement.Children("status")?[0].Value?.ToString();
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

    [Benchmark(Description = "Ignixa: Access nested object (IElement)")]
    [BenchmarkCategory("Nested")]
    public string? IgnixaNestedTypedElement()
    {
        return _ignixaTypedElement
            .Children("code")?[0]
            .Children("coding")?[0]
            .Children("code")?[0]
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

    [Benchmark(Description = "Ignixa: Access array element (IElement)")]
    [BenchmarkCategory("Array")]
    public string? IgnixaArrayTypedElement()
    {
        return _ignixaTypedElement
            .Children("component")?[0]
            .Children("valueQuantity")?[0]
            .Children("value")?[0]
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

    // ========== CONVERSION TO ISourceNavigator ==========

    [Benchmark(Description = "Ignixa: Convert to ISourceNavigator")]
    [BenchmarkCategory("Conversion")]
    public ISourceNavigator IgnixaToSourceNode()
    {
        return _ignixaObservation.ToSourceNavigator();
    }

    [Benchmark(Description = "Firely: Already ISourceNode (no-op)")]
    [BenchmarkCategory("Conversion")]
    public SdkISourceNode FirelyToSourceNode()
    {
        return _firelySourceNode;
    }

    // ========== CONVERSION TO TYPED ELEMENT ==========

    [Benchmark(Description = "Ignixa: Convert to IElement")]
    [BenchmarkCategory("Conversion")]
    public IElement IgnixaToTypedElement()
    {
        var sourceNode = _ignixaObservation.ToSourceNavigator();
        return (IElement)SchemaAwareElementExtensions.ToElement(sourceNode, _ignixaSchemaProvider);
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
