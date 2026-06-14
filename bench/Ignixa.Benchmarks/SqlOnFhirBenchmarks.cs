using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet.Schema;

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
[RankColumn]
[MarkdownExporter]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "BenchmarkDotNet requires concrete return types to prevent dead-code elimination")]
public class SqlOnFhirBenchmarks
{
    private IElement _patientElement = null!;
    private IElement _observationElement = null!;
    private IElement[] _bundlePatients = null!;
    private SqlOnFhirEvaluator _evaluator = null!;
    private ISourceNavigator _patientFlattenView = null!;
    private ISourceNavigator _observationView = null!;
    private ISourceNavigator _patientDemographicsView = null!;
    private List<Dictionary<string, object?>> _preEvaluatedPatientRows = null!;
    private List<Dictionary<string, object?>> _preEvaluatedObservationRows = null!;
    private string _tempDir = null!;

    [GlobalSetup]
    public void Setup()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var schemaProvider = FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();

        var patientJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.patient-small.json");
        var observationJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.observation-medium.json");
        var bundleJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.bundle-large.json");

        var patientNode = ResourceJsonNode.Parse(patientJson);
        _patientElement = (IElement)patientNode.ToElement(schemaProvider);

        var observationNode = ResourceJsonNode.Parse(observationJson);
        _observationElement = (IElement)observationNode.ToElement(schemaProvider);

        var bundleNode = ResourceJsonNode.Parse(bundleJson);
        var bundleElement = (IElement)bundleNode.ToElement(schemaProvider);
        _bundlePatients = Enumerable.Range(0, 10)
            .Select(_ => _patientElement)
            .ToArray();

        _evaluator = new SqlOnFhirEvaluator();

        _patientFlattenView = CreateViewDefinitionNode("""
            {
              "resource": "Patient",
              "select": [
                {
                  "column": [
                    { "name": "id", "path": "id", "type": "id" },
                    { "name": "active", "path": "active", "type": "boolean" },
                    { "name": "gender", "path": "gender", "type": "code" },
                    { "name": "birthDate", "path": "birthDate", "type": "date" }
                  ]
                },
                {
                  "forEach": "name",
                  "column": [
                    { "name": "family", "path": "family", "type": "string" },
                    { "name": "given", "path": "given.first()", "type": "string" }
                  ]
                }
              ]
            }
            """);

        _observationView = CreateViewDefinitionNode("""
            {
              "resource": "Observation",
              "select": [
                {
                  "column": [
                    { "name": "id", "path": "id", "type": "id" },
                    { "name": "status", "path": "status", "type": "code" },
                    { "name": "date", "path": "effectiveDateTime", "type": "dateTime" },
                    { "name": "subject_ref", "path": "subject.reference", "type": "string" }
                  ]
                },
                {
                  "forEach": "component",
                  "column": [
                    { "name": "code", "path": "code.coding.first().code", "type": "string" },
                    { "name": "display", "path": "code.coding.first().display", "type": "string" },
                    { "name": "value", "path": "valueQuantity.value", "type": "decimal" },
                    { "name": "unit", "path": "valueQuantity.unit", "type": "string" }
                  ]
                }
              ]
            }
            """);

        _patientDemographicsView = CreateViewDefinitionNode("""
            {
              "resource": "Patient",
              "where": [{ "path": "active = true" }],
              "select": [
                {
                  "column": [
                    { "name": "id", "path": "id", "type": "id" },
                    { "name": "gender", "path": "gender", "type": "code" },
                    { "name": "birthDate", "path": "birthDate", "type": "date" }
                  ]
                },
                {
                  "forEach": "identifier",
                  "column": [
                    { "name": "identifier_system", "path": "system", "type": "string" },
                    { "name": "identifier_value", "path": "value", "type": "string" }
                  ]
                }
              ]
            }
            """);

        _preEvaluatedPatientRows = _evaluator.Evaluate(_patientFlattenView, _patientElement).ToList();
        _preEvaluatedObservationRows = _evaluator.Evaluate(_observationView, _observationElement).ToList();

        _tempDir = Path.Combine(Path.GetTempPath(), "ignixa-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ========== VIEW DEFINITION PARSING ==========

    [Benchmark(Description = "SqlOnFhir: Parse ViewDefinition (Patient flatten)")]
    [BenchmarkCategory("SqlOnFhir", "Parse")]
    public ISourceNavigator SqlOnFhirParsePatientView()
    {
        return CreateViewDefinitionNode("""
            {
              "resource": "Patient",
              "select": [
                {
                  "column": [
                    { "name": "id", "path": "id", "type": "id" },
                    { "name": "active", "path": "active", "type": "boolean" },
                    { "name": "gender", "path": "gender", "type": "code" },
                    { "name": "birthDate", "path": "birthDate", "type": "date" }
                  ]
                },
                {
                  "forEach": "name",
                  "column": [
                    { "name": "family", "path": "family", "type": "string" },
                    { "name": "given", "path": "given.first()", "type": "string" }
                  ]
                }
              ]
            }
            """);
    }

    // ========== VIEW EVALUATION ==========

    [Benchmark(Description = "SqlOnFhir: Evaluate Patient (simple flatten)")]
    [BenchmarkCategory("SqlOnFhir", "Evaluate")]
    public List<Dictionary<string, object?>> SqlOnFhirEvaluatePatient()
    {
        return _evaluator.Evaluate(_patientFlattenView, _patientElement).ToList();
    }

    [Benchmark(Description = "SqlOnFhir: Evaluate Observation (forEach components)")]
    [BenchmarkCategory("SqlOnFhir", "Evaluate")]
    public List<Dictionary<string, object?>> SqlOnFhirEvaluateObservation()
    {
        return _evaluator.Evaluate(_observationView, _observationElement).ToList();
    }

    [Benchmark(Description = "SqlOnFhir: Evaluate Patient (with WHERE filter)")]
    [BenchmarkCategory("SqlOnFhir", "Evaluate")]
    public List<Dictionary<string, object?>> SqlOnFhirEvaluatePatientWithWhere()
    {
        return _evaluator.Evaluate(_patientDemographicsView, _patientElement).ToList();
    }

    [Benchmark(Description = "SqlOnFhir: Evaluate batch (10 patients)")]
    [BenchmarkCategory("SqlOnFhir", "Evaluate")]
    public List<Dictionary<string, object?>> SqlOnFhirEvaluateBatch()
    {
        var allRows = new List<Dictionary<string, object?>>();
        foreach (var patient in _bundlePatients)
        {
            allRows.AddRange(_evaluator.Evaluate(_patientFlattenView, patient));
        }
        return allRows;
    }

    // ========== EXPORT: CSV ==========

    [Benchmark(Description = "SqlOnFhir: Export Patient rows to CSV")]
    [BenchmarkCategory("SqlOnFhir", "Export-CSV")]
    public async Task<long> SqlOnFhirExportCsv()
    {
        var path = Path.Combine(_tempDir, $"patient-{Guid.NewGuid():N}.csv");
        await using var writer = new CsvFileWriter(path, NullLogger.Instance);
        foreach (var row in _preEvaluatedPatientRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    [Benchmark(Description = "SqlOnFhir: Export Observation rows to CSV")]
    [BenchmarkCategory("SqlOnFhir", "Export-CSV")]
    public async Task<long> SqlOnFhirExportObservationCsv()
    {
        var path = Path.Combine(_tempDir, $"observation-{Guid.NewGuid():N}.csv");
        await using var writer = new CsvFileWriter(path, NullLogger.Instance);
        foreach (var row in _preEvaluatedObservationRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    // ========== EXPORT: NDJSON ==========

    [Benchmark(Description = "SqlOnFhir: Export Patient rows to NDJSON")]
    [BenchmarkCategory("SqlOnFhir", "Export-NDJSON")]
    public async Task<long> SqlOnFhirExportNdjson()
    {
        var path = Path.Combine(_tempDir, $"patient-{Guid.NewGuid():N}.ndjson");
        await using var writer = new NdjsonFileWriter(path, NullLogger.Instance);
        foreach (var row in _preEvaluatedPatientRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    [Benchmark(Description = "SqlOnFhir: Export Observation rows to NDJSON")]
    [BenchmarkCategory("SqlOnFhir", "Export-NDJSON")]
    public async Task<long> SqlOnFhirExportObservationNdjson()
    {
        var path = Path.Combine(_tempDir, $"observation-{Guid.NewGuid():N}.ndjson");
        await using var writer = new NdjsonFileWriter(path, NullLogger.Instance);
        foreach (var row in _preEvaluatedObservationRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    // ========== EXPORT: PARQUET ==========

    [Benchmark(Description = "SqlOnFhir: Export Patient rows to Parquet")]
    [BenchmarkCategory("SqlOnFhir", "Export-Parquet")]
    public async Task<long> SqlOnFhirExportParquet()
    {
        var path = Path.Combine(_tempDir, $"patient-{Guid.NewGuid():N}.parquet");
        var schema = new ParquetSchema(
            new Parquet.Schema.DataField<string>("id"),
            new Parquet.Schema.DataField<bool?>("active"),
            new Parquet.Schema.DataField<string>("gender"),
            new Parquet.Schema.DataField<string>("birthDate"),
            new Parquet.Schema.DataField<string>("family"),
            new Parquet.Schema.DataField<string>("given"));

        var columnTypeMap = new Dictionary<string, string>
        {
            ["id"] = "STRING",
            ["active"] = "BOOLEAN",
            ["gender"] = "STRING",
            ["birthDate"] = "STRING",
            ["family"] = "STRING",
            ["given"] = "STRING"
        };

        await using var writer = new ParquetFileWriter(path, schema, NullLogger.Instance, columnTypeMap);
        foreach (var row in _preEvaluatedPatientRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    [Benchmark(Description = "SqlOnFhir: Export Observation rows to Parquet")]
    [BenchmarkCategory("SqlOnFhir", "Export-Parquet")]
    public async Task<long> SqlOnFhirExportObservationParquet()
    {
        var path = Path.Combine(_tempDir, $"observation-{Guid.NewGuid():N}.parquet");
        var schema = new ParquetSchema(
            new Parquet.Schema.DataField<string>("id"),
            new Parquet.Schema.DataField<string>("status"),
            new Parquet.Schema.DataField<string>("date"),
            new Parquet.Schema.DataField<string>("subject_ref"),
            new Parquet.Schema.DataField<string>("code"),
            new Parquet.Schema.DataField<string>("display"),
            new Parquet.Schema.DataField<decimal?>("value"),
            new Parquet.Schema.DataField<string>("unit"));

        var columnTypeMap = new Dictionary<string, string>
        {
            ["id"] = "STRING",
            ["status"] = "STRING",
            ["date"] = "STRING",
            ["subject_ref"] = "STRING",
            ["code"] = "STRING",
            ["display"] = "STRING",
            ["value"] = "DECIMAL",
            ["unit"] = "STRING"
        };

        await using var writer = new ParquetFileWriter(path, schema, NullLogger.Instance, columnTypeMap);
        foreach (var row in _preEvaluatedObservationRows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    // ========== END-TO-END: EVALUATE + EXPORT ==========

    [Benchmark(Description = "SqlOnFhir: End-to-end Patient (evaluate + CSV)")]
    [BenchmarkCategory("SqlOnFhir", "EndToEnd")]
    public async Task<long> SqlOnFhirEndToEndCsv()
    {
        var rows = _evaluator.Evaluate(_patientFlattenView, _patientElement).ToList();
        var path = Path.Combine(_tempDir, $"e2e-{Guid.NewGuid():N}.csv");
        await using var writer = new CsvFileWriter(path, NullLogger.Instance);
        foreach (var row in rows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    [Benchmark(Description = "SqlOnFhir: End-to-end Patient (evaluate + NDJSON)")]
    [BenchmarkCategory("SqlOnFhir", "EndToEnd")]
    public async Task<long> SqlOnFhirEndToEndNdjson()
    {
        var rows = _evaluator.Evaluate(_patientFlattenView, _patientElement).ToList();
        var path = Path.Combine(_tempDir, $"e2e-{Guid.NewGuid():N}.ndjson");
        await using var writer = new NdjsonFileWriter(path, NullLogger.Instance);
        foreach (var row in rows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    [Benchmark(Description = "SqlOnFhir: End-to-end Patient (evaluate + Parquet)")]
    [BenchmarkCategory("SqlOnFhir", "EndToEnd")]
    public async Task<long> SqlOnFhirEndToEndParquet()
    {
        var rows = _evaluator.Evaluate(_patientFlattenView, _patientElement).ToList();
        var path = Path.Combine(_tempDir, $"e2e-{Guid.NewGuid():N}.parquet");
        var schema = new ParquetSchema(
            new Parquet.Schema.DataField<string>("id"),
            new Parquet.Schema.DataField<bool?>("active"),
            new Parquet.Schema.DataField<string>("gender"),
            new Parquet.Schema.DataField<string>("birthDate"),
            new Parquet.Schema.DataField<string>("family"),
            new Parquet.Schema.DataField<string>("given"));

        var columnTypeMap = new Dictionary<string, string>
        {
            ["id"] = "STRING",
            ["active"] = "BOOLEAN",
            ["gender"] = "STRING",
            ["birthDate"] = "STRING",
            ["family"] = "STRING",
            ["given"] = "STRING"
        };

        await using var writer = new ParquetFileWriter(path, schema, NullLogger.Instance, columnTypeMap);
        foreach (var row in rows)
        {
            await writer.WriteRowAsync(row);
        }
        await writer.FlushAsync();
        return writer.BytesWritten;
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ISourceNavigator CreateViewDefinitionNode(string json)
    {
        var jsonNode = JsonNode.Parse(json)!;
        return JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
    }
}
