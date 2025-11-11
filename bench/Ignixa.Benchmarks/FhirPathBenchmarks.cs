using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath; // SDK 6.0 FHIRPath extension methods
using Ignixa.Search.Infrastructure;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
// Ignixa FHIRPath extension methods
using Microsoft.Extensions.Logging.Abstractions;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;
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
public class FhirPathBenchmarks
{
    private ResourceJsonNode _ignixaPatient = null!;
    private ResourceJsonNode _ignixaObservation = null!;
    private ITypedElement _ignixaPatientTyped = null!;
    private ITypedElement _ignixaObservationTyped = null!;
    private SdkITypedElement _firelyPatientTyped = null!;
    private SdkITypedElement _firelyObservationTyped = null!;
    private IFhirSchemaProvider _ignixaSchemaProvider = null!;
    private FhirVersionContext _versionContext = null!;

    private Ignixa.FhirPath.FhirPathCompiler _ignixaCompiler = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Load test data
        var assembly = Assembly.GetExecutingAssembly();
        var patientJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.patient-small.json");
        var observationJson = ReadEmbeddedResource(assembly, "Ignixa.Benchmarks.TestData.observation-medium.json");

        // Ignixa setup
        _ignixaPatient = JsonSerializer.Deserialize<ResourceJsonNode>(patientJson)!;
        _ignixaObservation = JsonSerializer.Deserialize<ResourceJsonNode>(observationJson)!;

        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        _versionContext = new FhirVersionContext(NullLoggerFactory.Instance, searchParamOptions);
        _ignixaSchemaProvider = _versionContext.GetBaseSchemaProvider(FhirSpecification.R4);

        _ignixaPatientTyped = TypedElementExtensions.ToTypedElement(_ignixaPatient.ToSourceNode(), _ignixaSchemaProvider);
        _ignixaObservationTyped = TypedElementExtensions.ToTypedElement(_ignixaObservation.ToSourceNode(), _ignixaSchemaProvider);

        _ignixaCompiler = new Ignixa.FhirPath.FhirPathCompiler();

        // Firely setup
        var firelyPatientSource = Hl7.Fhir.Serialization.FhirJsonNode.Parse(patientJson);
        var firelyObservationSource = Hl7.Fhir.Serialization.FhirJsonNode.Parse(observationJson);

        _firelyPatientTyped = firelyPatientSource.ToTypedElement(ModelInfo.ModelInspector);
        _firelyObservationTyped = firelyObservationSource.ToTypedElement(ModelInfo.ModelInspector);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ========== SIMPLE PROPERTY ACCESS ==========

    [Benchmark(Description = "Ignixa: Simple FHIRPath (Patient.name.family)")]
    [BenchmarkCategory("Simple")]
    public ITypedElement[] IgnixaSimple()
    {
        return _ignixaPatientTyped.Select("Patient.name.family").ToArray();
    }

    [Benchmark(Description = "Firely: Simple FHIRPath (Patient.name.family)")]
    [BenchmarkCategory("Simple")]
    public SdkITypedElement[] FirelySimple()
    {
        return _firelyPatientTyped.Select("Patient.name.family").ToArray();
    }

    // ========== ARRAY INDEXING ==========

    [Benchmark(Description = "Ignixa: Array indexing (Patient.name[0].given)")]
    [BenchmarkCategory("Array")]
    public ITypedElement[] IgnixaArray()
    {
        return _ignixaPatientTyped.Select("Patient.name[0].given").ToArray();
    }

    [Benchmark(Description = "Firely: Array indexing (Patient.name[0].given)")]
    [BenchmarkCategory("Array")]
    public SdkITypedElement[] FirelyArray()
    {
        return _firelyPatientTyped.Select("Patient.name[0].given").ToArray();
    }

    // ========== COMPLEX NAVIGATION WITH where() ==========

    [Benchmark(Description = "Ignixa: Complex navigation (where + first)")]
    [BenchmarkCategory("Complex")]
    public ITypedElement[] IgnixaComplex()
    {
        return _ignixaPatientTyped.Select("Patient.name.where(use='official').given.first()").ToArray();
    }

    [Benchmark(Description = "Firely: Complex navigation (where + first)")]
    [BenchmarkCategory("Complex")]
    public SdkITypedElement[] FirelyComplex()
    {
        return _firelyPatientTyped.Select("Patient.name.where(use='official').given.first()").ToArray();
    }

    // ========== REALISTIC SEARCH PARAMETER EXTRACTION ==========

    [Benchmark(Description = "Ignixa: Search parameter extraction (component value)")]
    [BenchmarkCategory("SearchParam")]
    public ITypedElement[] IgnixaSearchParam()
    {
        return _ignixaObservationTyped.Select("Observation.component.where(code.coding.code='8480-6').valueQuantity.value").ToArray();
    }

    [Benchmark(Description = "Firely: Search parameter extraction (component value)")]
    [BenchmarkCategory("SearchParam")]
    public SdkITypedElement[] FirelySearchParam()
    {
        return _firelyObservationTyped.Select("Observation.component.where(code.coding.code='8480-6').valueQuantity.value").ToArray();
    }

    // ========== SCALAR VALUE EXTRACTION ==========

    [Benchmark(Description = "Ignixa: Scalar extraction (Patient.birthDate)")]
    [BenchmarkCategory("Scalar")]
    public object? IgnixaScalar()
    {
        return _ignixaPatientTyped.Scalar("Patient.birthDate");
    }

    [Benchmark(Description = "Firely: Scalar extraction (Patient.birthDate)")]
    [BenchmarkCategory("Scalar")]
    public object? FirelyScalar()
    {
        return _firelyPatientTyped.Scalar("Patient.birthDate");
    }

    // ========== COMPILATION PERFORMANCE ==========

    [Benchmark(Description = "Ignixa: Compile FHIRPath expression")]
    [BenchmarkCategory("Compile")]
    public Ignixa.FhirPath.Expressions.Expression IgnixaCompile()
    {
        return _ignixaCompiler.Parse("Patient.name.where(use='official').given.first()");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _versionContext?.Dispose();
    }
}
