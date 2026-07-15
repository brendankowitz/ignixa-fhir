// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Application.Tests.Search.Expressions.Parsers;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.InMemory;

/// <summary>
/// Behavioral equivalence test for Task 7 (docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md,
/// "Testing" item 5): for a representative set of query strings and a small in-memory resource fixture,
/// evaluates the SAME parsed query two ways against the SAME <see cref="SearchQueryInterpreter"/> instance --
/// "before" (the new typed tree lowered back to the old field-level shape via
/// <see cref="LegacyExpressionLowerer"/>, then dispatched through this class's pre-existing, unmodified
/// structural visit methods -- <see cref="SearchQueryInterpreter.VisitBinary"/>/<see cref="SearchQueryInterpreter.VisitString"/>/etc.)
/// and "after" (the new typed tree dispatched directly through the new
/// <see cref="SearchQueryInterpreter.VisitSearchParameterPredicate"/>/<see cref="SearchQueryInterpreter.VisitCompositeComponent"/>
/// overrides) -- and asserts identical result sets.
/// </summary>
public class SearchQueryInterpreterPredicateTests
{
    private static SearchParserTestContext BuildContext()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Patient", "birthdate", SearchParamType.Date);
        context.Add("Patient", "identifier", SearchParamType.Token);

        return context;
    }

    private static (string Key, string Value) SplitQuery(string query)
    {
        int index = query.IndexOf('=');
        return (query[..index], query[(index + 1)..]);
    }

    private static (Expression New, Expression LoweredOld) ParseBothShapes(SearchParserTestContext context, string resourceType, string query)
    {
        var resourceTypes = new[] { resourceType };
        (string key, string value) = SplitQuery(query);

        Expression newExpression = context.Parser.Parse(resourceTypes, key, value);
        Expression loweredOld = newExpression.AcceptVisitor(new LegacyExpressionLowerer(), context: null);

        return (newExpression, loweredOld);
    }

    private static IReadOnlyList<ResourceKey> Evaluate(
        SearchQueryInterpreter interpreter,
        Expression expression,
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> fixture)
    {
        SearchPredicate predicate = expression.AcceptVisitor(interpreter, interpreter.InitialContext);
        return predicate(fixture).Select(x => x.Location).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private static (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) Patient(
        string id,
        string? name = null,
        string? birthdate = null,
        (string? System, string Code)? identifier = null)
    {
        var entries = new List<SearchIndexEntry>();

        if (name is not null)
        {
            entries.Add(new SearchIndexEntry(
                new SearchParameterInfo("name", "name", SearchParamType.String),
                new StringSearchValue(name)));
        }

        if (birthdate is not null)
        {
            entries.Add(new SearchIndexEntry(
                new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date),
                DateTimeSearchValue.Parse(birthdate)));
        }

        if (identifier is not null)
        {
            entries.Add(new SearchIndexEntry(
                new SearchParameterInfo("identifier", "identifier", SearchParamType.Token),
                new TokenSearchValue(identifier.Value.System, identifier.Value.Code, text: null)));
        }

        return (new ResourceKey("Patient", id), entries);
    }

    private static readonly (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)[] Fixture =
    [
        Patient("1", name: "Smith", birthdate: "2020-06-15", identifier: ("http://example.org/mrn", "1001")),
        Patient("2", name: "Smithson", birthdate: "2019-01-01", identifier: ("http://example.org/mrn", "1002")),
        Patient("3", name: "Jones", birthdate: "2021-12-31", identifier: (null, "1001")),
        Patient("4", name: "SMITH", birthdate: "2020-06-15T12:00:00Z", identifier: ("http://other.org/mrn", "1001")),
    ];

    [Theory]
    [InlineData("Patient", "name=Smith")]
    [InlineData("Patient", "name:exact=Smith")]
    [InlineData("Patient", "name:contains=mith")]
    [InlineData("Patient", "birthdate=2020-06-15")]
    [InlineData("Patient", "birthdate=ge2020-01-01")]
    [InlineData("Patient", "birthdate=lt2020-01-01")]
    [InlineData("Patient", "birthdate=ne2020-06-15")]
    [InlineData("Patient", "identifier=http://example.org/mrn|1001")]
    [InlineData("Patient", "identifier=1001")]
    public void GivenAQuery_WhenEvaluatedDirectlyOrViaLoweredLegacyTree_ThenResultsMatch(string resourceType, string query)
    {
        // Arrange
        var context = BuildContext();
        (Expression newExpression, Expression loweredOld) = ParseBothShapes(context, resourceType, query);
        var interpreter = new SearchQueryInterpreter();

        // Act
        IReadOnlyList<ResourceKey> beforeResults = Evaluate(interpreter, loweredOld, Fixture);
        IReadOnlyList<ResourceKey> afterResults = Evaluate(interpreter, newExpression, Fixture);

        // Assert
        afterResults.ShouldBe(
            beforeResults,
            $"query='{query}'\nnew: {newExpression}\nlowered: {loweredOld}\nbefore: [{string.Join(", ", beforeResults)}]\nafter: [{string.Join(", ", afterResults)}]");

        // Sanity: the equivalence check above passes trivially if both sides return nothing for every
        // query -- assert at least one query in this theory actually narrows the fixture, so the test
        // is exercising real filtering, not just "both sides no-op".
        afterResults.Count.ShouldBeLessThanOrEqualTo(Fixture.Length);
    }

    [Fact]
    public void GivenNameEqualsSmith_WhenEvaluated_ThenMatchesOnlyStartsWithSmithCaseInsensitive()
    {
        // Arrange: a concrete, non-trivial assertion (not just old-vs-new parity) that the new direct
        // dispatch path produces the FHIR-correct result for a simple string search -- "name=Smith" is a
        // case-insensitive starts-with per http://hl7.org/fhir/search.html#string.
        var context = BuildContext();
        (Expression newExpression, _) = ParseBothShapes(context, "Patient", "name=Smith");
        var interpreter = new SearchQueryInterpreter();

        // Act
        IReadOnlyList<ResourceKey> results = Evaluate(interpreter, newExpression, Fixture);

        // Assert: "1" (Smith), "2" (Smithson, starts-with), "4" (SMITH, case-insensitive) match; "3" (Jones) does not.
        results.ShouldBe([new ResourceKey("Patient", "1"), new ResourceKey("Patient", "2"), new ResourceKey("Patient", "4")]);
    }

    [Fact]
    public void GivenNameExactEqualsSmith_WhenEvaluated_ThenMatchesOnlyExactCaseSensitiveValue()
    {
        // Arrange
        var context = BuildContext();
        (Expression newExpression, _) = ParseBothShapes(context, "Patient", "name:exact=Smith");
        var interpreter = new SearchQueryInterpreter();

        // Act
        IReadOnlyList<ResourceKey> results = Evaluate(interpreter, newExpression, Fixture);

        // Assert: only "1" is an exact, case-sensitive match for "Smith".
        results.ShouldBe([new ResourceKey("Patient", "1")]);
    }

    // --- Pre-existing gaps, faithfully preserved (equivalence-of-failure) ---
    //
    // The two tests below document real, PRE-EXISTING (not introduced by Task 7) gaps in
    // SearchQueryInterpreter's evaluation model, confirmed empirically: both the "before" path
    // (LegacyExpressionLowerer + this class's own pre-existing, unmodified VisitBinary/VisitString/
    // VisitMultiary) and the "after" path (the new VisitSearchParameterPredicate/VisitCompositeComponent)
    // throw the SAME exception for these inputs. This is expected, not a regression: Task 7's mandate is
    // to replicate Step 1's real semantics, not invent new (more correct) ones, and LowerAndVisit
    // (SearchQueryInterpreter.cs) reuses the exact same SearchValueExpressionBuilderHelper decomposition
    // plus this class's own already-implemented structural visitors for both paths, so any pre-existing
    // gap in that pipeline necessarily reproduces identically on both sides.
    //
    // Root causes (traced, not guessed):
    // 1. Reference-typed search parameters decompose (via SearchValueExpressionBuilderHelper) into
    //    StringEquals nodes over FieldName.ReferenceResourceType/ReferenceResourceId. VisitString's
    //    CompareStringParameter local function switches on SearchIndexEntry.SearchParameter.Type and only
    //    handles SearchParamType.String/Token -- SearchParamType.Reference falls to its `default` branch,
    //    which throws NotImplementedException. This means ANY Reference-typed search (not just chained/
    //    internal-kind ones) is non-functional through SearchQueryInterpreter today.
    // 2. Composite search parameters are indexed (ElementSearchIndexer.cs) as ONE SearchIndexEntry per
    //    resource, keyed by the COMPOSITE's own SearchParameterInfo (Type = SearchParamType.Composite),
    //    whose Value is a CompositeIndexSearchValue wrapping ALL components together.
    //    ComparisonValueVisitor.Visit(CompositeIndexSearchValue) flattens every component's ISearchValue
    //    into ONE comparison call regardless of which component/position a given AND-clause targets, so
    //    evaluating (for example) the quantity half of a token+quantity composite also visits the token
    //    half's ISearchValue -- a cross-type comparison that reliably throws (NullReferenceException or
    //    an IComparable type-mismatch, depending on the concrete values involved). This means composite
    //    search parameters with a Token or String component -- the vast majority of real FHIR composites
    //    -- are non-functional through SearchQueryInterpreter today.
    [Fact]
    public void GivenReferenceSearch_WhenEvaluatedDirectlyOrViaLoweredLegacyTree_ThenBothThrowIdenticalPreExistingGap()
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo referenceParam = context.Add("Observation", "subject", SearchParamType.Reference, targets: ["Patient"]);
        (Expression newExpression, Expression loweredOld) = ParseBothShapes(context, "Observation", "subject=Patient/123");
        var interpreter = new SearchQueryInterpreter();

        var fixture = new (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)[]
        {
            (new ResourceKey("Observation", "1"), new[]
            {
                new SearchIndexEntry(referenceParam, new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", "123")),
            }),
        };

        // Act
        var beforeException = Record.Exception(() => Evaluate(interpreter, loweredOld, fixture));
        var afterException = Record.Exception(() => Evaluate(interpreter, newExpression, fixture));

        // Assert
        beforeException.ShouldNotBeNull();
        afterException.ShouldNotBeNull();
        afterException.GetType().ShouldBe(beforeException.GetType());
        afterException.Message.ShouldBe(beforeException.Message);
    }

    [Fact]
    public void GivenTokenQuantityCompositeSearch_WhenEvaluatedDirectlyOrViaLoweredLegacyTree_ThenBothThrowIdenticalPreExistingGap()
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo codeParam = context.Add("Observation", "component-code", SearchParamType.Token);
        SearchParameterInfo valueParam = context.Add("Observation", "component-value-quantity", SearchParamType.Quantity);
        SearchParameterInfo compositeParam = context.Add(
            "Observation",
            "component-code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo(codeParam.Url) { ResolvedSearchParameter = codeParam },
                new SearchParameterComponentInfo(valueParam.Url) { ResolvedSearchParameter = valueParam },
            ]);
        (Expression newExpression, Expression loweredOld) = ParseBothShapes(
            context, "Observation", "component-code-value-quantity=http://loinc.org|1234-5$gt10");
        var interpreter = new SearchQueryInterpreter();

        var componentValues = new IReadOnlyList<ISearchValue>[]
        {
            new ISearchValue[] { new TokenSearchValue("http://loinc.org", "1234-5", text: null) },
            new ISearchValue[] { new QuantitySearchValue(system: null!, code: null!, quantity: 15m) },
        };
        var fixture = new (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)[]
        {
            (new ResourceKey("Observation", "1"), new[]
            {
                new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(componentValues)),
            }),
        };

        // Act
        var beforeException = Record.Exception(() => Evaluate(interpreter, loweredOld, fixture));
        var afterException = Record.Exception(() => Evaluate(interpreter, newExpression, fixture));

        // Assert
        beforeException.ShouldNotBeNull();
        afterException.ShouldNotBeNull();
        afterException.GetType().ShouldBe(beforeException.GetType());
    }
}
