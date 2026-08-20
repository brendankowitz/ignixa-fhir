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
/// Pins that every bound of a composite date component survives into the emitted query.
/// <para>
/// The date component of a Token|DateTime composite is handed to the composite generator as a conjunction
/// of bounds, and <see cref="DateTimeEqualityRewriter"/> makes that conjunction three bounds wide: it opts
/// composites in explicitly, and <c>eq</c>'s containment shape is exactly the pattern it matches, so
/// <c>And(Start &gt;= x, End &lt;= y)</c> reaches the generator as
/// <c>And(Start &gt;= x, Start &lt;= y, End &lt;= y)</c>. Two of those three bounds name the same field, so a
/// generator that keeps one bound per field rather than accumulating them drops the lower bound and asks
/// only <c>Start &lt;= y AND End &lt;= y</c> — a predicate every row that ends before the window satisfies.
/// </para>
/// <para>
/// The failure is silent: it widens the result set rather than erroring, and it widens it only on the
/// rewritten tree, so the unrewritten two-conjunct shape keeps answering correctly and hides it. Both
/// shapes are asserted here for that reason.
/// </para>
/// </summary>
public sealed class TokenDateTimeCompositeBoundsTests : IDisposable
{
    private const short ObservationResourceTypeId = 3;
    private const short CompositeSearchParamId = 11;
    private const string CompositeParameterUri = "http://ignixa.dev/SearchParameter/Observation-code-date";
    private const string IndexedCode = "8480-6";

    /// <summary>Right code, but its date sits years before the window — excluded by the lower bound alone.</summary>
    private const long BeforeWindow = 1;

    /// <summary>Right code, date inside the window. The only correct match.</summary>
    private const long InsideWindow = 2;

    /// <summary>Right code, date after the window — excluded by the upper bound.</summary>
    private const long AfterWindow = 3;

    /// <summary>Date inside the window but the wrong code, so the token component must exclude it.</summary>
    private const long InsideWindowWrongCode = 4;

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _generator;

    public TokenDateTimeCompositeBoundsTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = ObservationResourceTypeId, Name = "Observation" });
        _context.SearchParams.Add(new SearchParamEntity { SearchParamId = CompositeSearchParamId, Uri = CompositeParameterUri, Status = "Enabled" });
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
    public async Task GivenACompositeDateEq_WhenTheIndexRewriteHasAddedASecondStartBound_ThenTheLowerBoundStillExcludesEarlierRows()
    {
        // Arrange - resource 1 carries the searched code on a 2015 date. eq2020 contains it in neither
        // direction, so the lower bound is the only thing standing between it and a false match.

        // Act
        var matches = await MatchAsync(CompositeEqExpression(IndexedCode, "2020"));

        // Assert
        matches.ShouldBe([InsideWindow], "code-date=8480-6$eq2020");
    }

    [Fact]
    public async Task GivenACompositeDateEqBeforeTheIndexRewrite_WhenBothBoundsNameDistinctFields_ThenTheSameRowsMatch()
    {
        // Arrange - the same query on the two-conjunct tree the rewriter has not touched. The rewrite is an
        // index hint and must not change which rows match, so this answer and the rewritten one must agree.

        // Act
        var matches = await MatchAsync(CompositeEqExpression(IndexedCode, "2020", applyIndexRewrite: false));

        // Assert
        matches.ShouldBe([InsideWindow], "code-date=8480-6$eq2020, unrewritten");
    }

    [Fact]
    public async Task GivenACompositeWindowWithNothingIndexedInside_WhenSearchingWithEq_ThenNothingMatches()
    {
        // Arrange - June 2020 sits between resource 2's May date and resource 3's 2021 date, so every row is
        // outside the window on one side or the other and any match at all is a dropped bound.

        // Act
        var matches = await MatchAsync(CompositeEqExpression(IndexedCode, "2020-06"));

        // Assert
        matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenACompositeDateNe_WhenItsOperandsAreADisjunction_ThenTheyAreNotFoldedTogetherWithAnd()
    {
        // Arrange - ne lowers to Or(Start < x, End > y): outside the window on either side is enough. Folding
        // the two operands with AND instead asks for a row that both starts before 2020 and ends after it,
        // which no single-day row can be, so the whole comparator answers empty.

        // Act
        var matches = await MatchAsync(CompositeNeExpression(IndexedCode, "2020"));

        // Assert
        matches.ShouldBe([BeforeWindow, AfterWindow], "code-date=8480-6$ne2020");
    }

    [Fact]
    public async Task GivenACompositeDateEqAndNe_WhenTakenTogether_ThenEveryRowCarryingTheCodeIsAccountedForExactlyOnce()
    {
        // Arrange - eq and ne are complements over the rows the token component admits, so their results must
        // partition that set. Any bound dropped from either side breaks the partition.

        // Act
        var equal = await MatchAsync(CompositeEqExpression(IndexedCode, "2020"));
        var notEqual = await MatchAsync(CompositeNeExpression(IndexedCode, "2020"));

        // Assert
        equal.Intersect(notEqual).ShouldBeEmpty();
        equal.Concat(notEqual).Order().ShouldBe([BeforeWindow, InsideWindow, AfterWindow]);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    private void SeedRows()
    {
        AddRow(BeforeWindow, IndexedCode, 2015, 3, 1);
        AddRow(InsideWindow, IndexedCode, 2020, 5, 5);
        AddRow(AfterWindow, IndexedCode, 2021, 6, 1);
        AddRow(InsideWindowWrongCode, "1234-5", 2020, 5, 5);

        _context.SaveChanges();
    }

    private void AddRow(long surrogateId, string code, int year, int month, int day)
    {
        var start = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        _context.TokenDateTimeCompositeSearchParams.Add(new TokenDateTimeCompositeSearchParamEntity
        {
            ResourceTypeId = ObservationResourceTypeId,
            ResourceSurrogateId = surrogateId,
            SearchParamId = CompositeSearchParamId,
            SystemId1 = null,
            Code1 = code,
            StartDateTime2 = start,
            EndDateTime2 = start.AddDays(1).AddTicks(-1)
        });
    }

    private async Task<IReadOnlyList<long>> MatchAsync(SearchParameterExpression expression)
    {
        var query = await _generator.GenerateQueryAsync(ObservationResourceTypeId, expression, CancellationToken.None);
        return [.. query.ToList().Distinct().Order()];
    }

    private static SearchParameterExpression CompositeNeExpression(string code, string date)
        => CompositeExpression(code, date, SearchComparator.Ne, applyIndexRewrite: true);

    private static SearchParameterExpression CompositeEqExpression(string code, string date, bool applyIndexRewrite = true)
        => CompositeExpression(code, date, SearchComparator.Eq, applyIndexRewrite);

    private static SearchParameterExpression CompositeExpression(string code, string date, SearchComparator comparator, bool applyIndexRewrite)
    {
        var codeParameter = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://ignixa.dev/SearchParameter/Observation-code"));
        var dateParameter = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://ignixa.dev/SearchParameter/Observation-date"));
        var compositeParameter = CompositeParameter(codeParameter, dateParameter);

        var lowered = new SearchParameterExpression(
            compositeParameter,
            LegacyExpressionLowerer.LowerToLegacy(
                Expression.And(
                    new CompositeComponentExpression(
                        codeParameter,
                        0,
                        new SearchParameterPredicateExpression(codeParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null))),
                    new CompositeComponentExpression(
                        dateParameter,
                        1,
                        new SearchParameterPredicateExpression(dateParameter, comparator, modifier: null, new DateTimeSearchValue(PartialDateTime.Parse(date)))))));

        return applyIndexRewrite
            ? (SearchParameterExpression)lowered.AcceptVisitor(DateTimeEqualityRewriter.Instance, null)
            : lowered;
    }

    private static SearchParameterInfo CompositeParameter(SearchParameterInfo codeParameter, SearchParameterInfo dateParameter)
    {
        var components = new[]
        {
            new SearchParameterComponentInfo(codeParameter.Url, "Observation.code") { ResolvedSearchParameter = codeParameter },
            new SearchParameterComponentInfo(dateParameter.Url, "Observation.effective") { ResolvedSearchParameter = dateParameter }
        };

        return new SearchParameterInfo(
            "code-date",
            "code-date",
            SearchParamType.Composite,
            new Uri(CompositeParameterUri),
            components);
    }
}
