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

        options.CountOnly.ShouldBeFalse();
        options.IncludeLimit.ShouldBe(0);
        options.SortPhase.ShouldBe(SortPhase.Valued);
        options.CountPhaseScoped.ShouldBeFalse();
        options.IncludesOnly.ShouldBeFalse();
        options.Top.ShouldBeNull();
        options.Page.ShouldBeNull();
        options.OffsetPage.ShouldBeNull();
        options.SurrogateRange.ShouldBeNull();
        options.SearchParameterHash.ShouldBeNull();
        options.OperationExpression.ShouldBeNull();
        options.DiagnosticsLevel.ShouldBe(SearchDiagnosticsLevel.None);
    }

    [Fact]
    public void GivenSearchPlanOptions_WhenCopyingWithAChangedProperty_ThenTheOriginalIsUnchanged()
    {
        var original = new SearchPlanOptions { Top = 10 };

        var copy = original with { Top = 20 };

        original.Top.ShouldBe(10);
        copy.Top.ShouldBe(20);
    }
}
