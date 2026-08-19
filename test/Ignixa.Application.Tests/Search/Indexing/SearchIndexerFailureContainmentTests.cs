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
    public void GivenATimingWithAnUnparseableEvent_WhenIndexed_ThenTheFailureIsLoggedAsExpectedNotUnexpected()
    {
        // Arrange - same malformed Timing as the containment test above. PartialDateTime.Parse throws
        // FormatException for "not-a-date" (see PartialDateTime.cs), which is the textbook "expected" case
        // ConvertOrLog's own doc comment names: a bad literal in patient data, not a converter defect.
        // IsExpectedEvaluationFailure must route it through the Warning-level ConverterFailed message, not
        // the Error-level UnexpectedConverterFailure message reserved for genuine bugs (NullReferenceException,
        // InvalidCastException) - otherwise every malformed Timing.event in real data logs an Error per element.
        var captured = new List<(LogLevel Level, string Message)>();
        ILoggerFactory capturingLoggerFactory = new CapturingLoggerFactory(captured);

        var indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            capturingLoggerFactory,
            _searchParameterDefinitionManager,
            NullFhirBaseUriProvider.Instance);

        var serviceRequest = ServiceRequestWithTiming("""{"event":["not-a-date"]}""");
        var element = serviceRequest.ToElement(_schemaProvider);

        // Act
        indexer.Extract(element);

        // Assert - logged as an expected data-quality miss (Warning), never as a converter defect (Error).
        captured.ShouldContain(c => c.Level == LogLevel.Warning);
        captured.ShouldNotContain(c => c.Level == LogLevel.Error);
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
    public void GivenANonCompositeWhoseExpressionFails_WhenIndexed_ThenTheFailureIsLoggedAndSiblingParametersStillIndex()
    {
        // Arrange
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenNonComposite()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real: evaluating the non-composite expression against this resource throws.
        Should.Throw<Exception>(() => element.Select(BrokenNonCompositeExpression).ToList());

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert
        var failureLog = captured.Single(c => c.Message.Contains(BrokenNonCompositeExpression, StringComparison.Ordinal));
        failureLog.Level.ShouldBe(LogLevel.Warning);
        failureLog.Message.ShouldContain(BrokenNonCompositeUrl);
        failureLog.Message.ShouldContain("Observation/o1");

        indices.Count(i => i.SearchParameter.Code == BrokenNonCompositeCode).ShouldBe(0);
        indices.Count(i => i.SearchParameter.Code == "status").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "value-quantity").ShouldBe(1);
    }

    [Fact]
    public void GivenACompositeWhoseRootExpressionFails_WhenIndexed_ThenTheFailureIsLoggedAndSiblingParametersStillIndex()
    {
        // Arrange
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenRootComposite()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real: evaluating that root expression against this resource does throw.
        Should.Throw<Exception>(() => element.Select(BrokenRootExpression).ToList());

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert
        var failureLog = captured.Single(c => c.Message.Contains(BrokenRootExpression, StringComparison.Ordinal));
        failureLog.Level.ShouldBe(LogLevel.Warning);
        failureLog.Message.ShouldContain(BrokenCompositeUrl);
        failureLog.Message.ShouldContain("Observation/o1");

        indices.Count(i => i.SearchParameter.Code == BrokenCompositeCode).ShouldBe(0);
        indices.Count(i => i.SearchParameter.Code == "status").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "value-quantity").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code-value-quantity").ShouldBe(1);
    }

    private const string BrokenNonCompositeCode = "broken-non-composite";
    private const string BrokenNonCompositeExpression = "Observation.value.ofType(Quantity) + 'not-a-number'";
    private const string BrokenNonCompositeUrl = "http://example.org/fhir/SearchParameter/broken-non-composite";
    private const string BrokenCompositeCode = "broken-root-composite";
    private const string BrokenRootExpression = "Observation.value.ofType(Quantity) + 'not-a-number'";
    private const string BrokenCompositeUrl = "http://example.org/fhir/SearchParameter/broken-root-composite";

    private IElement BrokenNonComposite()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{BrokenNonCompositeCode}}",
              "url": "{{BrokenNonCompositeUrl}}",
              "name": "{{BrokenNonCompositeCode}}",
              "status": "active",
              "code": "{{BrokenNonCompositeCode}}",
              "base": [ "Observation" ],
              "type": "string",
              "expression": "{{BrokenNonCompositeExpression}}"
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }

    private IElement BrokenRootComposite()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{BrokenCompositeCode}}",
              "url": "{{BrokenCompositeUrl}}",
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

    private ISearchIndexer CreateIndexer(ILoggerFactory loggerFactory)
        => SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            loggerFactory,
            _searchParameterDefinitionManager,
            NullFhirBaseUriProvider.Instance);

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

/// <summary>
/// Test helper: records every log entry's level and formatted message instead of discarding them, so a
/// test can assert on severity (e.g. that a data-quality miss logs at Warning, never Error) rather than
/// only on whether logging happened at all.
/// </summary>
internal sealed class CapturingLoggerFactory(List<(LogLevel Level, string Message)> captured) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(captured);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<(LogLevel Level, string Message)> captured) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            captured.Add((logLevel, formatter(state, exception)));
        }
    }
}
