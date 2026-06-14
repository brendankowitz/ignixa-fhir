using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Ignixa.Application.Features.Search;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
// Ignixa FHIRPath extension methods
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;
using IElement = Ignixa.Abstractions.IElement;

// Static using for extension methods
using Ignixa.Extensions.FirelySdk;

#pragma warning disable CS0618

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net10_0)]
[RankColumn]
[MarkdownExporter]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet handles cleanup via GlobalCleanup")]
public class FhirPathBenchmarks
{
    private ResourceJsonNode _ignixaPatient = null!;
    private ResourceJsonNode _ignixaObservation = null!;
    private IElement _ignixaPatientTyped = null!;
    private IElement _ignixaObservationTyped = null!;
    private SdkITypedElement _firelyPatientTyped = null!;
    private SdkITypedElement _firelyObservationTyped = null!;
    private IElement _hybridPatientTyped = null!;
    private IElement _hybridObservationTyped = null!;
    private IFhirSchemaProvider _ignixaSchemaProvider = null!;
    private FhirVersionContext _versionContext = null!;

    private FhirPathParser _ignixaParser = null!;
    private FhirPathParser _ignixaParserOptimized = null!;
    private FhirPathCompiler _firelyCompiler = null!;

    // Ignixa pre-compiled (AST + delegate resolved once, matching Select() minus the cache lookups)
    private FhirPathDelegateCompiler _ignixaDelegateCompiler = null!;
    private FhirPathEvaluator _ignixaEvaluator = null!;
    private Ignixa.FhirPath.Expressions.Expression _ignixaSimpleAst = null!;
    private Ignixa.FhirPath.Expressions.Expression _ignixaComplexAst = null!;
    private Ignixa.FhirPath.Expressions.Expression _ignixaSearchParamAst = null!;
    private Func<IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, IEnumerable<IElement>>? _ignixaSimpleDelegate;
    private Func<IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, IEnumerable<IElement>>? _ignixaComplexDelegate;
    private Func<IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, IEnumerable<IElement>>? _ignixaSearchParamDelegate;
    private Ignixa.FhirPath.Evaluation.EvaluationContext _ignixaPatientCtx = null!;
    private Ignixa.FhirPath.Evaluation.EvaluationContext _ignixaObservationCtx = null!;

    // Firely pre-compiled (CompiledExpression resolved once)
    private CompiledExpression _firelySimpleCompiled = null!;
    private CompiledExpression _firelyComplexCompiled = null!;
    private CompiledExpression _firelySearchParamCompiled = null!;
    private Hl7.FhirPath.EvaluationContext _firelyEvalCtxPrecompiled = null!;
    private Hl7.Fhir.Model.PocoNode _firelyPatientPocoNode = null!;
    private Hl7.Fhir.Model.PocoNode _firelyObservationPocoNode = null!;

    private const string ComplexExpression = "Patient.name.where(use='official').given.first()";
    private const string SimpleExpression = "Patient.name.family";
    private const string ArrayExpression = "Patient.name[0].given";
    private const string SearchParamExpression = "Observation.component.where(code.coding.code='8480-6').valueQuantity.value";
    private const string ScalarExpression = "Patient.birthDate";

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
        
        _ignixaSchemaProvider = new R4CoreSchemaProvider();

        _ignixaPatientTyped = _ignixaPatient.ToSourceNavigator().ToElement(_ignixaSchemaProvider);
        _ignixaObservationTyped = _ignixaObservation.ToSourceNavigator().ToElement(_ignixaSchemaProvider);

        _ignixaParser = new FhirPathParser();
        _ignixaParserOptimized = new FhirPathParser(Ignixa.FhirPath.Parsing.CompilationOptions.Optimized);
        _firelyCompiler = new FhirPathCompiler();

        // Firely setup
        var firelyPatientSource = Hl7.Fhir.Serialization.FhirJsonNode.Parse(patientJson);
        var firelyObservationSource = Hl7.Fhir.Serialization.FhirJsonNode.Parse(observationJson);

        _firelyPatientTyped = firelyPatientSource.ToTypedElement(ModelInfo.ModelInspector);
        _firelyObservationTyped = firelyObservationSource.ToTypedElement(ModelInfo.ModelInspector);

        // Hybrid setup: Firely deserialization -> Ignixa IElement (via adapter) for FHIRPath evaluation
        // Uses IgnixaElementAdapter to wrap Firely's ITypedElement as Ignixa's IElement
        _hybridPatientTyped = new IgnixaElementAdapter(_firelyPatientTyped);
        _hybridObservationTyped = new IgnixaElementAdapter(_firelyObservationTyped);

        WarmupCaches();

        _ignixaEvaluator = new FhirPathEvaluator();
        _ignixaDelegateCompiler = new FhirPathDelegateCompiler(_ignixaEvaluator);

        _ignixaSimpleAst = _ignixaParser.Parse(SimpleExpression);
        _ignixaComplexAst = _ignixaParser.Parse(ComplexExpression);
        _ignixaSearchParamAst = _ignixaParser.Parse(SearchParamExpression);
        _ignixaSimpleDelegate = _ignixaDelegateCompiler.TryCompile(_ignixaSimpleAst);
        _ignixaComplexDelegate = _ignixaDelegateCompiler.TryCompile(_ignixaComplexAst);
        _ignixaSearchParamDelegate = _ignixaDelegateCompiler.TryCompile(_ignixaSearchParamAst);

        _ignixaPatientCtx = new Ignixa.FhirPath.Evaluation.EvaluationContext() with { Resource = _ignixaPatientTyped, RootResource = _ignixaPatientTyped };
        _ignixaObservationCtx = new Ignixa.FhirPath.Evaluation.EvaluationContext() with { Resource = _ignixaObservationTyped, RootResource = _ignixaObservationTyped };

        _firelyEvalCtxPrecompiled = Hl7.FhirPath.EvaluationContext.CreateDefault();
        // CompiledExpression operates on PocoNode. ITypedElement.Select does input.ToPocoNode(...)
        // on every call; hoist that same conversion into setup so the benchmark measures
        // evaluation only.
        _firelyPatientPocoNode = _firelyPatientTyped.ToPocoNode(rootName: _firelyPatientTyped.Location);
        _firelyObservationPocoNode = _firelyObservationTyped.ToPocoNode(rootName: _firelyObservationTyped.Location);
        _firelySimpleCompiled = _firelyCompiler.Compile(SimpleExpression);
        _firelyComplexCompiled = _firelyCompiler.Compile(ComplexExpression);
        _firelySearchParamCompiled = _firelyCompiler.Compile(SearchParamExpression);
    }

    private void WarmupCaches()
    {
        _ = _ignixaPatientTyped.Select(SimpleExpression).ToArray();
        _ = _ignixaPatientTyped.Select(ArrayExpression).ToArray();
        _ = _ignixaPatientTyped.Select(ComplexExpression).ToArray();
        _ = _ignixaObservationTyped.Select(SearchParamExpression).ToArray();
        _ = _ignixaPatientTyped.Scalar(ScalarExpression);

        _ = _firelyPatientTyped.Select(SimpleExpression).ToArray();
        _ = _firelyPatientTyped.Select(ArrayExpression).ToArray();
        _ = _firelyPatientTyped.Select(ComplexExpression).ToArray();
        _ = _firelyObservationTyped.Select(SearchParamExpression).ToArray();
        _ = _firelyPatientTyped.Scalar(ScalarExpression);

        _ = _hybridPatientTyped.Select(SimpleExpression).ToArray();
        _ = _hybridPatientTyped.Select(ArrayExpression).ToArray();
        _ = _hybridPatientTyped.Select(ComplexExpression).ToArray();
        _ = _hybridObservationTyped.Select(SearchParamExpression).ToArray();
        _ = _hybridPatientTyped.Scalar(ScalarExpression);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Benchmark(Description = "Ignixa: Parse (no optimizations)")]
    [BenchmarkCategory("Compilation")]
    public Ignixa.FhirPath.Expressions.Expression IgnixaParseBaseline()
    {
        return _ignixaParser.Parse(ComplexExpression);
    }

    [Benchmark(Description = "Ignixa: Parse (with optimizations)")]
    [BenchmarkCategory("Compilation")]
    public Ignixa.FhirPath.Expressions.Expression IgnixaParseOptimized()
    {
        return _ignixaParserOptimized.Parse(ComplexExpression);
    }

    [Benchmark(Description = "Firely: Compile FHIRPath expression")]
    [BenchmarkCategory("Compilation")]
    public CompiledExpression FirelyCompile()
    {
        return _firelyCompiler.Compile(ComplexExpression);
    }

    [Benchmark(Description = "Ignixa: Simple FHIRPath (Patient.name.family)")]
    [BenchmarkCategory("Execution-Simple")]
    public IElement[] IgnixaSimple()
    {
        return _ignixaPatientTyped.Select(SimpleExpression).ToArray();
    }

    [Benchmark(Description = "Firely: Simple FHIRPath (Patient.name.family)")]
    [BenchmarkCategory("Execution-Simple")]
    public SdkITypedElement[] FirelySimple()
    {
        return _firelyPatientTyped.Select(SimpleExpression).ToArray();
    }

    [Benchmark(Description = "Ignixa: Array indexing (Patient.name[0].given)")]
    [BenchmarkCategory("Execution-Array")]
    public IElement[] IgnixaArray()
    {
        return _ignixaPatientTyped.Select(ArrayExpression).ToArray();
    }

    [Benchmark(Description = "Firely: Array indexing (Patient.name[0].given)")]
    [BenchmarkCategory("Execution-Array")]
    public SdkITypedElement[] FirelyArray()
    {
        return _firelyPatientTyped.Select(ArrayExpression).ToArray();
    }

    [Benchmark(Description = "Ignixa: Complex navigation (where + first)")]
    [BenchmarkCategory("Execution-Complex")]
    public IElement[] IgnixaComplex()
    {
        return _ignixaPatientTyped.Select(ComplexExpression).ToArray();
    }

    [Benchmark(Description = "Firely: Complex navigation (where + first)")]
    [BenchmarkCategory("Execution-Complex")]
    public SdkITypedElement[] FirelyComplex()
    {
        return _firelyPatientTyped.Select(ComplexExpression).ToArray();
    }

    [Benchmark(Description = "Ignixa: Search parameter extraction (component value)")]
    [BenchmarkCategory("Execution-SearchParam")]
    public IElement[] IgnixaSearchParam()
    {
        return _ignixaObservationTyped.Select(SearchParamExpression).ToArray();
    }

    [Benchmark(Description = "Firely: Search parameter extraction (component value)")]
    [BenchmarkCategory("Execution-SearchParam")]
    public SdkITypedElement[] FirelySearchParam()
    {
        return _firelyObservationTyped.Select(SearchParamExpression).ToArray();
    }

    [Benchmark(Description = "Ignixa: Scalar extraction (Patient.birthDate)")]
    [BenchmarkCategory("Execution-Scalar")]
    public object? IgnixaScalar()
    {
        return _ignixaPatientTyped.Scalar(ScalarExpression);
    }

    [Benchmark(Description = "Firely: Scalar extraction (Patient.birthDate)")]
    [BenchmarkCategory("Execution-Scalar")]
    public object? FirelyScalar()
    {
        return _firelyPatientTyped.Scalar(ScalarExpression);
    }

    // ========== HYBRID: Firely deserialization + Ignixa FHIRPath engine ==========

    [Benchmark(Description = "Hybrid: Simple FHIRPath (Firely parse + Ignixa eval)")]
    [BenchmarkCategory("Execution-Simple", "Hybrid")]
    public IElement[] HybridSimple()
    {
        return _hybridPatientTyped.Select(SimpleExpression).ToArray();
    }

    [Benchmark(Description = "Hybrid: Array indexing (Firely parse + Ignixa eval)")]
    [BenchmarkCategory("Execution-Array", "Hybrid")]
    public IElement[] HybridArray()
    {
        return _hybridPatientTyped.Select(ArrayExpression).ToArray();
    }

    [Benchmark(Description = "Hybrid: Complex navigation (Firely parse + Ignixa eval)")]
    [BenchmarkCategory("Execution-Complex", "Hybrid")]
    public IElement[] HybridComplex()
    {
        return _hybridPatientTyped.Select(ComplexExpression).ToArray();
    }

    [Benchmark(Description = "Hybrid: Search parameter extraction (Firely parse + Ignixa eval)")]
    [BenchmarkCategory("Execution-SearchParam", "Hybrid")]
    public IElement[] HybridSearchParam()
    {
        return _hybridObservationTyped.Select(SearchParamExpression).ToArray();
    }

    [Benchmark(Description = "Hybrid: Scalar extraction (Firely parse + Ignixa eval)")]
    [BenchmarkCategory("Execution-Scalar", "Hybrid")]
    public object? HybridScalar()
    {
        return _hybridPatientTyped.Scalar(ScalarExpression);
    }

    // ========== NORMALIZED EVALUATION (both engines pre-compiled; measures eval only) ==========

    private static IElement[] IgnixaEval(
        Func<IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, IEnumerable<IElement>>? compiled,
        FhirPathEvaluator evaluator,
        Ignixa.FhirPath.Expressions.Expression ast,
        IElement input,
        Ignixa.FhirPath.Evaluation.EvaluationContext ctx)
    {
        return compiled != null
            ? compiled(input, ctx).ToArray()
            : evaluator.Evaluate(input, ast, ctx).ToArray();
    }

    [Benchmark(Description = "Ignixa: Eval simple (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-Simple")]
    public IElement[] IgnixaEvalSimplePrecompiled()
        => IgnixaEval(_ignixaSimpleDelegate, _ignixaEvaluator, _ignixaSimpleAst, _ignixaPatientTyped, _ignixaPatientCtx);

    [Benchmark(Description = "Firely: Eval simple (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-Simple")]
    public SdkITypedElement[] FirelyEvalSimplePrecompiled()
        => _firelySimpleCompiled(_firelyPatientPocoNode, _firelyEvalCtxPrecompiled).ToArray();

    [Benchmark(Description = "Ignixa: Eval complex (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-Complex")]
    public IElement[] IgnixaEvalComplexPrecompiled()
        => IgnixaEval(_ignixaComplexDelegate, _ignixaEvaluator, _ignixaComplexAst, _ignixaPatientTyped, _ignixaPatientCtx);

    [Benchmark(Description = "Firely: Eval complex (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-Complex")]
    public SdkITypedElement[] FirelyEvalComplexPrecompiled()
        => _firelyComplexCompiled(_firelyPatientPocoNode, _firelyEvalCtxPrecompiled).ToArray();

    [Benchmark(Description = "Ignixa: Eval searchparam (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-SearchParam")]
    public IElement[] IgnixaEvalSearchParamPrecompiled()
        => IgnixaEval(_ignixaSearchParamDelegate, _ignixaEvaluator, _ignixaSearchParamAst, _ignixaObservationTyped, _ignixaObservationCtx);

    [Benchmark(Description = "Firely: Eval searchparam (pre-compiled, eval only)")]
    [BenchmarkCategory("Eval-SearchParam")]
    public SdkITypedElement[] FirelyEvalSearchParamPrecompiled()
        => _firelySearchParamCompiled(_firelyObservationPocoNode, _firelyEvalCtxPrecompiled).ToArray();

    [GlobalCleanup]
    public void Cleanup()
    {
        _versionContext?.Dispose();
    }
}
