// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Indexing;

public class ReferenceIdentifierIndexingTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly ISearchIndexer _indexer;

    public ReferenceIdentifierIndexingTests()
    {
        var manager = new SearchParameterDefinitionManager(
            _schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            NullLoggerFactory.Instance,
            manager,
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenIdentifierOnlyReference_WhenIndexing_ThenDerivedTokenEntryIsProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "subject:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/mrn");
        token.Code.ShouldBe("1234");
        entries.ShouldNotContain(e => e.SearchParameter.Code == "subject" && e.Value is ReferenceSearchValue);
    }

    [Fact]
    public void GivenReferenceWithLiteralAndIdentifier_WhenIndexing_ThenReferenceAndDerivedTokenEntriesAreProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "reference": "Patient/123",
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.Any(e => e.SearchParameter.Code == "subject" && e.Value is ReferenceSearchValue).ShouldBeTrue();
        entries.Any(e =>
            e.SearchParameter.Code == "subject:identifier" &&
            e.Value is TokenSearchValue token &&
            token.System == "http://example.org/mrn" &&
            token.Code == "1234").ShouldBeTrue();
    }

    [Fact]
    public void GivenReferenceIdentifierWithOnlySystem_WhenIndexing_ThenDerivedTokenEntryIsNotProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "identifier": {
                "system": "http://example.org/mrn"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.ShouldNotContain(e => e.SearchParameter.Code == "subject:identifier");
    }

    private IElement CreateEncounter(string subject)
    {
        string json = $$"""
            {
              "resourceType": "Encounter",
              "status": "planned",
              "class": {
                "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                "code": "AMB"
              },
              "subject": {{subject}}
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }
}
