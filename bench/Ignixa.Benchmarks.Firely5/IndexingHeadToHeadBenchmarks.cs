using System.Reflection;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using IgnixaElement = Ignixa.Abstractions.IElement;
using IgnixaEvaluationContext = Ignixa.FhirPath.Evaluation.EvaluationContext;
using IgnixaExpression = Ignixa.FhirPath.Expressions.Expression;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;

namespace Ignixa.Benchmarks.Firely5;

/// <summary>
/// Ignixa versus Firely SDK 5.11.4 over the workload the seam's performance rationale rests on:
/// evaluating every shipped R4 search parameter expression for a resource type, which is what runs
/// on every write.
/// <para>
/// Compilation and evaluation are separate benchmarks on purpose. The indexer compiles once per
/// process and caches, so steady-state evaluation is the per-write cost; a blended number would let
/// a fast compiler hide a slow evaluator or the reverse.
/// </para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net10_0)]
[MarkdownExporter]
public class IndexingHeadToHeadBenchmarks
{
    private SearchParameterExpressionCorpus _corpus = null!;

    private FhirPathParser _ignixaParser = null!;
    private FhirPathCompiler _firelyCompiler = null!;

    private string[] _compileCorpus = null!;

    private IgnixaElement _ignixaPatient = null!;
    private IgnixaElement _ignixaObservation = null!;
    private SdkITypedElement _firelyPatient = null!;
    private SdkITypedElement _firelyObservation = null!;

    private FhirPathEvaluator _ignixaEvaluator = null!;
    private FhirPathDelegateCompiler _ignixaDelegateCompiler = null!;

    private CompiledIgnixa[] _ignixaPatientPlan = null!;
    private CompiledIgnixa[] _ignixaObservationPlan = null!;
    private CompiledExpression[] _firelyPatientPlan = null!;
    private CompiledExpression[] _firelyObservationPlan = null!;

    private IgnixaEvaluationContext _ignixaPatientContext = null!;
    private IgnixaEvaluationContext _ignixaObservationContext = null!;
    private Hl7.FhirPath.EvaluationContext _firelyContext = null!;

    private readonly record struct CompiledIgnixa(
        IgnixaExpression Ast,
        Func<IgnixaElement, IgnixaEvaluationContext, IEnumerable<IgnixaElement>>? Compiled);

    [GlobalSetup]
    public void Setup()
    {
        _corpus = SearchParameterExpressionCorpus.Load();
        _compileCorpus = _corpus.CommonExpressions.ToArray();

        _ignixaParser = new FhirPathParser();
        _firelyCompiler = new FhirPathCompiler();

        var assembly = Assembly.GetExecutingAssembly();
        var patientJson = ReadEmbeddedResource(assembly, "patient-small.json");
        var observationJson = ReadEmbeddedResource(assembly, "observation-medium.json");

        var schemaProvider = new R4CoreSchemaProvider();
        _ignixaPatient = ResourceJsonNode.Parse(patientJson).ToElement(schemaProvider);
        _ignixaObservation = ResourceJsonNode.Parse(observationJson).ToElement(schemaProvider);

        _firelyPatient = FhirJsonNode.Parse(patientJson).ToTypedElement(ModelInfo.ModelInspector);
        _firelyObservation = FhirJsonNode.Parse(observationJson).ToTypedElement(ModelInfo.ModelInspector);

        _ignixaEvaluator = new FhirPathEvaluator();
        _ignixaDelegateCompiler = new FhirPathDelegateCompiler(_ignixaEvaluator);

        _ignixaPatientPlan = BuildIgnixaPlan("Patient");
        _ignixaObservationPlan = BuildIgnixaPlan("Observation");
        _firelyPatientPlan = BuildFirelyPlan("Patient");
        _firelyObservationPlan = BuildFirelyPlan("Observation");

        _ignixaPatientContext = new IgnixaEvaluationContext() with { Resource = _ignixaPatient, RootResource = _ignixaPatient };
        _ignixaObservationContext = new IgnixaEvaluationContext() with { Resource = _ignixaObservation, RootResource = _ignixaObservation };
        _firelyContext = new Hl7.FhirPath.EvaluationContext();

        // Warm both engines' evaluation paths so neither pays first-call JIT inside a measurement.
        _ = EvaluateIgnixa(_ignixaPatientPlan, _ignixaPatient, _ignixaPatientContext);
        _ = EvaluateIgnixa(_ignixaObservationPlan, _ignixaObservation, _ignixaObservationContext);
        _ = EvaluateFirely(_firelyPatientPlan, _firelyPatient);
        _ = EvaluateFirely(_firelyObservationPlan, _firelyObservation);
    }

    public SearchParameterExpressionCorpusSummary Summary => new(
        _corpus.AllExpressions.Count,
        _corpus.CommonExpressions.Count,
        _corpus.IgnixaCompileFailures,
        _corpus.FirelyCompileFailures,
        _corpus.CommonByResourceType.TryGetValue("Patient", out var patient) ? patient.Count : 0,
        _corpus.CommonByResourceType.TryGetValue("Observation", out var observation) ? observation.Count : 0);

    private CompiledIgnixa[] BuildIgnixaPlan(string resourceType)
    {
        return GetExpressions(resourceType)
            .Select(expression =>
            {
                var ast = _ignixaParser.Parse(expression);
                return new CompiledIgnixa(ast, _ignixaDelegateCompiler.TryCompile(ast));
            })
            .ToArray();
    }

    private CompiledExpression[] BuildFirelyPlan(string resourceType)
    {
        return GetExpressions(resourceType)
            .Select(_firelyCompiler.Compile)
            .ToArray();
    }

    private IReadOnlyList<string> GetExpressions(string resourceType)
    {
        return _corpus.CommonByResourceType.TryGetValue(resourceType, out IReadOnlyList<string>? expressions)
            ? expressions
            : [];
    }

    private static string ReadEmbeddedResource(Assembly assembly, string fileName)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Resource not found: {fileName}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ========== COMPILATION (paid once per process, then cached) ==========

    [Benchmark(Description = "Ignixa: compile whole R4 search parameter corpus")]
    [BenchmarkCategory("Compile")]
    public int IgnixaCompileCorpus()
    {
        var count = 0;
        foreach (var expression in _compileCorpus)
        {
            var ast = new FhirPathParser().Parse(expression);
            if (ast is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "Firely 5.11.4: compile whole R4 search parameter corpus")]
    [BenchmarkCategory("Compile")]
    public int FirelyCompileCorpus()
    {
        var count = 0;
        var compiler = new FhirPathCompiler();
        foreach (var expression in _compileCorpus)
        {
            if (compiler.Compile(expression) is not null)
            {
                count++;
            }
        }

        return count;
    }

    // ========== EVALUATION (paid on every write) ==========

    [Benchmark(Baseline = true, Description = "Ignixa: evaluate all Patient search expressions")]
    [BenchmarkCategory("Evaluate-Patient")]
    public int IgnixaEvaluatePatient()
        => EvaluateIgnixa(_ignixaPatientPlan, _ignixaPatient, _ignixaPatientContext);

    [Benchmark(Description = "Firely 5.11.4: evaluate all Patient search expressions")]
    [BenchmarkCategory("Evaluate-Patient")]
    public int FirelyEvaluatePatient()
        => EvaluateFirely(_firelyPatientPlan, _firelyPatient);

    [Benchmark(Description = "Ignixa: evaluate all Observation search expressions")]
    [BenchmarkCategory("Evaluate-Observation")]
    public int IgnixaEvaluateObservation()
        => EvaluateIgnixa(_ignixaObservationPlan, _ignixaObservation, _ignixaObservationContext);

    [Benchmark(Description = "Firely 5.11.4: evaluate all Observation search expressions")]
    [BenchmarkCategory("Evaluate-Observation")]
    public int FirelyEvaluateObservation()
        => EvaluateFirely(_firelyObservationPlan, _firelyObservation);

    private int EvaluateIgnixa(CompiledIgnixa[] plan, IgnixaElement input, IgnixaEvaluationContext context)
    {
        var results = 0;
        foreach (CompiledIgnixa entry in plan)
        {
            IEnumerable<IgnixaElement> matches = entry.Compiled is not null
                ? entry.Compiled(input, context)
                : _ignixaEvaluator.Evaluate(input, entry.Ast, context);

            foreach (var _ in matches)
            {
                results++;
            }
        }

        return results;
    }

    private int EvaluateFirely(CompiledExpression[] plan, SdkITypedElement input)
    {
        var results = 0;
        foreach (CompiledExpression compiled in plan)
        {
            foreach (var _ in compiled(input, _firelyContext))
            {
                results++;
            }
        }

        return results;
    }
}

/// <summary>
/// Corpus shape, reported alongside the timings so the reader knows how many expressions each number
/// covers and how many each engine could not compile.
/// </summary>
public readonly record struct SearchParameterExpressionCorpusSummary(
    int DistinctExpressions,
    int CommonExpressions,
    int IgnixaCompileFailures,
    int FirelyCompileFailures,
    int PatientExpressions,
    int ObservationExpressions);
