using Ignixa.Search.Sql.Tracing;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class SearchCompilerCompileFromOptionsTests
{
    [Fact]
    public async Task GivenAnAlreadyBuiltSearchOptions_WhenCompiledFromOptions_ThenTheTraceHasSqlParametersAndCompiledPlan()
    {
        // Arrange -- Patient?_id=abc, built directly as a SearchOptions rather than through
        // SearchOptionsBuilder.Build, proving this entry point genuinely does not need a QueryParameter
        // list or an ISearchOptionsBuilder.
        var (options, resolver) = SearchTraceFixtures.BuildPatientIdAbcOptionsAndResolver();

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Patient",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            CancellationToken.None);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain("@p0");
        trace.Sql.Parameters.ShouldNotBeEmpty();
        // CteRef is a value type (readonly record struct), so ShouldNotBeNull() cannot target Match itself
        // (CS0452) -- Ctes non-empty is the meaningful proxy that Lower actually produced real plan
        // structure, not just a CompiledPlan shell.
        trace.CompiledPlan.ShouldNotBeNull();
        trace.CompiledPlan!.Ctes.ShouldNotBeEmpty();
    }
}
