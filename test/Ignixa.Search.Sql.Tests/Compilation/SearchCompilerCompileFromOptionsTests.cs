using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Covers <see cref="SearchSqlCompiler.TryCreatePlanFromOptionsAsync"/> for <see cref="SearchOptions"/> built
/// directly rather than through <see cref="Ignixa.Search.Parsing.ISearchOptionsBuilder"/> -- the same
/// bypass-the-builder shape <see cref="CompileFromOptionsTests"/> already covers for access constraints,
/// resource types, and surrogate bounds, extended here to the resource-column (<c>_id</c>) and null-
/// resourceType (system-level) cases <see cref="CompilationFixtures.BuildPatientIdAbcOptionsAndResolver"/> and
/// <see cref="CompilationFixtures.BuildSystemLevelStatusFinalOptionsAndResolver"/> were built for.
/// </summary>
public class SearchCompilerCompileFromOptionsTests
{
    [Fact]
    public async Task GivenPatientIdAbc_WhenCompiledFromOptions_ThenTheResourceColumnPredicateCompilesToTheOuterJoin()
    {
        // Arrange -- Patient?_id=abc, built by hand (no ISearchOptionsBuilder in the loop).
        var (options, resolver) = CompilationFixtures.BuildPatientIdAbcOptionsAndResolver();

        // Act
        var result = await new SearchSqlCompiler(resolver).TryCreatePlanFromOptionsAsync(options, "Patient");

        // Assert -- _id is a resource-column parameter: it must lift onto OuterPredicate, not become a CTE
        // that gets intersected in. Ctes.Count == 1 is the base ResourceSource with nothing joined to it.
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        var plan = result.Plan!.Query;
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.OuterPredicate.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
    }

    [Fact]
    public async Task GivenSystemLevelStatusFinal_WhenCompiledFromOptions_ThenItCompilesWithNoResourceTypeAnywhere()
    {
        // Arrange -- ?status=final with no resource type anywhere in the request, built by hand.
        var (options, resolver) = CompilationFixtures.BuildSystemLevelStatusFinalOptionsAndResolver();

        // Act -- resourceType: null all the way through, the genuine system-level-search entry point.
        var result = await new SearchSqlCompiler(resolver).TryCreatePlanFromOptionsAsync(options, resourceType: null);

        // Assert -- the leaf lowers to a ParamSource with no ResourceTypeId of its own, mirroring
        // EndToEndCompilationTests.GivenABareStatusPredicateWithNoResourceType... for the query-string entry
        // point: this proves CreatePlanFromOptionsAsync's own resourceType normalization does not silently
        // invent a type for a request that named none.
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        var plan = result.Plan!.Query;
        var cte = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>();
        cte.ResourceTypeId.ShouldBeNull();
        cte.SearchParamId.ShouldBe((short)202);
    }
}
