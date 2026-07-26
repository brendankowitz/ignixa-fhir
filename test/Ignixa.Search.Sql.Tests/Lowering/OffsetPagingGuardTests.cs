using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class OffsetPagingGuardTests
{
    private static SymbolTable PatientSymbols() =>
        new(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });

    private static LoweredPlan RunWith(SymbolTable symbols, PageSpec? page, LowerOptions options) =>
        Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page,
            options);

    [Fact]
    public void GivenBothOffsetPageAndKeysetPage_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = PatientSymbols();
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));
        var options = new LowerOptions { OffsetPage = new OffsetSpec(Offset: 10, Limit: 5) };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => RunWith(symbols, page, options));
    }

    [Fact]
    public void GivenBothOffsetPageAndTop_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = PatientSymbols();
        var options = new LowerOptions { OffsetPage = new OffsetSpec(Offset: 10, Limit: 5), Top = 10 };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => RunWith(symbols, page: null, options));
    }

    [Fact]
    public void GivenPageAndTopTogetherWithNoOffsetPage_WhenLowering_ThenDoesNotThrow()
    {
        // Regression guard for the design doc's own corrected rule: page+top together is keyset
        // paging's own valid, existing call shape (top is keyset's page-size mechanism) and must remain
        // legal -- only offset-vs-page and offset-vs-top are mutually exclusive, not page-vs-top.
        var symbols = PatientSymbols();
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));
        var options = new LowerOptions { Top = 10 };

        Should.NotThrow(() => RunWith(symbols, page, options));
    }

    [Fact]
    public void GivenCountPhaseScopedWithoutCountOnly_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = PatientSymbols();
        var options = new LowerOptions { CountOnly = false, CountPhaseScoped = true };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => RunWith(symbols, page: null, options));
    }

    [Fact]
    public void GivenCountPhaseScopedWithCountOnlyButEmptySort_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = PatientSymbols();
        var options = new LowerOptions { CountOnly = true, CountPhaseScoped = true };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => RunWith(symbols, page: null, options));
    }
}
