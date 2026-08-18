// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins the blast radius of an indexing failure to the one search parameter that caused it.
/// <para>
/// <see cref="ISearchIndexer.Extract"/> runs on the write path, so anything escaping it fails the create or
/// update outright. One malformed literal or one bad custom expression must therefore cost that parameter's
/// index entries and nothing else - the resource still stores, and every other parameter still indexes.
/// </para>
/// <para>
/// Laziness is what makes this easy to get wrong. Both <c>ProcessCompositeSearchParameter</c> and the
/// converters are <c>yield</c> iterators, so a value produced inside a try block does no work until it is
/// enumerated, and the enumeration happens further out - past the catch that looked like it covered it. Each
/// test below asserts the underlying operation genuinely throws before asserting that Extract survives it,
/// so neither can quietly stop testing anything if the hazard is refactored away.
/// </para>
/// </summary>
public class SearchIndexerFailureContainmentTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ISearchIndexer _indexer;

    public SearchIndexerFailureContainmentTests()
    {
        _searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            _loggerFactory,
            _searchParameterDefinitionManager,
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenATimingWithAnUnparseableEvent_WhenIndexed_ThenTheWriteSurvivesAndOtherParametersStillIndex()
    {
        // Arrange - ServiceRequest.occurrenceTiming is a date search parameter, so the Timing converter runs
        // on it and PartialDateTime.Parse meets the malformed literal. The converter is a yield iterator, so
        // that parse used to happen at the indexer's results.AddRange, outside the try around element.Select.
        var serviceRequest = ServiceRequestWithTiming("""{"event":["not-a-date"]}""");
        var element = serviceRequest.ToElement(_schemaProvider);

        // The hazard is real: the converter itself still rejects the literal.
        var timing = element.Select("occurrence").ShouldHaveSingleItem();
        Should.Throw<Exception>(() => new TimingToDateTimeSearchValueConverter().ConvertTo(timing).ToList());

        // Act
        var indices = Should.NotThrow(() => _indexer.Extract(element));

        // Assert - the occurrence parameter contributes nothing, and everything else is unaffected.
        indices.ShouldNotBeEmpty();
        indices.Select(i => i.SearchParameter.Code).ShouldContain("status");
        indices.Select(i => i.SearchParameter.Code).ShouldNotContain("occurrence");
    }

    [Fact]
    public void GivenATimingWithAParseableEvent_WhenIndexed_ThenTheOccurrenceParameterStillIndexes()
    {
        // Arrange - the control for the test above. Containment must come from catching the failure, not from
        // the occurrence parameter having quietly stopped producing entries for every ServiceRequest.
        var serviceRequest = ServiceRequestWithTiming("""{"event":["2015-03-09"]}""");
        var element = serviceRequest.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        indices.Select(i => i.SearchParameter.Code).ShouldContain("occurrence");
    }

    [Fact]
    public void GivenACompositeWhoseRootExpressionFails_WhenIndexed_ThenTheWriteSurvivesAndOtherParametersStillIndex()
    {
        // Arrange - a custom composite whose root expression cannot be evaluated. ProcessCompositeSearchParameter
        // is a yield iterator, so the root Select's error surfaced at Extract's entries.AddRange and propagated
        // straight out of ISearchIndexer.Extract, failing the whole write over one bad custom parameter.
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenRootComposite()]);

        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real: evaluating that root expression against this resource does throw.
        Should.Throw<Exception>(() => element.Select(BrokenRootExpression).ToList());

        // Act
        var indices = Should.NotThrow(() => _indexer.Extract(element));

        // Assert
        indices.ShouldNotBeEmpty();
        indices.Select(i => i.SearchParameter.Code).ShouldContain("status");
        indices.Select(i => i.SearchParameter.Code).ShouldNotContain(BrokenCompositeCode);
    }

    private const string BrokenCompositeCode = "broken-root-composite";

    private const string BrokenRootExpression = "Observation.value.ofType(Quantity) + 'not-a-number'";

    private IElement BrokenRootComposite()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{BrokenCompositeCode}}",
              "url": "http://example.org/fhir/SearchParameter/{{BrokenCompositeCode}}",
              "name": "{{BrokenCompositeCode}}",
              "status": "active",
              "code": "{{BrokenCompositeCode}}",
              "base": [ "Observation" ],
              "type": "composite",
              "expression": "{{BrokenRootExpression}}",
              "component": [
                {
                  "definition": "http://hl7.org/fhir/SearchParameter/clinical-code",
                  "expression": "code"
                },
                {
                  "definition": "http://hl7.org/fhir/SearchParameter/Observation-value-quantity",
                  "expression": "value.ofType(Quantity)"
                }
              ]
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }

    private static ResourceJsonNode ServiceRequestWithTiming(string timingJson)
        => ResourceJsonNode.Parse($$"""
            {"resourceType":"ServiceRequest","id":"s1","status":"active","intent":"order",
             "subject":{"reference":"Patient/p1"},
             "occurrenceTiming":{{timingJson}}}
            """);

    private static ResourceJsonNode ObservationJson()
        => ResourceJsonNode.Parse("""
            {"resourceType":"Observation","id":"o1","status":"final",
             "code":{"coding":[{"system":"http://loinc.org","code":"9272-6"}]},
             "subject":{"reference":"Patient/p1"},
             "valueQuantity":{"value":10,"unit":"{score}","system":"http://unitsofmeasure.org","code":"{score}"}}
            """);
}
