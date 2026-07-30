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
        options.IncludeLimit.ShouldBeNull();
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
        paging.From.ShouldBeNull();
    }

    [Fact]
    public void GivenAContinuation_WhenReadingItsDefaults_ThenItIsTheFirstPageOfTheValuedSegment()
    {
        var continuation = new SearchContinuation();

        continuation.Phase.ShouldBe(SortPhase.Valued);
        continuation.Boundary.ShouldBeNull();
    }
}
