using System.Reflection;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification.Generated;
using FirelyEvaluationContext = Hl7.Fhir.FhirPath.FhirEvaluationContext;
using IgnixaElement = Ignixa.Abstractions.IElement;
using IgnixaEvaluationContext = Ignixa.FhirPath.Evaluation.FhirEvaluationContext;
using IgnixaExpression = Ignixa.FhirPath.Expressions.Expression;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;

namespace Ignixa.Benchmarks.Firely5;

/// <summary>
/// Measures the Phase 3 indexing topology where both engines receive the same Firely 5.11.4 POCO.
/// Parsing, compilation, and corpus loading happen in setup. The Ignixa path pays the input adapter,
/// evaluation-context bridge, evaluation, and result adapter on every expression invocation.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
public class IndexingHeadToHeadBenchmarks
{
    private static readonly FhirVersion[] Versions =
    [
        FhirVersion.Stu3,
        FhirVersion.R4,
        FhirVersion.R4B,
        FhirVersion.R5,
        FhirVersion.R6,
    ];

    private SearchParameterExpressionCorpus[] _corpora = null!;
    private string[] _compileCorpus = null!;
    private IReadOnlyDictionary<ExpressionFamily, EvaluationPlanEntry[]> _plans = null!;

    private FhirPathParser _ignixaParser = null!;
    private FhirPathCompiler _firelyCompiler = null!;
    private FhirPathEvaluator _ignixaEvaluator = null!;
    private FhirPathDelegateCompiler _ignixaDelegateCompiler = null!;
    private R4CoreSchemaProvider _schemaProvider = null!;

    private SdkITypedElement _firelyPatient = null!;
    private SdkITypedElement _firelyObservation = null!;
    private SdkITypedElement _firelyAppointment = null!;

    private readonly record struct CompiledIgnixa(
        IgnixaExpression Ast,
        Func<IgnixaElement, Ignixa.FhirPath.Evaluation.EvaluationContext, IEnumerable<IgnixaElement>>? Compiled);

    private readonly record struct EvaluationPlanEntry(
        CompiledIgnixa Ignixa,
        CompiledExpression Firely,
        SdkITypedElement Input,
        AdapterFixture Fixture);

    private readonly record struct FirelyContexts(
        FirelyEvaluationContext Patient,
        FirelyEvaluationContext Observation,
        FirelyEvaluationContext Appointment);

    public enum ExpressionFamily
    {
        All,
        Union,
        Where,
        OfType,
        Resolve,
        As,
        Plain,
    }

    public enum AdapterFixture
    {
        Patient,
        Observation,
        Appointment,
    }

    public static IEnumerable<ExpressionFamily> Families => Enum.GetValues<ExpressionFamily>();

    public static IEnumerable<AdapterFixture> AdapterFixtures => Enum.GetValues<AdapterFixture>();

    [GlobalSetup]
    public void Setup()
    {
        FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        _corpora = Versions.Select(SearchParameterExpressionCorpus.Load).ToArray();
        _compileCorpus = _corpora
            .SelectMany(corpus => corpus.CommonExpressions)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _ignixaParser = new FhirPathParser();
        _firelyCompiler = new FhirPathCompiler();
        _ignixaEvaluator = new FhirPathEvaluator();
        _ignixaDelegateCompiler = new FhirPathDelegateCompiler(_ignixaEvaluator);
        _schemaProvider = new R4CoreSchemaProvider();

        var assembly = Assembly.GetExecutingAssembly();
        var patientJson = ReadEmbeddedResource(assembly, "patient-small.json");
        var observationJson = ReadEmbeddedResource(assembly, "observation-medium.json");
        var appointmentJson = ReadEmbeddedResource(assembly, "appointment-resolve.json");
        var parser = new FhirJsonParser();

        _firelyPatient = parser.Parse<Resource>(patientJson).ToTypedElement();
        _firelyObservation = parser.Parse<Resource>(observationJson).ToTypedElement();
        _firelyAppointment = parser.Parse<Resource>(appointmentJson).ToTypedElement();

        _plans = BuildPlans();

        foreach (ExpressionFamily family in Families)
        {
            _ = EvaluateFirelyDirect(family);
            _ = EvaluateFirelyScoped(family);
            _ = EvaluateIgnixaAdapterInput(family);
        }

        foreach (AdapterFixture fixture in AdapterFixtures)
        {
            _ = MaterializeIgnixaAdapter(fixture);
        }
    }

    public SearchParameterExpressionCorpusSummary Summary => new(
        Versions.Length,
        _corpora.Sum(corpus => corpus.Parameters.Count),
        _corpora.SelectMany(corpus => corpus.AllExpressions).Distinct(StringComparer.Ordinal).Count(),
        _compileCorpus.Length,
        _corpora.Sum(corpus => corpus.IgnixaCompileFailures),
        _corpora.Sum(corpus => corpus.FirelyCompileFailures),
        Families.ToDictionary(family => family, family => _plans[family].Length));

    private IReadOnlyDictionary<ExpressionFamily, EvaluationPlanEntry[]> BuildPlans()
    {
        var plans = Families.ToDictionary(family => family, _ => new List<EvaluationPlanEntry>());
        var ignixaCache = new Dictionary<string, CompiledIgnixa>(StringComparer.Ordinal);
        var firelyCache = new Dictionary<string, CompiledExpression>(StringComparer.Ordinal);

        foreach (SearchParameterExpressionCorpus corpus in _corpora)
        {
            AddResourcePlans(corpus, "Patient", _firelyPatient, AdapterFixture.Patient);
            AddResourcePlans(corpus, "Observation", _firelyObservation, AdapterFixture.Observation);
            AddResourcePlans(corpus, "Appointment", _firelyAppointment, AdapterFixture.Appointment);
        }

        return plans.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        void AddResourcePlans(
            SearchParameterExpressionCorpus corpus,
            string resourceType,
            SdkITypedElement input,
            AdapterFixture fixture)
        {
            if (!corpus.CommonByResourceType.TryGetValue(resourceType, out IReadOnlyList<string>? expressions))
            {
                return;
            }

            foreach (var expression in expressions)
            {
                if (!ignixaCache.TryGetValue(expression, out CompiledIgnixa ignixa))
                {
                    var ast = _ignixaParser.Parse(expression);
                    ignixa = new CompiledIgnixa(ast, _ignixaDelegateCompiler.TryCompile(ast));
                    ignixaCache.Add(expression, ignixa);
                }

                if (!firelyCache.TryGetValue(expression, out CompiledExpression? firely))
                {
                    firely = _firelyCompiler.Compile(expression);
                    firelyCache.Add(expression, firely);
                }

                var entry = new EvaluationPlanEntry(ignixa, firely, input, fixture);
                plans[ExpressionFamily.All].Add(entry);

                var matchedFamily = false;
                foreach (ExpressionFamily family in GetExpressionFamilies(expression))
                {
                    plans[family].Add(entry);
                    matchedFamily = true;
                }

                if (!matchedFamily)
                {
                    plans[ExpressionFamily.Plain].Add(entry);
                }
            }
        }
    }

    private static IEnumerable<ExpressionFamily> GetExpressionFamilies(string expression)
    {
        if (expression.Contains('|', StringComparison.Ordinal))
        {
            yield return ExpressionFamily.Union;
        }

        if (expression.Contains("where(", StringComparison.Ordinal))
        {
            yield return ExpressionFamily.Where;
        }

        if (expression.Contains("ofType(", StringComparison.Ordinal))
        {
            yield return ExpressionFamily.OfType;
        }

        if (expression.Contains("resolve()", StringComparison.Ordinal))
        {
            yield return ExpressionFamily.Resolve;
        }

        if (expression.Contains("as(", StringComparison.Ordinal))
        {
            yield return ExpressionFamily.As;
        }
    }

    private static FirelyEvaluationContext CreateFirelyContext(
        SdkITypedElement input,
        Func<string, SdkITypedElement>? elementResolver = null) => new()
    {
        Resource = input,
        RootResource = input,
        ElementResolver = elementResolver ?? (static _ => null!),
    };

    private FirelyContexts CreateFirelyContexts() => new(
        CreateFirelyContext(_firelyPatient),
        CreateFirelyContext(_firelyObservation),
        CreateFirelyContext(
            _firelyAppointment,
            reference => reference == "Patient/benchmark" ? _firelyPatient : null!));

    private static FirelyEvaluationContext GetFirelyContext(
        AdapterFixture fixture,
        FirelyContexts contexts) => fixture switch
    {
        AdapterFixture.Patient => contexts.Patient,
        AdapterFixture.Observation => contexts.Observation,
        AdapterFixture.Appointment => contexts.Appointment,
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown adapter fixture"),
    };

    private IgnixaEvaluationContext CreateIgnixaContext(
        IgnixaElement input,
        FirelyEvaluationContext firelyContext)
    {
        Func<string, IgnixaElement?>? elementResolver = firelyContext.ElementResolver is null
            ? null
            : reference => firelyContext.ElementResolver(reference)?.ToIgnixaElement();

        return new IgnixaEvaluationContext
        {
            Schema = _schemaProvider,
            Resource = input,
            RootResource = input,
            ElementResolver = elementResolver,
        };
    }

    private static string ReadEmbeddedResource(Assembly assembly, string fileName)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Resource not found: {fileName}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Benchmark(Description = "Ignixa: compile five-version search expression corpus")]
    [BenchmarkCategory("Compile")]
    public int IgnixaCompileCorpus()
    {
        var count = 0;
        foreach (var expression in _compileCorpus)
        {
            if (new FhirPathParser().Parse(expression) is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "Firely 5.11.4: compile five-version search expression corpus")]
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

    [Benchmark(Baseline = true, Description = "Firely indexer: direct precompiled evaluation")]
    [BenchmarkCategory("AdapterInputEvaluation")]
    [ArgumentsSource(nameof(Families))]
    public int EvaluateFirelyDirect(ExpressionFamily family)
    {
        var checksum = 0;
        FirelyContexts contexts = CreateFirelyContexts();
        foreach (EvaluationPlanEntry entry in _plans[family])
        {
            checksum += ConsumeFirelyResults(entry.Firely(entry.Input, GetFirelyContext(entry.Fixture, contexts)));
        }

        return checksum;
    }

    [Benchmark(Description = "Firely seam: scoped precompiled evaluation")]
    [BenchmarkCategory("AdapterInputEvaluation")]
    [ArgumentsSource(nameof(Families))]
    public int EvaluateFirelyScoped(ExpressionFamily family)
    {
        var checksum = 0;
        FirelyContexts contexts = CreateFirelyContexts();
        foreach (EvaluationPlanEntry entry in _plans[family])
        {
            SdkITypedElement scopedInput = entry.Input.ToScopedNode();
            checksum += ConsumeFirelyResults(entry.Firely(scopedInput, GetFirelyContext(entry.Fixture, contexts)));
        }

        return checksum;
    }

    [Benchmark(Description = "Ignixa seam: per-call adapter-input evaluation")]
    [BenchmarkCategory("AdapterInputEvaluation")]
    [ArgumentsSource(nameof(Families))]
    public int EvaluateIgnixaAdapterInput(ExpressionFamily family)
    {
        var checksum = 0;
        FirelyContexts contexts = CreateFirelyContexts();
        foreach (EvaluationPlanEntry entry in _plans[family])
        {
            IgnixaElement input = entry.Input.ToIgnixaElement();
            IgnixaEvaluationContext context = CreateIgnixaContext(
                input,
                GetFirelyContext(entry.Fixture, contexts));
            IEnumerable<IgnixaElement> matches = entry.Ignixa.Compiled is not null
                ? entry.Ignixa.Compiled(input, context)
                : _ignixaEvaluator.Evaluate(input, entry.Ignixa.Ast, context);

            foreach (IgnixaElement match in matches)
            {
                var adaptedResult = new TypedElementAdapter(match);
                checksum += ConsumeFirelyResult(adaptedResult);
            }
        }

        return checksum;
    }

    [Benchmark(Description = "Firely to Ignixa: create root adapter")]
    [BenchmarkCategory("AdapterIsolation")]
    [ArgumentsSource(nameof(AdapterFixtures))]
    public IgnixaElement CreateIgnixaRootAdapter(AdapterFixture fixture)
        => GetFixture(fixture).ToIgnixaElement();

    [Benchmark(Description = "Firely to Ignixa: materialize full lazy tree")]
    [BenchmarkCategory("AdapterIsolation")]
    [ArgumentsSource(nameof(AdapterFixtures))]
    public int MaterializeIgnixaAdapter(AdapterFixture fixture)
        => ConsumeIgnixaTree(GetFixture(fixture).ToIgnixaElement());

    private SdkITypedElement GetFixture(AdapterFixture fixture) => fixture switch
    {
        AdapterFixture.Patient => _firelyPatient,
        AdapterFixture.Observation => _firelyObservation,
        AdapterFixture.Appointment => _firelyAppointment,
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown adapter fixture"),
    };

    private static int ConsumeFirelyResults(IEnumerable<SdkITypedElement> results)
    {
        var checksum = 0;
        foreach (SdkITypedElement result in results)
        {
            checksum += ConsumeFirelyResult(result);
        }

        return checksum;
    }

    private static int ConsumeFirelyResult(SdkITypedElement result)
        => (result.InstanceType?.Length ?? 0) + (result.Value is null ? 0 : 1);

    private static int ConsumeIgnixaTree(IgnixaElement element)
    {
        var checksum = element.InstanceType.Length + (element.Value is null ? 0 : 1);
        foreach (IgnixaElement child in element.Children())
        {
            checksum += ConsumeIgnixaTree(child);
        }

        return checksum;
    }
}

public readonly record struct SearchParameterExpressionCorpusSummary(
    int FhirVersions,
    int ShippedSearchParameters,
    int DistinctExpressions,
    int CommonExpressions,
    int IgnixaCompileFailures,
    int FirelyCompileFailures,
    IReadOnlyDictionary<IndexingHeadToHeadBenchmarks.ExpressionFamily, int> EvaluationCounts);
