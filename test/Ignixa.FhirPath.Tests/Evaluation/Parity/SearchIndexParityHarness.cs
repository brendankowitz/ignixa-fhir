using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class SearchIndexParityHarness
{
    private static readonly IReadOnlyDictionary<FhirVersion, Harness> Harnesses =
        new[] { FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6 }
            .ToDictionary(version => version, Create);

    public static SearchIndexComparison Compare(FhirVersion version, string json)
    {
        var harness = Harnesses[version];
        var resource = ResourceJsonNode.Parse(json).ToElement(harness.Schema);
        var ignixa = harness.Ignixa.Extract(resource);
        var firely = harness.Firely.Extract(resource);

        return new SearchIndexComparison(
            SearchIndexCanonicalizer.Canonicalize(
                firely.Entries.Where(entry => harness.CommonExpressions.Contains(entry.SearchParameter.Expression))),
            SearchIndexCanonicalizer.Canonicalize(
                ignixa.Where(entry => harness.CommonExpressions.Contains(entry.SearchParameter.Expression))),
            firely.Failures
                .Where(failure => harness.CommonExpressions.Contains(failure.ParameterExpression))
                .ToArray());
    }

    private static Harness Create(FhirVersion version)
    {
        FirelyEngine.EnsureInitialized();
        var schema = version.GetSchemaProvider();
        var definitions = new SearchParameterDefinitionManager(
            schema,
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var (converters, _) = SearchIndexerFactory.CreateIndexingComponents(
            schema,
            NullFhirBaseUriProvider.Instance);
        var ignixa = SearchIndexerFactory.CreateInstance(
            schema,
            NullLoggerFactory.Instance,
            definitions,
            NullFhirBaseUriProvider.Instance);
        var firely = new FirelySearchIndexer(definitions, converters, schema);
        var commonExpressions = SearchParameterExpressionCorpus.Load(version)
            .CommonExpressions
            .ToHashSet(StringComparer.Ordinal);

        return new Harness(schema, ignixa, firely, commonExpressions);
    }

    private sealed record Harness(
        IFhirSchemaProvider Schema,
        ISearchIndexer Ignixa,
        FirelySearchIndexer Firely,
        IReadOnlySet<string> CommonExpressions);
}
