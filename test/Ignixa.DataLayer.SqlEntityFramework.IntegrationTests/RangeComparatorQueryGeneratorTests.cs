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
/// Exercises the live query path — the expression builder, then <see cref="SearchParameterQueryGenerator"/> —
/// against seeded rows whose LowValue differs from HighValue.
/// <para>
/// Every plain <c>valueQuantity</c> or number indexes to a point row (LowValue = HighValue), and on a point row
/// all six ordering comparators' two candidate columns agree. A generator that re-derives the column from the
/// operator therefore passes every point-valued test while implementing <c>sa</c> for <c>gt</c> and <c>eb</c>
/// for <c>lt</c>. Only a row that straddles the search value separates them, which is what these rows are for.
/// </para>
/// <para>
/// The row/comparator matrix mirrors the one
/// <c>Ignixa.Search.Sql.Tests.Lowering.RangeComparatorSemanticsTests</c> pins over the compiler's lowering, so
/// both backends are demonstrably held to one table.
/// </para>
/// </summary>
public sealed class RangeComparatorQueryGeneratorTests : IDisposable
{
    private const short ObservationResourceTypeId = 3;
    private const short NumberSearchParamId = 6;
    private const short QuantitySearchParamId = 7;
    private const string ValueNumberParameterUri = "http://hl7.org/fhir/SearchParameter/Observation-value-number";
    private const string ValueQuantityParameterUri = "http://hl7.org/fhir/SearchParameter/Observation-value-quantity";
    private const string UnitsOfMeasure = "http://unitsofmeasure.org";
    private const decimal SearchValue = 5.4m;

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _generator;

    public RangeComparatorQueryGeneratorTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = ObservationResourceTypeId, Name = "Observation" });
        _context.SearchParams.AddRange(
            new SearchParamEntity { SearchParamId = NumberSearchParamId, Uri = ValueNumberParameterUri, Status = "Enabled" },
            new SearchParamEntity { SearchParamId = QuantitySearchParamId, Uri = ValueQuantityParameterUri, Status = "Enabled" });
        _context.SaveChanges();

        _generator = new SearchParameterQueryGenerator(
            _context,
            _cache,
            NullLogger<SearchParameterQueryGenerator>.Instance,
            new CompositeSearchParameterQueryGenerator(
                _context,
                _cache,
                NullLogger<CompositeSearchParameterQueryGenerator>.Instance));

        SeedRanges();
    }

    /// <summary>
    /// The stored ranges, each identified by the surrogate id the expected match sets name. Rows 1-8 are the
    /// ordering matrix; rows 9-14 add the cases that separate containment from overlap, since the <c>eq</c>
    /// window for 5.4 is [5.35, 5.45].
    /// </summary>
    public static TheoryData<string, decimal, decimal, long> SeededRows() => new()
    {
        { "range straddling the search value", 5.0m, 6.0m, 1 },
        { "range entirely above", 6.0m, 7.0m, 2 },
        { "range entirely below", 4.0m, 5.0m, 3 },
        { "range touching from above", 5.4m, 7.0m, 4 },
        { "range touching from below", 4.0m, 5.4m, 5 },
        { "point at the search value", 5.4m, 5.4m, 6 },
        { "point above", 6.0m, 6.0m, 7 },
        { "point below", 5.0m, 5.0m, 8 },
        { "point on the lower window edge", 5.35m, 5.35m, 9 },
        { "point on the upper window edge", 5.45m, 5.45m, 10 },
        { "range strictly inside the window", 5.36m, 5.44m, 11 },
        { "range exactly spanning the window", 5.35m, 5.45m, 12 },
        { "range straddling the lower window edge", 5.30m, 5.40m, 13 },
        { "range straddling the upper window edge", 5.40m, 5.50m, 14 },
    };

    /// <summary>
    /// The FHIR prefix table over parameter value 5.4 and stored range [Low, High]: <c>gt</c> is High &gt; v,
    /// <c>ge</c> is High &gt;= v, <c>lt</c> is Low &lt; v, <c>le</c> is Low &lt;= v, <c>sa</c> is Low &gt; v,
    /// <c>eb</c> is High &lt; v. <c>gt</c> and <c>sa</c> emit the same operator against different columns, as
    /// do <c>lt</c> and <c>eb</c> — that is the pair a shared switch arm silently merged.
    /// </summary>
    public static TheoryData<SearchComparator, long[]> OrderingComparatorMatches() => new()
    {
        { SearchComparator.Gt, [1, 2, 4, 7, 10, 11, 12, 14] },
        { SearchComparator.Ge, [1, 2, 4, 5, 6, 7, 10, 11, 12, 13, 14] },
        { SearchComparator.Lt, [1, 3, 5, 8, 9, 11, 12, 13] },
        { SearchComparator.Le, [1, 3, 4, 5, 6, 8, 9, 11, 12, 13, 14] },
        { SearchComparator.Sa, [2, 7, 10] },
        { SearchComparator.Eb, [3, 8, 9] },
    };

    [Theory]
    [MemberData(nameof(OrderingComparatorMatches))]
    public async Task GivenRangeValuedNumberRows_WhenSearchingWithAnOrderingComparator_ThenTheBoundNamedByTheSpecIsCompared(
        SearchComparator comparator, long[] expected)
    {
        // Arrange
        var expression = NumberExpression(comparator);

        // Act
        var matches = await MatchAsync(expression);

        // Assert
        matches.ShouldBe(expected, $"value-number={comparator} {SearchValue}");
    }

    [Theory]
    [MemberData(nameof(OrderingComparatorMatches))]
    public async Task GivenRangeValuedQuantityRows_WhenSearchingWithAnOrderingComparator_ThenTheBoundNamedByTheSpecIsCompared(
        SearchComparator comparator, long[] expected)
    {
        // Arrange
        var expression = QuantityExpression(comparator);

        // Act
        var matches = await MatchAsync(expression);

        // Assert
        matches.ShouldBe(expected, $"value-quantity={comparator} {SearchValue}");
    }

    [Fact]
    public async Task GivenARowStraddlingTheSearchValue_WhenSearchingWithGtAndSa_ThenTheMatchSetsDiffer()
    {
        // Arrange — row 1 stores [5.0, 6.0]. gt asks whether the row reaches above 5.4 (it does, up to 6.0);
        // sa asks whether the whole range starts after 5.4 (it does not, it starts at 5.0).

        // Act
        var gt = await MatchAsync(NumberExpression(SearchComparator.Gt));
        var sa = await MatchAsync(NumberExpression(SearchComparator.Sa));

        // Assert
        gt.ShouldContain(1L);
        sa.ShouldNotContain(1L);
        gt.ShouldNotBe(sa);
    }

    [Fact]
    public async Task GivenARowStraddlingTheSearchValue_WhenSearchingWithLtAndEb_ThenTheMatchSetsDiffer()
    {
        // Arrange — the mirror of the gt/sa case: [5.0, 6.0] reaches below 5.4 but does not end before it.

        // Act
        var lt = await MatchAsync(NumberExpression(SearchComparator.Lt));
        var eb = await MatchAsync(NumberExpression(SearchComparator.Eb));

        // Assert
        lt.ShouldContain(1L);
        eb.ShouldNotContain(1L);
        lt.ShouldNotBe(eb);
    }

    [Fact]
    public async Task GivenARowStraddlingTheSearchValue_WhenSearchingWithGeAndLe_ThenBothMatchIt()
    {
        // Arrange — ge and le constrain the far bound, so a range containing 5.4 satisfies both. Comparing
        // against the near bound instead inverts them and one of the two drops the row.

        // Act
        var ge = await MatchAsync(NumberExpression(SearchComparator.Ge));
        var le = await MatchAsync(NumberExpression(SearchComparator.Le));

        // Assert
        ge.ShouldContain(1L);
        le.ShouldContain(1L);
    }

    [Fact]
    public async Task GivenRangeValuedNumberRows_WhenSearchingWithEq_ThenOnlyRowsContainedByThePrecisionWindowMatch()
    {
        // Arrange — eq for 5.4 widens to [5.35, 5.45] and requires the stored range to sit inside it.

        // Act
        var matches = await MatchAsync(NumberExpression(SearchComparator.Eq));

        // Assert
        matches.ShouldBe([6L, 9L, 10L, 11L, 12L]);
    }

    [Fact]
    public async Task GivenRangeValuedQuantityRows_WhenSearchingWithEq_ThenOnlyRowsContainedByThePrecisionWindowMatch()
    {
        // Arrange — quantity shares the number range semantics; the eq window is the same [5.35, 5.45].

        // Act
        var matches = await MatchAsync(QuantityExpression(SearchComparator.Eq));

        // Assert
        matches.ShouldBe([6L, 9L, 10L, 11L, 12L]);
    }

    [Theory]
    [MemberData(nameof(SeededRowIds))]
    public async Task GivenAnySeededNumberRow_WhenSearchingWithEqAndNe_ThenExactlyOneMatchesIt(string scenario, long surrogateId)
    {
        // Arrange — ne is the exact negation of eq's containment, so the two must partition every row. A ne
        // lowered as "disjoint from the window" leaves rows that merely overlap it matched by neither.

        // Act
        var eq = await MatchAsync(NumberExpression(SearchComparator.Eq));
        var ne = await MatchAsync(NumberExpression(SearchComparator.Ne));

        // Assert
        (eq.Contains(surrogateId) ^ ne.Contains(surrogateId))
            .ShouldBeTrue($"{scenario}: eq and ne must partition every row");
    }

    [Fact]
    public async Task GivenARowEnclosingTheSearchValue_WhenSearchingWithNe_ThenItMatchesBecauseItIsNotContained()
    {
        // Arrange — row 1 stores [5.0, 6.0], which encloses 5.4 without being contained by [5.35, 5.45].

        // Act
        var matches = await MatchAsync(NumberExpression(SearchComparator.Ne));

        // Assert
        matches.ShouldContain(1L);
    }

    [Fact]
    public async Task GivenRangeValuedNumberRows_WhenSearchingWithAp_ThenTheWidenedWindowIsOverlappedNotContained()
    {
        // Arrange — ap widens to max(precision, |5.4| * 0.1) = 0.54, giving [4.86, 5.94], and the spec defines
        // ap as "the range of the search value overlaps with the range of the target value". Only rows 2 and 7
        // ([6.0, 7.0] and the point 6.0) sit entirely above the window and are excluded.
        //
        // This test previously asserted containment, dropping rows 1, 3, 4 and 5 — every row that straddles a
        // window edge. That was the live behaviour, since this EF path answers real requests, so ap was
        // silently under-matching in production. See NumericRangeComparisonSemantics.

        // Act
        var matches = await MatchAsync(NumberExpression(SearchComparator.Ap));

        // Assert
        matches.ShouldBe([1L, 3L, 4L, 5L, 6L, 8L, 9L, 10L, 11L, 12L, 13L, 14L]);
    }

    [Fact]
    public async Task GivenRangeValuedQuantityRows_WhenSearchingWithAp_ThenTheWidenedWindowIsOverlappedNotContained()
    {
        // Arrange — quantity shares the number range semantics, so the ap window is the same [4.86, 5.94].

        // Act
        var matches = await MatchAsync(QuantityExpression(SearchComparator.Ap));

        // Assert
        matches.ShouldBe([1L, 3L, 4L, 5L, 6L, 8L, 9L, 10L, 11L, 12L, 13L, 14L]);
    }

    [Fact]
    public async Task GivenAQuantityCarryingSystemAndCode_WhenSearchingWithGt_ThenTheHighBoundIsCompared()
    {
        // Arrange — a quantity carrying system and code takes the fused single-row AND path
        // (GenerateQuantityAndQueryAsync) instead of the per-field dispatch, so the column choice has to be
        // right there too. Only rows 1-3 are tagged with the system/code.
        await TagQuantityRowsWithUnitAsync(1, 2, 3);
        var expression = QuantityExpression(SearchComparator.Gt, UnitsOfMeasure, "mg");

        // Act
        var matches = await MatchAsync(expression);

        // Assert — rows 1 ([5.0, 6.0]) and 2 ([6.0, 7.0]) reach above 5.4; row 3 ([4.0, 5.0]) does not.
        matches.ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task GivenAQuantityCarryingSystemAndCode_WhenSearchingWithSa_ThenTheLowBoundIsCompared()
    {
        // Arrange — the same fused path, proving gt and sa stay distinct on it as well.
        await TagQuantityRowsWithUnitAsync(1, 2, 3);
        var expression = QuantityExpression(SearchComparator.Sa, UnitsOfMeasure, "mg");

        // Act
        var matches = await MatchAsync(expression);

        // Assert — only row 2 ([6.0, 7.0]) starts after 5.4.
        matches.ShouldBe([2L]);
    }

    [Fact]
    public async Task GivenAQuantityCarryingSystemAndCode_WhenSearchingWithNe_ThenTheValueFilterStillApplies()
    {
        // Arrange — ne lowers to an OR of the two window bounds nested inside the system/code AND. Collecting
        // only conjuncts dropped that disjunction, so the value filter vanished and every tagged row matched;
        // recursing into the OR and conjoining it instead inverted ne into "matches nothing". Tag one row
        // outside the precision window (1, ne-match) and one inside it (6, not ne), plus another outside (4).
        await TagQuantityRowsWithUnitAsync(1, 4, 6);
        var expression = QuantityExpression(SearchComparator.Ne, UnitsOfMeasure, "mg");

        // Act
        var matches = await MatchAsync(expression);

        // Assert — window is [5.35, 5.45]. Rows 1 ([5.0, 6.0]) and 4 ([5.4, 7.0]) fall outside it; row 6
        // ([5.4, 5.4]) sits inside and is the one value ne excludes.
        matches.ShouldBe([1L, 4L]);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    public static TheoryData<string, long> SeededRowIds()
    {
        var data = new TheoryData<string, long>();

        foreach (var row in SeededRows())
        {
            data.Add((string)row[0]!, (long)row[3]!);
        }

        return data;
    }

    private void SeedRanges()
    {
        foreach (var row in SeededRows())
        {
            var low = (decimal)row[1]!;
            var high = (decimal)row[2]!;
            var surrogateId = (long)row[3]!;

            _context.NumberSearchParams.Add(new NumberSearchParamEntity
            {
                ResourceTypeId = ObservationResourceTypeId,
                ResourceSurrogateId = surrogateId,
                SearchParamId = NumberSearchParamId,
                LowValue = low,
                HighValue = high
            });

            _context.QuantitySearchParams.Add(new QuantitySearchParamEntity
            {
                ResourceTypeId = ObservationResourceTypeId,
                ResourceSurrogateId = surrogateId,
                SearchParamId = QuantitySearchParamId,
                LowValue = low,
                HighValue = high
            });
        }

        _context.SaveChanges();
    }

    private async Task TagQuantityRowsWithUnitAsync(params long[] surrogateIds)
    {
        var system = new SystemEntity { Value = UnitsOfMeasure };
        var quantityCode = new QuantityCodeEntity { Value = "mg" };
        _context.Systems.Add(system);
        _context.QuantityCodes.Add(quantityCode);
        await _context.SaveChangesAsync();

        foreach (var surrogateId in surrogateIds)
        {
            var row = await _context.QuantitySearchParams.SingleAsync(sp => sp.ResourceSurrogateId == surrogateId);
            row.SystemId = system.SystemId;
            row.QuantityCodeId = quantityCode.QuantityCodeId;
        }

        await _context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<long>> MatchAsync(SearchParameterExpression expression)
    {
        var query = await _generator.GenerateQueryAsync(ObservationResourceTypeId, expression, CancellationToken.None);
        return [.. query.ToList().Order()];
    }

    private static SearchParameterExpression NumberExpression(SearchComparator comparator)
        => Lower(NumberParameter(), comparator, new NumberSearchValue(SearchValue));

    private static SearchParameterExpression QuantityExpression(SearchComparator comparator, string? system = null, string? code = null)
        => Lower(QuantityParameter(), comparator, new QuantitySearchValue(system, code, SearchValue));

    /// <summary>
    /// Builds the expression the way production does: the typed predicate is lowered through
    /// <see cref="LegacyExpressionLowerer"/>, which is what <c>SearchExpressionQueryBuilder</c> calls before
    /// handing the tree to the generator. Hand-writing the field-level tree here would test the generator
    /// against an oracle nothing produces.
    /// </summary>
    private static SearchParameterExpression Lower(SearchParameterInfo parameter, SearchComparator comparator, ISearchValue value)
        => new(
            parameter,
            LegacyExpressionLowerer.LowerToLegacy(
                new SearchParameterPredicateExpression(parameter, comparator, modifier: null, value)));

    private static SearchParameterInfo NumberParameter() =>
        new("value-number", "value-number", SearchParamType.Number, new Uri(ValueNumberParameterUri));

    private static SearchParameterInfo QuantityParameter() =>
        new("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri(ValueQuantityParameterUri));
}
