using Ignixa.Search.Sql.Tracing;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class EmittedSqlTraceParametersTests
{
    [Fact]
    public async Task GivenACompiledSearchWithABoundValue_WhenTraced_ThenSqlTraceCarriesTheParameter()
    {
        // Arrange -- Patient?active=true, a plain leaf lowered straight to a ParamSource CTE with no
        // resource-column extraction ahead of it, so its own bound value is genuinely @p0 (a search
        // wrapped in a resource-column predicate, e.g. Patient?_id=abc, would bind ResourceSource's own
        // ResourceTypeId as @p0 first and push the tested value to @p1 -- see
        // EndToEndCompilationTests' "ResourceSource's own ResourceTypeId consumes @p0" note).
        var trace = await SearchTraceFixtures.TracePatientActiveTrueAsync();

        // Act
        var sqlTrace = trace.Sql;

        // Assert
        sqlTrace.ShouldNotBeNull();
        sqlTrace!.Parameters.ShouldNotBeEmpty();
        sqlTrace.Sql.ShouldContain("@p0");
        sqlTrace.Parameters[0].Name.ShouldBe("@p0");
        sqlTrace.Parameters[0].Value.ShouldBe("true");
    }
}
