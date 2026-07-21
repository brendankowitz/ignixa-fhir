using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Generated;

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[CsvExporter(CsvSeparator.Comma)]
[MarkdownExporterAttribute.GitHub]
public class SearchExpressionParserBenchmarks
{
    private static readonly string[] s_patient = ["Patient"];
    private static readonly string[] s_observation = ["Observation"];

    private IExpressionParser _parser = null!;

    [ParamsAllValues]
    public SearchParserBenchmarkCase Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        IFhirSchemaProvider schemaProvider = new R4CoreSchemaProvider();
        ISearchParameterDefinitionManager definitionManager = new BenchmarkSearchParameterDefinitionManager();
        IReferenceSearchValueParser referenceSearchValueParser = new ReferenceSearchValueParser(schemaProvider);
        ISearchParameterExpressionParser searchParameterExpressionParser = new SearchParameterExpressionParser(
            referenceSearchValueParser,
            schemaProvider);

        _parser = new ExpressionParser(
            () => definitionManager,
            searchParameterExpressionParser,
            schemaProvider);

        foreach (SearchParserBenchmarkCase benchmarkCase in Enum.GetValues<SearchParserBenchmarkCase>())
        {
            _ = Parse(benchmarkCase);
        }
    }

    [Benchmark]
    public Expression Parse()
    {
        return Parse(Case);
    }

    private Expression Parse(SearchParserBenchmarkCase benchmarkCase)
    {
        return benchmarkCase switch
        {
            SearchParserBenchmarkCase.Simple => _parser.Parse(s_patient, "name", "Smith"),
            SearchParserBenchmarkCase.Modified => _parser.Parse(s_patient, "name:exact", "Smith"),
            SearchParserBenchmarkCase.TypedChain => _parser.Parse(s_observation, "subject:Patient.name", "Smith"),
            SearchParserBenchmarkCase.NestedReverseChain => _parser.Parse(
                s_observation,
                "patient:Patient._has:Group:member:_tag",
                "http://example.org/tags|reviewed"),
            SearchParserBenchmarkCase.EscapedAlternative => _parser.Parse(
                s_observation,
                "code",
                @"http://example.org|a\,b,http://example.org|c"),
            SearchParserBenchmarkCase.Composite => _parser.Parse(
                s_observation,
                "code-value-quantity",
                "http://loinc.org|8480-6$gt120,29463-7$lt80"),
            _ => throw new UnreachableException()
        };
    }
}
