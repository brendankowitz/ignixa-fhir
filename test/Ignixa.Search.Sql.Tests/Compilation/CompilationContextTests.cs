using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CompilationContextTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAnEmptyResourceType_WhenCreatingTheContext_ThenItIsNormalizedToNullAndTheSearchIsSystemLevel()
    {
        var context = CompilationContext.Create(new SearchOptions(), string.Empty, new SearchPlanOptions(), ReferenceTime);

        context.TargetResourceType.ShouldBeNull();
        context.SystemLevelSearch.ShouldBeTrue();
    }

    [Fact]
    public void GivenResourceVersionTypesNone_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.None };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenResourceVersionTypesLatest_WhenCreatingTheContext_ThenVisibilityIsNull()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.Latest };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldBeNull();
    }

    [Fact]
    public void GivenResourceVersionTypesLatestAndHistory_WhenCreatingTheContext_ThenTheHistoryColumnIsUnfiltered()
    {
        // Naming both partitions is a request for their union, which is the absence of an IsHistory filter --
        // not IsHistory = 1. The deleted axis is untouched by the history flags and stays pinned to current.
        var searchOptions = new SearchOptions
        {
            ResourceVersionTypes = ResourceVersionTypes.Latest | ResourceVersionTypes.History,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldNotBeNull();
        context.Visibility!.IsHistory.ShouldBeNull();
        context.Visibility.IsDeleted.ShouldBe(false);
    }

    [Fact]
    public void GivenResourceVersionTypesHistoryAlone_WhenCreatingTheContext_ThenTheHistoryColumnIsPinnedToSuperseded()
    {
        // History without Latest is the exclusive shape -- superseded rows only. An earlier relaxation-only
        // mapping could not express it, which is why history-only searches had to be refused upstream.
        var searchOptions = new SearchOptions
        {
            ResourceVersionTypes = ResourceVersionTypes.History,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldNotBeNull();
        context.Visibility!.IsHistory.ShouldBe(true);
        context.Visibility.IsDeleted.ShouldBeNull();
    }

    [Fact]
    public void GivenNullCollectionsOnSearchOptions_WhenCreatingTheContext_ThenTheyBecomeEmptyLists()
    {
        var searchOptions = new SearchOptions
        {
            Include = null,
            RevInclude = null,
            Sort = null,
            AccessConstraints = null,
            ResourceTypes = null,
            AllowedResourceTypes = null,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Includes.ShouldBeEmpty();
        context.RevIncludes.ShouldBeEmpty();
        context.Sort.ShouldBeEmpty();
        context.AccessConstraints.ShouldBeEmpty();
        context.ResourceTypes.ShouldBeEmpty();
        context.AllowedResourceTypes.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAllowedResourceTypes_WhenCreatingTheContext_ThenTheyReachTheCompilationInputs()
    {
        // The allow-list is an authorization input; if it stopped here it would be accepted by the API and
        // never enforced, which fails open rather than closed.
        var searchOptions = new SearchOptions
        {
            AllowedResourceTypes = ["Patient", "Observation"],
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.AllowedResourceTypes.ShouldBe(["Patient", "Observation"]);
    }

    [Fact]
    public void GivenResourceVersionTypesLatestAndSoftDeleted_WhenCreatingTheContext_ThenTheDeletedColumnIsUnfiltered()
    {
        var searchOptions = new SearchOptions
        {
            ResourceVersionTypes = ResourceVersionTypes.Latest | ResourceVersionTypes.SoftDeleted,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldNotBeNull();
        context.Visibility!.IsDeleted.ShouldBeNull();
        context.Visibility.IsHistory.ShouldBe(false);
    }

    [Fact]
    public void GivenNeitherAnExplicitRangeNorSearchOptionsBounds_WhenCreatingTheContext_ThenTheSurrogateRangeIsNull()
    {
        var context = CompilationContext.Create(new SearchOptions(), "Patient", new SearchPlanOptions(), ReferenceTime);

        context.SurrogateRange.ShouldBeNull();
    }

    [Fact]
    public void GivenOnlySearchOptionsBounds_WhenCreatingTheContext_ThenTheSurrogateRangeIsBuiltFromThem()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1, EndSurrogateId = 2 };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.SurrogateRange.ShouldNotBeNull();
        context.SurrogateRange!.Start.Value.ShouldBe(1L);
        context.SurrogateRange.End.Value.ShouldBe(2L);
    }

    [Fact]
    public void GivenOnlyAStartSurrogateId_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1 };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenBothAnExplicitRangeAndSearchOptionsBounds_WhenCreatingTheContext_ThenTheExplicitRangeWins()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1, EndSurrogateId = 2 };
        var options = new SearchPlanOptions { SurrogateRange = (10, 20) };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.SurrogateRange.ShouldNotBeNull();
        context.SurrogateRange!.Start.Value.ShouldBe(10L);
        context.SurrogateRange.End.Value.ShouldBe(20L);
    }

    [Fact]
    public void GivenAnOperationExpression_WhenCreatingTheContext_ThenItReplacesTheSearchExpressionWithoutMutatingSearchOptions()
    {
        var param = new SearchParameterInfo("x", "x", SearchParamType.String, new Uri("http://example.com/x"));
        Expression searchExpression = Expression.MissingSearchParameter(param, isMissing: true);
        var searchOptions = new SearchOptions { Expression = searchExpression };
        Expression operationExpression = Expression.MissingSearchParameter(param, isMissing: false);
        var options = new SearchPlanOptions { OperationExpression = operationExpression };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.Expression.ShouldBeSameAs(operationExpression);
        searchOptions.Expression.ShouldBeSameAs(searchExpression);
    }
}
