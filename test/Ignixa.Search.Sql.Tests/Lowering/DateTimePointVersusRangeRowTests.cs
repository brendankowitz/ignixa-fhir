using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Answers, against the real lowered predicate rather than by reading the prefix table, when the two index
/// shapes a temporal element can take actually change a search result.
/// </summary>
/// <remarks>
/// <para>
/// An <c>instant</c> is indexed as a zero-width point and a <c>dateTime</c> written to the same second is
/// indexed as the whole of that second — the FHIR search specification requires exactly that
/// (https://hl7.org/fhir/R4/search.html#date: an instant is "an interval with an effective width of 0",
/// a dateTime is "the range of the value as defined above"). The obvious worry is that the two shapes make
/// otherwise-identical resources answer the same query differently.
/// </para>
/// <para>
/// They do, but only when a search bound falls strictly INSIDE the indexed second, which needs a sub-second
/// search literal. At second precision or coarser — which is every literal a server echoes back, and so
/// every cursor a client pages with — the two shapes are indistinguishable under all nine prefixes. That
/// is the claim these tests hold down, in both directions, so that "instant indexes differently" is not
/// mistaken for "instant searches differently".
/// </para>
/// </remarks>
public class DateTimePointVersusRangeRowTests
{
    private const string Second = "2015-02-07T13:28:17Z";

    private static readonly SearchComparator[] AllComparators =
    [
        SearchComparator.Eq,
        SearchComparator.Ne,
        SearchComparator.Lt,
        SearchComparator.Gt,
        SearchComparator.Le,
        SearchComparator.Ge,
        SearchComparator.Sa,
        SearchComparator.Eb,
        SearchComparator.Ap,
    ];

    [Theory]
    [InlineData(Second)]
    [InlineData("2015-02-07T13:28")]
    [InlineData("2015-02-07")]
    [InlineData("2015-02")]
    [InlineData("2015")]
    public void GivenASearchValueNoFinerThanASecond_WhenEveryPrefixIsLowered_ThenThePointAndRangeRowsAgree(
        string searchLiteral)
    {
        // Arrange -- the two rows the same wire literal produces depending on whether its element is typed
        // instant or dateTime.
        var (point, range) = RowsFor(Second);
        var searchValue = DateTimeSearchValue.Parse(searchLiteral);

        foreach (var comparator in AllComparators)
        {
            // Act
            var pointVerdict = DateRowMatcher.Matches(comparator, searchValue, point);
            var rangeVerdict = DateRowMatcher.Matches(comparator, searchValue, range);

            // Assert
            pointVerdict.ShouldBe(
                rangeVerdict,
                $"{comparator} disagreed between the point and range rows for '{searchLiteral}'");
        }
    }

    [Fact]
    public void GivenAnEqSearchAtSecondPrecision_WhenLowered_ThenBothRowsMatch()
    {
        // The specific prediction worth killing: that a second-precision instant is missed by an eq search
        // written at the same second. It is not -- eq is containment of the row's range within the search
        // value's range, and a zero-width point sitting on the search range's own lower bound is contained
        // just as the full second is.

        // Arrange
        var (point, range) = RowsFor(Second);
        var searchValue = DateTimeSearchValue.Parse(Second);

        // Act & Assert
        DateRowMatcher.Matches(SearchComparator.Eq, searchValue, point).ShouldBeTrue();
        DateRowMatcher.Matches(SearchComparator.Eq, searchValue, range).ShouldBeTrue();
    }

    [Fact]
    public void GivenASubSecondGtBoundInsideTheIndexedSecond_WhenLowered_ThenOnlyTheRangeRowMatches()
    {
        // Where the divergence does become observable, and where the spec wants it to: half a second into
        // 13:28:17, a dateTime written as "13:28:17" still has time left to run, while an instant written
        // identically has already happened.

        // Arrange
        var (point, range) = RowsFor(Second);
        var searchValue = DateTimeSearchValue.Parse("2015-02-07T13:28:17.5Z");

        // Act & Assert
        DateRowMatcher.Matches(SearchComparator.Gt, searchValue, point).ShouldBeFalse();
        DateRowMatcher.Matches(SearchComparator.Gt, searchValue, range).ShouldBeTrue();
    }

    [Fact]
    public void GivenASubSecondEbBoundInsideTheIndexedSecond_WhenLowered_ThenOnlyThePointRowMatches()
    {
        // The mirror image: "ended before 13:28:17.5" is true of the instant and false of the dateTime,
        // for the same reason and in the opposite direction.

        // Arrange
        var (point, range) = RowsFor(Second);
        var searchValue = DateTimeSearchValue.Parse("2015-02-07T13:28:17.5Z");

        // Act & Assert
        DateRowMatcher.Matches(SearchComparator.Eb, searchValue, point).ShouldBeTrue();
        DateRowMatcher.Matches(SearchComparator.Eb, searchValue, range).ShouldBeFalse();
    }

    private static (DateTimeSearchValue Point, DateTimeSearchValue Range) RowsFor(string literal)
    {
        var range = DateTimeSearchValue.Parse(literal);
        var point = new DateTimeSearchValue(range.Start);

        point.End.ShouldBe(point.Start);
        range.End.ShouldBeGreaterThan(range.Start);

        return (point, range);
    }
}
