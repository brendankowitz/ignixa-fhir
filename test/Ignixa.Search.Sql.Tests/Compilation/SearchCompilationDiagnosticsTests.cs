using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchCompilationDiagnosticsTests
{
    [Fact]
    public void GivenDefaultDiagnostics_WhenReadingThem_ThenTheCollectionsAreEmptyRatherThanNull()
    {
        var diagnostics = new SearchCompilationDiagnostics();

        diagnostics.Parameters.ShouldBeEmpty();
        diagnostics.Implicit.ShouldBeEmpty();
        diagnostics.SqlTextRanges.ShouldBeEmpty();
        diagnostics.Plan.ShouldBeNull();
    }
}
