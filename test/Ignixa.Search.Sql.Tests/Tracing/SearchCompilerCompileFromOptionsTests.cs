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

    [Fact]
    public async Task GivenAnEmptyStringResourceType_WhenCompiledFromOptions_ThenItBehavesIdenticallyToNull()
    {
        // Arrange -- ?status=final, a genuine system-level search (no resource type anywhere), compiled
        // once with resourceType: null and once with resourceType: "" -- proving CompileFromOptionsAsync
        // normalizes the empty string to null before it reaches Resolve/Lower, rather than the two
        // diverging on whether this is a system-level search.
        var (nullOptions, nullResolver) = SearchTraceFixtures.BuildSystemLevelStatusFinalOptionsAndResolver();
        var (emptyOptions, emptyResolver) = SearchTraceFixtures.BuildSystemLevelStatusFinalOptionsAndResolver();

        // Act
        var nullTrace = await SearchCompiler.CompileFromOptionsAsync(
            nullOptions,
            null,
            nullResolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            CancellationToken.None);

        var emptyTrace = await SearchCompiler.CompileFromOptionsAsync(
            emptyOptions,
            string.Empty,
            emptyResolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            CancellationToken.None);

        // Assert
        nullTrace.Failure.ShouldBeNull();
        emptyTrace.Failure.ShouldBeNull();
        nullTrace.Sql.ShouldNotBeNull();
        emptyTrace.Sql.ShouldNotBeNull();
        nullTrace.Sql!.Sql.ShouldBe(emptyTrace.Sql!.Sql);
        nullTrace.ResourceType.ShouldBeNull();
        emptyTrace.ResourceType.ShouldBeNull();
    }
}
