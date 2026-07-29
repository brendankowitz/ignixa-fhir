using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class ResourceColumnLoweringRuleTests
{
    private static LeafContext ContextResolving(string resourceType, short resourceTypeId, DateTimeOffset? approximationReferenceTime = null)
        => new(
            new SymbolTable(
                new Dictionary<string, short>(),
                new Dictionary<string, short> { [resourceType] = resourceTypeId }),
            approximationReferenceTime);

    private static SearchParameterInfo IdParameter()
        => new("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static SearchParameterInfo TypeParameter()
        => new("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

    [Fact]
    public void GivenAnOrdinaryTokenParameter_WhenTried_ThenReturnsNull()
    {
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)).ShouldBeNull();
    }

    [Fact]
    public void GivenAnIdParameter_WhenTried_ThenComparesResourceId()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceId");
        equal.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenASystemQualifiedIdParameter_WhenTried_ThenThrows()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org", code: "123", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenATypeParameter_WhenTried_ThenComparesResourceTypeIdViaTheResolver()
    {
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Patient", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceTypeId");
        equal.Value.Value.ShouldBe((short)103);
    }

    [Fact]
    public void GivenATypeParameterNamingATypeTheResolverCouldNotFind_WhenTried_ThenLowersToADiagnosablePredicateFalse()
    {
        // Arrange — Resolve records an unfound type as UnmatchableResourceTypeId (-1). Equal(col, -1) is
        // already unsatisfiable, but only Predicate.False carries the reason SearchSqlCompiler reports as a
        // KnownMiss; anything else leaves the miss discoverable only by spotting a magic -1 in the SQL.
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Nonexistent", text: null));
        var context = ContextResolving("Nonexistent", SymbolTable.UnmatchableResourceTypeId);

        // Act
        var result = ResourceColumnLoweringRule.TryLower(predicate, context);

        // Assert
        var unsatisfiable = result.ShouldBeOfType<Predicate.False>();
        unsatisfiable.Reason.ShouldNotBeNull();
        unsatisfiable.Reason.ShouldContain("Nonexistent");
    }

    [Fact]
    public void GivenAnIdParameterWithANotModifier_WhenTried_ThenThrowsRatherThanSilentlyDroppingTheNegation()
    {
        // Arrange -- _id:not=123. Without this guard, the modifier would be silently discarded and
        // this would lower to a POSITIVE match (WHERE ResourceId = '123'), the exact opposite of what
        // :not means -- the same bug class Lower.LowerSearchParameter's own :not handling exists to
        // prevent, just reachable here through the resource-column extraction path instead.
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "123", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenATypeParameterWithANotModifier_WhenTried_ThenThrows()
    {
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "Patient", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    private static SearchParameterInfo LastUpdatedParameter()
        => new("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));

    [Fact]
    public void GivenAnExactInstantLastUpdatedParameter_WhenTried_ThenComparesResourceSurrogateId()
    {
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var value = new DateTimeSearchValue(instant);
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ge, modifier: null, value);

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var ge = result.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("ResourceSurrogateId");
        // 2023-06-15T12:30:00.000Z truncated-to-millisecond ticks, left-shifted 3 bits
        var expectedTicks = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc).Ticks;
        ge.Value.Value.ShouldBe(expectedTicks << 3);
    }

    [Fact]
    public void GivenAPartialPrecisionLastUpdatedParameter_WhenTried_ThenThrows()
    {
        // Arrange -- "_lastUpdated=2023" (year-only precision). DateTimeSearchValue.Parse("2023") runs
        // it through PartialDateTime.Parse (only Year is set) and widens it to a non-degenerate range
        // ([2023-01-01T00:00:00.0000000Z, 2023-12-31T23:59:59.9999999Z]), not a single instant.
        var value = DateTimeSearchValue.Parse("2023");
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Eq, modifier: null, value);

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenALastUpdatedParameterWithAModifier_WhenTried_ThenThrowsRatherThanSilentlyIgnoringIt()
    {
        // Arrange -- no modifier is supported on _lastUpdated yet. Without this guard the modifier
        // would be silently dropped and the query would run as if it were never specified -- the same
        // bug class already found and fixed twice this increment for _id/_type, just for a different
        // parameter and modifier.
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Eq, new SearchModifier(SearchModifierCode.Missing), new DateTimeSearchValue(DateTimeOffset.UtcNow));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    // :ap — _lastUpdated is a single point column (ResourceSurrogateId), unlike date's [Start, End]
    // column pair, so the widened [Start, End] interval from ApproximateDateRange.Widen becomes a
    // between-style AND against that one column: ResourceSurrogateId >= lower AND <= upper, each bound
    // converted through the same ToSurrogateId truncation as the exact-instant Eq/Ge/etc. case above.
    [Fact]
    public void GivenAnApComparatorExactInstantLastUpdatedParameter_WhenTried_ThenComparesWidenedRangeAgainstResourceSurrogateId()
    {
        // Arrange: instant is exactly 1 day before the reference instant -- 1-day gap / 10 = 2h24m
        // tolerance, so widened = [instant - 2h24m, instant + 2h24m]. Both land on whole seconds, so
        // ToSurrogateId's millisecond truncation is a no-op here (covered separately below).
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var referenceTime = new DateTimeOffset(2023, 6, 16, 12, 30, 0, TimeSpan.Zero);
        var value = new DateTimeSearchValue(instant);
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2023, 6, 15, 10, 6, 0, TimeSpan.Zero);
        var widenedEnd = new DateTimeOffset(2023, 6, 15, 14, 54, 0, TimeSpan.Zero);

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103, referenceTime));

        var and = result.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        ge.Column.Column.ShouldBe("ResourceSurrogateId");
        le.Column.Column.ShouldBe("ResourceSurrogateId");
        ge.Value.Value.ShouldBe(widenedStart.UtcTicks << 3);

        // The upper bound must cover the whole boundary millisecond. The database appends a uniquifier of
        // 0-79999 at write time, so comparing against the bare floor would match only the row that drew 0,
        // dropping up to 79,999 resources written in that millisecond.
        le.Value.Value.ShouldBe((widenedEnd.UtcTicks << 3) + 79999);
    }

    [Fact]
    public void GivenAnApComparatorPartialPrecisionLastUpdatedParameter_WhenTried_ThenComparesWidenedRangeAgainstResourceSurrogateId()
    {
        // Arrange: "2023-06" resolves to [Jun 1 00:00:00, Jun 30 23:59:59.9999999]. The proportional term
        // is 36h, but the value's own precision -- one month less one tick -- is larger, so the max()
        // floor selects it and the interval widens by a full month either side. widenedEnd's
        // sub-millisecond remainder is truncated away by ToSurrogateId, landing on .999s -- expressed
        // directly below via the millisecond-precision constructor instead of replicating that math.
        var value = DateTimeSearchValue.Parse("2023-06");
        var referenceTime = new DateTimeOffset(2023, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2023, 5, 2, 0, 0, 0, TimeSpan.Zero);
        var truncatedWidenedEnd = new DateTimeOffset(2023, 7, 30, 23, 59, 59, 999, TimeSpan.Zero);

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103, referenceTime));

        var and = result.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        ge.Column.Column.ShouldBe("ResourceSurrogateId");
        le.Column.Column.ShouldBe("ResourceSurrogateId");
        ge.Value.Value.ShouldBe(widenedStart.UtcTicks << 3);
        le.Value.Value.ShouldBe((truncatedWidenedEnd.UtcTicks << 3) + 79999);
    }

    [Fact]
    public void GivenAnApComparatorLastUpdatedParameterWithNoReferenceTime_WhenTried_ThenThrowsInvalidOperationExceptionNamingSearchSqlCompiler()
    {
        // Arrange -- ApproximateDateRange.Widen (the shared helper Task 3 already covers directly)
        // requires an explicit reference instant; this proves this rule's :ap call site surfaces that
        // same failure rather than swallowing or rewording it.
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ap, modifier: null, value);

        var exception = Should.Throw<InvalidOperationException>(
            () => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
        exception.Message.ShouldContain("SearchSqlCompiler");
    }

    [Fact]
    public void GivenANonApComparatorPartialPrecisionLastUpdatedParameter_WhenTried_ThenStillThrows()
    {
        // Arrange -- proves the partial-precision guard remains in force for every comparator except
        // :ap (e.g. :ge here, distinct from the :eq case already covered above), so :ap's new
        // Widen-based handling can't have accidentally loosened it for the others.
        var value = DateTimeSearchValue.Parse("2023");
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ge, modifier: null, value);

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    /// <summary>
    /// Every comparator whose bound is the top of the millisecond must compare against
    /// <c>floor + 79999</c>, not the bare floor. The database appends a uniquifier drawn from a sequence
    /// declared MAXVALUE 79999, so a bound at the floor addresses only the single resource that happened
    /// to draw 0 and silently drops up to 79,999 others written in that millisecond.
    /// </summary>
    /// <remarks>
    /// Ge, Lt and Eb are deliberately absent: they bound at the bottom of the millisecond, where the
    /// floor is the correct value. "eb" means the resource ends strictly before the instant, so it
    /// belongs with Lt, not with Sa.
    /// </remarks>
    public static TheoryData<SearchComparator> UpperBoundComparators() => new()
    {
        SearchComparator.Eq,
        SearchComparator.Ne,
        SearchComparator.Gt,
        SearchComparator.Sa,
        SearchComparator.Le,
    };

    [Theory]
    [MemberData(nameof(UpperBoundComparators))]
    public void GivenAnExactInstantLastUpdated_WhenLoweredWithAnUpperBoundComparator_ThenTheBoundCoversTheWholeMillisecond(
        SearchComparator comparator)
    {
        // Arrange
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(
            LastUpdatedParameter(), comparator, modifier: null, new DateTimeSearchValue(instant));
        var floor = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc).Ticks << 3;

        // Act
        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        // Assert -- whichever shape the comparator produces, the upper parameter it binds is the last
        // surrogate id in the millisecond.
        var bounds = CollectParameterValues(result.ShouldNotBeNull()).ToArray();
        bounds.ShouldContain(floor + 79999, $"'{comparator}' must bound at the top of the millisecond bucket.");
    }

    [Fact]
    public void GivenAnExactInstantLastUpdated_WhenLoweredWithEqAndNe_ThenTheirBoundsAreIdentical()
    {
        // Arrange -- eq and ne must address exactly the same bucket, or a resource can satisfy both or
        // neither.
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var value = new DateTimeSearchValue(instant);

        // Act
        var eq = ResourceColumnLoweringRule.TryLower(
            new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Eq, null, value),
            ContextResolving("Patient", 103));
        var ne = ResourceColumnLoweringRule.TryLower(
            new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ne, null, value),
            ContextResolving("Patient", 103));

        // Assert
        CollectParameterValues(eq.ShouldNotBeNull()).OrderBy(v => v)
            .ShouldBe(CollectParameterValues(ne.ShouldNotBeNull()).OrderBy(v => v));
    }

    // Surrogate ids stop being encodable around year 3653 (ticks are shifted left 3 bits into an Int64),
    // so anything past that saturates. Saturation has to preserve ordering: the clamp is applied to the
    // instant, not to the encoded result. Clamping the result to long.MaxValue - 79999 lands *below* the
    // floor of the last encodable millisecond, so _lastUpdated=lt9999-12-31 would exclude a resource
    // stored in it -- a lower bound that decreases as the instant increases.
    [Fact]
    public void GivenLastUpdatedInstantsSpanningTheEncodableLimit_WhenLowered_ThenTheSurrogateBoundsAreMonotonic()
    {
        // Arrange — year 3000 encodes normally; years 4000 and DateTimeOffset.MaxValue both saturate
        var withinRange = LastUpdatedLowerBound(new DateTimeOffset(3000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var pastLimit = LastUpdatedLowerBound(new DateTimeOffset(4000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var atMax = LastUpdatedLowerBound(DateTimeOffset.MaxValue);

        // Assert
        pastLimit.ShouldBeGreaterThanOrEqualTo(withinRange);
        atMax.ShouldBe(pastLimit);
    }

    [Fact]
    public void GivenALastUpdatedInstantPastTheEncodableLimit_WhenLowered_ThenTheUpperBoundStillFitsInInt64()
    {
        // Arrange — the saturated floor plus the 79999 uniquifier must not wrap. Wrapping produces a
        // negative upper bound, which inverts every between-style comparison built on it.
        var predicate = new SearchParameterPredicateExpression(
            LastUpdatedParameter(), SearchComparator.Le, modifier: null, new DateTimeSearchValue(DateTimeOffset.MaxValue));

        // Act
        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        // Assert
        var le = result.ShouldBeOfType<Predicate.LessThanOrEqual>();
        var upperBound = (long)le.Value.Value!;
        upperBound.ShouldBeGreaterThan(0);
        upperBound.ShouldBe(LastUpdatedLowerBound(DateTimeOffset.MaxValue) + 79999);
    }

    private static long LastUpdatedLowerBound(DateTimeOffset instant)
    {
        var predicate = new SearchParameterPredicateExpression(
            LastUpdatedParameter(), SearchComparator.Ge, modifier: null, new DateTimeSearchValue(instant));
        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));
        return (long)result.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Value.Value!;
    }

    private static IEnumerable<long> CollectParameterValues(Predicate predicate) => predicate switch
    {
        Predicate.And and => CollectParameterValues(and.Left).Concat(CollectParameterValues(and.Right)),
        Predicate.Or or => CollectParameterValues(or.Left).Concat(CollectParameterValues(or.Right)),
        Predicate.Equal e => [(long)e.Value.Value!],
        Predicate.LessThan lt => [(long)lt.Value.Value!],
        Predicate.LessThanOrEqual le => [(long)le.Value.Value!],
        Predicate.GreaterThan gt => [(long)gt.Value.Value!],
        Predicate.GreaterThanOrEqual ge => [(long)ge.Value.Value!],
        _ => [],
    };
}
