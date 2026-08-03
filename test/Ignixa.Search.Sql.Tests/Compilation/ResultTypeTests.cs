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

        var result = SearchCompilationResult.Failed(failure);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBeSameAs(failure);
    }

    [Fact]
    public void GivenASearchPlanResultCarryingAFailure_WhenCheckingSucceeded_ThenItIsFalse()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Resolve, "boom", ParameterCode: null, Span: null, Exception: null);

        var result = SearchPlanResult.Failed(failure);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBeSameAs(failure);
    }

    [Fact]
    public void GivenTheResultFactories_WhenPassedNull_ThenTheyRejectItRatherThanMintingAnUninhabitedResult()
    {
        // The factories are the only construction route precisely so "exactly one member is non-null"
        // holds by construction; a null argument would produce a result that is neither a success nor a
        // failure, which is the state MemberNotNullWhen(false, nameof(Failure)) promises cannot exist.
        Should.Throw<ArgumentNullException>(() => SearchPlanResult.Success(null!));
        Should.Throw<ArgumentNullException>(() => SearchPlanResult.Failed(null!));
        Should.Throw<ArgumentNullException>(() => SearchCompilationResult.Success(null!));
        Should.Throw<ArgumentNullException>(() => SearchCompilationResult.Failed(null!));
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
