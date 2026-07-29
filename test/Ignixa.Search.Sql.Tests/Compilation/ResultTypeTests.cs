using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class ResultTypeTests
{
    [Fact]
    public void GivenASearchCompilationResultCarryingAFailure_WhenCheckingSucceeded_ThenItIsFalse()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Emit, "boom", ParameterCode: null, Span: null, Exception: null);

        var result = new SearchCompilationResult(Compiled: null, failure);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBeSameAs(failure);
    }

    [Fact]
    public void GivenASearchPlanResultCarryingAFailure_WhenCheckingSucceeded_ThenItIsFalse()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Resolve, "boom", ParameterCode: null, Span: null, Exception: null);

        var result = new SearchPlanResult(Plan: null, failure);

        result.Succeeded.ShouldBeFalse();
    }
}
