using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS0618

namespace Ignixa.Benchmarks;

/// <summary>
/// Benchmarks for POST/PUT operations to identify performance hotspots.
/// Focuses on the key operations in the request pipeline:
/// 1. JSON Parsing (JsonSourceNodeFactory)
/// 2. Validation (ValidationBehavior) - simulated
/// 3. Search Index Extraction (ISearchIndexExtractor)
/// 4. Repository persistence - simulated
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet handles cleanup via GlobalCleanup")]
public class PostPutBenchmarks
{
    private string _patientJson = null!;
    private byte[] _patientJsonBytes = null!;
    private ResourceJsonNode _patientNode = null!;
    private IFhirSchemaProvider _schemaProvider = null!;
    private FhirVersionContext _versionContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Load test data (matches test data used in production)
        _patientJson = @"{
  ""resourceType"": ""Patient"",
  ""id"": ""perf-test-123"",
  ""meta"": {
    ""versionId"": ""1"",
    ""lastUpdated"": ""2025-01-15T10:30:00Z""
  },
  ""identifier"": [
    {
      ""system"": ""http://hospital.example.org/patients"",
      ""value"": ""987654""
    }
  ],
  ""active"": true,
  ""name"": [
    {
      ""use"": ""official"",
      ""family"": ""Performance"",
      ""given"": [""Test"", ""Patient""]
    }
  ],
  ""telecom"": [
    {
      ""system"": ""phone"",
      ""value"": ""555-1234"",
      ""use"": ""home""
    }
  ],
  ""gender"": ""male"",
  ""birthDate"": ""1990-03-15"",
  ""address"": [
    {
      ""line"": [""123 Benchmark St""],
      ""city"": ""Performance City"",
      ""state"": ""PC"",
      ""postalCode"": ""12345"",
      ""country"": ""USA""
    }
  ]
}";
        _patientJsonBytes = Encoding.UTF8.GetBytes(_patientJson);

        // Setup version context and schema provider
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        _versionContext = new FhirVersionContext(NullLoggerFactory.Instance, searchParamOptions);
        _schemaProvider = _versionContext.GetBaseSchemaProvider(FhirSpecification.R4);

        // Pre-parse for some benchmarks
        _patientNode = JsonSerializer.Deserialize<ResourceJsonNode>(_patientJson)!;
    }

    // ========== STEP 1: JSON PARSING ==========

    [Benchmark(Baseline = true, Description = "1. Parse JSON to ResourceJsonNode")]
    [BenchmarkCategory("Parsing")]
    public async Task<ResourceJsonNode> ParseJsonToNode()
    {
        using var stream = new MemoryStream(_patientJsonBytes);
        return await JsonSourceNodeFactory.Parse(stream);
    }

    [Benchmark(Description = "1a. Parse JSON (JsonSerializer only)")]
    [BenchmarkCategory("Parsing")]
    public ResourceJsonNode ParseJsonSerializerOnly()
    {
        return JsonSerializer.Deserialize<ResourceJsonNode>(_patientJson)!;
    }

    // ========== STEP 2: CONVERT TO TYPED ELEMENT ==========

    [Benchmark(Description = "2. Convert to ITypedElement (schema navigation)")]
    [BenchmarkCategory("Navigation")]
    public Ignixa.Abstractions.IElement ConvertToTypedElement()
    {
        var sourceNode = _patientNode.ToSourceNavigator();
        return (Ignixa.Abstractions.IElement)sourceNode.ToElement(_schemaProvider);
    }

    // ========== STEP 3: SEARCH INDEX EXTRACTION ==========

    [Benchmark(Description = "3. Extract search indices (FHIRPath evaluation)")]
    [BenchmarkCategory("Indexing")]
    public async Task<System.Collections.Generic.IReadOnlyCollection<Ignixa.Search.Indexing.SearchIndexEntry>> ExtractSearchIndices()
    {
        // Use JsonSourceNodeFactory.Parse() like production code does
        // (JsonSerializer.Deserialize doesn't set up source node structure correctly)
        ResourceJsonNode node;
        using (var stream = new MemoryStream(_patientJsonBytes))
        {
            node = await JsonSourceNodeFactory.Parse(stream);
        }

        var searchIndexer = _versionContext.GetSearchIndexer(FhirSpecification.R4, tenantId: null);
        var typedElement = node.ToElement(_schemaProvider);
        return searchIndexer.Extract((Ignixa.Abstractions.IElement)typedElement);
    }

    // ========== COMBINED: FULL POST/PUT PIPELINE (WITHOUT REPOSITORY) ==========

    [Benchmark(Description = "4. Full POST pipeline (parse → index)")]
    [BenchmarkCategory("EndToEnd")]
    public async Task<FullPipelineResult> FullPostPipeline()
    {
        // Step 1: Parse JSON
        ResourceJsonNode node;
        using (var stream = new MemoryStream(_patientJsonBytes))
        {
            node = await JsonSourceNodeFactory.Parse(stream);
        }

        // Step 2: Convert to ITypedElement
        var typedElement = node.ToElement(_schemaProvider);

        // Step 3: Extract search indices
        var searchIndexer = _versionContext.GetSearchIndexer(FhirSpecification.R4, tenantId: null);
        var searchIndices = searchIndexer.Extract((Ignixa.Abstractions.IElement)typedElement);

        // Step 4: Set meta (simulating handler logic)
        node.Meta.LastUpdated = DateTimeOffset.UtcNow;
        node.Meta.VersionId = "1";

        return new FullPipelineResult
        {
            ResourceNode = node,
            SearchIndexCount = searchIndices.Count
        };
    }

    // ========== ANALYSIS: MEMORY ALLOCATIONS ==========

    [Benchmark(Description = "5. Memory: Parse + allocate ResourceWrapper")]
    [BenchmarkCategory("Memory")]
    public async Task<object> MemoryAllocationTest()
    {
        // Simulate ResourceWrapper creation (from CreateOrUpdateResourceHandler line 230-242)
        ResourceJsonNode node;
        using (var stream = new MemoryStream(_patientJsonBytes))
        {
            node = await JsonSourceNodeFactory.Parse(stream);
        }

        var typedElement = node.ToElement(_schemaProvider);
        var searchIndexer = _versionContext.GetSearchIndexer(FhirSpecification.R4, tenantId: null);
        var searchIndices = searchIndexer.Extract((Ignixa.Abstractions.IElement)typedElement);

        // Simulate wrapper creation
        var wrapper = new
        {
            ResourceType = "Patient",
            Id = "perf-test-123",
            VersionId = "1",
            LastUpdated = DateTimeOffset.UtcNow,
            ResourceNode = node,
            SearchIndices = searchIndices.ToArray()
        };

        return wrapper;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _versionContext?.Dispose();
    }

    public class FullPipelineResult
    {
        public ResourceJsonNode ResourceNode { get; set; } = null!;
        public int SearchIndexCount { get; set; }
    }
}
