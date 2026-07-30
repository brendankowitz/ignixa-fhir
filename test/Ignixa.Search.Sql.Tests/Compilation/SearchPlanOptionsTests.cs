using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchPlanOptionsTests
{
    [Fact]
    public void GivenADefaultSearchPlanOptions_WhenReadingIt_ThenItIsTheLeanNonTracingShape()
    {
        var options = new SearchPlanOptions();

        options.Shape.ShouldBe(ResultShape.Default);
        options.Shape.ShouldBeOfType<ResultShape.Matches>();
        options.Paging.ShouldBeNull();
        options.IncludeLimit.ShouldBe(0);
        options.SurrogateRange.ShouldBeNull();
        options.SearchParameterHash.ShouldBeNull();
        options.OperationExpression.ShouldBeNull();
        options.DiagnosticsLevel.ShouldBe(SearchDiagnosticsLevel.None);
    }

    [Fact]
    public void GivenSearchPlanOptions_WhenCopyingWithAChangedProperty_ThenTheOriginalIsUnchanged()
    {
        var original = new SearchPlanOptions { Paging = new SearchPaging.Keyset(10) };

        var copy = original with { Paging = new SearchPaging.Keyset(20) };

        ((SearchPaging.Keyset)original.Paging!).Top.ShouldBe(10);
        ((SearchPaging.Keyset)copy.Paging!).Top.ShouldBe(20);
    }

    [Fact]
    public void GivenAKeysetPaging_WhenReadingItsDefaults_ThenItIsAnUncappedFirstPageOfTheValuedSegment()
    {
        var paging = new SearchPaging.Keyset();

        paging.Top.ShouldBeNull();
        paging.Boundary.ShouldBeNull();
        paging.Phase.ShouldBe(SortPhase.Valued);
    }

    [Fact]
    public void GivenAnOffsetPaging_WhenReadingItsDefaults_ThenItAlsoStartsInTheValuedSegment()
    {
        // Phase sits on the base rather than on Keyset: it names which segment of a two-phase sort the query
        // reads, which is orthogonal to how that segment is paged.
        var paging = new SearchPaging.Offset(new OffsetSpec(20, 10));

        paging.Phase.ShouldBe(SortPhase.Valued);
        paging.Spec.Offset.ShouldBe(20);
    }

    [Fact]
    public void GivenACountShape_WhenReadingItsDefaults_ThenItCoversTheWholeMatchSet()
    {
        var count = new ResultShape.Count();

        count.RestrictToSortPhase.ShouldBeFalse();
    }
}
