using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class OffsetPagingGuardTests
{
    [Fact]
    public void GivenBothOffsetPageAndKeysetPage_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));
        var offsetPage = new OffsetSpec(Offset: 10, Limit: 5);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page,
            offsetPage: offsetPage));
    }

    [Fact]
    public void GivenBothOffsetPageAndTop_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });
        var offsetPage = new OffsetSpec(Offset: 10, Limit: 5);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            top: 10,
            offsetPage: offsetPage));
    }

    [Fact]
    public void GivenPageAndTopTogetherWithNoOffsetPage_WhenLowering_ThenDoesNotThrow()
    {
        // Regression guard for the design doc's own corrected rule: page+top together is keyset
        // paging's own valid, existing call shape (top is keyset's page-size mechanism) and must remain
        // legal -- only offset-vs-page and offset-vs-top are mutually exclusive, not page-vs-top.
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));

        Should.NotThrow(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page,
            top: 10));
    }

    [Fact]
    public void GivenCountPhaseScopedWithoutCountOnly_WhenLowering_ThenThrowsArgumentException()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<ArgumentException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            countOnly: false,
            countPhaseScoped: true));
    }

    [Fact]
    public void GivenCountPhaseScopedWithCountOnlyButEmptySort_WhenLowering_ThenThrowsArgumentException()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<ArgumentException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            countOnly: true,
            countPhaseScoped: true));
    }
}
