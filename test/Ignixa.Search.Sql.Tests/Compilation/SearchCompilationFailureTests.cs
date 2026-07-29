using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchCompilationFailureTests
{
    [Fact]
    public void GivenAFailure_WhenWrappingItInAnException_ThenTheExceptionCarriesItAndRepeatsItsMessage()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower,
            "Chained search requires a single target resource type.",
            ParameterCode: "subject",
            Span: null,
            Exception: null);

        var exception = new SearchCompilationException(failure);

        exception.Failure.ShouldBeSameAs(failure);
        exception.Message.ShouldBe(failure.Message);
    }

    [Fact]
    public void GivenAFailure_WhenNoDiagnosticsWereCaptured_ThenAttributionIsStillPresent()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower, "boom", ParameterCode: "name", Span: null, Exception: null);

        failure.Diagnostics.ShouldBeNull();
        failure.ParameterCode.ShouldBe("name");
    }
}
