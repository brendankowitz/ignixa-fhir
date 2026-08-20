using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS0618

namespace Ignixa.Benchmarks;

/// <summary>
/// Benchmarks for search parameter extraction: the full ElementSearchIndexer.Extract path over the
/// real R4 core search parameter set. This exercises FHIRPath evaluation of every search parameter
/// expression for a resource type plus the element-to-search-value converters, including the date and
/// Timing converters that changed when temporal primitives became typed values.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net10_0)]
[RankColumn]
[MarkdownExporter]
public class SearchIndexingBenchmarks
{
    private ISearchIndexer _indexer = null!;
    private IElement _patientElement = null!;
    private IElement _observationElement = null!;

    [GlobalSetup]
    public void Setup()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var patientJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.patient-small.json");
        var observationJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.observation-medium.json");

        var schemaProvider = new R4CoreSchemaProvider();

        _patientElement = ResourceJsonNode.Parse(patientJson)
            .ToSourceNavigator()
            .ToElement(schemaProvider);

        _observationElement = ResourceJsonNode.Parse(observationJson)
            .ToSourceNavigator()
            .ToElement(schemaProvider);

        var definitionManager = new SearchParameterDefinitionManager(
            schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);

        _indexer = SearchIndexerFactory.CreateInstance(
            schemaProvider,
            NullLoggerFactory.Instance,
            definitionManager,
            NullFhirBaseUriProvider.Instance);

        // Warm the FHIRPath AST / delegate caches so the benchmarks measure extraction, not compilation.
        _ = _indexer.Extract(_patientElement);
        _ = _indexer.Extract(_observationElement);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Benchmark(Baseline = true, Description = "Ignixa: Extract search parameters (Patient)")]
    public IReadOnlyCollection<SearchIndexEntry> ExtractPatient()
    {
        return _indexer.Extract(_patientElement);
    }

    [Benchmark(Description = "Ignixa: Extract search parameters (Observation)")]
    public IReadOnlyCollection<SearchIndexEntry> ExtractObservation()
    {
        return _indexer.Extract(_observationElement);
    }
}
