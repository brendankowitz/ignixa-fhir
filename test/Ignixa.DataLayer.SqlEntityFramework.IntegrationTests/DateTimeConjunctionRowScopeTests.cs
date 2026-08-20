// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Pins that a date comparator whose predicate is a conjunction of bounds is satisfied by a single index
/// row, not by the union of rows.
/// <para>
/// <c>eq</c> lowers to <c>DateTimeStart &gt;= x AND DateTimeEnd &lt;= y</c> — "the search range contains the
/// target range" — which is a statement about one stored range. Evaluating the conjunction as a set
/// intersection of per-bound resource-id sets instead lets a resource carrying several rows on the same
/// parameter satisfy one bound from one row and the other bound from a different row, so a resource with
/// nothing indexed anywhere near the window matches it. A parameter reaching a repeating element — every
/// <c>Timing.event</c>, <c>CarePlan.activity.detail.scheduled</c>, and so on — indexes exactly that shape.
/// </para>
/// <para>
/// The expression is built the way production does: lowered through <see cref="LegacyExpressionLowerer"/>
/// and then through <see cref="DateTimeEqualityRewriter"/>, which is the pair <c>SearchExpressionQueryBuilder</c>
/// applies before handing the tree to the generator. The rewriter injects a third, redundant bound on
/// containment shapes, so this exercises the 3-conjunct tree the SQL backend actually sees.
/// </para>
/// </summary>
public sealed class DateTimeConjunctionRowScopeTests : IDisposable
{
    private const short ObservationResourceTypeId = 3;
    private const short DateSearchParamId = 8;
    private const string DateParameterUri = "http://hl7.org/fhir/SearchParameter/clinical-date";

    /// <summary>Resource whose two rows sit either side of the window, neither of them inside it.</summary>
    private const long StraddlingPair = 1;

    /// <summary>Resource with one row inside the window.</summary>
    private const long SingleRowInside = 2;

    /// <summary>Resource with two rows, one of them inside the window.</summary>
    private const long PairWithOneInside = 3;

    /// <summary>Resource with one row after the window.</summary>
    private const long SingleRowAfter = 4;

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _generator;

    public DateTimeConjunctionRowScopeTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = ObservationResourceTypeId, Name = "Observation" });
        _context.SearchParams.Add(new SearchParamEntity { SearchParamId = DateSearchParamId, Uri = DateParameterUri, Status = "Enabled" });
        _context.SaveChanges();

        _generator = new SearchParameterQueryGenerator(
            _context,
            _cache,
            NullLogger<SearchParameterQueryGenerator>.Instance,
            new CompositeSearchParameterQueryGenerator(
                _context,
                _cache,
                NullLogger<CompositeSearchParameterQueryGenerator>.Instance));

        SeedRows();
    }

    [Fact]
    public async Task GivenAResourceWhoseRowsBracketTheWindow_WhenSearchingWithEq_ThenNeitherRowSatisfiesTheWholePredicateAndItDoesNotMatch()
    {
        // Arrange — resource 1 indexes 2019-03-01 and 2021-06-01. date=eq2020 asks for a row inside
        // [2020-01-01, 2020-12-31]; the 2021 row clears the lower bound and the 2019 row clears the upper
        // bound, but no row clears both. Resources 2 and 3 each own a row that does.

        // Act
        var matches = await MatchAsync(EqExpression("2020"));

        // Assert
        matches.ShouldBe([SingleRowInside, PairWithOneInside], "date=eq2020");
    }

    [Fact]
    public async Task GivenAResourceWhoseRowsBracketTheWindow_WhenSearchingWithEqBeforeTheIndexRewrite_ThenItStillDoesNotMatch()
    {
        // Arrange — the same claim about the two-conjunct tree, without DateTimeEqualityRewriter's third
        // redundant bound. The rewrite is an index hint and must not be what makes the answer correct: were
        // row scoping to depend on it, disabling the hint would silently widen every eq date search.

        // Act
        var matches = await MatchAsync(EqExpression("2020", applyIndexRewrite: false));

        // Assert
        matches.ShouldBe([SingleRowInside, PairWithOneInside], "date=eq2020, unrewritten");
    }

    [Fact]
    public async Task GivenAWindowBetweenEveryIndexedRow_WhenSearchingWithEq_ThenNothingMatches()
    {
        // Arrange — June 2020 falls into the gap in resources 1 (2019 / 2021) and 3 (2018 / 2020-07), and
        // resource 2's only row is May. Nothing is indexed inside the window, so the correct answer is empty
        // and any match at all is manufactured by pairing bounds across rows.

        // Act
        var matches = await MatchAsync(EqExpression("2020-06"));

        // Assert
        matches.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    private void SeedRows()
    {
        AddRow(StraddlingPair, 2019, 3, 1);
        AddRow(StraddlingPair, 2021, 6, 1);
        AddRow(SingleRowInside, 2020, 5, 5);
        AddRow(PairWithOneInside, 2018, 1, 1);
        AddRow(PairWithOneInside, 2020, 7, 7);
        AddRow(SingleRowAfter, 2021, 6, 1);

        _context.SaveChanges();
    }

    private void AddRow(long surrogateId, int year, int month, int day)
    {
        var start = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        _context.DateTimeSearchParams.Add(new DateTimeSearchParamEntity
        {
            ResourceTypeId = ObservationResourceTypeId,
            ResourceSurrogateId = surrogateId,
            SearchParamId = DateSearchParamId,
            StartDateTime = start,
            EndDateTime = start.AddDays(1).AddTicks(-1)
        });
    }

    private async Task<IReadOnlyList<long>> MatchAsync(SearchParameterExpression expression)
    {
        var query = await _generator.GenerateQueryAsync(ObservationResourceTypeId, expression, CancellationToken.None);
        return [.. query.ToList().Distinct().Order()];
    }

    private static SearchParameterExpression EqExpression(string date, bool applyIndexRewrite = true)
    {
        var parameter = DateParameter();
        var value = new DateTimeSearchValue(PartialDateTime.Parse(date));

        var lowered = new SearchParameterExpression(
            parameter,
            LegacyExpressionLowerer.LowerToLegacy(
                new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value)));

        return applyIndexRewrite
            ? (SearchParameterExpression)lowered.AcceptVisitor(DateTimeEqualityRewriter.Instance, null)
            : lowered;
    }

    private static SearchParameterInfo DateParameter() =>
        new("date", "date", SearchParamType.Date, new Uri(DateParameterUri));
}
