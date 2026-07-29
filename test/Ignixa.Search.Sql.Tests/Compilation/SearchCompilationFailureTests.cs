using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
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
    public void GivenAFailureCarryingAnException_WhenWrappingItInAnException_ThenTheOriginalCauseIsChainedAsTheInnerException()
    {
        // A Try* caller that decides to rethrow, and every caller of the throwing entry points, relies on
        // the original exception surviving as InnerException -- it is the only route back to the real
        // stack once the failure has been flattened into a message and a stage.
        var cause = new NotSupportedException("half-open surrogate range");
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower, cause.Message, ParameterCode: null, Span: null, cause);

        var exception = new SearchCompilationException(failure);

        exception.InnerException.ShouldBeSameAs(cause);
    }

    [Fact]
    public void GivenANullFailure_WhenWrappingItInAnException_ThenItIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => new SearchCompilationException(null!));
    }

    [Fact]
    public void GivenAnAttributedLoweringFailure_WhenRecordingIt_ThenTheFailureNamesTheParameterAndItsSpan()
    {
        // Arrange -- an empty symbol table makes the leaf dispatcher throw, and it enriches the exception
        // with the owning parameter and span on the way out. That enrichment reads no diagnostics level,
        // which is what the type's remarks promise: attribution survives at SearchDiagnosticsLevel.None.
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var span = new SourceSpan(SourceOrigin.Value, 0, 4);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = span,
        };
        var context = new LeafContext(new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>()));
        var exception = Should.Throw<KeyNotFoundException>(() => LeafLoweringDispatcher.Lower(predicate, context, 103));

        // Act
        var failure = CompilationDiagnosticsBuilder.RecordFailure([], CompilationStage.Lower, exception);

        // Assert
        failure.Stage.ShouldBe(CompilationStage.Lower);
        failure.ParameterCode.ShouldBe("active");
        failure.Span.ShouldBe(span);
        failure.Exception.ShouldBeSameAs(exception);
        failure.Diagnostics.ShouldBeNull();
    }
}
