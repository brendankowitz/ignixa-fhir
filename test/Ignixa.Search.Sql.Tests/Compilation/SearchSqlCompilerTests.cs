using Ignixa.Search.Exceptions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchSqlCompilerTests
{
    [Fact]
    public async Task GivenAQueryString_WhenCreatingAPlanAndCompilingIt_ThenSqlIsEmitted()
    {
        var compiler = CompilerFixtures.ForPatient();

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);
        var compiled = plan.Compile();

        compiled.Sql.ShouldNotBeNullOrWhiteSpace();
        compiled.Query.ShouldBeSameAs(plan.Query);
    }

    [Fact]
    public async Task GivenAnUnresolvableSearchParameter_WhenCreatingAPlan_ThenItThrowsAtTheResolveStage()
    {
        var compiler = CompilerFixtures.WithUnresolvableParameters();

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));

        exception.Failure.Stage.ShouldBe(CompilationStage.Resolve);
    }

    [Fact]
    public async Task GivenAnUnresolvableSearchParameter_WhenTryingToCreateAPlan_ThenItReturnsAFailure()
    {
        var compiler = CompilerFixtures.WithUnresolvableParameters();

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Resolve);
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingAnOperationExpression_WhenCreatingAPlan_ThenTheCallersOptionsAreNotMutated()
    {
        var compiler = CompilerFixtures.ForPatient();
        var searchOptions = new SearchOptions();
        var operationExpression = PlanFixtures.EverythingExpression();

        await compiler.CreatePlanFromOptionsAsync(
            searchOptions, "Patient", new SearchPlanOptions { OperationExpression = operationExpression });

        searchOptions.Expression.ShouldBeNull();
    }

    [Fact]
    public async Task GivenSearchOptionsWithAHalfOpenSurrogateRange_WhenTryingToCreateAPlan_ThenItReturnsAFailureInsteadOfThrowing()
    {
        var compiler = CompilerFixtures.ForPatient();
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = PlanFixtures.EverythingExpression(),
            StartSurrogateId = 1,
        };

        var result = await compiler.TryCreatePlanFromOptionsAsync(searchOptions, "Patient");

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Exception.ShouldBeOfType<NotSupportedException>();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelNone_WhenCreatingAPlan_ThenNoDiagnosticsAreAttached()
    {
        var compiler = CompilerFixtures.ForPatient();

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        plan.Diagnostics.ShouldBeNull();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelParameters_WhenCreatingAPlan_ThenPerParameterOutcomesAreAttached()
    {
        var compiler = CompilerFixtures.ForPatient();
        var options = new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Parameters };

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")], options);

        plan.Diagnostics!.Parameters.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenNoOptionsBuilder_WhenCreatingAPlanFromAQueryString_ThenItThrowsNamingTheMissingDependency()
    {
        var compiler = new SearchSqlCompiler(CompilerFixtures.PatientResolver());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));

        exception.Message.ShouldContain(nameof(ISearchOptionsBuilder));
    }

    [Fact]
    public async Task GivenAFhirExceptionFromTheOptionsBuilder_WhenCreatingAPlan_ThenItPropagatesUnwrapped()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();

        await Should.ThrowAsync<BadSearchRequestException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));
    }

    [Fact]
    public async Task GivenAFhirExceptionFromTheOptionsBuilder_WhenTryingToCreateAPlan_ThenItIsCapturedAtTheBuildStage()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Build);
        result.Failure.Exception.ShouldBeOfType<BadSearchRequestException>();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelParameters_WhenABuildFailureIsCaptured_ThenTheFailureStillCarriesDiagnostics()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();
        var options = new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Parameters };

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")], options);

        result.Failure!.Diagnostics.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelNone_WhenABuildFailureIsCaptured_ThenNoDiagnosticsAreAttached()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        result.Failure!.Diagnostics.ShouldBeNull();
    }

    [Fact]
    public void GivenANullResolver_WhenConstructingTheCompiler_ThenItIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => new SearchSqlCompiler(null!));
    }

    [Fact]
    public async Task GivenNullParameters_WhenCreatingAPlan_ThenItIsRejected()
    {
        var compiler = CompilerFixtures.ForPatient();

        await Should.ThrowAsync<ArgumentNullException>(() => compiler.CreatePlanAsync("Patient", null!));
    }

    [Fact]
    public async Task GivenNullSearchOptions_WhenCreatingAPlanFromOptions_ThenItIsRejected()
    {
        var compiler = CompilerFixtures.ForPatient();

        await Should.ThrowAsync<ArgumentNullException>(() => compiler.CreatePlanFromOptionsAsync(null!, "Patient"));
    }

    [Fact]
    public async Task GivenSearchOptionsWithAHalfOpenSurrogateRange_WhenCreatingAPlanFromOptions_ThenItThrows()
    {
        // The Try sibling is covered above; this pins the throwing entry point's own delegation, which is
        // the only thing standing between a caller and a silently swallowed options-mapping error.
        var compiler = CompilerFixtures.ForPatient();
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = PlanFixtures.EverythingExpression(),
            StartSurrogateId = 1,
        };

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(searchOptions, "Patient"));

        exception.Failure.Stage.ShouldBe(CompilationStage.Lower);
    }
}
