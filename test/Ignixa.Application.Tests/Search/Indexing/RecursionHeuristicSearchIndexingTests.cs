// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Falsification test for issue #454's search-indexing impact: <c>Encounter-location</c> indexed
/// nothing because <c>SchemaAwareElement</c>'s name-equality recursion heuristic mistyped
/// <c>Encounter.location.location</c> as the backbone <c>Encounter.Location</c> instead of
/// <c>Reference</c>, so the converter pipeline never found a converter for it and the search
/// parameter silently produced an empty index for every Encounter.
/// </summary>
public class RecursionHeuristicSearchIndexingTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly ISearchIndexer _indexer;

    public RecursionHeuristicSearchIndexingTests()
    {
        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            NullLoggerFactory.Instance,
            new SearchParameterDefinitionManager(_schemaProvider, new NullLogger<SearchParameterDefinitionManager>()),
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenEncounterWithPopulatedLocationLocation_WhenIndexed_ThenEncounterLocationProducesAReferenceSearchValue()
    {
        // Arrange
        var encounterJson = """
            {"resourceType":"Encounter","id":"enc1","status":"in-progress",
             "class":{"code":"AMB"},
             "location":[{"location":{"reference":"Location/loc1"},"status":"active"}]}
            """;
        var element = JsonSourceNodeFactory.Parse(encounterJson).ToElement(_schemaProvider);

        // Act
        var entries = _indexer.Extract(element)
            .Where(entry => entry.SearchParameter.Code == "location")
            .Select(entry => entry.Value)
            .OfType<ReferenceSearchValue>()
            .ToArray();

        // Assert
        var reference = entries.ShouldHaveSingleItem();
        reference.ResourceType.ShouldBe("Location");
        reference.ResourceId.ShouldBe("loc1");
    }
}
