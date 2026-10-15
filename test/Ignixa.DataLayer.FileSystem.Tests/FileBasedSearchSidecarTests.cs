// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// Regressions for the FileSystem search sidecars: at 206143a a resource whose index held a value the compact format could
/// not read back (any composite, so every Observation with a coded quantity value) was silently left out of search, and a
/// sidecar that failed to read was skipped everywhere it was read.
/// </summary>
public sealed class FileBasedSearchSidecarTests : IDisposable
{
    private const string WeightObservation =
        """{"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"29463-7"}],"text":"Body Weight"},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":185,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"}}""";

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private static readonly SearchParameterDefinitionManager Definitions =
        new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), $"ignixa-filesearch-tests-{Guid.NewGuid()}");
    private readonly RecordingLogger<FileBasedFhirRepository> _logger = new();
    private readonly FileBasedFhirRepository _repository;
    private readonly FileBasedSearchService _search;

    public FileBasedSearchSidecarTests()
    {
        _repository = new FileBasedFhirRepository(_baseDirectory, _logger);
        _search = new FileBasedSearchService(_repository, NullLogger<FileBasedSearchService>.Instance, _baseDirectory);
    }

    [Theory]
    [InlineData("code-value-quantity=http://loinc.org|29463-7$gt100")]
    [InlineData("code-value-quantity=http://loinc.org|29463-7$185||lbs")]
    [InlineData("combo-code-value-quantity=29463-7$185")]
    [InlineData("value-quantity=185||lbs")]
    [InlineData("code:text=body")]
    public async Task GivenAnObservationWithACodedQuantity_WhenWrittenAndSearched_ThenItIsFound(string query)
    {
        await WriteIndexedAsync(WeightObservation);

        IReadOnlyList<SearchEntryResult> results = await _search.SearchAsync(Options("Observation", query), CancellationToken.None);

        results.Select(result => result.ResourceId).ShouldBe(["o1"]);
    }

    [Fact]
    public async Task GivenAnObservationWithACodedQuantity_WhenWrittenAndItsIndexReloaded_ThenEveryValueIsAsIndexed()
    {
        IReadOnlyCollection<SearchIndexEntry> written = await WriteIndexedAsync(WeightObservation);

        var (_, reloaded) = (await _repository.GetResourceMetadataAsync("Observation", CancellationToken.None)).ShouldHaveSingleItem();

        reloaded.Select(Describe).ShouldBe(written.Select(Describe));
    }

    [Fact]
    public async Task GivenASidecarThatCannotBeRead_WhenSearched_ThenTheFailureIsLoggedAndSurfaced()
    {
        await WriteIndexedAsync(WeightObservation);
        string sidecar = CorruptSidecar("Observation", "o1");

        await Should.ThrowAsync<JsonException>(() => _search.SearchAsync(Options("Observation", "status=final"), CancellationToken.None).AsTask());

        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Message.Contains(sidecar, StringComparison.Ordinal) && entry.Exception is JsonException);
    }

    [Fact]
    public async Task GivenASidecarThatCannotBeRead_WhenTheResourceIsRead_ThenTheFailureIsSurfacedRatherThanNotFound()
    {
        await WriteIndexedAsync(WeightObservation);
        CorruptSidecar("Observation", "o1");

        await Should.ThrowAsync<JsonException>(() => _repository.GetAsync(new ResourceKey("Observation", "o1")).AsTask());
    }

    [Fact]
    public async Task GivenASidecarThatCannotBeRead_WhenTheResourceIsUpdated_ThenTheWriteFailsRatherThanRestartingAtVersionOne()
    {
        await WriteIndexedAsync(WeightObservation);
        CorruptSidecar("Observation", "o1");

        await Should.ThrowAsync<JsonException>(() => WriteIndexedAsync(WeightObservation));
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    private async Task<IReadOnlyCollection<SearchIndexEntry>> WriteIndexedAsync(string json)
    {
        ISearchIndexer indexer = SearchIndexerFactory.CreateInstance(Schema, NullLoggerFactory.Instance, Definitions, NullFhirBaseUriProvider.Instance);
        var node = ResourceJsonNode.Parse(json);
        IReadOnlyCollection<SearchIndexEntry> index = indexer.Extract(node.ToElement(Schema));
        await _repository.CreateOrUpdateAsync(new ResourceWrapper(
            ResourceType: node.ResourceType,
            ResourceId: node.Id,
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: node,
            Request: new ResourceRequest("PUT", $"{node.ResourceType}/{node.Id}"))
        {
            SearchIndices = [.. index],
        });
        return index;
    }

    private string CorruptSidecar(string resourceType, string id)
    {
        string sidecar = Directory.GetFiles(Path.Combine(_baseDirectory, "_internal", resourceType, id), "*.metadata.json").ShouldHaveSingleItem();
        File.WriteAllText(sidecar, """{"TransactionId":"x","SearchIndexes":{"status":[{"n_s":"FINAL"}]}}""");
        return sidecar;
    }

    private static SearchOptions Options(string resourceType, string query)
    {
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(
                () => Definitions,
                new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema),
                Schema),
            Definitions);
        SearchOptions options = builder.Build(resourceType, new QueryParameterParser().Parse(query), Schema);
        options.UnsupportedParams.ShouldBeEmpty($"'{query}' was not fully parsed");
        return options;
    }

    private static string Describe(SearchIndexEntry entry) => $"{entry.SearchParameter.Code} {Describe(entry.Value)}";

    private static string Describe(ISearchValue value) => value switch
    {
        CompositeIndexSearchValue composite => $"[{string.Join(" $ ", composite.Components.Select(component => string.Join(" , ", component.Select(Describe))))}]",
        TokenSearchValue token => $"token {token.System}|{token.Code} text={token.Text} type={token.IdentifierTypeSystem}|{token.IdentifierTypeCode}",
        QuantitySearchValue quantity => $"quantity {quantity.Low}..{quantity.High} {quantity.System}|{quantity.Code}",
        ReferenceSearchValue reference => $"reference {reference.Kind} {reference.BaseUri} {reference.ResourceType}/{reference.ResourceId}",
        DateTimeSearchValue date => $"date {date.Start:o}..{date.End:o} {date} min={date.IsMin} max={date.IsMax}",
        StringSearchValue text => $"string {text.String} min={text.IsMin} max={text.IsMax}",
        UriSearchValue uri => $"uri {uri.Uri}|{uri.Version}#{uri.Fragment}",
        NumberSearchValue number => $"number {number.Low}..{number.High}",
        _ => throw new InvalidOperationException(value.GetType().Name),
    };
}
