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
        result.Failure.ShouldBeSameAs(failure);
    }

    [Fact]
    public void GivenAFailureCarryingAnException_WhenRenderingItToString_ThenItNamesTheExceptionTypeWithoutAStackTrace()
    {
        // Throw and catch so the exception has a populated StackTrace -- the generated record ToString would
        // inline that whole trace, which is exactly what the PrintMembers override exists to suppress.
        Exception caught;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        var failure = new SearchCompilationFailure(
            CompilationStage.Lower, "boom", ParameterCode: "name", Span: null, caught);

        var rendered = failure.ToString();

        rendered.ShouldContain(nameof(InvalidOperationException));
        rendered.ShouldNotContain("   at ");
    }
}
