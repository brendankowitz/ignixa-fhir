using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class SearchIndexParityHarness
{
    private static readonly IReadOnlyDictionary<FhirVersion, Harness> Harnesses =
        new[] { FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6 }
            .ToDictionary(version => version, Create);

    /// <summary>
    /// The type <paramref name="version"/> declares for the search parameter at <paramref name="url"/>,
    /// or <see langword="null"/> when that version does not publish it.
    /// </summary>
    /// <remarks>
    /// Resolved against the same definition manager the sweep indexed with, and per version because a URL
    /// is not one search parameter: <c>Location-near</c> is <c>Token</c> under STU3 and <c>Special</c>
    /// from R4 onward, so "what type was this parameter" has no version-free answer.
    /// </remarks>
    public static SearchParamType? ParameterType(FhirVersion version, Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return Harnesses[version].Definitions.TryGetSearchParameter(url, out SearchParameterInfo parameter)
            ? parameter.Type
            : null;
    }

    public static SearchIndexComparison Compare(FhirVersion version, string json)
    {
        var harness = Harnesses[version];
        var resource = ResourceJsonNode.Parse(json).ToElement(harness.Schema);
        IReadOnlyCollection<SearchIndexEntry> ignixa = [];
        var ignixaFailures = IgnixaFailureCapture.While(() => ignixa = harness.Ignixa.Extract(resource));
        var firely = harness.Firely.Extract(resource);

        return new SearchIndexComparison(
            SearchIndexCanonicalizer.Canonicalize(
                firely.Entries.Where(entry => harness.CommonExpressions.Contains(entry.SearchParameter.Expression))),
            SearchIndexCanonicalizer.Canonicalize(
                ignixa.Where(entry => harness.CommonExpressions.Contains(entry.SearchParameter.Expression))),
            firely.Failures
                .Where(failure => harness.CommonExpressions.Contains(failure.ParameterExpression))
                .ToArray(),
            [.. ignixaFailures.Select(failure => failure with { Version = version })]);
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
            IgnixaFailureCapture.Instance,
            definitions,
            NullFhirBaseUriProvider.Instance);
        var firely = new FirelySearchIndexer(definitions, converters, schema);
        var commonExpressions = SearchParameterExpressionCorpus.Load(version)
            .CommonExpressions
            .ToHashSet(StringComparer.Ordinal);

        return new Harness(schema, definitions, ignixa, firely, commonExpressions);
    }

    private sealed record Harness(
        IFhirSchemaProvider Schema,
        ISearchParameterDefinitionManager Definitions,
        ISearchIndexer Ignixa,
        FirelySearchIndexer Firely,
        IReadOnlySet<string> CommonExpressions);
}
