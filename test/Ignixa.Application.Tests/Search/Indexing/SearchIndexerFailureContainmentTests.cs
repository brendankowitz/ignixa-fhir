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
/// Laziness is what makes this easy to get wrong. The named range from
/// <see cref="GivenATimingWithAnUnparseableEvent_WhenIndexed_ThenTheWriteSurvivesAndOtherParametersStillIndex"/>
/// through <see cref="GivenACompositeWhoseComponentExpressionFails_WhenIndexed_ThenTheWholeCompositeEntryIsDroppedAndTheComponentDefinitionIsLogged"/>
/// spans six tests, and only three of them exist to pin the laziness hazard (issue #403):
/// <see cref="GivenANonCompositeWhoseExpressionFails_WhenIndexed_ThenTheFailureIsLoggedAndSiblingParametersStillIndex"/>,
/// <see cref="GivenACompositeWhoseRootExpressionFails_WhenIndexed_ThenTheFailureIsLoggedAndSiblingParametersStillIndex"/>
/// and <see cref="GivenACompositeWhoseComponentExpressionFails_WhenIndexed_ThenTheWholeCompositeEntryIsDroppedAndTheComponentDefinitionIsLogged"/>.
/// <c>element.Select</c> hands back the evaluator's own enumerable, which for anything built on <c>where()</c>
/// is a <c>yield</c> iterator that does no work until enumerated; <c>ProcessCompositeSearchParameter</c> is a
/// <c>yield</c> iterator too. A value produced inside a try block therefore does nothing until it is
/// enumerated, and the enumeration happens further out - past the catch that looked like it covered it - so
/// one bad expression fails the create or update of the whole resource. Each of those three tests is built
/// from an expression chosen so that <c>Select</c> returns <em>without</em> throwing and the throw lands on
/// enumeration - an expression that threw eagerly inside <c>Select</c> would satisfy every other assertion
/// those tests make while the guarded <c>.ToList()</c> was deleted and the bug fully reintroduced.
/// </para>
/// <para>
/// The remaining three in that range cover containment without touching laziness.
/// <see cref="GivenATimingWithAnUnparseableEvent_WhenIndexed_ThenTheWriteSurvivesAndOtherParametersStillIndex"/>
/// and <see cref="GivenATimingWithAnUnparseableEvent_WhenIndexed_ThenTheFailureIsLoggedAsExpectedNotUnexpected"/>
/// both use a malformed <c>Timing.event</c> literal whose failure lives inside
/// <c>TimingToDateTimeSearchValueConverter.ConvertTo</c> - a separate <c>yield</c> iterator downstream of
/// <c>Select</c>, not the FHIRPath <c>where()</c> iterator itself, as the first test's own comment says.
/// <see cref="GivenATimingWithAParseableEvent_WhenIndexed_ThenTheOccurrenceParameterStillIndexes"/> is the
/// control for those two: its literal parses cleanly, so nothing throws anywhere and it exists only to prove
/// containment comes from catching the failure, not from the occurrence parameter having quietly stopped
/// producing entries.
/// </para>
/// <para>
/// The seventh test,
/// <see cref="GivenACustomSearchParameterWhoseExpressionNeverTerminates_WhenIndexed_ThenTheIterationGuardIsLoggedAsExpectedNotUnexpectedAndSiblingParametersStillIndex"/>,
/// sits outside that range and pins a different, unrelated property - the log tier a guard exception lands
/// in (issues #428 and #433). Its expression throws eagerly inside <c>Select</c> itself, so it provides no
/// coverage of the #403 laziness guard; its own doc comment says why that is fine for its purpose.
/// </para>
/// <para>
/// <strong>A <c>repeat()</c> sibling of that test was deleted rather than kept.</strong> It indexed against
/// <c>status.repeat($this &amp; 'x')</c> and cost 35 seconds against its <c>repeatAll</c> sibling's 503
/// milliseconds, because tripping <c>Repeat</c>'s cap is Θ(cap²) with a recursing deep-equality comparator.
/// It carried no marginal information: <c>ElementSearchIndexer.IsExpectedEvaluationFailure</c> is a pure
/// type test, both guards throw <c>FhirPathEvaluationException</c>, so nothing it could assert would
/// distinguish the two - and the two facts it composed are each pinned more cheaply elsewhere.
/// <c>RemainingCoverageTests</c> pins that <c>Repeat</c>'s guard throws that type; the test above pins that
/// the type routes to Warning.
/// Do not restore it - but not because of the clock. The 35 seconds is historical: it is what tripping
/// <c>Repeat</c>'s <em>iteration</em> cap cost while that cap was the only guard, and #435's
/// comparison-count budget now trips first on a constructing projection. What is missing is the
/// information, not the time. The cap's <em>value</em> is pinned by
/// <c>RemainingCoverageTests.GivenAWideFocusOfDeepEqualItems_WhenRepeat_ThenTheProductionIterationCapIsExactlyTenThousand</c>,
/// from both sides and at one comparison per iteration - not, as this comment once claimed, as a
/// by-product of paying the Θ(cap²) cost, which nothing there does any more.
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
        // Arrange - pins the materialisation inside ExtractSearchValues, the non-composite call site. The
        // expression parses and compiles cleanly, so Select returns a lazy where() iterator and the type error
        // (string + integer) only surfaces on enumeration. Delete the .ToList() from that try and the throw
        // escapes the per-parameter catch and aborts Extract for the whole Observation.
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenNonComposite()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real and it is lazy: Select returns cleanly, then enumerating what it returned throws.
        IEnumerable<IElement> lazyValues = Should.NotThrow(() => element.Select(BrokenNonCompositeExpression));
        Should.Throw<Exception>(() => lazyValues.ToList());

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
        // Arrange - pins the materialisation inside ProcessCompositeSearchParameter, the composite root call
        // site. That method is itself a yield iterator, so a lazy root enumerable escaping it would not be
        // enumerated until Extract's entries.AddRange - outside ISearchIndexer.Extract entirely.
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenRootComposite()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real and it is lazy: Select returns cleanly, then enumerating what it returned throws.
        IEnumerable<IElement> lazyRootObjects = Should.NotThrow(() => element.Select(BrokenRootExpression));
        Should.Throw<Exception>(() => lazyRootObjects.ToList());

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

    [Fact]
    public void GivenACompositeWhoseComponentExpressionFails_WhenIndexed_ThenTheWholeCompositeEntryIsDroppedAndTheComponentDefinitionIsLogged()
    {
        // Arrange - pins the materialisation inside ExtractCompositeComponentSearchValues, the one call site
        // the tests above cannot reach: they fail at the composite's root expression, so rootObjects is empty
        // and the per-component loop never runs. Here the root expression is valid and the first component
        // succeeds, so the loop does run and the second component fails during enumeration.
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([BrokenComponentComposite()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // The hazard is real and it is lazy: Select returns cleanly, then enumerating what it returned throws.
        // Evaluated against the element itself because the valid root expression yields the resource.
        IEnumerable<IElement> lazyComponentValues = Should.NotThrow(() => element.Select(BrokenComponentExpression));
        Should.Throw<Exception>(() => lazyComponentValues.ToList());

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert - the failure is attributed to the component's resolved definition, not to the composite that
        // declared it, so a test asserting the composite's own URL would miss a regression at this call site.
        var failureLog = captured.Single(c => c.Message.Contains(BrokenComponentExpression, StringComparison.Ordinal));
        failureLog.Level.ShouldBe(LogLevel.Warning);
        failureLog.Message.ShouldContain(BrokenComponentDefinitionUrl);
        failureLog.Message.ShouldNotContain(BrokenComponentCompositeUrl);
        failureLog.Message.ShouldContain("Observation/o1");

        // Containment here is coarser than the non-composite case: an empty component result trips the
        // "no values for this component" check, which skips the entire composite entry for that root object -
        // discarding the first component's successful values with it. Only the composite entry is lost; the
        // parameters its components resolve to still index on their own.
        indices.Count(i => i.SearchParameter.Code == BrokenComponentCompositeCode).ShouldBe(0);
        indices.Count(i => i.SearchParameter.Code == "status").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "value-quantity").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code-value-quantity").ShouldBe(1);
    }

    [Fact]
    public void GivenACustomSearchParameterWhoseExpressionNeverTerminates_WhenIndexed_ThenTheIterationGuardIsLoggedAsExpectedNotUnexpectedAndSiblingParametersStillIndex()
    {
        // Arrange - repeatAll($this) is a deliberate infinite loop, not a typo: the projection is the identity,
        // so every item repeatAll() dequeues re-projects itself back onto the queue and the queue never drains.
        // CollectionFunctions.RepeatAll trips its own 100_000-iteration guard and throws FhirPathEvaluationException
        // (PR #427 converted this from a bare InvalidOperationException; issue #428 is about which log tier that
        // move landed the guard in). The guard is tripped by a tenant-supplied expression against a tenant-supplied
        // resource shape, deterministically, on demand - the same class of failure as a malformed literal
        // (FormatException) or an unsupported function (NotSupportedException), both of which IsExpectedEvaluationFailure
        // already routes to Warning. It must not surface as UnexpectedExtractionFailure (Error): that tier is
        // reserved for indexer or converter code defects (NullReferenceException, InvalidCastException), not for a
        // guard the evaluator raises on purpose against input it does not control. A bare InvalidOperationException
        // staying out of the expected set is correct and is not what this test is pinning - the guard's exception
        // type changed, not its cause, and IsExpectedEvaluationFailure must follow the type, not special-case it.
        // repeatAll appears in none of the generated *SearchParameterDefinitions.g.cs files, so this path is
        // reachable only from a tenant-authored custom search parameter, never from base FHIR search parameters.
        //
        // This test pins tier assignment (#428) only. It deliberately does not cover the issue #403 laziness
        // guard the class doc describes: RepeatAll's iteration check throws inside the loop that Select()
        // itself drives to build its result, so the exception surfaces eagerly from element.Select(...) and
        // never reaches a lazy ToList() further out. Deleting the materialising .ToList() from the try block
        // this test's call site uses would not un-catch this exception, so it does not exercise that guard.
        var captured = new List<(LogLevel Level, string Message)>();
        _searchParameterDefinitionManager.AddNewSearchParameters([NeverTerminatingSearchParameter()]);
        var indexer = CreateIndexer(new CapturingLoggerFactory(captured));
        var observation = ObservationJson();
        var element = observation.ToElement(_schemaProvider);

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert - logged as an expected data/expression-level miss (Warning) naming the parameter, never as an
        // indexer defect (Error), and containment is per-parameter: every sibling parameter still indexes.
        var failureLog = captured.Single(c => c.Message.Contains(NeverTerminatingExpression, StringComparison.Ordinal));
        failureLog.Level.ShouldBe(LogLevel.Warning);
        failureLog.Message.ShouldContain(NeverTerminatingUrl);
        failureLog.Message.ShouldContain("Observation/o1");
        captured.ShouldNotContain(c => c.Level == LogLevel.Error);

        indices.Count(i => i.SearchParameter.Code == NeverTerminatingCode).ShouldBe(0);
        indices.Count(i => i.SearchParameter.Code == "status").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "code").ShouldBe(1);
        indices.Count(i => i.SearchParameter.Code == "value-quantity").ShouldBe(1);
    }

    private const string BrokenNonCompositeCode = "broken-non-composite";
    private const string BrokenNonCompositeExpression = "Observation.code.coding.where(system + 1 = 'non-composite')";
    private const string BrokenNonCompositeUrl = "http://example.org/fhir/SearchParameter/broken-non-composite";
    private const string BrokenCompositeCode = "broken-root-composite";
    private const string BrokenRootExpression = "Observation.code.coding.where(system + 2 = 'composite-root')";
    private const string BrokenCompositeUrl = "http://example.org/fhir/SearchParameter/broken-root-composite";
    private const string BrokenComponentCompositeCode = "broken-component-composite";
    private const string BrokenComponentExpression = "code.coding.where(system + 3 = 'composite-component')";
    private const string BrokenComponentCompositeUrl = "http://example.org/fhir/SearchParameter/broken-component-composite";

    /// <summary>
    /// The definition the broken component resolves to. ExtractCompositeComponentSearchValues logs this URL
    /// rather than the URL of the composite that declared the component.
    /// </summary>
    private const string BrokenComponentDefinitionUrl = "http://hl7.org/fhir/SearchParameter/clinical-code";

    private const string NeverTerminatingCode = "repeat-all-never-terminates";
    private const string NeverTerminatingExpression = "repeatAll($this)";
    private const string NeverTerminatingUrl = "http://example.org/fhir/SearchParameter/repeat-all-never-terminates";

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

    /// <summary>
    /// A composite whose root expression is valid and whose first component succeeds, so extraction reaches the
    /// second component - the only way to exercise ExtractCompositeComponentSearchValues, which the tests that
    /// fail at the root expression never reach.
    /// </summary>
    private IElement BrokenComponentComposite()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{BrokenComponentCompositeCode}}",
              "url": "{{BrokenComponentCompositeUrl}}",
              "name": "{{BrokenComponentCompositeCode}}",
              "status": "active",
              "code": "{{BrokenComponentCompositeCode}}",
              "base": [ "Observation" ],
              "type": "composite",
              "expression": "Observation",
              "component": [
                {
                  "definition": "http://hl7.org/fhir/SearchParameter/Observation-value-quantity",
                  "expression": "value.ofType(Quantity)"
                },
                {
                  "definition": "{{BrokenComponentDefinitionUrl}}",
                  "expression": "{{BrokenComponentExpression}}"
                }
              ]
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }

    private IElement NeverTerminatingSearchParameter()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{NeverTerminatingCode}}",
              "url": "{{NeverTerminatingUrl}}",
              "name": "{{NeverTerminatingCode}}",
              "status": "active",
              "code": "{{NeverTerminatingCode}}",
              "base": [ "Observation" ],
              "type": "string",
              "expression": "{{NeverTerminatingExpression}}"
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
